using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace X360GameHack.Core.Formats
{
    /// <summary>
    /// XDBF (Xbox Data Base File) container - the format of a profile's GPD files. Big-endian.
    ///   header 0x18: magic "XDBF", version, entry table length, entry count, free table length, free count
    ///   entry table: length x 0x12 { namespace u16, id u64, offset u32, length u32 }, sorted by (namespace, id)
    ///   free table : length x 0x08 { offset u32, length u32 }, sorted by offset; its last record marks the end of the
    ///                data (offset = end, length = 0xFFFFFFFF - end)
    ///   data       : starts after both tables; every byte up to the end marker belongs to an entry or a free record
    /// Each namespace also keeps the console's upload bookkeeping as two ordinary entries: a sync list (id 0x100000000,
    /// 16-byte records { entry id, sync id }) and sync data (id 0x200000000: next sync id, last synced id, time).
    /// The layout rules here were checked against all 69 GPDs of two console-written profiles: tables are always in
    /// order, entries and free records never overlap, neighbouring free records are always merged, and unused table
    /// slots are zero. A file that breaks those rules can still be read but is never edited.
    /// </summary>
    public sealed class XdbfFile
    {
        public const uint Magic = 0x58444246;   // "XDBF"
        public const ulong SyncListId = 0x100000000UL, SyncDataId = 0x200000000UL;
        /// <summary>The avatar award namespace (the GPDs inside a profile's PEC) keeps its sync list / sync data under ids 1 and 2.</summary>
        public const ulong AwardSyncListId = 1, AwardSyncDataId = 2;

        public static ulong SyncListIdOf(Ns ns) => ns == Ns.AvatarAward ? AwardSyncListId : SyncListId;
        public static ulong SyncDataIdOf(Ns ns) => ns == Ns.AvatarAward ? AwardSyncDataId : SyncDataId;
        private const int HeaderSize = 0x18, EntryRecord = 0x12, FreeRecord = 8;
        private const long MaxFileSize = 64L * 1024 * 1024;

        public enum Ns : ushort { Achievement = 1, Image = 2, Setting = 3, Title = 4, String = 5, AvatarAward = 6 }

        public sealed class Entry
        {
            public ushort Namespace;
            public ulong Id;
            /// <summary>The entry's bytes. Change them in place, or assign a new array to resize the entry.</summary>
            public byte[] Data;
            internal uint Offset;         // where the stored bytes start, relative to the data area
            internal uint StoredLength;   // how many bytes the file reserves there
            /// <summary>True for the console's sync list / sync data bookkeeping entries (not a real record).</summary>
            public bool IsSync => Namespace == (ushort)Ns.AvatarAward ? Id == AwardSyncListId || Id == AwardSyncDataId : Id == SyncListId || Id == SyncDataId;
        }

        public struct SyncRecord
        {
            public ulong EntryId, SyncId;
        }

        private struct Hole
        {
            public uint Offset, Length;
        }

        private byte[] _file;                      // the file image; grows when data is appended
        private uint _entryTableLength, _freeTableLength, _end;
        private long _dataStart;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Hole> _holes = new List<Hole>();
        private bool _tablesChanged;

        private XdbfFile() { }

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>Why this file must not be edited (it does not follow the layout rules), or null when editing is safe.</summary>
        public string EditBlocker { get; private set; }

        // ------------------------------------------------------------------------------------------------ read

        public static XdbfFile Parse(byte[] b)
        {
            if (b == null || b.Length < HeaderSize) throw new InvalidDataException("GPD file is too small.");
            if (BE.U32(b, 0) != Magic) throw new InvalidDataException("Not an XDBF/GPD file (bad magic).");

            var f = new XdbfFile { _file = (byte[])b.Clone(), _entryTableLength = BE.U32(b, 0x08), _freeTableLength = BE.U32(b, 0x10) };
            uint entryCount = BE.U32(b, 0x0C), freeCount = BE.U32(b, 0x14);
            f._dataStart = HeaderSize + (long)f._entryTableLength * EntryRecord + (long)f._freeTableLength * FreeRecord;
            if (f._dataStart > b.Length) throw new InvalidDataException("GPD header describes tables larger than the file.");
            long dataLength = b.Length - f._dataStart;

            string blocker = null;
            if (entryCount > f._entryTableLength) { blocker = "its entry count is larger than its entry table"; entryCount = f._entryTableLength; }
            if (freeCount > f._freeTableLength) { blocker = blocker ?? "its free count is larger than its free table"; freeCount = f._freeTableLength; }

            Entry previous = null;
            for (uint i = 0; i < entryCount; i++)
            {
                int p = HeaderSize + (int)i * EntryRecord;
                uint offset = BE.U32(b, p + 0x0A), length = BE.U32(b, p + 0x0E);
                if ((long)offset + length > dataLength) { blocker = blocker ?? "an entry points outside the file"; continue; }
                var e = new Entry { Namespace = BE.U16(b, p), Id = BE.U64(b, p + 2), Data = new byte[length], Offset = offset, StoredLength = length };
                Buffer.BlockCopy(b, (int)(f._dataStart + offset), e.Data, 0, (int)length);
                if (previous != null && Compare(previous.Namespace, previous.Id, e.Namespace, e.Id) >= 0) blocker = blocker ?? "its entry table is not in order";
                previous = e;
                f._entries.Add(e);
            }

            int freeBase = HeaderSize + (int)f._entryTableLength * EntryRecord;
            bool hasEnd = false;
            for (uint i = 0; i < freeCount; i++)
            {
                int p = freeBase + (int)i * FreeRecord;
                uint offset = BE.U32(b, p), length = BE.U32(b, p + 4);
                if (i == freeCount - 1 && (ulong)offset + length == 0xFFFFFFFFUL) { f._end = offset; hasEnd = true; }
                else if (length > 0) f._holes.Add(new Hole { Offset = offset, Length = length });
            }
            if (!hasEnd)
            {
                blocker = blocker ?? "it has no end-of-data record";
                f._end = (uint)Math.Min(dataLength, uint.MaxValue);
            }
            else if (f._end > dataLength) blocker = blocker ?? "its data area is cut short";

            f.EditBlocker = blocker ?? f.LayoutProblem();
            return f;
        }

        /// <summary>An empty GPD the size a console allocates (512 entry slots, 512 free-space slots, version 0x00010000).</summary>
        public static XdbfFile Create()
        {
            var f = new XdbfFile { _entryTableLength = 512, _freeTableLength = 512 };
            f._dataStart = HeaderSize + (long)f._entryTableLength * EntryRecord + (long)f._freeTableLength * FreeRecord;
            f._file = new byte[f._dataStart];
            BE.W32(f._file, 0, Magic);
            BE.W32(f._file, 4, 0x00010000);
            BE.W32(f._file, 0x08, f._entryTableLength);
            BE.W32(f._file, 0x10, f._freeTableLength);
            f._tablesChanged = true;
            return f;
        }

        /// <summary>An empty GPD with the same version and table sizes as <paramref name="like"/> (what a console allocates).</summary>
        public static XdbfFile Create(XdbfFile like)
        {
            if (like == null) throw new ArgumentNullException(nameof(like));
            var f = new XdbfFile { _entryTableLength = like._entryTableLength, _freeTableLength = like._freeTableLength };
            if (f._entryTableLength == 0 || f._freeTableLength == 0) throw new InvalidDataException("The source GPD has no tables to model a new one on.");
            f._dataStart = HeaderSize + (long)f._entryTableLength * EntryRecord + (long)f._freeTableLength * FreeRecord;
            f._file = new byte[f._dataStart];
            BE.W32(f._file, 0, Magic);
            Buffer.BlockCopy(like._file, 4, f._file, 4, 4);   // version
            BE.W32(f._file, 0x08, f._entryTableLength);
            BE.W32(f._file, 0x10, f._freeTableLength);
            f._tablesChanged = true;
            return f;
        }

        /// <summary>
        /// Writes a namespace's sync bookkeeping the way a console writes it for records nothing has happened to yet:
        /// the records get sync ids 1..N in id order, the list holds them newest first, and the next id is N + 1.
        /// (Seen in every untouched game GPD and award GPD of the console-written profiles examined.)
        /// </summary>
        public void ResetSync(Ns ns)
        {
            RequireEditable();
            var ids = Records(ns).Select(e => e.Id).OrderBy(i => i).ToList();
            var records = new List<SyncRecord>();
            for (int i = ids.Count - 1; i >= 0; i--) records.Add(new SyncRecord { EntryId = ids[i], SyncId = (ulong)(i + 1) });
            // a namespace without records has only its sync data - the console adds the list with the first record
            var list = Find(ns, SyncListIdOf(ns)) ?? (ids.Count > 0 ? Add(ns, SyncListIdOf(ns), new byte[0]) : null);
            if (list != null) WriteSyncList(list, records);
            var data = Find(ns, SyncDataIdOf(ns)) ?? Add(ns, SyncDataIdOf(ns), NewSyncData());
            data.Data = new byte[0x18];
            BE.W64(data.Data, 0, (ulong)ids.Count + 1);
        }

        /// <summary>Entries and free records must each lie before the end marker and never overlap.</summary>
        private string LayoutProblem()
        {
            var regions = new List<KeyValuePair<long, long>>();
            foreach (var e in _entries)
                if (e.StoredLength > 0) regions.Add(new KeyValuePair<long, long>(e.Offset, (long)e.Offset + e.StoredLength));
            long last = -1;
            foreach (var h in _holes)
            {
                if (h.Offset < last) return "its free table is not in order";
                last = h.Offset;
                regions.Add(new KeyValuePair<long, long>(h.Offset, (long)h.Offset + h.Length));
            }
            regions.Sort((x, y) => x.Key.CompareTo(y.Key));
            long position = 0;
            foreach (var r in regions)
            {
                if (r.Key < position) return "two of its records overlap";
                position = r.Value;
            }
            return position > _end ? "a record lies past its end-of-data record" : null;
        }

        private static int Compare(ushort nsA, ulong idA, ushort nsB, ulong idB)
        {
            int c = nsA.CompareTo(nsB);
            return c != 0 ? c : idA.CompareTo(idB);
        }

        public IEnumerable<Entry> OfNamespace(Ns ns) => _entries.Where(e => e.Namespace == (ushort)ns);

        /// <summary>The namespace's own records, without the sync list / sync data bookkeeping entries.</summary>
        public IEnumerable<Entry> Records(Ns ns) => _entries.Where(e => e.Namespace == (ushort)ns && !e.IsSync);

        public Entry Find(Ns ns, ulong id) => _entries.FirstOrDefault(e => e.Namespace == (ushort)ns && e.Id == id);

        public IEnumerable<GpdTitle> Titles()
        {
            foreach (var e in Records(Ns.Title))
                if (GpdTitle.TryParse(e, out var t)) yield return t;
        }

        public IEnumerable<GpdAchievement> Achievements()
        {
            foreach (var e in Records(Ns.Achievement))
                if (GpdAchievement.TryParse(e, out var a)) yield return a;
        }

        public IEnumerable<GpdAvatarAward> AvatarAwards()
        {
            foreach (var e in Records(Ns.AvatarAward))
                if (GpdAvatarAward.TryParse(e, out var a)) yield return a;
        }

        public IEnumerable<GpdSetting> Settings()
        {
            foreach (var e in Records(Ns.Setting))
                if (GpdSetting.TryParse(e, out var s)) yield return s;
        }

        /// <summary>A 32-bit number setting, or null when the setting does not exist (or is not a number).</summary>
        public int? GetInt32Setting(uint id)
        {
            var e = Find(Ns.Setting, id);
            if (e == null || e.Data.Length < 0x18 || e.Data[8] != GpdSetting.TypeInt32) return null;
            return (int)BE.U32(e.Data, 0x10);
        }

        /// <summary>
        /// A text setting (type 4), or null when it does not exist. Record: { id, 4 bytes, type, 7 bytes, byte length
        /// including the terminator, 4 bytes, UTF-16 big-endian text + terminator } - as read from console-written profiles.
        /// </summary>
        public string GetStringSetting(uint id)
        {
            var e = Find(Ns.Setting, id);
            if (e == null || e.Data.Length < 0x18 || e.Data[8] != GpdSetting.TypeString) return null;
            int length = (int)Math.Min(BE.U32(e.Data, 0x10), (uint)(e.Data.Length - 0x18));
            return ReadWStr(e.Data.Take(0x18 + length).ToArray(), 0x18, out _);
        }

        /// <summary>The longest text a setting id allows: the id carries its maximum size in bytes (bits 16-27), terminator included.</summary>
        public static int MaxStringLength(uint id) => Math.Max(0, (int)(id >> 16 & 0xFFF) / 2 - 1);

        /// <summary>
        /// Writes a text setting and marks it changed (its sync record moves to the end with the next id, as for any
        /// record the console changes). An existing record keeps its other bytes; a new one is created zero-filled.
        /// </summary>
        public void SetStringSetting(uint id, string value)
        {
            RequireEditable();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if ((id >> 28) != 4) throw new ArgumentException("Setting 0x" + id.ToString("X8") + " is not a text setting.");
            if (value.Length > MaxStringLength(id)) throw new ArgumentException("That text is too long: this setting holds at most " + MaxStringLength(id) + " characters.");
            if (value.IndexOf('\0') >= 0) throw new ArgumentException("The text contains a character that cannot be stored.");
            byte[] text = Encoding.BigEndianUnicode.GetBytes(value + "\0");
            var e = Find(Ns.Setting, id);
            var d = new byte[0x18 + text.Length];
            if (e != null)
            {
                if (e.Data.Length < 0x18 || e.Data[8] != GpdSetting.TypeString) throw new InvalidDataException("Setting 0x" + id.ToString("X8") + " is not text in this GPD.");
                Buffer.BlockCopy(e.Data, 0, d, 0, 0x18);
            }
            else
            {
                BE.W32(d, 0, id);
                d[8] = GpdSetting.TypeString;
            }
            BE.W32(d, 0x10, (uint)text.Length);
            Buffer.BlockCopy(text, 0, d, 0x18, text.Length);
            if (e != null) e.Data = d; else Add(Ns.Setting, id, d);
            MarkChanged(Ns.Setting, id);
        }

        /// <summary>A floating-point setting (type 5), or null when it does not exist.</summary>
        public float? GetFloatSetting(uint id)
        {
            var e = Find(Ns.Setting, id);
            if (e == null || e.Data.Length < 0x18 || e.Data[8] != GpdSetting.TypeFloat) return null;
            return BitConverter.ToSingle(new[] { e.Data[0x13], e.Data[0x12], e.Data[0x11], e.Data[0x10] }, 0);
        }

        /// <summary>Writes an existing floating-point setting and marks it changed.</summary>
        public void SetFloatSetting(uint id, float value)
        {
            RequireEditable();
            var e = Find(Ns.Setting, id);
            if (e == null || e.Data.Length < 0x18 || e.Data[8] != GpdSetting.TypeFloat) throw new InvalidDataException("Setting 0x" + id.ToString("X8") + " is not a number with a fraction in this GPD.");
            byte[] b = BitConverter.GetBytes(value);
            e.Data[0x10] = b[3]; e.Data[0x11] = b[2]; e.Data[0x12] = b[1]; e.Data[0x13] = b[0];
            MarkChanged(Ns.Setting, id);
        }

        public List<SyncRecord> SyncList(Ns ns)
        {
            var list = new List<SyncRecord>();
            var e = Find(ns, SyncListIdOf(ns));
            if (e == null) return list;
            for (int o = 0; o + 0x10 <= e.Data.Length; o += 0x10)
                list.Add(new SyncRecord { EntryId = BE.U64(e.Data, o), SyncId = BE.U64(e.Data, o + 8) });
            return list;
        }

        // ------------------------------------------------------------------------------------------------ edit

        private void RequireEditable()
        {
            if (EditBlocker != null) throw new InvalidDataException("This GPD cannot be edited safely: " + EditBlocker + ".");
        }

        /// <summary>Adds a new entry at its place in the (sorted) entry table.</summary>
        public Entry Add(Ns ns, ulong id, byte[] data)
        {
            RequireEditable();
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (_entries.Count >= _entryTableLength) throw new InvalidOperationException("The GPD's entry table is full.");
            int i = 0;
            while (i < _entries.Count && Compare(_entries[i].Namespace, _entries[i].Id, (ushort)ns, id) < 0) i++;
            if (i < _entries.Count && Compare(_entries[i].Namespace, _entries[i].Id, (ushort)ns, id) == 0)
                throw new InvalidOperationException("The GPD already has that entry.");
            var e = new Entry { Namespace = (ushort)ns, Id = id, Data = data };
            _entries.Insert(i, e);
            _tablesChanged = true;
            return e;
        }

        /// <summary>
        /// Writes a 32-bit number setting, creating it when missing. A new setting is created the way a console creates
        /// its counters: 0x18 bytes { id, 0, type 1, 0, value, 0 } and a sync record with id 0 (nothing to upload).
        /// </summary>
        public void SetInt32Setting(uint id, int value)
        {
            RequireEditable();
            var e = Find(Ns.Setting, id);
            if (e == null)
            {
                var d = new byte[0x18];
                BE.W32(d, 0, id);
                d[8] = GpdSetting.TypeInt32;
                BE.W32(d, 0x10, (uint)value);
                Add(Ns.Setting, id, d);
                TrackUnsynced(Ns.Setting, id);
                return;
            }
            if (e.Data.Length < 0x18 || e.Data[8] != GpdSetting.TypeInt32)
                throw new InvalidDataException("Setting 0x" + id.ToString("X8") + " is not a 32-bit number in this GPD.");
            BE.W32(e.Data, 0x10, (uint)value);
        }

        /// <summary>
        /// Marks a record as changed the way a console does: its sync record moves to the end of the namespace's sync
        /// list with the next sync id, and the next id advances. (Seen on real offline unlocks: the changed
        /// achievement and its title each sit last in their list with the newest id.)
        /// </summary>
        public void MarkChanged(Ns ns, ulong entryId)
        {
            RequireEditable();
            var data = Find(ns, SyncDataIdOf(ns)) ?? Add(ns, SyncDataIdOf(ns), NewSyncData());
            if (data.Data.Length < 0x18) throw new InvalidDataException("This GPD's sync data has an unexpected size.");
            var records = EditableSyncList(ns, out var list);
            ulong next = Math.Max(1, BE.U64(data.Data, 0));
            foreach (var r in records) if (r.SyncId >= next) next = r.SyncId + 1;   // never reuse an id that is already out there
            records.RemoveAll(r => r.EntryId == entryId);
            records.Add(new SyncRecord { EntryId = entryId, SyncId = next });
            WriteSyncList(list, records);
            BE.W64(data.Data, 0, next + 1);
        }

        /// <summary>Lists a new record that has nothing to upload: appended with sync id 0, as a console lists its counters.</summary>
        public void TrackUnsynced(Ns ns, ulong entryId)
        {
            RequireEditable();
            if (Find(ns, SyncDataIdOf(ns)) == null) Add(ns, SyncDataIdOf(ns), NewSyncData());
            var records = EditableSyncList(ns, out var list);
            if (records.Any(r => r.EntryId == entryId)) return;
            records.Add(new SyncRecord { EntryId = entryId, SyncId = 0 });
            WriteSyncList(list, records);
        }

        private List<SyncRecord> EditableSyncList(Ns ns, out Entry list)
        {
            list = Find(ns, SyncListIdOf(ns)) ?? Add(ns, SyncListIdOf(ns), new byte[0]);
            if (list.Data.Length % 0x10 != 0) throw new InvalidDataException("This GPD's sync list has an unexpected size.");
            return SyncList(ns);
        }

        private static void WriteSyncList(Entry list, List<SyncRecord> records)
        {
            var d = new byte[records.Count * 0x10];
            for (int i = 0; i < records.Count; i++)
            {
                BE.W64(d, i * 0x10, records[i].EntryId);
                BE.W64(d, i * 0x10 + 8, records[i].SyncId);
            }
            list.Data = d;
        }

        private static byte[] NewSyncData()
        {
            var d = new byte[0x18];   // next sync id 1, last synced 0, never synced
            BE.W64(d, 0, 1);
            return d;
        }

        // ------------------------------------------------------------------------------------------------ write

        /// <summary>
        /// The file with every change applied. Entries that kept their size are written where they were; resized and
        /// new entries get space from the free records (smallest that fits) or at the end of the data. An untouched
        /// file comes back byte for byte.
        /// </summary>
        public byte[] ToBytes()
        {
            foreach (var e in _entries)
                if (e.Data == null) throw new InvalidOperationException("A GPD entry has no data.");

            if (_tablesChanged || _entries.Any(e => e.Data.Length != e.StoredLength))
            {
                RequireEditable();
                foreach (var e in _entries)
                {
                    if (e.Data.Length == e.StoredLength) continue;
                    if (e.StoredLength > 0) Free(e.Offset, e.StoredLength);
                    e.StoredLength = 0;
                    if (e.Data.Length > 0) e.Offset = Allocate((uint)e.Data.Length);
                    e.StoredLength = (uint)e.Data.Length;
                }
                WriteTables();
                _tablesChanged = false;
            }

            foreach (var e in _entries)
                if (e.Data.Length > 0) Buffer.BlockCopy(e.Data, 0, _file, (int)(_dataStart + e.Offset), e.Data.Length);
            return (byte[])_file.Clone();
        }

        private uint Allocate(uint length)
        {
            int best = -1;
            for (int i = 0; i < _holes.Count; i++)
                if (_holes[i].Length >= length && (best < 0 || _holes[i].Length < _holes[best].Length)) best = i;
            if (best >= 0)
            {
                var h = _holes[best];
                if (h.Length == length) _holes.RemoveAt(best);
                else _holes[best] = new Hole { Offset = h.Offset + length, Length = h.Length - length };
                return h.Offset;
            }
            if ((ulong)_end + length > 0xFFFFFFFEUL || _dataStart + _end + length > MaxFileSize)
                throw new InvalidOperationException("The GPD would grow too large.");
            uint offset = _end;
            _end += length;
            return offset;
        }

        private void Free(uint offset, uint length)
        {
            int i = 0;
            while (i < _holes.Count && _holes[i].Offset < offset) i++;
            _holes.Insert(i, new Hole { Offset = offset, Length = length });
            if (i + 1 < _holes.Count && _holes[i].Offset + _holes[i].Length == _holes[i + 1].Offset)
            {
                _holes[i] = new Hole { Offset = _holes[i].Offset, Length = _holes[i].Length + _holes[i + 1].Length };
                _holes.RemoveAt(i + 1);
            }
            if (i > 0 && _holes[i - 1].Offset + _holes[i - 1].Length == _holes[i].Offset)
            {
                _holes[i - 1] = new Hole { Offset = _holes[i - 1].Offset, Length = _holes[i - 1].Length + _holes[i].Length };
                _holes.RemoveAt(i);
                i--;
            }
            if (_holes[i].Offset + _holes[i].Length == _end)   // free space that reaches the end just moves the end back
            {
                _end = _holes[i].Offset;
                _holes.RemoveAt(i);
            }
        }

        private void WriteTables()
        {
            if (_entries.Count > _entryTableLength) throw new InvalidOperationException("The GPD's entry table is full.");
            if (_freeTableLength == 0) throw new InvalidOperationException("The GPD has no free table.");
            while (_holes.Count + 1 > _freeTableLength)   // table full: let go of the smallest gap (it simply stays unused)
            {
                int smallest = 0;
                for (int i = 1; i < _holes.Count; i++)
                    if (_holes[i].Length < _holes[smallest].Length) smallest = i;
                _holes.RemoveAt(smallest);
            }

            long need = _dataStart + _end;
            if (need > _file.Length) Array.Resize(ref _file, (int)need);

            BE.W32(_file, 0x0C, (uint)_entries.Count);
            BE.W32(_file, 0x14, (uint)_holes.Count + 1);
            int p = HeaderSize;
            foreach (var e in _entries)
            {
                BE.W16(_file, p, e.Namespace);
                BE.W64(_file, p + 2, e.Id);
                BE.W32(_file, p + 0x0A, e.Offset);
                BE.W32(_file, p + 0x0E, e.StoredLength);
                p += EntryRecord;
            }
            int freeBase = HeaderSize + (int)_entryTableLength * EntryRecord;
            Array.Clear(_file, p, freeBase - p);
            p = freeBase;
            foreach (var h in _holes)
            {
                BE.W32(_file, p, h.Offset);
                BE.W32(_file, p + 4, h.Length);
                p += FreeRecord;
            }
            BE.W32(_file, p, _end);
            BE.W32(_file, p + 4, 0xFFFFFFFF - _end);
            p += FreeRecord;
            Array.Clear(_file, p, (int)_dataStart - p);
        }

        // ------------------------------------------------------------------------------------------------ helpers

        /// <summary>Null-terminated UTF-16 big-endian string; advances <paramref name="next"/> past the terminator.</summary>
        internal static string ReadWStr(byte[] b, int off, out int next)
        {
            int p = off;
            while (p + 1 < b.Length && (b[p] != 0 || b[p + 1] != 0)) p += 2;
            string s = p > off ? Encoding.BigEndianUnicode.GetString(b, off, p - off) : "";
            next = Math.Min(b.Length, p + 2);
            return s;
        }

        /// <summary>Xbox stores times as a Windows FILETIME (100 ns ticks since 1601). Null when out of range.</summary>
        internal static DateTime? FileTime(ulong ft)
        {
            if (ft == 0 || ft > 0x7FFFFFFFFFFFFFFFUL) return null;
            try { return DateTime.FromFileTimeUtc((long)ft).ToLocalTime(); }
            catch { return null; }
        }
    }

    /// <summary>A game's record in the dashboard GPD (namespace 4): the achievement / gamerscore totals for that game.</summary>
    public sealed class GpdTitle
    {
        public const uint FlagNeedsSync = 0x1, FlagDownloadAchievementImage = 0x2, FlagAvatarAwardSync = 0x10;
        /// <summary>Avatar awards of the game: earned / existing, in total and per avatar gender (bytes 0x16..0x1B of the record).</summary>
        public byte AwardsEarned, AwardCount, MaleAwardsEarned, MaleAwardCount, FemaleAwardsEarned, FemaleAwardCount;

        public uint TitleId;
        public uint AchievementCount;
        public uint AchievementsUnlocked;
        public uint GamerscoreTotal;
        public uint GamerscoreUnlocked;
        public ushort AchievementsUnlockedOnline;
        public uint Flags;
        public DateTime? LastPlayed;
        public string Name = "";

        public string TitleIdHex => TitleId.ToString("X8");

        public static bool TryParse(XdbfFile.Entry e, out GpdTitle t)
        {
            t = null;
            var b = e.Data;
            if (b == null || b.Length < 0x28 || e.IsSync) return false;
            t = new GpdTitle
            {
                TitleId = BE.U32(b, 0x00),
                AchievementCount = BE.U32(b, 0x04),
                AchievementsUnlocked = BE.U32(b, 0x08),
                GamerscoreTotal = BE.U32(b, 0x0C),
                GamerscoreUnlocked = BE.U32(b, 0x10),
                AchievementsUnlockedOnline = BE.U16(b, 0x14),
                AwardsEarned = b[0x16], AwardCount = b[0x17], MaleAwardsEarned = b[0x18], MaleAwardCount = b[0x19], FemaleAwardsEarned = b[0x1A], FemaleAwardCount = b[0x1B],
                Flags = BE.U32(b, 0x1C),
                LastPlayed = XdbfFile.FileTime(BE.U64(b, 0x20)),
                Name = b.Length > 0x28 ? XdbfFile.ReadWStr(b, 0x28, out _) : ""
            };
            return true;
        }

        /// <summary>Writes the unlocked totals, the unlocked-online count and extra flag bits into a raw title record.</summary>
        public static void SetTotals(byte[] data, uint achievementsUnlocked, uint gamerscoreUnlocked, ushort unlockedOnline, uint addFlags)
        {
            if (data == null || data.Length < 0x28) throw new InvalidDataException("Title record is too small.");
            BE.W32(data, 0x08, achievementsUnlocked);
            BE.W32(data, 0x10, gamerscoreUnlocked);
            BE.W16(data, 0x14, unlockedOnline);
            BE.W32(data, 0x1C, BE.U32(data, 0x1C) | addFlags);
        }
    }

    /// <summary>A single achievement in a per-title GPD (namespace 1).</summary>
    public sealed class GpdAchievement
    {
        /// <summary>Earned on Xbox LIVE / earned / the platform it was earned on (0x100000 = Xbox 360, always set by a console).</summary>
        public const uint FlagUnlockedOnline = 0x10000, FlagUnlocked = 0x20000, PlatformMask = 0x700000, PlatformXbox360 = 0x100000;

        public ulong EntryId;
        public uint Id;
        public uint ImageId;
        public uint Gamerscore;
        public uint Flags;
        public DateTime? UnlockTime;
        public string Name = "";
        public string UnlockedDescription = "";
        public string LockedDescription = "";

        public bool Unlocked => (Flags & FlagUnlocked) != 0;
        public bool UnlockedOnline => (Flags & FlagUnlockedOnline) != 0;
        public int Type => (int)(Flags & 0x7);

        public static bool TryParse(XdbfFile.Entry e, out GpdAchievement a)
        {
            a = null;
            var b = e.Data;
            if (b == null || b.Length < 0x1C || e.IsSync) return false;
            a = new GpdAchievement
            {
                EntryId = e.Id,
                Id = BE.U32(b, 0x04),
                ImageId = BE.U32(b, 0x08),
                Gamerscore = BE.U32(b, 0x0C),
                Flags = BE.U32(b, 0x10),
                UnlockTime = XdbfFile.FileTime(BE.U64(b, 0x14))
            };
            int p = 0x1C;
            a.Name = XdbfFile.ReadWStr(b, p, out p);
            a.UnlockedDescription = p < b.Length ? XdbfFile.ReadWStr(b, p, out p) : "";
            a.LockedDescription = p < b.Length ? XdbfFile.ReadWStr(b, p, out p) : "";
            return true;
        }

        /// <summary>
        /// Unlocks or locks a raw achievement record exactly as a console stores it offline: unlocked = the earned flag
        /// plus the Xbox 360 platform bits and the unlock time; locked = those bits (and the earned-online flag) and
        /// the time cleared. Every other bit is kept.
        /// </summary>
        public static void SetUnlockState(byte[] data, bool unlock, DateTime? time = null)
        {
            if (data == null || data.Length < 0x1C) throw new InvalidDataException("Achievement record is too small.");
            uint flags = BE.U32(data, 0x10);
            if (unlock)
            {
                flags |= FlagUnlocked;
                if ((flags & PlatformMask) == 0) flags |= PlatformXbox360;
                BE.W64(data, 0x14, (ulong)(time ?? DateTime.UtcNow).ToFileTimeUtc());
            }
            else
            {
                flags &= ~(FlagUnlocked | FlagUnlockedOnline | PlatformMask);
                BE.W64(data, 0x14, 0);
            }
            BE.W32(data, 0x10, flags);
        }
    }

    /// <summary>
    /// An avatar award (namespace 6 of a game's GPD inside the profile's PEC). Record: 0x2C bytes
    /// { size, clothing type, asset id (8), title id, image id, flags, unlock time (8), subcategory, (one more field) }
    /// followed by name, unlocked description and locked description. The entry id is title id, award number and the
    /// avatar gender it fits: ...0100 male, ...0200 female, ...0300 both. Earned awards carry the same flag bits as
    /// achievements earned offline (0x20000 + the Xbox 360 platform bits) and the time.
    /// </summary>
    public sealed class GpdAvatarAward
    {
        public ulong EntryId;
        public uint ClothingType, TitleId, ImageId, Flags, Subcategory;
        public ulong AssetId;
        public DateTime? UnlockTime;
        public string Name = "", UnlockedDescription = "", LockedDescription = "";

        public bool Unlocked => (Flags & GpdAchievement.FlagUnlocked) != 0;
        public bool ForMale => GenderOf(EntryId) == 1 || GenderOf(EntryId) == 3;
        public bool ForFemale => GenderOf(EntryId) == 2 || GenderOf(EntryId) == 3;
        public string Gender => GenderOf(EntryId) == 1 ? "Male" : GenderOf(EntryId) == 2 ? "Female" : GenderOf(EntryId) == 3 ? "Both" : "";

        public static int GenderOf(ulong entryId) => (int)(entryId >> 8 & 0xFF);

        public static bool TryParse(XdbfFile.Entry e, out GpdAvatarAward a)
        {
            a = null;
            var b = e.Data;
            if (b == null || e.IsSync || b.Length < 0x2C || BE.U32(b, 0) != 0x2C) return false;
            a = new GpdAvatarAward
            {
                EntryId = e.Id,
                ClothingType = BE.U32(b, 0x04),
                AssetId = BE.U64(b, 0x08),
                TitleId = BE.U32(b, 0x10),
                ImageId = BE.U32(b, 0x14),
                Flags = BE.U32(b, 0x18),
                UnlockTime = XdbfFile.FileTime(BE.U64(b, 0x1C)),
                Subcategory = BE.U32(b, 0x24)
            };
            int p = 0x2C;
            a.Name = XdbfFile.ReadWStr(b, p, out p);
            a.UnlockedDescription = p < b.Length ? XdbfFile.ReadWStr(b, p, out p) : "";
            a.LockedDescription = p < b.Length ? XdbfFile.ReadWStr(b, p, out p) : "";
            return true;
        }

        /// <summary>Earns or takes back a raw award record, exactly like <see cref="GpdAchievement.SetUnlockState"/> does for achievements.</summary>
        public static void SetUnlockState(byte[] data, bool unlock, DateTime? time = null)
        {
            if (data == null || data.Length < 0x2C) throw new InvalidDataException("Avatar award record is too small.");
            uint flags = BE.U32(data, 0x18);
            if (unlock)
            {
                flags |= GpdAchievement.FlagUnlocked;
                if ((flags & GpdAchievement.PlatformMask) == 0) flags |= GpdAchievement.PlatformXbox360;
                BE.W64(data, 0x1C, (ulong)(time ?? DateTime.UtcNow).ToFileTimeUtc());
            }
            else
            {
                flags &= ~(GpdAchievement.FlagUnlocked | GpdAchievement.FlagUnlockedOnline | GpdAchievement.PlatformMask);
                BE.W64(data, 0x1C, 0);
            }
            BE.W32(data, 0x18, flags);
        }
    }

    /// <summary>A profile setting (namespace 3): id, data type and raw value bytes.</summary>
    public sealed class GpdSetting
    {
        public const byte TypeInt32 = 1, TypeInt64 = 2, TypeDouble = 3, TypeString = 4, TypeFloat = 5, TypeBinary = 6, TypeDateTime = 7;

        public uint Id;
        public byte DataType;
        public byte[] Value;    // raw bytes from offset 0x10 onward (interpretation depends on DataType)

        public static bool TryParse(XdbfFile.Entry e, out GpdSetting s)
        {
            s = null;
            var b = e.Data;
            if (b == null || b.Length < 0x10 || e.IsSync) return false;
            s = new GpdSetting
            {
                Id = BE.U32(b, 0x00),
                DataType = b[0x08],
                Value = b.Skip(0x10).ToArray()
            };
            return true;
        }
    }
}
