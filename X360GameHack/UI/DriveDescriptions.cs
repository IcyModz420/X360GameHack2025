using System;
using System.Globalization;
using System.Linq;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// The "What it is" column of Drive Explorer: names for what an Xbox drive holds under its hexadecimal names - a game's
    /// title folder, the kind of content a folder keeps, a profile's folder, what a package says it is. Only from what is
    /// on the drive and the names this PC already knows (nothing is looked up online).
    /// </summary>
    public static class DriveDescriptions
    {
        private static bool IsHex(string s, int length) =>
            s != null && s.Length == length && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

        /// <summary>A folder's description from its name and the name of the folder it is in ("" = the root).</summary>
        public static string Folder(string name, string parent)
        {
            if (string.Equals(name, "Content", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(parent)) return "Games, DLC, updates, saves and profiles";
            if (name == "0000000000000000") return "Shared by every profile on the console";
            if (IsHex(name, 16) && string.Equals(parent, "Content", StringComparison.OrdinalIgnoreCase)) return "A profile's own content (saves, its own downloads)";
            if (string.Equals(name, "Cache", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(parent)) return "The console's cache (title updates it found online, and more)";
            if (string.Equals(name, "Compatibility", StringComparison.OrdinalIgnoreCase)) return "The original Xbox emulator (backward compatibility)";
            if (string.Equals(name, "TDATA", StringComparison.OrdinalIgnoreCase)) return "Game data the original Xbox's games keep (per game)";
            if (string.Equals(name, "UDATA", StringComparison.OrdinalIgnoreCase)) return "Saved games of the original Xbox's games (per game)";
            if (!IsHex(name, 8)) return "";
            uint id = uint.Parse(name, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            // inside a title folder: the kind of content
            if (IsHex(parent, 8))
            {
                string kind = StfsPackage.TypeName(id);
                if (!kind.StartsWith("Unknown")) return Plural(kind);
            }
            if (id == 0xFFFE07D1) return "Xbox 360 dashboard (profiles live here)";
            if (id == 0xFFFE07DF) return "Avatar Editor";
            if (id == 0xFFFE0700) return "Xbox Live Arcade (the console's own)";
            string game = TitleLookup.Cached(id);
            return string.IsNullOrEmpty(game) ? "" : game;
        }

        /// <summary>A package's kind in a word or two, for the list (Properties shows the full name).</summary>
        public static string ShortKind(uint contentType)
        {
            switch (contentType)
            {
                case 0x00000001: return "Save";
                case 0x00000002: return "DLC";
                case 0x00004000: return "Installed game";
                case 0x00005000: return "Original Xbox game";
                case 0x00007000: return "Games on Demand";
                case 0x00009000: return "Avatar item";
                case 0x00010000: return "Profile";
                case 0x00020000: return "Gamer picture";
                case 0x00030000: return "Theme";
                case 0x00040000: return "Cache";
                case 0x00080000: return "Demo";
                case 0x00090000: return "Video";
                case 0x000B0000: return "Title update";
                case 0x000D0000: return "Arcade game";
                default: return StfsPackage.TypeName(contentType);
            }
        }

        /// <summary>"Title Update" -> "Title Updates"; names that are not counted stay as they are.</summary>
        private static string Plural(string kind)
        {
            if (kind.EndsWith(")") || kind.EndsWith("s") || kind == "Games on Demand" || kind == "TV" || kind == "XNA" || kind == "License Store" ||
                kind == "Publisher" || kind == "Storage Download" || kind == "Xbox Download" || kind == "IPTV Pause Buffer") return kind;
            return kind + "s";
        }

        /// <summary>
        /// What a file is, from the start of it (<paramref name="head"/>, at least 0x1712 bytes for a package): a package says
        /// its own name, kind and game. "" when it says nothing.
        /// </summary>
        public static string File(string name, byte[] head)
        {
            if (head != null && head.Length >= 4)
            {
                string magic = System.Text.Encoding.ASCII.GetString(head, 0, 4);
                if (magic == "CON " || magic == "LIVE" || magic == "PIRS")
                {
                    var h = XeniaContent.ReadHeader(head);
                    if (h != null)
                    {
                        string kind = StfsPackage.TypeName(h.ContentType);
                        string title = h.TitleName.Length > 0 ? h.TitleName : TitleLookup.Cached(h.TitleId) ?? "";
                        string what = h.DisplayName.Length > 0 ? h.DisplayName : kind;
                        // the game, unless the package's own name already starts with it ("Call of Duty: WaW - Map Pack 2" for
                        // Call of Duty: World at War - the part before a colon is enough)
                        int colon = title.IndexOf(':');
                        string lead = colon > 3 ? title.Substring(0, colon) : title;
                        if (title.Length > 0 && !what.StartsWith(lead, StringComparison.OrdinalIgnoreCase)) what += " - " + title;
                        return what + (h.DisplayName.Length > 0 && !kind.StartsWith("Unknown") ? "   [" + ShortKind(h.ContentType) + "]" : "");
                    }
                }
                if (magic == "XEX2") return "Xbox 360 program";
                if (magic == "XBEH") { string title = XbeTitle(head); return title.Length > 0 ? title + "   [Original Xbox program]" : "Original Xbox program"; }
                if (magic == "XDBF") return "Achievements and settings (GPD)";
            }
            string ext = System.IO.Path.GetExtension(name ?? "").ToLowerInvariant();
            if (ext == ".xex") return "Xbox 360 program";
            if (ext == ".xbe") return "Original Xbox program";
            if (ext == ".gpd") return "Achievements and settings (GPD)";
            return "";
        }

        /// <summary>
        /// A value from an original Xbox meta file (TitleMeta.xbx "TitleName=...", SaveMeta.xbx "Name=..."): UTF-16 little-endian
        /// text lines "Key=Value", with or without a byte order mark. "" when it is not there.
        /// </summary>
        public static string XbxValue(byte[] b, string key)
        {
            if (b == null || b.Length < 4) return "";
            int start = b[0] == 0xFF && b[1] == 0xFE ? 2 : 0;
            string text = System.Text.Encoding.Unicode.GetString(b, start, (b.Length - start) & ~1);
            foreach (string line in text.Split('\r', '\n'))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return new string(line.Substring(eq + 1).Where(c => c >= ' ').ToArray()).Trim();
            }
            return "";
        }

        /// <summary>The game's name from the start of its default.xbe (the certificate's title), or "".</summary>
        public static string XbeTitle(byte[] head)
        {
            try { var x = XbeFile.FromHead(head); return x.Valid ? (x.TitleName ?? "").Trim() : ""; }
            catch { return ""; }
        }

        /// <summary>The kinds of file worth reading the start of (a package is at least 0xA000 bytes; programs and GPDs say what they are in their first bytes).</summary>
        public static bool WorthReading(FatxEntry e) =>
            !e.IsDirectory && e.Size >= 4 && (e.Size >= 0xA000 || e.Name.EndsWith(".xex", StringComparison.OrdinalIgnoreCase) ||
                                               e.Name.EndsWith(".xbe", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".gpd", StringComparison.OrdinalIgnoreCase));
    }
}
