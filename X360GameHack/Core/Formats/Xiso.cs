// =============================================================================
// Native XDVDFS ("XISO") engine: read / list / extract / create / rewrite.
//
// The creation and rewrite logic is a faithful C# port of extract-xiso 2.7.1 so the
// images it writes are byte-identical to extract-xiso's (verified by differential tests
// that build the same folders with both tools and compare every byte).
//
// Portions copyright (c) 2003 in <in@fishtank.com>. All rights reserved.
// Redistribution and use in source and binary forms, with or without modification,
// are permitted provided that the copyright notice and this disclaimer are retained.
// THIS SOFTWARE IS PROVIDED "AS IS" WITHOUT ANY WARRANTY (see the extract-xiso license).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Formats
{
    public enum XisoLayout { Xiso, Xgd2, Xgd3, Xgd1 }

    public sealed class XisoEntry
    {
        public string Name;
        public bool IsDirectory;
        public uint Sector;
        public uint Size;
        public string RelativePath;
        /// <summary>The entry's attribute byte as stored (0x10 folder; Microsoft's discs use 0x80 / 0x90, extract-xiso 0x20 / 0x10).</summary>
        public byte Attributes;
        public List<XisoEntry> Children = new List<XisoEntry>();   // directories only, pre-order (on-disc tree order)

        /// <summary>The attribute byte in words: folder, read-only, hidden, system, archive, normal.</summary>
        public string AttributeText
        {
            get
            {
                var p = new List<string>();
                if ((Attributes & 0x10) != 0) p.Add("folder");
                if ((Attributes & 0x01) != 0) p.Add("read-only");
                if ((Attributes & 0x02) != 0) p.Add("hidden");
                if ((Attributes & 0x04) != 0) p.Add("system");
                if ((Attributes & 0x20) != 0) p.Add("archive");
                if ((Attributes & 0x80) != 0) p.Add("normal");
                return "0x" + Attributes.ToString("X2") + (p.Count > 0 ? " (" + string.Join(", ", p) + ")" : "");
            }
        }
    }

    /// <summary>Read-only access to an Xbox / Xbox 360 disc image.</summary>
    public sealed class XisoImage : IDisposable
    {
        public const int SectorSize = 2048;
        public const long HeaderOffset = 0x10000;
        public const int OptimizedTagOffset = 31337;
        public const string SystemUpdate = "$SystemUpdate";
        internal static readonly byte[] Magic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");
        internal static readonly byte[] OptimizedTagPrefix = Encoding.ASCII.GetBytes("in!xiso");

        // Same probe order as extract-xiso: plain XISO, XGD2 ("global"), XGD3, XGD1.
        private static readonly (XisoLayout Layout, long Offset)[] Probes =
        {
            (XisoLayout.Xiso, 0), (XisoLayout.Xgd2, 0x0FD90000), (XisoLayout.Xgd3, 0x02080000), (XisoLayout.Xgd1, 0x18300000)
        };

        private readonly Stream _fs;
        public string Path { get; }
        public XisoLayout Layout { get; private set; }
        public long PartitionOffset { get; private set; }
        public uint RootSector { get; private set; }
        public uint RootSize { get; private set; }
        public byte[] FileTime { get; private set; } = new byte[8];
        public bool Optimized { get; private set; }
        public long Length => _fs.Length;

        private XisoImage(string path)
        {
            Path = path;
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        }

        private XisoImage(Stream stream, string name)
        {
            Path = name;
            _fs = stream;
        }

        /// <summary>An Xbox disc image held by any readable, seekable stream (e.g. a Games on Demand package's data); the image owns the stream.</summary>
        public static XisoImage Open(Stream stream, string name)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek) throw new ArgumentException("The image's stream must be readable and seekable.");
            var x = new XisoImage(stream, name);
            try { x.Verify(); return x; }
            catch { x.Dispose(); throw; }
        }

        public static string Describe(XisoLayout l)
        {
            switch (l)
            {
                case XisoLayout.Xgd1: return "Original Xbox disc (XGD1 redump)";
                case XisoLayout.Xgd2: return "Xbox 360 disc (XGD2 redump)";
                case XisoLayout.Xgd3: return "Xbox 360 disc (XGD3 redump)";
                default: return "XISO (game partition only)";
            }
        }

        /// <summary>Returns the layout without throwing (null when the file is not an Xbox image).</summary>
        public static XisoLayout? Probe(string path)
        {
            try { using (var x = Open(path)) return x.Layout; }
            catch { return null; }
        }

        public static XisoImage Open(string path)
        {
            var x = new XisoImage(path);
            try { x.Verify(); return x; }
            catch { x.Dispose(); throw; }
        }

        internal void ReadExactly(long pos, byte[] buf, int off, int count)
        {
            lock (_fs)
            {
                _fs.Position = pos;
                int n = 0;
                while (n < count)
                {
                    int r = _fs.Read(buf, off + n, count - n);
                    if (r <= 0) throw new EndOfStreamException("The image is truncated (needed data at 0x" + (pos + n).ToString("X") + ").");
                    n += r;
                }
            }
        }

        /// <summary>Reads what the image holds at <paramref name="pos"/> - fewer bytes at its end; returns how many.</summary>
        internal int ReadAvailable(long pos, byte[] buf, int off, int count) => ReadSome(pos, buf, off, count);

        private int ReadSome(long pos, byte[] buf, int off, int count)
        {
            lock (_fs)
            {
                _fs.Position = pos;
                int n = 0;
                while (n < count)
                {
                    int r = _fs.Read(buf, off + n, count - n);
                    if (r <= 0) break;
                    n += r;
                }
                return n;
            }
        }

        private static bool IsMagic(byte[] b, int off)
        {
            for (int i = 0; i < Magic.Length; i++) if (b[off + i] != Magic[i]) return false;
            return true;
        }

        private void Verify()
        {
            var buf = new byte[SectorSize];
            foreach (var p in Probes)
            {
                if (_fs.Length < HeaderOffset + p.Offset + SectorSize) continue;
                ReadExactly(HeaderOffset + p.Offset, buf, 0, SectorSize);
                if (!IsMagic(buf, 0)) continue;
                if (!IsMagic(buf, 0x7EC)) throw new InvalidDataException("The image header is corrupt (trailing signature missing).");
                Layout = p.Layout;
                PartitionOffset = p.Offset;
                RootSector = BitConverter.ToUInt32(buf, 20);
                RootSize = BitConverter.ToUInt32(buf, 24);
                Buffer.BlockCopy(buf, 28, FileTime, 0, 8);
                if (_fs.Length > OptimizedTagOffset + 8)
                {
                    var tag = new byte[OptimizedTagPrefix.Length];
                    ReadExactly(OptimizedTagOffset, tag, 0, tag.Length);
                    Optimized = true;
                    for (int i = 0; i < tag.Length; i++) if (tag[i] != OptimizedTagPrefix[i]) Optimized = false;
                }
                return;
            }
            throw new InvalidDataException("This is not an Xbox / Xbox 360 disc image (no XDVDFS volume found).");
        }

        public List<XisoEntry> ReadRoot()
            => RootSector == 0 || RootSize == 0 ? new List<XisoEntry>() : ReadDirectory(RootSector, RootSize, "", 0, new HashSet<uint>());

        /// <summary>Reads one directory table (and its sub-directories) in on-disc pre-order.</summary>
        private List<XisoEntry> ReadDirectory(uint sector, uint size, string parent, int depth, HashSet<uint> visitedTables)
        {
            var list = new List<XisoEntry>();
            if (size == 0) return list;
            if (depth > 64) throw new InvalidDataException("Folders are nested too deeply - the image looks corrupt.");
            if (!visitedTables.Add(sector)) throw new InvalidDataException("A folder table is referenced twice - the image looks corrupt.");
            int tableLen = (int)Math.Min(((long)size + SectorSize - 1) / SectorSize * SectorSize, 64L * 1024 * 1024);
            var t = new byte[tableLen];
            int got = ReadSome(PartitionOffset + (long)sector * SectorSize, t, 0, tableLen);
            if (got < Math.Min(size, (uint)tableLen)) throw new EndOfStreamException("The image is truncated inside a folder table.");

            var seen = new HashSet<long>();
            var stack = new Stack<long>();
            stack.Push(0);
            bool first = true;
            while (stack.Count > 0)
            {
                long off = stack.Pop();               // DWORD offset as stored on disc
                long pos = off * 4;
                if (pos + 2 > tableLen) throw new InvalidDataException("A folder entry points outside its table.");
                ushort left = BitConverter.ToUInt16(t, (int)pos);
                if (left == 0xFFFF)
                {
                    if (off == 0 && first) return list;   // empty directory
                    pos += SectorSize - pos % SectorSize; // entries never span sectors: skip padding to the next sector
                    if (pos + 2 > tableLen) throw new InvalidDataException("A folder entry points outside its table.");
                    left = BitConverter.ToUInt16(t, (int)pos);
                    if (left == 0xFFFF) throw new InvalidDataException("A folder entry points at padding.");
                }
                first = false;
                if (pos + 14 > tableLen) throw new InvalidDataException("A folder entry is cut off.");
                if (!seen.Add(pos)) throw new InvalidDataException("A folder table contains a loop.");
                ushort right = BitConverter.ToUInt16(t, (int)pos + 2);
                uint start = BitConverter.ToUInt32(t, (int)pos + 4);
                uint length = BitConverter.ToUInt32(t, (int)pos + 8);
                byte attr = t[pos + 12];
                byte nameLen = t[pos + 13];
                if (nameLen == 0 || pos + 14 + nameLen > tableLen) throw new InvalidDataException("A folder entry has an invalid name.");
                string name = Encoding.Default.GetString(t, (int)pos + 14, nameLen);
                // Same safety rule as extract-xiso: never accept path components from the image.
                if (name == "." || name == ".." || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf('\0') >= 0)
                    throw new InvalidDataException("The image contains an unsafe file name ('" + name + "'). Extraction blocked.");
                var e = new XisoEntry
                {
                    Name = name,
                    IsDirectory = (attr & 0x10) != 0,
                    Sector = start,
                    Size = length,
                    Attributes = attr,
                    RelativePath = parent.Length == 0 ? name : parent + "\\" + name
                };
                list.Add(e);
                if (right != 0) stack.Push(right);
                if (left != 0) stack.Push(left);
            }
            foreach (var e in list)
                if (e.IsDirectory && e.Size > 0) e.Children = ReadDirectory(e.Sector, e.Size, e.RelativePath, depth + 1, visitedTables);
            return list;
        }

        public static long TotalBytes(IEnumerable<XisoEntry> entries, bool skipSystemUpdate)
        {
            long n = 0;
            foreach (var e in entries)
            {
                if (e.IsDirectory)
                {
                    if (skipSystemUpdate && e.Name.Contains(SystemUpdate)) continue;
                    n += TotalBytes(e.Children, skipSystemUpdate);
                }
                else n += e.Size;
            }
            return n;
        }

        /// <summary>Extracts the whole image (same result as extract-xiso -x, -s skips $SystemUpdate).</summary>
        public void ExtractAll(string destination, bool skipSystemUpdate, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            var root = ReadRoot();
            var state = new TransferProgress { Total = TotalBytes(root, skipSystemUpdate) };
            Directory.CreateDirectory(destination);
            ExtractEntries(root, destination, skipSystemUpdate, progress, state, ct);
        }

        public void ExtractEntries(IEnumerable<XisoEntry> entries, string destination, bool skipSystemUpdate,
            IProgress<TransferProgress> progress, TransferProgress state, CancellationToken ct)
        {
            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                string target = PathSafety.CombineUnder(destination, e.Name);
                if (e.IsDirectory)
                {
                    if (skipSystemUpdate && e.Name.Contains(SystemUpdate)) continue;
                    Directory.CreateDirectory(target);
                    ExtractEntries(e.Children, target, skipSystemUpdate, progress, state, ct);
                }
                else ExtractFile(e, target, progress, state, ct);
            }
        }

        public void ExtractFile(XisoEntry e, string target, IProgress<TransferProgress> progress, TransferProgress state, CancellationToken ct)
        {
            if (state != null) state.File = e.Name;
            var buf = new byte[1 << 20];
            long pos = PartitionOffset + (long)e.Sector * SectorSize;
            long remaining = e.Size;
            using (var o = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                while (remaining > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = ReadSome(pos, buf, 0, (int)Math.Min(buf.Length, remaining));
                    if (n <= 0)
                    {
                        Log.Warn(e.RelativePath + " is truncated in the image (" + (e.Size - remaining) + " of " + e.Size + " bytes present).");
                        break;
                    }
                    o.Write(buf, 0, n);
                    pos += n;
                    remaining -= n;
                    if (state != null) { state.Done += n; progress?.Report(state); }
                }
            }
        }

        /// <summary>The first bytes of a file in the image, in memory (enough to read a header without copying the file out).</summary>
        public byte[] ReadHead(XisoEntry e, int max)
        {
            int want = (int)Math.Min(max, e.Size);
            var head = new byte[want];
            long pos = PartitionOffset + (long)e.Sector * SectorSize;
            int got = 0;
            while (got < want)
            {
                int n = ReadSome(pos + got, head, got, want - got);
                if (n <= 0) break;
                got += n;
            }
            if (got < want) Array.Resize(ref head, got);
            return head;
        }

        public void Dispose() => _fs.Dispose();
    }

    /// <summary>
    /// Builds XISO images exactly like extract-xiso (-c from a folder, -r rewrite/optimize an image),
    /// including its AVL directory trees, sector layout, padding, ECMA-119 stub and XBE media-enable patch.
    /// </summary>
    public sealed class XisoBuilder
    {
        private const int SectorSize = XisoImage.SectorSize;
        private const int FileModulus = 0x10000;
        private const uint RootDirectorySector = 0x108;
        private static readonly byte[] OptimizedTag = Encoding.ASCII.GetBytes("in!xiso!2.7.1 (01.11.14)");
        private static readonly byte[] MediaEnable = { 0xE8, 0xCA, 0xFD, 0xFF, 0xFF, 0x85, 0xC0, 0x7D };
        private const byte MediaEnableByte = 0xEB;
        private const int MediaEnableBytePos = 7;

        private enum AvlResult { NoGrowth, Error, Grown }
        private enum Skew { None, Left, Right }

        private sealed class Node
        {
            public byte[] Name;
            public string DisplayName;
            public bool IsDirectory;
            public Node Sub;                 // first node of the child tree; null + IsDirectory = empty directory
            public uint FileSize;
            public uint StartSector;
            public uint OldStartSector;
            public uint Offset;
            public long DirStart;
            public Node Left, Right;
            public Skew Skew;
            public string SourcePath;        // local file (create mode)
        }

        private uint _current;
        private Stream _out;
        private XisoImage _from;
        private bool _mediaEnable;
        private IProgress<TransferProgress> _progress;
        private TransferProgress _state;
        private CancellationToken _ct;
        // READWRITE_BUFFER_SIZE + 1, plus slack for the final-chunk over-read described in WriteFile.
        private readonly byte[] _buffer = new byte[0x00200000 + 1 + 8];
        private const int ChunkSize = 0x00200000;

        // ------------------------------------------------------------- public API

        public static void CreateFromFolder(string folder, string outputIso, bool mediaEnable, IProgress<TransferProgress> progress, CancellationToken ct)
            => CreateFromFolder(folder, outputIso, mediaEnable, null, progress, ct);

        /// <summary>
        /// The same, with some files of the folder taken from elsewhere: <paramref name="replacements"/> maps a path inside the
        /// folder ("default.xex", any letter case) to the file on this PC whose content goes in instead. The folder is only read.
        /// </summary>
        public static void CreateFromFolder(string folder, string outputIso, bool mediaEnable, IDictionary<string, string> replacements, IProgress<TransferProgress> progress, CancellationToken ct)
            => CreateFromFolder(folder, outputIso, mediaEnable, replacements, null, progress, ct);

        /// <param name="imageTime">The image's creation time in its volume descriptor (null = now, as extract-xiso does).</param>
        public static void CreateFromFolder(string folder, string outputIso, bool mediaEnable, IDictionary<string, string> replacements, DateTime? imageTime, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            string full = System.IO.Path.GetFullPath(folder).TrimEnd('\\');
            string outFull = System.IO.Path.GetFullPath(outputIso);
            if (outFull.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save the XISO outside the folder you are packing.");
            var b = new XisoBuilder();
            if (replacements != null && replacements.Count > 0)
            {
                b._folderRoot = full;
                b._folderSwap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in replacements)
                {
                    string inside = (kv.Key ?? "").Replace('/', '\\').Trim('\\');
                    string local = System.IO.Path.GetFullPath(kv.Value);
                    if (inside.Length == 0 || !File.Exists(local)) throw new FileNotFoundException("Replacement file not found: " + local);
                    b._folderSwap[inside] = local;
                }
            }
            var root = new Node { IsDirectory = true, Name = new byte[0] };
            long total = 0;
            root.Sub = b.GenerateLocal(full, ref total, 0);
            if (b._folderSwap != null)
                foreach (string wanted in b._folderSwap.Keys)
                    if (!b._folderSwapUsed.Contains(wanted)) throw new FileNotFoundException("The folder has no file \"" + wanted + "\" to replace.");
            var ft = BitConverter.GetBytes((imageTime ?? DateTime.UtcNow).ToUniversalTime().ToFileTimeUtc());
            b.Write(root, outputIso, ft, total, mediaEnable, null, progress, ct);
        }

        private string _folderRoot;
        private Dictionary<string, string> _folderSwap;
        private readonly HashSet<string> _folderSwapUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Rewrite(string sourceIso, string outputIso, bool mediaEnable, IProgress<TransferProgress> progress, CancellationToken ct)
            => Rewrite(sourceIso, outputIso, mediaEnable, null, progress, ct);

        /// <summary>
        /// Writes a new image from <paramref name="sourceIso"/> in which some files hold other content:
        /// <paramref name="replacements"/> maps a file's path inside the image ("media\movie.bik", any letter case)
        /// to the file on this PC whose content it gets. Names, folders and every other file stay as they are; the
        /// new files may be smaller or larger. The source image is only read. Every replacement must name a file
        /// that exists in the image - nothing is added silently.
        /// </summary>
        public static void Rewrite(string sourceIso, string outputIso, bool mediaEnable, IDictionary<string, string> replacements, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            if (string.Equals(System.IO.Path.GetFullPath(sourceIso), System.IO.Path.GetFullPath(outputIso), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a different output file than the source image.");
            var swap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (replacements != null)
                foreach (var kv in replacements)
                {
                    string inside = (kv.Key ?? "").Replace('/', '\\').Trim('\\');
                    if (inside.Length == 0) throw new ArgumentException("A replacement has no path inside the image.");
                    string local = System.IO.Path.GetFullPath(kv.Value);
                    if (!File.Exists(local)) throw new FileNotFoundException("Replacement file not found: " + local);
                    if (new FileInfo(local).Length > uint.MaxValue) throw new InvalidOperationException("A file in an XISO cannot be larger than 4 GB: " + local);
                    if (string.Equals(local, System.IO.Path.GetFullPath(outputIso), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The output image cannot be one of the replacement files.");
                    if (swap.ContainsKey(inside)) throw new ArgumentException("The same file is replaced twice: " + inside);
                    swap[inside] = local;
                }
            using (var src = GameSources.OpenImage(sourceIso))
            {
                if (src.RootSector == 0 || src.RootSize == 0) throw new InvalidDataException("The image contains no files.");
                var b = new XisoBuilder();
                var root = new Node { IsDirectory = true, Name = new byte[0] };
                long total = 0;
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                root.Sub = src.RootSector != 0 && src.RootSize != 0 ? b.GenerateFromImage(src.ReadRoot(), ref total, "", swap, used) : null;
                foreach (string wanted in swap.Keys)
                    if (!used.Contains(wanted)) throw new FileNotFoundException("The image has no file \"" + wanted + "\" to replace.");
                b.Write(root, outputIso, src.FileTime, total, mediaEnable, src, progress, ct);
            }
        }

        // ------------------------------------------------------------- tree generation

        private Node GenerateLocal(string dir, ref long total, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("Folders are nested too deeply: " + dir);
            Node tree = null;
            bool empty = true;
            // Enumeration order matters (it decides the AVL shape). FindFirstFile order = extract-xiso's readdir order.
            foreach (var path in Directory.EnumerateFileSystemEntries(dir))
            {
                var attrs = File.GetAttributes(path);
                string name = System.IO.Path.GetFileName(path);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    Log.Warn("Skipped link / junction (not followed): " + path);
                    continue;
                }
                var node = new Node { DisplayName = name, Name = EncodeName(name, path) };
                if ((attrs & FileAttributes.Directory) != 0)
                {
                    empty = false;
                    node.IsDirectory = true;
                    node.Sub = GenerateLocal(path, ref total, depth + 1);
                }
                else
                {
                    empty = false;
                    string source = path;
                    // a file of the folder may be swapped for another (e.g. the game's XEX unlocked on a copy)
                    if (_folderSwap != null && path.Length > _folderRoot.Length + 1)
                    {
                        string rel = path.Substring(_folderRoot.Length + 1);
                        if (_folderSwap.TryGetValue(rel, out string swapped)) { source = swapped; _folderSwapUsed.Add(rel); }
                    }
                    long len = new FileInfo(source).Length;
                    if (len > uint.MaxValue) { Log.Warn("Skipped (larger than 4 GB, not allowed in XISO): " + path); continue; }
                    node.FileSize = (uint)len;
                    node.SourcePath = source;
                    total += len;
                }
                if (Insert(ref tree, node) == AvlResult.Error) throw new InvalidOperationException("Duplicate file name: " + path);
            }
            return empty ? null : tree;
        }

        private static byte[] EncodeName(string name, string where)
        {
            var bytes = Encoding.Default.GetBytes(name);
            if (Encoding.Default.GetString(bytes) != name)
                throw new InvalidOperationException("This name can't be stored in an XISO (unsupported characters): " + where);
            if (bytes.Length == 0 || bytes.Length > 255) throw new InvalidOperationException("File name too long for an XISO (max 255): " + where);
            foreach (byte c in bytes) if (c >= 0x80) { Log.Warn("Non-ASCII name may not work on the console: " + where); break; }
            return bytes;
        }

        private Node GenerateFromImage(List<XisoEntry> entries, ref long total, string folder, Dictionary<string, string> swap, HashSet<string> used)
        {
            Node tree = null;
            foreach (var e in entries)            // pre-order, exactly as extract-xiso inserts while traversing
            {
                var node = new Node
                {
                    Name = Encoding.Default.GetBytes(e.Name),
                    DisplayName = e.Name,
                    FileSize = e.Size,
                    OldStartSector = e.Sector,
                    IsDirectory = e.IsDirectory
                };
                string inside = folder.Length == 0 ? e.Name : folder + "\\" + e.Name;
                if (e.IsDirectory) node.Sub = e.Children.Count == 0 ? null : GenerateFromImage(e.Children, ref total, inside, swap, used);
                else
                {
                    if (swap.TryGetValue(inside, out string local))
                    {
                        // this file's content comes from the PC instead of the old image
                        node.SourcePath = local;
                        node.FileSize = (uint)new FileInfo(local).Length;
                        used.Add(inside);
                    }
                    total += node.FileSize;
                }
                if (Insert(ref tree, node) == AvlResult.Error) throw new InvalidDataException("This image appears to be corrupt (duplicate names in a folder).");
            }
            return entries.Count == 0 ? null : tree;
        }

        // ------------------------------------------------------------- AVL (port of extract-xiso)

        private static int CompareKey(byte[] a, byte[] b)
        {
            for (int i = 0; ; i++)
            {
                int x = i < a.Length ? (sbyte)a[i] : 0;    // MSVC 'char' is signed
                int y = i < b.Length ? (sbyte)b[i] : 0;
                if (x >= 'a' && x <= 'z') x -= 32;
                if (y >= 'a' && y <= 'z') y -= 32;
                if (x != 0)
                {
                    if (y != 0)
                    {
                        if (x < y) return -1;
                        if (x > y) return 1;
                    }
                    else return 1;
                }
                else return y != 0 ? -1 : 0;
            }
        }

        private static AvlResult Insert(ref Node root, Node node)
        {
            if (root == null) { root = node; return AvlResult.Grown; }
            int c = CompareKey(node.Name, root.Name);
            if (c < 0) { var t = Insert(ref root.Left, node); return t == AvlResult.Grown ? LeftGrown(ref root) : t; }
            if (c > 0) { var t = Insert(ref root.Right, node); return t == AvlResult.Grown ? RightGrown(ref root) : t; }
            return AvlResult.Error;
        }

        private static AvlResult LeftGrown(ref Node root)
        {
            switch (root.Skew)
            {
                case Skew.Left:
                    if (root.Left.Skew == Skew.Left)
                    {
                        root.Skew = root.Left.Skew = Skew.None;
                        RotateRight(ref root);
                    }
                    else
                    {
                        switch (root.Left.Right.Skew)
                        {
                            case Skew.Left: root.Skew = Skew.Right; root.Left.Skew = Skew.None; break;
                            case Skew.Right: root.Skew = Skew.None; root.Left.Skew = Skew.Left; break;
                            default: root.Skew = Skew.None; root.Left.Skew = Skew.None; break;
                        }
                        root.Left.Right.Skew = Skew.None;
                        RotateLeft(ref root.Left);
                        RotateRight(ref root);
                    }
                    return AvlResult.NoGrowth;
                case Skew.Right:
                    root.Skew = Skew.None;
                    return AvlResult.NoGrowth;
                default:
                    root.Skew = Skew.Left;
                    return AvlResult.Grown;
            }
        }

        private static AvlResult RightGrown(ref Node root)
        {
            switch (root.Skew)
            {
                case Skew.Left:
                    root.Skew = Skew.None;
                    return AvlResult.NoGrowth;
                case Skew.Right:
                    if (root.Right.Skew == Skew.Right)
                    {
                        root.Skew = root.Right.Skew = Skew.None;
                        RotateLeft(ref root);
                    }
                    else
                    {
                        switch (root.Right.Left.Skew)
                        {
                            case Skew.Left: root.Skew = Skew.None; root.Right.Skew = Skew.Right; break;
                            case Skew.Right: root.Skew = Skew.Left; root.Right.Skew = Skew.None; break;
                            default: root.Skew = Skew.None; root.Right.Skew = Skew.None; break;
                        }
                        root.Right.Left.Skew = Skew.None;
                        RotateRight(ref root.Right);
                        RotateLeft(ref root);
                    }
                    return AvlResult.NoGrowth;
                default:
                    root.Skew = Skew.Right;
                    return AvlResult.Grown;
            }
        }

        private static void RotateLeft(ref Node root)
        {
            var tmp = root;
            root = root.Right;
            tmp.Right = root.Left;
            root.Left = tmp;
        }

        private static void RotateRight(ref Node root)
        {
            var tmp = root;
            root = root.Left;
            tmp.Left = root.Right;
            root.Right = tmp;
        }

        private static void Prefix(Node n, Action<Node> f)
        {
            if (n == null) return;
            f(n);
            Prefix(n.Left, f);
            Prefix(n.Right, f);
        }

        // ------------------------------------------------------------- layout (port of extract-xiso)

        private static uint NSectors(uint size) => size / SectorSize + (size % SectorSize != 0 ? 1u : 0u);

        private static uint DirectorySize(Node sub)
        {
            uint size = 0;
            Prefix(sub, n =>
            {
                uint length = 14u + (uint)n.Name.Length;
                length += (4 - length % 4) % 4;
                if (NSectors(size + length) > NSectors(size)) size += (SectorSize - size % SectorSize) % SectorSize;
                n.Offset = size;
                size += length;
                if (n.Offset / 4 > 0xFFFF) throw new InvalidOperationException("Too many files in one folder for an XISO (folder table over 256 KB).");
            });
            return size;
        }

        private static void Requirements(Node n)
        {
            if (!n.IsDirectory) return;
            if (n.Sub != null)
            {
                n.FileSize = DirectorySize(n.Sub);
                Prefix(n.Sub, Requirements);
            }
            else n.FileSize = SectorSize;
        }

        private void Offsets(Node n)
        {
            if (!n.IsDirectory) return;
            if (n.Sub == null)
            {
                n.StartSector = _current;
                _current += 1;
                return;
            }
            n.StartSector = _current;
            long dirStart = (long)n.StartSector * SectorSize;
            _current += NSectors(n.FileSize);
            Prefix(n.Sub, c =>
            {
                c.DirStart = dirStart;
                if (!c.IsDirectory)
                {
                    c.StartSector = _current;
                    _current += NSectors(c.FileSize);
                }
            });
            Prefix(n.Sub, Offsets);
        }

        // ------------------------------------------------------------- writing (port of extract-xiso)

        private void Write(Node root, string outputIso, byte[] fileTime, long totalBytes, bool mediaEnable, XisoImage from,
            IProgress<TransferProgress> progress, CancellationToken ct)
        {
            _from = from;
            _mediaEnable = mediaEnable;
            _progress = progress;
            _ct = ct;
            _state = new TransferProgress { Total = totalBytes, File = System.IO.Path.GetFileName(outputIso) };

            root.StartSector = RootDirectorySector;
            Requirements(root);
            _current = root.StartSector;
            Offsets(root);

            string tmp = outputIso + ".partial";
            try
            {
                using (var o = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16))
                {
                    _out = o;
                    o.Write(new byte[XisoImage.HeaderOffset], 0, (int)XisoImage.HeaderOffset);
                    o.Write(XisoImage.Magic, 0, XisoImage.Magic.Length);
                    o.Write(BitConverter.GetBytes(root.StartSector), 0, 4);
                    o.Write(BitConverter.GetBytes(root.FileSize), 0, 4);
                    o.Write(fileTime, 0, 8);
                    o.Write(new byte[0x7C8], 0, 0x7C8);
                    o.Write(XisoImage.Magic, 0, XisoImage.Magic.Length);

                    o.Position = (long)root.StartSector * SectorSize;
                    WriteTree(root);

                    long pos = o.Length;
                    int pad = (int)((FileModulus - pos % FileModulus) % FileModulus);
                    o.Position = pos;
                    o.Write(new byte[pad], 0, pad);
                    WriteVolumeDescriptors(o, (uint)((pos + pad) / SectorSize));

                    o.Position = XisoImage.OptimizedTagOffset;
                    o.Write(OptimizedTag, 0, OptimizedTag.Length);
                }
                if (File.Exists(outputIso)) File.Delete(outputIso);
                File.Move(tmp, outputIso);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        private void WriteTree(Node n)
        {
            if (!n.IsDirectory) return;
            _ct.ThrowIfCancellationRequested();
            if (n.Sub != null)
            {
                Prefix(n.Sub, c => { if (!c.IsDirectory) WriteFile(c); });
                Prefix(n.Sub, WriteTree);
                _out.Position = (long)n.StartSector * SectorSize;
                Prefix(n.Sub, WriteDirectoryEntry);
                long pos = _out.Position;
                int pad = (int)((SectorSize - pos % SectorSize) % SectorSize);
                if (pad != 0) WritePad(pad);
            }
            else
            {
                _out.Position = (long)n.StartSector * SectorSize;
                WritePad(SectorSize);
            }
        }

        private void WritePad(int count)
        {
            var b = new byte[count];
            for (int i = 0; i < count; i++) b[i] = 0xFF;
            _out.Write(b, 0, count);
        }

        private Stream OpenSource(Node n)
        {
            if (_from == null || n.SourcePath != null) return new FileStream(n.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            return new ImageSlice(_from, _from.PartitionOffset + (long)n.OldStartSector * SectorSize, n.FileSize);
        }

        private void WriteFile(Node n)
        {
            _ct.ThrowIfCancellationRequested();
            _out.Position = (long)n.StartSector * SectorSize;
            _state.File = n.DisplayName;
            bool patch = _mediaEnable && n.Name.Length >= 4 && n.DisplayName.EndsWith(".xbe", StringComparison.OrdinalIgnoreCase);
            uint bytes = n.FileSize;
            using (var src = OpenSource(n))
            {
                var buf = _buffer;
                int size = ChunkSize, i = 0;
                do
                {
                    int want = (int)Math.Min(bytes, (uint)(size - i));
                    int r = want == 0 ? 0 : src.Read(buf, i, want);
                    if (r == 0)
                    {
                        if (i != 0) _out.Write(buf, 0, i);
                        break;
                    }
                    bytes -= (uint)r;
                    if (patch)
                    {
                        int n2 = r + i;
                        buf[n2] = 0;                       // extract-xiso's search sentinel
                        PatchMedia(buf, n2);
                        if (bytes != 0)
                        {
                            i = MediaEnable.Length - 1;    // carry the tail so a pattern split across chunks is found
                            _out.Write(buf, 0, n2 - i);
                            Buffer.BlockCopy(buf, n2 - i, buf, 0, i);
                        }
                        else
                        {
                            // extract-xiso writes n + i here although n already includes i; the i extra bytes
                            // spill into the next sector (later overwritten). Mirrored for byte-identical output.
                            _out.Write(buf, 0, n2 + i);
                        }
                    }
                    else _out.Write(buf, 0, r + i);
                    _state.Done += r;
                    _progress?.Report(_state);
                } while (bytes != 0);
            }
            uint written = n.FileSize - bytes;
            if (written != n.FileSize) Log.Warn(n.DisplayName + " changed size while being added (" + written + " of " + n.FileSize + " bytes).");
            n.FileSize = written;
            int pad = (int)((SectorSize - n.FileSize % SectorSize) % SectorSize);
            if (pad != 0) WritePad(pad);
        }

        /// <summary>extract-xiso's media-enable patch: in every non-overlapping E8 CA FD FF FF 85 C0 7D, byte 7 becomes EB.</summary>
        private static void PatchMedia(byte[] buf, int n)
        {
            int last = n - MediaEnable.Length;
            for (int p = 0; p <= last; p++)
            {
                if (buf[p] != MediaEnable[0]) continue;
                int k = 1;
                while (k < MediaEnable.Length && buf[p + k] == MediaEnable[k]) k++;
                if (k == MediaEnable.Length)
                {
                    buf[p + MediaEnableBytePos] = MediaEnableByte;
                    p += MediaEnable.Length - 1;
                }
            }
        }

        private void WriteDirectoryEntry(Node n)
        {
            uint fileSize = n.FileSize + (n.IsDirectory ? (uint)((SectorSize - n.FileSize % SectorSize) % SectorSize) : 0);
            byte attributes = n.IsDirectory ? (byte)0x10 : (byte)0x20;
            ushort l = (ushort)(n.Left != null ? n.Left.Offset / 4 : 0);
            ushort r = (ushort)(n.Right != null ? n.Right.Offset / 4 : 0);
            long pos = _out.Position;
            int pad = (int)(n.Offset + n.DirStart - pos);
            if (pad < 0) throw new InvalidOperationException("Internal layout error while writing a folder table.");
            if (pad != 0) WritePad(pad);
            _out.Write(BitConverter.GetBytes(l), 0, 2);
            _out.Write(BitConverter.GetBytes(r), 0, 2);
            _out.Write(BitConverter.GetBytes(n.StartSector), 0, 4);
            _out.Write(BitConverter.GetBytes(fileSize), 0, 4);
            _out.WriteByte(attributes);
            _out.WriteByte((byte)n.Name.Length);
            _out.Write(n.Name, 0, n.Name.Length);
        }

        private static void WriteVolumeDescriptors(Stream o, uint totalSectors)
        {
            const int start = 0x8000;
            void At(long p, byte[] b) { o.Position = p; o.Write(b, 0, b.Length); }
            var be = BitConverter.GetBytes(totalSectors);
            Array.Reverse(be);
            At(start, new byte[] { 0x01, (byte)'C', (byte)'D', (byte)'0', (byte)'0', (byte)'1', 0x01 });
            At(start + 80, BitConverter.GetBytes(totalSectors));
            o.Write(be, 0, 4);
            At(start + 120, new byte[] { 0x01, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x08, 0x08, 0x00 });
            var spaces = new byte[813 - 190];
            for (int i = 0; i < spaces.Length; i++) spaces[i] = 0x20;
            At(start + 190, spaces);
            var date = Encoding.ASCII.GetBytes("0000000000000000\0");
            for (int i = 0; i < 4; i++) o.Write(date, 0, date.Length);
            o.WriteByte(0x01);
            At(start + SectorSize, new byte[] { 0xFF, (byte)'C', (byte)'D', (byte)'0', (byte)'0', (byte)'1', 0x01 });
        }

        /// <summary>Read-only window onto a file inside a source image (rewrite mode).</summary>
        private sealed class ImageSlice : Stream
        {
            private readonly XisoImage _img;
            private readonly long _start, _length;
            private long _pos;
            public ImageSlice(XisoImage img, long start, long length) { _img = img; _start = start; _length = length; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                long avail = Math.Min(count, _length - _pos);
                long fileAvail = _img.Length - (_start + _pos);
                avail = Math.Min(avail, Math.Max(0, fileAvail));
                if (avail <= 0) return 0;
                _img.ReadExactly(_start + _pos, buffer, offset, (int)avail);
                _pos += avail;
                return (int)avail;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position { get => _pos; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
