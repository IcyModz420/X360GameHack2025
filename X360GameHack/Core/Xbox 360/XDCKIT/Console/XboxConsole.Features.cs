// =============================================================================
// XDCKIT.XboxConsole.Features.cs - High-level "console feature" wrappers
// =============================================================================
// Direct xbdm command wrappers (Reboot, Shutdown, Stop, Go, magicboot, dbgname,
// setcolor, getconsoleid, ...), modules/threads enumeration, system info,
// xbe info, drive map, dvd eject, screenshot, etc.
//
// These are the things that XDevkit's IXboxConsole calls out as "features"
// - hence the file name.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

    public partial class XboxConsole
    {
        #region Reboot / lifecycle

        /// <summary>Reboot using <c>magicboot</c>.  Cold or warm.</summary>
        public void Reboot(XboxReboot type)
        {
            switch (type)
            {
                case XboxReboot.Cold: Client.SendTextCommand("magicboot cold"); break;
                case XboxReboot.Warm: Client.SendTextCommand("magicboot warm"); break;
            }
        }

        /// <summary>XDevkit-style overload: reboot to a specific title with extras.</summary>
        public void Reboot(string imageName, string mediaDirectory, string commandLine, XboxRebootFlags flags)
        {
            var sb = new StringBuilder("magicboot");
            if (!string.IsNullOrEmpty(imageName))      sb.Append(" title=").Append(XboxExtensions.QuoteXbdm(imageName));
            if (!string.IsNullOrEmpty(mediaDirectory)) sb.Append(" directory=").Append(XboxExtensions.QuoteXbdm(mediaDirectory));
            if (!string.IsNullOrEmpty(commandLine))    sb.Append(" cmdline=").Append(XboxExtensions.QuoteXbdm(commandLine));
            if ((flags & XboxRebootFlags.Wait) != 0) sb.Append(" wait");
            if ((flags & XboxRebootFlags.Cold) != 0) sb.Append(" cold");
            if ((flags & XboxRebootFlags.Warm) != 0) sb.Append(" warm");
            if ((flags & XboxRebootFlags.Stop) != 0) sb.Append(" stop");
            Client.SendTextCommand(sb.ToString());
        }

        /// <summary>Send the native <c>shutdown</c> command (powers the console off).</summary>
        public void ShutDown()
        {
            try { Client.SendTextCommand("shutdown"); } catch { /* console gone */ }
        }

        /// <summary>Stop all threads (debugger break).</summary>
        public void Stop() => Client.SendTextCommand("stop");

        /// <summary>Resume all threads after a Stop.</summary>
        public void Go() => Client.SendTextCommand("go");

        /// <summary>Stop or resume execution (true == stop, false == go).</summary>
        public void FreezeConsole(bool freeze) { if (freeze) Stop(); else Go(); }

        /// <summary>
        /// Open or close the DVD tray with the native <c>dvdeject</c> command.
        /// dmserv.c HrDvdEject defaults the <c>eject</c> arg to 1 (open) when
        /// not specified; we send <c>eject=1</c> explicitly so behaviour is
        /// deterministic regardless of fork.
        /// </summary>
        public void DvdEject() => Client.SendTextCommand("dvdeject eject=1");

        /// <summary>Close the DVD tray (<c>dvdeject eject=0</c>).</summary>
        public void DvdLoad() => Client.SendTextCommand("dvdeject eject=0");

        /// <summary>Make FLASH:\ visible (or not) in the drive list.</summary>
        public void SetDriveMap(bool flashVisible)
            => Client.SendTextCommand(flashVisible ? "drivemap internal" : "drivemap noflash");

        #endregion

        #region System info / IDs

        /// <summary>
        /// DmGetSendXboxName helper — wraps the SDK <c>BOXID</c> command,
        /// returning the unique 16-char hex box id.  This is a different
        /// identity from <see cref="GetConsoleID"/> (which goes through
        /// XeKeysGetConsoleID) and from <see cref="Name"/> (the friendly name).
        /// </summary>
        public string GetBoxID()
        {
            var resp = Client.SendTextCommand("BOXID");
            return resp.IsSuccess ? resp.StatusMessage?.Trim() : null;
        }

        /// <summary>
        /// DmGetConsoleId — wraps the xbdm <c>getconsoleid</c> command.
        /// Replies with <c>consoleid=&lt;13-char string&gt;</c>.
        /// </summary>
        public string GetConsoleID()
        {
            var resp = Client.SendTextCommand("getconsoleid");
            if (!resp.IsSuccess) return null;
            string id = ParseKvLine(resp.StatusMessage, "consoleid") ?? resp.StatusMessage;
            return id?.Trim();
        }

        /// <summary>
        /// DmGetCpuKey — wraps xbdm <c>getcpukey</c>.  Replies with one
        /// 32-char hex string (no <c>key=</c> prefix; see dmserv.c HrGetCpuKey).
        /// </summary>
        public string GetCpuKey()
        {
            var resp = Client.SendTextCommand("getcpukey");
            if (!resp.IsSuccess) return null;
            return (resp.StatusMessage ?? string.Empty).Trim();
        }

        /// <summary>Get the xbdm version string (<c>dmversion</c>).</summary>
        public string GetDMVersion()
        {
            var resp = Client.SendTextCommand("dmversion");
            return resp.IsSuccess ? resp.StatusMessage?.Trim() : null;
        }

        /// <summary>
        /// Get the SMC firmware version (read directly from a well-known SMC
        /// scratch DWORD in kernel space).  Format is <c>"vMAJOR.MINOR"</c>.
        /// </summary>
        public string GetSMCVersion()
        {
            var raw = GetMemory(0x81AC7C50, 4);
            return $" {raw[2]}.{raw[3]}";
        }

        /// <summary>
        /// Query xbdm <c>systeminfo</c> (a 202 multi-line response).
        /// Each line is <c>KEY=VALUE [VALUE2...]</c>, e.g.
        /// <c>HDD=Enabled</c>, <c>Type=DevKit</c>,
        /// <c>Platform=Mongrel System=Trinity</c>,
        /// <c>BaseKrnl=2.0.7371.0 Krnl=2.0.17489.0 XDK=2.0.20353.0</c>.
        /// </summary>
        public string GetSystemInfo(SystemInfo which)
        {
            var resp = Client.SendTextCommand("systeminfo");
            if (!resp.IsSuccess) return null;
            string flat = (resp.Body == null ? resp.StatusMessage : resp.Body)
                .Replace("\r", " ").Replace("\n", " ");

            switch (which)
            {
                case SystemInfo.HDD:             return ParseKvLine(flat, "HDD");
                case SystemInfo.Type:            return ParseKvLine(flat, "Type");
                case SystemInfo.Platform:        return ParseKvLine(flat, "Platform");
                case SystemInfo.System:          return ParseKvLine(flat, "System");
                case SystemInfo.BaseKrnlVersion: return ParseKvLine(flat, "BaseKrnl");
                case SystemInfo.KrnlVersion:     return ParseKvLine(flat, "Krnl");
                case SystemInfo.XDKVersion:      return ParseKvLine(flat, "XDK");
                default: return null;
            }
        }

        /// <summary>Return the raw multi-line <c>systeminfo</c> body.</summary>
        public string GetSystemInfoRaw()
        {
            var resp = Client.SendTextCommand("systeminfo");
            if (!resp.IsSuccess) return null;
            return resp.Body ?? resp.StatusMessage;
        }

        /// <summary>
        /// Set the dashboard / Neighborhood color tag for this console.
        /// dmserv.c HrSetColor accepts <c>name=black|blue|bluegray|nosidecar|white</c>.
        /// </summary>
        public void SetConsoleColor(XboxColor color)
        {
            string name;
            switch (color)
            {
                case XboxColor.Black:     name = "black"; break;
                case XboxColor.Blue:      name = "blue"; break;
                case XboxColor.BlueGray:  name = "bluegray"; break;
                case XboxColor.NoSidecar: name = "nosidecar"; break;
                case XboxColor.White:     name = "white"; break;
                default:                  name = color.ToString().ToLowerInvariant(); break;
            }
            Client.SendTextCommand("setcolor name=" + name);
        }

        #endregion

        #region Threads / modules / xbe info

        /// <summary>
        /// Get the list of running thread IDs.  xbdm <c>threads</c> emits
        /// each id as decimal text on its own line (see dmserv.c
        /// HrReportThreadList — <c>"%d\r\n"</c>); <c>threadex</c> is the
        /// hex/<c>"%08X\r\n"</c> variant exposed via
        /// <see cref="GetThreadListingEx"/>.
        /// </summary>
        public uint[] GetThreadIds()
        {
            var resp = Client.SendTextCommand("threads");
            if ((int)resp.Status != 202) return Array.Empty<uint>();
            var lines = SplitMultilineBody(resp.Body);
            var ids = new uint[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim();
                // A real console prints the ids as SIGNED decimals: 0xFB000008 arrives as -83886072.
                int signed;
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out signed)) ids[i] = unchecked((uint)signed);
                else if (!uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out ids[i]))
                    ids[i] = XboxExtensions.ParseHexUInt(s); // fall back to hex for forks
            }
            return ids;
        }

        /// <summary>
        /// DmGetThreadInfoEx — wraps xbdm <c>threadinfo thread=0xID</c>.
        /// xbdm replies with one line of:
        /// <c>suspend=N priority=N tlsbase=0xX start=0xX base=0xX limit=0xX
        /// slack=0xX createhi=0xX createlo=0xX nameaddr=0xX namelen=0xX
        /// proc=0xXX lasterr=0xX</c> (see dmserv.c HrReportThreadInfo).
        /// </summary>
        public XboxThreadInfo GetThreadInfo(uint threadId)
        {
            var resp = Client.SendTextCommand($"threadinfo thread=0x{threadId:X}");
            string body = (int)resp.Status == 202 ? resp.Body : resp.StatusMessage;
            if (string.IsNullOrEmpty(body)) return null;
            string flat = body.Replace("\r", " ").Replace("\n", " ");

            var info = new XboxThreadInfo
            {
                ThreadId         = threadId,
                Suspend          = ParseUIntKvHex(flat, "suspend"),
                Priority         = ParseUIntKvHex(flat, "priority"),
                TlsBase          = ParseUIntKvHex(flat, "tlsbase"),
                StartAddress     = ParseUIntKvHex(flat, "start"),
                Base             = ParseUIntKvHex(flat, "base"),
                Limit            = ParseUIntKvHex(flat, "limit"),
                Slack            = ParseUIntKvHex(flat, "slack"),
                NameAddress      = ParseUIntKvHex(flat, "nameaddr"),
                NameLength       = ParseUIntKvHex(flat, "namelen"),
                CurrentProcessor = ParseUIntKvHex(flat, "proc"),
                LastError        = ParseUIntKvHex(flat, "lasterr"),
            };

            uint cthi = ParseUIntKvHex(flat, "createhi");
            uint ctlo = ParseUIntKvHex(flat, "createlo");
            long ft = (long)CombineHiLo(cthi, ctlo);
            if (ft > 0) try { info.CreateTime = DateTime.FromFileTime(ft); } catch { /* ignore */ }
            return info;
        }

        /// <summary>
        /// DmWalkLoadedModulesEx — wraps xbdm <c>modules</c>.  Each row is:
        /// <c>name="X" base=0xX size=0xX check=0xX timestamp=0xX
        /// pdata=0xX psize=0xX [dll]thread=0xX osize=0xX</c>
        /// (see dmserv.c HrReportModules).
        /// </summary>
        public List<ModuleInfo> GetModules()
        {
            var list = new List<ModuleInfo>();
            var resp = Client.SendTextCommand("modules");
            if ((int)resp.Status != 202) return list;

            foreach (var line in SplitMultilineBody(resp.Body))
            {
                var mi = new ModuleInfo
                {
                    Name        = ParseKvLine(line, "name"),
                    BaseAddress = ParseUIntKvHex(line, "base"),
                    Size        = ParseUIntKvHex(line, "size"),
                    Checksum    = ParseUIntKvHex(line, "check"),
                };
                long ts = ParseUIntKvHex(line, "timestamp");
                if (ts > 0) try { mi.TimeStamp = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime; }
                            catch { mi.TimeStamp = DateTime.MinValue; }
                list.Add(mi);
            }

            return list;
        }

        /// <summary>
        /// DmWalkModuleSections — wraps xbdm <c>modsections name=...</c>.
        /// Each row is <c>name="X" base=0xX size=0xX index=N flags=N</c>
        /// (see dmserv.c HrReportModuleSections).
        /// </summary>
        public List<ModuleSection> GetModuleSections(string moduleName)
        {
            var list = new List<ModuleSection>();
            var resp = Client.SendTextCommand("modsections name=" + XboxExtensions.QuoteXbdm(moduleName));
            if ((int)resp.Status != 202) return list;
            foreach (var line in SplitMultilineBody(resp.Body))
            {
                list.Add(new ModuleSection
                {
                    Name  = ParseKvLine(line, "name"),
                    Base  = ParseUIntKvHex(line, "base"),
                    Size  = ParseUIntKvHex(line, "size"),
                    Index = ParseUIntKvHex(line, "index"),
                    Flags = ParseUIntKvHex(line, "flags"),
                });
            }
            return list;
        }

        /// <summary>
        /// Get info on the running xex (<c>xbeinfo running</c>).
        /// dmserv.c HrReportXbeInfo emits two lines:
        /// <c>timestamp=0xX checksum=0xX</c> then <c>name="\Device\Harddisk0\..."</c>.
        /// </summary>
        public string GetXbeInfo()
        {
            var resp = Client.SendTextCommand("xbeinfo running");
            return (int)resp.Status == 202 ? resp.Body : resp.StatusMessage;
        }

        /// <summary>Get info on the xex at the given path (<c>xbeinfo name="..."</c>).</summary>
        public string GetXbeInfo(string moduleName, bool onDiskOnly = false)
        {
            string cmd = string.IsNullOrEmpty(moduleName)
                ? "xbeinfo running"
                : "xbeinfo name=" + XboxExtensions.QuoteXbdm(moduleName) + (onDiskOnly ? " ondiskonly" : string.Empty);
            var resp = Client.SendTextCommand(cmd);
            return (int)resp.Status == 202 ? resp.Body : resp.StatusMessage;
        }

        #endregion

        #region SMC / fan / LED (low level)

        /// <summary>
        /// Build an SMC fan-speed message (caller is responsible for actually
        /// firing the SMC IPC).  Kept for API parity with old code.
        /// </summary>
        public static byte[] BuildFanSpeedMessage(int fan, int speed)
        {
            var msg = new byte[16];
            if (fan == 1) msg[0] = 0x94;
            else if (fan == 2) msg[0] = 0x89;
            else return null;

            if (speed > 100) speed = 100;
            if (speed <= 0) speed = 10;
            msg[1] = speed < 45 ? (byte)0x7F : (byte)(speed | 0x80);
            return msg;
        }

        #endregion

        #region Screenshots (xbdm screenshot + DmReceiveBinary semantics)

        /// <summary>
        /// Capture the front buffer via xbdm <c>screenshot</c> (same wire sequence as
        /// Microsoft <c>DmScreenShot</c>: status line, metadata line with pitch/width/…,
        /// then one raw binary block — not <c>getmemex</c> chunking).
        /// </summary>
        public ScreenshotInfo Screenshot(out byte[] frameBuffer)
            => Screenshot(out frameBuffer, null);

        /// <summary>
        /// Same as <see cref="Screenshot(out byte[])"/> but forwards extra xbdm arguments
        /// (e.g. region flags) when your xbdm build supports them.
        /// </summary>
        public ScreenshotInfo Screenshot(out byte[] frameBuffer, string screenshotArguments)
        {
            if (!Connected) throw new InvalidOperationException("Not connected.");
            string cmd = string.IsNullOrWhiteSpace(screenshotArguments)
                ? "screenshot"
                : "screenshot " + screenshotArguments.Trim();

            var (status, meta, pixels) = Client.TakeScreenshotCommand(cmd);
            frameBuffer = pixels ?? Array.Empty<byte>();
            return ParseScreenshotInfo(meta);
        }

        private static ScreenshotInfo ParseScreenshotInfo(string meta)
        {
            if (string.IsNullOrEmpty(meta)) return default;

            uint pitch = ParseUIntKvHex(meta, "pitch");
            if (pitch == 0)
            {
                uint phi = ParseUIntKvHex(meta, "pitchhi");
                uint plo = ParseUIntKvHex(meta, "pitchlo");
                if (phi != 0 || plo != 0)
                {
                    ulong p64 = ((ulong)phi << 32) | plo;
                    pitch = p64 > uint.MaxValue ? uint.MaxValue : (uint)p64;
                }
            }

            return new ScreenshotInfo
            {
                Pitch = pitch,
                Width = ParseUIntKvHex(meta, "width"),
                Height = ParseUIntKvHex(meta, "height"),
                Format = ParseUIntKvHex(meta, "format"),
                FrameBufferSize = ParseUIntKvHex(meta, "framebuffersize"),
                OffsetX = ParseUIntKvHex(meta, "offsetx"),
                OffsetY = ParseUIntKvHex(meta, "offsety"),
                SubWidth = ParseUIntKvHex(meta, "sw"),
                SubHeight = ParseUIntKvHex(meta, "sh"),
                ColorSpace = ParseUIntKvHex(meta, "colorspace"),
            };
        }

        #endregion

        #region xbdm discovery / misc commands (help, hwinfo, objlist, spew, threadex, xexfield)

        /// <summary>Raw <c>help</c> output (often a 202 multi-line body).</summary>
        public string GetXbdmHelp()
        {
            var resp = Client.SendTextCommand("help");
            if ((int)resp.Status == 202) return resp.Body ?? string.Empty;
            return resp.StatusMessage ?? string.Empty;
        }

        /// <summary><c>hwinfo</c> — hardware listing (cOz / neighborhood diagnostics).</summary>
        public string GetHwInfo()
        {
            var resp = Client.SendTextCommand("hwinfo");
            if ((int)resp.Status == 202) return resp.Body ?? string.Empty;
            return resp.StatusMessage ?? string.Empty;
        }

        /// <summary><c>objlist</c> — object listing for cOz-style tooling.</summary>
        public string GetObjList()
        {
            var resp = Client.SendTextCommand("objlist");
            if ((int)resp.Status == 202) return resp.Body ?? string.Empty;
            return resp.StatusMessage ?? string.Empty;
        }

        /// <summary><c>spew</c> — inject a debug string into the title's debug channel.</summary>
        public void Spew(string message)
        {
            Client.SendTextCommand("spew " + XboxExtensions.QuoteXbdm(message ?? string.Empty));
        }

        /// <summary><c>threadex</c> — extended thread listing (format differs from <c>threads</c>).</summary>
        public string[] GetThreadListingEx()
        {
            var resp = Client.SendTextCommand("threadex");
            if ((int)resp.Status != 202) return Array.Empty<string>();
            return SplitMultilineBody(resp.Body);
        }

        /// <summary>XEX header field id for the title id (<c>XEX_HEADER_EXECUTION_INFO</c>).</summary>
        public const uint XexFieldTitleId = 0x00010100;   // misnamed: this key is the entry point (measured), see GetTitleId

        /// <summary>
        /// DmGetXexHeaderField — wraps xbdm <c>xexfield module=&lt;name&gt; field=0xID</c>.
        /// dmserv.c HrGetXexField only recognises <c>field=0x00010100</c>
        /// (TitleId) and emits <c>fieldsize=0x4</c> followed by <c>%08X</c>
        /// on the next line.  Returns the raw payload value as a hex string,
        /// or null if the field isn't present / module isn't loaded.
        /// </summary>
        public string GetXexField(uint fieldId, string moduleName = "xam.xex")
        {
            string name = string.IsNullOrWhiteSpace(moduleName) ? "xam.xex" : moduleName.Trim();
            var resp = Client.SendTextCommand(
                "xexfield module=" + XboxExtensions.QuoteXbdm(name) + " field=0x" + fieldId.ToString("X"));
            if ((int)resp.Status != 202) return null;

            string body = resp.Body ?? string.Empty;
            // Skip the "fieldsize=0xN" line and return the payload line(s).
            foreach (var line in SplitMultilineBody(body))
            {
                if (line.IndexOf("fieldsize", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                return line.Trim();
            }
            return null;
        }

        /// <summary>
        /// Get the running title's TitleId (e.g. 0x4D5307E6 for Halo 3).
        /// Issues <c>xexfield module="running" field=0x00010100</c>; falls
        /// back to xam if the running title hasn't loaded a XEX module yet.
        /// </summary>
        public uint GetTitleId(string moduleName = "running")
        {
            // Measured on a retail-kernel console (xbdm 2.0.21076.11): "xexfield ... field=0x10100" answers with the
            // module's entry point address, not a title id, and module="running" is "file not found". So the id is
            // read from memory instead: the kernel's XexExecutableModuleHandle (export 403) -> the module record ->
            // its copy of the XEX header -> the execution info block. Reads only; nothing is written or called.
            try
            {
                uint at = RunningExecutionInfo();
                return at == 0 ? 0 : ReadUInt32(at + 0x0C);
            }
            catch (Exception ex) when (!(ex is System.IO.IOException) && !(ex is System.Net.Sockets.SocketException)) { }
            return 0;
        }

        /// <summary>
        /// The version of the program that is running, as its header in memory states it (0 when it cannot be
        /// read). A title update raises it: 0.0.0.2 as sold becomes 0.0.24.2 with title update 24 applied - seen on
        /// a real console. The third part is the title update number. Reads only.
        /// </summary>
        public uint GetTitleVersion()
        {
            try
            {
                uint at = RunningExecutionInfo();
                return at == 0 ? 0 : ReadUInt32(at + 0x04);
            }
            catch (Exception ex) when (!(ex is System.IO.IOException) && !(ex is System.Net.Sockets.SocketException)) { }
            return 0;
        }

        /// <summary>Address of the running program's execution info block (media id, version, base version, title id), or 0.</summary>
        private uint RunningExecutionInfo()
        {
            uint record = ReadUInt32(RunningModuleHandleAddress());
            if (record == 0) return 0;
            uint header = ReadUInt32(record + 0x58);
            byte[] h = GetMemory(header, 0x18);
            if (h == null || h.Length < 0x18 || h[0] != 'X' || h[1] != 'E' || h[2] != 'X' || h[3] != '2') return 0;
            uint count = Be(h, 0x14);
            if (count == 0 || count > 64) return 0;
            byte[] dir = GetMemory(header + 0x18, count * 8);
            for (int i = 0; i < count; i++)
            {
                if (Be(dir, i * 8) != 0x00040006) continue;      // execution info
                uint at = Be(dir, i * 8 + 4);
                if (at < 0x80000000u) at += header;
                return at;
            }
            return 0;
        }

        private uint _runningModuleHandle;

        /// <summary>Address of the kernel variable that points at the running program's module record, found in
        /// the kernel's own export table (ordinal 403).</summary>
        private uint RunningModuleHandleAddress()
        {
            if (_runningModuleHandle != 0) return _runningModuleHandle;
            uint kernel = 0x80040000;
            foreach (var m in GetModules())
                if (string.Equals(m.Name, "xboxkrnl.exe", StringComparison.OrdinalIgnoreCase)) { kernel = m.BaseAddress; break; }
            byte[] mz = GetMemory(kernel, 0x40);
            if (mz[0] != 'M' || mz[1] != 'Z') throw new InvalidOperationException("kernel image not found");
            uint pe = BitConverter.ToUInt32(mz, 0x3C);
            if (pe > 0x1000) throw new InvalidOperationException("kernel image not understood");
            byte[] nt = GetMemory(kernel + pe, 0x80);
            if (nt[0] != 'P' || nt[1] != 'E') throw new InvalidOperationException("kernel image not understood");
            uint exports = BitConverter.ToUInt32(nt, 0x78);
            byte[] ed = GetMemory(kernel + exports, 0x28);
            uint ordinalBase = BitConverter.ToUInt32(ed, 0x10), functions = BitConverter.ToUInt32(ed, 0x14), table = BitConverter.ToUInt32(ed, 0x1C);
            const uint ordinal = 403;
            if (ordinal < ordinalBase || ordinal - ordinalBase >= functions) throw new InvalidOperationException("kernel export not found");
            uint rva = BitConverter.ToUInt32(GetMemory(kernel + table + 4 * (ordinal - ordinalBase), 4), 0);
            if (rva == 0) throw new InvalidOperationException("kernel export not found");
            return _runningModuleHandle = kernel + rva;
        }

        private static uint Be(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

        #endregion

        #region Drive list / drive free space

        public List<XboxDrive> GetDrives()
        {
            var list = new List<XboxDrive>();
            var resp = Client.SendTextCommand("drivelist");
            if ((int)resp.Status != 202) return list;

            foreach (var line in SplitMultilineBody(resp.Body))
            {
                string name = ParseKvLine(line, "drivename");
                if (string.IsNullOrEmpty(name)) continue;
                var drive = new XboxDrive { DriveName = name };
                try { drive.Space = GetDriveFreeSpace(name); } catch { /* ignore */ }
                list.Add(drive);
            }
            return list;
        }

        public DriveSpaceInfo GetDriveFreeSpace(string driveName)
        {
            // dmserv.c HrGetDriveFreeSpace looks for name="DRIVE:\".
            var resp = Client.SendTextCommand("drivefreespace name=" + XboxExtensions.QuoteXbdm(driveName + ":\\"));
            if (!resp.IsSuccess) return default;
            string flat = (resp.Body ?? resp.StatusMessage).Replace("\r", " ").Replace("\n", " ");
            uint feHi = ParseUIntKvHex(flat, "freetocallerhi");
            uint feLo = ParseUIntKvHex(flat, "freetocallerlo");
            uint tHi = ParseUIntKvHex(flat, "totalbyteshi");
            uint tLo = ParseUIntKvHex(flat, "totalbyteslo");
            uint tfHi = ParseUIntKvHex(flat, "totalfreebyteshi");
            uint tfLo = ParseUIntKvHex(flat, "totalfreebyteslo");
            return new DriveSpaceInfo
            {
                FreeBytesAvailable = CombineHiLo(feHi, feLo),
                TotalBytes = CombineHiLo(tHi, tLo),
                TotalFreeBytes = CombineHiLo(tfHi, tfLo),
            };
        }

        #endregion

        #region Dashboard shortcuts (XAM ordinal launcher)

        /// <summary>
        /// Trigger one of the well-known XAM dashboard shortcuts (e.g.
        /// <c>XboxShortcuts.Achievements</c>).
        /// </summary>
        public void XboxShortcut(XboxShortcuts ui)
        {
            if (!Connected) return;
            const string XAM = "xam.xex";

            switch (ui)
            {
                case XboxShortcuts.XboxHome:
                    Reboot(@"\Device\Harddisk0\SystemExtPartition\20449700\dash.xex",
                           @"\Device\Harddisk0\SystemExtPartition\20449700",
                           @"\Device\Harddisk0\SystemExtPartition\20445700\dash.xex",
                           XboxRebootFlags.Title);
                    break;

                case XboxShortcuts.AvatarEditor:
                    Reboot(@"\Device\Harddisk0\SystemExtPartition\20449700\AvatarEditor.xex",
                           @"\Device\Harddisk0\SystemExtPartition\20449700",
                           @"\Device\Harddisk0\SystemExtPartition\20445700\AvatarEditor.xex",
                           XboxRebootFlags.Title);
                    break;

                // DriveSelector used to launch signin.xex - on a real console (2026-11-18) that shows an empty grey box and
                // then the sign-in screen, not a storage selector. The real selector is a XAM call (remote-call plugins only).

                case XboxShortcuts.Turn_Off_Console:
                    ShutDown();
                    break;

                default:
                    // Every other shortcut is a function inside the dashboard that only a remote-call plugin
                    // (JRPC2 / XDRPC / XRPC) can reach. This tool speaks plain xbdm only.
                    throw new NotSupportedException("That shortcut cannot be opened over plain XBDM.");
            }
        }

        /// <summary>The shortcuts plain xbdm can open (by launching the system program, or powering off).</summary>
        public static readonly XboxShortcuts[] PlainShortcuts = { XboxShortcuts.XboxHome, XboxShortcuts.AvatarEditor, XboxShortcuts.Turn_Off_Console };

        #endregion


    }