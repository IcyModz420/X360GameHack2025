using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace X360GameHack.Core.Formats
{
    /// <summary>How a flash dump stores its pages (from the spare bytes the NAND controller keeps per 512-byte page).</summary>
    public enum NandKind
    {
        /// <summary>16 MB small-block NAND, spare layout 0 (Xenon, Zephyr, Falcon, Opus): block ID in spare bytes 0-1.</summary>
        SmallBlock0,
        /// <summary>16 MB small-block NAND, spare layout 1 (Jasper 16 MB, Trinity, Corona 16 MB): block ID in spare bytes 1-2.</summary>
        SmallBlock1,
        /// <summary>Big-block NAND (Jasper 256 / 512 MB and the other big-block boards): 128 KB blocks, bad-block marker in spare byte 0, block ID in bytes 1-2.</summary>
        BigBlock,
        /// <summary>No spare bytes: Corona 4 GB (eMMC) - or a dump that had them removed.</summary>
        NoSpare
    }

    /// <summary>One bootloader stage or patch-slot stage as its plain header describes it.</summary>
    public sealed class NandBootloader
    {
        /// <summary>CB_A, CB_B, CD, CE (or SB / SC / SD / SE on devkit images), CF / CG in a patch slot.</summary>
        public string Name;
        public string Magic;
        public int Offset;
        public ushort Build, Qfe, Flags;
        public uint Entry, Size;
        /// <summary>The patch slot (0 / 1) for CF and CG, -1 for the boot chain.</summary>
        public int Slot = -1;
    }

    /// <summary>A file in the flash file system (xam.xex, dash.xex, crl.bin ...).</summary>
    public sealed class NandFile
    {
        public string Name;
        /// <summary>First 16 KB allocation unit (the file system's own numbering).</summary>
        public int Start;
        public uint Size;
        public uint Timestamp;
        /// <summary>The units it occupies, in order (from the allocation table).</summary>
        public List<int> Chain = new List<int>();
        /// <summary>Its units follow one another (J-Runner reads files this way; a fragmented file it reads wrongly).</summary>
        public bool Contiguous => Chain.Select((u, i) => u - i).Distinct().Count() <= 1;
    }

    /// <summary>What the keyvault says, once it is decrypted with the console's CPU key.</summary>
    public sealed class NandKeyVault
    {
        /// <summary>The decrypted keyvault (0x4000 bytes, the 16-byte nonce first). Kept in memory only.</summary>
        public byte[] Decrypted;
        public bool CpuKeyMatches;
        public string Serial, ConsoleId, DvdKey, Osig, ManufacturingDate, Region, MotherboardSerial;
        /// <summary>The console ID as the console's own settings show it.</summary>
        public string ConsoleIdFriendly => FriendlyConsoleId(ConsoleId);
        public ushort OddFeatures;
        /// <summary>The console needs its FCRT (DVD drive certificate, fcrt.bin in the file system) - J-Runner's "fcrt" flag.</summary>
        public bool NeedsFcrt;
        /// <summary>1 = an older keyvault without the type-2 fields, 2 = type 2 (as J-Runner calls them).</summary>
        public int Type;

        /// <summary>The region code's name, as J-Runner's flash info panel and SMC config editor name them.</summary>
        public static string RegionName(string code)
        {
            switch ((code ?? "").ToUpperInvariant())
            {
                case "00FF": return "NTSC/U (North America)";
                case "01FE": case "01FF": return "NTSC/J (Japan)";
                case "01FC": return "NTSC/J (Korea)";
                case "0101": return "NTSC/J (Hong Kong / Asia)";
                case "02FE": return "PAL (Europe)";
                case "0201": return "PAL (Australia / New Zealand)";
                case "7FFF": return "Region free (devkit)";
                default: return "unknown";
            }
        }

        /// <summary>
        /// The console ID as the console's own settings show it: the first 9 hex digits as a decimal number, then the last
        /// digit, 12 digits in all (J-Runner's consoleID_KV_to_friendly).
        /// </summary>
        public static string FriendlyConsoleId(string kvConsoleId)
        {
            if (kvConsoleId == null || kvConsoleId.Length < 10 || !kvConsoleId.All(Uri.IsHexDigit)) return null;
            return (Convert.ToUInt64(kvConsoleId.Substring(0, 9), 16).ToString() + kvConsoleId.Substring(9)).PadLeft(12, '0');
        }
    }

    /// <summary>The board families, numbered as the SMC numbers them (the high 4 bits of the decrypted SMC's byte 0x100).</summary>
    public enum NandBoard { Unknown = 0, Xenon = 1, Zephyr = 2, Falcon = 3, Jasper = 4, Trinity = 5, Corona = 6, Winchester = 7 }

    /// <summary>
    /// The SMC config: fan speeds, temperature limits, regions and the like, which the SMC reads at start-up. Read at the
    /// places J-Runner reads it (its own extracts are byte for byte these bytes); the layout and checksum are J-Runner's.
    /// </summary>
    public sealed class NandSmcConfig
    {
        /// <summary>What J-Runner saves as smc_config.bin: 64 KB (512 KB on big-block flash), the config at <see cref="ConfigOffset"/>.</summary>
        public byte[] Area;
        public int ConfigOffset;
        /// <summary>Where the area starts in the flash (data address, without spare bytes).</summary>
        public long Address;

        byte B(int o) => Area[ConfigOffset + o];
        ushort U16(int o) => (ushort)(B(o) << 8 | B(o + 1));
        uint U32(int o) => (uint)(B(o) << 24 | B(o + 1) << 16 | B(o + 2) << 8 | B(o + 3));
        public string Hex(int o, int n) => BitConverter.ToString(Area, ConfigOffset + o, n).Replace("-", "");

        /// <summary>Stored little-endian in the first two bytes: the inverted 16-bit sum of bytes 0x10-0x10B (J-Runner's calcConfigSum).</summary>
        public ushort StoredChecksum => (ushort)(B(0) | B(1) << 8);
        public ushort ComputedChecksum
        {
            get { int s = 0; for (int i = 0x10; i < 0x10C; i++) s += B(i); return (ushort)(~s & 0xFFFF); }
        }
        public bool ChecksumOk => StoredChecksum == ComputedChecksum;
        /// <summary>Nothing there: the area is erased (all 0xFF) or empty.</summary>
        public bool Blank { get { for (int i = 0; i < 0x244; i++) if (B(i) != 0xFF && B(i) != 0) return false; return true; } }

        public int Structure => B(0x0E);
        public int Config => B(0x0F);
        public int Clock => B(0x10);
        public int CpuFan => B(0x11);
        public int GpuFan => B(0x12);
        public int Bit => B(0x14);
        public int Ana => B(0x28);
        public int AnaBackup => B(0x102);
        /// <summary>CPU, GPU, eDRAM target temperatures, then CPU, GPU, eDRAM critical temperatures, in °C.</summary>
        public int[] Temperatures => Enumerable.Range(0x29, 6).Select(o => (int)B(o)).ToArray();
        public string Region => B(0x22C).ToString("X2") + B(0x22D).ToString("X2");
        public int DvdRegion => B(0x237);
        public string Mac => BitConverter.ToString(Area, ConfigOffset + 0x220, 6);
        public string ResetCode => Encoding.ASCII.GetString(Area, ConfigOffset + 0x238, 4);
        public uint Version => U32(0x204);
        public uint Net => U32(0x208);
        public uint Flags => U32(0x23C);
        public ushort PowerMode => U16(0x240);
        public ushort PowerVcs => U16(0x242);

        /// <summary>0x7F is the SMC's automatic fan curve; 0x80 and up is a fixed speed of (value - 0x80) % (J-Runner's reading).</summary>
        public static string FanText(int v) => v == 0x7F ? "automatic" : v >= 0x80 ? "fixed at " + (v - 0x80) + " %" : "0x" + v.ToString("X2");
        public static string DvdRegionText(int v) => v == 0xFF ? "region free" : v >= 1 && v <= 8 ? "region " + v : "0x" + v.ToString("X2");

        /// <summary>The reset code as buttons (J-Runner shows each letter as a pad button: U / D / L / R for the d-pad).</summary>
        public string ResetCodeButtons
        {
            get
            {
                var code = ResetCode;
                if (code.Any(c => c < 0x20 || c > 0x7E)) return null;
                return string.Join(" ", code.Select(c => c == 'U' ? "Up" : c == 'D' ? "Down" : c == 'L' ? "Left" : c == 'R' ? "Right" : c.ToString()));
            }
        }
    }

    /// <summary>
    /// An Xbox 360 flash (NAND) dump, read only: every board - small-block 16 MB (Xenon, Zephyr, Falcon, Opus, Jasper,
    /// Trinity, Corona), big-block Jasper (256 / 512 MB, the first 64 MB as J-Runner dumps them) and Corona 4 GB (eMMC, no spare
    /// bytes). Pages are 512 data bytes plus 16 spare bytes when the dump has them; every page's 26-bit EDC is checked, bad
    /// blocks are found from their markers and their remapped copies used. What it reads - the header, the boot chain and both
    /// patch slots, the SMC (decrypted with its published 4-byte key), the keyvault (decrypted and verified with the console's
    /// own CPU key, never kept on disk), the flash file system (xam.xex, dash.xex, crl.bin ...) and the SMC config - is checked
    /// against J-Runner's own reader on real dumps of every kind (NandTests). The boot stages and patch slots are decrypted with
    /// the published 1BL key (<see cref="DecryptBootChain"/>), and their lockdown values and pairing data read from them.
    /// </summary>
    public sealed class NandImage
    {
        public const int PageData = 0x200, PageRaw = 0x210, SmallBlockData = 0x4000, BigBlockData = 0x20000;
        /// <summary>What is read of a dump: the system area of every kind (a big-block dump's first 64 MB, a Corona's first 48 MB).</summary>
        public const long MaxRead = 0x4200000;

        public string Path { get; private set; }
        public long FileLength { get; private set; }
        public NandKind Kind { get; private set; }
        public bool HasSpare => Kind != NandKind.NoSpare;
        public bool IsBigBlock => Kind == NandKind.BigBlock;
        /// <summary>Data bytes per erase block (16 KB small block, 128 KB big block; the eMMC is read in 16 KB units).</summary>
        public int BlockData => IsBigBlock ? BigBlockData : SmallBlockData;
        int PagesPerBlock => BlockData / PageData;
        int BlockRaw => HasSpare ? PagesPerBlock * PageRaw : BlockData;

        private byte[] _raw;
        /// <summary>The dump without spare bytes, bad blocks replaced by their remapped copies (the console's view of the flash).</summary>
        public byte[] Data { get; private set; }
        public int Blocks { get; private set; }
        public readonly List<int> BadBlocks = new List<int>();
        /// <summary>Bad block -> the block that holds its data instead.</summary>
        public readonly Dictionary<int, int> Remapped = new Dictionary<int, int>();
        /// <summary>Pages whose stored EDC does not match their content (erased pages are skipped).</summary>
        public readonly List<int> EccErrors = new List<int>();
        public int PagesChecked { get; private set; }

        // ---- header (big-endian, at the start of the flash)
        public ushort Magic, Build, Qfe, HeaderFlags, PatchSlotCount, KvVersion;
        public uint CbOffset, SystemLength, KvLength, PatchSlotAddress, KvAddress, PatchSlotSize, SmcConfigAddress, SmcLength, SmcAddress;
        public string Copyright = "";
        public bool Devkit => Magic == 0x0F4F;

        public readonly List<NandBootloader> Bootloaders = new List<NandBootloader>();

        /// <summary>The SMC as stored (encrypted) and decrypted.</summary>
        public byte[] SmcEncrypted, Smc;
        /// <summary>The keyvault as stored (encrypted).</summary>
        public byte[] KeyVaultEncrypted;

        // ---- flash file system
        /// <summary>The physical block holding the file system root that is in use (-1 = none found).</summary>
        public int FsRootBlock { get; private set; } = -1;
        public uint FsRootSequence { get; private set; }
        public readonly List<NandFile> Files = new List<NandFile>();
        private ushort[] _fat;
        private Dictionary<int, int> _fsMap;

        public static NandImage Open(string path)
        {
            var n = new NandImage { Path = path };
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                n.FileLength = fs.Length;
                if (fs.Length < 0x10000) throw new InvalidDataException("Too small for an Xbox 360 flash dump.");
                n._raw = new byte[(int)Math.Min(fs.Length, MaxRead)];
                int got = 0;
                while (got < n._raw.Length) { int r = fs.Read(n._raw, got, n._raw.Length - got); if (r <= 0) break; got += r; }
            }
            n.Kind = DetectKind(n._raw);
            if (!n.HasSpare && n._raw.Length > 0x3000000 && n.FileLength > MaxRead)
                Array.Resize(ref n._raw, 0x3000000);                  // eMMC: the system area is the first 48 MB
            n.Build_();
            return n;
        }

        // ================================================================ pages, spare bytes, EDC

        /// <summary>
        /// The 26-bit EDC the NAND controller keeps in a page's last spare bytes: an LFSR (0x6954559) over the page's 512 data
        /// bytes and the first 102 bits of its spare bytes, each byte read low bit first and inverted (libxenon's sfcx_calcecc).
        /// </summary>
        public static uint Edc(byte[] buf, int page)
        {
            uint val = 0, v = 0;
            for (int i = 0; i < 0x1066; i++)
            {
                if ((i & 31) == 0)
                {
                    int w = page + (i >> 3);
                    v = ~(uint)(buf[w] | buf[w + 1] << 8 | buf[w + 2] << 16 | buf[w + 3] << 24);
                }
                val ^= v & 1;
                v >>= 1;
                if ((val & 1) != 0) val ^= 0x6954559;
                val >>= 1;
            }
            return ~val & 0x3FFFFFF;
        }

        public static uint StoredEdc(byte[] buf, int page)
        {
            int s = page + PageData;
            return (uint)(((buf[s + 12] >> 6) & 3) | buf[s + 13] << 2 | buf[s + 14] << 10 | buf[s + 15] << 18);
        }

        static bool Erased(byte[] buf, int page)
        {
            for (int i = 0; i < PageRaw; i++) if (buf[page + i] != 0xFF) return false;
            return true;
        }

        static NandKind DetectKind(byte[] raw)
        {
            bool spare = raw.Length >= 0x4200 && raw.Length % PageRaw == 0 && !Erased(raw, 0) && Edc(raw, 0) == StoredEdc(raw, 0);
            if (!spare) return NandKind.NoSpare;
            // block 1's first page: where its block ID (1) sits tells the layout
            int bb = 0x21000 + PageData, sb = 0x4200 + PageData;
            if (raw.Length > bb + 16 && raw[PageData] == 0xFF && raw[bb] == 0xFF && raw[bb + 1] == 1 && (raw[bb + 2] & 0x0F) == 0) return NandKind.BigBlock;
            if (raw[sb] == 1 && (raw[sb + 1] & 0x0F) == 0 && raw[sb + 5] == 0xFF) return NandKind.SmallBlock0;
            if (raw[sb + 1] == 1 && (raw[sb + 2] & 0x0F) == 0 && raw[sb + 5] == 0xFF) return NandKind.SmallBlock1;
            // a bad block 1: decide from block 2
            sb = 2 * 0x4200 + PageData;
            if (raw[sb] == 2) return NandKind.SmallBlock0;
            if (raw[sb + 1] == 2) return NandKind.SmallBlock1;
            return raw[PageData] == 0xFF ? NandKind.BigBlock : NandKind.SmallBlock0;
        }

        /// <summary>The spare bytes of a block's first page.</summary>
        int SpareOf(int block, int page = 0) => block * BlockRaw + page * PageRaw + PageData;

        public int BlockId(int block)
        {
            int s = SpareOf(block);
            switch (Kind)
            {
                case NandKind.SmallBlock0: return ((_raw[s + 1] & 0x0F) << 8) | _raw[s];
                default: return ((_raw[s + 2] & 0x0F) << 8) | _raw[s + 1];
            }
        }

        public bool IsBad(int block)
        {
            if (!HasSpare || IsZero(block)) return false;
            int s = SpareOf(block);
            byte marker = Kind == NandKind.BigBlock ? _raw[s] : _raw[s + 5];
            return marker != 0xFF;
        }

        /// <summary>A block whose first page (data and spare) is all zeros: a failed read rather than a block marked bad.</summary>
        public bool IsZero(int block)
        {
            int p = block * BlockRaw;
            for (int i = 0; i < PageRaw; i++) if (_raw[p + i] != 0) return false;
            return true;
        }

        /// <summary>Blocks that read back as all zeros (a failed or interrupted dump).</summary>
        public readonly List<int> ZeroBlocks = new List<int>();

        public int FsBlockType(int block) => _raw[SpareOf(block) + 12] & 0x3F;

        public uint FsSequence(int block)
        {
            int s = SpareOf(block);
            switch (Kind)
            {
                case NandKind.SmallBlock0: return (uint)(_raw[s + 2] | _raw[s + 3] << 8 | _raw[s + 4] << 16 | _raw[s + 6] << 24);
                case NandKind.SmallBlock1: return (uint)(_raw[s] | _raw[s + 3] << 8 | _raw[s + 4] << 16 | _raw[s + 6] << 24);
                default: return (uint)(_raw[s + 5] | _raw[s + 4] << 8 | _raw[s + 3] << 16);
            }
        }

        void Build_()
        {
            Blocks = (int)(_raw.Length / BlockRaw);
            // ---- every page's EDC, the bad blocks
            if (HasSpare)
            {
                for (int b = 0; b < Blocks; b++)
                {
                    if (IsZero(b)) ZeroBlocks.Add(b);
                    if (IsBad(b)) { BadBlocks.Add(b); continue; }
                    for (int p = 0; p < PagesPerBlock; p++)
                    {
                        int page = b * BlockRaw + p * PageRaw;
                        if (Erased(_raw, page)) continue;
                        PagesChecked++;
                        if (Edc(_raw, page) != StoredEdc(_raw, page)) EccErrors.Add(b * PagesPerBlock + p);
                    }
                }
            }
            // ---- the data without spare bytes; a bad block's data comes from the block that took its ID
            Data = new byte[Blocks * BlockData];
            for (int b = 0; b < Blocks; b++)
            {
                int src = b;
                if (HasSpare && BadBlocks.Contains(b))
                {
                    int r = -1;
                    for (int c = Blocks - 1; c >= 0; c--) if (c != b && !BadBlocks.Contains(c) && BlockId(c) == b && FsBlockType(c) == 0) { r = c; break; }
                    if (r < 0) continue;      // lost: left as zeros
                    Remapped[b] = r;
                    src = r;
                }
                if (HasSpare)
                    for (int p = 0; p < PagesPerBlock; p++)
                        Buffer.BlockCopy(_raw, src * BlockRaw + p * PageRaw, Data, b * BlockData + p * PageData, PageData);
                else Buffer.BlockCopy(_raw, src * BlockData, Data, b * BlockData, BlockData);
            }
            ReadHeader();
            ReadBootloaders();
            ReadSmc();
            ReadSmcConfig();
            ReadKeyVault();
            if (HasSpare) ReadFileSystem();
        }

        // ================================================================ header, boot chain, patch slots

        static ushort U16(byte[] b, int o) => (ushort)(b[o] << 8 | b[o + 1]);
        static uint U32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

        void ReadHeader()
        {
            var d = Data;
            Magic = U16(d, 0); Build = U16(d, 2); Qfe = U16(d, 4); HeaderFlags = U16(d, 6);
            CbOffset = U32(d, 8); SystemLength = U32(d, 0x0C);
            int end = Array.IndexOf(d, (byte)0, 0x10, 0x40);
            Copyright = Encoding.GetEncoding(1252).GetString(d, 0x10, (end < 0 ? 0x50 : end) - 0x10);
            KvLength = U32(d, 0x60); PatchSlotAddress = U32(d, 0x64); PatchSlotCount = U16(d, 0x68); KvVersion = U16(d, 0x6A);
            KvAddress = U32(d, 0x6C); PatchSlotSize = U32(d, 0x70); SmcConfigAddress = U32(d, 0x74); SmcLength = U32(d, 0x78); SmcAddress = U32(d, 0x7C);
            if ((Magic & 0x0FFF) != 0x0F4F) throw new InvalidDataException("This is not an Xbox 360 flash dump (no 0xFF4F / 0x0F4F header).");
        }

        NandBootloader BootloaderAt(int off, string name, int slot)
        {
            if (off <= 0 || off + 0x10 > Data.Length) return null;
            string magic = Encoding.ASCII.GetString(Data, off, 2);
            if (magic[0] < 'A' || magic[0] > 'Z' || magic[1] < 'A' || magic[1] > 'Z') return null;
            return new NandBootloader
            {
                Name = name ?? magic, Magic = magic, Offset = off, Slot = slot,
                Build = U16(Data, off + 2), Qfe = U16(Data, off + 4), Flags = U16(Data, off + 6), Entry = U32(Data, off + 8), Size = U32(Data, off + 12)
            };
        }

        void ReadBootloaders()
        {
            // the boot chain: CB_A, then - when the CB is split - CB_B (the last CB before the CD; xeBuild glitch images put
            // more CBs in between, CB_X), CD, CE. SB / SC / SD / SE on devkit images.
            int off = (int)CbOffset;
            var first = BootloaderAt(off, null, -1);
            if (first == null) return;
            first.Name = first.Magic + "_A";
            Bootloaders.Add(first);
            off += (int)Align16(first.Size);
            var next = BootloaderAt(off, null, -1);
            var more = new List<NandBootloader>();
            while (next != null && next.Magic == first.Magic && more.Count < 8 && next.Size > 0)
            {
                more.Add(next);
                off += (int)Align16(next.Size);
                next = BootloaderAt(off, null, -1);
            }
            for (int i = 0; i < more.Count; i++)
            {
                more[i].Name = first.Magic + (i == more.Count - 1 ? "_B" : "_X" + (more.Count > 2 ? (i + 1).ToString() : ""));
                Bootloaders.Add(more[i]);
            }
            if (next != null && (next.Magic == "CD" || next.Magic == "SD" || next.Magic == "SC"))
            {
                if (next.Magic == "SC") { Bootloaders.Add(next); off += (int)Align16(next.Size); next = BootloaderAt(off, null, -1); }
                if (next != null && (next.Magic == "CD" || next.Magic == "SD")) { Bootloaders.Add(next); off += (int)Align16(next.Size); next = BootloaderAt(off, null, -1); }
                if (next != null && (next.Magic == "CE" || next.Magic == "SE")) Bootloaders.Add(next);
            }
            // the patch slots: CF then CG. Older consoles and xeBuild images leave the slot size at 0: the slots are then 64 KB
            // apart (128 KB on big-block flash), as J-Runner and the console take them.
            uint slotSize = PatchSlotSize != 0 ? PatchSlotSize : (uint)(IsBigBlock ? 0x20000 : 0x10000);
            for (int s = 0; s < Math.Max(2, (int)PatchSlotCount); s++)
            {
                long at = PatchSlotAddress + (long)s * slotSize;
                if (PatchSlotAddress == 0 || at + 0x10 > Data.Length) break;
                var cf = BootloaderAt((int)at, null, s);
                if (cf == null || (cf.Magic != "CF" && cf.Magic != "SF")) continue;
                cf.Name = cf.Magic + "_" + s;
                Bootloaders.Add(cf);
                var cg = BootloaderAt((int)(at + Align16(cf.Size)), null, s);
                if (cg != null && (cg.Magic == "CG" || cg.Magic == "SG")) { cg.Name = cg.Magic + "_" + s; Bootloaders.Add(cg); }
            }
        }

        static uint Align16(uint v) => (v + 15) & ~15u;

        public NandBootloader Stage(string name) => Bootloaders.FirstOrDefault(b => b.Name == name);

        /// <summary>A stage's bytes as stored (its size rounded up to 16 bytes, as J-Runner extracts it).</summary>
        public byte[] StageBytes(NandBootloader stage)
        {
            if (stage == null) return null;
            int len = (int)Math.Min(Align16(stage.Size), (uint)Math.Max(0, Data.Length - stage.Offset));
            var b = new byte[len];
            Buffer.BlockCopy(Data, stage.Offset, b, 0, len);
            return b;
        }

        // ================================================================ board

        /// <summary>
        /// The CB build that is the console's own: CB_B when a glitch image's loader stages (CB_X) come first, else CB_A -
        /// the one J-Runner's identifyConsole looks at.
        /// </summary>
        public int ConsoleCb
        {
            get
            {
                bool loader = Bootloaders.Any(b => b.Slot < 0 && (b.Name.StartsWith("CB_X") || b.Name.StartsWith("SB_X")));
                var cb = loader ? Stage("CB_B") ?? Stage("SB_B") : Stage("CB_A") ?? Stage("SB_A");
                return cb?.Build ?? 0;
            }
        }

        /// <summary>The board a CB build belongs to: J-Runner's own ranges (identifyConsole) and its CB table (ntable).</summary>
        public static NandBoard BoardOfCb(int cb)
        {
            if (cb >= 9188 && cb <= 9250) return NandBoard.Trinity;
            if (cb >= 16000) return NandBoard.Winchester;
            if (cb >= 13121 && cb <= 13200) return NandBoard.Corona;
            if (cb >= 6712 && cb <= 6780) return NandBoard.Jasper;
            if (cb >= 4540 && cb <= 4590) return NandBoard.Zephyr;
            if (cb >= 1888 && cb <= 1960 || cb >= 7373 && cb <= 7378 || cb == 8192) return NandBoard.Xenon;
            if (cb >= 5760 && cb <= 5780) return NandBoard.Falcon;
            return NandBoard.Unknown;      // 10918 / 15432 (glitch loaders), 10375 / 14352 (devkit glitch CBs for every board)
        }

        /// <summary>The board the console's CB says; a Falcon CB_A with an Elpis CB_B (7373-7378) is a Xenon, as J-Runner takes it.</summary>
        public NandBoard CbBoard
        {
            get
            {
                var b = BoardOfCb(ConsoleCb);
                int cbB = (Stage("CB_B") ?? Stage("SB_B"))?.Build ?? 0;
                return b == NandBoard.Falcon && cbB >= 7373 && cbB <= 7378 ? NandBoard.Xenon : b;
            }
        }

        bool Elpis
        {
            get
            {
                int cbB = (Stage("CB_B") ?? Stage("SB_B"))?.Build ?? 0;
                return ConsoleCb >= 7373 && ConsoleCb <= 7378 || ConsoleCb == 8192 || cbB >= 7373 && cbB <= 7378;
            }
        }

        /// <summary>The board the SMC program is written for: the high 4 bits of its byte 0x100 (J-Runner's console_types).</summary>
        public NandBoard SmcBoard
        {
            get
            {
                if (Smc == null || Smc.Length <= 0x100) return NandBoard.Unknown;
                int n = (Smc[0x100] >> 4) & 0xF;
                return n >= 1 && n <= 7 ? (NandBoard)n : NandBoard.Unknown;
            }
        }

        /// <summary>
        /// The SMC carries a glitch / JTAG patch - J-Runner's own test (checkifhackedSMC): the 16 bytes at 0x2DB0 of the
        /// decrypted SMC are zero in Microsoft's SMC and hold the patch in a hacked one. That holds for Xenon to Jasper SMCs
        /// (on 71 dumps: every original clean, every glitch image patched); Trinity, Corona and Winchester SMCs keep code
        /// there, so for them the test says nothing and this is null.
        /// </summary>
        public bool? SmcPatched
        {
            get
            {
                if (Smc == null || Smc.Length < 0x2DC0) return null;
                var b = SmcBoard;
                if (b == NandBoard.Unknown || b >= NandBoard.Trinity) return null;
                return Enumerable.Range(0x2DB0, 0x10).Any(i => Smc[i] != 0);
            }
        }

        /// <summary>J-Runner's byte test as it is, for any SMC (what checkifhackedSMC answers - meaningful for Xenon to Jasper only).</summary>
        public bool SmcPatchBytesSet => Smc != null && Smc.Length >= 0x2DC0 && Enumerable.Range(0x2DB0, 0x10).Any(i => Smc[i] != 0);

        /// <summary>
        /// The board, from three things that a hack image can each change on its own: the CB (RGH3 images put Trinity's
        /// zero-paired CB 9188 on a Corona; glitch images start with loader CBs), the SMC (RGH 1.2 images put Jasper's SMC on a
        /// Falcon) and the flash itself (eMMC only on Corona / Winchester 4 GB). J-Runner weighs the same three.
        /// </summary>
        public NandBoard BoardFamily
        {
            get
            {
                NandBoard cb = CbBoard, smc = SmcBoard;
                if (!HasSpare) return cb == NandBoard.Winchester || smc == NandBoard.Winchester ? NandBoard.Winchester : NandBoard.Corona;
                if (cb == NandBoard.Unknown) return smc;
                if (smc == NandBoard.Unknown || smc == cb) return cb;
                if (cb == NandBoard.Trinity && smc >= NandBoard.Corona) return smc;
                return cb;
            }
        }

        public static string BoardName(NandBoard b)
        {
            switch (b)
            {
                case NandBoard.Falcon: return "Falcon / Opus";
                case NandBoard.Unknown: return "unknown board";
                default: return b.ToString();
            }
        }

        /// <summary>The board and its flash, e.g. "Jasper, big block (256 / 512 MB)", "Corona 4 GB", "Trinity (16 MB)".</summary>
        public string Board
        {
            get
            {
                var b = BoardFamily;
                string name = BoardName(b);
                if (b == NandBoard.Xenon && Elpis) name = "Xenon (Elpis)";
                if (!HasSpare) return name + " 4 GB";
                if (IsBigBlock) return name + ", big block (256 / 512 MB)";
                string size = FileLength >= MaxRead ? "64 MB" : "16 MB";
                return b == NandBoard.Jasper ? name + ", small block (" + size + ")" : name + " (" + size + ")";
            }
        }

        /// <summary>What the boot chain and SMC say about the image on the flash: glitch loaders, another board's CB or SMC.</summary>
        public List<string> ImageNotes
        {
            get
            {
                var notes = new List<string>();
                var loaders = Bootloaders.Where(b => b.Slot < 0 && (b.Name.StartsWith("CB_X") || b.Name.StartsWith("SB_X"))).ToList();
                var first = Stage("CB_A") ?? Stage("SB_A");
                var own = Stage("CB_B") ?? Stage("SB_B");
                var board = BoardFamily;
                if (loaders.Count > 0 && first != null && own != null)
                    notes.Add("Glitch image: the loader " + first.Name + " " + first.Build + " and " + string.Join(", ", loaders.Select(l => l.Name + " " + l.Build)) + " start the console's own " + own.Name + " " + own.Build + ".");
                var cbBoard = CbBoard;
                if (cbBoard != NandBoard.Unknown && cbBoard != board)
                    notes.Add("Its CB (" + ConsoleCb + ") is " + BoardName(cbBoard) + "'s" + (cbBoard == NandBoard.Trinity && board >= NandBoard.Corona ? " - zero-paired, as RGH3 images use it on a " + BoardName(board) : "") + ".");
                var smcBoard = SmcBoard;
                if (smcBoard != NandBoard.Unknown && smcBoard != board)
                    notes.Add("Its SMC is " + BoardName(smcBoard) + "'s" + (smcBoard == NandBoard.Jasper && board <= NandBoard.Falcon ? ", as RGH 1.2 images put it on a " + BoardName(board) : "") + ".");
                if (SmcPatched == true) notes.Add("The SMC is patched (a glitch / JTAG image's SMC) - J-Runner's test.");
                if (Copyright.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) < 0 && Copyright.Trim().Length > 0) notes.Add("Its header text is not Microsoft's: \"" + Copyright.Trim() + "\".");
                return notes;
            }
        }

        // ================================================================ SMC

        void ReadSmc()
        {
            if (SmcLength == 0 || SmcLength > 0x10000 || SmcAddress + SmcLength > Data.Length) return;
            SmcEncrypted = new byte[SmcLength];
            Buffer.BlockCopy(Data, (int)SmcAddress, SmcEncrypted, 0, (int)SmcLength);
            Smc = DecryptSmc(SmcEncrypted);
        }

        /// <summary>The SMC's own stream cipher (published on Free60): a 4-byte key that moves with each byte decrypted.</summary>
        public static byte[] DecryptSmc(byte[] enc)
        {
            var key = new byte[] { 0x42, 0x75, 0x4E, 0x79 };
            var dec = new byte[enc.Length];
            for (int i = 0; i < enc.Length; i++)
            {
                int j = enc[i];
                int mod = j * 0xFB;
                dec[i] = (byte)(j ^ key[i & 3]);
                key[(i + 1) & 3] += (byte)mod;
                key[(i + 2) & 3] += (byte)(mod >> 8);
            }
            return dec;
        }

        public static byte[] EncryptSmc(byte[] dec)
        {
            var key = new byte[] { 0x42, 0x75, 0x4E, 0x79 };
            var enc = new byte[dec.Length];
            for (int i = 0; i < dec.Length; i++)
            {
                int j = dec[i] ^ key[i & 3];
                enc[i] = (byte)j;
                int mod = j * 0xFB;
                key[(i + 1) & 3] += (byte)mod;
                key[(i + 2) & 3] += (byte)(mod >> 8);
            }
            return enc;
        }

        /// <summary>"major.minor" as J-Runner shows it, from the decrypted SMC's version bytes.</summary>
        public string SmcVersion => Smc == null || Smc.Length < 0x103 ? "" : Smc[0x101] + "." + Smc[0x102].ToString("D2");

        // ================================================================ SMC config

        /// <summary>The SMC config, or null when the place J-Runner reads it from is blank.</summary>
        public NandSmcConfig SmcConfig { get; private set; }

        void ReadSmcConfig()
        {
            // J-Runner's places (getsmcconfig; its own extracts of real dumps are these bytes): 64 KB at 0xF70000 on 16 MB
            // small-block flash, 0x3DF0000 on 64 MB, 512 KB at 0x3B80000 on big-block flash, 64 KB at 0x2FF0000 on eMMC; the
            // config 0xC000 into it (0x60000 on big block). The flash header's own field (0x74) is 0 on every dump seen.
            long at; int len, off;
            if (!HasSpare) { at = 0x2FF0000; len = 0x10000; off = 0xC000; }
            else if (IsBigBlock) { at = 0x3B80000; len = 0x80000; off = 0x60000; }
            else if (FileLength >= MaxRead) { at = 0x3DF0000; len = 0x10000; off = 0xC000; }
            else { at = 0xF70000; len = 0x10000; off = 0xC000; }
            if (at + len > Data.Length) return;
            var area = new byte[len];
            Buffer.BlockCopy(Data, (int)at, area, 0, len);
            var c = new NandSmcConfig { Area = area, ConfigOffset = off, Address = at };
            if (!c.Blank) SmcConfig = c;
        }

        // ================================================================ FCRT

        /// <summary>fcrt.bin from the flash file system (the DVD drive certificate on consoles that need one), or null.</summary>
        public byte[] Fcrt
        {
            get
            {
                var f = Files.FirstOrDefault(x => x.Name.Equals("fcrt.bin", StringComparison.OrdinalIgnoreCase));
                return f == null ? null : ReadFile(f);
            }
        }

        /// <summary>
        /// Decrypts fcrt.bin with the console's CPU key as J-Runner does (decrypt_fcrt): AES-128-CBC with the IV at 0x100 over
        /// 0x3EC0 bytes from the offset at 0x11C; right only when their SHA-1 is the one stored at 0x12C. Null otherwise.
        /// </summary>
        public static byte[] DecryptFcrt(byte[] fcrt, byte[] cpuKey)
        {
            if (fcrt == null || fcrt.Length != 0x4000 || cpuKey == null || cpuKey.Length != 16) return null;
            int off = fcrt[0x11C] << 24 | fcrt[0x11D] << 16 | fcrt[0x11E] << 8 | fcrt[0x11F];
            const int len = 0x3EC0;
            if (off < 0 || off > fcrt.Length - len) return null;
            byte[] plain;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = cpuKey;
                var iv = new byte[16];
                Buffer.BlockCopy(fcrt, 0x100, iv, 0, 16);
                aes.IV = iv;
                using (var d = aes.CreateDecryptor()) plain = d.TransformFinalBlock(fcrt, off, len);
            }
            byte[] sha;
            using (var s = SHA1.Create()) sha = s.ComputeHash(plain);
            for (int i = 0; i < 20; i++) if (sha[i] != fcrt[0x12C + i]) return null;
            var result = new byte[off + plain.Length];
            Buffer.BlockCopy(fcrt, 0, result, 0, off);
            Buffer.BlockCopy(plain, 0, result, off, plain.Length);
            return result;
        }

        // ================================================================ keyvault

        void ReadKeyVault()
        {
            if (KvAddress == 0 || KvLength == 0 || KvLength > 0x10000 || KvAddress + KvLength > Data.Length) return;
            KeyVaultEncrypted = new byte[KvLength];
            Buffer.BlockCopy(Data, (int)KvAddress, KeyVaultEncrypted, 0, (int)KvLength);
        }

        /// <summary>Parses "0123...ABCD" (32 hex digits, spaces / dashes allowed) into a CPU key; null when it is not one.</summary>
        public static byte[] ParseCpuKey(string text)
        {
            if (text == null) return null;
            var s = new string(text.Where(Uri.IsHexDigit).ToArray());
            if (s.Length != 32) return null;
            var k = new byte[16];
            for (int i = 0; i < 16; i++) k[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return k;
        }

        /// <summary>
        /// Decrypts the keyvault with the console's CPU key: RC4 keyed with HMAC-SHA1(CPU key, the 16-byte nonce)[0..16] over
        /// everything after the nonce. The key is right when HMAC-SHA1(CPU key, the decrypted data + the header's 2-byte
        /// keyvault version, 07 12) gives the nonce back. Nothing is written anywhere.
        /// </summary>
        public NandKeyVault DecryptKeyVault(byte[] cpuKey)
        {
            if (KeyVaultEncrypted == null || cpuKey == null || cpuKey.Length != 16) return null;
            var kv = KeyVaultEncrypted;
            byte[] rc4Key;
            using (var h = new HMACSHA1(cpuKey)) rc4Key = h.ComputeHash(kv, 0, 16).Take(16).ToArray();
            var dec = new byte[kv.Length];
            Buffer.BlockCopy(kv, 0, dec, 0, 16);
            Rc4(rc4Key, kv, 16, dec, 16, kv.Length - 16);
            var r = new NandKeyVault { Decrypted = dec };
            var check = new byte[kv.Length - 16 + 2];
            Buffer.BlockCopy(dec, 16, check, 0, kv.Length - 16);
            check[check.Length - 2] = (byte)(KvVersion >> 8);
            check[check.Length - 1] = (byte)KvVersion;
            using (var h = new HMACSHA1(cpuKey)) r.CpuKeyMatches = h.ComputeHash(check).Take(16).SequenceEqual(kv.Take(16));
            if (!r.CpuKeyMatches) return r;
            var latin = Encoding.GetEncoding(1252);
            string Ascii(int o, int n) => latin.GetString(dec, o, n).TrimEnd('\0');
            string HexOf(int o, int n) => BitConverter.ToString(dec, o, n).Replace("-", "");
            r.Serial = Ascii(0xB0, 0x0C);
            r.MotherboardSerial = HexOf(0xBC, 0x08);
            r.Region = HexOf(0xC8, 0x02);
            r.DvdKey = HexOf(0x100, 0x10);
            r.ConsoleId = HexOf(0x9CA, 0x05);
            r.ManufacturingDate = Ascii(0x9E4, 0x08);
            r.Osig = Ascii(0xC92, 0x1C);
            r.OddFeatures = U16(dec, 0x1C);
            r.Type = dec.Skip(0x0C1C).Take(4).All(b => b == 0) ? 1 : 2;
            r.NeedsFcrt = (r.OddFeatures & 0x120) != 0;
            return r;
        }

        static void Rc4(byte[] key, byte[] src, int so, byte[] dst, int dof, int n)
        {
            var s = new byte[256];
            for (int i = 0; i < 256; i++) s[i] = (byte)i;
            for (int i = 0, j = 0; i < 256; i++) { j = (j + s[i] + key[i % key.Length]) & 255; var t = s[i]; s[i] = s[j]; s[j] = t; }
            for (int k = 0, i = 0, j = 0; k < n; k++)
            {
                i = (i + 1) & 255; j = (j + s[i]) & 255;
                var t = s[i]; s[i] = s[j]; s[j] = t;
                dst[dof + k] = (byte)(src[so + k] ^ s[(s[i] + s[j]) & 255]);
            }
        }

        // ================================================================ flash file system

        /// <summary>The file system root's block type: 0x30 on small-block flash, 0x2C on big-block flash.</summary>
        int RootType => IsBigBlock ? 0x2C : 0x30;

        /// <summary>Every file-system root block in the dump (newest first): older roots are earlier states of the flash.</summary>
        public List<int> FsRoots { get; private set; } = new List<int>();

        void ReadFileSystem()
        {
            FsRoots = Enumerable.Range(0, Blocks).Where(b => !BadBlocks.Contains(b) && FsBlockType(b) == RootType).OrderByDescending(FsSequence).ThenByDescending(b => b).ToList();
            // the live root is the one written last: the highest sequence number (proven on real dumps: only it has every
            // system XEX intact - older roots point at blocks written over since)
            if (FsRoots.Count > 0) UseFsRoot(FsRoots[0]);
        }

        /// <summary>Reads the file system as one root block describes it (the newest is read when the dump is opened).</summary>
        public void UseFsRoot(int block)
        {
            if (!HasSpare || block < 0 || block >= Blocks) throw new ArgumentOutOfRangeException(nameof(block));
            Files.Clear();
            FsRootBlock = block;
            FsRootSequence = FsSequence(block);
            // the root: 32 pages of 512 bytes - even pages are the allocation table (256 big-endian links each),
            // odd pages hold 16 entries of 0x20 bytes: name[0x16], start unit, size, timestamp
            var root = new byte[0x4000];
            for (int p = 0; p < 32; p++) Buffer.BlockCopy(_raw, FsRootBlock * BlockRaw + p * PageRaw, root, p * PageData, PageData);
            _fat = new ushort[16 * 256];
            for (int p = 0; p < 16; p++)
                for (int i = 0; i < 256; i++) _fat[p * 256 + i] = U16(root, (p * 2) * PageData + i * 2);
            // where each 16 KB unit of the file system is. Small block: the block whose spare ID is that unit. Big block: the
            // file system numbers 16 KB units from a base that sits below the end of the first 64 MB; the root's spare bytes 8
            // and 9 say how far (J-Runner's getBlockOffset: base = units - 256 - spare[9] * 8 - spare[8] * 32)
            _fsMap = new Dictionary<int, int>();
            if (IsBigBlock)
            {
                int s = SpareOf(FsRootBlock);
                int units = Data.Length / SmallBlockData;
                FsBase = units - 256 - _raw[s + 9] * 8 - (_raw[s + 8] << 5);
            }
            else
                for (int b = 0; b < Blocks; b++)
                {
                    if (BadBlocks.Contains(b)) continue;
                    int t = FsBlockType(b);
                    if (t == RootType || IsMobileType(t)) continue;
                    int id = BlockId(b);
                    if (!_fsMap.ContainsKey(id)) _fsMap[id] = b;
                }
            for (int p = 0; p < 16; p++)
            {
                int pageOff = (p * 2 + 1) * PageData;
                for (int e = 0; e < 16; e++)
                {
                    int o = pageOff + e * 0x20;
                    // empty, or deleted (its first name byte is overwritten with a control character)
                    if (root[o] < 0x20 || root[o] >= 0x7F) continue;
                    int nameEnd = Array.IndexOf(root, (byte)0, o, 0x16);
                    var f = new NandFile
                    {
                        Name = Encoding.ASCII.GetString(root, o, (nameEnd < 0 ? o + 0x16 : nameEnd) - o),
                        Start = U16(root, o + 0x16),
                        Size = U32(root, o + 0x18),
                        Timestamp = U32(root, o + 0x1C)
                    };
                    int unit = f.Start, guard = 0;
                    long need = (f.Size + 0x3FFF) / 0x4000;
                    while (f.Chain.Count < need && guard++ < 0x2000)
                    {
                        f.Chain.Add(unit);
                        int next = _fat[unit & 0xFFF] & 0x1FFF;
                        if (next >= 0x1FF0) break;
                        unit = next;
                    }
                    Files.Add(f);
                }
            }
        }

        bool IsMobileType(int t) => IsBigBlock ? t >= 0x2D && t <= 0x35 : t >= 0x31 && t <= 0x3F;

        /// <summary>Big-block flash: the 16 KB unit (counted from the start of the dump) where file-system unit 0 is.</summary>
        public int FsBase { get; private set; }

        /// <summary>The raw-dump offset of a file-system unit's data (16 KB, possibly inside a 128 KB big block).</summary>
        int UnitRaw(int unit, out bool found)
        {
            if (IsBigBlock)
            {
                long u = FsBase + (long)unit;
                found = u >= 0 && (u + 1) * 32 * PageRaw <= _raw.Length;
                return found ? (int)(u * 32 * PageRaw) : -1;
            }
            // small block: unit n is physical block n - unless n is bad, then the good block that took its ID
            if (unit >= 0 && unit < Blocks && !BadBlocks.Contains(unit)) { found = true; return unit * BlockRaw; }
            found = _fsMap != null && _fsMap.TryGetValue(unit, out int block);
            return found ? _fsMap[unit] * BlockRaw : -1;
        }

        /// <summary>A file of the flash file system, exactly its size long.</summary>
        public byte[] ReadFile(NandFile f)
        {
            var o = new byte[f.Size];
            int at = 0;
            foreach (int unit in f.Chain)
            {
                int raw = UnitRaw(unit, out bool found);
                if (!found) throw new InvalidDataException(f.Name + ": unit " + unit + " is not in the dump.");
                for (int p = 0; p < 32 && at < o.Length; p++)
                {
                    int n = Math.Min(PageData, o.Length - at);
                    Buffer.BlockCopy(_raw, raw + p * PageRaw, o, at, n);
                    at += n;
                }
            }
            if (at < o.Length) throw new InvalidDataException(f.Name + ": the chain ends before the file does.");
            return o;
        }

        // ================================================================ writing a file back (boot-animation swap)

        /// <summary>A file in the flash file system can be replaced when the dump has one (small or big block, not a Corona 4 GB eMMC).</summary>
        public bool CanReplaceFiles => HasSpare && FsRootBlock >= 0;

        /// <summary>Recomputes a page's 26-bit EDC and stores it in the spare, keeping the rest of the spare (block ID, type, sequence).</summary>
        static void WriteEdc(byte[] buf, int page)
        {
            int s = page + PageData;
            uint e = Edc(buf, page);                 // reads only the data and spare[0..11] + spare[12] low 6 bits - never the stored EDC
            buf[s + 12] = (byte)((buf[s + 12] & 0x3F) | ((e & 3) << 6));
            buf[s + 13] = (byte)((e >> 2) & 0xFF);
            buf[s + 14] = (byte)((e >> 10) & 0xFF);
            buf[s + 15] = (byte)((e >> 18) & 0xFF);
        }

        /// <summary>
        /// A copy of the whole dump with one flash-file-system file's contents replaced, for swapping a boot animation
        /// (bootanim.xex) or another system file. The file keeps its own blocks: the new data is written into them, each page's
        /// spare is kept and only its EDC recomputed, the directory entry's size is updated and the allocation table is trimmed
        /// when the new file is shorter. Replacing a file with its own bytes gives back a byte-identical dump. A file that needs
        /// more 16 KB units than it already holds is refused (growing the file system is not supported). The source dump is only
        /// read; the result is returned for the caller to save to a new file.
        /// </summary>
        public byte[] BuildWithReplacedFile(string name, byte[] newData, out string warning)
        {
            warning = null;
            if (!HasSpare) throw new InvalidOperationException("A Corona 4 GB keeps its files on the eMMC, not in the flash file system - this cannot replace them.");
            if (FsRootBlock < 0) throw new InvalidOperationException("This dump has no flash file system to write into.");
            if (newData == null || newData.Length == 0) throw new ArgumentException("The replacement file is empty.");
            var file = Files.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (file == null) throw new FileNotFoundException("\"" + name + "\" is not in this dump's flash file system.");
            int haveUnits = file.Chain.Count;
            int needUnits = (int)((newData.LongLength + 0x3FFF) / 0x4000);
            if (needUnits > haveUnits)
                throw new InvalidDataException(name + " would need " + needUnits + " blocks of 16 KB (" + newData.Length + " bytes); the one in this dump holds " + haveUnits +
                    ". Growing a file in the flash file system is not supported yet - use a " + name + " that is the same size or smaller (" + (haveUnits * 0x4000) + " bytes or less).");

            var full = File.ReadAllBytes(Path);
            if (full.Length < _raw.Length) throw new IOException("The dump on disk is shorter than when it was opened.");

            // ---- write the new data into the file's own units, keeping each page's spare and recomputing its EDC
            long at = 0;
            var touchedPages = new List<int>();
            for (int i = 0; i < needUnits; i++)
            {
                int raw = UnitRaw(file.Chain[i], out bool found);
                if (!found) throw new InvalidDataException(name + ": unit " + file.Chain[i] + " is not in the dump.");
                for (int p = 0; p < 32 && at < newData.LongLength; p++)
                {
                    int page = raw + p * PageRaw;
                    int n = (int)Math.Min(PageData, newData.LongLength - at);
                    Buffer.BlockCopy(newData, (int)at, full, page, n);
                    WriteEdc(full, page);
                    touchedPages.Add(page);
                    at += n;
                }
            }

            // ---- the root block: trim the allocation table and set the directory entry's size
            int rootRaw = FsRootBlock * BlockRaw;
            var changedRootPages = new HashSet<int>();
            void SetFat(int unit, int value)
            {
                int k = unit & 0xFFF;
                int evenPage = (k / 256) * 2;
                int off = rootRaw + evenPage * PageRaw + (k % 256) * 2;
                full[off] = (byte)(value >> 8); full[off + 1] = (byte)value;
                changedRootPages.Add(evenPage);
            }
            for (int i = 0; i < needUnits - 1; i++) SetFat(file.Chain[i], file.Chain[i + 1]);
            SetFat(file.Chain[needUnits - 1], 0x1FFF);                         // end of chain
            for (int i = needUnits; i < haveUnits; i++) SetFat(file.Chain[i], 0x0000);   // free the units no longer needed

            // the directory entry: odd pages, 16 entries of 0x20 (name[0x16], start, size, timestamp)
            bool wroteSize = false;
            for (int p = 1; p < 32 && !wroteSize; p += 2)
                for (int e = 0; e < 16; e++)
                {
                    int o = rootRaw + p * PageRaw + e * 0x20;
                    int nameEnd = Array.IndexOf(full, (byte)0, o, 0x16);
                    string entry = Encoding.ASCII.GetString(full, o, (nameEnd < 0 ? o + 0x16 : nameEnd) - o);
                    if (full[o] >= 0x20 && full[o] < 0x7F && entry.Equals(name, StringComparison.Ordinal) && U16(full, o + 0x16) == file.Start)
                    {
                        full[o + 0x18] = (byte)(newData.Length >> 24); full[o + 0x19] = (byte)(newData.Length >> 16);
                        full[o + 0x1A] = (byte)(newData.Length >> 8); full[o + 0x1B] = (byte)newData.Length;
                        changedRootPages.Add(p);
                        wroteSize = true;
                        break;
                    }
                }
            if (!wroteSize) throw new InvalidDataException(name + ": its directory entry could not be found to update.");

            foreach (int p in changedRootPages) WriteEdc(full, rootRaw + p * PageRaw);

            if (Encoding.ASCII.GetString(newData, 0, Math.Min(4, newData.Length)) != "XEX2" && name.EndsWith(".xex", StringComparison.OrdinalIgnoreCase))
                warning = "The replacement does not start with \"XEX2\" - make sure it is a real " + name + " (a boot animation is an XEX).";
            return full;
        }

        /// <summary>The raw-dump offset of the page that backs a 512-byte-aligned offset of the console's view (Data), bad blocks remapped.</summary>
        int DataPageToRaw(int dataOffset)
        {
            int block = dataOffset / BlockData, within = dataOffset % BlockData;
            int src = Remapped.TryGetValue(block, out int r) ? r : block;
            return src * BlockRaw + (within / PageData) * PageRaw;
        }

        /// <summary>
        /// A copy of the whole dump with the keyvault replaced - swapping in another console's keyvault, as stored (encrypted,
        /// <see cref="KvLength"/> bytes, the same form "Save keyvault (as stored)" writes). The keyvault's blocks keep their spare;
        /// only the data and each page's EDC change, so replacing it with the dump's own keyvault gives a byte-identical dump.
        /// The console the new keyvault belongs to must boot with its own CPU key. The source dump is only read.
        /// </summary>
        public byte[] BuildWithReplacedKeyVault(byte[] rawKv, out string warning)
        {
            warning = null;
            if (!HasSpare) throw new InvalidOperationException("A Corona 4 GB keeps its keyvault on the eMMC - this cannot replace it here.");
            if (KvAddress == 0 || KvLength == 0 || KvAddress + KvLength > Data.Length) throw new InvalidOperationException("This dump has no keyvault in its flash to replace.");
            if (rawKv == null || rawKv.Length != (int)KvLength)
                throw new ArgumentException("A keyvault is " + KvLength + " bytes as stored; this file is " + (rawKv?.Length ?? 0) + ". Give a keyvault saved \"as stored\" (KV_en.bin).");
            var full = File.ReadAllBytes(Path);
            if (full.Length < _raw.Length) throw new IOException("The dump on disk is shorter than when it was opened.");
            for (int off = 0; off < (int)KvLength; off += PageData)
            {
                int page = DataPageToRaw((int)KvAddress + off);
                int n = (int)Math.Min(PageData, KvLength - off);
                Buffer.BlockCopy(rawKv, off, full, page, n);
                WriteEdc(full, page);
            }
            // the keyvault version word (0x0712) sits two bytes before the serial, at 0x1A6 of the decrypted vault - but the
            // stored vault is encrypted, so there is nothing to check here beyond its length.
            return full;
        }

        // ================================================================ export

        /// <summary>The dump as a plain image (no spare bytes) - what "remove ECC" tools write.</summary>
        public byte[] WithoutSpare() => (byte[])Data.Clone();

        // ================================================================ boot stage decryption

        /// <summary>
        /// The 1BL key, published long ago (free60, J-Runner, xeBuild and 360 Flash Tool all carry it), built in at the user's
        /// request (2026-11-24); NandTests checks it against J-Runner's own copy. It opens CB_A and each patch slot's CF; every
        /// later stage's key comes from the stage before it, with the console's CPU key where the console pairs them.
        /// </summary>
        public static readonly byte[] Key1bl = { 0xDD, 0x88, 0xAD, 0x0C, 0x9E, 0xD6, 0x69, 0xE7, 0xB5, 0x67, 0x94, 0xFB, 0x68, 0x56, 0x3E, 0xFA };

        static byte[] Hmac(byte[] key, byte[] data, int off, int len) { using (var h = new HMACSHA1(key)) return h.ComputeHash(data, off, len); }

        /// <summary>
        /// The stage's key is HMAC-SHA1(parent key, the stage's 16-byte nonce at <paramref name="nonceAt"/>)[0..16]; everything
        /// after the nonce is RC4 with that key. The result keeps J-Runner's layout: the header as stored, the stage's own key
        /// where the nonce was, then the decrypted body.
        /// </summary>
        static byte[] Unwrap(byte[] stage, byte[] parentKey, int nonceAt)
        {
            var key = Hmac(parentKey, stage, nonceAt, 0x10).Take(16).ToArray();
            return Unwrap(stage, nonceAt, key);
        }

        static byte[] Unwrap(byte[] stage, int nonceAt, byte[] key)
        {
            var outp = (byte[])stage.Clone();
            Buffer.BlockCopy(key, 0, outp, nonceAt, 16);
            Rc4(key, stage, nonceAt + 0x10, outp, nonceAt + 0x10, stage.Length - nonceAt - 0x10);
            return outp;
        }

        static byte[] Part(byte[] b, int off, int len) { var r = new byte[len]; Buffer.BlockCopy(b, off, r, 0, len); return r; }

        /// <summary>CB_B (or CB_X) with CB_A's key: J-Runner's decrypt_CB_cpukey - the CPU key is mixed in unless CB_A's flags say the
        /// CB is not paired (low flag byte set: zeros instead), and the newer scheme (flag 0x1000) adds CB_A's header, flags cleared.</summary>
        static byte[] DecryptCbWithCbA(byte[] cb, byte[] cbADecrypted, byte[] cpuKey)
        {
            var data = new List<byte>(Part(cb, 0x10, 0x10));
            data.AddRange(cbADecrypted[7] != 0 || cpuKey == null ? new byte[16] : cpuKey);
            if (((cbADecrypted[6] << 8 | cbADecrypted[7]) & 0x1000) != 0)
            {
                var hdr = Part(cbADecrypted, 0, 0x10);
                hdr[6] = 0; hdr[7] = 0;
                data.AddRange(hdr);
            }
            var key = Hmac(Part(cbADecrypted, 0x10, 0x10), data.ToArray(), 0, data.Count).Take(16).ToArray();
            return Unwrap(cb, 0x10, key);
        }

        /// <summary>A CD / CE / SD with its parent's key (J-Runner's decrypt_CD_cpukey): when the result fails J-Runner's check
        /// (bytes 0x40-0x43 and 0x230-0x233 not both blank), the key is run through HMAC-SHA1 once more with the CPU key.</summary>
        static byte[] DecryptWithParent(byte[] stage, byte[] parentDecrypted, byte[] cpuKey, out bool retriedWithCpuKey)
        {
            retriedWithCpuKey = false;
            var key = Hmac(Part(parentDecrypted, 0x10, 0x10), stage, 0x10, 0x10).Take(16).ToArray();
            var dec = Unwrap(stage, 0x10, key);
            if (!CdLooksRight(dec) && cpuKey != null)
            {
                key = Hmac(cpuKey, key, 0, 16).Take(16).ToArray();
                dec = Unwrap(stage, 0x10, key);
                retriedWithCpuKey = true;
            }
            return dec;
        }

        /// <summary>J-Runner's check of a decrypted CD: bytes 0x40-0x43 or bytes 0x230-0x233 are all zero.</summary>
        static bool CdLooksRight(byte[] dec) =>
            dec.Length < 0x234 || Enumerable.Range(0x40, 4).All(i => dec[i] == 0) || Enumerable.Range(0x230, 4).All(i => dec[i] == 0);

        /// <summary>J-Runner's check of a decrypted CB_B (unpack_cbb_data): bytes 0xA0, 0xA7 and 0xAF are zero.</summary>
        static bool CbLooksRight(byte[] dec) => dec.Length > 0x3B1 && dec[0xA0] == 0 && dec[0xA7] == 0 && dec[0xAF] == 0;

        /// <summary>
        /// Every stage of the boot chain and both patch slots decrypted, as J-Runner does it: CB_A and each CF with the 1BL key,
        /// CB_X / CB_B with CB_A's key (and the CPU key on boards that pair them - give it, or zeros are used as J-Runner does),
        /// CD with the last CB's key, CE with CD's, CG with its CF's; devkit SC with a zero key and SD with SC's. A glitch image's
        /// CB_B behind loader stages is stored unencrypted and is taken as it is.
        /// </summary>
        public List<NandDecryptedStage> DecryptBootChain(byte[] cpuKey)
        {
            var list = new List<NandDecryptedStage>();
            NandDecryptedStage Add(NandBootloader st, byte[] data, string how, bool? right)
            {
                var d = new NandDecryptedStage { Stage = st, Data = data, How = how, LooksRight = right };
                list.Add(d);
                return d;
            }
            var chain = Bootloaders.Where(b => b.Slot < 0).ToList();
            var cbA = chain.FirstOrDefault(b => b.Name == "CB_A" || b.Name == "SB_A");
            byte[] cbADec = null, lastCb = null, sc = null, cd = null;
            if (cbA != null)
            {
                cbADec = Unwrap(StageBytes(cbA), Key1bl, 0x10);
                Add(cbA, cbADec, "the 1BL key", null);
                lastCb = cbADec;
            }
            bool loaders = false;
            foreach (var x in chain.Where(b => b.Name.StartsWith("CB_X") || b.Name.StartsWith("SB_X")))
            {
                loaders = true;
                if (cbADec == null) break;
                lastCb = DecryptCbWithCbA(StageBytes(x), cbADec, cpuKey);
                Add(x, lastCb, cbADec[7] != 0 ? "CB_A's key (not paired to a console)" : "CB_A's key and the CPU key", null);
            }
            var cbB = chain.FirstOrDefault(b => b.Name == "CB_B" || b.Name == "SB_B");
            if (cbB != null && cbADec != null)
            {
                if (loaders) { lastCb = StageBytes(cbB); Add(cbB, lastCb, "stored unencrypted (behind the glitch loader)", CbLooksRight(lastCb)); }
                else
                {
                    lastCb = DecryptCbWithCbA(StageBytes(cbB), cbADec, cpuKey);
                    bool paired = cbADec[7] == 0;
                    Add(cbB, lastCb, paired ? (cpuKey != null ? "CB_A's key and the CPU key" : "CB_A's key - this CB is paired to the console: needs the CPU key") : "CB_A's key (not paired to a console)", CbLooksRight(lastCb));
                }
            }
            var scSt = chain.FirstOrDefault(b => b.Magic == "SC");
            if (scSt != null)
            {
                sc = DecryptWithParent(StageBytes(scSt), new byte[0x20], null, out _);
                Add(scSt, sc, "a zero key (devkit)", null);
            }
            var cdSt = chain.FirstOrDefault(b => b.Magic == "CD" || b.Magic == "SD");
            if (cdSt != null && (sc != null || lastCb != null))
            {
                cd = DecryptWithParent(StageBytes(cdSt), sc ?? lastCb, sc != null ? null : cpuKey, out bool retried);
                bool right = CdLooksRight(cd);
                Add(cdSt, cd, sc != null ? "SC's key" : retried ? "the CB's key and the CPU key" : right ? "the CB's key" : "the CB's key - does not look right" + (cpuKey == null ? "; it may need the CPU key" : ""), right);
            }
            var ceSt = chain.FirstOrDefault(b => b.Magic == "CE" || b.Magic == "SE");
            if (ceSt != null && cd != null)
                Add(ceSt, DecryptWithParent(StageBytes(ceSt), cd, null, out _), "CD's key", null);
            foreach (var cf in Bootloaders.Where(b => b.Slot >= 0 && (b.Magic == "CF" || b.Magic == "SF")))
            {
                var cfDec = Unwrap(StageBytes(cf), Key1bl, 0x20);
                Add(cf, cfDec, "the 1BL key", null);
                var cg = Bootloaders.FirstOrDefault(b => b.Slot == cf.Slot && (b.Magic == "CG" || b.Magic == "SG"));
                if (cg != null) Add(cg, Unwrap(StageBytes(cg), Part(cfDec, 0x330, 0x10), 0x10), "its CF's key", null);
            }
            return list;
        }

        /// <summary>
        /// The lockdown values (LDV) and pairing data the boot chain and patch slots were built for, read where J-Runner reads
        /// them: CB_A's decrypted byte 0x3B1 and 0x20-0x22 (taken over by CB_B's when CB_B decrypts right; a CB_B of build 15432
        /// has none), each CF's 0x21F and 0x21C-0x21E. Pairing data is shown byte-reversed, as J-Runner shows it.
        /// </summary>
        public NandLockdown ReadLockdown(List<NandDecryptedStage> chain)
        {
            var r = new NandLockdown();
            string Pd(byte[] d, int at) => "0x" + d[at + 2].ToString("X2") + d[at + 1].ToString("X2") + d[at].ToString("X2");
            var a = chain.FirstOrDefault(s => s.Stage.Name == "CB_A" || s.Stage.Name == "SB_A");
            if (a != null && a.Data.Length > 0x3B1)
            {
                if (a.Data[0x3B1] <= 16) r.CbLdv = a.Data[0x3B1];
                r.CbPairing = Pd(a.Data, 0x20);
            }
            var b = chain.FirstOrDefault(s => s.Stage.Name == "CB_B" || s.Stage.Name == "SB_B");
            r.HasCbB = b != null;
            if (b != null && CbLooksRight(b.Data))
            {
                if (b.Data[2] == 0x3C && b.Data[3] == 0x48) r.CbLdv = 0;
                else if (b.Data[0x3B1] <= 16) r.CbLdv = b.Data[0x3B1];
                r.CbPairing = Pd(b.Data, 0x20);
                r.FromCbB = true;
            }
            foreach (var cf in chain.Where(s => s.Stage.Slot >= 0 && (s.Stage.Magic == "CF" || s.Stage.Magic == "SF")))
                if (cf.Data.Length > 0x21F) r.Slots.Add(new NandSlotLockdown { Slot = cf.Stage.Slot, Build = cf.Stage.Build, Ldv = cf.Data[0x21F], Pairing = Pd(cf.Data, 0x21C) });
            return r;
        }
    }

    /// <summary>A boot or patch-slot stage decrypted (J-Runner's layout: the header, the stage's own key, the body).</summary>
    public sealed class NandDecryptedStage
    {
        public NandBootloader Stage;
        public byte[] Data;
        /// <summary>Which key opened it, in words.</summary>
        public string How;
        /// <summary>J-Runner's own check where it has one (CB_B, CD); null where there is none.</summary>
        public bool? LooksRight;
    }

    public sealed class NandSlotLockdown { public int Slot, Build, Ldv; public string Pairing; }

    /// <summary>The lockdown value (LDV) and pairing data of the CB and of each patch slot.</summary>
    public sealed class NandLockdown
    {
        public int CbLdv;
        public string CbPairing = "";
        /// <summary>Read from CB_B (it decrypted right) rather than CB_A.</summary>
        public bool FromCbB;
        /// <summary>The chain has a CB_B.</summary>
        public bool HasCbB;
        public readonly List<NandSlotLockdown> Slots = new List<NandSlotLockdown>();
        /// <summary>Pairing data 0x000000: the CB is not tied to one console (zero-paired, as glitch / RGH3 images use).</summary>
        public bool ZeroPaired => CbPairing == "0x000000";
    }
}
