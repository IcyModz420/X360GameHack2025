#nullable disable
#pragma warning disable 0067 // OnStdNotify / OnTextNotify reserved for future notify bridge
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace XDevkit
{
    /// <summary>
    /// XDevkit-compatible coclass implementation backed by XDCKIT's
    /// <see cref="global::XboxConsole"/> transport.
    /// </summary>
    public sealed class XboxConsoleClass : XboxConsole, IXboxConsole, XboxEvents_Event
    {
        private readonly XboxManagerClass _mgr;
        private readonly global::XboxConsole _host;
        private XboxDebugTargetClass _debug;
        private XboxAutomationShim _automation;
        private bool _shared;

        internal XboxConsoleClass(XboxManagerClass manager, global::XboxConsole host)
        {
            _mgr = manager ?? throw new ArgumentNullException(nameof(manager));
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        internal XboxManagerClass Manager => _mgr;

        public event XboxEvents_OnStdNotifyEventHandler OnStdNotify;
        public event XboxEvents_OnTextNotifyEventHandler OnTextNotify;

        public string Name
        {
            get => _host.Name;
            set => _host.Name = value;
        }

        public uint IPAddress => XDevkitInteropUtil.IpStringToUInt(_host.IPAddress);

        public uint IPAddressTitle
        {
            get
            {
                try
                {
                    string alt = _host.GetAltAddress();
                    return XDevkitInteropUtil.IpStringToUInt(alt);
                }
                catch { return 0; }
            }
        }

        public object SystemTime
        {
            get => _host.SystemTime;
            set
            {
                if (value is DateTime dt) _host.SystemTime = dt;
                else if (value != null) _host.SystemTime = Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        public bool Shared
        {
            get => _shared;
            set
            {
                _shared = value;
                if (value) _host.Client.MakeSharedConnection();
            }
        }

        public uint ConnectTimeout
        {
            get => (uint)Math.Max(0, _host.ConnectTimeoutMs);
            set => _host.ConnectTimeoutMs = value > int.MaxValue ? int.MaxValue : (int)value;
        }

        public uint ConversationTimeout
        {
            get => (uint)Math.Max(0, _host.ConversationTimeoutMs);
            set => _host.ConversationTimeoutMs = value > int.MaxValue ? int.MaxValue : (int)value;
        }

        public void FindConsole(uint retries, uint retryDelay)
        {
            int delay = (int)Math.Min(retryDelay, 60_000);
            for (uint i = 0; i <= retries; i++)
            {
                int found = _mgr.Inner.AutoDiscover(Math.Max(50, delay / 4));
                if (found > 0 && _mgr.Inner.Consoles.Count > 0)
                {
                    _mgr.Inner.DefaultConsole = _mgr.Inner.Consoles[0];
                    if (_host.Connected) _host.Disconnect();
                    _host.Connect(_mgr.Inner.DefaultConsole, _mgr.Inner.Port);
                    return;
                }
                if (i < retries) Thread.Sleep(delay);
            }
        }

        public XboxManager XboxManager => _mgr;

        public IXboxDebugTarget DebugTarget => _debug ??= new XboxDebugTargetClass(this, _host);

        public void Reboot(string imageName, string mediaDirectory, string cmdLine, XboxRebootFlags flags)
            => _host.Reboot(imageName, mediaDirectory, cmdLine, (global::XboxRebootFlags)(int)flags);

        public void SetDefaultTitle(string titleName, string mediaDirectory, uint flags)
        {
            _host.SetTitle(titleName);
        }

        public XBOX_PROCESS_INFO RunningProcessInfo
        {
            get
            {
                uint pid = _host.GetPid();
                string name = string.Empty;
                try { name = _host.GetXbeInfo() ?? string.Empty; } catch { /* ignore */ }
                return new XBOX_PROCESS_INFO { ProcessId = pid, ProgramName = name };
            }
        }

        public uint OpenConnection(string handler) => _host.OpenConnection(handler);

        public void CloseConnection(uint connection) => _host.CloseConnection(connection);

        public void SendTextCommand(uint connection, string command, out string response)
            => _host.SendTextCommand(connection, command, out response);

        public void ReceiveSocketLine(uint connection, out string line)
            => line = _host.Client.ReceiveSocketLine();

        public int ReceiveStatusResponse(uint connection, out string line)
        {
            var resp = _host.Client.ReceiveStatusResponse();
            line = resp.StatusMessage;
            return (int)resp.Status;
        }

        public void SendBinary(uint connection, byte[] data, uint count)
        {
            if (data == null) return;
            int n = (int)Math.Min(count, (uint)data.Length);
            _host.Client.SendBinary(data, 0, n);
        }

        public void ReceiveBinary(uint connection, byte[] data, uint count, out uint bytesReceived)
        {
            bytesReceived = 0;
            if (data == null || count == 0) return;
            int n = (int)Math.Min(count, (uint)data.Length);
            byte[] chunk = _host.Client.ReadExact(n);
            int copy = Math.Min(chunk.Length, n);
            Buffer.BlockCopy(chunk, 0, data, 0, copy);
            bytesReceived = (uint)copy;
        }

        public void SendBinary_cpp(uint connection, ref byte data, uint count)
            => throw new NotSupportedException("SendBinary_cpp is not supported by XDCKIT.");

        public void ReceiveBinary_cpp(uint connection, ref byte data, uint count, out uint bytesReceived)
            => throw new NotSupportedException("ReceiveBinary_cpp is not supported by XDCKIT.");

        public string Drives
        {
            get
            {
                try
                {
                    var list = _host.GetDrives();
                    if (list == null || list.Count == 0) return string.Empty;
                    var sb = new StringBuilder();
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(';');
                        sb.Append(list[i].DriveName);
                    }
                    return sb.ToString();
                }
                catch { return string.Empty; }
            }
        }

        public void GetDiskFreeSpace(ushort drive, out ulong freeBytesAvailableToCaller, out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes)
        {
            freeBytesAvailableToCaller = totalNumberOfBytes = totalNumberOfFreeBytes = 0;
            char letter = (char)drive;
            var list = _host.GetDrives();
            if (list == null) return;
            foreach (var d in list)
            {
                if (string.IsNullOrEmpty(d.DriveName)) continue;
                if (char.ToUpperInvariant(d.DriveName[0]) == char.ToUpperInvariant(letter) ||
                    d.DriveName.StartsWith(letter.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    var sp = _host.GetDriveFreeSpace(d.DriveName);
                    freeBytesAvailableToCaller = sp.FreeBytesAvailable;
                    totalNumberOfBytes = sp.TotalBytes;
                    totalNumberOfFreeBytes = sp.TotalFreeBytes;
                    return;
                }
            }
        }

        public void MakeDirectory(string directory) => _host.File.MakeDirectory(directory);

        public void RemoveDirectory(string directory) => _host.File.Delete(directory, true);

        public IXboxFiles DirectoryFiles(string directory)
        {
            var rows = _host.File.DirList(directory);
            var list = new List<IXboxFile>();
            foreach (var e in rows)
            {
                string full = directory.EndsWith("\\") ? directory + e.Name : directory + "\\" + e.Name;
                list.Add(new XboxFileEntryShim(full, e));
            }
            return new XboxFilesShim(list);
        }

        public void SendFile(string localName, string remoteName) => _host.File.SendFile(localName, remoteName);

        public void ReceiveFile(string localName, string remoteName) => _host.File.GetFile(remoteName, localName);

        public void ReadFileBytes(string filename, uint fileOffset, uint count, byte[] data, out uint bytesRead)
        {
            bytesRead = 0;
            if (data == null || count == 0) return;
            byte[] chunk = _host.ReadFilePartial(filename, fileOffset, count);
            int n = Math.Min(chunk.Length, Math.Min((int)count, data.Length));
            Buffer.BlockCopy(chunk, 0, data, 0, n);
            bytesRead = (uint)n;
        }

        public void WriteFileBytes(string filename, uint fileOffset, uint count, byte[] data, out uint bytesWritten)
        {
            bytesWritten = 0;
            if (data == null) return;
            int n = (int)Math.Min(count, (uint)data.Length);
            var slice = new byte[n];
            Buffer.BlockCopy(data, 0, slice, 0, n);
            _host.WriteFilePartial(filename, fileOffset, slice);
            bytesWritten = (uint)n;
        }

        public void ReadFileBytes_cpp(string filename, uint fileOffset, uint count, out byte data, out uint bytesRead)
            => throw new NotSupportedException("ReadFileBytes_cpp is not supported by XDCKIT.");

        public void WriteFileBytes_cpp(string filename, uint fileOffset, uint count, ref byte data, out uint bytesWritten)
            => throw new NotSupportedException("WriteFileBytes_cpp is not supported by XDCKIT.");

        public void SetFileSize(string filename, uint fileOffset, XboxCreateDisposition createDisposition)
        {
            bool must = createDisposition == XboxCreateDisposition.CreateNew;
            bool can = createDisposition == XboxCreateDisposition.OpenAlways
                    || createDisposition == XboxCreateDisposition.CreateAlways;
            _host.File.SetFileSize(filename, fileOffset, can, must);
        }

        public IXboxFile GetFileObject(string filename)
            => throw new NotSupportedException("GetFileObject is not implemented in XDCKIT; use DirectoryFiles or the global XboxFile helper.");

        public void RenameFile(string oldName, string newName) => _host.File.Rename(oldName, newName);

        public void DeleteFile(string filename) => _host.File.Delete(filename, false);

        public void ScreenShot(string filename)
        {
            if (string.IsNullOrEmpty(filename)) throw new ArgumentNullException(nameof(filename));
            _host.Screenshot(out byte[] pixels, null);
            File.WriteAllBytes(filename, pixels ?? Array.Empty<byte>());
        }

        public XboxDumpMode DumpMode
        {
            get => XDevkitInteropUtil.MapDumpModeGet(_host.GetDumpMode());
            set => _host.SetDumpMode(XDevkitInteropUtil.MapDumpModeSet(value));
        }

        public void GetDumpSettings(out XBOX_DUMP_SETTINGS dumpMode)
        {
            var s = _host.GetDumpSettings();
            dumpMode = new XBOX_DUMP_SETTINGS
            {
                Flags = XboxDumpReportFlags.AlwaysReport,
                NetworkPath = s?.Path ?? s?.Destination ?? string.Empty,
            };
        }

        public void SetDumpSettings(ref XBOX_DUMP_SETTINGS dumpMode)
        {
            _host.SetDumpSettings(new XboxDumpSettings
            {
                Report = dumpMode.Flags.ToString(),
                Destination = dumpMode.NetworkPath ?? string.Empty,
                Path = dumpMode.NetworkPath ?? string.Empty,
            });
        }

        public XboxEventDeferFlags EventDeferFlags
        {
            get => (XboxEventDeferFlags)(int)_host.GetEventDeferFlags();
            set => _host.SetEventDeferFlags((global::XboxEventDeferFlags)(int)value);
        }

        public XboxConsoleType ConsoleType => XDevkitInteropUtil.MapConsoleType(_host.ConsoleType);

        public void StartFileEventCapture() => _host.StartFileEventCapture();

        public void StopFileEventCapture() => _host.StopFileEventCapture();

        public IXboxAutomation XboxAutomation => _automation ??= new XboxAutomationShim(_host);

        public uint LoadDebuggerExtension(string extensionName) => _host.LoadDebuggerExtension(extensionName);

        public void UnloadDebuggerExtension(uint moduleHandle) => _host.UnloadDebuggerExtension(moduleHandle);

        public XboxConsoleFeatures ConsoleFeatures
        {
            get
            {
                var f = XboxConsoleFeatures.Debugging;
                try
                {
                    string raw = _host.GetSystemInfoRaw();
                    if (raw != null && raw.IndexOf("GB", StringComparison.OrdinalIgnoreCase) >= 0)
                        f |= XboxConsoleFeatures.GB_RAM;
                }
                catch { /* ignore */ }
                return f;
            }
        }
    }
}
