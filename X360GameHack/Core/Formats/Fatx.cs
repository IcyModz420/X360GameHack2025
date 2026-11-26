using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Formats
{
    public sealed class FatxEntry
    {
        public string Name;
        public bool IsDirectory;
        public uint FirstCluster;
        public uint Size;
        public DateTime Modified;
        /// <summary>Where this entry's 64-byte record sits in the image (used when it is changed).</summary>
        public long EntryOffset;
    }

    /// <summary>
    /// FATX / XTAF reader for original Xbox and Xbox 360 hard drives, partition dumps and raw disks.
    /// Written from the public FATX format documentation. Nothing is written unless the image was
    /// opened with <see cref="OpenImage(string, bool)"/> writable = true (see Fatx.Write.cs).
    /// </summary>
    public sealed partial class FatxVolume : IDisposable
    {
        private readonly Stream _s;
        private readonly bool _ownsStream;
        public string Name { get; }
        public long Offset { get; }
        public long Length { get; private set; }
        public bool BigEndian { get; }          // XTAF (360) = big-endian, FATX (OG Xbox) = little-endian
        public uint SectorsPerCluster { get; private set; }
        public uint RootCluster { get; private set; }
        public long ClusterSize => SectorsPerCluster * 512L;
        public uint ClusterCount { get; private set; }
        private int _entrySize;
        private long _fatOffset, _dataOffset;
        private const int MaxDepth = 64;

        private FatxVolume(Stream s, bool owns, string name, long offset, long length, bool bigEndian)
        {
            _s = s; _ownsStream = owns; Name = name; Offset = offset; Length = length; BigEndian = bigEndian;
        }

        public override string ToString() => $"{Name}  ({FileSniffer.FormatSize(Length)}, {(BigEndian ? "Xbox 360 XTAF" : "Xbox FATX")})";

        /// <summary>The Xbox 360's original-Xbox compatibility partition ("HddX"): its Compatibility folder holds the emulator files.</summary>
        public const string CompatibilityName = "Compatibility (HddX)";
        public const long CompatibilityOffset = 0x120EB0000, CompatibilitySize = 0x10000000, DataOffset360 = 0x130EB0000;

        // Candidate partition locations. Sizes are hints only: every candidate is validated against its root folder.
        private static readonly (long Off, long Size, string Name)[] X360Layout =
        {
            (0x80000, 0x80000000, "Cache partition"),
            (0x80080000, 0xA0E30000, "System cache partition"),
            (0x10C030000, 0xCE30000, "System partition @ 0x10C030000"),
            (0x10C080000, 0xCE30000, "SysExt (system extended)"),
            (0x118EB0000, 0x8000000, "SysExt2 (system auxiliary)"),
            (0x120EB0000, 0x10000000, CompatibilityName),
            (0x130EB0000, 0, "Content (Data)")
        };

        private static readonly (long Off, long Size, string Name)[] OgLayout =
        {
            (0x80000, 0x2EE00000, "X: Cache"),
            (0x2EE80000, 0x2EE00000, "Y: Cache"),
            (0x5DC80000, 0x2EE00000, "Z: Cache"),
            (0x8CA80000, 0x1F400000, "C: System"),
            (0xABE80000, 0x1312D6000, "E: Data"),
            (0x1DD156000, 0, "F: Extended")
        };

        /// <summary>
        /// Entries of a development kit drive's partition table (sector 0): offset and length in 512-byte sectors, big-endian.
        /// From the Free60 FATX page (0x08 content volume, 0x10 dashboard volume of 0x80000 sectors) and aerosoul94's FATX
        /// write-up (the full list). Retail drives have no table - their offsets are fixed in the kernel.
        /// </summary>
        private static readonly (int At, string Name)[] DevkitEntries =
        {
            (0x08, "Content (devkit Partition1)"), (0x10, "System (devkit dashboard)"), (0x20, "Dump (devkit)"), (0x28, "PIX dump (devkit)"),
            (0x40, "Alternate flash (devkit)"), (0x48, "Cache0 (devkit)"), (0x50, "Cache1 (devkit)")
        };

        /// <summary>Where an original Xbox drive over 137 GB starts G: without a table: the last 28-bit sector, 0x0FFFFFFF (XBpartitioner, LBA48 BIOSes).</summary>
        public const long OgGStart = 0x0FFFFFFFL * 512;

        /// <summary>
        /// The partitions in an original Xbox drive's table at sector 0, as XBpartitioner writes it (partition.h): "****PARTINFO****"
        /// (16 bytes), 32 reserved bytes, then 14 entries of 32 bytes from 0x30 - name (16, space-padded), flags (0x80000000 =
        /// in use), first sector, sector count, reserved - little-endian. The entries are, in order, E: C: X: Y: Z: F: G: and
        /// on. None when sector 0 is not such a table, or the table is not sane (a place off the drive, two that overlap).
        /// </summary>
        public static List<(long Off, long Size, string Name)> OgTable(Stream s, long len)
        {
            var list = new List<(long, long, string)>();
            if (len < 0x200) return list;
            var b = new byte[0x200];
            lock (s) { s.Position = 0; if (ReadFull(s, b, 0, 0x200) != 0x200) return list; }
            if (Encoding.ASCII.GetString(b, 0, 16) != "****PARTINFO****") return list;
            string[] names = { "E: Data", "C: System", "X: Cache", "Y: Cache", "Z: Cache", "F: Extended", "G: Extended" };
            for (int i = 0; i < 14; i++)
            {
                int at = 0x30 + i * 0x20;
                uint flags = BE.LE32(b, at + 16), start = BE.LE32(b, at + 20), count = BE.LE32(b, at + 24);
                if ((flags & 0x80000000u) == 0 || count == 0) continue;
                long off = start * 512L, size = count * 512L;
                if (off < 0x1000 || off + size > len) return new List<(long, long, string)>();
                if (list.Any(p => off < p.Item1 + p.Item2 && p.Item1 < off + size)) return new List<(long, long, string)>();
                string label = Encoding.ASCII.GetString(b, at, 16).TrimEnd(' ', '\0');
                string name = i < names.Length ? names[i] : (char)('H' + i - names.Length) + ": Extended";
                list.Add((off, size, name + (label.Length > 0 ? " (" + label + ")" : "")));
            }
            return list;
        }

        /// <summary>
        /// For the log when a drive opens: what is at each place an original Xbox drive keeps a partition - the kernel's fixed
        /// ones, the two places drives over 137 GB start a G: partition - and whether sector 0 holds a partition table
        /// ("****PARTINFO****", which some BIOSes and tools write). Read only; nothing here decides how the drive is read.
        /// </summary>
        public static List<string> DescribeOgLayout(Stream s)
        {
            var lines = new List<string>();
            long len = s.Length;
            byte[] Head(long off, int n)
            {
                if (off + n > len) return null;
                var b = new byte[n];
                lock (s) { s.Position = off; if (ReadFull(s, b, 0, n) != n) return null; }
                return b;
            }
            var sector = Head(0, 0x200);
            if (sector != null && Encoding.ASCII.GetString(sector, 0, 16) == "****PARTINFO****")
            {
                var used = new List<string>();
                for (int i = 0; i < 14; i++)
                {
                    int at = 0x30 + i * 0x20;
                    uint flags = BE.LE32(sector, at + 16), start = BE.LE32(sector, at + 20), size = BE.LE32(sector, at + 24);
                    if (size == 0) continue;
                    string name = Encoding.ASCII.GetString(sector, at, 16).TrimEnd(' ', '\0');
                    used.Add("#" + i + " \"" + name + "\" flags " + flags.ToString("X8") + ", sector " + start + " (0x" + (start * 512L).ToString("X") + "), " + (size * 512L / (1L << 30)) + " GB");
                }
                lines.Add("sector 0 holds a partition table (****PARTINFO****) with " + used.Count + " entr" + (used.Count == 1 ? "y" : "ies") + ": " + string.Join("; ", used));
            }
            else lines.Add("sector 0 holds no partition table");
            foreach (var (off, name) in new (long, string)[]
            {
                (0x80000, "X:"), (0x2EE80000, "Y:"), (0x5DC80000, "Z:"), (0x8CA80000, "C:"), (0xABE80000, "E:"), (0x1DD156000, "F:"),
                (0x1FFFFFFE00, "G: at the 137 GB boundary (sector 0x0FFFFFFF)"), (0x2000000000, "G: at 128 GiB (sector 0x10000000)")
            })
            {
                var h = Head(off, 0x10);
                if (h == null) continue;
                string magic = Encoding.ASCII.GetString(h, 0, 4);
                if (magic == "FATX") lines.Add(name + " @0x" + off.ToString("X") + ": FATX, " + (BE.LE32(h, 8) * 512L / 1024) + " KB clusters, root cluster " + BE.LE32(h, 12));
                else if (off > 0x1DD156000) lines.Add(name + " @0x" + off.ToString("X") + ": no FATX header");
            }
            return lines;
        }

        /// <summary>The partitions a development kit drive's table lists, or none when sector 0 is not such a table.</summary>
        public static List<(long Off, long Size, string Name)> DevkitTable(Stream s, long len)
        {
            var list = new List<(long, long, string)>();
            if (len < 0x200) return list;
            var b = new byte[0x200];
            lock (s) { s.Position = 0; if (ReadFull(s, b, 0, 0x200) != 0x200) return list; }
            foreach (var (at, name) in DevkitEntries)
            {
                long off = BE.U32(b, at) * 512L, size = BE.U32(b, at + 4) * 512L;
                if (off < 0x1000 || size < 0x10000 || off + size > len || off % 0x1000 != 0) continue;
                if (list.Any(p => off < p.Item1 + p.Item2 && p.Item1 < off + size)) return new List<(long, long, string)>();   // overlapping: not a table
                list.Add((off, size, name));
            }
            return list;
        }

        /// <summary>Opens every FATX volume in an image file (full HDD dump or a single partition dump).</summary>
        public static List<FatxVolume> OpenImage(string path) => OpenImage(path, false);

        /// <summary>Opens an image file; with <paramref name="writable"/> the volumes can be changed (image files only).</summary>
        public static List<FatxVolume> OpenImage(string path, bool writable)
        {
            var fs = writable
                ? new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1 << 16)
                : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            var vols = OpenStream(fs, Path.GetFileName(path), true);
            if (vols.Count == 0) fs.Dispose();
            return vols;
        }

        /// <summary>Opens every FATX volume found in a read-only stream (image file or raw disk).</summary>
        public static List<FatxVolume> OpenStream(Stream s, string displayName, bool ownsStream)
        {
            var found = new List<FatxVolume>();
            long len = s.Length;
            string MagicAt(long off)
            {
                if (off + 4 > len) return null;
                var m = new byte[4];
                lock (s) { s.Position = off; if (ReadFull(s, m, 0, 4) != 4) return null; }
                return Encoding.ASCII.GetString(m);
            }

            string m0 = MagicAt(0);
            if (m0 == "XTAF" || m0 == "FATX")
            {
                var v = new FatxVolume(s, ownsStream, displayName, 0, len, m0 == "XTAF");
                if (v.InitValidated(new[] { len })) found.Add(v);
                return found;
            }
            bool owns = ownsStream;
            // A development kit drive lists its partitions in a table at sector 0; each is still checked like any other volume
            foreach (var p in DevkitTable(s, len))
            {
                if (MagicAt(p.Off) != "XTAF") continue;
                var v = new FatxVolume(s, owns, p.Name, p.Off, 0, true);
                if (v.InitValidated(new[] { p.Size })) { found.Add(v); owns = false; }
            }
            if (found.Count > 0) return found;
            // An original Xbox drive set up by XBpartitioner (read by LBA48 BIOSes such as Cerbios) lists its partitions in a
            // table at sector 0: where they are and how big is the drive's own. Each is still checked like any other volume.
            foreach (var p in OgTable(s, len))
            {
                if (MagicAt(p.Off) != "FATX") continue;
                var v = new FatxVolume(s, owns, p.Name, p.Off, 0, false);
                if (v.InitValidated(new[] { p.Size })) { found.Add(v); owns = false; }
            }
            if (found.Count > 0) return found;
            foreach (var layout in new[] { X360Layout, OgLayout })
            {
                // Both consoles start their first partition at 0x80000, so the position alone does not say which
                // drive this is; the signature does: Xbox 360 volumes say "XTAF", original Xbox volumes "FATX".
                // (Found with the xemu project's reference drive image, which was first read with the 360 table.)
                string wanted = ReferenceEquals(layout, X360Layout) ? "XTAF" : "FATX";
                var places = layout.ToList();
                // a drive over 137 GB without a table: LBA48 BIOSes end F: at the last 28-bit sector and start G: there
                // (XBpartitioner's standard layout) - when a volume starts at that sector, F: ends where it begins
                if (!ReferenceEquals(layout, X360Layout) && len > OgGStart + 0x2000) places.Add((OgGStart, 0, "G: Extended"));
                var present = places.Where(p => MagicAt(p.Off) == wanted).ToList();
                for (int i = 0; i < present.Count; i++)
                {
                    var p = present[i];
                    bool big = MagicAt(p.Off) == "XTAF";
                    var sizes = new List<long>();
                    if (p.Size > 0 && p.Off + p.Size <= len) sizes.Add(p.Size);
                    if (i + 1 < present.Count) sizes.Add(present[i + 1].Off - p.Off);
                    sizes.Add(len - p.Off);
                    var v = new FatxVolume(s, owns, p.Name, p.Off, 0, big);
                    if (v.InitValidated(sizes.Where(x => x > 0x2000).Distinct()))
                    {
                        found.Add(v);
                        owns = false;      // only the first volume closes the shared stream
                    }
                }
                if (found.Count > 0) break;
            }
            return found;
        }

        private static int ReadFull(Stream s, byte[] b, int off, int count)
        {
            int n = 0;
            while (n < count) { int r = s.Read(b, off + n, count - n); if (r <= 0) break; n += r; }
            return n;
        }

        private uint U32(byte[] b, int o) => BigEndian ? BE.U32(b, o) : BE.LE32(b, o);
        private ushort U16(byte[] b, int o) => BigEndian ? BE.U16(b, o) : (ushort)(b[o] | b[o + 1] << 8);

        private byte[] Read(long absolute, int count)
        {
            var buf = new byte[count];
            lock (_s)
            {
                _s.Position = absolute;
                ReadFull(_s, buf, 0, count);
            }
            return buf;
        }

        /// <summary>
        /// Tries the candidate partition sizes and keeps the first one whose root folder is valid.
        ///
        /// Where the data area starts was checked on a real Xbox 360 drive (Hitachi HTS545025B9SA00, 2026-11-23):
        ///  - the table (FAT) has one entry more than the volume has clusters (entry 0 is the media marker), as the
        ///    console's kernel computes it - on SysExt2 (8192 clusters) only that puts the root folder (with "online")
        ///    where it is; one entry less lands 4 KB early on an empty block. It only matters when the cluster count
        ///    lands the table exactly on a 4 KB boundary, which is why the other partitions read the same either way;
        ///  - the size of a table entry is what the table's own first entry says (0xFFF8 = 2 bytes, 0xFFFFFFF8 =
        ///    4 bytes): the compatibility partition (16384 clusters) uses 4-byte entries although the count alone
        ///    would say 2.
        /// Original Xbox (FATX) drives keep the plain cluster count, which the xemu project's reference drive needs.
        /// When two layouts both look possible, the one that shows a real folder wins; an empty root on an Xbox 360
        /// volume goes with the console's count.
        /// </summary>
        private bool InitValidated(IEnumerable<long> candidateLengths)
        {
            var h = Read(Offset, 0x20);
            SectorsPerCluster = U32(h, 8);
            RootCluster = U32(h, 12);
            if (SectorsPerCluster == 0 || SectorsPerCluster > 1024 || (SectorsPerCluster & (SectorsPerCluster - 1)) != 0 || RootCluster == 0)
                return false;
            _fatOffset = Offset + 0x1000;
            var media = Read(_fatOffset, 4);
            // big-endian only: there FF F8 (2-byte) and FF FF FF F8 (4-byte) cannot be mistaken for each other; on a
            // little-endian table F8 FF followed by an end marker FF FF reads like a 4-byte marker
            int marked = !BigEndian ? 0 : U32(media, 0) == 0xFFFFFFF8 ? 4 : U16(media, 0) == 0xFFF8 ? 2 : 0;
            // Every real Xbox 360 volume's table starts with that marker. The console's two cache partitions (0x80000 and
            // 0x80080000) carry an XTAF header too, but hold the console's own cache data, not files: their "table" is
            // noise (checked on a real drive) - they are not a file system and are never opened as one.
            if (BigEndian && marked == 0) return false;
            // a size whose root folder reads as empty is kept only when no other size shows real entries: zeros (a fresh
            // drive, or the wrong place on a big one - F: of an original Xbox drive split at 137 GB, read to the drive's end)
            // look like an empty folder too
            (long Len, long Data, int Entry)? empty = null;
            foreach (long len in candidateLengths)
            {
                Length = len;
                ClusterCount = (uint)(Length / ClusterSize);
                (long Data, int Entry, int Score)? best = null;
                foreach (long extra in BigEndian ? new long[] { 1, 0 } : new long[] { 0, 1 })
                {
                    long entries = ClusterCount + extra;
                    int entry = marked != 0 ? marked : entries < 0xFFF0 ? 2 : 4;
                    long fatBytes = (entries * entry + 0xFFF) & ~0xFFFL;
                    _entrySize = entry;
                    _dataOffset = _fatOffset + fatBytes;
                    if (RootCluster > ClusterCount) continue;
                    int score = RootScore();
                    if (score < 0) continue;
                    if (best == null || score > best.Value.Score) best = (_dataOffset, entry, score);
                }
                if (best != null && best.Value.Score > 0)
                {
                    _dataOffset = best.Value.Data;
                    _entrySize = best.Value.Entry;
                    return true;
                }
                if (best != null && empty == null) empty = (len, best.Value.Data, best.Value.Entry);
            }
            if (empty == null) return false;
            Length = empty.Value.Len;
            ClusterCount = (uint)(Length / ClusterSize);
            _dataOffset = empty.Value.Data;
            _entrySize = empty.Value.Entry;
            return true;
        }

        private bool RootLooksValid() => RootScore() >= 0;

        /// <summary>-1 when the root folder is not a folder here; otherwise how many real entries it shows before its end (0 = empty).</summary>
        private int RootScore()
        {
            var d = Read(ClusterOffset(RootCluster), 64 * 4);
            int names = 0;
            for (int o = 0; o + 64 <= d.Length; o += 64)
            {
                byte n = d[o];
                if (n == 0x00 || n == 0xFF) return names;       // end of directory reached cleanly
                if (n == 0xE5) continue;                          // deleted entry
                if (n > 42) return -1;
                for (int i = 0; i < n; i++) if (d[o + 2 + i] < 0x20 || d[o + 2 + i] > 0x7E) return -1;
                uint first = U32(d, o + 0x2C);
                if (first > ClusterCount) return -1;
                names++;
            }
            return names;
        }

        private uint NextCluster(uint cluster)
        {
            var b = Read(_fatOffset + (long)cluster * _entrySize, _entrySize);
            uint v = _entrySize == 2 ? U16(b, 0) : U32(b, 0);
            if (_entrySize == 2 && v >= 0xFFF0) return 0xFFFFFFFF;
            return v;
        }

        private bool IsEnd(uint c) => c == 0 || c >= 0xFFFFFFF0 || c > ClusterCount;

        private long ClusterOffset(uint cluster) => _dataOffset + (long)(cluster - 1) * ClusterSize;

        private List<uint> Chain(uint first, int max = 1 << 22)
        {
            var list = new List<uint>();
            var seen = new HashSet<uint>();
            for (uint c = first; !IsEnd(c) && list.Count < max && seen.Add(c); c = NextCluster(c)) list.Add(c);
            return list;
        }

        public List<FatxEntry> ListRoot() => ListDirectory(RootCluster);

        public List<FatxEntry> ListDirectory(uint firstCluster)
        {
            var result = new List<FatxEntry>();
            foreach (uint c in Chain(firstCluster, 4096))
            {
                var data = Read(ClusterOffset(c), (int)ClusterSize);
                for (int o = 0; o + 64 <= data.Length; o += 64)
                {
                    byte nameLen = data[o];
                    if (nameLen == 0x00 || nameLen == 0xFF) return result;   // end of directory
                    if (nameLen == 0xE5 || nameLen > 42) continue;           // deleted / invalid
                    byte attr = data[o + 1];
                    string name = Encoding.ASCII.GetString(data, o + 2, nameLen);
                    if (name == "." || name == "..") continue;
                    result.Add(new FatxEntry
                    {
                        Name = name,
                        IsDirectory = (attr & 0x10) != 0,
                        FirstCluster = U32(data, o + 0x2C),
                        Size = U32(data, o + 0x30),
                        Modified = Shown(FatTime(U32(data, o + 0x38), BigEndian ? 1980 : 2000)),
                        EntryOffset = ClusterOffset(c) + o
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// A stored time as this PC shows times (local). An Xbox 360 drive keeps UTC - proven on a real console on 2026-11-24:
        /// a file XBDM created at 18:34:10 UTC is stored as 18:34:12, and a time stored as 00:07:06 is listed by the console as
        /// 00:07:06 UTC. Original Xbox drives: shown as stored (not yet checked against an original Xbox).
        /// </summary>
        private DateTime Shown(DateTime stored)
        {
            if (stored == DateTime.MinValue || !BigEndian) return stored;
            try { return DateTime.SpecifyKind(stored, DateTimeKind.Utc).ToLocalTime(); }
            catch (ArgumentException) { return stored; }
        }

        private static DateTime FatTime(uint t, int baseYear)
        {
            try
            {
                int date = (int)(t >> 16), time = (int)(t & 0xFFFF);
                int year = ((date >> 9) & 0x7F) + baseYear, month = (date >> 5) & 0xF, day = date & 0x1F;
                int hour = (time >> 11) & 0x1F, min = (time >> 5) & 0x3F, sec = (time & 0x1F) * 2;
                if (month < 1 || month > 12 || day < 1 || day > 31 || hour > 23 || min > 59 || sec > 59) return DateTime.MinValue;
                return new DateTime(year, month, day, hour, min, sec);
            }
            catch { return DateTime.MinValue; }
        }

        public void ExtractFile(FatxEntry e, string localPath, Action<long> progress, CancellationToken ct)
        {
            long remaining = e.Size;
            using (var o = File.Create(localPath))
            {
                foreach (uint c in Chain(e.FirstCluster))
                {
                    if (remaining <= 0) break;
                    ct.ThrowIfCancellationRequested();
                    int n = (int)Math.Min(ClusterSize, remaining);
                    var d = Read(ClusterOffset(c), n);
                    o.Write(d, 0, n);
                    remaining -= n;
                    progress?.Invoke(n);
                }
            }
            if (remaining > 0) throw new IOException(e.Name + ": cluster chain ended early (damaged file system).");
        }

        /// <summary>The first <paramref name="count"/> bytes of a file (or the whole file if smaller) - e.g. an STFS header.</summary>
        public byte[] ReadHead(FatxEntry e, int count)
        {
            int want = (int)Math.Min(count, e.Size);
            var result = new byte[want];
            int done = 0;
            foreach (uint c in Chain(e.FirstCluster, (int)((want + ClusterSize - 1) / ClusterSize) + 1))
            {
                if (done >= want) break;
                int n = (int)Math.Min(ClusterSize, want - done);
                Buffer.BlockCopy(Read(ClusterOffset(c), n), 0, result, done, n);
                done += n;
            }
            if (done < want) throw new IOException(e.Name + ": cluster chain ended early (damaged file system).");
            return result;
        }

        public void ExtractDirectory(FatxEntry dir, string localDir, Action<long> progress, CancellationToken ct)
            => ExtractDirectory(dir, localDir, progress, ct, 0, new HashSet<uint>());

        private void ExtractDirectory(FatxEntry dir, string localDir, Action<long> progress, CancellationToken ct, int depth, HashSet<uint> seen)
        {
            if (depth > MaxDepth || !seen.Add(dir.FirstCluster)) throw new IOException("Folder loop detected at " + dir.Name + " (damaged file system).");
            Directory.CreateDirectory(localDir);
            foreach (var e in ListDirectory(dir.FirstCluster))
            {
                ct.ThrowIfCancellationRequested();
                string target = PathSafety.CombineUnder(localDir, PathSafety.SafeName(e.Name));
                if (e.IsDirectory) ExtractDirectory(e, target, progress, ct, depth + 1, seen);
                else ExtractFile(e, target, progress, ct);
            }
        }

        /// <summary>
        /// A read-only, seekable stream over a file's bytes, read from the drive only as it is consumed - runs of clusters that
        /// sit one after another are read in one go (drag-out to Windows hands one of these per file to Explorer).
        /// </summary>
        public Stream OpenRead(FatxEntry e)
        {
            if (e == null || e.IsDirectory) throw new ArgumentException("Not a file.");
            return new FileReader(this, e);
        }

        private sealed class FileReader : Stream
        {
            private readonly FatxVolume _v;
            private readonly FatxEntry _e;
            private List<uint> _chain;
            private long _pos;
            public FileReader(FatxVolume v, FatxEntry e) { _v = v; _e = e; }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _e.Size;
            public override long Position
            {
                get => _pos;
                set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); _pos = value; }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_pos >= _e.Size || count <= 0) return 0;
                long cs = _v.ClusterSize;
                if (_chain == null)
                {
                    long need = (_e.Size + cs - 1) / cs;
                    _chain = _v.Chain(_e.FirstCluster, (int)Math.Min(need, int.MaxValue));
                    if (_chain.Count < need) throw new IOException(_e.Name + ": cluster chain ended early (damaged file system).");
                }
                int done = 0;
                while (done < count && _pos < _e.Size)
                {
                    int idx = (int)(_pos / cs), last = idx;
                    long at = _pos % cs, want = Math.Min(count - done, _e.Size - _pos), run = cs - at;
                    while (run < want && last + 1 < _chain.Count && _chain[last + 1] == _chain[last] + 1) { run += cs; last++; }
                    int n = (int)Math.Min(want, run);
                    var d = _v.Read(_v.ClusterOffset(_chain[idx]) + at, n);
                    Buffer.BlockCopy(d, 0, buffer, offset + done, n);
                    done += n;
                    _pos += n;
                }
                return done;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long p = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _pos + offset : _e.Size + offset;
                if (p < 0) throw new IOException("Seek before the start of " + _e.Name + ".");
                return _pos = p;
            }

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        public long MeasureDirectory(FatxEntry dir) => MeasureDirectory(dir, 0, new HashSet<uint>());

        private long MeasureDirectory(FatxEntry dir, int depth, HashSet<uint> seen)
        {
            if (depth > MaxDepth || !seen.Add(dir.FirstCluster)) return 0;
            long total = 0;
            foreach (var e in ListDirectory(dir.FirstCluster))
                total += e.IsDirectory ? MeasureDirectory(e, depth + 1, seen) : e.Size;
            return total;
        }

        public void Dispose()
        {
            if (_ownsStream) _s.Dispose();
        }
    }
}
