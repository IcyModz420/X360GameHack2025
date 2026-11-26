using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using X360GameHack.Core.Formats;

namespace X360GameHack.Core.Services
{
    /// <summary>A game Xenia can start: a game folder (its XEX), a game package or a disc image.</summary>
    public sealed class XeniaGame
    {
        /// <summary>The game's own title once read from its XEX or package; until then the folder's (or file's) name.</summary>
        public string Name = "";
        /// <summary>The folder it is in (Open folder; Remove deletes it for a game in the Xenia games folder).</summary>
        public string Folder = "";
        /// <summary>What Xenia starts: default.xex, the package file or the disc image.</summary>
        public string LaunchPath = "";
        public uint TitleId;
        public string Kind = "";
        /// <summary>Added by hand from anywhere on this PC: Remove only takes it off the list.</summary>
        public bool Added;
        /// <summary>Bytes on disk, -1 until counted.</summary>
        public long Size = -1;

        public string TitleIdHex => TitleId == 0 ? "" : TitleId.ToString("X8");
    }

    /// <summary>
    /// The games on the Xenia page: each game Play in Xenia copied into the Xenia games folder (one folder per game) and
    /// any game added by hand. Only reads; Xenia starts <see cref="XeniaGame.LaunchPath"/>.
    /// </summary>
    public static class XeniaLibrary
    {
        public const string FolderKind = "Game folder", PackageKind = "Package", ImageKind = "Disc image", XexKind = "XEX";

        /// <summary>Content types a game package can have (the ones Xenia starts from the package file).</summary>
        private static readonly HashSet<uint> GameTypes = new HashSet<uint> { 0x00004000, 0x00005000, 0x00007000, 0x00080000, 0x000D0000, 0x02000000 };

        /// <summary>Every game in <paramref name="gamesDir"/> (each sub-folder holding one) and the added ones that still exist.</summary>
        public static List<XeniaGame> Scan(string gamesDir, IEnumerable<string> added)
        {
            var list = new List<XeniaGame>();
            if (!string.IsNullOrWhiteSpace(gamesDir) && Directory.Exists(gamesDir))
                foreach (string dir in Directory.GetDirectories(gamesDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var g = FromFolder(dir);
                    if (g != null) list.Add(g);
                }
            foreach (string path in added ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path) || list.Any(x => Same(x.LaunchPath, path))) continue;
                var g = FromFile(path, true);
                if (g != null) list.Add(g);
            }
            return list;
        }

        /// <summary>The game a folder holds (default.xex, else its only XEX, else a game package, else a disc image), or null.</summary>
        public static XeniaGame FromFolder(string dir)
        {
            try
            {
                string xex = Path.Combine(dir, "default.xex");
                if (!File.Exists(xex))
                {
                    var all = Directory.GetFiles(dir, "*.xex");
                    xex = all.Length == 1 ? all[0] : null;
                }
                if (xex != null) return Describe(xex, dir, FolderKind, Path.GetFileName(dir), false);
                foreach (string f in Directory.GetFiles(dir))
                {
                    var pkg = GamePackage(f);
                    if (pkg != null) return Describe(f, dir, PackageKind, PackageName(pkg, Path.GetFileName(dir)), false, pkg.TitleId);
                }
                var iso = Directory.GetFiles(dir, "*.iso").FirstOrDefault();
                if (iso != null) return Describe(iso, dir, ImageKind, Path.GetFileName(dir), false);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        /// <summary>A game file picked by hand (a XEX, a game package or a disc image), or null when it is none of those.</summary>
        public static XeniaGame FromFile(string path, bool added)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".xex")
                    return Describe(path, dir, XexKind, Path.GetFileName(path).Equals("default.xex", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(dir) : Path.GetFileNameWithoutExtension(path), added);
                if (ext == ".iso") return Describe(path, dir, ImageKind, Path.GetFileNameWithoutExtension(path), added);
                var pkg = GamePackage(path);
                if (pkg != null) return Describe(path, dir, PackageKind, PackageName(pkg, Path.GetFileName(path)), added, pkg.TitleId);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        private static StfsPackage GamePackage(string file)
        {
            try
            {
                var pkg = StfsPackage.Read(file);
                return pkg.Valid && GameTypes.Contains(pkg.ContentType) ? pkg : null;
            }
            catch (Exception) { return null; }
        }

        private static string PackageName(StfsPackage pkg, string fallback) =>
            !string.IsNullOrWhiteSpace(pkg.TitleName) ? pkg.TitleName.Trim() : !string.IsNullOrWhiteSpace(pkg.DisplayName) ? pkg.DisplayName.Trim() : fallback;

        private static XeniaGame Describe(string launch, string dir, string kind, string name, bool added, uint titleId = 0)
        {
            var g = new XeniaGame { LaunchPath = Path.GetFullPath(launch), Folder = dir, Kind = kind, Name = name ?? "", Added = added, TitleId = titleId };
            if (g.TitleId == 0 && launch.EndsWith(".xex", StringComparison.OrdinalIgnoreCase))
            {
                try { g.TitleId = XexFile.Read(launch).TitleId; } catch (Exception) { }
            }
            return g;
        }

        /// <summary>The game's own title from its XEX (its title data) or package header; null when it has none. Reads the whole XEX.</summary>
        public static string OwnTitle(XeniaGame g)
        {
            try
            {
                if (g.LaunchPath.EndsWith(".xex", StringComparison.OrdinalIgnoreCase))
                {
                    string name = XexTitleData.FromXex(g.LaunchPath)?.Name;
                    return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>The bytes a game takes on disk: its whole folder for a game in the Xenia games folder, else the file (with a package's .data folder).</summary>
        public static long SizeOf(XeniaGame g)
        {
            try
            {
                if (g.Kind == FolderKind) return FolderBytes(g.Folder);
                long n = new FileInfo(g.LaunchPath).Length;
                if (g.Kind == PackageKind && Directory.Exists(g.LaunchPath + ".data")) n += FolderBytes(g.LaunchPath + ".data");
                return n;
            }
            catch (Exception) { return -1; }
        }

        private static long FolderBytes(string dir)
        {
            long n = 0;
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)) n += f.Length;
            return n;
        }

        /// <summary>How many saves a profile has for a game in Xenia (0 when either is unknown).</summary>
        public static int SaveCount(string xeniaExe, string profileId, uint titleId)
        {
            if (titleId == 0 || string.IsNullOrEmpty(profileId)) return 0;
            try
            {
                string dir = Path.Combine(XeniaContent.Root(xeniaExe), profileId.ToUpperInvariant(), titleId.ToString("X8"), XeniaContent.SaveType.ToString("X8"));
                return Directory.Exists(dir) ? Directory.GetFileSystemEntries(dir).Count(e => Path.GetFileName(e).IndexOf(".x360gamehack-", StringComparison.Ordinal) < 0) : 0;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>"2 DLC, 1 update" - the downloadable content and title updates Xenia has for a game ("" when none).</summary>
        public static string ExtrasOf(string xeniaExe, uint titleId)
        {
            if (titleId == 0) return "";
            try
            {
                string game = Path.Combine(XeniaContent.Root(xeniaExe), XeniaContent.Everyone, titleId.ToString("X8"));
                // each one a folder in Xenia's own form (a package file left there by an older version counts too, until it is put in order)
                int Count(uint type)
                {
                    string d = Path.Combine(game, type.ToString("X8"));
                    return Directory.Exists(d) ? Directory.GetFileSystemEntries(d).Count(e => Path.GetFileName(e).IndexOf(".x360gamehack-", StringComparison.Ordinal) < 0) : 0;
                }
                int dlc = Count(XeniaContent.DlcType), updates = Count(XeniaContent.UpdateType);
                var parts = new List<string>();
                if (dlc > 0) parts.Add(dlc + " DLC");
                if (updates > 0) parts.Add(updates + (updates == 1 ? " update" : " updates"));
                return string.Join(", ", parts);
            }
            catch (Exception) { return ""; }
        }

        /// <summary>When a game was last started in Xenia from this program, or null.</summary>
        public static DateTime? LastPlayed(string launchPath)
        {
            var map = AppSettings.Current.XeniaLastPlayed;
            if (map == null || string.IsNullOrEmpty(launchPath)) return null;
            foreach (var kv in map) if (Same(kv.Key, launchPath)) return kv.Value;
            return null;
        }

        /// <summary>Remembers that a game was just started (only from the window, where the settings are loaded).</summary>
        public static void MarkPlayed(string launchPath)
        {
            if (string.IsNullOrEmpty(launchPath)) return;
            var s = AppSettings.Current;
            if (s.XeniaLastPlayed == null) s.XeniaLastPlayed = new Dictionary<string, DateTime>();
            foreach (var key in s.XeniaLastPlayed.Keys.Where(k => Same(k, launchPath)).ToList()) s.XeniaLastPlayed.Remove(key);
            s.XeniaLastPlayed[Path.GetFullPath(launchPath)] = DateTime.Now;
            AppSettings.Save();
        }

        public static bool Same(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }
    }
}
