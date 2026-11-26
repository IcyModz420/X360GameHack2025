using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// Every start of Xenia goes through here, so the same three things are always taken care of:
    ///  1. Where Xenia keeps its settings, saves, cache and patches. Xenia Canary uses its own folder, and when that
    ///     folder cannot be written - an install in Program Files - it closes at once (0xC0000409). It is then given
    ///     %LocalAppData%\X360GameHack\Xenia instead (--storage_root, measured to work with the bundled build).
    ///  2. Every kind of controller: Xenia's input setting is kept on "any" (its XInput, SDL and keyboard drivers), and
    ///     the SDL controller list (gamecontrollerdb.txt, shipped next to xenia_canary.exe) is picked up because Xenia is
    ///     started in its own folder. SDL is what makes PlayStation 4 / 5 and Switch Pro controllers work, over USB or
    ///     Bluetooth, without DS4Windows.
    ///  3. The same storage folder is used by the patch installer, so Xenia finds installed patches.
    /// </summary>
    public static class XeniaHost
    {
        public const string ControllerList = "gamecontrollerdb.txt";

        /// <summary>The folder used when Xenia's own folder cannot be written.</summary>
        public static string OwnStorage =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "X360GameHack", "Xenia");

        /// <summary>True when Xenia's folder cannot be written, so it has to be given another storage folder.</summary>
        public static bool NeedsOwnStorage(string xeniaExe)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(xeniaExe));
            return !CanWrite(dir);
        }

        /// <summary>Where this Xenia keeps its settings and content when started by the program.</summary>
        public static string StorageRoot(string xeniaExe) =>
            NeedsOwnStorage(xeniaExe) ? OwnStorage : Path.GetDirectoryName(Path.GetFullPath(xeniaExe));

        /// <summary>Xenia's settings file for this Xenia (it may not exist before Xenia's first start).</summary>
        public static string ConfigFile(string xeniaExe) => Path.Combine(StorageRoot(xeniaExe), "xenia-canary.config.toml");

        /// <summary>The command line for running <paramref name="target"/> (a XEX, disc image or package) in Xenia.</summary>
        public static string Arguments(string xeniaExe, string target)
        {
            string game = string.IsNullOrEmpty(target) ? "" : ToolRunner.Q(target);
            if (!NeedsOwnStorage(xeniaExe)) return game;
            Directory.CreateDirectory(OwnStorage);
            return ("--storage_root=" + ToolRunner.Q(OwnStorage) + " " + game).Trim();
        }

        /// <summary>
        /// Set by the window: asked before a game starts in Xenia, with the Xenia that will run it - the profile Xenia signs
        /// in to (see UI\XeniaSignIn.cs). False = do not start. Null without a window (tests, command line).
        /// </summary>
        public static Func<string, bool> BeforeStart;

        /// <summary>Starts <paramref name="target"/> in the Xenia chosen in Settings (or the bundled one). False when it could not be started.</summary>
        public static bool Start(string target)
        {
            string xenia = Tools.Xenia;
            if (string.IsNullOrEmpty(xenia) || !File.Exists(xenia))
            {
                Log.Error("Xenia was not found. Set its path on the Xenia page (Setup tab).");
                return false;
            }
            if (AppSettings.Current.XeniaAllControllers) KeepEveryController(xenia);
            // what older versions of this program put into Xenia, in the form this Xenia reads (see XeniaContent.Repair)
            if (!IsRunning(xenia))
                try { foreach (string done in XeniaContent.Repair(xenia)) Log.Info("Xenia: " + done + "."); }
                catch (Exception ex) { Log.Warn("Could not put Xenia's saves and downloads in order: " + ex.Message); }
            if (NeedsOwnStorage(xenia)) Log.Info("Xenia's folder cannot be written (" + Path.GetDirectoryName(xenia) + "), so it keeps its settings and saves in " + OwnStorage + ".");
            // without its content folder a game's storage check fails (Xenia's log: "GetContentFreeSpace: The system cannot
            // find the path specified") and it finds no place for its saves
            try { Directory.CreateDirectory(Path.Combine(StorageRoot(xenia), "content")); }
            catch (Exception ex) { Log.Warn("Could not make Xenia's content folder: " + ex.Message); }
            if (!string.IsNullOrEmpty(target) && BeforeStart != null && !BeforeStart(xenia)) { Log.Info("Xenia was not started."); return false; }
            return Tools.Launch(xenia, Arguments(xenia, target));
        }

        private static readonly Regex HidLine = new Regex(@"^(\s*hid\s*=\s*"")([^""]*)("".*)$", RegexOptions.Compiled);

        /// <summary>
        /// When Xenia's input setting was changed to one that leaves controllers out (xinput only, keyboard only or
        /// none), puts it back to "any". Returns the old value when it was changed, else null. Nothing else in the file
        /// is touched; a missing file (Xenia not started yet) needs nothing, Xenia's default is already "any".
        /// </summary>
        public static string KeepEveryController(string xeniaExe)
        {
            string file = ConfigFile(xeniaExe);
            try
            {
                if (!File.Exists(file)) return null;
                string[] lines = File.ReadAllLines(file, Encoding.UTF8);
                bool inHid = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].Trim();
                    if (t.StartsWith("[")) { inHid = t.Equals("[HID]", StringComparison.OrdinalIgnoreCase); continue; }
                    if (!inHid) continue;
                    var m = HidLine.Match(lines[i]);
                    if (!m.Success) continue;
                    string value = m.Groups[2].Value.Trim().ToLowerInvariant();
                    if (value == "any" || value == "sdl") return null;        // SDL is on: every controller works
                    lines[i] = m.Groups[1].Value + "any" + m.Groups[3].Value;
                    File.WriteAllLines(file, lines, new UTF8Encoding(false));
                    Log.Info("Xenia's controller setting was \"" + value + "\", which leaves controllers out - set back to \"any\" (every controller). Untick \"Every controller\" on the Xenia page (Setup tab) to keep your own setting.");
                    return value;
                }
            }
            catch (Exception ex) { Log.Warn("Could not check Xenia's controller setting: " + ex.Message); }
            return null;
        }

        /// <summary>True while a program with this Xenia's name runs.</summary>
        public static bool IsRunning(string xeniaExe)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(xeniaExe ?? "");
                if (name.Length == 0) return false;
                var ps = System.Diagnostics.Process.GetProcessesByName(name);
                foreach (var p in ps) p.Dispose();
                return ps.Length > 0;
            }
            catch (Exception) { return false; }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                string probe = Path.Combine(dir, ".x360gamehack-write-check-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch { return false; }
        }
    }
}
