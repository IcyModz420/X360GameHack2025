using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Net
{
    public sealed class RemoteItem
    {
        public string Name;
        public bool IsDirectory;
        public long Size;
        public DateTime Modified;
    }

    /// <summary>Transfer progress for the status bar: bytes done / total and the current file.</summary>
    public sealed class TransferProgress
    {
        public long Done;
        public long Total;
        public string File;
        public int Percent => Total <= 0 ? 0 : (int)Math.Min(100, Done * 100 / Total);
    }

    /// <summary>
    /// FTP client used for Xbox (Aurora / FSD / XeXMenu / UnleashX / EvoX), PS3 and PS4 FTP servers.
    /// Every call opens its own short connection, which is what these small homebrew servers handle best.
    /// </summary>
    public sealed class FtpService
    {
        private const int BufferSize = 64 * 1024;
        public FtpProfile Profile { get; }

        public FtpService(FtpProfile profile) { Profile = profile; }

        public static string Combine(string dir, string name)
        {
            dir = string.IsNullOrEmpty(dir) ? "/" : dir;
            if (!dir.EndsWith("/")) dir += "/";
            return dir + name.Trim('/');
        }

        public static string Parent(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/") return "/";
            string p = path.TrimEnd('/');
            int i = p.LastIndexOf('/');
            return i <= 0 ? "/" : p.Substring(0, i);
        }

        private Uri BuildUri(string path)
        {
            var segments = (path ?? "/").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(Uri.EscapeDataString);
            string host = Profile.Host.Trim();
            // %2F after the authority makes the path absolute (servers that chroot to a home dir need this)
            string joined = string.Join("/", segments);
            // A trailing slash tells FtpWebRequest to CWD into the folder before LIST.
            if ((path ?? "").EndsWith("/") && joined.Length > 0) joined += "/";
            return new Uri("ftp://" + host + ":" + Profile.Port + "/%2F" + joined);
        }

        private FtpWebRequest Create(string path, string method)
        {
            var req = (FtpWebRequest)WebRequest.Create(BuildUri(path));
            req.Method = method;
            req.Credentials = new NetworkCredential(Profile.User ?? "", Profile.Password ?? "");
            req.UsePassive = Profile.Passive;
            req.UseBinary = true;
            req.KeepAlive = false;
            req.Timeout = 15000;
            req.ReadWriteTimeout = 30000;
            req.Proxy = null;
            return req;
        }

        private const string LoginHelp = "The console did not accept that user name and password.\n\nAurora uses xboxftp / xboxftp unless you changed it (Aurora > Settings > FTP). FreeStyle Dash and XeXMenu use xbox / xbox.";

        public async Task<List<RemoteItem>> ListAsync(string path, CancellationToken ct = default(CancellationToken))
        {
            try { return await ListOnceAsync(path, ct); }
            catch (WebException ex) when ((ex.Response as FtpWebResponse)?.StatusCode == FtpStatusCode.NotLoggedIn)
            {
                throw new InvalidOperationException(LoginHelp, ex);
            }
            catch (WebException ex) when ((ex.Response as FtpWebResponse)?.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
            {
                // Measured on a real console (Aurora's FTP server): listing an EMPTY folder is answered with
                // 550, the same as a folder that does not exist. Ask the parent which of the two it is.
                string trimmed = (path ?? "").TrimEnd('/');
                int cut = trimmed.LastIndexOf('/');
                if (cut <= 0) throw;
                string name = trimmed.Substring(cut + 1);
                List<RemoteItem> parent;
                try { parent = await ListOnceAsync(trimmed.Substring(0, cut), ct); }
                catch (WebException) { parent = null; }
                if (parent != null && parent.Any(i => i.IsDirectory && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)))
                    return new List<RemoteItem>();
                throw;
            }
        }

        private Task<List<RemoteItem>> ListOnceAsync(string path, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var req = Create(path.EndsWith("/") ? path : path + "/", WebRequestMethods.Ftp.ListDirectoryDetails);
                var lines = new List<string>();
                using (ct.Register(() => req.Abort()))
                using (var resp = (FtpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null) lines.Add(line);
                }
                var items = new List<RemoteItem>();
                foreach (var l in lines)
                {
                    var item = ParseListLine(l);
                    if (item != null && item.Name != "." && item.Name != "..") items.Add(item);
                }
                return items.OrderByDescending(i => i.IsDirectory)
                            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }, ct);
        }

        private static readonly Regex UnixLine = new Regex(
            @"^(?<perm>[\-dlbcps][\-rwxsStT]{9})\s+\d+\s+\S+\s+\S+\s+(?<size>\d+)\s+(?<mon>\w{3})\s+(?<day>\d{1,2})\s+(?<timeyear>\d{1,2}:\d{2}|\d{4})\s+(?<name>.+)$",
            RegexOptions.Compiled);
        private static readonly Regex DosLine = new Regex(
            @"^(?<date>\d{2}-\d{2}-\d{2,4})\s+(?<time>\d{1,2}:\d{2}\s*[AaPp][Mm])\s+(?<dirOrSize><DIR>|\d+)\s+(?<name>.+)$",
            RegexOptions.Compiled);

        public static RemoteItem ParseListLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("total ")) return null;
            var m = UnixLine.Match(line);
            if (m.Success)
            {
                var item = new RemoteItem
                {
                    Name = m.Groups["name"].Value,
                    IsDirectory = m.Groups["perm"].Value[0] == 'd',
                    Size = long.Parse(m.Groups["size"].Value)
                };
                if (m.Groups["perm"].Value[0] == 'l')
                {
                    int arrow = item.Name.IndexOf(" -> ", StringComparison.Ordinal);
                    if (arrow > 0) item.Name = item.Name.Substring(0, arrow);
                    item.IsDirectory = true;
                }
                string ty = m.Groups["timeyear"].Value;
                string stamp = m.Groups["mon"].Value + " " + m.Groups["day"].Value + " " +
                               (ty.Contains(":") ? DateTime.Now.Year + " " + ty : ty + " 00:00");
                DateTime dt;
                if (DateTime.TryParseExact(stamp, new[] { "MMM d yyyy HH:mm", "MMM d yyyy H:mm" },
                        CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dt))
                    item.Modified = dt > DateTime.Now.AddDays(2) ? dt.AddYears(-1) : dt;
                return item;
            }
            m = DosLine.Match(line);
            if (m.Success)
            {
                var item = new RemoteItem { Name = m.Groups["name"].Value };
                if (m.Groups["dirOrSize"].Value == "<DIR>") item.IsDirectory = true;
                else item.Size = long.Parse(m.Groups["dirOrSize"].Value);
                DateTime dt;
                if (DateTime.TryParse(m.Groups["date"].Value + " " + m.Groups["time"].Value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out dt))
                    item.Modified = dt;
                return item;
            }
            // Last resort: bare name list (NLST-style servers)
            return new RemoteItem { Name = line.Trim(), IsDirectory = !line.Contains(".") };
        }

        public Task UploadFileAsync(string localPath, string remotePath, IProgress<TransferProgress> progress,
            TransferProgress state, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var req = Create(remotePath, WebRequestMethods.Ftp.UploadFile);
                state.File = Path.GetFileName(localPath);
                using (ct.Register(() => req.Abort()))
                {
                    using (var src = File.OpenRead(localPath))
                    using (var dst = req.GetRequestStream())
                    {
                        var buf = new byte[BufferSize];
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            dst.Write(buf, 0, n);
                            state.Done += n;
                            progress?.Report(state);
                        }
                    }
                    using (req.GetResponse()) { }
                }
            }, ct);
        }

        public Task DownloadFileAsync(string remotePath, string localPath, IProgress<TransferProgress> progress,
            TransferProgress state, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var req = Create(remotePath, WebRequestMethods.Ftp.DownloadFile);
                state.File = Path.GetFileName(localPath);
                string tmp = localPath + ".part";
                try
                {
                    using (ct.Register(() => req.Abort()))
                    using (var resp = (FtpWebResponse)req.GetResponse())
                    using (var src = resp.GetResponseStream())
                    using (var dst = File.Create(tmp))
                    {
                        var buf = new byte[BufferSize];
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            dst.Write(buf, 0, n);
                            state.Done += n;
                            progress?.Report(state);
                        }
                    }
                    if (File.Exists(localPath)) File.Delete(localPath);
                    File.Move(tmp, localPath);
                }
                catch
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    throw;
                }
            }, ct);
        }

        private Task SimpleAsync(string path, string method, string renameTo = null)
        {
            return Task.Run(() =>
            {
                var req = Create(path, method);
                if (renameTo != null) req.RenameTo = renameTo;
                using (req.GetResponse()) { }
            });
        }

        public Task MakeDirectoryAsync(string path) => SimpleAsync(path, WebRequestMethods.Ftp.MakeDirectory);
        public Task DeleteFileAsync(string path) => SimpleAsync(path, WebRequestMethods.Ftp.DeleteFile);
        public Task RemoveDirectoryAsync(string path) => SimpleAsync(path, WebRequestMethods.Ftp.RemoveDirectory);

        /// <summary>Rename within the same folder (most console FTP servers do not support moves).</summary>
        public Task RenameAsync(string path, string newName) => SimpleAsync(path, WebRequestMethods.Ftp.Rename, newName);

        /// <summary>Creates a directory, ignoring "already exists" errors.</summary>
        public async Task EnsureDirectoryAsync(string path)
        {
            try { await MakeDirectoryAsync(path); }
            catch (WebException) { /* usually means it already exists */ }
        }

        /// <summary>Creates every level of a path (e.g. /Hdd1/Content/0000000000000000/4D5307E6).</summary>
        public async Task EnsurePathAsync(string path)
        {
            string current = "";
            foreach (var seg in path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                current += "/" + seg;
                await EnsureDirectoryAsync(current);
            }
        }

        private const int MaxDepth = 64;

        public async Task DeleteRecursiveAsync(string path, bool isDirectory, CancellationToken ct, int depth = 0)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("Folders are nested too deeply on the server: " + path);
            if (!isDirectory) { await DeleteFileAsync(path); return; }
            foreach (var item in await ListAsync(path, ct))
            {
                ct.ThrowIfCancellationRequested();
                await DeleteRecursiveAsync(Combine(path, item.Name), item.IsDirectory, ct, depth + 1);
            }
            await RemoveDirectoryAsync(path);
        }

        public async Task UploadDirectoryAsync(string localDir, string remoteDir, IProgress<TransferProgress> progress,
            TransferProgress state, CancellationToken ct, int depth = 0)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("Folders are nested too deeply: " + localDir);
            await EnsureDirectoryAsync(remoteDir);
            foreach (var file in Directory.GetFiles(localDir))
            {
                ct.ThrowIfCancellationRequested();
                await UploadFileAsync(file, Combine(remoteDir, Path.GetFileName(file)), progress, state, ct);
            }
            foreach (var dir in Directory.GetDirectories(localDir))
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;   // never follow links
                await UploadDirectoryAsync(dir, Combine(remoteDir, Path.GetFileName(dir)), progress, state, ct, depth + 1);
            }
        }

        public async Task<long> MeasureRemoteAsync(string path, CancellationToken ct, int depth = 0)
        {
            if (depth > MaxDepth) return 0;
            long total = 0;
            foreach (var item in await ListAsync(path, ct))
                total += item.IsDirectory ? await MeasureRemoteAsync(Combine(path, item.Name), ct, depth + 1) : item.Size;
            return total;
        }

        public async Task DownloadDirectoryAsync(string remoteDir, string localDir, IProgress<TransferProgress> progress,
            TransferProgress state, CancellationToken ct, int depth = 0)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("Folders are nested too deeply on the server: " + remoteDir);
            Directory.CreateDirectory(localDir);
            foreach (var item in await ListAsync(remoteDir, ct))
            {
                ct.ThrowIfCancellationRequested();
                string remote = Combine(remoteDir, item.Name);
                string local = PathSafety.CombineUnder(localDir, SafeLocalName(item.Name));
                if (item.IsDirectory) await DownloadDirectoryAsync(remote, local, progress, state, ct, depth + 1);
                else await DownloadFileAsync(remote, local, progress, state, ct);
            }
        }

        /// <summary>Single safe local file name for an untrusted remote name (no traversal, no device names).</summary>
        public static string SafeLocalName(string name) => PathSafety.SafeName(name);

        public static long MeasureLocal(string path)
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                string d = stack.Pop();
                foreach (var f in Directory.GetFiles(d)) total += new FileInfo(f).Length;
                foreach (var sub in Directory.GetDirectories(d))
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0) stack.Push(sub);
            }
            return total;
        }
    }
}
