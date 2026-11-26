using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// Playing an original Xbox game in xemu: xemu boots disc images in XISO form only (xemu.app/docs/disc-images), so a
    /// game's folder - copied off an original Xbox drive, or already on this PC - gets its XISO beside it (Black -> Black.iso),
    /// made again only when the folder changed, and xemu is started with -dvd_path (xemu.app/docs/cli). xemu's own setup
    /// (boot ROM, BIOS, hard drive image) stays xemu's.
    /// </summary>
    public static class XemuPlay
    {
        /// <summary>Where original Xbox games copied off a drive go: Documents\X360GameHack\Original Xbox Games, unless set.</summary>
        public static string GamesDir
        {
            get
            {
                string set = AppSettings.Current.XemuGamesDir;
                if (!string.IsNullOrWhiteSpace(set)) return set.Trim();
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "X360GameHack", "Original Xbox Games");
            }
        }

        /// <summary>xemu.exe as set (Original Xbox page, or asked for once), or null when it is not set or not there.</summary>
        public static string Exe
        {
            get
            {
                string x = AppSettings.Current.XemuPath;
                return !string.IsNullOrWhiteSpace(x) && File.Exists(x.Trim()) ? x.Trim() : null;
            }
        }

        /// <summary>What a folder holds, file by file (path, size, time) - the image beside it is made again when this changes.</summary>
        internal static string Listing(string folder)
        {
            string root = Path.GetFullPath(folder).TrimEnd('\\');
            return string.Join("\n", Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .Select(f => f.FullName.Substring(root.Length + 1) + "|" + f.Length + "|" + f.LastWriteTimeUtc.Ticks)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The XISO for a game's folder, beside it (folder + ".iso"): the one there when it was made from exactly what the folder
        /// holds now (a hidden list of the folder's files, beside it, says so), else made now. The folder is only read.
        /// </summary>
        public static string IsoFor(string folder, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            string root = Path.GetFullPath(folder).TrimEnd('\\');
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The game's folder is not there: " + root);
            if (!File.Exists(Path.Combine(root, "default.xbe")))
                throw new InvalidOperationException(Path.GetFileName(root) + " has no default.xbe - xemu starts an original Xbox game from its default.xbe.");
            string iso = root + ".iso", list = iso + ".files";
            string now = Listing(root);
            if (File.Exists(iso) && File.Exists(list) && File.ReadAllText(list) == now) return iso;
            string tmp = iso + ".making";
            if (File.Exists(tmp)) File.Delete(tmp);
            try
            {
                XisoBuilder.CreateFromFolder(root, tmp, false, progress, ct);
                if (File.Exists(iso)) File.Delete(iso);
                File.Move(tmp, iso);
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
            if (File.Exists(list)) File.SetAttributes(list, FileAttributes.Normal);
            File.WriteAllText(list, now);
            File.SetAttributes(list, FileAttributes.Hidden);
            return iso;
        }

        /// <summary>For automated UI tests only: receives the program and arguments instead of starting xemu. Null in normal use.</summary>
        internal static Action<string, string> TestStart;

        /// <summary>Starts xemu with the disc image. Null when it started, else why not.</summary>
        public static string Start(string iso)
        {
            string xemu = Exe;
            if (xemu == null) return "Where is xemu? Set xemu.exe on the Original Xbox (OG) page (xemu card), then press Play again.";
            if (!File.Exists(iso)) return "The disc image is not there: " + iso;
            string args = "-dvd_path " + ToolRunner.Q(iso);
            if (TestStart != null) { TestStart(xemu, args); return null; }
            return Tools.Launch(xemu, args) ? null : "xemu could not be started (see the log).";
        }
    }
}
