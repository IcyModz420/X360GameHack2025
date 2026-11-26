using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace X360GameHack.Core.Services
{
    /// <summary>One downloadable file of a release, as the signed update notice describes it.</summary>
    public sealed class UpdateFile
    {
        public string Name = "", Url = "", Sha256 = "";
        public long Size;
    }

    /// <summary>The signed update notice: the newest version and its two downloads (installer and portable zip).</summary>
    public sealed class UpdateManifest
    {
        public string Version = "", Published = "", Notes = "";
        public UpdateFile Installer, Portable;
    }

    /// <summary>What the start-up check found out - so "Offline" is only said when this PC really has no internet.</summary>
    public enum UpdateCheckState
    {
        /// <summary>The signed notice was read and checked out.</summary>
        Ok,
        /// <summary>Windows says this PC has no internet connection.</summary>
        NoInternet,
        /// <summary>This PC is online, but the update address could not be reached (no answer, name not found, timed out).</summary>
        ServerUnreachable,
        /// <summary>The update address answered without an update notice: nothing newer has been released (the title says Latest Version).</summary>
        NoNotice,
        /// <summary>A notice came back but did not pass the signature check (or did not make sense) - it was ignored.</summary>
        NotTrusted
    }

    /// <summary>The start-up check: the notice when it checked out, and otherwise why there is none.</summary>
    public sealed class UpdateCheckResult
    {
        public UpdateCheckState State;
        public UpdateManifest Manifest;
        /// <summary>A short technical reason for the log ("HTTP 404", "timed out", ...).</summary>
        public string Detail = "";

        /// <summary>What the title bar says when there is no version to compare with.</summary>
        public string Short
        {
            get
            {
                switch (State)
                {
                    case UpdateCheckState.NoInternet: return "Offline";
                    case UpdateCheckState.ServerUnreachable: return "Update Server Unreachable";
                    case UpdateCheckState.NoNotice: return "Latest Version";          // nothing newer has been released
                    case UpdateCheckState.NotTrusted: return "Update Notice Failed Its Check";
                    default: return "";
                }
            }
        }

        /// <summary>One plain sentence for the log and the "Check for updates now" answer.</summary>
        public string Explain
        {
            get
            {
                switch (State)
                {
                    case UpdateCheckState.NoInternet: return "This PC is offline, so X360GameHack could not check for a new version.";
                    case UpdateCheckState.ServerUnreachable: return "This PC is online, but X360GameHack's update server did not answer (" + Detail + "). It is tried again at the next start.";
                    case UpdateCheckState.NoNotice: return "You have the latest version (" + Pastebin.Version + ") - the update server has no newer release (" + Detail + ").";
                    case UpdateCheckState.NotTrusted: return "The update server sent a notice that is not signed by X360GameHack (" + Detail + ") - it was ignored and nothing was downloaded.";
                    default: return "";
                }
            }
        }
    }

    /// <summary>One file inside a downloaded portable update, compared with the copy that is installed now.</summary>
    public sealed class UpdateEntry
    {
        public string Path = "", Sha256 = "";
        public long Size;
        /// <summary>"New" (not in the program folder yet), "Replaces" (different from the file there now) or "Same".</summary>
        public string Change = "";
    }

    /// <summary>How this copy of the program can be updated.</summary>
    public enum UpdateRoute
    {
        /// <summary>It cannot update itself (for example a portable copy in a folder only administrators can change).</summary>
        None,
        /// <summary>The copy the installer put in Program Files: updated by running the new installer (Windows asks for permission).</summary>
        Installer,
        /// <summary>A portable copy in a folder this user can change: its files are replaced in place.</summary>
        Portable
    }

    /// <summary>
    /// Automatic updates.
    ///
    /// Trust: the program carries the PUBLIC half of an ECDSA P-256 key. At start-up it reads a small notice from
    /// Cloudflare (update.json) and its signature (update.json.sig), and believes the notice only when the signature
    /// checks out with that key - the private half never leaves the publisher's PC, so neither Cloudflare, GitHub, the
    /// network nor anyone else can make the program take a file. The notice names the newest version and, for each
    /// download, its GitHub release address, exact size and SHA-256. A download is kept only when its size and SHA-256
    /// match; anything else is deleted and nothing runs.
    ///
    /// Applying: the program never elevates. A small helper (a copy of this program in the user's own Updates folder,
    /// started with --apply-update) waits for the program to close, checks the file once more while holding it locked
    /// against changes, then either runs the new installer (Windows asks for permission, as for any install) or, for a
    /// portable copy, unpacks the zip over the program folder - and starts the program again.
    /// </summary>
    public static class AppUpdater
    {
        public const string ManifestUrl = "https://x360gh-update.x360gh-licence.workers.dev/update.json";
        private const string ReleasePath = "/IcyModz420/X360GameHack2025/releases/download/";
        // update-signing.pub.txt of the publisher: X || Y of the P-256 public key
        private const string PublicKey = "QXQKZQbSlvLS3H+4b44KW5q1dQ2ExZ58UcAzeadB1G2Mz3YsYxWUsvqZJIFohwzCyAHXixW75snEo4FAw1hn/A==";
        private const long MaxDownload = 1L << 30;

        /// <summary>For automated tests only: a loopback address ("http://127.0.0.1:port/") serving the notice and the files, and the test key. Null in normal use.</summary>
        internal static string TestBaseUrl, TestPublicKey;
        /// <summary>For automated tests only: the folder that stands for the installed copy's folder. Null in normal use.</summary>
        internal static string TestInstallFolder;

        public static string UpdatesFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "X360GameHack", "Updates");

        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = true, MaxAutomaticRedirections = 5 })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        // ------------------------------------------------------------------ the notice

        /// <summary>For automated tests only: stands in for Windows' own "is this PC on the internet" answer. Null in normal use.</summary>
        internal static Func<bool> TestInternet;

        /// <summary>Reads and checks the signed notice, and says why there is none when there is none (never throws).</summary>
        public static async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
        {
            string url = TestBaseUrl != null ? TestBaseUrl + "update.json" : ManifestUrl;
            try
            {
                var notice = await GetSmallAsync(url, 64 * 1024, ct).ConfigureAwait(false);
                if (notice.Data == null) return new UpdateCheckResult { State = UpdateCheckState.NoNotice, Detail = notice.Problem };
                var sigText = await GetSmallAsync(url + ".sig", 1024, ct).ConfigureAwait(false);
                // a notice without its signature cannot be trusted - never taken for "nothing newer"
                if (sigText.Data == null) return new UpdateCheckResult { State = UpdateCheckState.NotTrusted, Detail = "the notice has no signature (" + sigText.Problem + ")" };
                byte[] signature;
                try { signature = Convert.FromBase64String(Encoding.ASCII.GetString(sigText.Data).Trim()); }
                catch (FormatException) { return new UpdateCheckResult { State = UpdateCheckState.NotTrusted, Detail = "the signature is not readable" }; }
                if (!Verify(notice.Data, signature, TestPublicKey ?? PublicKey)) return new UpdateCheckResult { State = UpdateCheckState.NotTrusted, Detail = "wrong signature" };
                try { return new UpdateCheckResult { State = UpdateCheckState.Ok, Manifest = Parse(notice.Data) }; }
                catch (Exception ex) when (ex is InvalidDataException || ex is Newtonsoft.Json.JsonException) { return new UpdateCheckResult { State = UpdateCheckState.NotTrusted, Detail = ex.Message }; }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Unreachable("timed out");
            }
            catch (HttpRequestException ex) { return Unreachable((ex.InnerException ?? ex).Message); }
            catch (Exception ex) when (!(ex is OperationCanceledException)) { return Unreachable(ex.Message); }
        }

        /// <summary>No answer at all: offline when Windows says so, otherwise the update server is the problem.</summary>
        private static UpdateCheckResult Unreachable(string why)
            => new UpdateCheckResult { State = InternetConnected() ? UpdateCheckState.ServerUnreachable : UpdateCheckState.NoInternet, Detail = why };

        [ComImport, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface INetworkListManager
        {
            [DispId(1)] object GetNetworks(int flags);
            [DispId(2)] object GetNetwork(Guid networkId);
            [DispId(3)] object GetNetworkConnections();
            [DispId(4)] object GetNetworkConnection(Guid connectionId);
            [DispId(5)] bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
            [DispId(6)] bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
            [DispId(7)] int GetConnectivity();
        }

        /// <summary>
        /// Windows' own answer to "is this PC on the internet" (the same one behind the network icon), asked locally -
        /// no extra request goes anywhere. Falls back to "is any network up" when that cannot be asked.
        /// </summary>
        public static bool InternetConnected()
        {
            if (TestInternet != null) return TestInternet();
            try
            {
                var t = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"), false);
                if (t != null)
                {
                    object o = Activator.CreateInstance(t);
                    try { return ((INetworkListManager)o).IsConnectedToInternet; }
                    finally { Marshal.ReleaseComObject(o); }
                }
            }
            catch (Exception) { }
            try { return System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); } catch { return true; }
        }

        private sealed class Fetched { public byte[] Data; public string Problem = ""; }

        private static async Task<Fetched> GetSmallAsync(string url, int limit, CancellationToken ct)
        {
            if (!AddressAllowed(new Uri(url), false)) return new Fetched { Problem = "address not allowed" };
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                using (var r = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                {
                    if (!AddressAllowed(r.RequestMessage.RequestUri, false)) return new Fetched { Problem = "sent to another address" };
                    if (!r.IsSuccessStatusCode) return new Fetched { Problem = "HTTP " + (int)r.StatusCode };
                    using (var s = await r.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var m = new MemoryStream())
                    {
                        var buf = new byte[8192];
                        int n;
                        while ((n = await s.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false)) > 0)
                        {
                            if (m.Length + n > limit) return new Fetched { Problem = "too large" };
                            m.Write(buf, 0, n);
                        }
                        return new Fetched { Data = m.ToArray() };
                    }
                }
            }
        }

        public const string CounterUrl = "https://x360gh-update.x360gh-licence.workers.dev/open";

        /// <summary>
        /// Adds this start to the public open counter and returns the new count, or null (never throws). Nothing about the
        /// PC or the user is sent - the request is empty - and the counter stores only the number.
        /// </summary>
        public static async Task<long?> CountOpenAsync(CancellationToken ct)
        {
            try
            {
                string url = TestBaseUrl != null ? TestBaseUrl + "open" : CounterUrl;
                if (!AddressAllowed(new Uri(url), false)) return null;
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(10));
                    using (var empty = new ByteArrayContent(new byte[0]))
                    using (var r = await Http.PostAsync(url, empty, cts.Token).ConfigureAwait(false))
                    {
                        if (!r.IsSuccessStatusCode || !AddressAllowed(r.RequestMessage.RequestUri, false)) return null;
                        byte[] body;
                        using (var s = await r.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var m = new MemoryStream())
                        {
                            var buf = new byte[1024];
                            int n;
                            while ((n = await s.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false)) > 0)
                            {
                                if (m.Length + n > 1024) return null;
                                m.Write(buf, 0, n);
                            }
                            body = m.ToArray();
                        }
                        long? opens = (long?)JObject.Parse(Encoding.UTF8.GetString(body))["opens"];
                        return opens.HasValue && opens.Value >= 0 && opens.Value < 1000000000000L ? opens : null;
                    }
                }
            }
            catch { return null; }
        }

        /// <summary>True when <paramref name="signature"/> (r || s, 64 bytes) signs <paramref name="data"/> with the P-256 key X || Y.</summary>
        public static bool Verify(byte[] data, byte[] signature, string publicKeyBase64)
        {
            if (data == null || signature == null || signature.Length != 64) return false;
            byte[] q;
            try { q = Convert.FromBase64String(publicKeyBase64); } catch (FormatException) { return false; }
            if (q.Length != 64) return false;
            var p = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = q.Take(32).ToArray(), Y = q.Skip(32).ToArray() } };
            try
            {
                using (var ec = ECDsa.Create(p))
                    return ec.VerifyData(data, signature, HashAlgorithmName.SHA256);
            }
            catch (CryptographicException) { return false; }
        }

        private static readonly Regex VersionText = new Regex(@"^\d{1,4}(\.\d{1,6}){1,3}$", RegexOptions.CultureInvariant);
        private static readonly Regex FileName = new Regex(@"^[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant);
        private static readonly Regex Hex64 = new Regex(@"^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

        /// <summary>Reads a notice that has already passed the signature check; anything unexpected in it refuses the whole notice.</summary>
        public static UpdateManifest Parse(byte[] json)
        {
            var o = JObject.Parse(Encoding.UTF8.GetString(json));
            if ((string)o["product"] != "X360GameHack") throw new InvalidDataException("The update notice is for another program.");
            string version = (string)o["version"] ?? "";
            if (!VersionText.IsMatch(version)) throw new InvalidDataException("The update notice has no valid version.");
            return new UpdateManifest
            {
                Version = version,
                Published = ((string)o["published"] ?? "").Trim(),
                Notes = ((string)o["notes"] ?? "").Trim(),
                Installer = ParseFile(o["installer"] as JObject),
                Portable = ParseFile(o["portable"] as JObject)
            };
        }

        private static UpdateFile ParseFile(JObject f)
        {
            if (f == null) throw new InvalidDataException("The update notice misses a download.");
            var u = new UpdateFile { Name = (string)f["name"] ?? "", Url = (string)f["url"] ?? "", Sha256 = ((string)f["sha256"] ?? "").ToLowerInvariant(), Size = (long?)f["size"] ?? 0 };
            if (!FileName.IsMatch(u.Name) || !Hex64.IsMatch(u.Sha256) || u.Size <= 0 || u.Size > MaxDownload)
                throw new InvalidDataException("The update notice describes a download wrongly.");
            if (!Uri.TryCreate(u.Url, UriKind.Absolute, out Uri uri) || !AddressAllowed(uri, true) ||
                (TestBaseUrl == null && !uri.AbsolutePath.StartsWith(ReleasePath, StringComparison.Ordinal)))
                throw new InvalidDataException("The update notice points somewhere else than the program's own releases.");
            return u;
        }

        /// <summary>Only HTTPS to the notice's own host, GitHub and GitHub's download hosts (loopback HTTP only under test).</summary>
        private static bool AddressAllowed(Uri u, bool download)
        {
            if (TestBaseUrl != null) return u.IsLoopback && (u.Scheme == "http" || u.Scheme == "https");
            if (u.Scheme != Uri.UriSchemeHttps || !u.IsDefaultPort) return false;
            string host = u.Host.ToLowerInvariant();
            if (!download) return host == new Uri(ManifestUrl).Host;
            return host == "github.com" || host == "objects.githubusercontent.com" || host == "release-assets.githubusercontent.com";
        }

        /// <summary>True when the notice offers a version newer than this copy.</summary>
        public static bool IsNewer(UpdateManifest m) => m != null && Pastebin.Compare(m.Version, Pastebin.Version) > 0;

        // ------------------------------------------------------------------ which copy is this

        private static string ProgramFolder => Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\');

        /// <summary>The folder the installer put the program in (from its uninstall data), or null.</summary>
        public static string InstalledFolder()
        {
            if (TestInstallFolder != null) return TestInstallFolder;
            try
            {
                // the installer is 32-bit: its key lives in the 32-bit view of the registry
                using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (var k = hk.OpenSubKey(@"Software\X360GameHack"))
                {
                    string f = k?.GetValue("InstallFolder") as string;
                    return string.IsNullOrWhiteSpace(f) ? null : Path.GetFullPath(f).TrimEnd('\\');
                }
            }
            catch { return null; }
        }

        public static UpdateRoute RouteForThisCopy()
        {
            string here = ProgramFolder, installed = InstalledFolder();
            if (installed != null && string.Equals(installed, here, StringComparison.OrdinalIgnoreCase)) return UpdateRoute.Installer;
            return CanWrite(here) ? UpdateRoute.Portable : UpdateRoute.None;
        }

        private static bool CanWrite(string folder)
        {
            try
            {
                string probe = Path.Combine(folder, ".x360gh-write-test-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ download

        /// <summary>
        /// Downloads <paramref name="f"/> into the Updates folder and returns its path - only when its size and SHA-256 are
        /// exactly what the signed notice says; otherwise the partial file is deleted and an exception says why.
        /// </summary>
        public static async Task<string> DownloadAsync(UpdateFile f, string version, IProgress<Net.TransferProgress> progress, CancellationToken ct)
        {
            string dir = Path.Combine(UpdatesFolder, version);
            Directory.CreateDirectory(dir);
            string final = Path.Combine(dir, f.Name), part = final + ".part";
            if (File.Exists(final) && Matches(final, f)) return final;          // already downloaded and checked
            try
            {
                using (var r = await Http.GetAsync(f.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    r.EnsureSuccessStatusCode();
                    if (!AddressAllowed(r.RequestMessage.RequestUri, true)) throw new InvalidDataException("The download was sent somewhere unexpected (" + r.RequestMessage.RequestUri.Host + ").");
                    long? length = r.Content.Headers.ContentLength;
                    if (length.HasValue && length.Value != f.Size) throw new InvalidDataException("The download is " + length.Value + " bytes, not the " + f.Size + " the update notice names.");
                    using (var s = await r.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var o = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var sha = SHA256.Create())
                    {
                        var buf = new byte[81920];
                        long done = 0;
                        int n;
                        while ((n = await s.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            done += n;
                            if (done > f.Size) throw new InvalidDataException("The download is larger than the update notice says.");
                            sha.TransformBlock(buf, 0, n, null, 0);
                            await o.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                            progress?.Report(new Net.TransferProgress { Done = done, Total = f.Size, File = f.Name });
                        }
                        sha.TransformFinalBlock(buf, 0, 0);
                        if (done != f.Size) throw new InvalidDataException("The download stopped early (" + done + " of " + f.Size + " bytes).");
                        if (Hex(sha.Hash) != f.Sha256) throw new InvalidDataException("The download does not match the signed update notice (SHA-256) - it was deleted.");
                    }
                }
                if (File.Exists(final)) File.Delete(final);
                File.Move(part, final);
                return final;
            }
            catch
            {
                try { File.Delete(part); } catch { }
                throw;
            }
        }

        private static string Hex(byte[] b) => BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();

        /// <summary>The SHA-256 of a file on this PC (lower-case hex), read with sharing so a file in use can still be hashed.</summary>
        public static string Sha256Of(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) return Hash(fs);
        }

        /// <summary>
        /// Every file a downloaded portable update holds, with its SHA-256, and what installing it does to the program
        /// folder: adds it, replaces a different file, or leaves an identical one as it is. The zip is only read.
        /// </summary>
        public static System.Collections.Generic.List<UpdateEntry> ListUpdate(string zipPath, string programFolder = null)
        {
            string folder = programFolder ?? ProgramFolder;
            var list = new System.Collections.Generic.List<UpdateEntry>();
            using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Read))
                foreach (var e in z.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (!name.StartsWith("X360GameHack/", StringComparison.Ordinal) || name.EndsWith("/")) continue;
                    string rel = name.Substring("X360GameHack/".Length);
                    if (rel.Split('/').Any(p => p.Length == 0 || p == "." || p == ".." || p.Contains(":"))) throw new InvalidDataException("The update holds a file name that points outside the program folder: " + e.FullName);
                    string sha;
                    using (var s = e.Open()) using (var h = SHA256.Create()) sha = Hex(h.ComputeHash(s));
                    string here = Path.Combine(folder, rel.Replace('/', '\\'));
                    string change = !File.Exists(here) ? "New" : (new FileInfo(here).Length == e.Length && Sha256Of(here) == sha ? "Same" : "Replaces");
                    list.Add(new UpdateEntry { Path = rel.Replace('/', '\\'), Size = e.Length, Sha256 = sha, Change = change });
                }
            return list;
        }

        private static bool Matches(string path, UpdateFile f)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    return fs.Length == f.Size && Hash(fs) == f.Sha256;
            }
            catch { return false; }
        }

        private static string Hash(Stream s) { using (var sha = SHA256.Create()) { s.Position = 0; return Hex(sha.ComputeHash(s)); } }

        // ------------------------------------------------------------------ apply

        /// <summary>
        /// Starts the helper that applies a checked download once this program has closed. The caller closes the
        /// program right after. With <paramref name="restart"/> the helper starts the program again afterwards.
        /// </summary>
        public static void StartApply(UpdateRoute route, string file, UpdateFile f, string version, bool restart)
        {
            if (route == UpdateRoute.None) throw new InvalidOperationException("This copy cannot update itself.");
            string helperDir = Path.Combine(UpdatesFolder, "helper");
            Directory.CreateDirectory(helperDir);
            string self = typeof(AppUpdater).Assembly.Location;              // X360GameHack.exe itself
            // not "...Update..." / "...Setup...": Windows treats a program without an embedded manifest whose name holds
            // such a word as an installer and refuses to start it without administrator rights
            string helper = Path.Combine(helperDir, "X360GameHack-Helper.exe");
            File.Copy(self, helper, true);
            if (File.Exists(self + ".config")) File.Copy(self + ".config", helper + ".config", true);
            string target = route == UpdateRoute.Installer ? (InstalledFolder() ?? ProgramFolder) : ProgramFolder;
            var psi = new ProcessStartInfo(helper)
            {
                Arguments = string.Join(" ", new[] { "--apply-update", route == UpdateRoute.Installer ? "installer" : "portable", Q(file), f.Sha256, f.Size.ToString(),
                                                     Process.GetCurrentProcess().Id.ToString(), Q(target), version, restart ? "restart" : "no-restart" }),
                UseShellExecute = false,
                WorkingDirectory = helperDir
            };
            Process.Start(psi)?.Dispose();
            Log.Info("Update " + version + " will be applied as soon as X360GameHack closes.");
        }

        private static string Q(string s) => "\"" + s + "\"";

        /// <summary>The version this PC last applied, so a release whose files do not carry the new version cannot loop.</summary>
        public static string LastApplied
        {
            get { try { return File.ReadAllText(Path.Combine(UpdatesFolder, "applied.txt")).Trim(); } catch { return ""; } }
        }

        private static void Note(string line)
        {
            try { Directory.CreateDirectory(UpdatesFolder); File.AppendAllText(Path.Combine(UpdatesFolder, "update.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine); } catch { }
        }

        /// <summary>
        /// The helper (X360GameHack-Helper.exe --apply-update ...). Runs without a main window and returns the exit code.
        /// Arguments: route, file, sha256, size, pid to wait for, program folder, version, restart / no-restart.
        /// </summary>
        public static int RunApply(string[] a)
        {
            // a: [0]=--apply-update [1]=route [2]=file [3]=sha256 [4]=size [5]=pid [6]=folder [7]=version [8]=restart
            if (a.Length != 9) return 2;
            string route = a[1], file = a[2], sha = a[3].ToLowerInvariant(), folder = Path.GetFullPath(a[6]).TrimEnd('\\'), version = a[7];
            bool restart = a[8] == "restart";
            if ((route != "installer" && route != "portable") || !Hex64.IsMatch(sha) || !long.TryParse(a[4], out long size) || !int.TryParse(a[5], out int pid) || !VersionText.IsMatch(version)) return 2;
            string exe = Path.Combine(folder, "X360GameHack.exe");
            // only files the program downloaded itself, into this user's Updates folder
            string full = Path.GetFullPath(file);
            if (!full.StartsWith(Path.GetFullPath(UpdatesFolder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) return 2;
            Note("applying " + version + " (" + route + ") from " + full);
            bool ok = false;
            try
            {
                // locked against changes from here to the end: what is checked is what gets installed
                using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length != size || Hash(fs) != sha) { Note("refused: the file changed since it was checked"); Tell("The downloaded update does not match the signed update notice any more, so it was not applied."); return 3; }
                    try { using (var p = Process.GetProcessById(pid)) p.WaitForExit(120000); } catch (ArgumentException) { }
                    if (route == "installer") ok = RunInstaller(full);
                    else { fs.Position = 0; ok = Unpack(fs, folder, version); }
                }
                if (ok)
                {
                    File.WriteAllText(Path.Combine(UpdatesFolder, "applied.txt"), version);
                    Note("done");
                    try { Directory.Delete(Path.GetDirectoryName(full), true); } catch { }
                }
            }
            catch (Exception ex) { Note("failed: " + ex.Message); Tell("The update could not be applied: " + ex.Message); }
            if (restart || !ok)
            {
                try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = folder })?.Dispose(); } catch (Exception ex) { Note("restart failed: " + ex.Message); }
            }
            return ok ? 0 : 1;
        }

        private static bool RunInstaller(string msi)
        {
            string msiexec = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
            using (var p = Process.Start(new ProcessStartInfo(msiexec, "/i \"" + msi + "\" /passive /norestart") { UseShellExecute = false }))
            {
                p.WaitForExit();
                Note("installer exit code " + p.ExitCode);
                if (p.ExitCode == 0 || p.ExitCode == 3010 || p.ExitCode == 1641) return true;
                if (p.ExitCode == 1602) { Note("cancelled at the permission prompt"); return false; }
                Tell("The installer stopped (code " + p.ExitCode + "). X360GameHack was left as it was.");
                return false;
            }
        }

        /// <summary>Unpacks the portable zip (entries "X360GameHack/...") into a staging folder, then copies it over the program folder.</summary>
        private static bool Unpack(Stream zip, string folder, string version)
        {
            string staging = Path.Combine(UpdatesFolder, version, "staging");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            string root = Path.GetFullPath(staging).TrimEnd('\\') + "\\";
            int files = 0;
            using (var z = new ZipArchive(zip, ZipArchiveMode.Read, true))
                foreach (var e in z.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (!name.StartsWith("X360GameHack/", StringComparison.Ordinal) || name.EndsWith("/")) continue;
                    string rel = name.Substring("X360GameHack/".Length);
                    string dest = Path.GetFullPath(Path.Combine(root, rel.Replace('/', '\\')));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase) || rel.Split('/').Any(p => p.Length == 0 || p == "." || p == ".." || p.Contains(":")))
                        throw new InvalidDataException("The update holds a file name that points outside the program folder: " + e.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    using (var i = e.Open()) using (var o = new FileStream(dest, FileMode.CreateNew, FileAccess.Write)) i.CopyTo(o);
                    files++;
                }
            if (!File.Exists(Path.Combine(staging, "X360GameHack.exe"))) throw new InvalidDataException("The update does not hold X360GameHack.exe.");
            // every file that will be replaced must be free first (Xenia started from this folder, for example) -
            // otherwise nothing is copied at all, so the folder never ends up half old and half new
            var sources = Directory.GetFiles(staging, "*", SearchOption.AllDirectories);
            foreach (string src in sources)
            {
                string dest = Path.Combine(folder, src.Substring(root.Length));
                if (!File.Exists(dest)) continue;
                for (int attempt = 0; ; attempt++)
                {
                    try { using (new FileStream(dest, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } break; }
                    catch (IOException) when (attempt < 20) { Thread.Sleep(500); }
                    catch (IOException) { throw new IOException(Path.GetFileName(dest) + " is still in use (is Xenia or another program from the X360GameHack folder still open?). Nothing was changed."); }
                }
            }
            foreach (string src in sources)
            {
                string dest = Path.Combine(folder, src.Substring(root.Length));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Copy(src, dest, true); break; }
                    catch (IOException) when (attempt < 20) { Thread.Sleep(500); }      // the old program may still be letting go of a file
                }
            }
            Note(files + " files copied into " + folder);
            Directory.Delete(staging, true);
            return true;
        }

        /// <summary>
        /// Shows the update helper's messages; the program sets it to its own message window in the theme saved in
        /// Settings (see Other\Program.cs). Without it, a plain Windows message box.
        /// </summary>
        internal static Action<string> ShowMessage;

        private static void Tell(string text)
        {
            try { if (ShowMessage != null) { ShowMessage(text); return; } } catch { }
            try { System.Windows.Forms.MessageBox.Show(text, "X360GameHack update", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning); } catch { }
        }
    }
}
