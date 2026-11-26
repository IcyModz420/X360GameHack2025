using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace X360GameHack.Core.Formats
{
    /// <summary>How much of a disc image goes into a Games on Demand package (ISO2God's "Padding Removal").</summary>
    public enum GodPadding
    {
        /// <summary>Everything from the start of the game partition to the end of the image file ("None").</summary>
        None = 0,
        /// <summary>Cut after the last sector a file or folder uses ("Partial - ISO cropped"): quick, only the unused end is dropped.</summary>
        Partial = 1,
        /// <summary>The image is rebuilt with every file packed one after another ("Full - ISO rebuilt"): the smallest package.</summary>
        Full = 2
    }

    /// <summary>How a new package file is named.</summary>
    public enum GodNaming
    {
        /// <summary>As ISO2God names it, from the title ID, media ID and disc: the same disc always gets the same name, so converting it again replaces the older package.</summary>
        Iso2God = 0,
        /// <summary>As Microsoft names its own packages: from the header's hash.</summary>
        Microsoft = 1
    }

    /// <summary>How a Games on Demand package is turned back into a disc image.</summary>
    public enum GodIsoLayout
    {
        /// <summary>
        /// As god2iso does it: the data one block after another. A Microsoft-made package leaves out the empty stretch between the
        /// disc's volume header and its first file, so the folder tables get new sector numbers and the image is that much smaller;
        /// the first 64 KB (the "XSF" lead-in the package does not carry) are written fresh.
        /// </summary>
        Compact = 0,
        /// <summary>Every sector back at its place on the disc - the empty stretch comes back as zeros, no sector number changes.</summary>
        Exact = 1
    }

    /// <summary>What GOD to ISO does besides the layout.</summary>
    public sealed class GodIsoOptions
    {
        public GodIsoLayout Layout = GodIsoLayout.Compact;
        /// <summary>god2iso's "Fix CreateIsoGood broken header": an image whose lead-in says it is 0x277C00 bytes long gets a correct lead-in. Changes nothing else.</summary>
        public bool FixCreateIsoGoodHeader = true;
        /// <summary>Text put at offset 31337 of a lead-in this program writes (god2iso puts its name there, extract-xiso "in!xiso").</summary>
        public string Tag = "X360GameHack";
    }

    /// <summary>
    /// What a Games on Demand header says about a game. Read from the game's own executable on the disc - default.xex for an
    /// Xbox 360 game (execution info, plus the name and picture from the title data inside it), default.xbe for an original Xbox
    /// game (certificate, picture from its XPR texture) - and every field can be changed before converting, as in ISO2God.
    /// </summary>
    public sealed class GodTitleInfo
    {
        public bool Xbox360 = true;
        /// <summary>The game's own name ("" when it could not be read).</summary>
        public string Name = "";
        public uint TitleId, MediaId, Version, BaseVersion;
        public byte Platform, ExecutableType, DiscNumber = 1, DiscCount = 1;
        /// <summary>PNG for the dashboard tile (null = none).</summary>
        public byte[] Thumbnail;
        /// <summary>What could not be read (empty when everything was).</summary>
        public string Notes = "";

        public uint ContentType => Xbox360 ? 0x00007000u : 0x00005000u;

        public GodTitleInfo Clone()
        {
            var c = (GodTitleInfo)MemberwiseClone();
            if (Thumbnail != null) c.Thumbnail = (byte[])Thumbnail.Clone();
            return c;
        }

        /// <summary>
        /// The name the dashboard shows: the game's own name (or else the image's file name), followed by " Disc N" for a game that
        /// comes on several discs when <paramref name="addDiscNumber"/> is set - ISO2God's "Auto-rename multi-disc games".
        /// </summary>
        public string DashboardName(string imagePath, bool addDiscNumber)
        {
            string n = (Name ?? "").Trim();
            if (n.Length == 0)
            {
                string p = (imagePath ?? "").TrimEnd('\\', '/');
                n = Directory.Exists(p) ? Path.GetFileName(p) : Path.GetFileNameWithoutExtension(p) ?? "";
            }
            if (addDiscNumber && DiscCount > 1) n += " Disc " + DiscNumber;
            return n;
        }

        /// <summary>
        /// From a disc image, or from a Games on Demand package (the disc inside it; the name and picture the package's own header
        /// shows win over the game's, so a renamed package keeps its name when it is converted again).
        /// </summary>
        public static GodTitleInfo FromImage(string imagePath, CancellationToken ct)
        {
            GodTitleInfo info;
            using (var iso = GameSources.OpenImage(imagePath)) info = Read(iso, iso.ReadRoot(), ct);
            if (File.Exists(imagePath) && GodPackage.IsGod(imagePath))
            {
                byte[] h = BE.ReadHead(imagePath, 0x9800);
                string name = BE.Utf16BE(h, 0x411, 0x100).Trim('\0', ' ');
                if (name.Length > 0) info.Name = name;
                uint size = BE.U32(h, 0x1712);
                if (size > 8 && size <= GodThumbnail.MaxBytes && 0x171A + size <= h.Length)
                {
                    var png = new byte[size];
                    Buffer.BlockCopy(h, 0x171A, png, 0, (int)size);
                    info.Thumbnail = png;
                }
                info.Notes = info.Notes.Replace("The game's title data has no name.", "").Replace("The game's title data has no title picture.", "").Trim();
            }
            return info;
        }

        /// <summary>A game in its "XEX version" - an extracted folder with default.xex (Xbox 360) or default.xbe (original Xbox) in it.</summary>
        public static GodTitleInfo FromFolder(string folder)
        {
            string xex = Path.Combine(folder, "default.xex"), xbe = Path.Combine(folder, "default.xbe");
            bool is360 = File.Exists(xex);
            if (!is360 && !File.Exists(xbe)) throw new InvalidDataException("The folder has no default.xex or default.xbe - it is not an extracted game.");
            var info = new GodTitleInfo { Xbox360 = is360 };
            if (is360) ReadXex(info, xex); else ReadXbe(info, xbe);
            if (info.TitleId == 0) info.Notes += "The executable has no title ID - enter one before converting. ";
            info.Notes = info.Notes.Trim();
            return info;
        }

        /// <summary>True when the folder holds an extracted game (default.xex or default.xbe in it).</summary>
        public static bool IsGameFolder(string folder)
            => Directory.Exists(folder) && (File.Exists(Path.Combine(folder, "default.xex")) || File.Exists(Path.Combine(folder, "default.xbe")));

        internal static GodTitleInfo Read(XisoImage iso, List<XisoEntry> root, CancellationToken ct)
        {
            var xex = root.FirstOrDefault(e => !e.IsDirectory && e.Name.Equals("default.xex", StringComparison.OrdinalIgnoreCase));
            var xbe = root.FirstOrDefault(e => !e.IsDirectory && e.Name.Equals("default.xbe", StringComparison.OrdinalIgnoreCase));
            if (xex == null && xbe == null) throw new InvalidDataException("The image has no default.xex or default.xbe - it is not a game disc.");
            var info = new GodTitleInfo { Xbox360 = xex != null };
            string tmp = Path.Combine(Path.GetTempPath(), "x360gh-" + Guid.NewGuid().ToString("N") + (xex != null ? ".xex" : ".xbe"));
            try
            {
                iso.ExtractFile(xex ?? xbe, tmp, null, null, ct);
                if (xex != null) ReadXex(info, tmp); else ReadXbe(info, tmp);
            }
            finally { try { File.Delete(tmp); } catch { } }
            // homebrew often has none: the package needs one, so it is asked for (ISO2God's details window does the same)
            if (info.TitleId == 0) info.Notes += "The executable has no title ID - enter one before converting. ";
            info.Notes = info.Notes.Trim();
            return info;
        }

        private static void ReadXex(GodTitleInfo info, string path)
        {
            var x = XexFile.Read(path);
            if (!x.Valid) throw new InvalidDataException("default.xex could not be read.");
            if (x.ExecutionInfoOffset < 0) throw new InvalidDataException("default.xex carries no execution info (title ID, media ID, disc).");
            info.TitleId = x.TitleId; info.MediaId = x.MediaId;
            info.Version = x.Version; info.BaseVersion = x.BaseVersion;
            info.Platform = x.Platform; info.ExecutableType = x.ExecutableType;
            info.DiscNumber = (byte)(x.DiscNumber > 0 ? x.DiscNumber : 1);
            info.DiscCount = (byte)(x.DiscCount > 0 ? x.DiscCount : 1);
            try
            {
                // the title data's string 0x8000 in English (or the game's own language) and picture 0x8000 - the name and tile
                // the dashboard shows for the game
                var t = XexTitleData.FromXex(path);
                info.Name = (t.Name ?? "").Trim();
                if (t.Images.TryGetValue(0x8000, out byte[] png)) info.Thumbnail = GodThumbnail.Fit(png);
                if (info.Name.Length == 0) info.Notes += "The game's title data has no name. ";
                if (info.Thumbnail == null) info.Notes += "The game's title data has no title picture. ";
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                info.Notes += "The game's name and picture could not be read (" + ex.Message.TrimEnd('.') + "). ";
            }
        }

        private static void ReadXbe(GodTitleInfo info, string path)
        {
            var x = XbeFile.Read(path);
            if (!x.Valid) throw new InvalidDataException("default.xbe could not be read.");
            info.TitleId = x.TitleId;
            info.Name = (x.TitleName ?? "").Trim('\0', ' ');
            // as ISO2God: the certificate's disc number when it has one, else disc 1; one disc; no media ID, platform or type
            info.DiscNumber = (byte)(x.DiscNumber > 0 && x.DiscNumber < 256 ? x.DiscNumber : 1);
            info.DiscCount = 1;
            try
            {
                info.Thumbnail = XbeImages.TitlePng(File.ReadAllBytes(path));
                if (info.Thumbnail == null) info.Notes += "default.xbe carries no picture. ";
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                info.Notes += "The game's picture could not be read (" + ex.Message.TrimEnd('.') + "). ";
            }
            if (info.Name.Length == 0) info.Notes += "default.xbe has no title name. ";
        }
    }

    /// <summary>The picture of a package header: a PNG of at most 0x3D00 bytes (the room metadata version 2 gives it).</summary>
    public static class GodThumbnail
    {
        public const int MaxBytes = 0x3D00;

        /// <summary>The PNG as it is when it fits, else redrawn at 64x64; null when there is none or it cannot be made to fit.</summary>
        public static byte[] Fit(byte[] png)
        {
            if (png == null || png.Length == 0) return null;
            if (png.Length <= MaxBytes) return png;
            try
            {
                using (var ms = new MemoryStream(png))
                using (var img = Image.FromStream(ms))
                {
                    byte[] small = XbeImages.ToPng64(img);
                    return small.Length <= MaxBytes ? small : null;
                }
            }
            catch { return null; }
        }
    }

    /// <summary>Package names.</summary>
    public static class GodNames
    {
        /// <summary>
        /// The name ISO2God gives a package: SHA-1 over the title ID and the media ID (each as 8-digit upper-case hex text with a
        /// one-byte length in front, as .NET's BinaryWriter writes a string) followed by the disc number and the disc count (one
        /// byte each); the first 10 bytes of the hash in upper-case hex.
        /// </summary>
        public static string Iso2God(uint titleId, uint mediaId, byte discNumber, byte discCount)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, new UTF8Encoding(false), true))
                {
                    w.Write(titleId.ToString("X8"));
                    w.Write(mediaId.ToString("X8"));
                    w.Write(discNumber);
                    w.Write(discCount);
                }
                byte[] h;
                using (var sha = SHA1.Create()) h = sha.ComputeHash(ms.ToArray());
                var sb = new StringBuilder(20);
                for (int i = 0; i < 10; i++) sb.Append(h[i].ToString("X2"));
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// The first 64 KB of an Xbox 360 game partition as Microsoft's tools lay it out: the "XSF" signature, the image size and an
    /// ISO 9660 volume descriptor (an empty CD volume for a PC to see); the Xbox file system's own descriptor follows at 0x10000.
    /// ISO2God writes it at the start of an image it rebuilds; god2iso writes it in front of the data of a package that left those
    /// sectors out. It holds no game data.
    /// </summary>
    internal static class XsfHeader
    {
        public const int Size = 0x10000;
        /// <summary>Offset 31337, where extract-xiso puts its "in!xiso" tag and god2iso its own name.</summary>
        public const int TagOffset = 0x7A69;
        /// <summary>The image size "CreateIsoGood" left in its broken lead-in (god2iso's test for it).</summary>
        public const long CreateIsoGoodSize = 2587648;

        /// <param name="god2isoStyle">
        /// god2iso's lead-in: its template's byte 2 at 0x10 and the volume size in both byte orders. Otherwise ISO2God's (when it
        /// rebuilds an image): no byte at 0x10, and the volume size little-endian in BOTH places - ISO2God writes the big-endian
        /// copy with the same little-endian write. Kept as ISO2God has it so rebuilt images match its own byte for byte; only a
        /// PC reading the ISO 9660 stub ever looks at that field.
        /// </param>
        public static byte[] Build(long imageLength, bool god2isoStyle, string tag)
        {
            var h = new byte[Size];
            h[0] = (byte)'X'; h[1] = (byte)'S'; h[2] = (byte)'F'; h[3] = 0x1A;
            PutLE32(h, 4, 0x400);
            if (god2isoStyle) h[0x10] = 2;
            WriteIso9660(h, 0x8000);
            if (!string.IsNullOrEmpty(tag))
            {
                byte[] t = Encoding.ASCII.GetBytes(tag);
                Buffer.BlockCopy(t, 0, h, TagOffset, Math.Min(t.Length, 64));
            }
            SetSizes(h, imageLength, god2isoStyle);
            return h;
        }

        /// <summary>The size fields: image length - 0x400 at 8 (64-bit little-endian), sectors at 0x8050 (little-endian) and 0x8054 (big-endian, or as ISO2God writes it).</summary>
        public static void SetSizes(byte[] h, long imageLength, bool bigEndianCopy)
        {
            PutLE64(h, 8, imageLength - 0x400);
            uint sectors = (uint)(imageLength / 2048);
            PutLE32(h, 0x8050, sectors);
            if (bigEndianCopy) { h[0x8054] = (byte)(sectors >> 24); h[0x8055] = (byte)(sectors >> 16); h[0x8056] = (byte)(sectors >> 8); h[0x8057] = (byte)sectors; }
            else PutLE32(h, 0x8054, sectors);
        }

        /// <summary>ISO 9660 primary volume descriptor with only the fields both ISO2God and god2iso fill, and the set terminator after it.</summary>
        private static void WriteIso9660(byte[] b, int o)
        {
            b[o] = 1; Encoding.ASCII.GetBytes("CD001").CopyTo(b, o + 1); b[o + 6] = 1;
            b[o + 120] = 1; b[o + 123] = 1;                      // volume set size 1 (both byte orders)
            b[o + 124] = 1; b[o + 127] = 1;                      // volume sequence number 1
            b[o + 129] = 8; b[o + 130] = 8;                      // logical block size 2048
            for (int i = 190; i <= 812; i++) b[o + i] = 0x20;    // volume set / publisher / preparer / application / file identifiers: blank
            foreach (int date in new[] { 813, 830, 847, 864 })  // creation, modification, expiration, effective: "0000000000000000" + zone 0
                for (int i = 0; i < 16; i++) b[o + date + i] = (byte)'0';
            b[o + 881] = 1;                                      // file structure version
            b[o + 2048] = 0xFF; Encoding.ASCII.GetBytes("CD001").CopyTo(b, o + 2049); b[o + 2054] = 1;
        }

        internal static void PutLE32(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
        internal static void PutLE64(byte[] b, int o, long v) { for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (8 * i)); }
    }

    /// <summary>Where the 4 KB blocks of a new package come from.</summary>
    internal interface IGodSource
    {
        long Blocks { get; }
        void ReadBlock(long block, byte[] buf, int offset);
    }

    /// <summary>A disc image's game partition from its first byte; anything past the end of the image file reads as zeros.</summary>
    internal sealed class PartitionSource : IGodSource
    {
        private readonly XisoImage _iso;
        private readonly Func<long, long> _map;
        public long Blocks { get; }

        /// <param name="map">Partition block for each package block (null = the same number).</param>
        public PartitionSource(XisoImage iso, long blocks, Func<long, long> map) { _iso = iso; Blocks = blocks; _map = map; }

        public void ReadBlock(long block, byte[] buf, int offset)
        {
            long pos = _iso.PartitionOffset + (_map != null ? _map(block) : block) * GodPackage.BlockSize;
            Array.Clear(buf, offset, GodPackage.BlockSize);
            if (pos < _iso.Length) _iso.ReadAvailable(pos, buf, offset, (int)Math.Min(GodPackage.BlockSize, _iso.Length - pos));
        }
    }

    /// <summary>
    /// Folder-table attributes the way Microsoft's mastering writes them: 0x80 (normal) for a file, 0x90 (normal + folder) for
    /// a folder - extract-xiso, and so this program's byte-identical builder, writes 0x20 / 0x10. abgx360 finds default.xex only
    /// by the bytes 0x80 0x0B "default.xex" (its source), so an image gets these before abgx360 stealth patches it.
    /// </summary>
    public static class XisoAttributes
    {
        /// <summary>Sets every entry the console can reach to Microsoft's attribute; returns how many changed (0 = already so).</summary>
        public static int UseMicrosoftAttributes(string isoPath)
        {
            var changes = new List<KeyValuePair<long, byte>>();
            using (var iso = XisoImage.Open(isoPath))
            {
                void Walk(GdfTable t)
                {
                    long table = iso.PartitionOffset + (long)t.Sector * GdfTable.SectorSize;
                    foreach (var e in t.Entries.Where(e => e.Live))
                    {
                        byte want = (byte)(e.IsDirectory ? 0x90 : 0x80);
                        if (e.Attributes != want) changes.Add(new KeyValuePair<long, byte>(table + e.Offset + 12, want));
                        if (e.Sub != null) Walk(e.Sub);
                    }
                }
                Walk(GdfTable.ReadRoot(iso));
            }
            if (changes.Count == 0) return 0;
            using (var fs = new FileStream(isoPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                foreach (var c in changes) { fs.Position = c.Key; fs.WriteByte(c.Value); }
            return changes.Count;
        }
    }

    /// <summary>One entry of a folder table, as stored.</summary>
    internal sealed class GdfEntry
    {
        public int Offset;
        public ushort Left, Right;
        public uint Sector, Size;
        public byte Attributes;
        public byte[] Name;
        /// <summary>Reached from the table's tree (the console only ever sees these).</summary>
        public bool Live;
        public GdfTable Sub;
        public uint NewSector;
        /// <summary>Path inside the image ("media\movie.bik").</summary>
        public string Path;
        /// <summary>A file on this PC whose content this file gets in the rebuilt image (null = its own).</summary>
        public string Replacement;
        public bool IsDirectory => (Attributes & 0x10) != 0;
        /// <summary>Bytes it takes in a table: 14 + name, padded to 4.</summary>
        public int Length { get { int n = 14 + Name.Length; return n + (4 - n % 4) % 4; } }
    }

    /// <summary>
    /// A folder table read the way ISO2God reads it: front to back, entry after entry, 4-byte steps over 0xFF padding. Which
    /// entries the table's tree really reaches is worked out as well, so nothing else is ever followed or copied.
    /// </summary>
    internal sealed class GdfTable
    {
        public const int SectorSize = 2048;
        public uint Sector, Size, NewSector;
        public byte[] Raw = new byte[0];
        public readonly List<GdfEntry> Entries = new List<GdfEntry>();

        public static uint Sectors(uint bytes) => (uint)(((long)bytes + SectorSize - 1) / SectorSize);

        public static GdfTable ReadRoot(XisoImage iso) => Read(iso, iso.RootSector, iso.RootSize, 0, new HashSet<uint>(), "");

        private static GdfTable Read(XisoImage iso, uint sector, uint size, int depth, HashSet<uint> seen, string parent)
        {
            if (depth > 64) throw new InvalidDataException("Folders are nested too deeply - the image looks corrupt.");
            var t = new GdfTable { Sector = sector, Size = size };
            if (size == 0) return t;
            if (size > 64u * 1024 * 1024) throw new InvalidDataException("A folder table is impossibly large - the image looks corrupt.");
            if (!seen.Add(sector)) throw new InvalidDataException("A folder table is referenced twice - the image looks corrupt.");
            t.Raw = new byte[(int)Sectors(size) * SectorSize];
            long pos = iso.PartitionOffset + (long)sector * SectorSize;
            int got = pos < iso.Length ? iso.ReadAvailable(pos, t.Raw, 0, (int)Math.Min(t.Raw.Length, iso.Length - pos)) : 0;
            if (got < size) throw new EndOfStreamException("The image is cut short inside a folder table.");

            var at = new Dictionary<int, GdfEntry>();
            int p = 0;
            while (p < size)
            {
                if (p + 4 > size) break;
                ushort l = LE16(t.Raw, p), r = LE16(t.Raw, p + 2);
                if (l == 0xFFFF || r == 0xFFFF) { p += 4; continue; }
                if (p + 14 > size) break;
                int n = t.Raw[p + 13];
                if (p + 14 + n > size) break;
                var e = new GdfEntry
                {
                    Offset = p, Left = l, Right = r,
                    Sector = BE.LE32(t.Raw, p + 4), Size = BE.LE32(t.Raw, p + 8), Attributes = t.Raw[p + 12],
                    Name = new byte[n]
                };
                Buffer.BlockCopy(t.Raw, p + 14, e.Name, 0, n);
                string name = Encoding.Default.GetString(e.Name);
                e.Path = parent.Length == 0 ? name : parent + "\\" + name;
                t.Entries.Add(e);
                at[p] = e;
                p = (p + 14 + n + 3) & ~3;
            }
            MarkLive(t, at);
            foreach (var e in t.Entries)
                if (e.Live && e.IsDirectory) e.Sub = Read(iso, e.Sector, e.Size, depth + 1, seen, e.Path);
            return t;
        }

        /// <summary>Follows the tree from offset 0 (the same way the XISO reader does) and marks what it reaches.</summary>
        private static void MarkLive(GdfTable t, Dictionary<int, GdfEntry> at)
        {
            byte[] b = t.Raw;
            var stack = new Stack<int>();
            var seen = new HashSet<int>();
            stack.Push(0);
            bool first = true;
            while (stack.Count > 0)
            {
                int pos = stack.Pop() * 4;
                if (pos + 2 > b.Length) throw new InvalidDataException("A folder entry points outside its table.");
                if (LE16(b, pos) == 0xFFFF)
                {
                    if (pos == 0 && first) return;                  // empty folder
                    pos += SectorSize - pos % SectorSize;           // entries never span sectors
                    if (pos + 2 > b.Length || LE16(b, pos) == 0xFFFF) throw new InvalidDataException("A folder entry points at padding.");
                }
                first = false;
                if (!seen.Add(pos)) throw new InvalidDataException("A folder table contains a loop.");
                if (!at.TryGetValue(pos, out GdfEntry e))
                    throw new InvalidDataException("A folder table's tree reaches an entry that is not where the table's entries are stored - this image's tables are laid out in a way that cannot be rebuilt; use Partial padding removal.");
                e.Live = true;
                if (e.Right != 0) stack.Push(e.Right);
                if (e.Left != 0) stack.Push(e.Left);
            }
        }

        internal static ushort LE16(byte[] b, int o) => (ushort)(b[o] | b[o + 1] << 8);
    }

    /// <summary>
    /// ISO2God's "Full (ISO Rebuilt)" padding removal, without writing a temporary image: the rebuilt image is laid out first
    /// and its blocks are produced on demand.
    ///   0x0000    "XSF" lead-in (signature, size, ISO 9660 volume descriptor at 0x8000)
    ///   0x10000   the Xbox volume descriptor: root at sector 36, root size rounded to whole sectors, the disc's creation time
    ///   sector 36 the root table, then every other folder table (each folder's sub-folders in their stored order, depth first),
    ///             then every file: a folder's files in stored order, then those of its sub-folders - each file padded with zeros
    ///             to whole sectors.
    /// Folder tables are written back entry by entry the way ISO2God writes them; when that would move an entry from where the
    /// table's tree expects it, the table is copied as stored and only the sector numbers change.
    /// </summary>
    internal sealed class GdfRebuild : IGodSource, IDisposable
    {
        public const uint FirstSector = 36;
        private const int Block = GodPackage.BlockSize;

        private sealed class Extent
        {
            public long Start, End;     // in the rebuilt image
            public byte[] Data;         // a folder table
            public long Source = -1;    // a file: where its data starts in the source image
            public string Local;        // ... or the file on this PC that replaces it
            public long SourceLength;
        }

        private readonly Dictionary<string, FileStream> _locals = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);

        public void Dispose()
        {
            foreach (var s in _locals.Values) s.Dispose();
            _locals.Clear();
        }

        private readonly XisoImage _iso;
        private byte[] _head;
        private readonly List<Extent> _extents = new List<Extent>();
        private int _cursor;
        private long _lastBlock = -1;

        /// <summary>Bytes of the rebuilt image.</summary>
        public long Length { get; private set; }
        public long Blocks => (Length + Block - 1) / Block;
        public int Files { get; private set; }
        public int Folders { get; private set; }
        public string Notes { get; private set; } = "";

        private GdfRebuild(XisoImage iso) { _iso = iso; }

        public static GdfRebuild Plan(XisoImage iso, CancellationToken ct) => Plan(iso, ct, null);

        /// <param name="replacements">Path inside the image -> file on this PC whose content that file gets (its size may differ).</param>
        public static GdfRebuild Plan(XisoImage iso, CancellationToken ct, IDictionary<string, string> replacements)
        {
            var r = new GdfRebuild(iso);
            var root = GdfTable.ReadRoot(iso);
            if (replacements != null && replacements.Count > 0)
            {
                var want = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in replacements)
                {
                    string inside = (kv.Key ?? "").Replace('/', '\\').Trim('\\');
                    string local = System.IO.Path.GetFullPath(kv.Value);
                    if (inside.Length == 0 || !File.Exists(local)) throw new FileNotFoundException("Replacement file not found: " + local);
                    if (new FileInfo(local).Length > uint.MaxValue) throw new InvalidOperationException("A file on an Xbox disc cannot be larger than 4 GB: " + local);
                    want[inside] = local;
                }
                Replace(root, want);
                if (want.Count > 0) throw new FileNotFoundException("The image has no file \"" + want.Keys.First() + "\" to replace.");
                r.Notes += replacements.Count + " file(s) replaced (" + string.Join(", ", replacements.Keys) + "). ";
            }
            uint rootSectors = GdfTable.Sectors(iso.RootSize);
            long free = FirstSector;
            root.NewSector = (uint)free;
            free += rootSectors;
            RemapDirs(root, ref free);
            RemapFiles(root, ref free);
            if (free >= uint.MaxValue) throw new InvalidDataException("The rebuilt image would be too large.");
            ct.ThrowIfCancellationRequested();

            int keptAsStored = 0;
            if (iso.RootSize > 0) r.AddTable(root, rootSectors * (uint)GdfTable.SectorSize, ref keptAsStored);
            r.AddTables(root, ref keptAsStored);
            int pastEnd = 0;
            r.AddFiles(root, ref pastEnd);
            r._extents.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (int i = 1; i < r._extents.Count; i++)
                if (r._extents[i].Start < r._extents[i - 1].End) throw new InvalidDataException("The rebuild laid two things on the same sectors - nothing was written.");

            r.Length = Math.Max(XisoImage.HeaderOffset + 0x800, r._extents.Count == 0 ? 0 : r._extents.Max(x => x.End));
            r._head = new byte[FirstSector * GdfTable.SectorSize];
            Buffer.BlockCopy(XsfHeader.Build(r.Length, false, null), 0, r._head, 0, XsfHeader.Size);
            int vd = (int)XisoImage.HeaderOffset;
            XisoImage.Magic.CopyTo(r._head, vd);
            XsfHeader.PutLE32(r._head, vd + 20, root.NewSector);
            XsfHeader.PutLE32(r._head, vd + 24, rootSectors * (uint)GdfTable.SectorSize);
            Buffer.BlockCopy(iso.FileTime, 0, r._head, vd + 28, 8);
            r._head[vd + 36] = 1;
            XisoImage.Magic.CopyTo(r._head, vd + 0x7EC);

            if (keptAsStored > 0) r.Notes += keptAsStored + " folder table(s) were kept exactly as stored (only their sector numbers changed), because writing them entry by entry would have moved an entry. ";
            if (pastEnd > 0) r.Notes += pastEnd + " file(s) reach past the end of the source image; the missing part is zeros. ";
            return r;
        }

        private static void Replace(GdfTable t, Dictionary<string, string> want)
        {
            foreach (var e in t.Entries)
            {
                if (!e.Live) continue;
                if (e.IsDirectory) { if (e.Sub != null) Replace(e.Sub, want); continue; }
                if (!want.TryGetValue(e.Path, out string local)) continue;
                e.Replacement = local;
                e.Size = (uint)new FileInfo(local).Length;
                want.Remove(e.Path);
            }
        }

        private static void RemapDirs(GdfTable t, ref long free)
        {
            foreach (var e in t.Entries)
                if (e.Live && e.IsDirectory)
                {
                    e.NewSector = (uint)free;
                    e.Sub.NewSector = (uint)free;
                    free += GdfTable.Sectors(e.Size);
                    RemapDirs(e.Sub, ref free);
                }
        }

        private static void RemapFiles(GdfTable t, ref long free)
        {
            foreach (var e in t.Entries)
                if (e.Live && !e.IsDirectory)
                {
                    e.NewSector = (uint)free;
                    free += GdfTable.Sectors(e.Size);
                }
            foreach (var e in t.Entries)
                if (e.Live && e.IsDirectory) RemapFiles(e.Sub, ref free);
        }

        private void AddTables(GdfTable t, ref int keptAsStored)
        {
            foreach (var e in t.Entries)
                if (e.Live && e.IsDirectory)
                {
                    Folders++;
                    if (e.Size > 0) AddTable(e.Sub, e.Size, ref keptAsStored);
                    AddTables(e.Sub, ref keptAsStored);
                }
        }

        private void AddTable(GdfTable t, uint size, ref int keptAsStored)
        {
            byte[] bytes = Serialize(t, size, out bool asStored);
            if (asStored) keptAsStored++;
            if (bytes.Length == 0) return;
            long start = (long)t.NewSector * GdfTable.SectorSize;
            _extents.Add(new Extent { Start = start, End = start + bytes.Length, Data = bytes });
        }

        private void AddFiles(GdfTable t, ref int pastEnd)
        {
            foreach (var e in t.Entries)
                if (e.Live && !e.IsDirectory)
                {
                    Files++;
                    if (e.Size == 0) continue;
                    long start = (long)e.NewSector * GdfTable.SectorSize;
                    long end = start + (long)GdfTable.Sectors(e.Size) * GdfTable.SectorSize;
                    if (e.Replacement != null)
                    {
                        _extents.Add(new Extent { Start = start, End = end, Local = e.Replacement, SourceLength = e.Size });
                        continue;
                    }
                    long src = _iso.PartitionOffset + (long)e.Sector * GdfTable.SectorSize;
                    if (src + e.Size > _iso.Length) pastEnd++;
                    _extents.Add(new Extent { Start = start, End = end, Source = src, SourceLength = e.Size });
                }
            foreach (var e in t.Entries)
                if (e.Live && e.IsDirectory) AddFiles(e.Sub, ref pastEnd);
        }

        /// <summary>
        /// The table as ISO2God writes it: whole sectors of 0xFF, entries in stored order, an entry that does not fit in what is
        /// left of a sector starts the next one. Falls back to the stored bytes (new sector numbers only) when an entry would
        /// not land where it was - the tree's links are offsets, so moving an entry would break the folder.
        /// </summary>
        private static byte[] Serialize(GdfTable t, uint size, out bool asStored)
        {
            int len = (int)GdfTable.Sectors(size) * GdfTable.SectorSize;
            var b = new byte[len];
            for (int i = 0; i < len; i++) b[i] = 0xFF;
            asStored = false;
            int p = 0;
            foreach (var e in t.Entries)
            {
                int el = e.Length, room = GdfTable.SectorSize - p % GdfTable.SectorSize;
                if (el > room) p += room;
                if (p + el > len || p != e.Offset) { asStored = true; break; }
                PutEntry(b, p, e);
                p += el;
            }
            if (!asStored) return b;

            b = new byte[len];
            Buffer.BlockCopy(t.Raw, 0, b, 0, Math.Min(t.Raw.Length, len));
            foreach (var e in t.Entries)
                if (e.Live)
                {
                    XsfHeader.PutLE32(b, e.Offset + 4, e.NewSector);
                    XsfHeader.PutLE32(b, e.Offset + 8, e.Size);     // a replaced file's new size
                }
            return b;
        }

        private static void PutEntry(byte[] b, int p, GdfEntry e)
        {
            b[p] = (byte)e.Left; b[p + 1] = (byte)(e.Left >> 8);
            b[p + 2] = (byte)e.Right; b[p + 3] = (byte)(e.Right >> 8);
            XsfHeader.PutLE32(b, p + 4, e.Live ? e.NewSector : e.Sector);
            XsfHeader.PutLE32(b, p + 8, e.Size);
            b[p + 12] = e.Attributes;
            b[p + 13] = (byte)e.Name.Length;
            Buffer.BlockCopy(e.Name, 0, b, p + 14, e.Name.Length);
        }

        public void ReadBlock(long block, byte[] buf, int offset)
        {
            long start = block * Block, end = start + Block;
            Array.Clear(buf, offset, Block);
            if (block < _lastBlock) _cursor = 0;
            _lastBlock = block;
            if (start < _head.Length) Buffer.BlockCopy(_head, (int)start, buf, offset, (int)Math.Min(Block, _head.Length - start));
            while (_cursor < _extents.Count && _extents[_cursor].End <= start) _cursor++;
            for (int i = _cursor; i < _extents.Count && _extents[i].Start < end; i++)
            {
                var x = _extents[i];
                long a = Math.Max(start, x.Start), b = Math.Min(end, x.End);
                if (b <= a) continue;
                if (x.Data != null)
                {
                    Buffer.BlockCopy(x.Data, (int)(a - x.Start), buf, offset + (int)(a - start), (int)(b - a));
                    continue;
                }
                // a file: its bytes, then zeros to the end of its last sector
                long dataEnd = Math.Min(b, x.Start + x.SourceLength);
                if (dataEnd <= a) continue;
                if (x.Local != null)
                {
                    if (!_locals.TryGetValue(x.Local, out FileStream fs))
                        _locals[x.Local] = fs = new FileStream(x.Local, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                    fs.Position = a - x.Start;
                    int want = (int)(dataEnd - a), got = 0;
                    while (got < want)
                    {
                        int n = fs.Read(buf, offset + (int)(a - start) + got, want - got);
                        if (n <= 0) throw new IOException(x.Local + " changed while it was being packed.");
                        got += n;
                    }
                    continue;
                }
                long from = x.Source + (a - x.Start);
                if (from < _iso.Length)
                    _iso.ReadAvailable(from, buf, offset + (int)(a - start), (int)Math.Min(dataEnd - a, _iso.Length - from));
            }
        }
    }

    /// <summary>
    /// The pictures an original Xbox game carries in its XBE: the sections "$$XSIMAGE" (64x64, the saved-game picture) and
    /// "$$XTIMAGE" (128x128, the title picture), each an XPR texture.
    /// </summary>
    public static class XbeImages
    {
        public sealed class Section
        {
            public string Name;
            public uint Flags, VirtualAddress, VirtualSize, RawAddress, RawSize;
        }

        public static List<Section> Sections(byte[] xbe)
        {
            if (xbe == null || xbe.Length < 0x178 || xbe[0] != 'X' || xbe[1] != 'B' || xbe[2] != 'E' || xbe[3] != 'H') throw new InvalidDataException("Not an XBE.");
            uint baseAddress = BE.LE32(xbe, 0x104), count = BE.LE32(xbe, 0x11C), headers = BE.LE32(xbe, 0x120);
            if (count > 512 || headers < baseAddress) throw new InvalidDataException("The XBE's section table is damaged.");
            var list = new List<Section>();
            for (uint i = 0; i < count; i++)
            {
                long h = (long)headers - baseAddress + i * 0x38;
                if (h < 0 || h + 0x38 > xbe.Length) throw new InvalidDataException("The XBE's section table is cut short.");
                var s = new Section
                {
                    Flags = BE.LE32(xbe, (int)h), VirtualAddress = BE.LE32(xbe, (int)h + 4), VirtualSize = BE.LE32(xbe, (int)h + 8),
                    RawAddress = BE.LE32(xbe, (int)h + 12), RawSize = BE.LE32(xbe, (int)h + 16)
                };
                long name = (long)BE.LE32(xbe, (int)h + 20) - baseAddress;
                var sb = new StringBuilder();
                for (long k = name; k >= 0 && k < xbe.Length && sb.Length < 64 && xbe[k] != 0; k++) sb.Append((char)xbe[k]);
                s.Name = sb.ToString();
                list.Add(s);
            }
            return list;
        }

        /// <summary>The game's picture as a 64x64 PNG for a package header (as ISO2God picks it: the saved-game picture, else the title picture made smaller), or null.</summary>
        public static byte[] TitlePng(byte[] xbe)
        {
            var sections = Sections(xbe);
            Exception problem = null;
            foreach (string want in new[] { "$$XSIMAGE", "$$XTIMAGE" })
            {
                var s = sections.FirstOrDefault(x => x.Name == want);
                if (s == null || s.RawSize == 0 || (long)s.RawAddress + s.RawSize > xbe.Length) continue;
                var data = new byte[s.RawSize];
                Buffer.BlockCopy(xbe, (int)s.RawAddress, data, 0, data.Length);
                try
                {
                    using (var bmp = DecodeXpr(data))
                    {
                        byte[] png = ToPng64(bmp);
                        if (png.Length <= GodThumbnail.MaxBytes) return png;
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is NotSupportedException) { problem = ex; }
            }
            if (problem != null) throw problem;
            return null;
        }

        /// <summary>Any picture redrawn at 64x64 as a PNG.</summary>
        public static byte[] ToPng64(Image img)
        {
            using (var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var edges = new ImageAttributes())
                {
                    edges.SetWrapMode(WrapMode.TileFlipXY);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(img, new Rectangle(0, 0, 64, 64), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, edges);
                }
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// An XPR0 texture: "XPR0", total size, header size, then the texture: common, data, lock and the format word (bits 8-15
        /// the format, 20-23 / 24-27 the width / height as powers of two). DXT1 / DXT3 / DXT5 blocks, and 32-bit pixels either
        /// swizzled (formats 0x06 / 0x07) or linear (0x12 / 0x1E).
        /// </summary>
        public static Bitmap DecodeXpr(byte[] x)
        {
            if (x.Length < 0x1C || BE.LE32(x, 0) != 0x30525058) throw new InvalidDataException("The picture is not an XPR texture.");
            uint total = BE.LE32(x, 4), header = BE.LE32(x, 8), word = BE.LE32(x, 0x18);
            int format = (int)(word >> 8) & 0xFF;
            int w = 1 << (int)((word >> 20) & 0xF), h = 1 << (int)((word >> 24) & 0xF);
            if (w > 2048 || h > 2048 || header >= x.Length) throw new InvalidDataException("The picture's texture header is damaged.");
            int end = (int)Math.Min(x.Length, Math.Max(total, header));
            int at = (int)header, avail = end - at;
            var px = new int[w * h];
            switch (format)
            {
                case 0x0C: Dxt(x, at, avail, w, h, px, 1); break;
                case 0x0E: Dxt(x, at, avail, w, h, px, 3); break;
                case 0x0F: Dxt(x, at, avail, w, h, px, 5); break;
                case 0x06: case 0x07: Argb(x, at, avail, w, h, px, true, format == 0x07); break;
                case 0x12: case 0x1E: Argb(x, at, avail, w, h, px, false, format == 0x1E); break;
                default: throw new NotSupportedException("Picture format 0x" + format.ToString("X2") + " is not supported.");
            }
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var lk = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(px, y * w, lk.Scan0 + y * lk.Stride, w);
            }
            finally { bmp.UnlockBits(lk); }
            return bmp;
        }

        private static void Argb(byte[] x, int at, int avail, int w, int h, int[] px, bool swizzled, bool opaque)
        {
            if (avail < w * h * 4) throw new InvalidDataException("The picture's pixel data is cut short.");
            for (int y = 0; y < h; y++)
                for (int c = 0; c < w; c++)
                {
                    int i = swizzled ? Swizzle(c, y, w, h) : y * w + c;
                    int v = (int)BE.LE32(x, at + i * 4);
                    px[y * w + c] = opaque ? v | unchecked((int)0xFF000000) : v;
                }
        }

        /// <summary>Xbox texture swizzle: x and y bits interleaved from the lowest up, x first, while each still has bits.</summary>
        internal static int Swizzle(int x, int y, int w, int h)
        {
            int offset = 0, shift = 0;
            for (int bit = 1; bit < w || bit < h; bit <<= 1)
            {
                if (bit < w) { if ((x & bit) != 0) offset |= 1 << shift; shift++; }
                if (bit < h) { if ((y & bit) != 0) offset |= 1 << shift; shift++; }
            }
            return offset;
        }

        private static void Dxt(byte[] x, int at, int avail, int w, int h, int[] px, int kind)
        {
            int blockBytes = kind == 1 ? 8 : 16;
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            if (avail < bw * bh * blockBytes) throw new InvalidDataException("The picture's compressed data is cut short.");
            var colors = new int[4];
            var alpha = new int[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int p = at + (by * bw + bx) * blockBytes;
                    for (int i = 0; i < 16; i++) alpha[i] = 255;
                    if (kind == 3)
                    {
                        for (int i = 0; i < 16; i++) { int nib = (x[p + i / 2] >> ((i & 1) * 4)) & 0xF; alpha[i] = nib * 17; }
                        p += 8;
                    }
                    else if (kind == 5)
                    {
                        int a0 = x[p], a1 = x[p + 1];
                        ulong bits = 0;
                        for (int i = 0; i < 6; i++) bits |= (ulong)x[p + 2 + i] << (8 * i);
                        var table = new int[8];
                        table[0] = a0; table[1] = a1;
                        if (a0 > a1) for (int i = 1; i < 7; i++) table[i + 1] = ((7 - i) * a0 + i * a1) / 7;
                        else { for (int i = 1; i < 5; i++) table[i + 1] = ((5 - i) * a0 + i * a1) / 5; table[6] = 0; table[7] = 255; }
                        for (int i = 0; i < 16; i++) alpha[i] = table[(int)(bits >> (3 * i)) & 7];
                        p += 8;
                    }
                    int c0 = x[p] | x[p + 1] << 8, c1 = x[p + 2] | x[p + 3] << 8;
                    uint idx = BE.LE32(x, p + 4);
                    Rgb565(c0, out int r0, out int g0, out int b0);
                    Rgb565(c1, out int r1, out int g1, out int b1);
                    colors[0] = Pack(255, r0, g0, b0);
                    colors[1] = Pack(255, r1, g1, b1);
                    if (kind != 1 || c0 > c1)
                    {
                        colors[2] = Pack(255, (2 * r0 + r1) / 3, (2 * g0 + g1) / 3, (2 * b0 + b1) / 3);
                        colors[3] = Pack(255, (r0 + 2 * r1) / 3, (g0 + 2 * g1) / 3, (b0 + 2 * b1) / 3);
                    }
                    else
                    {
                        colors[2] = Pack(255, (r0 + r1) / 2, (g0 + g1) / 2, (b0 + b1) / 2);
                        colors[3] = 0;   // transparent black
                    }
                    for (int i = 0; i < 16; i++)
                    {
                        int xx = bx * 4 + (i & 3), yy = by * 4 + (i >> 2);
                        if (xx >= w || yy >= h) continue;
                        int c = colors[(idx >> (2 * i)) & 3];
                        if (kind != 1) c = (c & 0x00FFFFFF) | alpha[i] << 24;
                        px[yy * w + xx] = c;
                    }
                }
        }

        private static void Rgb565(int c, out int r, out int g, out int b)
        {
            r = (c >> 11) & 0x1F; g = (c >> 5) & 0x3F; b = c & 0x1F;
            r = r << 3 | r >> 2; g = g << 2 | g >> 4; b = b << 3 | b >> 2;
        }

        private static int Pack(int a, int r, int g, int b) => a << 24 | r << 16 | g << 8 | b;
    }
}
