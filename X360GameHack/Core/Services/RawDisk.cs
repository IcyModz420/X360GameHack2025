using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace X360GameHack.Core.Services
{
    public sealed class DiskInfo
    {
        public int Index;
        public string Model = "";
        public string Interface = "";
        public long Size;
        public bool IsSystem;
        /// <summary>How many partitions Windows sees on the disk (an Xbox drive: none).</summary>
        public int Partitions;
        public override string ToString() =>
            $"Disk {Index}: {Model}  ({Formats.FileSniffer.FormatSize(Size)}, {Interface}){(IsSystem ? "  - Windows disk" : "")}";
    }

    /// <summary>
    /// Physical-disk access through the elevated, read-only X360GameHack Disk Reader helper.
    /// The helper is only ever started from a protected (installed) location.
    /// </summary>
    public static class RawDisk
    {
        /// <summary>The native launcher Windows elevates (UAC prompt "X360GameHack Disk Reader").</summary>
        public static string HelperPath => Path.Combine(Tools.AppDir, "X360GameHack.DiskReader.exe");

        /// <summary>The Disk Reader itself, which the launcher starts with a clean environment and which connects to the pipe.</summary>
        public static string CorePath => Path.Combine(Tools.AppDir, "X360GameHack.DiskReaderCore.exe");

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder name, ref int size);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W
        {
            public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        /// <summary>The full path of a running program (any rights level), or null.</summary>
        internal static string ProcessImagePath(int pid)
        {
            IntPtr p = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (p == IntPtr.Zero) return null;
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                int n = sb.Capacity;
                return QueryFullProcessImageName(p, 0, sb, ref n) ? sb.ToString(0, n) : null;
            }
            finally { CloseHandle(p); }
        }

        /// <summary>The process id of whoever started <paramref name="pid"/>, or 0.</summary>
        internal static int ParentProcessId(int pid)
        {
            IntPtr snap = CreateToolhelp32Snapshot(0x2 /* TH32CS_SNAPPROCESS */, 0);
            if (snap == new IntPtr(-1)) return 0;
            try
            {
                var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W)) };
                for (bool more = Process32FirstW(snap, ref e); more; more = Process32NextW(snap, ref e))
                    if (e.th32ProcessID == (uint)pid) return (int)e.th32ParentProcessID;
                return 0;
            }
            finally { CloseHandle(snap); }
        }

        /// <summary>
        /// True when the process on the other end of the pipe is the Disk Reader (X360GameHack.DiskReaderCore.exe in the
        /// program folder) that the launcher this program started (<paramref name="launcherPid"/>) started in turn.
        /// </summary>
        internal static bool IsOurDiskReader(int clientPid, int launcherPid)
        {
            string image = ProcessImagePath(clientPid);
            return image != null && string.Equals(Path.GetFullPath(image), Path.GetFullPath(CorePath), StringComparison.OrdinalIgnoreCase) &&
                   ParentProcessId(clientPid) == launcherPid;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);

        /// <summary>
        /// True when the program folder cannot be modified by the current (non-admin) user - i.e. it is installed
        /// under Program Files. Only then is it safe to run an elevated helper from it.
        /// </summary>
        public static bool AppDirIsProtected()
        {
            string probe = Path.Combine(Tools.AppDir, ".write-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return false;   // we could write here, so could malware
            }
            catch (UnauthorizedAccessException) { return true; }
            catch (IOException) { return false; }
        }

        private static int SystemDiskNumber()
        {
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                using (var v = CreateFile(@"\\.\" + root.Substring(0, 2), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    if (v.IsInvalid) return -1;
                    var o = new byte[12];
                    return DeviceIoControl(v, 0x002D1080, IntPtr.Zero, 0, o, 12, out _, IntPtr.Zero) ? BitConverter.ToInt32(o, 4) : -1;
                }
            }
            catch { return -1; }
        }

        /// <summary>
        /// The disk's exact size in bytes (IOCTL_DISK_GET_DRIVE_GEOMETRY_EX on a handle that may neither read nor write - no
        /// administrator rights needed), or -1. Windows' own disk list rounds the size down to whole cylinders; a layout
        /// that runs to the end of the drive needs the real one.
        /// </summary>
        public static long ExactSize(int index)
        {
            try
            {
                using (var h = CreateFile(@"\\.\PhysicalDrive" + index, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) return -1;
                    var o = new byte[256];
                    if (!DeviceIoControl(h, 0x000700A0, IntPtr.Zero, 0, o, o.Length, out int got, IntPtr.Zero) || got < 32) return -1;
                    return BitConverter.ToInt64(o, 24);              // DISK_GEOMETRY_EX: the 24-byte DISK_GEOMETRY, then DiskSize
                }
            }
            catch { return -1; }
        }

        /// <summary>Lists physical disks (no admin rights needed). The Windows disk is flagged and cannot be opened.</summary>
        public static List<DiskInfo> List()
        {
            var list = new List<DiskInfo>();
            int sys = SystemDiskNumber();
            using (var q = new ManagementObjectSearcher("SELECT Index, Model, Size, InterfaceType, Partitions FROM Win32_DiskDrive"))
                foreach (ManagementObject d in q.Get())
                {
                    var info = new DiskInfo
                    {
                        Index = Convert.ToInt32(d["Index"]),
                        Model = (d["Model"] as string ?? "").Trim(),
                        Interface = d["InterfaceType"] as string ?? "",
                        Size = d["Size"] == null ? 0 : Convert.ToInt64(d["Size"]),
                        Partitions = d["Partitions"] == null ? 0 : Convert.ToInt32(d["Partitions"])
                    };
                    info.IsSystem = info.Index == sys || sys < 0 && info.Index == 0;
                    list.Add(info);
                }
            list.Sort((a, b) => a.Index.CompareTo(b.Index));
            return list;
        }

        /// <summary>
        /// Starts the helper (UAC prompt) and returns a stream over the disk - read-only, or with <paramref name="writable"/>
        /// one the helper lets write only inside the disk's Xbox partitions, keeping an undo journal of every change.
        /// <paramref name="format"/> ("og" / "x360", with <paramref name="writable"/>) opens it to be made into an Xbox drive:
        /// a blank drive is accepted too, and the helper lets write only where that console's layout has something to write
        /// (journaled the same way, so the format can be undone).
        /// </summary>
        public static async Task<ElevatedDiskStream> OpenAsync(int diskIndex, CancellationToken ct, bool writable = false, string format = null)
        {
            if (format != null && (!writable || (format != "og" && format != "x360"))) throw new ArgumentException("format is og or x360, with writable.");
            if (!AppDirIsProtected())
                throw new UnauthorizedAccessException("Raw disk access is only available when X360GameHack is installed (Program Files).");
            if (!File.Exists(HelperPath) || !File.Exists(CorePath)) throw new FileNotFoundException("The Disk Reader helper is missing - reinstall X360GameHack.", HelperPath);

            string pipeName = "X360GameHack-disk-" + Guid.NewGuid().ToString("N");
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                1 << 20, 1 << 20, security);
            Process helper;
            try
            {
                helper = Process.Start(new ProcessStartInfo(HelperPath,
                    "--pipe " + pipeName + " --disk " + diskIndex + " --parent " + Process.GetCurrentProcess().Id + (writable ? " --write" : "") +
                    (format != null ? " --format " + format : ""))
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Tools.AppDir
                });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                server.Dispose();
                throw new OperationCanceledException("Administrator permission was not granted.");
            }
            if (helper == null) { server.Dispose(); throw new InvalidOperationException("The Disk Reader could not be started."); }

            try
            {
                var connect = server.WaitForConnectionAsync(ct);
                var exited = Task.Run(() => helper.WaitForExit(120000));
                if (await Task.WhenAny(connect, exited) != connect)
                {
                    int code = helper.HasExited ? helper.ExitCode : -1;
                    throw new InvalidOperationException(ExitReason(code));
                }
                await connect;
                if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out uint pid) || pid > int.MaxValue || !IsOurDiskReader((int)pid, helper.Id))
                    throw new InvalidOperationException("An unexpected process connected to the disk pipe - aborted for safety.");
                var stream = new ElevatedDiskStream(server, helper);
                stream.ReadHello();
                return stream;
            }
            catch
            {
                server.Dispose();
                try { if (!helper.HasExited) helper.Kill(); } catch { }
                helper.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Safely removes an Xbox drive, also from a port Windows does not call removable (an internal SATA port): the Disk
        /// Reader flushes the drive's cache, spins it down (heads parked) and has Windows remove the disk device, so it can be
        /// unplugged. Nothing is written to the drive. The drive must not be open (close it in Drive Explorer first).
        /// Null when done; otherwise why not.
        /// </summary>
        public static async Task<string> EjectAsync(int diskIndex)
        {
            if (!AppDirIsProtected()) return "Safely removing a drive is only available when X360GameHack is installed (Program Files).";
            if (!File.Exists(HelperPath) || !File.Exists(CorePath)) return "The Disk Reader helper is missing - reinstall X360GameHack.";
            Process helper;
            try
            {
                helper = Process.Start(new ProcessStartInfo(HelperPath, "--eject --disk " + diskIndex + " --parent " + Process.GetCurrentProcess().Id)
                { UseShellExecute = true, Verb = "runas", WorkingDirectory = Tools.AppDir });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return "Administrator permission was not granted - the drive was left as it is."; }
            if (helper == null) return "The Disk Reader could not be started.";
            using (helper)
            {
                bool done = await Task.Run(() => helper.WaitForExit(90000));
                if (!done) { try { helper.Kill(); } catch { } return "The drive did not answer in time."; }
                switch (helper.ExitCode)
                {
                    case 0: return null;
                    case 61: return "The drive's cache could not be flushed - do not unplug it; try again.";
                    case 62: return "Windows does not list that drive any more (it may be unplugged already).";
                    case 63: return "Something still has the drive open - close it in Drive Explorer (and any other program using it), then try again.";
                    case 64: return "Windows did not let the drive be removed. Shut the PC down before unplugging it.";
                    default: return ExitReason(helper.ExitCode);
                }
            }
        }

        /// <summary>
        /// Brings back drives that were safely removed but are still connected - Windows keeps such a drive parked, and a
        /// hardware scan alone does not start it again - and has Windows look for drives connected since. The Disk Reader
        /// reads and writes no disk for this. Null when done; otherwise why not.
        /// </summary>
        public static async Task<string> RemountAsync()
        {
            if (!AppDirIsProtected()) return "Bringing a drive back is only available when X360GameHack is installed (Program Files).";
            if (!File.Exists(HelperPath) || !File.Exists(CorePath)) return "The Disk Reader helper is missing - reinstall X360GameHack.";
            Process helper;
            try
            {
                helper = Process.Start(new ProcessStartInfo(HelperPath, "--remount --parent " + Process.GetCurrentProcess().Id)
                { UseShellExecute = true, Verb = "runas", WorkingDirectory = Tools.AppDir });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return "Administrator permission was not granted - nothing was changed."; }
            if (helper == null) return "The Disk Reader could not be started.";
            using (helper)
            {
                bool done = await Task.Run(() => helper.WaitForExit(90000));
                if (!done) { try { helper.Kill(); } catch { } return "Windows did not answer in time."; }
                if (helper.ExitCode == 0) { await Task.Delay(1500); return null; }        // the disk shows up a moment after it starts
                if (helper.ExitCode == 71) return "A safely removed drive did not start again - unplug it and plug it in again, or restart the PC.";
                return ExitReason(helper.ExitCode);
            }
        }

        private static string ExitReason(int code)
        {
            switch (code)
            {
                case 14: return "The Disk Reader refused to run (it only works for the X360GameHack.exe installed next to it).";
                case 15: return "The Disk Reader refused to run: it was not started by its own launcher. Reinstall X360GameHack.";
                case 20: return "The disk could not be opened.";
                case 21: return "That is the Windows system disk - it is never opened.";
                case 22: return "The disk size could not be read.";
                case 23: return "No Xbox (FATX / XTAF) file system was found on that disk - it was not opened.";
                case 24: return "That disk has a Windows partition table, so it is never changed. To make it an Xbox drive, first take the table off: " +
                                "copy everything you want to keep, open an administrator Command Prompt, type diskpart, then: list disk, select disk N " +
                                "(the number of THAT drive - check the size), clean. Then try again.";
                case 25: return "The undo folder (ProgramData\\X360GameHack\\DiskUndo) could not be made safe, so changes are not allowed.";
                case 27: return "That disk holds a Windows volume (NTFS, FAT, exFAT...) - the Disk Reader never opens a Windows disk. If it really is an Xbox drive, it is not one Windows should be using: check it in Disk Management.";
                case 26: return "This is a development kit drive. It can be read, recovered from and safely removed; changing it is not allowed yet (not tested on a devkit drive).";
                case 30: return "The disk pipe could not be verified - aborted for safety.";
                case 90: return "The Disk Reader could not be started - reinstall X360GameHack.";
                case 91: return "The Disk Reader will not run with administrator rights while a .NET runtime option is set for your Windows account " +
                                "(values in HKEY_CURRENT_USER\\Software\\Microsoft\\.NETFramework). Such options can load other code into .NET programs; " +
                                "remove them (or ask whoever set them up), then try again.";
                case 92: return "The Disk Reader did not get administrator rights.";
                default: return "The Disk Reader stopped (code " + code + ").";
            }
        }
    }

    /// <summary>
    /// Seekable stream over the Disk Reader helper's protocol, with a small aligned block cache. Read-only, unless the
    /// helper was started in write mode: then writes go to the helper, which accepts them only inside the disk's Xbox
    /// partitions, journals what each sector held first, and reads every write back.
    /// </summary>
    public sealed class ElevatedDiskStream : Stream
    {
        private const int Align = 4096;
        // the disk helper answers one request at a time over the pipe, so each round-trip has real latency. The read-ahead is
        // adaptive: a small block after a seek (so copying thousands of scattered tiny files does not read 4 MB to get 10 KB),
        // doubling while reads stay sequential up to the helper's maximum (so one big file still streams fast).
        private const int MaxRequest = 4 * 1024 * 1024;
        private const int MinAhead = 128 * 1024;
        private const byte OpRead = 1, OpWrite = 2, OpUndo = 3, OpSessions = 4;
        private readonly Stream _pipe;
        private readonly Process _helper;
        private readonly BinaryReader _r;
        private readonly BinaryWriter _w;
        private long _length, _pos, _cacheStart = -1;
        private int _ahead;
        private byte[] _cache = new byte[MaxRequest];
        private int _cacheLen;
        private bool _writable;

        /// <summary>The undo session this connection's changes are recorded in ("" when read-only).</summary>
        public string Session { get; private set; } = "";
        /// <summary>How many 4 KB blocks this connection has changed on the disk.</summary>
        public long BlocksWritten { get; private set; }

        internal ElevatedDiskStream(Stream pipe, Process helper)
        {
            _pipe = pipe;
            _helper = helper;
            _r = new BinaryReader(pipe);
            _w = new BinaryWriter(pipe);
        }

        internal void ReadHello()
        {
            if (_r.ReadUInt32() != 0x44484758) throw new InvalidDataException("Unexpected reply from the Disk Reader.");
            _length = _r.ReadInt64();
            _writable = _r.ReadBoolean();
            Session = _r.ReadString();
            if (_length <= 0 || _length % Align != 0) throw new InvalidDataException("The Disk Reader reported an invalid disk size.");
        }

        // a long read that never comes back (a Cancel click, or a drive that stopped answering) must not hang the copy: while a
        // request is out, a watchdog closes the pipe after ReadTimeoutMs, and Cancel closes it at once - either way the blocked
        // read throws and the copy stops with a clear reason.
        private volatile bool _aborted, _timedOut;
        public int ReadTimeoutMs { get; set; } = 60000;
        public bool Faulted => _aborted;

        /// <summary>While <paramref name="ct"/> is alive, cancelling it abandons any blocked read and ends this connection. Dispose the result to unbind.</summary>
        public IDisposable AbortOn(CancellationToken ct) => ct.Register(() => { _aborted = true; try { _pipe.Dispose(); } catch { } });

        private Exception Stopped() => _timedOut
            ? new TimeoutException("The drive stopped responding (no data for " + (ReadTimeoutMs / 1000) + " seconds). Check its cable and power, then open it again.")
            : (Exception)new OperationCanceledException();

        private int ReadRaw(long off, byte[] into, int len)
        {
            if (_aborted) throw Stopped();
            using (new System.Threading.Timer(_ => { _timedOut = true; _aborted = true; try { _pipe.Dispose(); } catch { } }, null, ReadTimeoutMs, System.Threading.Timeout.Infinite))
            {
                try
                {
                    _w.Write(OpRead);
                    _w.Write(off);
                    _w.Write(len);
                    _w.Flush();
                    int n = _r.ReadInt32();
                    if (n < 0 || n > len) throw new InvalidDataException("Invalid reply from the Disk Reader.");
                    int got = 0;
                    while (got < n) { int k = _r.Read(into, got, n - got); if (k <= 0) throw new EndOfStreamException(); got += k; }
                    return n;
                }
                catch (Exception) when (_aborted) { throw Stopped(); }
            }
        }

        /// <summary>
        /// Loads the read cache for a miss at <paramref name="pos"/>. A miss just past the end of what is cached means the
        /// caller is streaming forward, so the read doubles (up to the helper's 4 MB maximum); any other miss is a seek and reads
        /// only a small block - copying thousands of scattered tiny files must not fetch 4 MB for every 10 KB file.
        /// </summary>
        private void Fill(long pos)
        {
            long cacheEnd = _cacheStart + _cacheLen;
            bool streaming = _cacheStart >= 0 && pos >= cacheEnd && pos - cacheEnd < Math.Max(_ahead, MinAhead);
            _ahead = streaming ? Math.Min(MaxRequest, Math.Max(_ahead, MinAhead) * 2) : MinAhead;
            long start = pos / Align * Align;
            int len = (int)Math.Min(_ahead, _length - start);    // both are whole 4 KB blocks (the disk size is checked to be)
            _cacheStart = -1;                                    // not valid until the read completes
            _cacheLen = ReadRaw(start, _cache, len);
            _cacheStart = start;
        }

        /// <summary>
        /// Writes <paramref name="count"/> bytes at the current position. The helper only takes whole 4 KB blocks, so the
        /// blocks around the bytes are read first and changed in memory. A refused or failed write throws, and nothing of
        /// that request is left half done on the disk side (the helper journals each block before it changes it).
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!_writable) throw new NotSupportedException("This drive was opened read-only.");
            if (count <= 0) return;
            if (_pos < 0 || _pos + count > _length) throw new IOException("The write goes past the end of the disk.");
            lock (_pipe)
            {
                long start = _pos / Align * Align, end = (_pos + count + Align - 1) / Align * Align;
                for (long chunk = start; chunk < end; chunk += MaxRequest)
                {
                    int len = (int)Math.Min(MaxRequest, end - chunk);
                    var block = new byte[len];
                    if (ReadRaw(chunk, block, len) != len) throw new IOException("The disk could not be read before writing.");
                    long from = Math.Max(_pos, chunk), to = Math.Min(_pos + count, chunk + len);
                    Buffer.BlockCopy(buffer, offset + (int)(from - _pos), block, (int)(from - chunk), (int)(to - from));
                    _w.Write(OpWrite);
                    _w.Write(chunk);
                    _w.Write(len);
                    _w.Write(block, 0, len);
                    _w.Flush();
                    int status = _r.ReadInt32();
                    if (status == 1) throw new IOException("The Disk Reader refused to change that part of the drive (outside its Xbox partitions) - nothing was written there.");
                    if (status == 2) throw new IOException("A write did not read back the same - stop and use Undo changes.");
                    if (status != 0) throw new IOException("The drive could not be written (code " + status + ").");
                    BlocksWritten += len / Align;
                    // keep the read cache in step with the disk
                    if (_cacheStart >= 0 && chunk < _cacheStart + _cacheLen && chunk + len > _cacheStart) _cacheStart = -1;
                }
                _pos += count;
            }
        }

        /// <summary>The undo sessions the helper has recorded for this disk, newest first.</summary>
        public List<string> UndoSessions()
        {
            lock (_pipe)
            {
                _w.Write(OpSessions);
                _w.Flush();
                int n = _r.ReadInt32();
                if (n < 0 || n > 10000) throw new InvalidDataException("Invalid reply from the Disk Reader.");
                var list = new List<string>();
                for (int i = 0; i < n; i++) list.Add(_r.ReadString());
                return list;
            }
        }

        /// <summary>
        /// Puts every sector a session changed back as it was (newest change first). Null when done; otherwise why it
        /// stopped. Needs the drive opened with changes allowed.
        /// </summary>
        public string Undo(string session, out int restored, out int total)
        {
            restored = total = 0;
            if (!_writable) return "Open the drive with changes allowed to undo.";
            lock (_pipe)
            {
                var name = System.Text.Encoding.UTF8.GetBytes(session ?? "");
                _w.Write(OpUndo);
                _w.Write(name.Length);
                _w.Write(name);
                _w.Flush();
                int status = _r.ReadInt32();
                restored = _r.ReadInt32();
                total = _r.ReadInt32();
                _cacheStart = -1;
                switch (status)
                {
                    case 0: return null;
                    case 4: return "That undo session is not there.";
                    case 5: return "That undo session is damaged.";
                    case 6: return "That undo session belongs to another drive.";
                    case 8: return "Stopped: a changed part of the drive was changed again since, by something else. " + restored + " of " + total + " changes were put back.";
                    case 9: return "That session was recorded by an older version, without the check that nothing else changed the drive since - it cannot be put back safely. Delete what it added instead.";
                    case 10: return "Something else changed the drive since that session (a console used it, or a later session was not put back first). Putting it back now could hide or damage those changes, so nothing was put back. Delete what the session added instead.";
                    default: return "The undo stopped (code " + status + "). " + restored + " of " + total + " changes were put back.";
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _length) return 0;
            int total = 0;
            lock (_pipe)
            {
                while (count > 0 && _pos < _length)
                {
                    if (_cacheStart < 0 || _pos < _cacheStart || _pos >= _cacheStart + _cacheLen) Fill(_pos);
                    int inBlock = (int)(_pos - _cacheStart);
                    if (inBlock >= _cacheLen) break;
                    int n = Math.Min(count, _cacheLen - inBlock);
                    Buffer.BlockCopy(_cache, inBlock, buffer, offset, n);
                    offset += n; count -= n; total += n; _pos += n;
                }
            }
            return total;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => _writable;
        public override long Length => _length;
        public override long Position { get => _pos; set => _pos = Math.Max(0, value); }
        public override long Seek(long offset, SeekOrigin origin)
        {
            _pos = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _pos + offset : _length + offset;
            return _pos;
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _pipe.Dispose(); } catch { }
                try { if (_helper != null && !_helper.WaitForExit(3000)) _helper.Kill(); } catch { }
                _helper?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
