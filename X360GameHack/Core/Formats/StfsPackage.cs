using System;
using System.Drawing;
using System.IO;
using System.Text;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Formats
{
    /// <summary>Native reader for the STFS (CON / LIVE / PIRS) metadata header.</summary>
    public sealed class StfsPackage
    {
        public string Path { get; private set; }
        public bool Valid { get; private set; }
        public string Signature { get; private set; } = "";
        public uint ContentType { get; private set; }
        public ulong ContentSize { get; private set; }
        public uint MediaId { get; private set; }
        public uint Version { get; private set; }
        public uint BaseVersion { get; private set; }
        public uint TitleId { get; private set; }
        public int DiscNumber { get; private set; }
        public int DiscCount { get; private set; }
        public string ConsoleId { get; private set; } = "";
        public string ProfileId { get; private set; } = "";
        public string DeviceId { get; private set; } = "";
        public string DisplayName { get; private set; } = "";
        public string Description { get; private set; } = "";
        public string Publisher { get; private set; } = "";
        public string TitleName { get; private set; } = "";
        public Image Thumbnail { get; private set; }
        public Image TitleThumbnail { get; private set; }

        public string ContentTypeName => TypeName(ContentType);
        public string TitleIdHex => TitleId.ToString("X8");
        public string ContentTypeHex => ContentType.ToString("X8");

        /// <summary>True for Games-on-Demand / installed packages that carry a sibling ".data" folder.</summary>
        public bool HasDataFolder => Directory.Exists(Path + ".data");

        /// <summary>
        /// Upper-case "TU_..." title updates are cache-format and belong in the Cache folder; lower-case
        /// "tu..." updates go in Content\0000000000000000\{titleid}\000B0000 (ConsoleMods title-update guide).
        /// </summary>
        public bool IsCacheTitleUpdate => System.IO.Path.GetFileName(Path ?? "").StartsWith("TU_", StringComparison.Ordinal);

        /// <summary>True when the header names an owning profile (a non-zero profile ID).</summary>
        private bool HasOwner => !string.IsNullOrEmpty(ProfileId) && ProfileId.Trim('0').Length > 0;

        /// <summary>
        /// Relative install location: Content\{profile}\{titleid}\{type}\ (or Cache for TU_ updates). Content that
        /// belongs to a profile - its saves and the profile itself - lives under that profile's ID; everything else
        /// under 0000000000000000. (As a console lays out a USB drive: Content\{profile}\{title}\00000001 for saves and
        /// Content\{profile}\FFFE07D1\00010000 for the profile.)
        /// </summary>
        public string InstallDir
        {
            get
            {
                if (IsCacheTitleUpdate) return "Cache";
                bool owned = (ContentType == 0x00000001 || ContentType == 0x00010000) && HasOwner;
                return "Content\\" + (owned ? ProfileId : "0000000000000000") + "\\" + TitleIdHex + "\\" + ContentTypeHex;
            }
        }

        /// <summary>The file name a console expects: a profile is named after its profile ID; anything else keeps its name.</summary>
        public string InstallName => ContentType == 0x00010000 && HasOwner ? ProfileId : System.IO.Path.GetFileName(Path ?? "");

        public static StfsPackage Read(string path)
        {
            byte[] h;
            try { h = BE.ReadHead(path, 0x971A); }
            catch (Exception ex) { Log.Warn("STFS header read failed: " + ex.Message); return new StfsPackage { Path = path }; }
            return FromHeader(h, path);
        }

        /// <summary>Parses a header already in memory (e.g. read over XBDM or from a drive image).</summary>
        public static StfsPackage FromHeader(byte[] h, string path)
        {
            var p = new StfsPackage { Path = path };
            try
            {
                if (h == null || h.Length < 0x1712) return p;
                p.Signature = Encoding.ASCII.GetString(h, 0, 4).Trim();
                if (p.Signature != "CON" && p.Signature != "LIVE" && p.Signature != "PIRS") return p;
                p.ContentType = BE.U32(h, 0x344);
                p.ContentSize = BE.U64(h, 0x34C);
                p.MediaId = BE.U32(h, 0x354);
                p.Version = BE.U32(h, 0x358);
                p.BaseVersion = BE.U32(h, 0x35C);
                p.TitleId = BE.U32(h, 0x360);
                p.DiscNumber = h[0x366];
                p.DiscCount = h[0x367];
                p.ConsoleId = BE.Hex(h, 0x36C, 5);
                p.ProfileId = BE.Hex(h, 0x371, 8);
                p.DeviceId = BE.Hex(h, 0x3FD, 0x14);
                // Display name and description: nine languages of 128 UTF-16 characters (0x100 bytes) each, English first.
                // (Publisher and title name below are single 64-character fields.)
                p.DisplayName = BE.Utf16BE(h, 0x411, 0x100);
                p.Description = BE.Utf16BE(h, 0xD11, 0x100);
                p.Publisher = BE.Utf16BE(h, 0x1611, 0x80);
                p.TitleName = BE.Utf16BE(h, 0x1691, 0x80);
                if (h.Length >= 0x171A)
                {
                    uint thumbSize = BE.U32(h, 0x1712);
                    uint titleThumbSize = BE.U32(h, 0x1716);
                    p.Thumbnail = LoadImage(h, 0x171A, thumbSize);
                    p.TitleThumbnail = LoadImage(h, 0x571A, titleThumbSize);
                }
                p.Valid = true;
            }
            catch (Exception ex)
            {
                Log.Warn("STFS header read failed: " + ex.Message);
            }
            return p;
        }

        private static Image LoadImage(byte[] h, int offset, uint size)
        {
            if (size == 0 || size > 0x4000 || offset + size > h.Length) return null;
            try
            {
                using (var ms = new MemoryStream(h, offset, (int)size))
                using (var img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
            catch { return null; }
        }

        public static string TypeName(uint t)
        {
            switch (t)
            {
                case 0x00000001: return "Saved Game";
                case 0x00000002: return "Marketplace Content (DLC)";
                case 0x00000003: return "Publisher";
                case 0x00001000: return "Xbox 360 Title";
                case 0x00002000: return "IPTV Pause Buffer";
                case 0x00004000: return "Installed Game";
                case 0x00005000: return "Original Xbox Game";
                case 0x00007000: return "Games on Demand";
                case 0x00009000: return "Avatar Item";
                case 0x00010000: return "Profile";
                case 0x00020000: return "Gamer Picture";
                case 0x00030000: return "Theme";
                case 0x00040000: return "Cache File";
                case 0x00050000: return "Storage Download";
                case 0x00060000: return "Xbox Saved Game";
                case 0x00070000: return "Xbox Download";
                case 0x00080000: return "Game Demo";
                case 0x00090000: return "Video";
                case 0x000A0000: return "Game Title";
                case 0x000B0000: return "Title Update";
                case 0x000C0000: return "Game Trailer";
                case 0x000D0000: return "Arcade Title (XBLA)";
                case 0x000E0000: return "XNA";
                case 0x000F0000: return "License Store";
                case 0x00100000: return "Movie";
                case 0x00200000: return "TV";
                case 0x00300000: return "Music Video";
                case 0x00400000: return "Game Video";
                case 0x00500000: return "Podcast Video";
                case 0x00600000: return "Viral Video";
                case 0x02000000: return "Community Game";
                default: return "Unknown (0x" + t.ToString("X8") + ")";
            }
        }
    }
}
