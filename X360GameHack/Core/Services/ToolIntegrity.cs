using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// Integrity allowlist for the third-party programs X360GameHack starts. Before a bundled tool runs, its
    /// SHA-256 must match the value recorded here; the file is held open (no write / delete / rename sharing)
    /// from the check until the process has started, so it cannot be swapped in between (no TOCTOU window).
    /// Regenerate this table with Other\UpdateToolHashes.ps1 when you ship a new tool version.
    /// </summary>
    public static class ToolIntegrity
    {
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "xextool.exe", "D93C1B814AD6FF124834F4235BF8AAC9F09DBA8D69C335EBECC8D6EFE8D5A062" },
            { "X360PkgTool.exe", "FDCA59C894F2A301A9B7DBAA53C68726688B432157F80727DEAED5A6653C4395" },
            { "XbeTool.exe", "51149DDBBF5D9AC9228F97A57A971E950F22FA5FDBBF134C36544C4B9FBAD8A0" },
            { "ISO2God.exe", "82F911F812E14E4CB40E890B998E22C1990D5424A70BCDB8B11A46791EA90410" },
            { "JRunner.exe", "639BCF3EC449C19C833AACA0E79B66236F753BB1466839152E42B0377E7AF326" },
            { "JungleFlasher.exe", "02F45C181763D418317663909732EF056C40AE2CB6850FA226ADCB6FD3EBAE2C" },
            { "XBBC.exe", "4898C26CE9864B72244C63ECDCF4DAAF9DE1466C88BAB836E9D0E910F60CCC9E" },
            { "abgx360_v1.0.7_setup_hadzz.exe", "3AAAB159969C60434F6B65CDB7C5DFDF66F45D7EB3C3F9EF03701F77FC75F7BE" },
            { "xenia_canary.exe", "8946563CAE2DA4D8F08F7335E6E2627A42EE670231F2F8B8E917FABDCC914675" },   // Xenia Canary release 02d2cb5 (2026-03-24)
        };

        /// <summary>Checks every bundled tool now: (file name, "Verified" / "Missing" / "MODIFIED").</summary>
        public static List<(string Name, string State)> CheckAll()
        {
            var r = new List<(string, string)>();
            foreach (var kv in Known)
            {
                string path = Path.Combine(Tools.AppDir, kv.Key);
                if (!File.Exists(path)) { r.Add((kv.Key, "Missing")); continue; }
                try
                {
                    string actual;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var sha = SHA256.Create()) actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
                    r.Add((kv.Key, string.Equals(actual, kv.Value, StringComparison.OrdinalIgnoreCase) ? "Verified" : "MODIFIED"));
                }
                catch (Exception ex) { r.Add((kv.Key, "Unreadable: " + ex.Message)); }
            }
            return r;
        }

        /// <summary>True for executables X360GameHack ships (and therefore must verify).</summary>
        public static bool IsBundled(string exePath)
        {
            return Known.ContainsKey(Path.GetFileName(exePath ?? "")) &&
                   string.Equals(Path.GetDirectoryName(Path.GetFullPath(exePath)).TrimEnd('\\'), Tools.AppDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Opens the file with read-only sharing and verifies its hash. Returns the open handle (dispose it after
        /// the process has started) or null when verification fails (the reason is logged).
        /// External programs the user picked themselves (Xenia, xemu, installed abgx360) are not bundled,
        /// so they are only locked, not hash-checked.
        /// </summary>
        public static FileStream LockAndVerify(string exePath)
        {
            FileStream fs;
            try
            {
                fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception ex)
            {
                Log.Error("Cannot open " + Path.GetFileName(exePath) + ": " + ex.Message);
                return null;
            }
            if (!Known.TryGetValue(Path.GetFileName(exePath), out string expected) || !IsBundled(exePath)) return fs;
            string actual;
            using (var sha = SHA256.Create()) actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
            fs.Position = 0;
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return fs;
            fs.Dispose();
            Log.Error("SECURITY: " + Path.GetFileName(exePath) + " failed its integrity check (the file was modified or replaced). " +
                      "It was NOT started. Reinstall X360GameHack to restore the original.");
            return null;
        }
    }
}
