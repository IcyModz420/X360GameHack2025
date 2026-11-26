using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace X360GameHack
{
    /// <summary>How this copy compares with the version number published for everyone.</summary>
    public enum VersionState
    {
        /// <summary>The published number could not be read (offline, or the notice did not check out).</summary>
        Unknown,
        /// <summary>Exactly the published version.</summary>
        Latest,
        /// <summary>Lower than the published version: not the latest.</summary>
        Outdated,
        /// <summary>Higher than the published version: a preview that has not been released yet. Never called "latest".</summary>
        Preview
    }

    public sealed class UpdateInfo
    {
        public bool Online;
        public string Latest = "";
        public VersionState State = VersionState.Unknown;
        /// <summary>True only when a released version newer than this copy exists.</summary>
        public bool UpdateAvailable => State == VersionState.Outdated;
        /// <summary>How many times X360GameHack has been opened ("2,782"), or "" when it could not be read.</summary>
        public string Opens = "";
    }

    /// <summary>
    /// This copy's version and the program's public addresses, and how a published version compares with this copy.
    /// (The name is historical: the version and the open count used to come from a Pastebin page. Since 2026-11-19 both
    /// come from the program's own Cloudflare address - the version inside the signed update notice and the count from
    /// its open counter, which started at the Pastebin page's 2,781; see Core\Services\AppUpdater.cs.)
    /// </summary>
    public static class Pastebin
    {
        public const string Version = "v4.0.0";
        public const string ReleasesUrl = "https://github.com/IcyModz420/X360GameHack2025/releases";
        public const string IssuesUrl = "https://github.com/IcyModz420/X360GameHack2025/issues";
        public const string YouTubeUrl = "https://www.youtube.com/@IcyModz420";

        /// <summary>Decides the state from the published version text and this copy's version.</summary>
        public static UpdateInfo Evaluate(string published, string mine)
        {
            published = (published ?? "").Trim();
            // anything that is not a plain version string is ignored
            bool valid = published.Length <= 24 && Regex.IsMatch(published, @"^[vV]?\d{1,4}(\.\d{1,6}){1,3}$");
            var info = new UpdateInfo { Online = valid, Latest = valid ? published : "" };
            if (!valid) return info;
            int c = Compare(mine, published);
            info.State = c == 0 ? VersionState.Latest : c > 0 ? VersionState.Preview : VersionState.Outdated;
            return info;
        }

        private static bool TryParse(string text, out Version version)
        {
            version = null;
            string t = (text ?? "").Trim().TrimStart('v', 'V');
            var parts = t.Split('.');
            if (parts.Length < 1 || parts.Length > 4) return false;
            var numbers = new int[4];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
            version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);     // "4.0" and "4.0.0" are the same version
            return true;
        }

        /// <summary>
        /// Compares "v1.2.3" style versions: 0 when they are the same version, above 0 when a is higher, below 0 when a
        /// is lower. Text that is not a version counts as different-and-lower only if the two texts differ.
        /// </summary>
        public static int Compare(string a, string b)
        {
            if (TryParse(a, out Version va) && TryParse(b, out Version vb)) return va.CompareTo(vb);
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase) ? 0 : -1;
        }
    }
}
