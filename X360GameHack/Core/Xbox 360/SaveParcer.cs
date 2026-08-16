using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace X360GameHack.Core.Xbox_360
{
    public class SaveParser
    {
        // These hold the results after calling GetPkgInfo()
        public string PkgVersion { get; private set; } = "";
        public string BaseVersion { get; private set; } = "";
        public string ConsoleID { get; private set; } = "";
        public string ProfileID { get; private set; } = "";
        public string DisplayName { get; private set; } = "";
        public string Description { get; private set; } = "";
        public string Publisher { get; private set; } = "";
        public string TitleName { get; private set; } = "";
        public string SaveVersion { get; private set; } = "";
        public string SaveBaseVersion { get; private set; } = "";
        public string ContentID { get; private set; } = "";
        public string TitleId { get; private set; } = "";
        public string MediaId { get; private set; } = "";
        public string InstallDir { get; private set; } = "";
        public string PkgType { get; private set; } = "Unknown";

        private readonly string _toolPath;

        public SaveParser()
        {
            // Automatically find X360PkgTool.exe in the same folder as your .exe
            string appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _toolPath = Path.Combine(appDir, "X360PkgTool.exe");

            if (!File.Exists(_toolPath))
            {
                Console.WriteLine("[ERROR] X360PkgTool.exe not found in application directory!");
                Console.WriteLine($"       Expected: {_toolPath}");
            }
        }

        /// <summary>
        /// ONE LINE. ONE CALL. EVERYTHING FILLED.
        /// Just pass the .pkg filename (or full path)
        /// </summary>
        public void GetPkgInfo(string pkgPath)
        {
            // Reset
            TitleId = MediaId = InstallDir = "";
            PkgType = "Unknown";

            if (!File.Exists(_toolPath))
                return;

            string fullPkgPath = Path.IsPathRooted(pkgPath) ? pkgPath : Path.Combine(Path.GetDirectoryName(_toolPath), pkgPath);

            if (!File.Exists(fullPkgPath))
            {
                // X360GameHack.CurrentInstance.UpdateListboxForOutput($"[ERROR] PKG not found: {fullPkgPath}");
                // return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _toolPath,
                    Arguments = $"-p \"{fullPkgPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEndAsync().Result;
                    proc.WaitForExit();
                    Thread.Sleep(500);

                    if (proc.ExitCode != 0) return;
                    SaveVersion = Regex.Match(output, @"Version:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    SaveBaseVersion = Regex.Match(output, @"Base Version:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    PkgVersion = Regex.Match(output, @"Version:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    BaseVersion = Regex.Match(output, @"Base Version:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    TitleId = Regex.Match(output, @"Title Id:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    MediaId = Regex.Match(output, @"Media Id:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    ContentID = Regex.Match(output, @"Content Id:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    InstallDir = Regex.Match(output, @"Install Dir:\s*([^\r\n]+)")?.Groups[1].Value.Trim();
                    TitleName = Regex.Match(output, @"Title Name:\s*([^\r\n]+)")?.Groups[1].Value.Trim();
                    DisplayName = Regex.Match(output, @"Display Name:\s*([^\r\n]+)")?.Groups[1].Value.Trim();
                    Description = Regex.Match(output, @"Description:\s*([^\r\n]+)")?.Groups[1].Value.Trim();
                    Publisher = Regex.Match(output, @"Publisher:\s*([^\r\n]+)")?.Groups[1].Value.Trim();
                    ConsoleID = Regex.Match(output, @"Console Id:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    ProfileID = Regex.Match(output, @"Profile Id:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    InstallDir = Regex.Match(output, @"Install Dir:\s*([^\r\n]+)")?.Groups[1].Value.Trim();
                    PkgType = output.Contains("Retail") ? "Retail" :
                              output.Contains("Devkit") ? "Devkit" : "Unknown";
                }
            }
            catch { /* silently fail - properties stay empty */ }
        }
    }
}