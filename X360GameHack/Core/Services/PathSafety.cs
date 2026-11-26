using System;
using System.IO;
using System.Linq;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// Protects every extractor and downloader (XISO, FATX, FTP, XBDM) against path-traversal ("zip-slip")
    /// and Windows device-name tricks in names that come from untrusted images, consoles or servers.
    /// </summary>
    public static class PathSafety
    {
        private static readonly string[] Reserved =
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>Turns an untrusted name into a single safe file-name component.</summary>
        public static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_";
            var chars = name.ToCharArray();
            var invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] < 32 || invalid.Contains(chars[i])) chars[i] = '_';
            string s = new string(chars).TrimEnd(' ', '.');      // Windows silently strips these
            if (s.Length == 0 || s == "." || s == "..") s = "_" + s.Replace('.', '_');
            string stem = s.Split('.')[0];
            if (Reserved.Contains(stem, StringComparer.OrdinalIgnoreCase)) s = "_" + s;
            return s.Length > 240 ? s.Substring(0, 240) : s;
        }

        /// <summary>
        /// Combines a trusted root with an untrusted relative path (any separator) and guarantees the
        /// result stays inside the root. Each component is sanitised with <see cref="SafeName"/>.
        /// </summary>
        public static string CombineUnder(string root, string untrustedRelative)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            string combined = fullRoot;
            foreach (var part in (untrustedRelative ?? "").Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
                combined = Path.Combine(combined, SafeName(part));
            string full = Path.GetFullPath(combined);
            if (!(full + "\\").StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) && !full.Equals(fullRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Blocked a path that would escape the destination folder: " + untrustedRelative);
            return full;
        }
    }
}
