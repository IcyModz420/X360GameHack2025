using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack.Core.Xbox_360
{
    public class XEXParcer
    {
        public string GameIconBase64 { get; private set; } = "Loading...";
        public string LoadAddress { get; private set; } = "Loading...";
        public string EntryPoint { get; private set; } = "Loading...";
        public string XEXName { get; private set; } = "Loading...";
        public string XEXVersion { get; private set; } = "Loading...";
        public string BaseVersion { get; private set; } = "Loading...";
        public string Compressed { get; private set; } = "Loading...";
        public string TitleId { get; private set; } = "Loading...";
        public string MediaId { get; private set; } = "Loading...";
        public string Encrypted { get; private set; } = "Loading...";
        public string XEXSystem { get; private set; } = "Loading...";

        private readonly string _toolPath;

        public XEXParcer()
        {
            string appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _toolPath = Path.Combine(appDir, "xextool.exe");

            if (!File.Exists(_toolPath))
            {
                X360GameHack.CurrentInstance.UpdateListboxForOutput("[ERROR] xextool.exe not found in application directory!");
            }
        }

        public void GetXEXInfo(string pkgPath)
        {
            // Reset
          //  TitleId = MediaId = InstallDir = "";
          //  PkgType = "Unknown";

            if (!File.Exists(_toolPath))
                return;

            string fullPkgPath = Path.IsPathRooted(pkgPath) ? pkgPath : Path.Combine(Path.GetDirectoryName(_toolPath), pkgPath);

            if (!File.Exists(fullPkgPath))
            {
                //X360GameHack.CurrentInstance.UpdateListboxForOutput($"[ERROR] XEX not found: {fullPkgPath}");
               // return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _toolPath,
                    Arguments = $"-l -b CurrentXEXBaseFile.exe -x i \"{fullPkgPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEndAsync().Result;
                    proc.WaitForExit();
                    if (proc.ExitCode != 0) return;
                    XEXVersion = Regex.Match(output, @"Version:\s*([^\s]+)")?.Groups[1].Value.Trim();
                    EntryPoint = Regex.Match(output, @"Entry Point:\s*([^\s]+)")?.Groups[1].Value.Trim();
                    TitleId = Regex.Match(output, @"Title Id:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    MediaId = Regex.Match(output, @"Media Id:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    Compressed = output.Contains("Compressed") ? "Compressed" : output.Contains("Uncompressed") ? "Uncompressed" : "Unknown";
                    Encrypted = output.Contains("Not-Encrypted") ? "Not-Encrypted":Regex.IsMatch(output, @"\bEncrypted\b") ? "Encrypted" : "Unknown";
                    XEXName = Regex.Match(output, @"Original PE Name:\s*([^\r\n]+)")?.Groups[1].Value.Trim();
                    XEXSystem = output.Contains("Retail") ? "Retail" : output.Contains("Devkit") ? "Devkit" : "Unknown";
                    EntryPoint = Regex.Match(output, @"Entry Point:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    LoadAddress = Regex.Match(output, @"Load Address:\s*([0-9A-Fa-f]{8})")?.Groups[1].Value.Trim();
                    BaseVersion = Regex.Match(output, @"Base Version:\s*([^\s]+)")?.Groups[1].Value.Trim();
                    GameIconBase64 = Regex.Match(output,@"<GameIcon\s+format=""base64"">\s*(.*?)\s*</GameIcon>",RegexOptions.Singleline)?.Groups[1].Value;
                }
            }
            catch { /* silently fail - properties stay empty */ }
        }
    }
}
