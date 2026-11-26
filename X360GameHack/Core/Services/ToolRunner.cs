using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack.Core.Services
{
    public sealed class ToolResult
    {
        public int ExitCode;
        public string Output = "";
        public bool Success => ExitCode == 0;
    }

    /// <summary>Locations of the bundled third-party tools.</summary>
    public static class Tools
    {
        public static string AppDir { get; } = Path.GetDirectoryName(typeof(Tools).Assembly.Location);
        public static string Path_(string exe) => Path.Combine(AppDir, exe);

        public static string XexTool => Path_("xextool.exe");
        public static string PkgTool => Path_("X360PkgTool.exe");
        public static string ExtractXiso => Path_("extract-xiso.exe");
        /// <summary>The Xenia Canary that ships with the program.</summary>
        public static string BundledXenia => Path_("xenia_canary.exe");

        /// <summary>
        /// The Xenia to use: the one chosen in Settings when that file exists, otherwise the bundled one (which is
        /// then also written into the setting, so Settings shows where it is).
        /// </summary>
        public static string Xenia
        {
            get
            {
                var s = AppSettings.Current;
                if (!string.IsNullOrWhiteSpace(s.XeniaPath) && File.Exists(s.XeniaPath)) return s.XeniaPath;
                if (File.Exists(BundledXenia)) { s.XeniaPath = BundledXenia; return BundledXenia; }
                return s.XeniaPath ?? "";
            }
        }
        public static string XbeTool => Path_("XbeTool.exe");
        public static string Iso2God => Path_("ISO2God.exe");
        public static string JRunner => Path_("JRunner.exe");
        public static string JungleFlasher => Path_("JungleFlasher.exe");
        public static string XIB => Path_("XIB.exe");
        public static string XBBC => Path_("XBBC.exe");
        public static string AbgxInstaller => Path_("abgx360_v1.0.7_setup_hadzz.exe");
        public static string XeBuildDir => Path_("xeBuild");
        public static string BaseFileDump => Path_("CurrentXEXBaseFile.exe");

        public static string Abgx360
        {
            get
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string[] candidates =
                {
                    Path.Combine(win, "SysWOW64", "abgx360.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "abgx360.exe"),
                    @"C:\Program Files (x86)\abgx360\abgx360.exe",
                    @"C:\Program Files\abgx360\abgx360.exe"
                };
                return candidates.FirstOrDefault(File.Exists);
            }
        }

        public static string AbgxGui
        {
            get
            {
                string[] candidates =
                {
                    @"C:\Program Files (x86)\abgx360\abgx360gui.exe",
                    @"C:\Program Files\abgx360\abgx360gui.exe"
                };
                return candidates.FirstOrDefault(File.Exists);
            }
        }

        /// <summary>Starts a GUI program (bundled tools are integrity-checked first); returns false when it can't run.</summary>
        public static bool Launch(string exePath, string args = null)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                Log.Error("Not found: " + (exePath ?? "(unknown tool)") + ". Make sure it sits next to X360GameHack.exe.");
                return false;
            }
            using (var guard = ToolIntegrity.LockAndVerify(exePath))
            {
                if (guard == null) return false;
                try
                {
                    Process.Start(new ProcessStartInfo(exePath, args ?? "")
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(exePath)
                    })?.Dispose();
                    Log.Info("Launched " + Path.GetFileName(exePath));
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("Could not launch " + Path.GetFileName(exePath) + ": " + ex.Message);
                    return false;
                }
            }
        }

        private static string WindowsExe(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), name);

        /// <summary>For automated UI tests only: receives the address instead of the browser opening it. Null (never set) in normal use.</summary>
        internal static Action<string> TestOpenUrl;

        public static void OpenUrl(string url)
        {
            // Only http(s) links and existing local documents are opened (never arbitrary commands).
            bool web = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            bool doc = !web && IsSafeDocument(url) && File.Exists(url);
            if (!web && !doc) { Log.Error("Refused to open: " + url); return; }
            if (web && (url.IndexOfAny(new[] { '"', '\r', '\n', '\t' }) >= 0 || !Uri.TryCreate(url, UriKind.Absolute, out Uri u) ||
                        (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp))) { Log.Error("Refused to open: " + url); return; }
            if (TestOpenUrl != null) { TestOpenUrl(url); return; }
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
            catch (Exception ex) { Log.Error("Could not open " + url + ": " + ex.Message); }
        }

        /// <summary>
        /// The kinds of local file the program opens with Windows' own viewer: documents only. An allowlist on purpose -
        /// Windows runs far more file types than any list of "executables" holds (.url, .pif, .scf, .msc, .chm,
        /// .settingcontent-ms, script files of installed languages...).
        /// </summary>
        internal static bool IsSafeDocument(string path)
        {
            string e;
            try { e = Path.GetExtension(path).ToLowerInvariant(); } catch (ArgumentException) { return false; }
            return e == ".txt" || e == ".log" || e == ".md" || e == ".pdf" || e == ".html" || e == ".htm" ||
                   e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".gif" || e == ".bmp" || e == ".csv";
        }

        /// <summary>Test hook: when set, OpenFolder hands the path here instead of opening Explorer. Null in normal use.</summary>
        internal static Action<string> TestOpenFolder;

        public static void OpenFolder(string path)
        {
            if (TestOpenFolder != null) { TestOpenFolder(path); return; }
            try
            {
                if (File.Exists(path)) Process.Start(WindowsExe("explorer.exe"), "/select,\"" + Path.GetFullPath(path) + "\"")?.Dispose();
                else if (Directory.Exists(path)) Process.Start(WindowsExe("explorer.exe"), "\"" + Path.GetFullPath(path) + "\"")?.Dispose();
            }
            catch (Exception ex) { Log.Error("Could not open folder: " + ex.Message); }
        }
    }

    /// <summary>
    /// Runs the command line tools asynchronously and streams their output to the log.
    /// Arguments are built with <see cref="Q"/> so paths with spaces work; no shell is involved
    /// in captured mode, so there is nothing to inject into.
    /// </summary>
    public static class ToolRunner
    {
        /// <summary>Quote a single argument for the Windows CRT argv parser.</summary>
        public static string Q(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', backslashes); sb.Append(c); }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>
        /// Runs a tool. When <see cref="AppSettings.ShowToolWindows"/> is on, the tool runs in a visible
        /// console window (useful for interactive tools) and output is not captured.
        /// </summary>
        public static Task<ToolResult> RunAsync(string exe, string args, string workDir = null,
            bool quiet = false, CancellationToken ct = default(CancellationToken), bool forceVisible = false)
        {
            if (!File.Exists(exe))
            {
                Log.Error("Tool missing: " + exe + " - it must be in the same folder as X360GameHack.exe.");
                return Task.FromResult(new ToolResult { ExitCode = -1 });
            }

            if (forceVisible || (AppSettings.Current.ShowToolWindows && !quiet))
                return RunVisibleAsync(exe, args, workDir);

            return Task.Run(() =>
            {
                var result = new ToolResult();
                var sb = new StringBuilder();
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args ?? "",
                    WorkingDirectory = workDir ?? Tools.AppDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                if (!quiet) Log.Info("> " + Path.GetFileName(exe) + " " + args);
                var guard = ToolIntegrity.LockAndVerify(exe);
                if (guard == null) return new ToolResult { ExitCode = -1 };
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data == null) return;
                        lock (sb) sb.AppendLine(e.Data);
                        if (!quiet && e.Data.Trim().Length > 0) Log.Tool(e.Data);
                    };
                    p.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data == null) return;
                        lock (sb) sb.AppendLine(e.Data);
                        if (!quiet && e.Data.Trim().Length > 0) Log.Tool(e.Data);
                    };
                    try { p.Start(); }
                    finally { guard.Dispose(); }   // the image is mapped now; release the lock
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    using (ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } }))
                        p.WaitForExit();
                    p.WaitForExit(); // flush async readers
                    result.ExitCode = p.ExitCode;
                }
                result.Output = sb.ToString();
                if (!quiet)
                {
                    if (result.ExitCode == 0) Log.Success(Path.GetFileName(exe) + " finished.");
                    else Log.Warn(Path.GetFileName(exe) + " exited with code " + result.ExitCode + ".");
                }
                return result;
            }, ct);
        }

        private static Task<ToolResult> RunVisibleAsync(string exe, string args, string workDir)
        {
            // cmd.exe treats these characters specially; refuse rather than risk injection.
            string full = exe + " " + args;
            if (full.IndexOfAny(new[] { '&', '|', '<', '>', '^', '%', '\r', '\n' }) >= 0)
            {
                Log.Error("This path contains characters that cannot be passed to a console window (& | < > ^ %). " +
                          "Turn off 'Show tool windows' in Settings or rename the file.");
                return Task.FromResult(new ToolResult { ExitCode = -1 });
            }
            return Task.Run(() =>
            {
                Log.Info("> (window) " + Path.GetFileName(exe) + " " + args);
                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    Arguments = "/k \"\"" + exe + "\" " + args + "\"",
                    WorkingDirectory = workDir ?? Tools.AppDir,
                    UseShellExecute = true
                };
                var guard = ToolIntegrity.LockAndVerify(exe);
                if (guard == null) return new ToolResult { ExitCode = -1 };
                // Held until the window closes: cmd.exe starts the tool only after Process.Start returns, so letting go
                // here would leave a moment in which the checked file could be swapped. The lock only refuses writes.
                using (guard)
                using (var p = Process.Start(psi))
                {
                    p?.WaitForExit();
                    return new ToolResult { ExitCode = 0 };
                }
            });
        }
    }

    /// <summary>Validation for paths handed to the command line tools.</summary>
    public static class PathGuard
    {
        private static readonly string[] Reserved =
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>Returns null when the file is OK to process, otherwise a human readable reason.</summary>
        public static string CheckFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "No file selected.";
            if (path.IndexOf('"') >= 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return "The path contains invalid characters.";
            string full;
            try { full = Path.GetFullPath(path); }
            catch { return "The path is malformed."; }
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(win) && full.StartsWith(win, StringComparison.OrdinalIgnoreCase))
                return "Files inside the Windows folder are not allowed.";
            if (Reserved.Contains(Path.GetFileNameWithoutExtension(full), StringComparer.OrdinalIgnoreCase))
                return "The file name is a reserved Windows device name.";
            if (!File.Exists(full)) return "File not found: " + full;
            return null;
        }

        /// <summary>Logs and returns false when the file is not usable.</summary>
        public static bool Ensure(string path)
        {
            string err = CheckFile(path);
            if (err == null) return true;
            Log.Error(err);
            return false;
        }
    }
}
