using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace X360GameHack.Core.Services
{
    /// <summary>What was found when abgx360's setup was checked.</summary>
    public sealed class AbgxState
    {
        /// <summary>abgx360.exe, or null when it is not installed.</summary>
        public string Exe;
        /// <summary>The exact abgx360.exe 1.0.7 that Hadzz's installer (the one bundled with this program) installs.</summary>
        public bool HadzzBuild;
        /// <summary>"1.0.7" etc., read from the program's own banner text; null when it could not be read.</summary>
        public string Version;
        /// <summary>The program has Hadzz's server addresses built in (1.0.7 Hadzz build) - no fix file needed, but it does no harm.</summary>
        public bool ServerBuiltIn;
        /// <summary>%APPDATA%\abgx360 - where abgx360 keeps abgx360.dat, the fix file, StealthFiles and Images.</summary>
        public string DataFolder;
        public bool FixFilePresent;
        /// <summary>The fix file points every address at Hadzz's server.</summary>
        public bool FixFileCurrent;
        /// <summary>The server an existing fix file points at when it is not Hadzz's (null otherwise).</summary>
        public string FixFileServer;
        /// <summary>Video files from the installer that abgx360 can use straight away (in its StealthFiles folder).</summary>
        public int VideoFiles;

        /// <summary>abgx360 is installed and reaches a database server that works.</summary>
        public bool Ready => Exe != null && (ServerBuiltIn || FixFileCurrent || FixFileServer != null && !AbgxSetup.IsDeadServer(FixFileServer));

        public string Describe()
        {
            if (Exe == null) return "abgx360 is not installed. \"Set up abgx360\" runs Hadzz's installer (it comes with this program); Windows asks for permission because it installs into the Windows folder.";
            string what = "abgx360 " + (Version ?? "(unknown version)") + (HadzzBuild ? " (Hadzz build, file checked)" : "") + " - " + Exe;
            string server = ServerBuiltIn ? "database: " + AbgxSetup.Server + " (built in)"
                          : FixFileCurrent ? "database: " + AbgxSetup.Server + " (through the fix file)"
                          : FixFileServer != null ? "database: " + FixFileServer + " (your own abgx360.ini)"
                          : "database: the original abgx360.net, which is gone - \"Set up abgx360\" adds the fix file";
            string fix = FixFileCurrent ? "fix file in place" : FixFilePresent ? "abgx360.ini present" : "no fix file yet";
            return what + "\n" + server + "; " + fix + (VideoFiles > 0 ? "; " + VideoFiles + " video file(s) ready offline" : "") + ".";
        }
    }

    /// <summary>
    /// Makes abgx360 ready to use with Hadzz's database server (abgx360.net, the original server, is gone):
    ///  - abgx360.exe: the Hadzz 1.0.7 build from the bundled installer (abgx360_v1.0.7_setup_hadzz.exe), which has the server
    ///    addresses built in; another abgx360 that is found is used as well;
    ///  - the fix file: %APPDATA%\abgx360\abgx360.ini, next to abgx360.dat - the file older builds (1.0.6 and before) need to reach
    ///    the server; harmless for 1.0.7. It holds the same seven addresses Hadzz's 1.0.7 build has built in, in the form
    ///    abgx360's checkini() reads ("web_inidir: http://...", one per line, each ending in a line break). Written when it is
    ///    missing; replaced (the old one kept as .bak) when it is incomplete or still points at the dead abgx360.net; left alone
    ///    when it points at a server of the user's own choosing;
    ///  - the two video files the installer puts in Program Files, copied into the StealthFiles folder abgx360 actually reads
    ///    (each checked against the CRC-32 in its name first), so they are not downloaded again.
    /// Installing abgx360 needs administrator rights (its installer writes to the Windows folder). This program never asks for
    /// them: Windows asks the user when the installer starts.
    /// </summary>
    public static class AbgxSetup
    {
        public const string Server = "abgx360.hadzz.com";

        /// <summary>SHA-256 of abgx360.exe 1.0.7 as Hadzz's installer (abgx360_v1.0.7_setup_hadzz.exe) installs it.</summary>
        public const string HadzzExeSha256 = "9ABB5C07F19FB32705FA95F9902EBD85B95F593B84F1334E633EB3F1863D835B";

        /// <summary>The fix file's lines: the addresses in Hadzz's abgx360 1.0.7 (read from that program's own text).</summary>
        public static readonly KeyValuePair<string, string>[] FixLines =
        {
            new KeyValuePair<string, string>("web_inidir", "http://abgx360.hadzz.com/verified/"),
            new KeyValuePair<string, string>("web_unverifiedinidir", "http://abgx360.hadzz.com/unverified/"),
            new KeyValuePair<string, string>("web_stealthdir", "http://abgx360.hadzz.com/StealthFiles/"),
            new KeyValuePair<string, string>("web_csv", "http://abgx360.hadzz.com/GameNameLookup.csv"),
            new KeyValuePair<string, string>("web_dat", "http://abgx360.hadzz.com/abgx360.dat"),
            new KeyValuePair<string, string>("web_topology", "http://abgx360.hadzz.com/topology.php"),
            new KeyValuePair<string, string>("web_autoupload", "http://abgx360.hadzz.com/Control/AutoUpload.php")
        };

        /// <summary>abgx360's home: getenv("APPDATA") + "\abgx360\" (the same variable the program itself reads).</summary>
        public static string DataFolder
        {
            get
            {
                string appData = Environment.GetEnvironmentVariable("APPDATA");
                if (string.IsNullOrEmpty(appData)) appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "abgx360");
            }
        }

        public static string FixFilePath => Path.Combine(DataFolder, "abgx360.ini");
        public static string StealthFolder => Path.Combine(DataFolder, "StealthFiles");
        public static string ImagesFolder => Path.Combine(DataFolder, "Images");

        internal static bool IsDeadServer(string server) => server.IndexOf("abgx360.net", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Looks at what is installed; changes nothing.</summary>
        public static AbgxState Check()
        {
            var st = new AbgxState { Exe = Tools.Abgx360, DataFolder = DataFolder };
            if (st.Exe != null)
            {
                try
                {
                    byte[] b = File.ReadAllBytes(st.Exe);
                    using (var sha = SHA256.Create()) st.HadzzBuild = Hex(sha.ComputeHash(b)) == HadzzExeSha256;
                    string text = Encoding.ASCII.GetString(b);
                    var v = System.Text.RegularExpressions.Regex.Match(text, @"abgx360 v(\d+\.\d+\.\d+)");
                    if (v.Success) st.Version = v.Groups[1].Value;
                    st.ServerBuiltIn = text.IndexOf("http://" + Server + "/verified/", StringComparison.Ordinal) >= 0;
                }
                catch (Exception ex) { Log.Warn("abgx360: could not read " + st.Exe + ": " + ex.Message); }
            }
            var ini = ReadFixFile(FixFilePath);
            st.FixFilePresent = ini != null;
            if (ini != null)
            {
                st.FixFileCurrent = FixLines.All(l => ini.TryGetValue(l.Key, out string u) && u == l.Value);
                if (!st.FixFileCurrent)
                    st.FixFileServer = ini.Values.Select(ServerOf).FirstOrDefault(s => s != null && !s.Equals(Server, StringComparison.OrdinalIgnoreCase));
            }
            try { st.VideoFiles = Directory.Exists(StealthFolder) ? Directory.GetFiles(StealthFolder, "Video_*.iso").Length : 0; } catch { }
            return st;
        }

        /// <summary>The web_* values of an abgx360.ini, read the way abgx360's checkini() reads them; null when there is no file.</summary>
        public static Dictionary<string, string> ReadFixFile(string path)
        {
            if (!File.Exists(path)) return null;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            int n = 0;
            foreach (string raw in File.ReadAllText(path, Encoding.ASCII).Split('\n'))
            {
                if (++n > 2000) break;                                  // checkini reads at most 2000 lines
                string line = raw.TrimEnd('\r');
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon);
                if (!FixLines.Any(l => l.Key == key)) continue;
                string value = line.Substring(colon + 1);
                int lead = value.Length - value.TrimStart(' ').Length;
                if (lead > 20) continue;                                // checkini allows at most 20 spaces after the colon
                d[key] = value.Substring(lead);
            }
            return d;
        }

        private static string ServerOf(string url)
        {
            try { return new Uri(url).Host; } catch { return null; }
        }

        /// <summary>The fix file's text, exactly as written.</summary>
        public static string FixFileText() => string.Concat(FixLines.Select(l => l.Key + ": " + l.Value + "\r\n"));

        /// <summary>
        /// Writes the fix file when it is missing, incomplete or points at the dead abgx360.net (an existing file is kept as
        /// abgx360.ini.bak first). A file that points at another working server is left alone. Returns what was done.
        /// </summary>
        public static string EnsureFixFile()
        {
            string path = FixFilePath;
            var ini = ReadFixFile(path);
            if (ini != null)
            {
                if (FixLines.All(l => ini.TryGetValue(l.Key, out string u) && u == l.Value)) return "The fix file is already in place.";
                string other = ini.Values.Select(ServerOf).FirstOrDefault(s => s != null && !s.Equals(Server, StringComparison.OrdinalIgnoreCase));
                bool complete = FixLines.All(l => ini.ContainsKey(l.Key));
                if (other != null && !IsDeadServer(other) && complete) return "abgx360.ini points at " + other + " (left as it is - your own choice).";
            }
            Directory.CreateDirectory(DataFolder);
            string tmp = path + ".new";
            File.WriteAllText(tmp, FixFileText(), Encoding.ASCII);
            if (File.Exists(path))
            {
                string bak = path + ".bak";
                for (int i = 2; File.Exists(bak); i++) bak = path + "." + i + ".bak";
                File.Replace(tmp, path, bak);
                return "Fix file updated to " + Server + " (the old one is kept as " + Path.GetFileName(bak) + ").";
            }
            File.Move(tmp, path);
            return "Fix file written: " + path + ".";
        }

        /// <summary>Where the installer put abgx360's own files (it records this in the registry).</summary>
        public static string InstallFolder
        {
            get
            {
                foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                {
                    try
                    {
                        using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                        using (var k = hk.OpenSubKey(@"SOFTWARE\abgx360"))
                            if (k?.GetValue("Install_Dir") is string d && Directory.Exists(d)) return d;
                    }
                    catch { }
                }
                foreach (string d in new[] { @"C:\Program Files (x86)\abgx360", @"C:\Program Files\abgx360" })
                    if (Directory.Exists(d)) return d;
                return null;
            }
        }

        /// <summary>
        /// Copies the installer's video files (Install_Dir\StealthFiles\Video_*.iso) into the StealthFiles folder abgx360 reads,
        /// each only when its CRC-32 is the one in its name and it is not there already. Returns how many were copied.
        /// </summary>
        public static int CopyVideoFiles()
        {
            string src = InstallFolder == null ? null : Path.Combine(InstallFolder, "StealthFiles");
            if (src == null || !Directory.Exists(src)) return 0;
            int copied = 0;
            foreach (string f in Directory.GetFiles(src, "Video_*.iso"))
            {
                string name = Path.GetFileName(f);
                string target = Path.Combine(StealthFolder, name);
                if (File.Exists(target)) continue;
                var m = System.Text.RegularExpressions.Regex.Match(name, @"^Video_([0-9A-Fa-f]{8})\.iso$");
                if (!m.Success) continue;
                uint want = Convert.ToUInt32(m.Groups[1].Value, 16);
                if (Crc32Of(f) != want) { Log.Warn("abgx360: " + name + " does not match the CRC in its name - not copied."); continue; }
                Directory.CreateDirectory(StealthFolder);
                string tmp = target + ".new";
                File.Copy(f, tmp, true);
                if (Crc32Of(tmp) != want) { File.Delete(tmp); Log.Warn("abgx360: copying " + name + " failed its check - not copied."); continue; }
                File.Move(tmp, target);
                copied++;
            }
            return copied;
        }

        /// <summary>
        /// Gets abgx360 ready: runs Hadzz's installer when abgx360 is missing (Windows asks for permission; this program never
        /// does), then writes the fix file and copies the video files. Returns the state afterwards and what was done.
        /// </summary>
        public static async Task<(AbgxState State, string Done)> SetUpAsync()
        {
            var done = new List<string>();
            if (Tools.Abgx360 == null)
            {
                string setup = Tools.AbgxInstaller;
                if (!File.Exists(setup)) throw new FileNotFoundException("abgx360_v1.0.7_setup_hadzz.exe is missing next to X360GameHack.exe.");
                int code = await Task.Run(() =>
                {
                    using (var guard = ToolIntegrity.LockAndVerify(setup))
                    {
                        if (guard == null) return -2;
                        try
                        {
                            using (var p = Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true, WorkingDirectory = Tools.AppDir }))
                            {
                                if (p == null) return -1;
                                p.WaitForExit();
                                return p.ExitCode;
                            }
                        }
                        catch (System.ComponentModel.Win32Exception) { return -3; }   // the permission prompt was declined
                    }
                });
                if (code == -2) throw new InvalidOperationException("The bundled abgx360 installer failed its integrity check - it was not started.");
                if (code == -3) throw new OperationCanceledException("Installing abgx360 was cancelled.");
                if (Tools.Abgx360 == null) throw new InvalidOperationException("abgx360 was not installed (the installer ended with code " + code + ").");
                done.Add("abgx360 installed");
            }
            done.Add(EnsureFixFile());
            int v = CopyVideoFiles();
            if (v > 0) done.Add(v + " video file(s) copied into " + StealthFolder);
            return (Check(), string.Join(" ", done));
        }

        public static uint Crc32Of(string path)
        {
            uint crc = 0xFFFFFFFF;
            var table = CrcTable.Value;
            byte[] buf = new byte[1 << 16];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                    for (int i = 0; i < n; i++) crc = table[(crc ^ buf[i]) & 0xFF] ^ (crc >> 8);
            }
            return ~crc;
        }

        private static readonly Lazy<uint[]> CrcTable = new Lazy<uint[]>(() =>
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        });

        private static string Hex(byte[] b) => string.Concat(b.Select(x => x.ToString("X2")));
    }
}
