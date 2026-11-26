using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Formats
{
    public sealed class StfsEntry
    {
        public string Name;
        public string Path;          // folder\name
        public bool IsDirectory;
        public bool Consecutive;
        public int StartBlock;
        public int BlockCount;
        public uint Size;
        public short ParentIndex;
        public int Index;
    }

    /// <summary>
    /// STFS (CON / LIVE / PIRS) file system reader. Block layout is proven by the package's own SHA-1 hash
    /// tables: every data block read can be checked against its stored hash (see <see cref="VerifyAll"/>).
    /// </summary>
    /// <summary>
    /// Where an STFS volume keeps its descriptor and its header hash. Two containers use the file system:
    ///   a package (CON / LIVE / PIRS): descriptor at 0x379, header hash at 0x32C over 0x344..end of header
    ///   a profile's nested "PEC" file (avatar awards): a 0x228-byte console certificate + signature, the header hash
    ///   at 0x228 over 0x23C..0x1000, the descriptor at 0x244, the profile ID at 0x26C, data from 0x1000.
    /// (The PEC numbers were confirmed on a console-written profile: SHA-1 of 0x23C..0x1000 is the hash stored at 0x228.)
    /// </summary>
    internal struct StfsLayout
    {
        public bool IsPec;
        public int Descriptor, HashAt, HashFrom;
        public long Base;

        public static StfsLayout Detect(byte[] head)
        {
            if (head == null || head.Length < 0x400) throw new InvalidDataException("The file is too small to be an STFS package.");
            string magic = Encoding.ASCII.GetString(head, 0, 4);
            if (magic == "CON " || magic == "LIVE" || magic == "PIRS")
            {
                if (head[0x379] != 0x24) throw new InvalidDataException("This package does not use the STFS file system.");
                long b = ((long)BE.U32(head, 0x340) + 0xFFF) & ~0xFFFL;
                if (b <= 0x344 || b > 0x100000) throw new InvalidDataException("Malformed package header (bad header size).");
                return new StfsLayout { Descriptor = 0x379, HashAt = 0x32C, HashFrom = 0x344, Base = b };
            }
            // a PEC starts with the console certificate (its length field, 0x01A8) and has its descriptor at 0x244
            if (head[0] == 0x01 && head[1] == 0xA8 && head[0x244] == 0x24)
                return new StfsLayout { IsPec = true, Descriptor = 0x244, HashAt = 0x228, HashFrom = 0x23C, Base = 0x1000 };
            throw new InvalidDataException("Not an STFS package.");
        }
    }

    public sealed class StfsVolume : IDisposable
    {
        private const int BlockSize = 0x1000;
        private const int HashesPerTable = 0xAA;
        private readonly Stream _fs;
        private readonly byte[] _head;
        public string Path { get; }
        public long BaseOffset { get; }          // header size aligned to 4 KB
        internal StfsLayout Layout { get; }
        /// <summary>True for a profile's nested "PEC" container (avatar awards) rather than a CON / LIVE / PIRS package.</summary>
        public bool IsPec => Layout.IsPec;
        public int Shift { get; private set; }    // 0 = one hash table per level ("read-only" packages), 1 = two (CON)
        public int RootActive { get; }            // which copy of the top hash table the console reads (descriptor flag)
        private byte[] _top;                      // that top table (two-level packages), read on demand
        public int FileTableBlock { get; }
        public int FileTableBlockCount { get; }
        public int TotalBlocks { get; }
        public List<StfsEntry> Entries { get; } = new List<StfsEntry>();

        private StfsVolume(string path, byte[] image, int? forcedShift)
        {
            Path = path;
            _fs = image != null ? (Stream)new MemoryStream(image, false) : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            _head = new byte[0x971A];
            _fs.Read(_head, 0, _head.Length);
            try { Layout = StfsLayout.Detect(_head); }
            catch { _fs.Dispose(); throw; }
            int d = Layout.Descriptor;
            BaseOffset = Layout.Base;
            byte sep = _head[d + 2];
            FileTableBlockCount = _head[d + 3] | _head[d + 4] << 8;
            FileTableBlock = _head[d + 5] | _head[d + 6] << 8 | _head[d + 7] << 16;
            TotalBlocks = (int)BE.U32(_head, d + 0x1C);
            Shift = forcedShift ?? ((sep & 1) == 1 ? 0 : 1);
            RootActive = (sep >> 1) & 1;
        }

        public static StfsVolume Open(string path) => Open(path, null);

        /// <summary>Opens a volume held in memory (a PEC read out of a profile); nothing is written anywhere.</summary>
        public static StfsVolume Open(byte[] image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            return Open(null, image);
        }

        private static StfsVolume Open(string path, byte[] image)
        {
            // The block-separation flag decides the layout; confirm it against the file-table block hash and
            // fall back to the other layout if it does not verify.
            foreach (int? shift in new int?[] { null, 0, 1 })
            {
                var v = new StfsVolume(path, image, shift);
                if (v.BlockHashMatches(v.FileTableBlock)) { v.ReadFileTable(); return v; }
                v.Dispose();
            }
            throw new InvalidDataException("The package's file table does not match its hash table (corrupt or unsupported).");
        }

        // ------------------------------------------------------------- block math (free60 STFS layout)

        private long BlockOffset(long backingBlock) => BaseOffset + backingBlock * BlockSize;

        public long DataBlockToBacking(int block)
        {
            long b = block;
            long ret = ((b + 0xAA) / 0xAA << Shift) + b;
            if (b >= 0xAA)
            {
                ret += (b + 0x70E4) / 0x70E4 << Shift;
                if (b >= 0x70E4) ret += (b + 0x4AF768) / 0x4AF768 << Shift;
            }
            return ret;
        }

        /// <summary>Backing block of the level-0 hash table that covers <paramref name="block"/>.</summary>
        private long Level0TableBacking(int block)
        {
            long b = block;
            long step0 = 0xAA + (1L << Shift);                // data + its table(s) per level-0 group
            long step1 = 0xAA * step0 + (1L << Shift);        // one level-1 group
            long group0 = b / 0xAA, group1 = b / 0x70E4;
            long ret = group0 * step0;
            if (b >= 0xAA) ret += (group1 + 1) << Shift;      // level-1 table(s) in front of each level-1 group
            if (b >= 0x70E4) ret += (b / 0x4AF768 + 1) << Shift;
            return ret;
        }

        private byte[] ReadAt(long offset, int count)
        {
            var b = new byte[count];
            lock (_fs)
            {
                _fs.Position = offset;
                int n = 0;
                while (n < count) { int r = _fs.Read(b, n, count - n); if (r <= 0) break; n += r; }
            }
            return b;
        }

        /// <summary>
        /// The copy (0 / 1) of a level-0 table the console reads, per the active-index flags: the descriptor flag for
        /// one-level packages, bit 0x40 of the level-1 entry otherwise. -1 when unknown (three-level packages).
        /// </summary>
        private int ActiveCopy(int group)
        {
            if (Shift == 0) return 0;
            if (!HasLevel1) return RootActive;
            if (TotalBlocks > 0x70E4) return -1;
            if (_top == null) _top = ReadAt(BlockOffset(TopTableBacking + RootActive), BlockSize);
            return (_top[group * 0x18 + 0x14] >> 6) & 1;
        }

        /// <summary>
        /// Hash entry (20-byte SHA-1, status, next block) for a data block. The copy the console reads is tried first;
        /// <paramref name="strict"/> = only that copy (what a console accepts), otherwise the other copy is a fallback.
        /// </summary>
        private bool HashEntry(int block, byte[] data, out int nextBlock, bool strict = false)
        {
            nextBlock = -1;
            long table = Level0TableBacking(block);
            int active = ActiveCopy(block / HashesPerTable);
            int[] order = active < 0 ? new[] { 0, 1 } : Order(active, strict);
            using (var sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(data);
                foreach (int c in order)
                {
                    var e = ReadAt(BlockOffset(table + c) + (block % HashesPerTable) * 0x18, 0x18);
                    bool ok = true;
                    for (int i = 0; i < 20 && ok; i++) ok = e[i] == h[i];
                    if (ok) { nextBlock = e[0x15] << 16 | e[0x16] << 8 | e[0x17]; return true; }
                }
            }
            return false;
        }

        private bool BlockHashMatches(int block)
        {
            if (block < 0 || block > 0xFFFFFF) return false;
            long off = BlockOffset(DataBlockToBacking(block));
            if (off + BlockSize > _fs.Length) return false;
            return HashEntry(block, ReadAt(off, BlockSize), out _);
        }

        public byte[] ReadBlock(int block, bool verify, out int next)
        {
            var data = ReadAt(BlockOffset(DataBlockToBacking(block)), BlockSize);
            if (!HashEntry(block, data, out next) && verify)
                throw new InvalidDataException("Block " + block + " does not match its hash (package is corrupt or was edited without rehashing).");
            return data;
        }

        // ------------------------------------------------------------- file table

        private void ReadFileTable()
        {
            int block = FileTableBlock;
            int index = 0;
            for (int t = 0; t < FileTableBlockCount && block >= 0 && block < 0xFFFFFF; t++)
            {
                var data = ReadBlock(block, true, out int next);
                for (int o = 0; o < BlockSize; o += 0x40)
                {
                    byte flags = data[o + 0x28];
                    int len = flags & 0x3F;
                    if (len == 0) { index++; continue; }
                    var e = new StfsEntry
                    {
                        Index = index++,
                        Name = Encoding.ASCII.GetString(data, o, Math.Min(len, 0x28)),
                        IsDirectory = (flags & 0x80) != 0,
                        Consecutive = (flags & 0x40) != 0,
                        BlockCount = data[o + 0x29] | data[o + 0x2A] << 8 | data[o + 0x2B] << 16,
                        StartBlock = data[o + 0x2F] | data[o + 0x30] << 8 | data[o + 0x31] << 16,
                        ParentIndex = (short)BE.U16(data, o + 0x32),
                        Size = BE.U32(data, o + 0x34)
                    };
                    Entries.Add(e);
                }
                block = next;
            }
            foreach (var e in Entries) e.Path = BuildPath(e, 0);
        }

        private string BuildPath(StfsEntry e, int depth)
        {
            if (e.ParentIndex < 0 || depth > 64) return e.Name;
            var parent = Entries.FirstOrDefault(p => p.Index == e.ParentIndex && p.IsDirectory);
            return parent == null ? e.Name : BuildPath(parent, depth + 1) + "\\" + e.Name;
        }

        /// <summary>Block numbers of a file in order (follows the hash-table chain unless the file is consecutive).</summary>
        public List<int> FileBlocks(StfsEntry e) => FileBlocks(e, null);

        /// <param name="bad">null = every block on the way must match its hash (throws otherwise). A list = a block that
        /// does not is added there, and the chain goes on as the hash table the console reads has it.</param>
        public List<int> FileBlocks(StfsEntry e, List<int> bad)
        {
            var list = new List<int>();
            int count = (int)((e.Size + BlockSize - 1) / BlockSize);
            int block = e.StartBlock;
            var seen = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                if (block < 0 || block >= 0xFFFFFF || !seen.Add(block)) throw new InvalidDataException(e.Path + ": broken block chain.");
                list.Add(block);
                if (i + 1 == count) break;
                if (e.Consecutive) block++;
                else
                {
                    ReadBlock(block, bad == null, out int next);
                    if (next < 0 && bad != null)
                    {
                        if (!bad.Contains(block)) bad.Add(block);
                        next = ChainNext(block);
                    }
                    block = next;
                }
            }
            return list;
        }

        /// <summary>The next block of a chain as the hash table the console reads has it, without checking the block's own hash.</summary>
        private int ChainNext(int block)
        {
            int active = ActiveCopy(block / HashesPerTable);
            var e = ReadAt(BlockOffset(Level0TableBacking(block) + Math.Max(0, active)) + (block % HashesPerTable) * 0x18, 0x18);
            return e[0x15] << 16 | e[0x16] << 8 | e[0x17];
        }

        public void ExtractFile(StfsEntry e, string target, CancellationToken ct) => ExtractFile(e, target, ct, null);

        /// <summary>
        /// Writes one file of the package to <paramref name="target"/>. <paramref name="bad"/> null = every block must match
        /// its hash (throws otherwise). A list = a block that does not is written as it is and added there - for a copy whose
        /// reader never checks hashes (Xenia), where refusing a whole profile over one block would help nobody.
        /// </summary>
        public void ExtractFile(StfsEntry e, string target, CancellationToken ct, List<int> bad)
        {
            using (var o = new FileStream(target, FileMode.Create, FileAccess.Write))
            {
                long remaining = e.Size;
                foreach (int b in FileBlocks(e, bad))
                {
                    ct.ThrowIfCancellationRequested();
                    var d = ReadBlock(b, bad == null, out int next);
                    if (next < 0 && bad != null && !bad.Contains(b)) bad.Add(b);
                    int n = (int)Math.Min(BlockSize, remaining);
                    o.Write(d, 0, n);
                    remaining -= n;
                }
            }
        }

        public byte[] ReadFile(StfsEntry e)
        {
            using (var ms = new MemoryStream())
            {
                long remaining = e.Size;
                foreach (int b in FileBlocks(e))
                {
                    var d = ReadBlock(b, true, out _);
                    int n = (int)Math.Min(BlockSize, remaining);
                    ms.Write(d, 0, n);
                    remaining -= n;
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Checks every block of every file against its hash: <paramref name="strict"/> = only the table copy a console reads
        /// first, otherwise its backup copy may vouch instead. Returns (blocks checked, bad blocks).
        /// </summary>
        public (int Checked, int Bad) VerifyAll(bool strict = true)
        {
            int ok = 0, bad = 0;
            foreach (var e in Entries.Where(x => !x.IsDirectory && x.Size > 0))
                foreach (int b in FileBlocks(e))
                {
                    var data = ReadAt(BlockOffset(DataBlockToBacking(b)), BlockSize);
                    if (HashEntry(b, data, out _, strict)) ok++; else bad++;
                }
            return (ok + bad, bad);
        }

        // ------------------------------------------------------------- hash tree (levels 0-1, packages up to ~115 MB)

        private long Step0 => 0xAA + (1L << Shift);
        private bool HasLevel1 => TotalBlocks > 0xAA;
        private long TopTableBacking => HasLevel1 ? Step0 : 0;

        private static byte[] Sha(byte[] b) { using (var s = SHA1.Create()) return s.ComputeHash(b); }
        private static bool Eq(byte[] a, int ao, byte[] b, int bo, int n) { for (int i = 0; i < n; i++) if (a[ao + i] != b[bo + i]) return false; return true; }

        /// <summary>
        /// Checks the header hash, the top hash table and every level-0 table. <paramref name="strict"/> = each in the copy
        /// the active-index flags select (what a console reads first); otherwise the backup copy may vouch instead.
        /// Strict success proves a rehash is complete.
        /// </summary>
        public bool VerifyTree(out string why, bool strict = true)
        {
            why = null;
            var head = ReadAt(0, (int)BaseOffset);
            if (!Eq(Sha(head.Skip(Layout.HashFrom).ToArray()), 0, head, Layout.HashAt, 20)) { why = "header hash"; return false; }
            if (TotalBlocks > 0x70E4) { why = "packages over 0x70E4 blocks are not supported"; return false; }
            byte[] top = null;
            foreach (int c in Order(RootActive, strict))
            {
                var t = ReadAt(BlockOffset(TopTableBacking + c), BlockSize);
                if (Eq(Sha(t), 0, head, Layout.Descriptor + 8, 20)) { top = t; break; }
            }
            if (top == null) { why = "top hash table"; return false; }
            if (!HasLevel1) return true;
            int groups = (TotalBlocks + 0xA9) / 0xAA;
            for (int g = 0; g < groups; g++)
            {
                long l0 = Level0TableBacking(g * 0xAA);
                if (!Order((top[g * 0x18 + 0x14] >> 6) & 1, strict).Any(c => Eq(Sha(ReadAt(BlockOffset(l0 + c), BlockSize)), 0, top, g * 0x18, 20)))
                {
                    why = "hash table " + g;
                    return false;
                }
            }
            return true;
        }

        /// <summary>Table copies to try: the flagged one, then (unless strict) its backup.</summary>
        private int[] Order(int flagged, bool strict) => Shift == 0 ? new[] { 0 } : strict ? new[] { flagged } : new[] { flagged, 1 - flagged };

        /// <summary>The whole package header (everything before the first data/hash block).</summary>
        public byte[] ReadHead() => ReadAt(0, (int)BaseOffset);

        public void Dispose() => _fs.Dispose();
    }

    /// <summary>
    /// Package editing: replace the contents of files inside an STFS package - any size, blocks are allocated or
    /// released as needed - and rebuild the hashes that cover them. Everything happens on a temporary copy that must
    /// verify completely (every block, the whole hash tree, the new contents and every untouched file) before it
    /// replaces the original, so a failure leaves the package exactly as it was. The signature is NOT renewed (RGH /
    /// JTAG skip it; stock consoles need a keyvault signature - see Rehash &amp; resign).
    /// Block bookkeeping, as real packages show it: the descriptor carries a free-block count, and each level-1 entry
    /// carries the active copy of its table (bit 30), a released-block count (bits 15-29), a never-used count (bits
    /// 0-14) and bit 31, which is always set while a count is non-zero (and may stay set afterwards). Packages that
    /// other tools have rewritten do not always keep those counts in step with the per-block states, so nothing is
    /// ever recomputed here: every count moves by exactly the blocks this edit takes or releases, and a block that
    /// any file's chain uses is never handed out, whatever its state says.
    /// </summary>
    /// <summary>Header details of a package to change; null members are kept (see <see cref="StfsEditor.SetMetadata"/>).</summary>
    public sealed class StfsMetadata
    {
        public string DisplayName, Description, Publisher, TitleName;
        public byte[] ThumbnailPng, TitleThumbnailPng;
    }

    public static class StfsEditor
    {
        private const int BlockSize = 0x1000, PerTable = 0xAA, MaxBlocks = 0x70E4;
        private const uint EndOfChain = 0xFFFFFF, InUse = 0x80000000, Released = 0x40000000;

        public static void ReplaceFile(string packagePath, string entryPath, byte[] content)
        {
            ReplaceFiles(packagePath, new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { { entryPath, content } });
        }

        /// <summary>Replaces several files in one step: either all of them are written and verified, or nothing changes.</summary>
        public static void ReplaceFiles(string packagePath, IDictionary<string, byte[]> files)
        {
            if (files == null || files.Count == 0) return;
            var wanted = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                if (f.Value == null) throw new ArgumentNullException(nameof(files), "No content given for " + f.Key + ".");
                if (wanted.ContainsKey(f.Key)) throw new ArgumentException("The same file is listed twice: " + f.Key);
                wanted[f.Key] = f.Value;
            }

            Apply(packagePath, wanted, null, v => { foreach (var f in wanted) v.SetFile(f.Key, f.Value); });
        }

        /// <summary>
        /// Replaces and adds files in one step: either every change is written and verified, or nothing changes.
        /// Either dictionary may be null or empty.
        /// </summary>
        public static void Update(string packagePath, IDictionary<string, byte[]> replace, IDictionary<string, byte[]> add) => Update(packagePath, replace, add, null);

        /// <summary>
        /// The same, and the header details in <paramref name="header"/> (when given) change in the same step - a
        /// profile's picture, for instance, is two files plus the picture in the header, and all three change or none.
        /// </summary>
        public static void Update(string packagePath, IDictionary<string, byte[]> replace, IDictionary<string, byte[]> add, StfsMetadata header)
        {
            if (header != null) ValidateMetadata(header);
            var wanted = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in new[] { replace, add })
                if (set != null)
                    foreach (var f in set)
                    {
                        if (f.Value == null) throw new ArgumentNullException(nameof(replace), "No content given for " + f.Key + ".");
                        if (wanted.ContainsKey(f.Key)) throw new ArgumentException("The same file is listed twice: " + f.Key);
                        wanted[f.Key] = f.Value;
                    }
            if (wanted.Count == 0)
            {
                if (header != null) SetMetadata(packagePath, header);
                return;
            }
            Apply(packagePath, wanted, null, v =>
            {
                if (replace != null) foreach (var f in replace) v.SetFile(f.Key, f.Value);
                if (add != null) foreach (var f in add) v.AddFile(f.Key, f.Value);
            }, header);
        }

        /// <summary>
        /// Adds a new file. <paramref name="entryPath"/> is a name of up to 40 ASCII characters, optionally inside an
        /// existing folder of the package ("folder\name").
        /// </summary>
        public static void AddFile(string packagePath, string entryPath, byte[] content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            var wanted = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { { entryPath, content } };
            Apply(packagePath, wanted, null, v => v.AddFile(entryPath, content));
        }

        /// <summary>Removes a file: its blocks are released and its file-table slot is marked unused.</summary>
        public static void DeleteFile(string packagePath, string entryPath)
        {
            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entryPath };
            Apply(packagePath, new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase), removed, v => v.DeleteFile(entryPath));
        }

        private static void Apply(string packagePath, Dictionary<string, byte[]> wanted, HashSet<string> removed, Action<Volume> edit, StfsMetadata header = null)
        {
            string tmp = packagePath + ".editing";
            File.Copy(packagePath, tmp, true);
            try
            {
                var before = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);   // file -> SHA-1 of its content
                var volume = Volume.Load(tmp, before);
                edit(volume);
                volume.Save(tmp);
                if (header != null) PatchHeader(tmp, header);
                if (removed != null) foreach (string r in removed) before.Remove(r);
                foreach (var w in wanted) if (!before.ContainsKey(w.Key)) before[w.Key] = null;   // a new file: checked by content below
                VerifyEdit(tmp, before, wanted);
                Commit(tmp, packagePath);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        /// <summary>
        /// The largest picture a package header holds: 0x3D00 bytes in metadata version 2, which every package uses
        /// (version 1 had 0x4000; version 2 keeps three more language slots in the last 0x300 bytes of each picture area).
        /// </summary>
        public const int MaxPictureBytes = 0x3D00;

        /// <summary>
        /// Changes the names in a package's header - the display name (what the dashboard lists, up to 127 characters,
        /// every language slot) and the title name (the game, up to 63) - and renews the header hash. Null keeps a name.
        /// </summary>
        public static void SetNames(string packagePath, string displayName, string titleName)
            => SetMetadata(packagePath, new StfsMetadata { DisplayName = displayName, TitleName = titleName });

        /// <summary>
        /// Changes what a package's header says about it - display name, description (every language slot, as
        /// X360PkgTool writes them, up to 127 characters each), publisher and game name (up to 63) and its two 64x64 pictures
        /// (PNG, at most <see cref="MaxPictureBytes"/> each: the item's own picture at 0x171A and the game's at 0x571A, sizes
        /// at 0x1712 / 0x1716) - and renews the header hash. Null members are left as they are. The data blocks are not touched.
        /// </summary>
        public static void SetMetadata(string packagePath, StfsMetadata m)
        {
            ValidateMetadata(m);
            string tmp = packagePath + ".editing";
            File.Copy(packagePath, tmp, true);
            try
            {
                PatchHeader(tmp, m);
                using (var check = StfsVolume.Open(tmp))
                {
                    if (!check.VerifyTree(out string why)) throw new InvalidDataException("The edited package did not verify (" + why + ") - the original was left untouched.");
                }
                Commit(tmp, packagePath);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        private static void ValidateMetadata(StfsMetadata m)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            string displayName = m.DisplayName, titleName = m.TitleName;
            if (displayName != null && displayName.Length > 127) throw new ArgumentException("The display name is at most 127 characters.");
            if (titleName != null && titleName.Length > 63) throw new ArgumentException("The title name is at most 63 characters.");
            if (m.Description != null && m.Description.Length > 127) throw new ArgumentException("The description is at most 127 characters.");
            if (m.Publisher != null && m.Publisher.Length > 63) throw new ArgumentException("The publisher is at most 63 characters.");
            foreach (var png in new[] { m.ThumbnailPng, m.TitleThumbnailPng })
            {
                if (png == null) continue;
                if (png.Length > MaxPictureBytes) throw new ArgumentException("A package picture can be at most " + MaxPictureBytes.ToString("N0") + " bytes (" + png.Length.ToString("N0") + " given).");
                if (png.Length < 8 || png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47) throw new ArgumentException("Package pictures are PNG files.");
            }
        }

        /// <summary>Writes the header details into the file at <paramref name="file"/> (a working copy) and renews its header hash.</summary>
        private static void PatchHeader(string file, StfsMetadata m)
        {
            string displayName = m.DisplayName, titleName = m.TitleName;
            {
                byte[] head;
                using (var v = StfsVolume.Open(file))
                {
                    if (v.IsPec) throw new InvalidOperationException("A PEC container has no names.");
                    head = v.ReadHead();
                }
                if (head.Length < 0x1711) throw new InvalidDataException("The package header is too small to hold names.");
                // Metadata version 2 (every package seen here - console saves, profiles and DLC alike) keeps three more
                // languages: their display names at 0x541A and their descriptions at 0x941A, in the last 0x300 bytes of the
                // two 16 KB picture areas, which leaves each picture 0x3D00 bytes. Xenia reads the header the same way.
                bool v2 = BE.U32(head, 0x348) >= 2;
                bool texts = displayName != null || m.Description != null, pictures = m.ThumbnailPng != null || m.TitleThumbnailPng != null;
                if ((pictures || (v2 && texts)) && head.Length < 0x971A) throw new InvalidDataException("The package header is too small for its pictures and language slots.");
                // Every language slot - the nine of version 1 and the three of version 2 - gets the new text, exactly as
                // X360PkgTool -sn / -sd writes it; otherwise a console or Xenia set to another language keeps showing the old
                // name from its own slot (found 2026-11-18 on a DLC with 7 slots filled; compared byte for byte with the file
                // X360PkgTool writes).
                for (int slot = 0; slot < 9; slot++)
                {
                    if (displayName != null) BE.WriteUtf16BE(head, 0x411 + slot * 0x100, 0x100, displayName);
                    if (m.Description != null) BE.WriteUtf16BE(head, 0xD11 + slot * 0x100, 0x100, m.Description);
                }
                if (v2)
                    for (int slot = 0; slot < 3; slot++)
                    {
                        if (displayName != null) BE.WriteUtf16BE(head, 0x541A + slot * 0x100, 0x100, displayName);
                        if (m.Description != null) BE.WriteUtf16BE(head, 0x941A + slot * 0x100, 0x100, m.Description);
                    }
                if (titleName != null) BE.WriteUtf16BE(head, 0x1691, 0x80, titleName);
                if (m.Publisher != null) BE.WriteUtf16BE(head, 0x1611, 0x80, m.Publisher);
                if (pictures)
                {
                    int area = v2 ? MaxPictureBytes : 0x4000;
                    if (m.ThumbnailPng != null) WritePicture(head, 0x1712, 0x171A, area, m.ThumbnailPng);
                    if (m.TitleThumbnailPng != null) WritePicture(head, 0x1716, 0x571A, area, m.TitleThumbnailPng);
                }
                using (var sha = SHA1.Create()) Buffer.BlockCopy(sha.ComputeHash(head, 0x344, head.Length - 0x344), 0, head, 0x32C, 20);
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Write(fs, 0, head);
                    fs.Flush(true);
                }
            }
        }

        /// <summary>
        /// Stores a picture as consoles do: the PNG at <paramref name="dataAt"/>, the rest of its area zeroed, and its exact
        /// length at <paramref name="sizeAt"/> (as in every console-made save; X360PkgTool -i writes a fixed D03D0000 there).
        /// </summary>
        private static void WritePicture(byte[] head, int sizeAt, int dataAt, int area, byte[] png)
        {
            if (png.Length > area) throw new ArgumentException("This package holds pictures of at most " + area.ToString("N0") + " bytes (" + png.Length.ToString("N0") + " given).");
            Array.Clear(head, dataAt, area);
            Buffer.BlockCopy(png, 0, head, dataAt, png.Length);
            BE.W32(head, sizeAt, (uint)png.Length);
        }

        /// <summary>What a console checks, plus: the replaced files read back exactly and no other file changed.</summary>
        private static void VerifyEdit(string path, Dictionary<string, byte[]> before, Dictionary<string, byte[]> wanted)
        {
            using (var check = StfsVolume.Open(path))
            {
                var (_, bad) = check.VerifyAll();
                string why = null;
                if (bad > 0 || !check.VerifyTree(out why))
                    throw new InvalidDataException("The edited package did not verify (" + (bad > 0 ? bad + " blocks" : why) + ") - the original was left untouched.");
                var entries = check.Entries.Where(e => !e.IsDirectory).ToList();
                if (entries.Count != before.Count)
                    throw new InvalidDataException("The edited package lists a different number of files - the original was left untouched.");
                foreach (var e in entries)
                {
                    byte[] now = check.ReadFile(e);
                    if (wanted.TryGetValue(e.Path, out var want))
                    {
                        if (!now.SequenceEqual(want)) throw new InvalidDataException(e.Path + " did not read back as written - the original was left untouched.");
                    }
                    else if (!before.TryGetValue(e.Path, out var hash) || !Sha1(now).SequenceEqual(hash))
                        throw new InvalidDataException(e.Path + " changed although it was not edited - the original was left untouched.");
                }
            }
        }

        /// <summary>
        /// Puts the finished working copy in place of the original. The swap is done by the file system where it can
        /// (so an interruption never leaves half a file); otherwise it falls back to a plain overwrite.
        /// </summary>
        internal static void Commit(string workingCopy, string target)
        {
            try { File.Replace(workingCopy, target, null); return; }
            catch (IOException) { }
            catch (PlatformNotSupportedException) { }
            catch (UnauthorizedAccessException) { }
            File.Copy(workingCopy, target, true);
        }

        /// <summary>A whole STFS volume (one or two hash levels, up to ~115 MB) held in memory while files are replaced.</summary>
        private sealed class Volume
        {
            private byte[] _head;
            private StfsLayout _layout;
            private long _base, _fileLength;
            private int _shift, _copies, _rootActive, _total, _originalTotal, _fileTableBlock, _fileTableBlockCount, _scanFrom;
            private uint _free;
            private List<StfsEntry> _entries;
            private readonly List<byte[]> _data = new List<byte[]>();
            private readonly List<uint> _info = new List<uint>();        // per block: state (bits 31-30) + next block (low 24 bits)
            private readonly List<byte[]> _l0 = new List<byte[]>();      // the current level-0 table of each group
            private readonly List<int> _l0Active = new List<int>();      // the copy of it that the active-index flag selects
            private byte[] _top;                                          // the level-1 table of a two-level volume
            private readonly HashSet<int> _owned = new HashSet<int>();         // blocks in some file's chain (or the file table's)
            private readonly HashSet<int> _dirtyBlocks = new HashSet<int>();
            private readonly HashSet<int> _dirtyGroups = new HashSet<int>();   // table content changed (a hash or a chain link)
            private readonly Dictionary<int, int> _releasedDelta = new Dictionary<int, int>();   // per group: change of its released-block count
            private readonly Dictionary<int, int> _unusedDelta = new Dictionary<int, int>();     // per group: change of its never-used count

            public static Volume Load(string path, Dictionary<string, byte[]> contentHashes)
            {
                var v = new Volume();
                using (var r = StfsVolume.Open(path))
                {
                    var (_, bad) = r.VerifyAll();
                    string why = null;
                    if (bad > 0 || !r.VerifyTree(out why))
                        throw new InvalidDataException("The package does not verify (" + (bad > 0 ? bad + " bad blocks" : why) + "), so it was not touched. Click Rehash & resign first, then replace the file.");
                    if (r.TotalBlocks > MaxBlocks) throw new InvalidOperationException("Editing packages over ~115 MB is not supported.");
                    if (r.TotalBlocks <= 0) throw new InvalidDataException("The package has no data blocks.");
                    v._shift = r.Shift;
                    v._copies = 1 << r.Shift;
                    v._rootActive = r.RootActive;
                    v._total = v._originalTotal = r.TotalBlocks;
                    v._base = r.BaseOffset;
                    v._layout = r.Layout;
                    v._fileTableBlock = r.FileTableBlock;
                    v._fileTableBlockCount = r.FileTableBlockCount;
                    v._entries = r.Entries.ToList();
                    foreach (var e in v._entries.Where(x => !x.IsDirectory))
                    {
                        contentHashes[e.Path] = Sha1(r.ReadFile(e));
                        foreach (int b in r.FileBlocks(e)) v._owned.Add(b);
                    }
                    int table = r.FileTableBlock;
                    for (int i = 0; i < r.FileTableBlockCount && table >= 0 && table < r.TotalBlocks; i++)
                    {
                        v._owned.Add(table);
                        r.ReadBlock(table, true, out table);
                    }
                }
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    v._fileLength = fs.Length;
                    v._head = new byte[v._base];
                    fs.Position = 0;
                    ReadFully(fs, v._head);
                    int topHash = v._layout.Descriptor + 8;
                    v._free = BE.U32(v._head, v._layout.Descriptor + 0x20);
                    bool twoLevel = v._total > PerTable;
                    long topBacking = PerTable + v._copies;
                    if (twoLevel)
                        v._top = ReadBlock(fs, v._base + (topBacking + CurrentCopy(fs, v._base, topBacking, v._copies, v._head, topHash, v._rootActive)) * BlockSize, true);
                    int groups = (v._total + PerTable - 1) / PerTable;
                    for (int g = 0; g < groups; g++)
                    {
                        long l0 = Level0Backing(g * PerTable, v._shift);
                        int flag = v._copies == 1 ? 0 : twoLevel ? (v._top[g * 0x18 + 0x14] >> 6) & 1 : v._rootActive;
                        int current = twoLevel
                            ? CurrentCopy(fs, v._base, l0, v._copies, v._top, g * 0x18, flag)
                            : CurrentCopy(fs, v._base, l0, v._copies, v._head, topHash, flag);
                        v._l0.Add(ReadBlock(fs, v._base + (l0 + current) * BlockSize, true));
                        v._l0Active.Add(flag);
                    }
                    for (int b = 0; b < v._total; b++)
                    {
                        uint info = BE.U32(v._l0[b / PerTable], (b % PerTable) * 0x18 + 0x14);
                        v._info.Add(info);
                        // a block no file uses may lie past the end of a short file; a block in use may not
                        v._data.Add(ReadBlock(fs, v._base + DataBacking(b, v._shift) * BlockSize, (info & InUse) != 0));
                    }
                }
                return v;
            }

            private static byte[] ReadBlock(FileStream fs, long offset, bool required)
            {
                var b = new byte[BlockSize];
                if (offset + BlockSize > fs.Length)
                {
                    if (required) throw new InvalidDataException("The package is cut short.");
                    return b;
                }
                fs.Position = offset;
                ReadFully(fs, b);
                return b;
            }

            public void SetFile(string entryPath, byte[] content)
            {
                var matches = _entries.Where(e => !e.IsDirectory && string.Equals(e.Path, entryPath, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0) throw new FileNotFoundException("No such file in the package: " + entryPath);
                if (matches.Count > 1) throw new InvalidDataException("The package holds more than one file named " + entryPath + ".");
                var entry = matches[0];

                // The file's blocks, walked the way the reader (and a console) walks them.
                int oldCount = (int)(((long)entry.Size + BlockSize - 1) / BlockSize);
                var chain = new List<int>(oldCount);
                var seen = new HashSet<int>();
                int block = entry.StartBlock;
                for (int i = 0; i < oldCount; i++)
                {
                    if (block < 0 || block >= _total || !seen.Add(block)) throw new InvalidDataException(entry.Path + ": broken block chain.");
                    chain.Add(block);
                    block = entry.Consecutive ? block + 1 : (int)(_info[block] & EndOfChain);
                }

                int newCount = (int)(((long)content.Length + BlockSize - 1) / BlockSize);
                bool resized = newCount != oldCount;
                // A resize moves the free-block counts, so the table must agree that this file's blocks are in use.
                if (resized && chain.Any(b => (_info[b] & InUse) == 0))
                    throw new InvalidDataException(entry.Path + ": the package's block table marks one of this file's blocks as free, so its size cannot be changed safely.");
                while (chain.Count < newCount) chain.Add(Allocate());
                while (chain.Count > newCount)
                {
                    Release(chain[chain.Count - 1]);
                    chain.RemoveAt(chain.Count - 1);
                }

                for (int i = 0; i < chain.Count; i++)
                {
                    int b = chain[i];
                    var d = new byte[BlockSize];
                    Buffer.BlockCopy(content, i * BlockSize, d, 0, Math.Min(BlockSize, content.Length - i * BlockSize));
                    _data[b] = d;
                    _dirtyBlocks.Add(b);
                    _dirtyGroups.Add(b / PerTable);
                    if (resized)   // every link of a chain that changed length is written out, last one terminated
                        _info[b] = (_info[b] & 0xC0000000) | (i + 1 < chain.Count ? (uint)chain[i + 1] : EndOfChain);
                }

                if (!resized && content.Length == entry.Size) return;

                // The file's record in the file table: size, and after a resize the block counts, first block and
                // the "blocks are consecutive" shortcut (cleared as soon as it stops being true, never set).
                const int perBlock = BlockSize / 0x40;
                int tableBlock = _fileTableBlock;
                for (int i = 0; i < entry.Index / perBlock; i++)
                {
                    if (tableBlock < 0 || tableBlock >= _total) throw new InvalidDataException("Broken file table chain.");
                    tableBlock = (int)(_info[tableBlock] & EndOfChain);
                }
                if (tableBlock < 0 || tableBlock >= _total) throw new InvalidDataException("Broken file table chain.");
                var table = (byte[])_data[tableBlock].Clone();
                int o = entry.Index % perBlock * 0x40;
                int nameLength = table[o + 0x28] & 0x3F;
                if (nameLength != entry.Name.Length || Encoding.ASCII.GetString(table, o, nameLength) != entry.Name)
                    throw new InvalidDataException("The file table record for " + entry.Path + " is not where it was read from.");
                BE.W32(table, o + 0x34, (uint)content.Length);
                if (resized)
                {
                    bool consecutive = true;
                    for (int i = 1; i < chain.Count; i++) if (chain[i] != chain[i - 1] + 1) consecutive = false;
                    Put24(table, o + 0x29, newCount);
                    Put24(table, o + 0x2C, newCount);
                    Put24(table, o + 0x2F, newCount > 0 ? chain[0] : 0);
                    if (!consecutive) table[o + 0x28] &= 0xBF;
                    entry.BlockCount = newCount;
                    entry.StartBlock = newCount > 0 ? chain[0] : 0;
                    entry.Consecutive = entry.Consecutive && consecutive;
                }
                entry.Size = (uint)content.Length;
                _data[tableBlock] = table;
                _dirtyBlocks.Add(tableBlock);
                _dirtyGroups.Add(tableBlock / PerTable);
            }

            private static void Put24(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); }

            /// <summary>The blocks of the file table, in order.</summary>
            private List<int> TableBlocks()
            {
                var list = new List<int>();
                int b = _fileTableBlock;
                for (int i = 0; i < _fileTableBlockCount; i++)
                {
                    if (b < 0 || b >= _total || list.Contains(b)) throw new InvalidDataException("Broken file table chain.");
                    list.Add(b);
                    b = (int)(_info[b] & EndOfChain);
                }
                return list;
            }

            public void AddFile(string entryPath, byte[] content)
            {
                int cut = entryPath.LastIndexOf('\\');
                string folder = cut >= 0 ? entryPath.Substring(0, cut) : "", name = cut >= 0 ? entryPath.Substring(cut + 1) : entryPath;
                if (name.Length == 0 || name.Length > 0x28 || name.Any(c => c < 0x20 || c > 0x7E || c == '\\' || c == '/' || c == ':'))
                    throw new ArgumentException("A file name inside a package is 1-40 plain ASCII characters without \\ / or :");
                short parent = -1;
                if (folder.Length > 0)
                {
                    var dir = _entries.FirstOrDefault(e => e.IsDirectory && string.Equals(e.Path, folder, StringComparison.OrdinalIgnoreCase))
                              ?? throw new DirectoryNotFoundException("The package has no folder named " + folder + ".");
                    parent = (short)dir.Index;
                }
                if (_entries.Any(e => string.Equals(e.Path, entryPath, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("The package already has " + entryPath + ".");

                // a free slot in the file table, or a new table block when every slot is taken
                const int perBlock = BlockSize / 0x40;
                var tables = TableBlocks();
                int slot = -1;
                for (int t = 0; t < tables.Count && slot < 0; t++)
                    for (int i = 0; i < perBlock; i++)
                        if ((_data[tables[t]][i * 0x40 + 0x28] & 0x3F) == 0) { slot = t * perBlock + i; break; }
                if (slot < 0)
                {
                    if (_fileTableBlockCount >= 0xFFFF) throw new InvalidOperationException("The package's file table is full.");
                    int last = tables[tables.Count - 1], fresh = Allocate();
                    _info[last] = (_info[last] & 0xC0000000) | (uint)fresh;
                    _dirtyGroups.Add(last / PerTable);
                    _data[fresh] = new byte[BlockSize];
                    _dirtyBlocks.Add(fresh);
                    _fileTableBlockCount++;
                    tables.Add(fresh);
                    slot = (tables.Count - 1) * perBlock;
                }

                int block = tables[slot / perBlock], o = slot % perBlock * 0x40;
                var table = (byte[])_data[block].Clone();
                Array.Clear(table, o, 0x40);
                Encoding.ASCII.GetBytes(name, 0, name.Length, table, o);
                table[o + 0x28] = (byte)name.Length;                 // a file, blocks not flagged consecutive
                BE.W16(table, o + 0x32, (ushort)parent);
                uint stamp = FatTime(DateTime.Now);
                BE.W32(table, o + 0x38, stamp);
                BE.W32(table, o + 0x3C, stamp);
                _data[block] = table;
                _dirtyBlocks.Add(block);
                _dirtyGroups.Add(block / PerTable);

                _entries.Add(new StfsEntry { Name = name, Path = entryPath, Index = slot, ParentIndex = parent });
                if (content.Length > 0) SetFile(entryPath, content);
            }

            public void DeleteFile(string entryPath)
            {
                var matches = _entries.Where(e => !e.IsDirectory && string.Equals(e.Path, entryPath, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0) throw new FileNotFoundException("No such file in the package: " + entryPath);
                if (matches.Count > 1) throw new InvalidDataException("The package holds more than one file named " + entryPath + ".");
                var entry = matches[0];
                if (entry.Size > 0) SetFile(entryPath, new byte[0]);     // releases every block of the file
                const int perBlock = BlockSize / 0x40;
                int block = TableBlocks()[entry.Index / perBlock], o = entry.Index % perBlock * 0x40;
                var table = (byte[])_data[block].Clone();
                table[o + 0x28] &= 0xC0;                                 // name length 0 = unused slot (what a console leaves behind)
                _data[block] = table;
                _dirtyBlocks.Add(block);
                _dirtyGroups.Add(block / PerTable);
                _entries.Remove(entry);
            }

            /// <summary>FAT-style date and time, as the file table stores them.</summary>
            private static uint FatTime(DateTime t)
            {
                int year = Math.Max(1980, Math.Min(2107, t.Year));
                return (uint)((year - 1980) << 25 | t.Month << 21 | t.Day << 16 | t.Hour << 11 | t.Minute << 5 | t.Second / 2);
            }

            private static void Bump(Dictionary<int, int> counts, int group, int by)
            {
                counts.TryGetValue(group, out int now);
                counts[group] = now + by;
            }

            /// <summary>
            /// A block for a growing file: one the volume already has that is marked released or never used and that no
            /// file's chain uses, else a new one at the end.
            /// </summary>
            private int Allocate()
            {
                for (int b = _scanFrom; b < _total; b++)
                {
                    if ((_info[b] & InUse) != 0 || _owned.Contains(b)) continue;
                    Bump((_info[b] & Released) != 0 ? _releasedDelta : _unusedDelta, b / PerTable, -1);
                    _info[b] = InUse | EndOfChain;
                    if (_free > 0) _free--;
                    _owned.Add(b);
                    _dirtyGroups.Add(b / PerTable);
                    _scanFrom = b + 1;
                    return b;
                }
                _scanFrom = _total;
                if (_total >= MaxBlocks) throw new InvalidOperationException("The package would grow past ~115 MB, which is not supported.");
                int n = _total++;
                _data.Add(new byte[BlockSize]);
                _info.Add(InUse | EndOfChain);
                if (n % PerTable == 0)
                {
                    _l0.Add(new byte[BlockSize]);
                    _l0Active.Add(0);
                }
                _owned.Add(n);
                _dirtyBlocks.Add(n);
                _dirtyGroups.Add(n / PerTable);
                _scanFrom = _total;
                return n;
            }

            private void Release(int block)
            {
                _info[block] = Released | EndOfChain;
                _free++;
                _owned.Remove(block);
                Bump(_releasedDelta, block / PerTable, 1);
                _dirtyGroups.Add(block / PerTable);
                if (block < _scanFrom) _scanFrom = block;
            }

            /// <summary>
            /// Level-1 bookkeeping of a table that has none yet (a volume that just grew a second hash level): its
            /// released and never-used blocks are counted, and bit 31 is set when there are any.
            /// </summary>
            private uint CountFree(int group)
            {
                uint released = 0, unused = 0;
                for (int b = group * PerTable, end = Math.Min(_total, b + PerTable); b < end; b++)
                {
                    if ((_info[b] & InUse) != 0 || _owned.Contains(b)) continue;
                    if ((_info[b] & Released) != 0) released++;
                    else unused++;
                }
                return released << 15 | unused | (released + unused > 0 ? 0x80000000u : 0);
            }

            /// <summary>Existing level-1 bookkeeping moved by what this edit took or released; bit 31 is set while a count is non-zero and never cleared.</summary>
            private uint MoveCounts(uint info, int group)
            {
                _releasedDelta.TryGetValue(group, out int dReleased);
                _unusedDelta.TryGetValue(group, out int dUnused);
                if (dReleased == 0 && dUnused == 0) return info;
                long released = Math.Max(0, Math.Min(0x7FFF, ((info >> 15) & 0x7FFF) + (long)dReleased));
                long unused = Math.Max(0, Math.Min(0x7FFF, (info & 0x7FFF) + (long)dUnused));
                info = (info & 0xC0000000) | (uint)released << 15 | (uint)unused;
                return released + unused > 0 ? info | 0x80000000u : info;
            }

            public void Save(string path)
            {
                bool twoLevel = _total > PerTable, wasTwoLevel = _originalTotal > PerTable;
                int groups = (_total + PerTable - 1) / PerTable, oldGroups = (_originalTotal + PerTable - 1) / PerTable;

                foreach (int g in _dirtyGroups)
                {
                    var t = _l0[g];
                    for (int b = g * PerTable, end = Math.Min(_total, b + PerTable); b < end; b++)
                    {
                        if (_dirtyBlocks.Contains(b)) Buffer.BlockCopy(Sha1(_data[b]), 0, t, b % PerTable * 0x18, 20);
                        BE.W32(t, b % PerTable * 0x18 + 0x14, _info[b]);
                    }
                }

                byte[] top;
                if (twoLevel)
                {
                    bool newTop = !wasTwoLevel;
                    if (newTop)
                    {
                        // The volume outgrew a single table: what was the top table becomes group 0 under a new level-1
                        // table. Its active copy moves from the descriptor flag into the level-1 entry.
                        _top = new byte[BlockSize];
                        _rootActive = 0;
                    }
                    for (int g = 0; g < groups; g++)
                    {
                        bool fresh = newTop || g >= oldGroups;
                        if (!fresh && !_dirtyGroups.Contains(g)) continue;
                        Buffer.BlockCopy(Sha1(_l0[g]), 0, _top, g * 0x18, 20);
                        uint info = fresh ? CountFree(g) : MoveCounts(BE.U32(_top, g * 0x18 + 0x14), g);
                        BE.W32(_top, g * 0x18 + 0x14, (info & ~0x40000000u) | (uint)_l0Active[g] << 30);
                    }
                    if (newTop || _total != _originalTotal) BE.W32(_top, 0xFF0, (uint)_total);   // the top table ends with the block total
                    top = _top;
                }
                else top = _l0[0];

                int d = _layout.Descriptor;
                Buffer.BlockCopy(Sha1(top), 0, _head, d + 8, 20);
                if (_total != _originalTotal) BE.W32(_head, d + 0x1C, (uint)_total);
                BE.W32(_head, d + 0x20, _free);
                _head[d + 3] = (byte)_fileTableBlockCount;            // little-endian, like the block numbers beside it
                _head[d + 4] = (byte)(_fileTableBlockCount >> 8);
                _head[d + 2] = (byte)(_head[d + 2] & ~2 | _rootActive << 1);
                long length = Math.Max(_fileLength, _base + (DataBacking(_total - 1, _shift) + 1) * BlockSize);
                if (!_layout.IsPec)
                {
                    ulong contentSize = BE.U64(_head, 0x34C);
                    if (length != _fileLength && contentSize != 0 && contentSize == (ulong)(_fileLength - _base))
                        BE.W64(_head, 0x34C, (ulong)(length - _base));   // only where the header tracked the data size before
                }
                using (var sha = SHA1.Create()) Buffer.BlockCopy(sha.ComputeHash(_head, _layout.HashFrom, _head.Length - _layout.HashFrom), 0, _head, _layout.HashAt, 20);

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    if (length > fs.Length) fs.SetLength(length);
                    foreach (int b in _dirtyBlocks) Write(fs, _base + DataBacking(b, _shift) * BlockSize, _data[b]);
                    foreach (int g in _dirtyGroups)
                        for (int c = 0; c < _copies; c++) Write(fs, _base + (Level0Backing(g * PerTable, _shift) + c) * BlockSize, _l0[g]);
                    if (twoLevel)
                        for (int c = 0; c < _copies; c++) Write(fs, _base + (PerTable + _copies + c) * BlockSize, _top);
                    Write(fs, 0, _head);
                    fs.Flush(true);
                }
            }
        }

        private static long DataBacking(int block, int shift)
        {
            long b = block;
            long ret = ((b + 0xAA) / 0xAA << shift) + b;
            if (b >= 0xAA)
            {
                ret += (b + 0x70E4) / 0x70E4 << shift;
                if (b >= 0x70E4) ret += (b + 0x4AF768) / 0x4AF768 << shift;
            }
            return ret;
        }

        private static long Level0Backing(int block, int shift)
        {
            long b = block;
            long step0 = 0xAA + (1L << shift);
            long group0 = b / 0xAA, group1 = b / 0x70E4;
            long ret = group0 * step0;
            if (b >= 0xAA) ret += (group1 + 1) << shift;
            if (b >= 0x70E4) ret += (b / 0x4AF768 + 1) << shift;
            return ret;
        }

        private static byte[] Sha1(byte[] b) { using (var s = SHA1.Create()) return s.ComputeHash(b); }
        private static byte[] Read(FileStream fs, long off) { var b = new byte[BlockSize]; fs.Position = off; fs.Read(b, 0, BlockSize); return b; }
        private static void Write(FileStream fs, long off, byte[] b) { fs.Position = off; fs.Write(b, 0, b.Length); }

        /// <summary>
        /// Rebuilds every SHA-1 of the hash tree from the data blocks (the standard "rehash"): the level-0 entries, the
        /// top table, the top hash and the header hash. Runs in place on a working copy; the caller verifies the result
        /// and decides whether to keep it. Each table is rebuilt from the copy the existing hash tree proves current
        /// (falling back to the active-index flags), so block chains and allocation states are kept, and the result is
        /// written to both copies so every reader sees the same table. The signature is not touched.
        /// </summary>
        internal static void RehashInPlace(string path)
        {
            byte[] raw = BE.ReadHead(path, 0x400);
            var layout = StfsLayout.Detect(raw);
            int d = layout.Descriptor;
            long baseOffset = layout.Base;
            int totalBlocks = (int)BE.U32(raw, d + 0x1C);
            if (totalBlocks <= 0) throw new InvalidDataException("The package has no data blocks.");
            if (totalBlocks > 0x70E4) throw new InvalidOperationException("Packages over ~115 MB are not supported here (saves and profiles are far smaller).");
            // One or two copies per table: proven by the file table's own hash while it still verifies, else the header
            // flag. Never guessed - with the wrong layout the tables would be written over data blocks.
            int shift;
            try { using (var v = StfsVolume.Open(path)) shift = v.Shift; }
            catch (InvalidDataException) { shift = (raw[d + 2] & 1) == 1 ? 0 : 1; }
            int rootActive = (raw[d + 2] >> 1) & 1;

            int copies = 1 << shift, groups = (totalBlocks + 0xA9) / 0xAA;
            bool twoLevel = totalBlocks > 0xAA;
            long topBacking = twoLevel ? 0xAA + copies : 0;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                long need = baseOffset + (DataBacking(totalBlocks - 1, shift) + 1) * BlockSize;
                if (fs.Length < need) throw new InvalidDataException("The package is cut short (" + fs.Length + " of " + need + " bytes).");
                var head = new byte[baseOffset];
                fs.Position = 0;
                ReadFully(fs, head);

                byte[] top = twoLevel ? Read(fs, baseOffset + (topBacking + CurrentCopy(fs, baseOffset, topBacking, copies, head, d + 8, rootActive)) * BlockSize) : null;
                var tables = new byte[groups][];
                for (int g = 0; g < groups; g++)
                {
                    long l0 = Level0Backing(g * 0xAA, shift);
                    int current = twoLevel
                        ? CurrentCopy(fs, baseOffset, l0, copies, top, g * 0x18, (top[g * 0x18 + 0x14] >> 6) & 1)
                        : CurrentCopy(fs, baseOffset, l0, copies, head, d + 8, rootActive);
                    var t = Read(fs, baseOffset + (l0 + current) * BlockSize);
                    for (int b = g * 0xAA, end = Math.Min(totalBlocks, b + 0xAA); b < end; b++)
                        Buffer.BlockCopy(Sha1(Read(fs, baseOffset + DataBacking(b, shift) * BlockSize)), 0, t, (b % 0xAA) * 0x18, 20);
                    tables[g] = t;
                }
                for (int g = 0; g < groups; g++)
                    for (int c = 0; c < copies; c++) Write(fs, baseOffset + (Level0Backing(g * 0xAA, shift) + c) * BlockSize, tables[g]);
                if (twoLevel)
                {
                    for (int g = 0; g < groups; g++) Buffer.BlockCopy(Sha1(tables[g]), 0, top, g * 0x18, 20);
                    for (int c = 0; c < copies; c++) Write(fs, baseOffset + (topBacking + c) * BlockSize, top);
                }
                else top = tables[0];
                Buffer.BlockCopy(Sha1(top), 0, head, d + 8, 20);
                using (var sha = SHA1.Create()) Buffer.BlockCopy(sha.ComputeHash(head, layout.HashFrom, head.Length - layout.HashFrom), 0, head, layout.HashAt, 20);
                Write(fs, 0, head);
                fs.Flush(true);
            }
        }

        /// <summary>The current copy of a hash table: the one its parent hash proves, else the active-index flag.</summary>
        private static int CurrentCopy(FileStream fs, long baseOffset, long backing, int copies, byte[] parent, int parentOffset, int flag)
        {
            if (copies == 1) return 0;
            foreach (int c in new[] { flag, 1 - flag })
            {
                var h = Sha1(Read(fs, baseOffset + (backing + c) * BlockSize));
                bool ok = true;
                for (int i = 0; i < 20 && ok; i++) ok = h[i] == parent[parentOffset + i];
                if (ok) return c;
            }
            return flag;
        }

        private static void ReadFully(FileStream fs, byte[] b)
        {
            int n = 0;
            while (n < b.Length) { int r = fs.Read(b, n, b.Length - n); if (r <= 0) throw new EndOfStreamException(); n += r; }
        }
    }
}
