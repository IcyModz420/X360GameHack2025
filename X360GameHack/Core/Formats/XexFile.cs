using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Formats
{
    /// <summary>
    /// XEX2 header reader. The header is parsed natively (instant) and the xextool listing
    /// is used for the extras it provides (game icon, base file dump, full info text).
    /// </summary>
    public sealed class XexFile
    {
        /// <summary>
        /// Every xextool limit EXCEPT 'z' (zero the media ID). "-r a" = "mrbdiyvklcz" zeroes the Media ID,
        /// which stops title updates (matched by Media ID) from loading, so X360GameHack never uses it.
        /// </summary>
        public const string SafeLimits = "-r mrbdiyvklc";

        /// <summary>True when an xextool command would zero the Media ID (-r a, or a -r flag set containing z).</summary>
        public static bool ZeroesMediaId(string xextoolArgs)
        {
            var m = Regex.Matches(" " + (xextoolArgs ?? "") + " ", @"\s-r\s+([a-z]+)");
            foreach (Match x in m) if (x.Groups[1].Value.Contains("a") || x.Groups[1].Value.Contains("z")) return true;
            return false;
        }

        /// <summary>Snapshot of both Media ID fields: execution-info (4 bytes) and security-info +0x140 (16 bytes).</summary>
        public sealed class MediaIds
        {
            public uint Exec;
            public byte[] Security = new byte[16];
            public bool SecurityZero => Security.All(b => b == 0);
        }

        public static MediaIds ReadMediaIds(string path)
        {
            var m = new MediaIds();
            try
            {
                var x = Read(path);
                if (!x.Valid) return m;
                m.Exec = x.MediaId;
                byte[] h = BE.ReadHead(path, 0x20);
                uint sec = BE.U32(h, 0x10);
                byte[] s = BE.ReadHead(path, (int)sec + 0x150);
                Buffer.BlockCopy(s, (int)sec + 0x140, m.Security, 0, 16);
            }
            catch { }
            return m;
        }

        /// <summary>
        /// Compares Media IDs before/after an xextool run. Any field that went from non-zero to zero is restored
        /// (exec ID via PatchIds + header digest; the 16-byte security ID is outside the digest). Returns a description or null.
        /// </summary>
        public static string RestoreZeroedMediaIds(string path, MediaIds before)
        {
            var after = ReadMediaIds(path);
            var parts = new System.Collections.Generic.List<string>();
            if (before.Exec != 0 && after.Exec == 0)
            {
                PatchIds(path, null, before.Exec);
                parts.Add("Media ID " + before.Exec.ToString("X8"));
            }
            if (!before.SecurityZero && after.SecurityZero)
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    var h = new byte[0x18]; fs.Read(h, 0, 0x18);
                    fs.Position = BE.U32(h, 0x10) + 0x140;
                    fs.Write(before.Security, 0, 16);
                }
                parts.Add("security media ID " + BitConverter.ToString(before.Security).Replace("-", ""));
            }
            return parts.Count == 0 ? null : string.Join(" and ", parts);
        }

        /// <summary>Reads only the Media ID (0 when the file has none or is not an XEX2).</summary>
        public static uint ReadMediaId(string path)
        {
            try { var x = Read(path); return x.Valid ? x.MediaId : 0; } catch { return 0; }
        }

        private const uint KeyExecutionInfo = 0x00040006;
        private const uint KeyFileFormat = 0x000003FF;
        private const uint KeyOriginalPeName = 0x000183FF;
        private const uint KeyEntryPoint = 0x00010100;
        private const uint KeyImageBase = 0x00010201;
        private const uint KeySystemFlags = 0x00030000;

        public string Path { get; private set; }
        public bool Valid { get; private set; }
        public string Magic { get; private set; } = "";
        public uint ModuleFlags { get; private set; }
        public uint TitleId { get; private set; }
        public uint MediaId { get; private set; }
        public uint Version { get; private set; }
        public uint BaseVersion { get; private set; }
        public int DiscNumber { get; private set; }
        public int DiscCount { get; private set; }
        /// <summary>The execution info's platform and executable-table bytes (+16 / +17) and savegame id (+20).</summary>
        public byte Platform { get; private set; }
        public byte ExecutableType { get; private set; }
        public uint SavegameId { get; private set; }
        public uint EntryPoint { get; private set; }
        public uint LoadAddress { get; private set; }
        public uint ImageSize { get; private set; }
        public string OriginalName { get; private set; } = "";
        public string Encryption { get; private set; } = "Unknown";
        public string Compression { get; private set; } = "Unknown";
        public string Target { get; private set; } = "Unknown";   // Retail / Devkit (from xextool)
        public Image Icon { get; private set; }
        public string ToolListing { get; private set; } = "";
        /// <summary>How the image was decoded (encryption, key, compression), or why the built-in decoder could not.</summary>
        public string DecodeNote { get; private set; } = "";

        /// <summary>File offset of the execution-info block (media/title IDs), or -1.</summary>
        public long ExecutionInfoOffset { get; private set; } = -1;

        /// <summary>Security info +0x10C (big-endian): the image flags. 0x08 = original disc only (as abgx360 reads it).</summary>
        public uint ImageFlags { get; private set; }
        /// <summary>
        /// Security info +0x17C (big-endian): the allowed media types, as abgx360 decodes them - 0x01 hard disk, 0x04 DVD/CD,
        /// 0x08 DVD-5, 0x10 DVD-9, 0x1000 Secure Virtual Optical Device (Games on Demand), 0xFFFFFFFF all.
        /// </summary>
        public uint AllowedMediaTypes { get; private set; }
        public bool HasSecurityInfo { get; private set; }
        public bool OriginalDiscOnly => (ImageFlags & 0x08) != 0;
        /// <summary>The XEX may run from a disc - false for a Games on Demand / digital edition that only allows SVOD or packages.</summary>
        public bool AllowsDisc => !HasSecurityInfo || OriginalDiscOnly || AllowedMediaTypes == 0xFFFFFFFF || (AllowedMediaTypes & 0x1C) != 0;

        public static string VersionString(uint v)
        {
            // major:4 minor:4 build:16 qfe:8
            return $"{v >> 28}.{(v >> 24) & 0xF}.{(v >> 8) & 0xFFFF}.{v & 0xFF}";
        }

        public string ModuleType
        {
            get
            {
                var parts = new StringBuilder();
                if ((ModuleFlags & 0x01) != 0) parts.Append("Title ");
                if ((ModuleFlags & 0x02) != 0) parts.Append("Exports ");
                if ((ModuleFlags & 0x04) != 0) parts.Append("Debugger ");
                if ((ModuleFlags & 0x08) != 0) parts.Append("DLL ");
                if ((ModuleFlags & 0x10) != 0) parts.Append("Module-Patch ");
                if ((ModuleFlags & 0x20) != 0) parts.Append("Full-Patch ");
                if ((ModuleFlags & 0x40) != 0) parts.Append("Delta-Patch ");
                if ((ModuleFlags & 0x80) != 0) parts.Append("User-Mode ");
                return parts.Length == 0 ? "None" : parts.ToString().Trim();
            }
        }

        public static XexFile Read(string path) => Parse(path, null);

        /// <summary>Reads the same facts from the first bytes of an XEX that is already in memory (64 KB is plenty for the IDs and version).</summary>
        public static XexFile ReadHeader(byte[] head, string shownPath = "") => Parse(shownPath, head ?? new byte[0]);

        private static XexFile Parse(string path, byte[] given)
        {
            var x = new XexFile { Path = path };
            try
            {
                byte[] h = given ?? BE.ReadHead(path, 0x10000);
                if (h.Length < 0x18) return x;
                x.Magic = Encoding.ASCII.GetString(h, 0, 4);
                if (x.Magic != "XEX2") return x;
                x.ModuleFlags = BE.U32(h, 0x04);
                uint peOffset = BE.U32(h, 0x08);
                uint secInfo = BE.U32(h, 0x10);
                uint count = BE.U32(h, 0x14);
                if (peOffset > h.Length && given == null) h = BE.ReadHead(path, (int)Math.Min(peOffset, 0x400000));
                if (secInfo >= 0x18 && secInfo + 0x180 <= h.Length)
                {
                    x.HasSecurityInfo = true;
                    x.ImageFlags = BE.U32(h, (int)secInfo + 0x10C);
                    x.AllowedMediaTypes = BE.U32(h, (int)secInfo + 0x17C);
                }

                for (uint i = 0; i < count && 0x18 + i * 8 + 8 <= h.Length; i++)
                {
                    int e = (int)(0x18 + i * 8);
                    uint key = BE.U32(h, e);
                    uint val = BE.U32(h, e + 4);
                    switch (key)
                    {
                        case KeyExecutionInfo:
                            if (val + 24 <= h.Length)
                            {
                                x.ExecutionInfoOffset = val;
                                x.MediaId = BE.U32(h, (int)val);
                                x.Version = BE.U32(h, (int)val + 4);
                                x.BaseVersion = BE.U32(h, (int)val + 8);
                                x.TitleId = BE.U32(h, (int)val + 12);
                                x.Platform = h[val + 16];
                                x.ExecutableType = h[val + 17];
                                x.DiscNumber = h[val + 18];
                                x.DiscCount = h[val + 19];
                                x.SavegameId = BE.U32(h, (int)val + 20);
                            }
                            break;
                        case KeyFileFormat:
                            if (val + 8 <= h.Length)
                            {
                                ushort enc = BE.U16(h, (int)val + 4);
                                ushort comp = BE.U16(h, (int)val + 6);
                                x.Encryption = enc == 0 ? "Not Encrypted" : "Encrypted";
                                x.Compression = comp == 0 ? "None" : comp == 1 ? "Uncompressed (raw)" :
                                                comp == 2 ? "Compressed (LZX)" : comp == 3 ? "Delta" : "Unknown";
                            }
                            break;
                        case KeyOriginalPeName:
                            if (val + 4 <= h.Length)
                            {
                                int len = (int)Math.Min(BE.U32(h, (int)val) - 4, 256);
                                if (len > 0 && val + 4 + len <= h.Length)
                                    x.OriginalName = Encoding.ASCII.GetString(h, (int)val + 4, len).TrimEnd('\0');
                            }
                            break;
                        case KeyEntryPoint: x.EntryPoint = val; break;
                        case KeyImageBase: x.LoadAddress = val; break;
                    }
                }
                if (secInfo + 0x114 <= h.Length)
                {
                    x.ImageSize = BE.U32(h, (int)secInfo + 4);
                    if (x.LoadAddress == 0) x.LoadAddress = BE.U32(h, (int)secInfo + 0x110);
                }
                x.Valid = true;
            }
            catch (Exception ex)
            {
                Log.Warn("XEX header read failed: " + ex.Message);
            }
            return x;
        }

        /// <summary>Where the decrypted/decompressed base file for an XEX is dumped (per-user temp folder).</summary>
        public static string BaseFilePath(string xexPath)
        {
            uint h = 2166136261;
            foreach (char c in (xexPath ?? "").ToLowerInvariant()) h = (h ^ c) * 16777619;
            return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "X360GameHack", "basefile_" + h.ToString("X8") + ".bin");
        }

        private static readonly object BaseFileGate = new object();

        /// <summary>Runs xextool -l to fill the icon / target and dump the base file for the hex editor.</summary>
        public async Task LoadToolInfoAsync()
        {
            string basePath = BaseFilePath(Path);
            try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(basePath)); } catch { }

            // Built in first: decrypt / decompress the image, take the machine type from the key that fits and the
            // icon from the game's own title data. xextool is only asked for what this could not provide.
            bool decoded = false;
            string path = Path;
            uint titleId = TitleId;
            try
            {
                var result = await Task.Run(() =>
                {
                    var image = XexImage.Load(path);
                    // the XEX Tool and the XEX Disassembler both decode a file when it opens: their copies of the base file
                    // take turns (two at once failed with "being used by another process" and the finished decode was thrown
                    // away for xextool's, which cannot name the key). A copy still held open elsewhere is the same image.
                    try { lock (BaseFileGate) File.WriteAllBytes(basePath, image.BaseFile); }
                    catch (IOException) { if (!File.Exists(basePath)) throw; }
                    byte[] icon = null;
                    try { XexTitleData.FromImage(image, titleId).Images.TryGetValue(0x8000, out icon); } catch (InvalidDataException) { }
                    return (Image: image, Icon: icon);
                });
                decoded = true;
                DecodeNote = result.Image.EncryptionName + ", " + result.Image.CompressionName.ToLowerInvariant() + " - decoded by X360GameHack";
                if (result.Image.EncryptionType == 1)
                {
                    Encryption = "Encrypted (" + result.Image.KeyUsed + " key)";
                    Target = result.Image.KeyUsed == "retail" ? "Retail" : "Devkit";
                }
                if (result.Icon != null)
                {
                    try { using (var ms = new MemoryStream(result.Icon)) using (var tmp = Image.FromStream(ms)) Icon = new Bitmap(tmp); }
                    catch { /* not a picture GDI+ can read */ }
                }
            }
            catch (Exception ex) { DecodeNote = "Not decoded natively: " + ex.Message; }
            if (decoded && Target != "Unknown") return;

            var r = await ToolRunner.RunAsync(Tools.XexTool,
                (decoded ? "-l " : "-l -b " + ToolRunner.Q(basePath) + " ") + "-x i " + ToolRunner.Q(Path), quiet: true);
            string output = r.Output ?? "";
            ToolListing = output;
            if (output.Contains("Retail")) Target = "Retail";
            else if (output.Contains("Devkit") || output.Contains("Development")) Target = "Devkit";

            var m = Regex.Match(output, @"<GameIcon\s+format=""base64"">\s*(.*?)\s*</GameIcon>", RegexOptions.Singleline);
            if (m.Success && Icon == null)
            {
                try
                {
                    byte[] img = Convert.FromBase64String(Regex.Replace(m.Groups[1].Value, @"\s+", ""));
                    using (var ms = new MemoryStream(img))
                    using (var tmp = Image.FromStream(ms))
                        Icon = new Bitmap(tmp);
                }
                catch { /* malformed icon - ignore */ }
            }
            if (!Valid)
            {
                // Fall back to xextool's text for files we could not parse ourselves.
                TitleId = TryHex(output, @"Title Id:\s*([0-9A-Fa-f]{8})");
                MediaId = TryHex(output, @"Media Id:\s*([0-9A-Fa-f]{8})");
                LoadAddress = TryHex(output, @"Load Address:\s*([0-9A-Fa-f]{8})");
                EntryPoint = TryHex(output, @"Entry Point:\s*([0-9A-Fa-f]{8})");
            }
        }

        private static uint TryHex(string text, string pattern)
        {
            var m = Regex.Match(text, pattern);
            return m.Success ? Convert.ToUInt32(m.Groups[1].Value, 16) : 0;
        }

        /// <summary>
        /// Patches the Title ID and/or Media ID inside the execution-info header only (the old tool replaced every
        /// matching 4-byte pattern in the whole file, which could corrupt code), then recomputes the header digest.
        /// </summary>
        public static void PatchIds(string path, uint? newTitleId, uint? newMediaId)
        {
            var x = Read(path);
            if (!x.Valid || x.ExecutionInfoOffset < 0)
                throw new InvalidOperationException("This XEX has no execution-info header; IDs cannot be patched.");
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                var buf = new byte[4];
                if (newMediaId.HasValue)
                {
                    BE.W32(buf, 0, newMediaId.Value);
                    fs.Position = x.ExecutionInfoOffset;
                    fs.Write(buf, 0, 4);
                }
                if (newTitleId.HasValue)
                {
                    BE.W32(buf, 0, newTitleId.Value);
                    fs.Position = x.ExecutionInfoOffset + 12;
                    fs.Write(buf, 0, 4);
                }
                FixHeaderDigest(fs);
            }
        }

        /// <summary>
        /// Recomputes the XEX2 header digest stored at SecurityInfo+0x164:
        /// SHA1(header[SecurityInfo+8+InfoSize .. HeaderSize] || header[0 .. SecurityInfo+8]).
        /// The formula was confirmed against Microsoft-built XEX files and xextool output.
        /// </summary>
        public static void FixHeaderDigest(Stream fs)
        {
            byte[] h = ReadHeader(fs, out int headerSize, out int sec, out int digestStart);
            using (var sha = SHA1.Create())
            {
                sha.TransformBlock(h, digestStart, headerSize - digestStart, null, 0);
                sha.TransformFinalBlock(h, 0, sec + 8);
                fs.Position = sec + 0x164;
                fs.Write(sha.Hash, 0, 20);
            }
        }

        /// <summary>True when the stored header digest matches the header (false = the header was edited without fixing it).</summary>
        public static bool HeaderDigestValid(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] h = ReadHeader(fs, out int headerSize, out int sec, out int digestStart);
                using (var sha = SHA1.Create())
                {
                    sha.TransformBlock(h, digestStart, headerSize - digestStart, null, 0);
                    sha.TransformFinalBlock(h, 0, sec + 8);
                    for (int i = 0; i < 20; i++) if (sha.Hash[i] != h[sec + 0x164 + i]) return false;
                    return true;
                }
            }
        }

        private static byte[] ReadHeader(Stream fs, out int headerSize, out int sec, out int digestStart)
        {
            var first = new byte[0x18];
            fs.Position = 0;
            if (fs.Read(first, 0, 0x18) != 0x18 || Encoding.ASCII.GetString(first, 0, 4) != "XEX2")
                throw new InvalidDataException("Not an XEX2 file.");
            headerSize = (int)BE.U32(first, 0x08);
            sec = (int)BE.U32(first, 0x10);
            if (headerSize <= 0 || headerSize > 0x1000000 || sec < 0x18 || sec + 0x184 > headerSize || headerSize > fs.Length)
                throw new InvalidDataException("The XEX header is malformed.");
            var h = new byte[headerSize];
            fs.Position = 0;
            int n = 0;
            while (n < headerSize) { int r = fs.Read(h, n, headerSize - n); if (r <= 0) throw new EndOfStreamException(); n += r; }
            int infoSize = (int)BE.U32(h, sec + 0x108);
            digestStart = sec + 8 + infoSize;          // = SecurityInfo + 0x17C for the standard 0x174 info size
            if (infoSize < 0x74 || digestStart > headerSize) throw new InvalidDataException("The XEX security info is malformed.");
            return h;
        }
    }
}
