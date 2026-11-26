using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Formats
{
    /// <summary>How much of something deleted can still be got back.</summary>
    public enum RecoveryState
    {
        /// <summary>Every cluster it needs is free (or still its own chain): its bytes are as they were.</summary>
        Recoverable,
        /// <summary>Some of its clusters now belong to something else: those parts hold other data.</summary>
        PartlyOverwritten,
        /// <summary>Its start belongs to something else, or holds something else: nothing of it is left.</summary>
        Overwritten
    }

    /// <summary>Something deleted that a FATX / XTAF volume still holds.</summary>
    public sealed class FatxDeleted
    {
        /// <summary>The folder it was in, from the partition's root ("" = the root), "\" between folders.</summary>
        public string Path = "";
        public string Name = "";
        public bool IsDirectory;
        /// <summary>No folder record is left: clusters still marked used that no file owns, read as the chain the table links.</summary>
        public bool LostChain;
        public uint FirstCluster;
        public long Size;
        public DateTime Modified;
        public RecoveryState State;
        /// <summary>Clusters it needs, and how many of those now belong to something else.</summary>
        public int Clusters, ClustersInUse;
        /// <summary>Its clusters are still linked in the table (nothing freed them), so it is read by that chain, wherever the pieces lie.</summary>
        public bool ByChain;
        /// <summary>What its first bytes are ("Xbox 360 package (CON)", "Xbox 360 program (XEX)", ...), "" when unknown.</summary>
        public string Type = "";
        /// <summary>Why it is marked overwritten when its clusters are free (its first bytes do not fit its name).</summary>
        public string Note = "";
        /// <summary>Where its 64-byte record is in the image (0 for a lost chain).</summary>
        public long EntryOffset;
        /// <summary>A deleted folder's content as it was when the folder went.</summary>
        public List<FatxDeleted> Children = new List<FatxDeleted>();
        /// <summary>Clusters it was read around: in use before it was written (lost clusters, older files), so the allocator went around them too.</summary>
        public long Skipped;
        /// <summary>How many pieces it is read in (1 = in one piece).</summary>
        public int Pieces = 1;
        /// <summary>When it was made and last written, as the record keeps them (raw FAT times, for comparing).</summary>
        internal uint Made, Written;
        /// <summary>Where a freed file's clusters are taken to be.</summary>
        internal List<(uint Start, int Count)> Runs;

        public string FullPath => (Path.Length == 0 ? "" : Path + "\\") + Name;
        public override string ToString() => FullPath + "  (" + State + ", " + Size + " bytes)";
    }

    /// <summary>
    /// Recovery of deleted files - read only, nothing on the drive is ever changed. Deleting on FATX / XTAF marks the file's
    /// 64-byte record (its first byte becomes E5) and frees its clusters in the table; the name, size, first cluster and
    /// times stay in the record, and the data stays in the freed clusters until something else is written there. So:
    ///  - deleted records in every folder (and everything inside deleted folders) are read back;
    ///  - a file is read from its first cluster on, in one piece (the console writes files in one piece when it can; the
    ///    links between pieces are gone once freed); clusters now used by something else are counted, and a file whose
    ///    first bytes do not fit its name is marked overwritten;
    ///  - when nothing freed a file's clusters (they are still linked but nobody owns them), the chain is followed - pieces
    ///    anywhere come back too; such chains without any record left are listed as lost data, named by their first cluster.
    /// </summary>
    public sealed partial class FatxVolume
    {
        private const int MaxRecoverDepth = 24;
        private const int MaxDeletedFolderClusters = 8;
        /// <summary>Every free cluster of the volume as runs, in order (made once per search).</summary>
        private List<(uint Start, int Count)> _freeRuns = new List<(uint Start, int Count)>();

        private static int FirstRunEndingAfter<T>(List<T> runs, Func<T, long> end, uint pos)
        {
            int lo = 0, hi = runs.Count;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (end(runs[mid]) <= pos) lo = mid + 1; else hi = mid; }
            return lo;
        }

        /// <summary>
        /// The clusters a freed file had, taken the way the allocator hands them out: from its first cluster on, the free
        /// clusters in order, going around clusters that were in use when it was written - lost clusters and clusters of
        /// files made before it - however far the next free ones are (checked on a real drive: a 76-cluster file written in
        /// pieces at 189515, 189537 and 8,643,426). A cluster a file made after it owns now was its own: counted in
        /// <paramref name="over"/> (it holds that file's data now). Its first cluster can only have been its own.
        /// </summary>
        private List<(uint Start, int Count)> AllocatedLike(uint f, int need, uint made, out int over, out long skipped, out bool firstGone)
        {
            var list = new List<(uint Start, int Count)>();
            int got = 0;
            over = 0; skipped = 0; firstGone = _fat[f] != 0;
            void Add(uint c, int count)
            {
                if (list.Count > 0 && list[list.Count - 1].Start + (uint)list[list.Count - 1].Count == c) list[list.Count - 1] = (list[list.Count - 1].Start, list[list.Count - 1].Count + count);
                else list.Add((c, count));
                got += count;
            }
            if (firstGone) over++;
            Add(f, 1);
            uint pos = f + 1;
            bool Newer(OwnedRun r) => r.Made == 0 || made == 0 || r.Made >= made;
            int fi = FirstRunEndingAfter(_freeRuns, r => (long)r.Start + r.Count, pos);
            int oi = FirstRunEndingAfter(_ownedRuns, r => (long)r.Start + r.Count, pos);
            while (got < need)
            {
                while (oi < _ownedRuns.Count && (!Newer(_ownedRuns[oi]) || (long)_ownedRuns[oi].Start + _ownedRuns[oi].Count <= pos)) oi++;
                while (fi < _freeRuns.Count && (long)_freeRuns[fi].Start + _freeRuns[fi].Count <= pos) fi++;
                long fs = fi < _freeRuns.Count ? Math.Max(_freeRuns[fi].Start, pos) : long.MaxValue;
                long os = oi < _ownedRuns.Count ? Math.Max(_ownedRuns[oi].Start, pos) : long.MaxValue;
                if (fs == long.MaxValue && os == long.MaxValue) break;                       // past the volume's end
                bool fromFree = fs <= os;
                long start = fromFree ? fs : os;
                long end = fromFree ? (long)_freeRuns[fi].Start + _freeRuns[fi].Count : (long)_ownedRuns[oi].Start + _ownedRuns[oi].Count;
                int take = (int)Math.Min(end - start, need - got);
                skipped += start - pos;
                if (!fromFree) over += take;
                Add((uint)start, take);
                pos = (uint)(start + take);
            }
            if (got < need) over += need - got;
            return list;
        }

        /// <summary>
        /// Two deleted items can claim the same free cluster: one was deleted, a newer file took its space and was deleted too.
        /// The newer one holds what is there now; the older one has lost those clusters (checked on a real drive: a file the
        /// console deleted, whose space a later folder used).
        /// </summary>
        private void SettleOverlaps(List<FatxDeleted> found)
        {
            var items = Flatten(found).Where(x => !x.LostChain && !x.ByChain && x.Runs != null && x.State != RecoveryState.Overwritten).ToList();
            if (items.Count < 2) return;
            var holder = new Dictionary<uint, FatxDeleted>();
            foreach (var x in items.OrderByDescending(x => Math.Max(x.Made, x.Written)))
            {
                int lostHere = 0;
                bool firstLost = false, first = true;
                FatxDeleted by = null;
                foreach (var (start, count) in x.Runs)
                    for (uint c = start; c < start + (uint)count; c++)
                    {
                        if (holder.TryGetValue(c, out var newer)) { lostHere++; if (by == null) by = newer; if (first) firstLost = true; }
                        else holder[c] = x;
                        first = false;
                    }
                if (lostHere == 0) continue;
                x.ClustersInUse += lostHere;
                x.State = firstLost ? RecoveryState.Overwritten : RecoveryState.PartlyOverwritten;
                x.Note = "a newer file, deleted too (" + by.FullPath + "), used " + (firstLost ? "its space" : lostHere + " of its clusters");
            }
        }

        private bool IsReservedMark(uint v) => v >= 0xFFFFFFF0 && v <= 0xFFFFFFF7;

        /// <summary>A chain as the table in memory links it (no reads from the drive).</summary>
        private List<uint> TableChain(uint first, int max)
        {
            var list = new List<uint>();
            var seen = new HashSet<uint>();
            for (uint c = first; c >= 1 && c <= _lastCluster && list.Count < max && seen.Add(c); c = _fat[c])
            {
                list.Add(c);
                if (_fat[c] == 0 || _fat[c] >= 0xFFFFFFF0) break;
            }
            return list;
        }

        /// <summary>A cluster still marked used that nothing owns and no deleted file has taken yet: its data was never freed.</summary>
        private bool Unfreed(uint c, bool[] owned, bool[] claimed) => c >= 1 && c <= _lastCluster && _fat[c] != 0 && !owned[c] && !claimed[c] && !IsReservedMark(_fat[c]);

        /// <summary>A run of clusters a file that is still there owns, with when that file was made (raw FAT time).</summary>
        private struct OwnedRun { public uint Start, Count, Made; }
        private List<OwnedRun> _ownedRuns = new List<OwnedRun>();

        /// <summary>When the file that owns <paramref name="c"/> now was made (raw FAT time); false when no file owns it.</summary>
        private bool OwnerMade(uint c, out uint made)
        {
            int lo = 0, hi = _ownedRuns.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var r = _ownedRuns[mid];
                if (c < r.Start) hi = mid - 1;
                else if (c >= r.Start + r.Count) lo = mid + 1;
                else { made = r.Made; return true; }
            }
            made = 0;
            return false;
        }

        /// <summary>
        /// Everything deleted this volume still holds, with how much of each can be got back. Read only.
        /// <paramref name="lostChains"/> also lists clusters marked used that no file owns (no name left).
        /// </summary>
        public List<FatxDeleted> FindDeleted(bool lostChains, CancellationToken ct = default(CancellationToken))
        {
            _fat = null;                      // always the table as it is on the drive now
            LoadFat();
            var owned = new bool[_lastCluster + 1];
            var runs = new List<OwnedRun>();
            var pending = new List<(FatxDeleted X, int Depth)>();
            var folders = new HashSet<uint>();

            // what files that are still there own, and when each was made
            void Claim(uint first, uint made)
            {
                uint start = 0, count = 0, prev = 0;
                for (uint c = first; c >= 1 && c <= _lastCluster && !owned[c]; c = _fat[c])
                {
                    owned[c] = true;
                    if (count > 0 && c == prev + 1) count++;
                    else { if (count > 0) runs.Add(new OwnedRun { Start = start, Count = count, Made = made }); start = c; count = 1; }
                    prev = c;
                    if (_fat[c] == 0 || _fat[c] >= 0xFFFFFFF0) break;
                }
                if (count > 0) runs.Add(new OwnedRun { Start = start, Count = count, Made = made });
            }

            // one walk of the tree: every live record's clusters, and every deleted record (judged once all is known)
            void Walk(uint cluster, string path, int depth)
            {
                ct.ThrowIfCancellationRequested();
                if (depth > MaxDepth || !folders.Add(cluster)) return;
                foreach (uint c in TableChain(cluster, 4096))
                {
                    var data = Read(ClusterOffset(c), (int)ClusterSize);
                    for (int o = 0; o + 64 <= data.Length; o += 64)
                    {
                        byte n = data[o];
                        if (n == 0x00 || n == 0xFF) return;                      // the folder's end
                        if (n == 0xE5)
                        {
                            var x = ParseRecord(data, o, path, true);
                            if (x != null) { x.EntryOffset = ClusterOffset(c) + o; pending.Add((x, depth + 1)); }
                            continue;
                        }
                        if (n > 42) continue;
                        uint first = U32(data, o + 0x2C);
                        string name = Encoding.ASCII.GetString(data, o + 2, n);
                        if (name == "." || name == ".." || first < 1 || first > _lastCluster) continue;
                        Claim(first, U32(data, o + 0x34));
                        if ((data[o + 1] & 0x10) != 0) Walk(first, path.Length == 0 ? name : path + "\\" + name, depth + 1);
                    }
                }
            }
            Claim(RootCluster, 0);
            Walk(RootCluster, "", 0);
            runs.Sort((p, q) => p.Start.CompareTo(q.Start));
            _ownedRuns = runs;
            var free = new List<(uint Start, int Count)>();
            for (uint c = 1; c <= _lastCluster; c++)
            {
                if (_fat[c] != 0) continue;
                if (free.Count > 0 && free[free.Count - 1].Start + (uint)free[free.Count - 1].Count == c) free[free.Count - 1] = (free[free.Count - 1].Start, free[free.Count - 1].Count + 1);
                else free.Add((c, 1));
            }
            _freeRuns = free;

            var claimed = new bool[_lastCluster + 1];        // lost clusters already given to a deleted file (read by its chain)
            var found = new List<FatxDeleted>();
            foreach (var (x, depth) in pending)
            {
                ct.ThrowIfCancellationRequested();
                Assess(x, owned, claimed, depth);
                found.Add(x);
            }
            SettleOverlaps(found);

            if (lostChains)
            {
                var lost = new bool[_lastCluster + 1];
                for (uint c = 1; c <= _lastCluster; c++)
                    if (_fat[c] != 0 && !owned[c] && !claimed[c] && !IsReservedMark(_fat[c])) lost[c] = true;
                var pointedAt = new bool[_lastCluster + 1];
                for (uint c = 1; c <= _lastCluster; c++)
                    if (lost[c] && _fat[c] >= 1 && _fat[c] <= _lastCluster && lost[_fat[c]]) pointedAt[_fat[c]] = true;
                var taken = new bool[_lastCluster + 1];
                var chains = new List<(uint First, int Count, bool Single)>();
                // heads first (nothing links to them), then whatever is left (chains that loop)
                for (int pass = 0; pass < 2; pass++)
                    for (uint c = 1; c <= _lastCluster; c++)
                    {
                        if (!lost[c] || taken[c] || (pass == 0 && pointedAt[c])) continue;
                        ct.ThrowIfCancellationRequested();
                        int count = 0;
                        for (uint k = c; k >= 1 && k <= _lastCluster && lost[k] && !taken[k]; k = _fat[k])
                        {
                            taken[k] = true;
                            count++;
                            if (_fat[k] >= 0xFFFFFFF0) break;
                        }
                        chains.Add((c, count, count == 1 && _fat[c] >= 0xFFFFFFF8));
                    }
                // Single clusters marked used one by one, side by side (checked on a real drive: 66,536 such clusters in 7
                // runs, nearly all zero) are one run of space, not thousands of files: they are listed as one item each run.
                chains.Sort((p, q) => p.First.CompareTo(q.First));
                for (int i = 0; i < chains.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var ch = chains[i];
                    bool run = false;
                    if (ch.Single)
                    {
                        int j = i;
                        while (j + 1 < chains.Count && chains[j + 1].Single && chains[j + 1].First == chains[j].First + 1) j++;
                        if (j > i) { ch = (ch.First, j - i + 1, true); run = true; i = j; }
                    }
                    var x = new FatxDeleted
                    {
                        LostChain = true, Path = "", FirstCluster = ch.First, Clusters = ch.Count, Size = ch.Count * ClusterSize,
                        ByChain = !run, State = RecoveryState.Recoverable, Modified = DateTime.MinValue,
                        Note = run ? ch.Count + " clusters side by side, each marked used on its own (no links between them)" : ""
                    };
                    x.Type = LooksEmpty(x) ? EmptyType : SniffType(Read(ClusterOffset(ch.First), 16));
                    x.Name = "lost-" + (run ? "clusters-" + ch.First + "-" + (ch.First + ch.Count - 1) : "cluster-" + ch.First) + ExtensionFor(x.Type);
                    found.Add(x);
                }
            }
            return found;
        }

        /// <summary>What a lost item that holds nothing but zeros (where it was looked at) is called.</summary>
        public const string EmptyType = "looks empty (zeros)";

        /// <summary>True when the first, middle and last cluster of a lost item are all zero (only those are read).</summary>
        private bool LooksEmpty(FatxDeleted x)
        {
            var at = new List<uint> { x.FirstCluster };
            if (x.ByChain)
            {
                var chain = TableChain(x.FirstCluster, x.Clusters);
                if (chain.Count > 2) at.Add(chain[chain.Count / 2]);
                if (chain.Count > 1) at.Add(chain[chain.Count - 1]);
            }
            else
            {
                if (x.Clusters > 2) at.Add((uint)(x.FirstCluster + x.Clusters / 2));
                if (x.Clusters > 1) at.Add((uint)(x.FirstCluster + x.Clusters - 1));
            }
            foreach (uint c in at)
            {
                var d = Read(ClusterOffset(c), (int)ClusterSize);
                for (int i = 0; i < d.Length; i++) if (d[i] != 0) return false;
            }
            return true;
        }

        /// <summary>
        /// A record as it was before it was deleted, or null when it is not a believable record. In a folder that is still
        /// there only deleted records count (<paramref name="onlyDeleted"/>); in a deleted folder every record does.
        /// </summary>
        private FatxDeleted ParseRecord(byte[] d, int o, string path, bool onlyDeleted)
        {
            byte n = d[o];
            byte attr = d[o + 1];
            if ((attr & 0xC8) != 0) return null;                          // no volume-label or unknown bits on a real record
            int len;
            if (n == 0xE5)
            {
                // the length byte became E5; the name itself is still there, padded with FF
                len = 0;
                while (len < 42 && d[o + 2 + len] != 0xFF && d[o + 2 + len] != 0x00) len++;
            }
            else if (n >= 1 && n <= 42 && !onlyDeleted) len = n;
            else return null;
            if (len == 0) return null;
            for (int i = 0; i < len; i++) { byte ch = d[o + 2 + i]; if (ch < 0x20 || ch > 0x7E) return null; }
            string name = Encoding.ASCII.GetString(d, o + 2, len);
            if (name == "." || name == "..") return null;
            uint first = U32(d, o + 0x2C), size = U32(d, o + 0x30);
            bool dir = (attr & 0x10) != 0;
            if (dir ? first < 1 || first > _lastCluster : (size > 0 && (first < 1 || first > _lastCluster))) return null;
            if (!dir && size > (long)_lastCluster * ClusterSize) return null;
            return new FatxDeleted
            {
                Path = path, Name = name, IsDirectory = dir, FirstCluster = first, Size = dir ? 0 : size,
                Modified = Shown(FatTime(U32(d, o + 0x38), BigEndian ? 1980 : 2000)),
                Made = U32(d, o + 0x34), Written = U32(d, o + 0x38)
            };
        }

        /// <summary>How much of <paramref name="x"/> is left; a deleted folder's content is read too.</summary>
        private void Assess(FatxDeleted x, bool[] owned, bool[] claimed, int depth)
        {
            x.Clusters = x.IsDirectory ? 1 : (int)((x.Size + ClusterSize - 1) / ClusterSize);
            if (x.Clusters == 0) { x.State = RecoveryState.Recoverable; return; }       // an empty file: nothing to read

            uint f = x.FirstCluster;
            // a deleted folder whose cluster nothing freed: its content is still there
            if (x.IsDirectory && Unfreed(f, owned, claimed))
            {
                claimed[f] = true;
                x.ByChain = true;
                x.State = RecoveryState.Recoverable;
                ReadDeletedFolder(x, owned, claimed, depth);
                return;
            }
            // nothing freed its clusters: still linked, owned by no file - follow that chain
            if (!x.IsDirectory && Unfreed(f, owned, claimed))
            {
                var chain = new List<uint>();
                var seen = new HashSet<uint>();
                for (uint c = f; c >= 1 && c <= _lastCluster && !owned[c] && !claimed[c] && seen.Add(c) && chain.Count < x.Clusters; c = _fat[c])
                {
                    chain.Add(c);
                    if (_fat[c] == 0 || _fat[c] >= 0xFFFFFFF0) break;
                }
                if (chain.Count == x.Clusters)
                {
                    foreach (uint c in chain) claimed[c] = true;
                    x.ByChain = true;
                    x.State = RecoveryState.Recoverable;
                    x.Type = SniffType(Read(ClusterOffset(f), 16));
                    return;
                }
            }

            var taken = AllocatedLike(f, x.Clusters, x.Made, out int over, out long skipped, out bool firstGone);
            x.Runs = taken;
            x.Pieces = taken.Count;
            x.ClustersInUse = over;
            x.Skipped = skipped;
            x.State = firstGone ? RecoveryState.Overwritten : over > 0 ? RecoveryState.PartlyOverwritten : RecoveryState.Recoverable;
            if (firstGone) return;

            if (x.IsDirectory)
            {
                ReadDeletedFolder(x, owned, claimed, depth);
                return;
            }
            x.Type = SniffType(Read(ClusterOffset(f), 16));
            string mismatch = Mismatch(x.Name, x.Type);
            if (mismatch != null) { x.State = RecoveryState.Overwritten; x.Note = mismatch; }
        }

        /// <summary>The records a deleted folder's cluster still holds - everything that was in it.</summary>
        private void ReadDeletedFolder(FatxDeleted folder, bool[] owned, bool[] claimed, int depth)
        {
            if (depth > MaxRecoverDepth) { folder.Note = "nested too deeply to follow"; return; }
            // a folder whose chain is still linked is read by it; a freed one in one piece from its first cluster
            var clusters = folder.ByChain ? TableChain(folder.FirstCluster, MaxDeletedFolderClusters)
                                          : Enumerable.Range(0, MaxDeletedFolderClusters).Select(i => (uint)(folder.FirstCluster + i)).ToList();
            for (int k = 0; k < clusters.Count; k++)
            {
                uint c = clusters[k];
                if (c > _lastCluster || (k > 0 && !folder.ByChain && (_fat[c] != 0 || owned[c]))) return;
                var data = Read(ClusterOffset(c), (int)ClusterSize);
                for (int o = 0; o + 64 <= data.Length; o += 64)
                {
                    byte n = data[o];
                    if (n == 0x00 || n == 0xFF) return;                 // the folder's end
                    var x = ParseRecord(data, o, folder.FullPath, false);
                    if (x == null)
                    {
                        // not a folder record: the cluster holds something else now
                        if (o == 0 && k == 0) { folder.State = RecoveryState.Overwritten; folder.Note = "its folder cluster holds something else now"; }
                        return;
                    }
                    x.EntryOffset = ClusterOffset(c) + o;
                    Assess(x, owned, claimed, depth + 1);
                    folder.Children.Add(x);
                }
                // a full cluster without the end: the folder went on in the next cluster (taken as the next one on the drive)
            }
        }

        /// <summary>Why a file's first bytes do not fit its name (it was overwritten and freed again), or null.</summary>
        internal static string Mismatch(string name, string type)
        {
            string ext = System.IO.Path.GetExtension(name ?? "").ToLowerInvariant();
            bool want(string what) => type.IndexOf(what, StringComparison.Ordinal) >= 0;
            switch (ext)
            {
                case ".xex": return want("XEX") ? null : "named .xex but its first bytes are not an XEX";
                case ".xbe": return want("XBE") ? null : "named .xbe but its first bytes are not an XBE";
                case ".png": return want("PNG") ? null : "named .png but its first bytes are not a PNG";
                case ".gpd": return want("GPD") ? null : "named .gpd but its first bytes are not a GPD";
                default: return null;
            }
        }

        /// <summary>What a file's first bytes say it is ("" when they say nothing known).</summary>
        public static string SniffType(byte[] b)
        {
            if (b == null || b.Length < 4) return "";
            string m4 = Encoding.ASCII.GetString(b, 0, 4);
            switch (m4)
            {
                case "CON ": return "Xbox 360 package (CON)";
                case "LIVE": return "Xbox 360 package (LIVE)";
                case "PIRS": return "Xbox 360 package (PIRS)";
                case "XEX2": return "Xbox 360 program (XEX)";
                case "XEX1": case "XEX%": case "XEX-": case "XEX?": return "Xbox 360 program (early XEX)";
                case "XBEH": return "Xbox program (XBE)";
                case "XDBF": return "Xbox game data (GPD)";
                case "XUIZ": return "Xbox 360 interface package (XZP)";
                case "XTAF": return "Xbox 360 drive or partition image";
                case "FATX": return "Xbox drive or partition image";
                case "RIFF": return "RIFF media (WAV / AVI)";
            }
            if (b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G') return "PNG picture";
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "JPEG picture";
            if (b[0] == 'P' && b[1] == 'K' && b[2] == 3 && b[3] == 4) return "ZIP archive";
            if (b[0] == 'M' && b[1] == 'Z') return "Windows program";
            if (b[0] == 'D' && b[1] == 'D' && b[2] == 'S' && b[3] == ' ') return "DDS texture";
            if (m4 == "<?xm") return "XML text";
            return "";
        }

        private static string ExtensionFor(string type)
        {
            if (type.Contains("XEX")) return ".xex";
            if (type.Contains("XBE")) return ".xbe";
            if (type.Contains("GPD")) return ".gpd";
            if (type.Contains("PNG")) return ".png";
            if (type.Contains("JPEG")) return ".jpg";
            if (type.Contains("ZIP")) return ".zip";
            if (type.Contains("XZP")) return ".xzp";
            if (type.Contains("DDS")) return ".dds";
            if (type.Contains("XML")) return ".xml";
            if (type.Contains("Windows program")) return ".exe";
            return ".bin";
        }

        /// <summary>
        /// Writes something deleted to this PC (a folder with everything that was in it) - never to the drive. A partly
        /// overwritten file is written whole (the overwritten parts hold other data); an overwritten one is skipped.
        /// Returns how many files were written. <paramref name="localPath"/> must not exist yet.
        /// </summary>
        public int Recover(FatxDeleted x, string localPath, Action<long> progress, CancellationToken ct)
        {
            if (_fat == null) LoadFat();
            if (x.IsDirectory)
            {
                Directory.CreateDirectory(localPath);
                int files = 0;
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var child in x.Children)
                {
                    if (child.State == RecoveryState.Overwritten && !child.IsDirectory) continue;
                    files += Recover(child, UniquePath(localPath, child.Name, used), progress, ct);
                }
                TrySetTime(localPath, x.Modified, true);
                return files;
            }
            if (x.State == RecoveryState.Overwritten) return 0;
            if (File.Exists(localPath) || Directory.Exists(localPath)) throw new IOException("Already there: " + localPath);
            var clusters = new List<uint>();
            if (x.ByChain)
            {
                var seen = new HashSet<uint>();
                for (uint c = x.FirstCluster; c >= 1 && c <= _lastCluster && seen.Add(c) && clusters.Count < x.Clusters; c = _fat[c])
                {
                    clusters.Add(c);
                    if (_fat[c] == 0 || _fat[c] >= 0xFFFFFFF0) break;
                }
            }
            else if (x.Runs != null) { foreach (var (start, count) in x.Runs) for (int i = 0; i < count; i++) clusters.Add((uint)(start + i)); }
            else for (long i = 0; i < x.Clusters; i++) clusters.Add((uint)(x.FirstCluster + i));
            using (var o = new FileStream(localPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
            {
                long remaining = x.Size;
                foreach (uint c in clusters)
                {
                    if (remaining <= 0) break;
                    ct.ThrowIfCancellationRequested();
                    int n = (int)Math.Min(ClusterSize, remaining);
                    var d = c >= 1 && c <= _lastCluster ? Read(ClusterOffset(c), n) : new byte[n];
                    o.Write(d, 0, n);
                    remaining -= n;
                    progress?.Invoke(n);
                }
            }
            TrySetTime(localPath, x.Modified, false);
            return 1;
        }

        /// <summary>
        /// Recovers <paramref name="items"/> into <paramref name="to"/> on this PC, each into the folders it was in (lost data
        /// into "(lost data)"); an item inside a deleted folder that is in the list too comes with that folder. Never writes to
        /// the drive. Returns how many files were written; <paramref name="written"/> gets every item's path.
        /// </summary>
        public int RecoverInto(IList<FatxDeleted> items, string to, Action<long> progress, CancellationToken ct, List<string> written = null)
        {
            var inFolders = new HashSet<FatxDeleted>();
            void Mark(FatxDeleted f) { foreach (var c in f.Children) { inFolders.Add(c); Mark(c); } }
            foreach (var x in items.Where(i => i.IsDirectory)) Mark(x);
            Directory.CreateDirectory(to);
            var used = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            int files = 0;
            foreach (var x in items)
            {
                if (inFolders.Contains(x)) continue;
                ct.ThrowIfCancellationRequested();
                string folder = to;
                foreach (var part in (x.LostChain ? "(lost data)" : x.Path).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
                    folder = PathSafety.CombineUnder(folder, PathSafety.SafeName(part));
                Directory.CreateDirectory(folder);
                if (!used.TryGetValue(folder, out var names)) used[folder] = names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string target = UniquePath(folder, x.Name, names);
                files += Recover(x, target, progress, ct);
                written?.Add(target);
            }
            return files;
        }

        /// <summary>Every item and everything inside deleted folders, as one list.</summary>
        public static IEnumerable<FatxDeleted> Flatten(IEnumerable<FatxDeleted> xs)
        {
            foreach (var x in xs) { yield return x; foreach (var y in Flatten(x.Children)) yield return y; }
        }

        /// <summary>A name in <paramref name="folder"/> that is not taken yet ("name (2).ext" after the first).</summary>
        public static string UniquePath(string folder, string name, HashSet<string> used)
        {
            string safe = PathSafety.SafeName(name);
            string stem = System.IO.Path.GetFileNameWithoutExtension(safe), ext = System.IO.Path.GetExtension(safe);
            string candidate = safe;
            for (int i = 2; used.Contains(candidate) || File.Exists(System.IO.Path.Combine(folder, candidate)) || Directory.Exists(System.IO.Path.Combine(folder, candidate)); i++)
                candidate = stem + " (" + i + ")" + ext;
            used.Add(candidate);
            return PathSafety.CombineUnder(folder, candidate);
        }

        private static void TrySetTime(string path, DateTime t, bool folder)
        {
            if (t == DateTime.MinValue) return;
            try { if (folder) Directory.SetLastWriteTime(path, t); else File.SetLastWriteTime(path, t); } catch { }
        }
    }
}
