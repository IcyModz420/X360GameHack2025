using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Net
{
    public sealed class ConsoleSummary
    {
        public string Name = "";
        public string Ip = "";
        public string ConsoleType = "";
        public string ConsoleId = "";
        public string CpuKey = "";
        public string DmVersion = "";
        public string Kernel = "";
        public string Platform = "";
        public string Motherboard = "";
        public string RunningTitleId = "";
        public string SystemInfoRaw = "";
        public string ExecutionState = "";
    }

    /// <summary>
    /// Single shared XBDM connection (port 730). The xbdm socket is strictly request/response,
    /// so every operation goes through one gate to keep transfers, peeks and freezes from interleaving.
    /// </summary>
    public static class ConsoleService
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static XboxConsole _console;

        public static event Action StateChanged;

        public static bool IsConnected => _console != null && _console.Connected;
        public static string Ip { get; private set; } = "";
        public static string Name { get; private set; } = "";

        private static void RaiseState()
        {
            try { StateChanged?.Invoke(); } catch { }
        }

        public static async Task<bool> ConnectAsync(string ip)
        {
            ip = (ip ?? "").Trim();
            if (ip.Length == 0) throw new ArgumentException("Enter the console IP address.");
            await Gate.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    try { _console?.Dispose(); } catch { }
                    _console = null;
                    LastConnectProblem = "";
                    // ONE connection, kept for everything (XBDM, JRPC2 and XRPC commands all go over it). No test connection
                    // first: every extra connect uses up one of the few the console's debug service can hold.
                    var c = new XboxConsole { IPAddress = ip, Port = XboxClient.DefaultPort };
                    try { c.Client.Connect(ip, XboxClient.DefaultPort); }
                    catch (Exception ex)
                    {
                        LastConnectProblem = WhyNoConnection(ip, ex);
                        Log.Warn("XBDM connect to " + ip + " failed: " + LastConnectProblem.Replace("\n", " "));
                        try { c.Dispose(); } catch { }
                        return false;
                    }
                    if (!c.Connected)
                    {
                        LastConnectProblem = "The console at " + ip + " accepted the connection on port 730 but did not answer like XBDM.";
                        Log.Warn("XBDM connect to " + ip + " failed: " + LastConnectProblem);
                        try { c.Dispose(); } catch { }
                        return false;
                    }
                    _console = c;
                    Ip = ip;
                    try { Name = c.Name ?? ""; } catch { Name = ""; }
                    return true;
                });
            }
            catch (Exception ex)
            {
                _console = null;
                Log.Error("XBDM connect to " + ip + " failed: " + ex.Message);
                return false;
            }
            finally
            {
                Gate.Release();
                RaiseState();
                if (IsConnected)
                {
                    Log.Success("Connected to " + (Name.Length > 0 ? Name + " " : "") + "(" + Ip + ") over XBDM.");
                    AppSettings.Current.XbdmIp = Ip;
                    AppSettings.Save();
                }
            }
        }

        /// <summary>Why the last connect attempt failed, in plain words ("" when it worked).</summary>
        public static string LastConnectProblem { get; private set; } = "";

        /// <summary>Why the one connect attempt failed, in plain words: nothing at that address, or a console without XBDM.</summary>
        private static string WhyNoConnection(string ip, Exception ex)
        {
            var se = ex as System.Net.Sockets.SocketException ?? ex.InnerException as System.Net.Sockets.SocketException;
            if (se != null && se.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused)
                return "Something answers at " + ip + " but XBDM is not running there (port 730 is closed).\n" +
                       "Load xbdm.xex as a DashLaunch plugin (Console Setup > launch.ini), then restart the console once.";
            System.Net.IPAddress addr;
            if (System.Net.IPAddress.TryParse(ip, out addr)) return "Nothing answers at " + ip + ".\n" + NetworkHint(addr);
            return "Could not reach " + ip + ": " + ex.Message;
        }

        /// <summary>Looks at this PC's own network to say why a console address may be dead.</summary>
        private static string NetworkHint(System.Net.IPAddress addr)
        {
            try
            {
                byte[] a = addr.GetAddressBytes();
                bool sameNet = false, sharingNet = false;
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (var u in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (u.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || u.IPv4Mask == null) continue;
                        byte[] m = u.IPv4Mask.GetAddressBytes(), l = u.Address.GetAddressBytes();
                        bool same = true;
                        for (int i = 0; i < 4; i++) if ((l[i] & m[i]) != (a[i] & m[i])) same = false;
                        if (!same) continue;
                        sameNet = true;
                        if (l[0] == 192 && l[1] == 168 && l[2] == 137 && l[3] == 1) sharingNet = true;
                    }
                }
                if (!sameNet)
                    return "This PC has no network connection in the same range as that address, so it cannot reach it. " +
                           "Check the cable or Wi-Fi, and read the console's current IP address from its network settings.";
                if (sharingNet)
                    return "The console is plugged into this PC and gets its address from Windows internet sharing. " +
                           "Windows often leaves sharing off after the PC restarts, and then the console has no address.\n" +
                           "Fix: Windows Settings > Network & internet > Advanced network settings > More network adapter options, " +
                           "right-click the adapter that has internet > Properties > Sharing, untick \"Allow other network users to connect\", " +
                           "press OK, then tick it again and pick the adapter the console is plugged into. " +
                           "Then on the console run Settings > System > Network Settings > Test Xbox LIVE Connection so it asks for an address again. " +
                           "If sharing is already on, the console may be off or its address has changed.";
                return "The console may be off, or its IP address has changed. " +
                       "Read the current address on the console (Settings > System > Network Settings > Configure Network), or press Find consoles.";
            }
            catch { return "The console may be off, or its IP address has changed."; }
        }

        public static void Disconnect()
        {
            var c = _console;
            _console = null;
            try { c?.Dispose(); } catch { }
            Log.Info("XBDM disconnected.");
            RaiseState();
        }

        /// <summary>Runs work against the console on a background thread, serialized with all other calls.</summary>
        public static async Task<T> RunAsync<T>(Func<XboxConsole, T> work, CancellationToken ct = default(CancellationToken))
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected to a console. Connect on the Neighborhood page first.");
            await Gate.WaitAsync(ct);
            try
            {
                var c = _console;   // may have been disconnected while we waited for the gate
                if (c == null || !c.Connected) throw new InvalidOperationException("Not connected to a console. Connect on the Neighborhood page first.");
                return await Task.Run(() => work(c), ct);
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                Log.Error("Lost connection to the console: " + ex.Message);
                try { _console?.Dispose(); } catch { }
                _console = null;
                RaiseState();
                throw;
            }
            finally
            {
                Gate.Release();
                if (_console != null && !_console.Connected) RaiseState();
            }
        }

        public static Task RunAsync(Action<XboxConsole> work, CancellationToken ct = default(CancellationToken))
            => RunAsync<bool>(c => { work(c); return true; }, ct);

        private static bool IsTransportFailure(Exception ex)
        {
            return ex is IOException || ex is System.Net.Sockets.SocketException || ex is ObjectDisposedException ||
                   ex is TimeoutException || (ex.InnerException != null && IsTransportFailure(ex.InnerException));
        }

        /// <summary>UDP name-service broadcast first (instant), then a TCP sweep of the local subnets.</summary>
        public static Task<List<string>> DiscoverAsync(bool deepScan)
        {
            return Task.Run(() =>
            {
                var found = new List<string>();
                try
                {
                    foreach (var c in XboxClient.FindConsole(1500))
                        found.Add(c.IPAddress + (string.IsNullOrEmpty(c.Name) ? "" : "  -  " + c.Name));
                }
                catch (Exception ex) { Log.Warn("Name-service broadcast failed: " + ex.Message); }
                if (found.Count == 0 && deepScan)
                {
                    try { found.AddRange(XboxClient.FindAllConsolesOnLan(150)); }
                    catch (Exception ex) { Log.Warn("LAN sweep failed: " + ex.Message); }
                }
                return found.Distinct().ToList();
            });
        }

        public static Task<ConsoleSummary> GetSummaryAsync()
        {
            return RunAsync(c =>
            {
                var s = new ConsoleSummary { Ip = Ip };
                s.Name = Safe(() => c.Name);
                s.ConsoleType = Safe(() => c.ConsoleType.ToString());
                s.ConsoleId = Safe(c.GetConsoleID);
                s.CpuKey = Safe(c.GetCpuKey);
                s.DmVersion = Safe(c.GetDMVersion);
                s.Kernel = Safe(() => c.GetSystemInfo(SystemInfo.KrnlVersion));
                s.Platform = Safe(() => c.GetSystemInfo(SystemInfo.Platform));
                s.Motherboard = Safe(() => c.GetSystemInfo(SystemInfo.System));
                s.ExecutionState = Safe(() => c.ExecutionState.ToString());
                s.RunningTitleId = Safe(() => { uint t = c.GetTitleId(); return t == 0 ? "" : t.ToString("X8"); });
                s.SystemInfoRaw = Safe(c.GetSystemInfoRaw);
                return s;
            });
        }

        private static string Safe(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }

        // ------------------------------------------------------------------ file system

        public static string Combine(string dir, string name)
        {
            if (string.IsNullOrEmpty(dir)) return name;
            return dir.EndsWith("\\") ? dir + name : dir + "\\" + name;
        }

        public static string Parent(string path)
        {
            string p = (path ?? "").TrimEnd('\\');
            int i = p.LastIndexOf('\\');
            if (i < 0) return p.EndsWith(":") ? p + "\\" : p;
            string parent = p.Substring(0, i);
            return parent.EndsWith(":") ? parent + "\\" : parent;
        }

        public static Task<List<RemoteItem>> ListAsync(string path)
        {
            return RunAsync(c => c.File.DirList(path)
                .Select(e => new RemoteItem { Name = e.Name, IsDirectory = e.IsDirectory, Size = (long)e.Size, Modified = e.ChangeTime })
                .OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList());
        }

        public static Task<List<XboxDrive>> GetDrivesAsync() => RunAsync(c => c.GetDrives());

        /// <summary>Upload one file with progress. Must be called inside RunAsync (holds the gate).</summary>
        private static void SendFile(XboxConsole c, string local, string remote, IProgress<TransferProgress> progress,
            TransferProgress state, CancellationToken ct)
        {
            long total = new FileInfo(local).Length;
            if (total > uint.MaxValue) throw new InvalidOperationException(Path.GetFileName(local) + " is larger than 4 GB (FAT32/XBDM limit).");
            state.File = Path.GetFileName(local);
            using (var fs = File.OpenRead(local))
            {
                var resp = c.SendTextCommand("sendfile name=" + XboxExtensions.QuoteXbdm(remote) + " length=" + total);
                if ((int)resp.Status != 204)
                    throw new InvalidOperationException($"Console refused upload of {remote}: {(int)resp.Status} {resp.StatusMessage}");
                if (total == 0)
                {
                    // Measured on a real console (xbdm 2.0.21076): after "204 send binary data" for an
                    // empty file the file exists, but xbdm keeps waiting for data and never sends the
                    // closing status. A fresh connection is the only clean way out; the file stays.
                    if (!c.Reconnect()) throw new IOException("Lost the connection to the console after creating the empty file " + remote + ".");
                    return;
                }
                var buf = new byte[XboxClient.FileTransferBufferSize];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    if (ct.IsCancellationRequested)
                    {
                        // The protocol cannot be aborted mid-stream; drop the socket so the next call reconnects.
                        try { c.Disconnect(); } catch { }
                        ct.ThrowIfCancellationRequested();
                    }
                    c.Client.SendRaw(buf, 0, n);
                    state.Done += n;
                    progress?.Report(state);
                }
            }
            var post = c.Client.ReceiveStatusResponse();
            if (!post.IsSuccess)
                throw new InvalidOperationException($"Upload of {remote} failed: {(int)post.Status} {post.StatusMessage}");
        }

        private static void GetFile(XboxConsole c, string remote, string local, IProgress<TransferProgress> progress,
            TransferProgress state, CancellationToken ct)
        {
            var resp = c.SendGetFile("getfile name=" + XboxExtensions.QuoteXbdm(remote), ct);
            if ((int)resp.Status != 203)
                throw new InvalidOperationException($"Console refused download of {remote}: {(int)resp.Status} {resp.StatusMessage}");
            // little-endian: measured on a real console (a 6,291,579 byte file is announced as 7B 00 60 00)
            uint len = BitConverter.ToUInt32(c.Client.ReadExact(4), 0);
            state.File = Path.GetFileName(local);
            string tmp = local + ".part";
            try
            {
                using (var fs = File.Create(tmp))
                {
                    var buf = new byte[XboxClient.FileTransferBufferSize];
                    long remaining = len;
                    while (remaining > 0)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            try { c.Disconnect(); } catch { }
                            ct.ThrowIfCancellationRequested();
                        }
                        int chunk = (int)Math.Min(buf.Length, remaining);
                        c.Client.ReadExact(buf, 0, chunk);
                        fs.Write(buf, 0, chunk);
                        remaining -= chunk;
                        state.Done += chunk;
                        progress?.Report(state);
                    }
                }
                if (File.Exists(local)) File.Delete(local);
                File.Move(tmp, local);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        private const int MaxDepth = 64;

        private static void SendDirectory(XboxConsole c, string localDir, string remoteDir, IProgress<TransferProgress> progress,
            TransferProgress state, CancellationToken ct, int depth = 0)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("Folders are nested too deeply: " + localDir);
            c.File.MakeDirectory(remoteDir);
            foreach (var f in Directory.GetFiles(localDir))
            {
                ct.ThrowIfCancellationRequested();
                SendFile(c, f, Combine(remoteDir, Path.GetFileName(f)), progress, state, ct);
            }
            foreach (var d in Directory.GetDirectories(localDir))
            {
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;   // never follow links
                SendDirectory(c, d, Combine(remoteDir, Path.GetFileName(d)), progress, state, ct, depth + 1);
            }
        }

        private static void GetDirectory(XboxConsole c, string remoteDir, string localDir, IProgress<TransferProgress> progress,
            TransferProgress state, CancellationToken ct, int depth = 0)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("Console folders are nested too deeply: " + remoteDir);
            Directory.CreateDirectory(localDir);
            foreach (var e in c.File.DirList(remoteDir))
            {
                ct.ThrowIfCancellationRequested();
                if (e.Name == "." || e.Name == ".." || e.Name.Length == 0) continue;
                string remote = Combine(remoteDir, e.Name);
                string local = PathSafety.CombineUnder(localDir, PathSafety.SafeName(e.Name));
                if (e.IsDirectory) GetDirectory(c, remote, local, progress, state, ct, depth + 1);
                else GetFile(c, remote, local, progress, state, ct);
            }
        }

        private static long MeasureRemote(XboxConsole c, string remoteDir, int depth = 0)
        {
            if (depth > MaxDepth) return 0;
            long total = 0;
            foreach (var e in c.File.DirList(remoteDir))
            {
                if (e.Name == "." || e.Name == "..") continue;
                total += e.IsDirectory ? MeasureRemote(c, Combine(remoteDir, e.Name), depth + 1) : (long)e.Size;
            }
            return total;
        }

        private static void DeleteRecursive(XboxConsole c, string path, bool isDir, CancellationToken ct, int depth = 0)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("Console folders are nested too deeply: " + path);
            if (isDir)
            {
                foreach (var e in c.File.DirList(path))
                {
                    ct.ThrowIfCancellationRequested();
                    if (e.Name == "." || e.Name == ".." || e.Name.Length == 0) continue;
                    DeleteRecursive(c, Combine(path, e.Name), e.IsDirectory, ct, depth + 1);
                }
            }
            var r = c.SendTextCommand("delete name=" + XboxExtensions.QuoteXbdm(path) + (isDir ? " dir" : ""));
            if (!r.IsSuccess) throw new InvalidOperationException($"Delete {path} failed: {(int)r.Status} {r.StatusMessage}");
        }

        /// <summary>Upload local files and folders into a remote folder.</summary>
        public static Task UploadAsync(IEnumerable<string> localPaths, string remoteDir, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            var paths = localPaths.ToList();
            return RunAsync(c =>
            {
                var state = new TransferProgress { Total = paths.Sum(FtpService.MeasureLocal) };
                foreach (var p in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    string target = Combine(remoteDir, Path.GetFileName(p.TrimEnd('\\')));
                    if (Directory.Exists(p)) SendDirectory(c, p, target, progress, state, ct);
                    else SendFile(c, p, target, progress, state, ct);
                    Log.Success("Sent " + Path.GetFileName(p.TrimEnd('\\')) + " -> " + target);
                }
            }, ct);
        }

        /// <summary>Download remote items (files or folders) into a local folder.</summary>
        public static Task DownloadAsync(IEnumerable<RemoteItem> items, string remoteDir, string localDir,
            IProgress<TransferProgress> progress, CancellationToken ct)
        {
            var list = items.ToList();
            return RunAsync(c =>
            {
                var state = new TransferProgress();
                foreach (var it in list)
                    state.Total += it.IsDirectory ? MeasureRemote(c, Combine(remoteDir, it.Name)) : it.Size;
                foreach (var it in list)
                {
                    ct.ThrowIfCancellationRequested();
                    string remote = Combine(remoteDir, it.Name);
                    string local = PathSafety.CombineUnder(localDir, PathSafety.SafeName(it.Name));
                    if (it.IsDirectory) GetDirectory(c, remote, local, progress, state, ct);
                    else GetFile(c, remote, local, progress, state, ct);
                    Log.Success("Received " + remote + " -> " + local);
                }
            }, ct);
        }

        public static Task DeleteAsync(IEnumerable<RemoteItem> items, string remoteDir, CancellationToken ct)
        {
            var list = items.ToList();
            return RunAsync(c =>
            {
                foreach (var it in list)
                {
                    DeleteRecursive(c, Combine(remoteDir, it.Name), it.IsDirectory, ct);
                    Log.Info("Deleted " + Combine(remoteDir, it.Name));
                }
            }, ct);
        }

        public static Task RenameAsync(string remotePath, string newPath)
        {
            return RunAsync(c =>
            {
                var r = c.SendTextCommand("rename name=" + XboxExtensions.QuoteXbdm(remotePath) + " newname=" + XboxExtensions.QuoteXbdm(newPath));
                if (!r.IsSuccess) throw new InvalidOperationException($"Rename failed: {(int)r.Status} {r.StatusMessage}");
            });
        }

        public static Task MakeDirectoryAsync(string remotePath)
        {
            return RunAsync(c =>
            {
                var r = c.SendTextCommand("mkdir name=" + XboxExtensions.QuoteXbdm(remotePath));
                if (!r.IsSuccess) throw new InvalidOperationException($"Create folder failed: {(int)r.Status} {r.StatusMessage}");
            });
        }

        /// <summary>Creates every folder level of a path like Hdd:\Content\0000000000000000\4D5307E6\00007000.</summary>
        public static void EnsurePath(XboxConsole c, string path)
        {
            int colon = path.IndexOf(":\\", StringComparison.Ordinal);
            if (colon < 0) return;
            string current = path.Substring(0, colon + 2);
            foreach (var seg in path.Substring(colon + 2).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Combine(current, seg);
                c.SendTextCommand("mkdir name=" + XboxExtensions.QuoteXbdm(current)); // exists -> harmless error
            }
        }

        /// <summary>Upload a file or folder to an exact remote path, creating parent folders.</summary>
        public static Task UploadToPathAsync(string local, string remotePath, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            return RunAsync(c =>
            {
                var state = new TransferProgress { Total = FtpService.MeasureLocal(local) };
                EnsurePath(c, Parent(remotePath));
                if (Directory.Exists(local)) SendDirectory(c, local, remotePath, progress, state, ct);
                else SendFile(c, local, remotePath, progress, state, ct);
            }, ct);
        }

        /// <summary>Downloads one file to an exact local path (written to a .part file first).</summary>
        public static Task DownloadFileAsync(string remote, string local, IProgress<TransferProgress> progress, CancellationToken ct)
            => RunAsync(c => GetFile(c, remote, local, progress, new TransferProgress(), ct), ct);

        /// <summary>The first bytes of a remote file (e.g. an STFS header) without downloading it.</summary>
        public static Task<byte[]> ReadHeadAsync(string remote, int count, CancellationToken ct)
            => RunAsync(c => c.ReadFilePartial(remote, 0, (uint)count), ct);

        public static Task DeleteFileAsync(string remote, CancellationToken ct)
            => RunAsync(c => DeleteRecursive(c, remote, false, ct), ct);

        /// <summary>
        /// Starts a program on the console: an .xex through plain XBDM ("magicboot title=..."); a package (Games on Demand,
        /// arcade, original Xbox) through <see cref="ConsoleExtras.LaunchPackage"/> - XBDM cannot mount a package, so that needs
        /// JRPC2 / XRPC on an RGH or the XDRPC of a development kit.
        /// </summary>
        public static Task LaunchAsync(string remotePath)
        {
            return RunAsync(c =>
            {
                if (remotePath.EndsWith(".xex", StringComparison.OrdinalIgnoreCase) || remotePath.EndsWith(".xbe", StringComparison.OrdinalIgnoreCase))
                {
                    c.Reboot(remotePath, Parent(remotePath), null, XboxRebootFlags.Title);
                    return;
                }
                byte[] head = c.ReadFilePartial(remotePath, 0, 0x400) ?? new byte[0];
                string magic = head.Length >= 4 ? System.Text.Encoding.ASCII.GetString(head, 0, 4) : "";
                if (head.Length < 0x348 || (magic != "CON " && magic != "LIVE" && magic != "PIRS"))
                    throw new InvalidOperationException(remotePath + " is neither an .xex nor a package (Games on Demand / arcade) - nothing to start.");
                uint type = Formats.BE.U32(head, 0x344);
                if (type == 0x00000001 || type == 0x00010000) throw new InvalidOperationException("That package is a " + (type == 1 ? "saved game" : "profile") + " - it cannot be started.");
                ConsoleExtras.LaunchPackage(c, remotePath, type == 0x00005000 ? "default.xbe" : "default.xex");
            });
        }

        /// <summary>
        /// An XBDM path (Hdd:\Content\...) as the console's kernel names it (\Device\Harddisk0\Partition1\Content\...), for the
        /// drives games are installed on; a path that already starts with \Device\ is kept; null for any other drive.
        /// </summary>
        public static string DevicePathOf(string drivePath)
        {
            if (string.IsNullOrEmpty(drivePath)) return null;
            if (drivePath.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) return drivePath;
            int colon = drivePath.IndexOf(':');
            if (colon <= 0) return null;
            string rest = drivePath.Substring(colon + 1).TrimStart('\\');
            string root;
            switch (drivePath.Substring(0, colon).ToUpperInvariant())
            {
                case "HDD": root = @"\Device\Harddisk0\Partition1"; break;
                case "USB0": root = @"\Device\Mass0"; break;
                case "USB1": root = @"\Device\Mass1"; break;
                case "USB2": root = @"\Device\Mass2"; break;
                case "INTUSB": root = @"\Device\BuiltInMuUsb\Storage"; break;
                case "INTMMC": root = @"\Device\BuiltInMuMmc\Storage"; break;
                case "DEVKIT": root = @"\Device\Harddisk0\Partition1\DEVKIT"; break;
                default: return null;
            }
            return rest.Length == 0 ? root : root + "\\" + rest;
        }

        /// <summary>
        /// Starts the running program again from its file, the same way as <see cref="LaunchAsync"/>. ("magicboot warm" on
        /// its own restarted nothing on a Trinity RGH3 on 2026-11-18: the game ran on with its memory untouched.)
        /// Returns the path that was started.
        /// </summary>
        public static Task<string> RestartTitleAsync()
        {
            return RunAsync(c =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(c.GetXbeInfo() ?? "", "name=\"([^\"]*)\"");
                if (!m.Success) throw new InvalidOperationException("The console did not say which program is running.");
                string path = DrivePathOf(m.Groups[1].Value);
                if (path == null)
                    throw new InvalidOperationException("The running program is " + m.Groups[1].Value + " - only a game on the hard drive or a USB drive can be started again from here. Use Reboot (cold) instead.");
                c.Reboot(path, Parent(path), null, XboxRebootFlags.Title);
                return path;
            });
        }

        /// <summary>A file's name as the console's kernel gives it (\Device\Harddisk0\Partition1\...) as an XBDM path (Hdd:\...); null for other drives.</summary>
        internal static string DrivePathOf(string devicePath)
        {
            var map = new[]
            {
                new[] { @"\Device\Harddisk0\Partition1\", @"Hdd:\" },
                new[] { @"\Device\Mass0\", @"Usb0:\" }, new[] { @"\Device\Mass1\", @"Usb1:\" }, new[] { @"\Device\Mass2\", @"Usb2:\" },
            };
            foreach (var m in map)
                if (devicePath != null && devicePath.StartsWith(m[0], StringComparison.OrdinalIgnoreCase) && devicePath.Length > m[0].Length)
                    return m[1] + devicePath.Substring(m[0].Length);
            return null;
        }
    }
}
