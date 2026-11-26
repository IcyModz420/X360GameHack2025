using System;
using System.IO;
using System.Text;

namespace X360GameHack.Core.Formats
{
    public enum WorkingFileKind
    {
        None,
        Xex,        // Xbox 360 executable (XEX2)
        Package,    // STFS CON/LIVE/PIRS (GOD, XBLA, DLC, TU, ...)
        Save,       // STFS with content type "Saved Game"
        Profile,    // STFS with content type "Profile" (0x10000)
        Iso,        // Xbox / Xbox 360 disc image
        Xbe,        // Original Xbox executable
        Pe,         // Windows PE (xextool can still handle some of these)
        Unknown,
        Gpd,        // XDBF database: a game's achievements, a dashboard or an avatar award file
        Nand,       // Xbox 360 flash (NAND) dump: nanddump.bin, updflash.bin ...
        DriveImage  // an Xbox / Xbox 360 hard drive image (whole drive or one partition) - Drive Explorer opens it
    }

    /// <summary>Identifies files by their content rather than by extension.</summary>
    public static class FileSniffer
    {
        public static WorkingFileKind Detect(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return WorkingFileKind.None;
            byte[] head;
            try { head = BE.ReadHead(path, 0x400); }
            catch { return WorkingFileKind.Unknown; }

            if (head.Length >= 4)
            {
                string magic = Encoding.ASCII.GetString(head, 0, 4);
                if (magic == "XEX2" || magic == "XEX1" || magic == "XEX%" || magic == "XEX-" || magic == "XEX0")
                    return WorkingFileKind.Xex;
                if (magic == "XBEH") return WorkingFileKind.Xbe;
                if (magic == "XDBF") return WorkingFileKind.Gpd;
                if (magic == "CON " || magic == "LIVE" || magic == "PIRS")
                {
                    try
                    {
                        var hdr = BE.ReadHead(path, 0x348);
                        if (hdr.Length >= 0x348)
                        {
                            uint ct = BE.U32(hdr, 0x344);
                            if (ct == 0x00000001) return WorkingFileKind.Save;
                            if (ct == 0x00010000) return WorkingFileKind.Profile;
                        }
                    }
                    catch { }
                    return WorkingFileKind.Package;
                }
                if (head[0] == 'M' && head[1] == 'Z') return WorkingFileKind.Pe;
                // a flash dump starts with the 1BL's header: FF4F (retail) / 0F4F (devkit), then Microsoft's copyright - or, on
                // hack images that replace that text (RGH3's "RGX Zerpaired ..."), a CB / SB stage where the header points
                if (head.Length >= 0x30 && (head[0] == 0xFF || head[0] == 0x0F) && head[1] == 0x4F &&
                    (Encoding.ASCII.GetString(head, 0x1C, 9) == "Microsoft" || PointsToBootStage(path, BE.U32(head, 8))))
                    return WorkingFileKind.Nand;
            }

            if (IsDriveImage(path, head)) return WorkingFileKind.DriveImage;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (IsXdvdfs(path) || ext == ".iso" || ext == ".xiso") return WorkingFileKind.Iso;
            if (ext == ".xex") return WorkingFileKind.Xex;
            if (ext == ".xbe") return WorkingFileKind.Xbe;
            return WorkingFileKind.Unknown;
        }

        /// <summary>
        /// The flash header's CB offset leads to a "CB" / "SB" stage header - in the dump as it is (no spare bytes) or with
        /// 16 spare bytes after every 512-byte page.
        /// </summary>
        private static bool PointsToBootStage(string path, uint cbOffset)
        {
            if (cbOffset < 0x80 || cbOffset > 0x100000 || (cbOffset & 0xF) != 0) return false;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    foreach (long at in new[] { (long)cbOffset, cbOffset / 0x200 * 0x210 + cbOffset % 0x200 })
                    {
                        if (at + 4 > fs.Length) continue;
                        fs.Position = at;
                        int a = fs.ReadByte(), b = fs.ReadByte();
                        if ((a == 'C' || a == 'S') && b == 'B') return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>True when the file contains an XDVDFS volume (XISO, XGD1, XGD2 or XGD3 layout).</summary>
        public static bool IsXdvdfs(string path) => XisoImage.Probe(path) != null;

        /// <summary>
        /// An Xbox hard drive image: one partition ("FATX" / "XTAF" at its start - also a memory unit), an original Xbox
        /// partition table at sector 0, or a file system header where either console keeps one (0x80000, the Xbox 360's
        /// SysExt and Data, the original Xbox's E:). Reads 4 bytes at each place.
        /// </summary>
        public static bool IsDriveImage(string path, byte[] head)
        {
            if (head != null && head.Length >= 16 && Encoding.ASCII.GetString(head, 0, 16) == "****PARTINFO****") return true;
            if (head != null && head.Length >= 4)
            {
                string m0 = Encoding.ASCII.GetString(head, 0, 4);
                if (m0 == "FATX" || m0 == "XTAF") return true;
            }
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var m = new byte[4];
                    foreach (long off in new long[] { 0x80000, 0x10C080000, 0x130EB0000, 0xABE80000 })
                    {
                        if (off + 4 > fs.Length) continue;
                        fs.Position = off;
                        if (fs.Read(m, 0, 4) != 4) continue;
                        string s = Encoding.ASCII.GetString(m);
                        if (s == "FATX" || s == "XTAF") return true;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }

        public static string Describe(WorkingFileKind k)
        {
            switch (k)
            {
                case WorkingFileKind.Xex: return "Xbox 360 Executable (XEX)";
                case WorkingFileKind.Package: return "Xbox 360 Package (STFS)";
                case WorkingFileKind.Save: return "Xbox 360 Save Game";
                case WorkingFileKind.Profile: return "Xbox 360 Profile";
                case WorkingFileKind.Iso: return "Xbox / Xbox 360 Disc Image";
                case WorkingFileKind.Xbe: return "Original Xbox Executable (XBE)";
                case WorkingFileKind.Pe: return "Windows / PE Executable";
                case WorkingFileKind.Gpd: return "Xbox 360 GPD Database";
                case WorkingFileKind.Nand: return "Xbox 360 Flash (NAND) Dump";
                case WorkingFileKind.DriveImage: return "Xbox / Xbox 360 Drive Image";
                case WorkingFileKind.Unknown: return "Unknown file type";
                default: return "No file loaded";
            }
        }

        public static string FormatSize(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return i == 0 ? bytes + " B" : v.ToString("0.##") + " " + u[i];
        }
    }
}
