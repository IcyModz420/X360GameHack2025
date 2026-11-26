using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Formats
{
    /// <summary>
    /// Games on Demand ("GOD") packages: a small LIVE / PIRS / CON header file plus a ".data" folder of Data0000,
    /// Data0001... files that together hold a disc's game partition (the SVOD layout).
    ///
    /// Layout, as read from a Microsoft-published package and checked hash by hash:
    ///   each data file  = 1 master hash block, then up to 203 groups of { 1 hash block, up to 204 data blocks }
    ///                     (4 KB blocks, so a full file is 0xA290000 bytes)
    ///   hash block      = SHA-1 of each of the 204 data blocks that follow it
    ///   master block    = SHA-1 of each hash block of the file, and at 0xFDC the SHA-1 of the NEXT file's master block
    ///   header 0x379    = SVOD descriptor: root hash (SHA-1 of Data0000's master block) at 0x37D, flags at 0x391
    ///                     (0x40 = "enhanced GDF"), data block count (3 bytes, big-endian) at 0x392, start block
    ///                     (3 bytes, little-endian) at 0x395; data file count at 0x39D, descriptor type 1 at 0x3A9
    /// Where the data sits in the disc's game partition:
    ///   plain layout    : data block i  ->  partition offset (start + i) * 0x1000
    ///   enhanced GDF    : block 0 is the volume descriptor (partition offset 0x10000); block i > 0 -> (start + i - 1) * 0x1000
    /// </summary>
    public sealed class GodPackage
    {
        public const int BlockSize = 0x1000, BlocksPerTable = 204, TablesPerFile = 203;
        public const long MaxDataFileSize = (1 + (long)TablesPerFile * (1 + BlocksPerTable)) * BlockSize;   // 0xA290000

        public string HeaderPath { get; private set; }
        public string DataFolder { get; private set; }
        public StfsPackage Info { get; private set; }
        public bool EnhancedGdf { get; private set; }
        public int StartBlock { get; private set; }
        public long DataBlocks { get; private set; }            // counted from the data files
        public IReadOnlyList<string> DataFiles { get; private set; }
        /// <summary>
        /// The header carries no root hash. ISO2God leaves it empty when a package has only one data file; the data is then
        /// checked against the package's own hash tables, from the master block down.
        /// </summary>
        public bool RootHashMissing { get; private set; }
        private byte[] _rootHash;
        private bool? _xsf;

        /// <summary>The root hash the header carries (SHA-1 of Data0000's master block).</summary>
        public byte[] RootHash => (byte[])_rootHash.Clone();

        /// <summary>The disc inside, readable like an image - see <see cref="GodPartitionStream"/>.</summary>
        public GodPartitionStream OpenPartition() => new GodPartitionStream(this);

        /// <summary>Size of the game-partition image this package unpacks to with every sector in its place.</summary>
        public long IsoLength => EnhancedGdf ? (StartBlock + DataBlocks - 1) * BlockSize : (StartBlock + DataBlocks) * BlockSize;

        /// <summary>True when the package's first data block starts with the "XSF" lead-in (made from a whole game partition).</summary>
        public bool HasXsfHeader
        {
            get
            {
                if (_xsf == null)
                {
                    var b = new byte[3];
                    using (var fs = new FileStream(DataFiles[0], FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        fs.Position = 2 * BlockSize;           // master block, first hash block, then the first data block
                        _xsf = fs.Read(b, 0, 3) == 3 && b[0] == 'X' && b[1] == 'S' && b[2] == 'F';
                    }
                }
                return _xsf.Value;
            }
        }

        /// <summary>
        /// Whether the compact layout differs from the exact one for this package: only a Microsoft-made ("enhanced") package
        /// without the lead-in has the empty stretch to drop.
        /// </summary>
        public bool Compacts(GodIsoLayout layout) => layout == GodIsoLayout.Compact && EnhancedGdf && !HasXsfHeader;

        /// <summary>Size of the disc image GOD to ISO writes.</summary>
        public long OutputLength(GodIsoLayout layout) => Compacts(layout) ? XsfHeader.Size + DataBlocks * BlockSize : IsoLength;

        /// <summary>True when the header describes an SVOD (Games on Demand) volume rather than an ordinary STFS package.</summary>
        public static bool IsGod(string headerPath)
        {
            try
            {
                byte[] h = BE.ReadHead(headerPath, 0x3B0);
                if (h.Length < 0x3B0) return false;
                string magic = Encoding.ASCII.GetString(h, 0, 4);
                return (magic == "LIVE" || magic == "PIRS" || magic == "CON ") && BE.U32(h, 0x3A9) == 1;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>Games on Demand packages in a folder and every folder below it (a header with its .data folder next to it).</summary>
        public static List<string> FindIn(string folder, int max = 2000)
        {
            var list = new List<string>();
            var pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0 && list.Count < max)
            {
                string dir = pending.Pop();
                string[] files, dirs;
                try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { continue; }
                foreach (var f in files)
                    if (Directory.Exists(f + ".data") && IsGod(f)) list.Add(f);
                foreach (var d in dirs)
                    if (!d.EndsWith(".data", StringComparison.OrdinalIgnoreCase)) pending.Push(d);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        internal static long DataBlocksIn(long fileLength)
        {
            long blocks = fileLength / BlockSize - 1;                      // without the master block
            if (blocks <= 0) return 0;
            long tables = (blocks + BlocksPerTable) / (BlocksPerTable + 1);
            return blocks - tables;
        }

        public static GodPackage Open(string headerPath)
        {
            byte[] h = BE.ReadHead(headerPath, 0x400);
            if (h.Length < 0x400) throw new InvalidDataException("The file is too small to be a package header.");
            string magic = Encoding.ASCII.GetString(h, 0, 4);
            if (magic != "LIVE" && magic != "PIRS" && magic != "CON ") throw new InvalidDataException("Not an Xbox 360 package.");
            if (BE.U32(h, 0x3A9) != 1) throw new InvalidDataException("This package is not a Games on Demand (SVOD) package - it holds its files itself.");

            var g = new GodPackage
            {
                HeaderPath = headerPath,
                DataFolder = headerPath + ".data",
                Info = StfsPackage.Read(headerPath),
                EnhancedGdf = (h[0x391] & 0x40) != 0,
                StartBlock = h[0x395] | h[0x396] << 8 | h[0x397] << 16,
                _rootHash = h.Skip(0x37D).Take(20).ToArray()
            };
            if (!Directory.Exists(g.DataFolder)) throw new DirectoryNotFoundException("The package's data folder is missing: " + g.DataFolder);

            int count = (int)BE.U32(h, 0x39D);
            var files = new List<string>();
            for (int i = 0; ; i++)
            {
                string f = Path.Combine(g.DataFolder, "Data" + i.ToString("D4"));
                if (!File.Exists(f)) break;
                files.Add(f);
            }
            if (files.Count == 0) throw new FileNotFoundException("No Data0000 file in " + g.DataFolder);
            if (count != files.Count) throw new InvalidDataException("The header lists " + count + " data files but " + files.Count + " are present.");
            long blocks = 0;
            for (int i = 0; i < files.Count; i++)
            {
                long len = new FileInfo(files[i]).Length;
                if (len % BlockSize != 0 || len < 3 * BlockSize || len > MaxDataFileSize || (i < files.Count - 1 && len != MaxDataFileSize))
                    throw new InvalidDataException(Path.GetFileName(files[i]) + " has an unexpected size (" + len + " bytes).");
                blocks += DataBlocksIn(len);
            }
            g.DataFiles = files;
            g.DataBlocks = blocks;
            g.RootHashMissing = g._rootHash.All(b => b == 0);
            if (g.RootHashMissing && files.Count > 1) throw new InvalidDataException("The header's root hash is empty although the package has " + files.Count + " data files - the header is damaged.");
            if (g.EnhancedGdf && blocks < 2) throw new InvalidDataException("The package holds no game data.");
            return g;
        }

        private long IsoOffset(long dataBlock) =>
            EnhancedGdf ? (dataBlock == 0 ? XisoImage.HeaderOffset : (StartBlock + dataBlock - 1) * BlockSize) : (StartBlock + dataBlock) * BlockSize;

        /// <summary>Checks every hash of every data file (the whole chain from the header's root hash) without writing anything.</summary>
        public void Verify(IProgress<TransferProgress> progress, CancellationToken ct) => Walk(null, null, progress, ct);

        /// <summary>Rebuilds the disc's game partition with every sector in its place (<see cref="GodIsoLayout.Exact"/>).</summary>
        public void ConvertToIso(string outputIso, IProgress<TransferProgress> progress, CancellationToken ct)
            => ConvertToIso(outputIso, new GodIsoOptions { Layout = GodIsoLayout.Exact }, progress, ct);

        /// <summary>
        /// Rebuilds the disc image. Every block is checked against the package's hashes on the way; a mismatch stops the
        /// conversion and nothing is left behind. The finished image is read back as an Xbox disc (every folder, every file
        /// inside the image) before it gets its name.
        /// </summary>
        public GodIsoResult ConvertToIso(string outputIso, GodIsoOptions options, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            options = options ?? new GodIsoOptions();
            var result = new GodIsoResult { Path = outputIso, Compacted = Compacts(options.Layout) };
            long length = OutputLength(options.Layout);
            string tmp = outputIso + ".part";
            try
            {
                using (var o = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20))
                {
                    o.SetLength(length);
                    if (result.Compacted)
                    {
                        // god2iso: the lead-in the package left out, then the data block after block; the folder tables then point
                        // (start block x 2 - 34) sectors lower, because the empty stretch before the start block is gone
                        byte[] lead = XsfHeader.Build(length, true, options.Tag);
                        o.Position = 0;
                        o.Write(lead, 0, lead.Length);
                        result.LeadInWritten = true;
                        Walk(o, i => XsfHeader.Size + i * BlockSize, progress, ct);
                        result.SectorsMoved = (long)StartBlock * 2 - 34;
                        result.EntriesMoved = FixSectors(o, result.SectorsMoved, length);
                    }
                    else Walk(o, IsoOffset, progress, ct);
                    if (options.FixCreateIsoGoodHeader) result.CreateIsoGoodFixed = FixCreateIsoGood(o, options.Tag);
                    o.Flush(true);
                }
                result.Files = CheckImage(tmp);
                if (File.Exists(outputIso)) File.Delete(outputIso);
                File.Move(tmp, outputIso);
                result.Length = length;
                return result;
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }

        /// <param name="place">Where each data block goes in the image (null = only check).</param>
        private void Walk(FileStream iso, Func<long, long> place, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            var state = new TransferProgress { Total = DataBlocks * BlockSize };
            byte[] expectedMaster = _rootHash;
            var master = new byte[BlockSize];
            var table = new byte[BlockSize];
            var run = new byte[BlocksPerTable * BlockSize];
            long index = 0;
            using (var sha = SHA1.Create())
            {
                for (int f = 0; f < DataFiles.Count; f++)
                {
                    string name = Path.GetFileName(DataFiles[f]);
                    state.File = name;
                    using (var fs = new FileStream(DataFiles[f], FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                    {
                        ReadFully(fs, master, BlockSize, name);
                        if (!(f == 0 && RootHashMissing) && !sha.ComputeHash(master).SequenceEqual(expectedMaster))
                            throw new InvalidDataException(name + ": the master hash block does not match " + (f == 0 ? "the header's root hash" : "the previous data file") + " - the package is damaged or files are mixed up.");
                        expectedMaster = master.Skip(0xFDC).Take(20).ToArray();

                        long left = fs.Length / BlockSize - 1;
                        for (int t = 0; left > 0; t++)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (t >= TablesPerFile) throw new InvalidDataException(name + " holds more hash groups than a data file can.");
                            ReadFully(fs, table, BlockSize, name);
                            left--;
                            if (!Same(sha.ComputeHash(table), master, t * 20))
                                throw new InvalidDataException(name + ": hash block " + t + " does not match the master hash block.");
                            int n = (int)Math.Min(BlocksPerTable, left);
                            ReadFully(fs, run, n * BlockSize, name);
                            left -= n;
                            for (int i = 0; i < n; i++)
                                if (!Same(sha.ComputeHash(run, i * BlockSize, BlockSize), table, i * 20))
                                    throw new InvalidDataException(name + ": data block " + (index + i) + " does not match its hash - the package is damaged.");
                            if (iso != null && n > 0)
                            {
                                int first = 0;
                                if (index == 0 && place(1) != place(0) + BlockSize)
                                {
                                    iso.Position = place(0);
                                    iso.Write(run, 0, BlockSize);
                                    first = 1;
                                }
                                if (n > first)
                                {
                                    iso.Position = place(index + first);
                                    iso.Write(run, first * BlockSize, (n - first) * BlockSize);
                                }
                            }
                            index += n;
                            state.Done += (long)n * BlockSize;
                            progress?.Report(state);
                        }
                    }
                }
            }
            if (expectedMaster.Any(b => b != 0)) throw new InvalidDataException("The last data file points at another one that is missing.");
        }

        /// <summary>
        /// Moves every sector number in the image's folder tables down by <paramref name="by"/> (god2iso's sector fix): the root's
        /// in the volume descriptor, then each table's entries, found by following the tables' trees. A zero-length entry that
        /// pointed into the dropped stretch gets sector 0; anything with data there means the package is not what it seems.
        /// </summary>
        private static int FixSectors(FileStream iso, long by, long length)
        {
            if (by <= 0) return 0;
            var b4 = new byte[8];
            iso.Position = XisoImage.HeaderOffset + 20;
            ReadFully(iso, b4, 8, "the image");
            uint rootSector = BE.LE32(b4, 0), rootSize = BE.LE32(b4, 4);
            uint newRoot = Shift(rootSector, by, rootSize, "the root folder");
            iso.Position = XisoImage.HeaderOffset + 20;
            iso.Write(BitConverter.GetBytes(newRoot), 0, 4);

            int moved = 0;
            var queue = new Queue<(uint Sector, uint Size, string Path)>();
            if (rootSize > 0) queue.Enqueue((newRoot, rootSize, ""));
            var done = new HashSet<uint>();
            while (queue.Count > 0)
            {
                var (sector, size, path) = queue.Dequeue();
                if (!done.Add(sector)) throw new InvalidDataException("A folder table is referenced twice - the image looks corrupt.");
                if (size > 64u * 1024 * 1024) throw new InvalidDataException("A folder table is impossibly large.");
                int len = (int)GdfTable.Sectors(size) * GdfTable.SectorSize;
                long at = (long)sector * GdfTable.SectorSize;
                if (at + len > length) throw new InvalidDataException("The folder table of \\" + path + " lies past the end of the image.");
                var t = new byte[len];
                iso.Position = at;
                ReadFully(iso, t, len, "a folder table");

                var stack = new Stack<int>();
                var seen = new HashSet<int>();
                stack.Push(0);
                bool first = true;
                while (stack.Count > 0)
                {
                    int pos = stack.Pop() * 4;
                    if (pos + 14 > len) throw new InvalidDataException("A folder entry points outside its table.");
                    if (GdfTable.LE16(t, pos) == 0xFFFF)
                    {
                        if (pos == 0 && first) break;
                        pos += GdfTable.SectorSize - pos % GdfTable.SectorSize;
                        if (pos + 14 > len || GdfTable.LE16(t, pos) == 0xFFFF) throw new InvalidDataException("A folder entry points at padding.");
                    }
                    first = false;
                    if (!seen.Add(pos)) throw new InvalidDataException("A folder table contains a loop.");
                    ushort left = GdfTable.LE16(t, pos), right = GdfTable.LE16(t, pos + 2);
                    uint s = BE.LE32(t, pos + 4), sz = BE.LE32(t, pos + 8);
                    byte attr = t[pos + 12];
                    int nameLen = t[pos + 13];
                    if (pos + 14 + nameLen > len) throw new InvalidDataException("A folder entry is cut off.");
                    string name = Encoding.ASCII.GetString(t, pos + 14, nameLen);
                    uint ns = s == 0 ? 0 : Shift(s, by, sz, path + "\\" + name);
                    if (ns != s) { XsfHeader.PutLE32(t, pos + 4, ns); moved++; }
                    if ((attr & 0x10) != 0 && sz > 0) queue.Enqueue((ns, sz, path.Length == 0 ? name : path + "\\" + name));
                    if (right != 0) stack.Push(right);
                    if (left != 0) stack.Push(left);
                }
                iso.Position = at;
                iso.Write(t, 0, len);
            }
            return moved;
        }

        private static uint Shift(uint sector, long by, uint size, string what)
        {
            long n = sector - by;
            if (n >= XisoImage.HeaderOffset / GdfTable.SectorSize + 2) return (uint)n;
            if (size == 0) return 0;
            throw new InvalidDataException(what + " lies in the stretch the package left out (sector 0x" + sector.ToString("X") + ") - the package is not laid out as its header says.");
        }

        /// <summary>god2iso's "CreateIsoGood" fix: a lead-in that says the image is 0x277C00 bytes long is replaced by a correct one.</summary>
        private static bool FixCreateIsoGood(FileStream iso, string tag)
        {
            if (iso.Length < XsfHeader.Size + XisoImage.HeaderOffset) return false;
            var b = new byte[8];
            iso.Position = 8;
            ReadFully(iso, b, 8, "the image");
            if (BitConverter.ToInt64(b, 0) != XsfHeader.CreateIsoGoodSize) return false;
            byte[] lead = XsfHeader.Build(iso.Length, true, tag);
            iso.Position = 0;
            iso.Write(lead, 0, lead.Length);
            return true;
        }

        /// <summary>Opens the image as an Xbox disc, reads every folder table and checks that every file lies inside the image. The number of files.</summary>
        private static int CheckImage(string path)
        {
            using (var x = XisoImage.Open(path))
            {
                if (x.PartitionOffset != 0) throw new InvalidDataException("The rebuilt image does not start with a game partition.");
                int files = 0;
                void Check(List<XisoEntry> list)
                {
                    foreach (var e in list)
                    {
                        if (e.IsDirectory) { Check(e.Children); continue; }
                        files++;
                        if (e.Size > 0 && (long)e.Sector * XisoImage.SectorSize + e.Size > x.Length)
                            throw new InvalidDataException(e.RelativePath + " lies past the end of the rebuilt image.");
                    }
                }
                Check(x.ReadRoot());
                return files;
            }
        }

        private static bool Same(byte[] hash, byte[] table, int offset)
        {
            for (int i = 0; i < 20; i++) if (hash[i] != table[offset + i]) return false;
            return true;
        }

        private static void ReadFully(Stream s, byte[] buf, int count, string name)
        {
            int n = 0;
            while (n < count)
            {
                int r = s.Read(buf, n, count - n);
                if (r <= 0) throw new EndOfStreamException(name + " is cut short.");
                n += r;
            }
        }
    }

    /// <summary>
    /// The game partition a Games on Demand package holds, read straight from its data files with every sector at its place
    /// (what a Microsoft-made package leaves out reads as zeros) - so the package can be read like a disc image (files
    /// extracted, converted again) without writing one. The master blocks are checked against the header's root hash when it
    /// opens; each hash block against its master block, and each data block against its hash block, as they are read.
    /// </summary>
    public sealed class GodPartitionStream : Stream
    {
        private const int Block = GodPackage.BlockSize;
        private const long PerFile = (long)GodPackage.TablesPerFile * GodPackage.BlocksPerTable;
        private readonly GodPackage _g;
        private readonly FileStream[] _files;
        private readonly byte[][] _masters;
        private readonly Dictionary<long, byte[]> _tables = new Dictionary<long, byte[]>();
        private readonly byte[] _block = new byte[Block];
        private readonly SHA1 _sha = SHA1.Create();
        private long _blockIndex = -1, _pos;

        public GodPartitionStream(GodPackage g)
        {
            _g = g;
            _files = new FileStream[g.DataFiles.Count];
            _masters = new byte[g.DataFiles.Count][];
            try
            {
                for (int f = 0; f < _files.Length; f++)
                {
                    _files[f] = new FileStream(g.DataFiles[f], FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                    _masters[f] = new byte[Block];
                    ReadAt(_files[f], 0, _masters[f], 0, Block, Path.GetFileName(g.DataFiles[f]));
                }
                for (int f = 0; f < _files.Length; f++)
                {
                    byte[] want = f == 0 ? g.RootHash : _masters[f - 1].Skip(0xFDC).Take(20).ToArray();
                    if (f == 0 && g.RootHashMissing) continue;
                    if (!_sha.ComputeHash(_masters[f]).SequenceEqual(want))
                        throw new InvalidDataException(Path.GetFileName(g.DataFiles[f]) + ": the master hash block does not match " + (f == 0 ? "the header's root hash" : "the previous data file") + " - the package is damaged or files are mixed up.");
                }
                if (_masters[_files.Length - 1].Skip(0xFDC).Take(20).Any(b => b != 0)) throw new InvalidDataException("The last data file points at another one that is missing.");
            }
            catch { Dispose(true); throw; }
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _g.IsoLength;
        public override long Position { get => _pos; set => _pos = value; }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
        {
            _pos = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _pos + offset : Length + offset;
            return _pos;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int done = 0;
            while (done < count && _pos < Length)
            {
                long pb = _pos / Block;
                int inBlock = (int)(_pos % Block);
                int n = (int)Math.Min(Math.Min(count - done, Block - inBlock), Length - _pos);
                if (pb != _blockIndex)
                {
                    long di = DataBlockOf(pb);
                    if (di < 0) Array.Clear(_block, 0, Block); else ReadDataBlock(di, _block);
                    _blockIndex = pb;
                }
                Buffer.BlockCopy(_block, inBlock, buffer, offset + done, n);
                done += n;
                _pos += n;
            }
            return done;
        }

        /// <summary>Which data block holds partition block <paramref name="pb"/> (-1: none - the package leaves it out).</summary>
        private long DataBlockOf(long pb)
        {
            if (!_g.EnhancedGdf)
            {
                long i = pb - _g.StartBlock;
                return i >= 0 && i < _g.DataBlocks ? i : -1;
            }
            if (pb == XisoImage.HeaderOffset / Block) return 0;
            long j = pb - _g.StartBlock + 1;
            return pb >= _g.StartBlock && j >= 1 && j < _g.DataBlocks ? j : -1;
        }

        private void ReadDataBlock(long i, byte[] into)
        {
            int f = (int)(i / PerFile);
            long inFile = i % PerFile;
            long group = inFile / GodPackage.BlocksPerTable;
            int k = (int)(inFile % GodPackage.BlocksPerTable);
            long groupAt = Block + group * (GodPackage.BlocksPerTable + 1L) * Block;
            long key = (long)f << 32 | group;
            if (!_tables.TryGetValue(key, out byte[] table))
            {
                table = new byte[Block];
                ReadAt(_files[f], groupAt, table, 0, Block, Path.GetFileName(_g.DataFiles[f]));
                byte[] h = _sha.ComputeHash(table);
                for (int b = 0; b < 20; b++)
                    if (h[b] != _masters[f][group * 20 + b]) throw new InvalidDataException(Path.GetFileName(_g.DataFiles[f]) + ": hash block " + group + " does not match the master hash block.");
                if (_tables.Count > 512) _tables.Clear();
                _tables[key] = table;
            }
            ReadAt(_files[f], groupAt + Block + (long)k * Block, into, 0, Block, Path.GetFileName(_g.DataFiles[f]));
            byte[] d = _sha.ComputeHash(into, 0, Block);
            for (int b = 0; b < 20; b++)
                if (d[b] != table[k * 20 + b]) throw new InvalidDataException("Data block " + i + " of the package does not match its hash - the package is damaged.");
        }

        private static void ReadAt(FileStream fs, long at, byte[] buf, int off, int count, string name)
        {
            fs.Position = at;
            int n = 0;
            while (n < count)
            {
                int r = fs.Read(buf, off + n, count - n);
                if (r <= 0) throw new EndOfStreamException(name + " is cut short.");
                n += r;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var f in _files) f?.Dispose();
                _sha.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Where a game can come from and go to: a disc image, a Games on Demand package, or its XEX version (an extracted folder).</summary>
    public enum GameFormat { Image = 0, Package = 1, Folder = 2 }

    /// <summary>Opening any of them as a disc.</summary>
    public static class GameSources
    {
        public static GameFormat FormatOf(string path) => Directory.Exists(path) ? GameFormat.Folder : GodPackage.IsGod(path) ? GameFormat.Package : GameFormat.Image;

        public static string Describe(GameFormat f) => f == GameFormat.Package ? "Games on Demand" : f == GameFormat.Folder ? "XEX version" : "disc image";

        /// <summary>A disc image, or the disc inside a Games on Demand package (read through <see cref="GodPartitionStream"/>).</summary>
        public static XisoImage OpenImage(string path)
        {
            if (File.Exists(path) && GodPackage.IsGod(path)) return XisoImage.Open(GodPackage.Open(path).OpenPartition(), path);
            return XisoImage.Open(path);
        }
    }

    /// <summary>What GOD to ISO did.</summary>
    public sealed class GodIsoResult
    {
        public string Path;
        public long Length;
        /// <summary>The empty stretch of a Microsoft-made package was dropped (compact layout).</summary>
        public bool Compacted;
        /// <summary>A fresh "XSF" lead-in was written (the package did not carry one).</summary>
        public bool LeadInWritten;
        /// <summary>Sectors every folder and file moved down by, and how many entries changed (compact layout).</summary>
        public long SectorsMoved;
        public int EntriesMoved;
        public bool CreateIsoGoodFixed;
        /// <summary>Files in the finished image (all inside it - checked).</summary>
        public int Files;
    }

    public sealed class GodBuildResult
    {
        public string HeaderPath, DataFolder, DisplayName;
        public uint TitleId, MediaId, ContentType;
        public int DataFiles;
        public long DataBytes;
        /// <summary>Bytes of the image that went into the package (after padding removal).</summary>
        public long StoredBytes;
        public GodPadding Padding;
        /// <summary>Where the rebuilt image was saved (Full padding removal with a path given), else null.</summary>
        public string RebuiltIsoPath;
        /// <summary>Things worth knowing that did not stop the conversion: no name or picture in the game, tables kept as stored, ...</summary>
        public string Notes = "";
    }

    /// <summary>What goes into a new Games on Demand package. Everything has a default taken from the disc image.</summary>
    public sealed class GodBuildOptions
    {
        /// <summary>The header's details (IDs, disc, platform, name, picture) - null = read from the image.</summary>
        public GodTitleInfo Info;
        /// <summary>Name shown on the dashboard (null = <see cref="GodTitleInfo.DashboardName"/>).</summary>
        public string DisplayName;
        /// <summary>ISO2God's "Auto-rename multi-disc games": " Disc N" after the game's name when it comes on several discs.</summary>
        public bool AddDiscNumber = true;
        /// <summary>PNG for the dashboard tile (null = the game's own picture).</summary>
        public byte[] ThumbnailPng;
        public GodPadding Padding = GodPadding.Partial;
        /// <summary>Full padding removal: also save the rebuilt image here (null = it is only used for the package).</summary>
        public string RebuiltIsoPath;
        /// <summary>
        /// Files of the game replaced by files on this PC (path inside the image -> file), e.g. its XEX unlocked or converted for
        /// a devkit. Their size may differ, so the image is then rebuilt (Full padding removal) whatever <see cref="Padding"/> says.
        /// </summary>
        public IDictionary<string, string> Replacements;
        public GodNaming Naming = GodNaming.Iso2God;
        /// <summary>Write Microsoft's "enhanced GDF" layout instead of the plain one (used to reproduce Microsoft's own packages in tests).</summary>
        public bool EnhancedGdf;
        /// <summary>Start block for the enhanced layout: where the first block after the volume descriptor sits in the partition.</summary>
        public int EnhancedStartBlock;
        /// <summary>Check every hash of the finished package before reporting success.</summary>
        public bool VerifyAfter = true;
    }

    /// <summary>Builds a Games on Demand package from an Xbox 360 (or original Xbox) disc image.</summary>
    public static class GodBuilder
    {
        private const int Block = GodPackage.BlockSize, PerTable = GodPackage.BlocksPerTable, PerFile = GodPackage.TablesPerFile;
        private const int HeaderFileSize = 0xB000, HeaderSize = 0xAD0E;

        /// <summary>Content folder of a package: {title ID}\{content type}.</summary>
        public static string ContentFolder(uint titleId, uint contentType) => Path.Combine(titleId.ToString("X8"), contentType.ToString("X8"));

        /// <summary>
        /// Writes {outputRoot}\{title id}\{content type}\{name} and its {name}.data folder. Every block gets its SHA-1 in the hash
        /// tables and the files are chained up to the header's root hash; the finished package is checked hash by hash.
        /// What is stored depends on <see cref="GodBuildOptions.Padding"/>, exactly as ISO2God decides it.
        /// </summary>
        public static GodBuildResult FromIso(string isoPath, string outputRoot, GodBuildOptions options, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            options = options ?? new GodBuildOptions();
            using (var iso = GameSources.OpenImage(isoPath))
            {
                var root = iso.ReadRoot();
                var info = options.Info != null ? options.Info.Clone() : GodTitleInfo.Read(iso, root, ct);
                if (info.TitleId == 0) throw new InvalidDataException("The game's title ID is 0 - enter the right one.");
                if (info.DiscNumber == 0) info.DiscNumber = 1;
                if (info.DiscCount == 0) info.DiscCount = 1;

                var result = new GodBuildResult { ContentType = info.ContentType, TitleId = info.TitleId, MediaId = info.MediaId, Padding = options.Padding, Notes = info.Notes ?? "" };
                result.DisplayName = !string.IsNullOrWhiteSpace(options.DisplayName) ? options.DisplayName.Trim() : info.DashboardName(isoPath, options.AddDiscNumber);
                if (result.DisplayName.Length > 127) result.DisplayName = result.DisplayName.Substring(0, 127);
                byte[] thumbnail = GodThumbnail.Fit(options.ThumbnailPng ?? info.Thumbnail);

                // which blocks go in
                IGodSource source;
                GdfRebuild rebuilt = null;
                int startBlock = 0;
                long descriptorBlocks;
                bool replacing = options.Replacements != null && options.Replacements.Count > 0;
                if (replacing && options.EnhancedGdf) throw new ArgumentException("Files cannot be replaced in Microsoft's layout.");
                if (replacing)
                {
                    rebuilt = GdfRebuild.Plan(iso, ct, options.Replacements);
                    source = rebuilt;
                    result.Padding = GodPadding.Full;
                    result.Notes = (result.Notes + " " + rebuilt.Notes + (options.Padding != GodPadding.Full ? "The image was rebuilt (Full padding removal) so the replaced files fit." : "")).Trim();
                    descriptorBlocks = source.Blocks;
                }
                else if (options.EnhancedGdf)
                {
                    long partitionBlocks = (CropEnd(iso, root) + Block - 1) / Block;
                    startBlock = options.EnhancedStartBlock;
                    if (startBlock < 0x11 || startBlock >= partitionBlocks) throw new ArgumentException("The enhanced layout needs a start block inside the partition, after the volume descriptor.");
                    int sb = startBlock;
                    source = new PartitionSource(iso, partitionBlocks - startBlock + 1, i => i == 0 ? XisoImage.HeaderOffset / Block : sb + i - 1);
                    descriptorBlocks = source.Blocks - 1;
                }
                else
                {
                    switch (options.Padding)
                    {
                        case GodPadding.None:
                            source = new PartitionSource(iso, (iso.Length - iso.PartitionOffset + Block - 1) / Block, null);
                            break;
                        case GodPadding.Full:
                            rebuilt = GdfRebuild.Plan(iso, ct);
                            source = rebuilt;
                            result.Notes = (result.Notes + " " + rebuilt.Notes).Trim();
                            break;
                        default:
                            source = new PartitionSource(iso, (CropEnd(iso, root) + Block - 1) / Block, null);
                            break;
                    }
                    descriptorBlocks = source.Blocks;
                }
                long dataBlocks = source.Blocks;
                if (dataBlocks > 0xFFFFFF) throw new InvalidDataException("The image is too large for a Games on Demand package.");
                result.StoredBytes = rebuilt != null ? rebuilt.Length : dataBlocks * Block;

                string dir = Path.Combine(outputRoot, ContentFolder(info.TitleId, info.ContentType));
                Directory.CreateDirectory(dir);
                string work = Path.Combine(dir, "building-" + Guid.NewGuid().ToString("N") + ".data");
                Directory.CreateDirectory(work);
                string rebuiltTmp = null;
                FileStream rebuiltOut = null;
                bool done = false;
                try
                {
                    if (rebuilt != null && !string.IsNullOrWhiteSpace(options.RebuiltIsoPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.RebuiltIsoPath)));
                        rebuiltTmp = options.RebuiltIsoPath + ".part";
                        rebuiltOut = new FileStream(rebuiltTmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                    }

                    int fileCount = (int)((dataBlocks + (long)PerFile * PerTable - 1) / ((long)PerFile * PerTable));
                    var masters = new byte[fileCount][];
                    var state = new TransferProgress { Total = dataBlocks * Block };
                    var run = new byte[PerTable * Block];
                    var table = new byte[Block];
                    long index = 0, totalFileBytes = 0;
                    using (var sha = SHA1.Create())
                    {
                        for (int f = 0; f < fileCount; f++)
                        {
                            string name = "Data" + f.ToString("D4");
                            state.File = name;
                            masters[f] = new byte[Block];
                            using (var o = new FileStream(Path.Combine(work, name), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                            {
                                o.Write(masters[f], 0, Block);     // placeholder; the real master block is written once the chain is known
                                for (int t = 0; t < PerFile && index < dataBlocks; t++)
                                {
                                    ct.ThrowIfCancellationRequested();
                                    int n = (int)Math.Min(PerTable, dataBlocks - index);
                                    Array.Clear(table, 0, Block);
                                    for (int i = 0; i < n; i++)
                                    {
                                        source.ReadBlock(index + i, run, i * Block);
                                        Buffer.BlockCopy(sha.ComputeHash(run, i * Block, Block), 0, table, i * 20, 20);
                                    }
                                    rebuiltOut?.Write(run, 0, n * Block);
                                    Buffer.BlockCopy(sha.ComputeHash(table), 0, masters[f], t * 20, 20);
                                    o.Write(table, 0, Block);
                                    o.Write(run, 0, n * Block);
                                    index += n;
                                    state.Done += (long)n * Block;
                                    progress?.Report(state);
                                }
                                totalFileBytes += o.Length;
                            }
                        }
                        // Chain the files from the last one back: each master block ends with the hash of the next one.
                        for (int f = fileCount - 1; f >= 0; f--)
                        {
                            if (f + 1 < fileCount) Buffer.BlockCopy(sha.ComputeHash(masters[f + 1]), 0, masters[f], 0xFDC, 20);
                            using (var o = new FileStream(Path.Combine(work, "Data" + f.ToString("D4")), FileMode.Open, FileAccess.Write, FileShare.None))
                                o.Write(masters[f], 0, Block);
                        }

                        byte[] head = BuildHeader(result, info, sha.ComputeHash(masters[0]), options.EnhancedGdf, descriptorBlocks, startBlock, fileCount, totalFileBytes, thumbnail);
                        string pkgName = options.Naming == GodNaming.Microsoft
                            ? BE.Hex(head, 0x32C, 16) + (info.TitleId >> 24).ToString("X2")      // Microsoft: 16 bytes of the header hash + the title ID's first byte
                            : GodNames.Iso2God(info.TitleId, info.MediaId, info.DiscNumber, info.DiscCount);
                        result.HeaderPath = Path.Combine(dir, pkgName);
                        result.DataFolder = result.HeaderPath + ".data";
                        if (File.Exists(result.HeaderPath)) File.Delete(result.HeaderPath);
                        if (Directory.Exists(result.DataFolder)) Directory.Delete(result.DataFolder, true);
                        File.WriteAllBytes(result.HeaderPath, head);
                        Directory.Move(work, result.DataFolder);
                        result.DataFiles = fileCount;
                        result.DataBytes = totalFileBytes;
                    }

                    if (rebuiltOut != null)
                    {
                        rebuiltOut.SetLength(rebuilt.Length);
                        rebuiltOut.Flush(true);
                        rebuiltOut.Dispose();
                        rebuiltOut = null;
                        using (var check = XisoImage.Open(rebuiltTmp)) check.ReadRoot();
                        if (File.Exists(options.RebuiltIsoPath)) File.Delete(options.RebuiltIsoPath);
                        File.Move(rebuiltTmp, options.RebuiltIsoPath);
                        result.RebuiltIsoPath = options.RebuiltIsoPath;
                    }
                    if (options.VerifyAfter) GodPackage.Open(result.HeaderPath).Verify(null, ct);
                    done = true;
                    return result;
                }
                finally
                {
                    (source as IDisposable)?.Dispose();
                    if (rebuiltOut != null) try { rebuiltOut.Dispose(); } catch { }
                    if (rebuiltTmp != null) try { if (File.Exists(rebuiltTmp)) File.Delete(rebuiltTmp); } catch { }
                    if (!done)
                    {
                        try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
                        try { if (result.HeaderPath != null && File.Exists(result.HeaderPath)) File.Delete(result.HeaderPath); } catch { }
                        try { if (result.DataFolder != null && Directory.Exists(result.DataFolder)) Directory.Delete(result.DataFolder, true); } catch { }
                    }
                }
            }
        }

        /// <summary>Bytes the package (header and data files) will take, worked out without writing anything.</summary>
        public static long EstimateBytes(string isoPath, GodPadding padding, CancellationToken ct, IDictionary<string, string> replacements = null)
        {
            using (var iso = GameSources.OpenImage(isoPath))
            {
                long blocks;
                if (replacements != null && replacements.Count > 0) using (var r = GdfRebuild.Plan(iso, ct, replacements)) blocks = r.Blocks;
                else if (padding == GodPadding.None) blocks = (iso.Length - iso.PartitionOffset + Block - 1) / Block;
                else if (padding == GodPadding.Full) using (var r = GdfRebuild.Plan(iso, ct)) blocks = r.Blocks;
                else blocks = (CropEnd(iso, iso.ReadRoot()) + Block - 1) / Block;
                return PackageBytes(blocks);
            }
        }

        /// <summary>Header plus data files for a package of <paramref name="blocks"/> 4 KB blocks.</summary>
        public static long PackageBytes(long blocks)
        {
            long files = (blocks + (long)PerFile * PerTable - 1) / ((long)PerFile * PerTable);
            long tables = (blocks + PerTable - 1) / PerTable;
            return (blocks + tables + files) * Block + HeaderFileSize;
        }

        /// <summary>Bytes of the disc image a game folder makes (an upper bound: every file rounded up, plus folder tables and the lead-in).</summary>
        public static long EstimateFolderImageBytes(string folder, IDictionary<string, string> replacements = null)
        {
            long total = 0x20000 + 0x10000;
            string full = Path.GetFullPath(folder).TrimEnd('\\');
            foreach (var f in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(full.Length + 1);
                long len = replacements != null && replacements.TryGetValue(rel, out string r) ? new FileInfo(r).Length : new FileInfo(f).Length;
                total += (len + 2047) / 2048 * 2048 + 64;
            }
            foreach (var d in Directory.EnumerateDirectories(full, "*", SearchOption.AllDirectories)) total += 2048 + 64;
            return total;
        }

        /// <summary>
        /// A game in its "XEX version" (an extracted folder with default.xex / default.xbe) into a Games on Demand package: the
        /// folder is packed into a disc image the way extract-xiso -c packs it (some files may come from elsewhere - see
        /// <see cref="GodBuildOptions.Replacements"/>), and that image becomes the package. The image is only temporary: it is
        /// written to <paramref name="tempFolder"/> (a drive with room for it that can hold files over 4 GB) and deleted after.
        /// </summary>
        public static GodBuildResult FromFolder(string folder, string outputRoot, string tempFolder, GodBuildOptions options, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            options = options ?? new GodBuildOptions();
            string full = Path.GetFullPath(folder).TrimEnd('\\');
            if (!GodTitleInfo.IsGameFolder(full)) throw new InvalidDataException("The folder has no default.xex or default.xbe - it is not an extracted game.");
            var info = options.Info != null ? options.Info.Clone() : GodTitleInfo.FromFolder(full);
            Directory.CreateDirectory(tempFolder);
            string tmp = Path.Combine(tempFolder, "x360gh-building-" + Guid.NewGuid().ToString("N") + ".iso");
            if (tmp.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The temporary image cannot go inside the game's own folder.");
            try
            {
                // stamped with the game executable's own date, so the same folder always makes the same package
                string exe = File.Exists(Path.Combine(full, "default.xex")) ? Path.Combine(full, "default.xex") : Path.Combine(full, "default.xbe");
                XisoBuilder.CreateFromFolder(full, tmp, false, options.Replacements, File.GetLastWriteTimeUtc(exe), progress, ct);
                var o = new GodBuildOptions
                {
                    Info = info,
                    DisplayName = !string.IsNullOrWhiteSpace(options.DisplayName) ? options.DisplayName : info.DashboardName(full, options.AddDiscNumber),
                    AddDiscNumber = false,
                    ThumbnailPng = options.ThumbnailPng,
                    Padding = GodPadding.None,          // extract-xiso leaves no gaps: the whole image goes in
                    Naming = options.Naming,
                    VerifyAfter = options.VerifyAfter
                };
                var r = FromIso(tmp, outputRoot, o, progress, ct);
                r.Padding = GodPadding.Full;
                if (options.Replacements != null && options.Replacements.Count > 0)
                    r.Notes = (r.Notes + " " + options.Replacements.Count + " file(s) replaced (" + string.Join(", ", options.Replacements.Keys) + ").").Trim();
                return r;
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }

        /// <summary>
        /// Where a temporary disc image of <paramref name="bytes"/> can go: next to the output if that drive has room and can hold
        /// files over 4 GB, else this PC's temp folder, else next to the game. Null when no drive has room.
        /// </summary>
        public static string TempFolderFor(long bytes, params string[] near)
        {
            var candidates = new List<string>();
            foreach (var n in near) if (!string.IsNullOrEmpty(n)) candidates.Add(n);
            candidates.Insert(Math.Min(1, candidates.Count), Path.GetTempPath());
            foreach (var c in candidates)
            {
                try
                {
                    string full = Path.GetFullPath(c);
                    var drive = new DriveInfo(Path.GetPathRoot(full));
                    if (!drive.IsReady) continue;
                    if (bytes >= uint.MaxValue && string.Equals(drive.DriveFormat, "FAT32", StringComparison.OrdinalIgnoreCase)) continue;
                    if (drive.AvailableFreeSpace < bytes + 256L * 1024 * 1024) continue;
                    return full;
                }
                catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException) { }
            }
            return null;
        }

        /// <summary>Bytes of the image Full padding removal rebuilds.</summary>
        public static long EstimateRebuiltBytes(string isoPath, CancellationToken ct)
        {
            using (var iso = GameSources.OpenImage(isoPath)) return GdfRebuild.Plan(iso, ct).Length;
        }

        /// <summary>
        /// Where "Partial" padding removal cuts: ISO2God's rule (every folder table front to back; an entry that starts at or after
        /// the current end moves the end to its own end, in whole sectors), and never before the end of anything the disc's tree
        /// uses - the furthest file or folder and the root table.
        /// </summary>
        internal static long CropEnd(XisoImage iso, List<XisoEntry> root)
        {
            long used = XisoImage.HeaderOffset + 2 * XisoImage.SectorSize;
            Measure(root, ref used);
            used = Math.Max(used, (long)iso.RootSector * XisoImage.SectorSize + iso.RootSize);
            try
            {
                long last = 0;
                Iso2GodLast(GdfTable.ReadRoot(iso), ref last);
                return Math.Max(used, last * XisoImage.SectorSize);
            }
            catch (InvalidDataException) { return used; }
        }

        private static void Iso2GodLast(GdfTable t, ref long last)
        {
            foreach (var e in t.Entries)
            {
                if (!e.Live) continue;
                if (e.Sector >= last) last = (long)e.Sector + GdfTable.Sectors(e.Size);
                if (e.IsDirectory && e.Sub != null) Iso2GodLast(e.Sub, ref last);
            }
        }

        private static void Measure(IEnumerable<XisoEntry> entries, ref long used)
        {
            foreach (var e in entries)
            {
                long end = (long)e.Sector * XisoImage.SectorSize + e.Size;
                if (e.Size > 0 && end > used) used = end;
                if (e.IsDirectory) Measure(e.Children, ref used);
            }
        }

        private static byte[] BuildHeader(GodBuildResult r, GodTitleInfo info, byte[] rootHash, bool enhanced, long descriptorBlocks, int startBlock,
                                          int fileCount, long fileBytes, byte[] thumbnail)
        {
            var h = new byte[HeaderFileSize];
            Encoding.ASCII.GetBytes("LIVE").CopyTo(h, 0);                 // unsigned: only Microsoft can sign LIVE packages (RGH / JTAG load them)
            // license table, as Microsoft's own Games on Demand packages carry it: two "any console" entries
            for (int i = 0; i < 8; i++) h[0x22C + i] = h[0x23C + i] = 0xFF;
            BE.W32(h, 0x234, 1); BE.W32(h, 0x238, 1);
            BE.W32(h, 0x244, 1);
            BE.W32(h, 0x340, HeaderSize);
            BE.W32(h, 0x344, r.ContentType);
            BE.W32(h, 0x348, 2);                                          // metadata version
            // the game's execution info, as the XEX carries it (Microsoft's own packages copy it)
            BE.W32(h, 0x354, info.MediaId);
            BE.W32(h, 0x358, info.Xbox360 ? info.Version : 0);
            BE.W32(h, 0x35C, info.Xbox360 ? info.BaseVersion : 0);
            BE.W32(h, 0x360, info.TitleId);
            h[0x364] = info.Platform;
            h[0x365] = info.ExecutableType;
            h[0x366] = info.DiscNumber;
            h[0x367] = info.DiscCount;
            // SVOD volume descriptor
            h[0x379] = 0x24; h[0x37A] = 5; h[0x37B] = 5; h[0x37C] = 0x11;
            Buffer.BlockCopy(rootHash, 0, h, 0x37D, 20);
            h[0x391] = (byte)(enhanced ? 0x40 : 0);
            h[0x392] = (byte)(descriptorBlocks >> 16); h[0x393] = (byte)(descriptorBlocks >> 8); h[0x394] = (byte)descriptorBlocks;
            h[0x395] = (byte)startBlock; h[0x396] = (byte)(startBlock >> 8); h[0x397] = (byte)(startBlock >> 16);
            BE.W32(h, 0x39D, (uint)fileCount);
            BE.W64(h, 0x3A1, (ulong)fileBytes);
            BE.W32(h, 0x3A9, 1);                                          // descriptor type: SVOD
            BE.WriteUtf16BE(h, 0x411, 0x100, r.DisplayName);
            BE.WriteUtf16BE(h, 0x1691, 0x80, r.DisplayName.Length > 63 ? r.DisplayName.Substring(0, 63) : r.DisplayName);
            h[0x1711] = 0x40;                                             // transfer flags
            if (thumbnail != null && thumbnail.Length > 0 && thumbnail.Length <= GodThumbnail.MaxBytes)
            {
                BE.W32(h, 0x1712, (uint)thumbnail.Length);
                BE.W32(h, 0x1716, (uint)thumbnail.Length);
                Buffer.BlockCopy(thumbnail, 0, h, 0x171A, thumbnail.Length);
                Buffer.BlockCopy(thumbnail, 0, h, 0x571A, thumbnail.Length);
            }
            using (var sha = SHA1.Create()) Buffer.BlockCopy(sha.ComputeHash(h, 0x344, h.Length - 0x344), 0, h, 0x32C, 20);
            return h;
        }
    }
}
