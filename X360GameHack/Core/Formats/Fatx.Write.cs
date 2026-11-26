using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace X360GameHack.Core.Formats
{
    /// <summary>What <see cref="FatxVolume.Check"/> found.</summary>
    public sealed class FatxCheck
    {
        public int Files, Folders;
        public long UsedClusters, FreeClusters, LostClusters;
        public long FileBytes;
        public readonly List<string> Problems = new List<string>();
        public bool Healthy => Problems.Count == 0;
        public override string ToString() =>
            Files + " file(s), " + Folders + " folder(s), " + UsedClusters + " cluster(s) used, " + FreeClusters + " free" +
            (LostClusters > 0 ? ", " + LostClusters + " marked used but belonging to nothing" : "") +
            (Problems.Count == 0 ? " - no problems" : " - " + Problems.Count + " problem(s)");
    }

    /// <summary>
    /// Changing a FATX / XTAF volume: add and replace files, make folders, rename, delete, and a consistency check.
    ///
    /// Order of every change, so that an interruption can never leave a file pointing at clusters it does not own:
    ///   adding   - 1. mark the clusters used in the table, 2. write the data, 3. write the folder record last
    ///   deleting - 1. mark the folder record deleted, 2. then give the clusters back
    /// The worst an interruption leaves behind is clusters marked used that belong to nothing (wasted space, never
    /// damage); <see cref="Check"/> counts them.
    /// </summary>
    public sealed partial class FatxVolume
    {
        private uint[] _fat;            // the whole allocation table, loaded on first use
        private uint _lastCluster;      // highest cluster number that lies inside the volume
        private uint _allocHint = 1;
        private const uint EndOfChain = 0xFFFFFFFF;
        private const int MaxDirectoryClusters = 4096;

        public bool Writable => _s.CanWrite;
        /// <summary>For automated tests only: the time new folders are stamped with. Null in normal use (the clock).</summary>
        internal static Func<DateTime> TestNow;

        private void RequireWritable()
        {
            if (!_s.CanWrite) throw new InvalidOperationException("This drive is open read-only.");
        }

        // ------------------------------------------------------------------ low level

        private void W32(byte[] b, int o, uint v)
        {
            if (BigEndian) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
            else { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
        }

        private void Write(long absolute, byte[] data, int offset, int count)
        {
            if (absolute < Offset || absolute + count > Offset + Length) throw new IOException("Refused to write outside the volume.");
            lock (_s)
            {
                _s.Position = absolute;
                _s.Write(data, offset, count);
            }
        }

        // one load of the table at a time: two threads loading it at once could leave the older copy in place after the
        // other had already changed it (and then hand out the same clusters twice)
        private readonly object _fatGate = new object();

        /// <summary>The highest cluster number the table and the data area both hold.</summary>
        private uint LastCluster()
        {
            long dataClusters = (Offset + Length - _dataOffset) / ClusterSize;
            long fatEntries = (_dataOffset - _fatOffset) / _entrySize;
            return (uint)Math.Max(0, Math.Min(Math.Min((long)ClusterCount, fatEntries - 1), dataClusters));
        }

        private void LoadFat()
        {
            if (_fat != null) return;
            lock (_fatGate)
            {
                if (_fat != null) return;
                _counted = -1;                  // a count read before this load may be out of date once changes start
                uint last = LastCluster();
                var raw = new byte[((long)last + 1) * _entrySize];
                lock (_s)
                {
                    _s.Position = _fatOffset;
                    if (ReadFull(_s, raw, 0, raw.Length) != raw.Length) throw new IOException("The allocation table could not be read.");
                }
                var fat = new uint[last + 1];
                for (uint i = 0; i <= last; i++)
                {
                    uint v = _entrySize == 2 ? U16(raw, (int)i * 2) : U32(raw, (int)i * 4);
                    if (_entrySize == 2 && v >= 0xFFF0) v = 0xFFFF0000 | v;      // reserved / bad / end, widened
                    fat[i] = v;
                }
                _lastCluster = last;
                _fat = fat;
            }
        }

        /// <summary>Writes table entries [first..last] back to the image.</summary>
        private void FlushFat(uint first, uint last)
        {
            var raw = new byte[((long)last - first + 1) * _entrySize];
            for (uint i = first; i <= last; i++)
            {
                uint v = _fat[i];
                int o = (int)(i - first) * _entrySize;
                if (_entrySize == 2)
                {
                    ushort w = (ushort)v;
                    if (BigEndian) { raw[o] = (byte)(w >> 8); raw[o + 1] = (byte)w; } else { raw[o] = (byte)w; raw[o + 1] = (byte)(w >> 8); }
                }
                else W32(raw, o, v);
            }
            lock (_s)
            {
                _s.Position = _fatOffset + (long)first * _entrySize;
                _s.Write(raw, 0, raw.Length);
            }
        }

        /// <summary>
        /// The free space, counted straight from the table on the drive, a few MB at a time - for showing it while browsing:
        /// it neither keeps the table in memory (2 TB drives have tables of hundreds of MB) nor touches the copy that
        /// changes use. When that copy is loaded already, it is counted instead (it is what the drive holds).
        /// </summary>
        public long CountFreeBytes(CancellationToken ct = default(CancellationToken))
        {
            var loaded = _fat;
            if (loaded != null)
            {
                long n = 0;
                for (long i = 1; i < loaded.Length; i++) if (loaded[i] == 0) n++;
                return n * ClusterSize;
            }
            // counted once: opening another folder does not read the table again (a 1 TB F: has a 60 MB table). Any change
            // loads the table first (LoadFat forgets this count), and from then on the loaded copy is counted instead.
            lock (_countGate)
            {
                if (_counted >= 0 && _fat == null) return _counted;
                long free = CountFromDrive(ct);
                if (_fat == null) _counted = free;
                return free;
            }
        }

        private readonly object _countGate = new object();
        private long _counted = -1;             // the last count read from the drive (-1: none, or the table was loaded since)

        private long CountFromDrive(CancellationToken ct)
        {
            uint last = LastCluster();
            long free = 0;
            var buf = new byte[4 << 20];
            long first = 1;                                            // entry 0 is the media marker
            while (first <= last)
            {
                ct.ThrowIfCancellationRequested();
                long count = Math.Min(buf.Length / _entrySize, last - first + 1);
                int bytes = (int)(count * _entrySize);
                lock (_s)
                {
                    _s.Position = _fatOffset + first * _entrySize;
                    if (ReadFull(_s, buf, 0, bytes) != bytes) throw new IOException("The allocation table could not be read.");
                }
                for (int i = 0; i < count; i++)
                    if ((_entrySize == 2 ? U16(buf, i * 2) : U32(buf, i * 4)) == 0) free++;
                first += count;
            }
            return free * ClusterSize;
        }

        public long FreeBytes
        {
            get
            {
                LoadFat();
                long free = 0;
                for (uint i = 1; i <= _lastCluster; i++) if (_fat[i] == 0) free++;
                return free * ClusterSize;
            }
        }

        /// <summary>Takes <paramref name="count"/> free clusters, links them into one chain and writes the table.</summary>
        private List<uint> Allocate(long count)
        {
            LoadFat();
            var chain = new List<uint>();
            if (count <= 0) return chain;
            uint start = _allocHint < 1 || _allocHint > _lastCluster ? 1 : _allocHint;
            for (uint n = 0, i = start; n < _lastCluster && chain.Count < count; n++, i = i == _lastCluster ? 1 : i + 1)
                if (_fat[i] == 0) chain.Add(i);
            if (chain.Count < count)
                throw new IOException("Not enough free space on the drive: " + FileSniffer.FormatSize(count * ClusterSize) + " needed, " +
                                      FileSniffer.FormatSize(chain.Count * ClusterSize) + " free.");
            for (int k = 0; k < chain.Count; k++) _fat[chain[k]] = k + 1 < chain.Count ? chain[k + 1] : EndOfChain;
            FlushRuns(chain);
            _allocHint = chain[chain.Count - 1] == _lastCluster ? 1 : chain[chain.Count - 1] + 1;
            return chain;
        }

        private void FreeChain(List<uint> chain)
        {
            if (chain.Count == 0) return;
            LoadFat();
            foreach (uint c in chain) if (c >= 1 && c <= _lastCluster) _fat[c] = 0;
            FlushRuns(chain.Where(c => c >= 1 && c <= _lastCluster).ToList());
            uint lowest = chain.Min();
            if (lowest >= 1 && lowest < _allocHint) _allocHint = lowest;
        }

        /// <summary>Writes the table entries of these clusters, grouped into runs that are close together.</summary>
        private void FlushRuns(List<uint> clusters)
        {
            if (clusters.Count == 0) return;
            var sorted = clusters.OrderBy(c => c).ToList();
            uint first = sorted[0], last = sorted[0];
            foreach (uint c in sorted.Skip(1))
            {
                if (c - last <= 4096) { last = c; continue; }
                FlushFat(first, last);
                first = last = c;
            }
            FlushFat(first, last);
            lock (_s) _s.Flush();
        }

        /// <summary>The chain of a file or folder, taken from the table in memory, with every link checked.</summary>
        private List<uint> StrictChain(uint first, string what)
        {
            LoadFat();
            var list = new List<uint>();
            var seen = new HashSet<uint>();
            uint c = first;
            while (c != 0 && c < 0xFFFF0000)
            {
                if (c > _lastCluster) throw new IOException(what + ": the cluster chain leaves the volume (damaged file system).");
                if (!seen.Add(c)) throw new IOException(what + ": the cluster chain loops (damaged file system).");
                list.Add(c);
                c = _fat[c];
            }
            return list;
        }

        // ------------------------------------------------------------------ names and records

        private const string NameChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!#$%&'()-.@[]^_`{}~ ";

        /// <summary>Why a name cannot be used on an Xbox drive, or null when it is fine.</summary>
        public static string NameProblem(string name)
        {
            if (string.IsNullOrEmpty(name)) return "The name is empty.";
            if (name.Length > 42) return "\"" + name + "\" is " + name.Length + " characters long; Xbox drives allow 42.";
            if (name == "." || name == "..") return "\"" + name + "\" is not a usable name.";
            foreach (char ch in name)
                if (NameChars.IndexOf(ch) < 0) return "\"" + name + "\" contains \"" + ch + "\", which Xbox drives do not allow.";
            return null;
        }

        private static void RequireName(string name)
        {
            string problem = NameProblem(name);
            if (problem != null) throw new ArgumentException(problem);
        }

        private uint EncodeTime(DateTime t)
        {
            // an Xbox 360 drive keeps UTC (see Shown); a time without a kind is this PC's local time
            if (BigEndian && t.Kind != DateTimeKind.Utc) t = t.ToUniversalTime();
            int baseYear = BigEndian ? 1980 : 2000;
            if (t.Year < baseYear) t = new DateTime(baseYear, 1, 1);
            if (t.Year > baseYear + 127) t = new DateTime(baseYear + 127, 12, 31, 23, 59, 58);
            uint date = (uint)((t.Year - baseYear) << 9 | t.Month << 5 | t.Day);
            uint time = (uint)(t.Hour << 11 | t.Minute << 5 | t.Second / 2);
            return date << 16 | time;
        }

/// <summary>
        /// A 64-byte record. <paramref name="time"/> is the modified time (what listings show); <paramref name="made"/>, when
        /// given, the creation and access time - when it was put on the drive, as Windows does for a copy and the console for
        /// an upload (recovery of deleted files relies on creation times being true).
        /// </summary>
        private byte[] BuildRecord(string name, bool directory, uint firstCluster, uint size, DateTime time, DateTime? made = null)
        {
            var r = new byte[64];
            for (int i = 0; i < 64; i++) r[i] = 0xFF;
            r[0] = (byte)name.Length;
            r[1] = (byte)(directory ? 0x10 : 0x00);
            Encoding.ASCII.GetBytes(name, 0, name.Length, r, 2);
            W32(r, 0x2C, firstCluster);
            W32(r, 0x30, size);
            uint stamp = EncodeTime(time), madeStamp = made.HasValue ? EncodeTime(made.Value) : stamp;
            W32(r, 0x34, madeStamp);
            W32(r, 0x38, stamp);
            W32(r, 0x3C, madeStamp);
            return r;
        }

        public FatxEntry Find(uint directoryCluster, string name)
            => ListDirectory(directoryCluster).FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// A place for a new record in a folder: a deleted record if there is one, else the end marker (the record
        /// after it becomes the new end), else a new cluster added to the folder.
        /// </summary>
        private long FindSlot(uint directoryCluster)
        {
            var chain = StrictChain(directoryCluster, "Folder");
            if (chain.Count == 0) throw new IOException("The folder has no clusters (damaged file system).");
            int per = (int)ClusterSize;
            long deleted = -1;
            for (int ci = 0; ci < chain.Count; ci++)
            {
                long at = ClusterOffset(chain[ci]);
                var data = Read(at, per);
                for (int o = 0; o + 64 <= per; o += 64)
                {
                    byte n = data[o];
                    if (n == 0xE5) { if (deleted < 0) deleted = at + o; continue; }
                    if (n != 0x00 && n != 0xFF) continue;
                    if (deleted >= 0) return deleted;
                    // the end marker: whatever follows it must read as the end too
                    long next = o + 64 < per ? at + o + 64 : ci + 1 < chain.Count ? ClusterOffset(chain[ci + 1]) : -1;
                    if (next >= 0) Write(next, new byte[] { 0xFF }, 0, 1);
                    return at + o;
                }
            }
            if (deleted >= 0) return deleted;
            if (chain.Count >= MaxDirectoryClusters) throw new IOException("The folder is full.");
            // every record is in use: give the folder one more cluster
            uint extra = Allocate(1)[0];
            var blank = new byte[per];
            for (int i = 0; i < per; i++) blank[i] = 0xFF;
            Write(ClusterOffset(extra), blank, 0, per);
            _fat[chain[chain.Count - 1]] = extra;
            FlushFat(chain[chain.Count - 1], chain[chain.Count - 1]);
            return ClusterOffset(extra);
        }

        // ------------------------------------------------------------------ changes

        public FatxEntry CreateDirectory(uint parentCluster, string name)
        {
            RequireWritable();
            RequireName(name);
            var existing = Find(parentCluster, name);
            if (existing != null)
            {
                if (existing.IsDirectory) return existing;
                throw new IOException("A file called \"" + name + "\" is already there.");
            }
            long slot = FindSlot(parentCluster);
            uint cluster = Allocate(1)[0];
            var blank = new byte[(int)ClusterSize];
            for (int i = 0; i < blank.Length; i++) blank[i] = 0xFF;
            Write(ClusterOffset(cluster), blank, 0, blank.Length);
            var now = TestNow?.Invoke() ?? DateTime.Now;
            Write(slot, BuildRecord(name, true, cluster, 0, now), 0, 64);
            lock (_s) _s.Flush();
            return new FatxEntry { Name = name, IsDirectory = true, FirstCluster = cluster, Size = 0, Modified = now, EntryOffset = slot };
        }

        /// <summary>Adds a file to a folder, or replaces the file of that name. The stream must supply exactly <paramref name="length"/> bytes.</summary>
        public FatxEntry WriteFile(uint parentCluster, string name, Stream source, long length, DateTime modified,
                                   Action<long> progress = null, CancellationToken ct = default(CancellationToken))
        {
            RequireWritable();
            RequireName(name);
            if (length < 0 || length > uint.MaxValue) throw new IOException(name + " is larger than 4 GB, the most one file can be on an Xbox drive.");
            var existing = Find(parentCluster, name);
            if (existing != null && existing.IsDirectory) throw new IOException("A folder called \"" + name + "\" is already there.");
            var oldChain = existing != null ? StrictChain(existing.FirstCluster, existing.Name) : new List<uint>();

            long slot = existing != null ? existing.EntryOffset : FindSlot(parentCluster);
            int per = (int)ClusterSize;
            var chain = Allocate((length + per - 1) / per);
            try
            {
                var buf = new byte[per];
                long remaining = length;
                foreach (uint c in chain)
                {
                    ct.ThrowIfCancellationRequested();
                    int want = (int)Math.Min(per, remaining);
                    int got = 0;
                    while (got < want) { int r = source.Read(buf, got, want - got); if (r <= 0) break; got += r; }
                    if (got != want) throw new IOException(name + ": the source ended early.");
                    if (want < per) Array.Clear(buf, want, per - want);
                    Write(ClusterOffset(c), buf, 0, per);
                    remaining -= want;
                    progress?.Invoke(want);
                }
            }
            catch
            {
                FreeChain(chain);       // nothing points at these yet
                throw;
            }
            // the record goes in last; for a replacement this one write swaps old for new
            string storedName = existing != null ? existing.Name : name;
            Write(slot, BuildRecord(storedName, false, chain.Count > 0 ? chain[0] : 0, (uint)length, modified, TestNow?.Invoke() ?? DateTime.Now), 0, 64);
            lock (_s) _s.Flush();
            FreeChain(oldChain);
            return new FatxEntry { Name = storedName, IsDirectory = false, FirstCluster = chain.Count > 0 ? chain[0] : 0, Size = (uint)length, Modified = modified, EntryOffset = slot };
        }

        public FatxEntry WriteFile(uint parentCluster, string localPath, string name = null, Action<long> progress = null, CancellationToken ct = default(CancellationToken))
        {
            using (var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                return WriteFile(parentCluster, name ?? Path.GetFileName(localPath), fs, fs.Length, File.GetLastWriteTime(localPath), progress, ct);
        }

        /// <summary>Copies a folder from this PC into a folder on the drive (created if missing), with everything inside it.</summary>
        public void WriteDirectory(uint parentCluster, string localDir, string name = null, Action<long> progress = null, CancellationToken ct = default(CancellationToken))
            => WriteDirectory(parentCluster, localDir, name ?? Path.GetFileName(localDir.TrimEnd('\\', '/')), progress, ct, 0);

        private void WriteDirectory(uint parentCluster, string localDir, string name, Action<long> progress, CancellationToken ct, int depth)
        {
            if (depth > MaxDepth) throw new IOException("Folders are nested too deeply: " + localDir);
            var dir = CreateDirectory(parentCluster, name);
            foreach (string f in Directory.GetFiles(localDir)) { ct.ThrowIfCancellationRequested(); WriteFile(dir.FirstCluster, f, null, progress, ct); }
            foreach (string d in Directory.GetDirectories(localDir)) WriteDirectory(dir.FirstCluster, d, Path.GetFileName(d), progress, ct, depth + 1);
        }

        /// <summary>Every name under a folder on this PC that an Xbox drive would refuse, so a copy can be stopped before it starts.</summary>
        public static List<string> NameProblemsIn(string localPath)
        {
            var list = new List<string>();
            void Look(string p, int depth)
            {
                string problem = NameProblem(Path.GetFileName(p.TrimEnd('\\', '/')));
                if (problem != null) list.Add(problem);
                if (depth > MaxDepth || !Directory.Exists(p)) return;
                foreach (string f in Directory.GetFiles(p)) { string q = NameProblem(Path.GetFileName(f)); if (q != null) list.Add(q); else if (new FileInfo(f).Length > uint.MaxValue) list.Add("\"" + Path.GetFileName(f) + "\" is larger than 4 GB."); }
                foreach (string d in Directory.GetDirectories(p)) Look(d, depth + 1);
            }
            Look(localPath, 0);
            return list;
        }

        public void Rename(uint parentCluster, FatxEntry entry, string newName)
        {
            RequireWritable();
            RequireName(newName);
            var clash = Find(parentCluster, newName);
            if (clash != null && clash.EntryOffset != entry.EntryOffset) throw new IOException("\"" + newName + "\" is already there.");
            var rec = Read(entry.EntryOffset, 64);
            if (rec[0] != entry.Name.Length || Encoding.ASCII.GetString(rec, 2, rec[0]) != entry.Name)
                throw new IOException("The folder changed since it was listed - refresh and try again.");
            rec[0] = (byte)newName.Length;
            for (int i = 0; i < 42; i++) rec[2 + i] = 0xFF;
            Encoding.ASCII.GetBytes(newName, 0, newName.Length, rec, 2);
            Write(entry.EntryOffset, rec, 0, 64);
            lock (_s) _s.Flush();
            entry.Name = newName;
        }

        /// <summary>Deletes a file, or a folder with everything in it.</summary>
        public void Delete(FatxEntry entry) => Delete(entry, 0);

        private void Delete(FatxEntry entry, int depth)
        {
            RequireWritable();
            if (depth > MaxDepth) throw new IOException("Folders are nested too deeply (damaged file system).");
            var rec = Read(entry.EntryOffset, 64);
            if (rec[0] != entry.Name.Length || Encoding.ASCII.GetString(rec, 2, rec[0]) != entry.Name)
                throw new IOException("The folder changed since it was listed - refresh and try again.");
            var chain = StrictChain(entry.FirstCluster, entry.Name);
            if (entry.IsDirectory)
                foreach (var child in ListDirectory(entry.FirstCluster)) Delete(child, depth + 1);
            Write(entry.EntryOffset, new byte[] { 0xE5 }, 0, 1);
            lock (_s) _s.Flush();
            FreeChain(chain);
        }

        // ------------------------------------------------------------------ paths

        /// <summary>The folder at a path like "Content\0000000000000000" (empty = the root); optionally created level by level.</summary>
        public uint ResolveDirectory(string path, bool create)
        {
            uint cluster = RootCluster;
            foreach (string part in (path ?? "").Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var e = Find(cluster, part);
                if (e == null)
                {
                    if (!create) throw new DirectoryNotFoundException("There is no folder \"" + part + "\" on the drive.");
                    e = CreateDirectory(cluster, part);
                }
                else if (!e.IsDirectory) throw new IOException("\"" + part + "\" is a file, not a folder.");
                cluster = e.FirstCluster;
            }
            return cluster;
        }

        /// <summary>The file or folder at a path, or null.</summary>
        public FatxEntry FindPath(string path)
        {
            var parts = (path ?? "").Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            uint cluster = RootCluster;
            for (int i = 0; i < parts.Length; i++)
            {
                var e = Find(cluster, parts[i]);
                if (e == null) return null;
                if (i == parts.Length - 1) return e;
                if (!e.IsDirectory) return null;
                cluster = e.FirstCluster;
            }
            return null;
        }

        // ------------------------------------------------------------------ check

        /// <summary>
        /// Walks every folder and file and compares it with the allocation table: chains that leave the volume, loop,
        /// are shared by two files or are the wrong length for the file's size, and clusters that belong to nothing.
        /// Read-only.
        /// </summary>
        public FatxCheck Check(CancellationToken ct = default(CancellationToken))
        {
            _fat = null;            // always from the image, never from memory
            LoadFat();
            var result = new FatxCheck();
            var owner = new Dictionary<uint, string>();

            List<uint> Claim(uint first, string what)
            {
                var list = new List<uint>();
                uint c = first;
                while (c != 0 && c < 0xFFFF0000)
                {
                    if (c > _lastCluster) { result.Problems.Add(what + ": chain leaves the volume at cluster " + c); break; }
                    string other;
                    if (owner.TryGetValue(c, out other)) { result.Problems.Add(what + ": cluster " + c + " is also used by " + other); break; }
                    owner[c] = what;
                    list.Add(c);
                    uint next = _fat[c];
                    if (next == 0) { result.Problems.Add(what + ": chain runs into a cluster marked free (" + c + ")"); break; }
                    c = next;
                }
                return list;
            }

            void Walk(uint cluster, string path, int depth)
            {
                ct.ThrowIfCancellationRequested();
                if (depth > MaxDepth) { result.Problems.Add(path + ": nested too deeply"); return; }
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in ListDirectory(cluster))
                {
                    string full = path + "\\" + e.Name;
                    if (!names.Add(e.Name)) result.Problems.Add(full + ": the name appears twice in its folder");
                    if (e.IsDirectory)
                    {
                        result.Folders++;
                        if (e.FirstCluster == 0) { result.Problems.Add(full + ": folder without a cluster"); continue; }
                        bool fresh = !owner.ContainsKey(e.FirstCluster);
                        Claim(e.FirstCluster, full);
                        if (fresh) Walk(e.FirstCluster, full, depth + 1);
                    }
                    else
                    {
                        result.Files++;
                        result.FileBytes += e.Size;
                        long need = (e.Size + ClusterSize - 1) / ClusterSize;
                        if (e.Size == 0 && e.FirstCluster == 0) continue;
                        var chain = Claim(e.FirstCluster, full);
                        if (chain.Count != need) result.Problems.Add(full + ": " + e.Size + " bytes need " + need + " cluster(s) but the chain has " + chain.Count);
                    }
                }
            }

            Claim(RootCluster, "(root folder)");
            Walk(RootCluster, "", 0);
            for (uint i = 1; i <= _lastCluster; i++)
            {
                if (_fat[i] == 0) result.FreeClusters++;
                else if (owner.ContainsKey(i)) result.UsedClusters++;
                else if (!(_fat[i] >= 0xFFFFFFF0 && _fat[i] <= 0xFFFFFFF7)) result.LostClusters++;     // reserved / bad-cluster marks are not "lost"
            }
            return result;
        }

        // ------------------------------------------------------------------ a new, empty volume inside a drive

        /// <summary>
        /// Formats an empty Xbox 360 (XTAF) volume into <paramref name="s"/> at <paramref name="offset"/> - a drive image, or a
        /// physical drive opened with changes allowed (then every block goes through the Disk Reader's journal). The whole
        /// table is written (zeros, then the media marker and the root's end marker) so nothing left over from before can
        /// look like a file; the root folder cluster is cleared. Layout as the console uses it: one table entry more than
        /// there are clusters, 4 KB aligned.
        /// </summary>
        public static void FormatInto(Stream s, long offset, long size, uint sectorsPerCluster, int entrySize, uint volumeId)
        {
            if (sectorsPerCluster == 0 || (sectorsPerCluster & (sectorsPerCluster - 1)) != 0) throw new ArgumentException("Sectors per cluster must be a power of two.");
            if (entrySize != 2 && entrySize != 4) throw new ArgumentException("A table entry is 2 or 4 bytes.");
            if (offset % 0x1000 != 0 || size % 0x1000 != 0) throw new ArgumentException("A volume starts and ends on 4 KB boundaries.");
            long clusterSize = sectorsPerCluster * 512L;
            long clusters = size / clusterSize, entries = clusters + 1;
            long fatBytes = (entries * entrySize + 0xFFF) & ~0xFFFL;
            if (0x1000 + fatBytes + clusterSize > size) throw new ArgumentException("Too small for a volume.");
            if (offset + size > s.Length) throw new ArgumentException("The volume would end past the end of the drive.");
            var head = new byte[0x1000];
            for (int i = 0; i < head.Length; i++) head[i] = 0xFF;
            Encoding.ASCII.GetBytes("XTAF", 0, 4, head, 0);
            void Put(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
            Put(head, 4, volumeId);
            Put(head, 8, sectorsPerCluster);
            Put(head, 12, 1);
            var fat = new byte[fatBytes];
            if (entrySize == 2) { fat[0] = 0xFF; fat[1] = 0xF8; fat[2] = 0xFF; fat[3] = 0xFF; }
            else { fat[0] = 0xFF; fat[1] = 0xFF; fat[2] = 0xFF; fat[3] = 0xF8; fat[4] = 0xFF; fat[5] = 0xFF; fat[6] = 0xFF; fat[7] = 0xFF; }
            var root = new byte[clusterSize];
            lock (s)
            {
                s.Position = offset; s.Write(head, 0, head.Length);
                s.Position = offset + 0x1000; s.Write(fat, 0, fat.Length);
                s.Position = offset + 0x1000 + fatBytes; s.Write(root, 0, root.Length);
                s.Flush();
            }
        }

        /// <summary>
        /// Creates the original-Xbox compatibility partition (HddX, 256 MB at 0x120EB0000) on an Xbox 360 drive that does not
        /// have one - formatted exactly like a console's own (checked against a real drive: "XTAF", id FFFFFFFF, 16 KB
        /// clusters, root at cluster 1, 4-byte table entries). Refused unless the drive has the Xbox 360 layout (its Data
        /// partition at 0x130EB0000) and nothing is at 0x120EB0000 yet.
        /// </summary>
        public static void CreateCompatibilityPartition(Stream s)
        {
            var m = new byte[4];
            lock (s)
            {
                s.Position = DataOffset360; if (ReadFull(s, m, 0, 4) != 4 || Encoding.ASCII.GetString(m) != "XTAF") throw new InvalidOperationException("This is not an Xbox 360 drive with the console's layout (no Data partition at 0x130EB0000).");
                s.Position = CompatibilityOffset; ReadFull(s, m, 0, 4);
            }
            string at = Encoding.ASCII.GetString(m);
            if (at == "XTAF" || at == "FATX") throw new InvalidOperationException("The compatibility partition is there already.");
            FormatInto(s, CompatibilityOffset, CompatibilitySize, 32, 4, 0xFFFFFFFF);
        }

        /// <summary>
        /// The folder whose contents belong in HddX\Compatibility, found from what the user picked: the Compatibility
        /// folder itself, a folder that holds one, or a folder that holds the emulator files directly. Throws when there
        /// is no emulator (xefu*.xex) in it - those files are Microsoft's and are never shipped with X360GameHack.
        /// </summary>
        public static string CompatibilitySource(string picked)
        {
            if (!Directory.Exists(picked)) throw new DirectoryNotFoundException("Not a folder: " + picked);
            string inner = Path.Combine(picked, "Compatibility");
            string src = Directory.Exists(inner) && !string.Equals(Path.GetFileName(picked.TrimEnd('\\')), "Compatibility", StringComparison.OrdinalIgnoreCase) ? inner : picked;
            if (!Directory.EnumerateFiles(src, "xefu*.xex", SearchOption.TopDirectoryOnly).Any())
                throw new InvalidOperationException("No original-Xbox emulator (xefu*.xex) is in " + src + ". Pick the Compatibility folder of a backward-compatibility pack you own.");
            return src;
        }

        /// <summary>Copies the emulator files into this volume's Compatibility folder (created if missing; files of the same name are replaced).</summary>
        public void InstallCompatibility(string sourceFolder, Action<long> progress = null, CancellationToken ct = default(CancellationToken))
        {
            RequireWritable();
            uint dir = ResolveDirectory("Compatibility", true);
            foreach (string f in Directory.GetFiles(sourceFolder)) { ct.ThrowIfCancellationRequested(); WriteFile(dir, f, null, progress, ct); }
            foreach (string d in Directory.GetDirectories(sourceFolder)) { ct.ThrowIfCancellationRequested(); WriteDirectory(dir, d, null, progress, ct); }
        }

        // ------------------------------------------------------------------ a new, empty volume (used by the tests)

        /// <summary>
        /// Writes an empty volume into a new file: Xbox 360 layout (big-endian "XTAF") or original Xbox ("FATX").
        /// An Xbox 360 table has one entry more than the volume has clusters, as the console counts it (checked on a
        /// real drive, see <see cref="FatxVolume"/>'s reader). <paramref name="entrySize"/> 4 forces 4-byte entries,
        /// as the console's own compatibility partition has them; 0 picks them by the cluster count.
        /// </summary>
        public static void CreateImage(string path, long sizeBytes, bool xbox360, uint sectorsPerCluster, int entrySize = 0)
        {
            if (sectorsPerCluster == 0 || (sectorsPerCluster & (sectorsPerCluster - 1)) != 0) throw new ArgumentException("Sectors per cluster must be a power of two.");
            if (entrySize != 0 && entrySize != 2 && entrySize != 4) throw new ArgumentException("A table entry is 2 or 4 bytes.");
            long clusterSize = sectorsPerCluster * 512L;
            if (sizeBytes < 0x1000 + 0x1000 + clusterSize * 4) throw new ArgumentException("Too small for a volume.");
            long clusters = sizeBytes / clusterSize;
            long entries = xbox360 ? clusters + 1 : clusters;
            int entry = entrySize != 0 ? entrySize : entries < 0xFFF0 ? 2 : 4;
            long fatBytes = (entries * entry + 0xFFF) & ~0xFFFL;
            using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.SetLength(sizeBytes);
                var head = new byte[0x1000];
                for (int i = 0; i < head.Length; i++) head[i] = 0xFF;
                Encoding.ASCII.GetBytes(xbox360 ? "XTAF" : "FATX", 0, 4, head, 0);
                void Put(int o, uint v)
                {
                    if (xbox360) { head[o] = (byte)(v >> 24); head[o + 1] = (byte)(v >> 16); head[o + 2] = (byte)(v >> 8); head[o + 3] = (byte)v; }
                    else { head[o] = (byte)v; head[o + 1] = (byte)(v >> 8); head[o + 2] = (byte)(v >> 16); head[o + 3] = (byte)(v >> 24); }
                }
                Put(4, 0x58333630);
                Put(8, sectorsPerCluster);
                Put(12, 1);
                fs.Write(head, 0, head.Length);
                // table: entry 0 is the media marker, entry 1 (the root folder) ends its own chain
                var fat = new byte[entry * 2];
                for (int i = 0; i < fat.Length; i++) fat[i] = 0xFF;
                if (entry == 2) { if (xbox360) { fat[0] = 0xFF; fat[1] = 0xF8; } else { fat[0] = 0xF8; fat[1] = 0xFF; } }
                else { if (xbox360) fat[3] = 0xF8; else fat[0] = 0xF8; }
                fs.Write(fat, 0, fat.Length);
                // the root folder: one cluster of end markers
                fs.Position = 0x1000 + fatBytes;
                var root = new byte[clusterSize];
                for (int i = 0; i < root.Length; i++) root[i] = 0xFF;
                fs.Write(root, 0, root.Length);
            }
        }
    }
}
