// =============================================================================
// XDCKIT.XboxConsole.Xbdm.cs - Misc xbdm.dll wrappers
// =============================================================================
// Long-tail xbdm exports that don't fit cleanly into the memory / RPC /
// features / threads / users / files / notification partials.
//
// Wire formats here come from the disassembled Microsoft xbdm.dll
// (see XBDM.asm in the project root - those are the literal sprintf format
// strings the genuine SDK sends).  The natelx open-source xbdm reimagining
// in xbdm-master/ does NOT implement many of these commands - so on freebooted
// JTAG/RGH consoles running that fork they may return 407 (unknown command);
// they do however work on real Microsoft devkits and on every full xbdm port.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

    public partial class XboxConsole
    {
        #region Error code translation (DmTranslateError)

        /// <summary>
        /// Convert an xbdm response code (200..497, or HRESULT-style 0x82DA0xxx)
        /// to a friendly error string.  Mirrors DmTranslateErrorA / DmTranslateErrorW.
        /// </summary>
        public static string TranslateError(int errorCode)
        {
            switch (errorCode)
            {
                case 200: return "OK";
                case 201: return "Connected";
                case 202: return "Multiline response follows";
                case 203: return "Binary response follows";
                case 204: return "Send binary data";
                case 205: return "Now in notify session";
                case 400: return "Undefined error";
                case 401: return "Maximum number of connections exceeded";
                case 402: return "File not found";
                case 403: return "No such module";
                case 404: return "Memory not mapped";
                case 405: return "No such thread";
                case 406: return "Clock not set";
                case 407: return "Unknown command";
                case 408: return "Not stopped";
                case 409: return "File must be copied";
                case 410: return "File already exists";
                case 411: return "Directory not empty";
                case 412: return "Bad file name";
                case 413: return "File cannot be created";
                case 414: return "Access denied";
                case 415: return "No room on device";
                case 416: return "Not debuggable";
                case 417: return "Type invalid";
                case 418: return "Data not available";
                case 420: return "Box is not locked";
                case 421: return "Key exchange required";
                case 422: return "Dedicated connection required";
                case 423: return "Invalid argument";
                case 424: return "Profile not started";
                case 425: return "Profile already started";
                case 480: return "D3D debug command not implemented";
                case 481: return "D3D invalid surface";
                case 496: return "VxTaskPending";
                case 497: return "VxTooManySessions";
                default:
                    uint u = unchecked((uint)errorCode);
                    if ((u & 0xFFFF0000) == 0x82DA0000) return $"XBDM error 0x{u:X8}";
                    return $"Unknown error ({errorCode})";
            }
        }

        public static string TranslateError(XboxResponseType code) => TranslateError((int)code);

        #endregion

        #region Memory checksum / walk committed memory

        /// <summary>
        /// DmGetMemoryChecksum — wraps the xbdm SDK helper that issues
        /// <c>GETSUM ADDR=0x%08x LENGTH=0x%08x BLOCKSIZE=0x%08x</c>
        /// and returns a 32-bit checksum line ("checksum=0xXXXXXXXX").
        /// Useful for fast diffing without transferring the bytes.
        /// </summary>
        public uint GetMemoryChecksum(uint address, uint length, uint blockSize = 0x1000)
        {
            string cmd = $"getsum addr=0x{address:X8} length=0x{length:X8} blocksize=0x{blockSize:X8}";
            var resp = SendTextCommand(cmd);
            if (!resp.IsSuccess) return 0;
            string s = ParseKvLine(resp.StatusMessage, "checksum") ?? resp.StatusMessage?.Trim();
            return XboxExtensions.ParseHexUInt(s);
        }

        /// <summary>
        /// DmWalkCommittedMemory — wraps xbdm <c>walkmem</c>.  Each row is:
        /// <c>base=0xX size=0xX protect=0xX phys=0xX</c>
        /// (see dmserv.c HrReportWalkMemory).
        /// </summary>
        public List<XboxMemoryRegion> WalkCommittedMemory()
        {
            var list = new List<XboxMemoryRegion>();
            var resp = SendTextCommand("walkmem");
            if ((int)resp.Status != 202) return list;

            foreach (var line in SplitMultilineBody(resp.Body))
            {
                list.Add(new XboxMemoryRegion
                {
                    BaseAddress = ParseUIntKvHex(line, "base"),
                    Size        = ParseUIntKvHex(line, "size"),
                    Flags       = (XboxMemoryRegionFlags)ParseUIntKvHex(line, "protect"),
                });
            }
            return list;
        }

        #endregion

        #region File attributes (DmGetFileAttributes / DmSetFileAttributes / DmGetVolumeFileAttributes)

        /// <summary>
        /// DmGetFileAttributes — wraps the SDK <c>GETFILEATTRIBUTES NAME="%s"</c>.
        /// The 202 body holds (see dmserv.c GetFileAttrSz):
        /// <c>sizehi=0xX sizelo=0xX createhi=0xX createlo=0xX
        /// changehi=0xX changelo=0xX [directory] [readonly] [hidden]</c>.
        /// </summary>
        public XboxFileAttributes GetFileAttributes(string path)
        {
            var resp = SendTextCommand("getfileattributes name=" + XboxExtensions.QuoteXbdm(path));
            if (!resp.IsSuccess) return null;
            string body = (int)resp.Status == 202 ? resp.Body : resp.StatusMessage;
            return ParseFileAttributes(body);
        }

        /// <summary>
        /// DmGetVolumeFileAttributes — wraps SDK <c>GETVFATTR COUNT=N</c>.
        /// Returns the volume-relative attributes for the next <paramref name="count"/>
        /// entries iterated via <c>dirlist</c>; mostly used internally by the SDK.
        /// </summary>
        public string[] GetVolumeFileAttributes(int count)
        {
            var resp = SendTextCommand($"getvfattr count={count}");
            if ((int)resp.Status != 202) return Array.Empty<string>();
            return SplitMultilineBody(resp.Body);
        }

        /// <summary>
        /// DmSetFileAttributes — wraps SDK
        /// <c>SETFILEATTRIBUTES NAME="%s" CREATEHI=0xX CREATELO=0xX
        /// CHANGEHI=0xX CHANGELO=0xX</c>.  The natelx fork also accepts
        /// <c>readonly=0|1</c> and <c>hidden=0|1</c> (see dmserv.c
        /// HrSetFileAttr); we send those too for full coverage.
        /// </summary>
        public void SetFileAttributes(string path, XboxFileAttributes attrs)
        {
            if (attrs == null) throw new ArgumentNullException(nameof(attrs));
            var sb = new StringBuilder();
            sb.Append("setfileattributes name=").Append(XboxExtensions.QuoteXbdm(path));

            long ctf = attrs.CreateTime == default ? 0 : attrs.CreateTime.ToFileTime();
            long chf = attrs.ChangeTime == default ? 0 : attrs.ChangeTime.ToFileTime();
            sb.Append(" createhi=0x").Append(((uint)(ctf >> 32)).ToString("X8"))
              .Append(" createlo=0x").Append(((uint)ctf).ToString("X8"))
              .Append(" changehi=0x").Append(((uint)(chf >> 32)).ToString("X8"))
              .Append(" changelo=0x").Append(((uint)chf).ToString("X8"));
            sb.Append(" readonly=").Append(attrs.ReadOnly ? '1' : '0');
            sb.Append(" hidden=").Append(attrs.Hidden ? '1' : '0');

            SendTextCommand(sb.ToString());
        }

        private static XboxFileAttributes ParseFileAttributes(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            string flat = body.Replace("\r", " ").Replace("\n", " ");

            uint sizeHi = ParseUIntKvHex(flat, "sizehi");
            uint sizeLo = ParseUIntKvHex(flat, "sizelo");
            uint cthi   = ParseUIntKvHex(flat, "createhi");
            uint ctlo   = ParseUIntKvHex(flat, "createlo");
            uint chhi   = ParseUIntKvHex(flat, "changehi");
            uint chlo   = ParseUIntKvHex(flat, "changelo");

            var attrs = new XboxFileAttributes
            {
                Size      = CombineHiLo(sizeHi, sizeLo),
                ReadOnly  = ContainsFlag(flat, "readonly"),
                Hidden    = ContainsFlag(flat, "hidden"),
                Directory = ContainsFlag(flat, "directory"),
            };

            long ct = (long)CombineHiLo(cthi, ctlo);
            if (ct > 0) try { attrs.CreateTime = DateTime.FromFileTime(ct); } catch { /* ignore */ }
            long ch = (long)CombineHiLo(chhi, chlo);
            if (ch > 0) try { attrs.ChangeTime = DateTime.FromFileTime(ch); } catch { /* ignore */ }
            return attrs;
        }

        private static bool ContainsFlag(string flat, string flag)
        {
            int i = flat.IndexOf(flag, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            int end = i + flag.Length;
            char next = end < flat.Length ? flat[end] : ' ';
            return next == ' ' || next == '\r' || next == '\n' || next == '=';
        }

        #endregion

        #region Partial file I/O (DmReadFilePartial / DmWriteFilePartial / DmFileEof)

        /// <summary>
        /// DmReadFilePartial — wraps SDK
        /// <c>GETFILE NAME="%s" OFFSET=%lu SIZE=%lu</c>.  Reads
        /// <paramref name="length"/> bytes from <paramref name="offset"/> in
        /// the remote file and returns them.  Useful for browsing huge XEX
        /// files without downloading the whole thing.
        /// </summary>
        public byte[] ReadFilePartial(string remotePath, ulong offset, uint length)
        {
            string cmd = "getfile name=" + XboxExtensions.QuoteXbdm(remotePath) +
                         " offset=" + offset + " size=" + length;
            var resp = SendTextCommand(cmd);
            if ((int)resp.Status != 203) return Array.Empty<byte>();

            // xbdm sends the length as a 4-byte big-endian unsigned int (PPC byte order).
            // Reading as a signed host-LE int caused negative remaining counts for
            // any chunk bigger than 2 GiB.
            var hdr = Client.ReadExact(4);
            uint n = XboxExtensions.ReadUInt32BE(hdr);
            return n == 0 ? Array.Empty<byte>() : Client.ReadExact((int)Math.Min(n, int.MaxValue));
        }

        /// <summary>
        /// DmWriteFilePartial — wraps SDK
        /// <c>WRITEFILE NAME="%s" OFFSET=%lu LENGTH=%lu</c>.  Writes
        /// <paramref name="data"/> at <paramref name="offset"/> in the remote
        /// file without re-uploading the whole thing.
        /// </summary>
        public void WriteFilePartial(string remotePath, ulong offset, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            string cmd = "writefile name=" + XboxExtensions.QuoteXbdm(remotePath) +
                         " offset=" + offset + " length=" + data.Length;
            var resp = SendTextCommand(cmd);
            if ((int)resp.Status != 204)
                throw new InvalidOperationException(
                    $"WRITEFILE partial failed: {(int)resp.Status} {resp.StatusMessage}");
            // Send-then-receive atomically so the trailing status isn't stolen by a sibling thread.
            var post = Client.SendBinaryAndReceiveStatus(data);
            if (!post.IsSuccess)
                throw new InvalidOperationException(
                    $"WRITEFILE partial completion failed: {(int)post.Status} {post.StatusMessage}");
        }

        /// <summary>
        /// DmFileEof — wraps SDK <c>FILEEOF NAME="%s" SIZE=%lu [CANCREATE | MUSTCREATE]</c>.
        /// Resize / truncate / extend a remote file.  Pass
        /// <paramref name="canCreate"/>=true to also create the file if it
        /// doesn't exist (mirrors <c>FILE_OPEN_IF</c>).
        /// </summary>
        public void SetFileSize(string remotePath, ulong size, bool canCreate = false, bool mustCreate = false)
        {
            string suffix = mustCreate ? " mustcreate"
                          : canCreate  ? " cancreate"
                          : string.Empty;
            SendTextCommand("fileeof name=" + XboxExtensions.QuoteXbdm(remotePath) + " size=" + size + suffix);
        }

        #endregion

        #region Title launch + server config (DmSetTitle / DmSetServerName / DmSetConfigValue)

        /// <summary>
        /// DmSetTitle — wraps SDK <c>TITLE NAME="%s"</c>.  Tells xbdm what
        /// to launch on the next title-cold reboot.
        /// </summary>
        public void SetTitle(string imagePath)
            => SendTextCommand("title name=" + XboxExtensions.QuoteXbdm(imagePath));

        /// <summary>DmSetTitle with NOPERSIST — only applies to the next reboot.</summary>
        public void SetTitleNoPersist() => SendTextCommand("title nopersist");

        /// <summary>
        /// DmSetServerName — wraps SDK <c>SETSERVER NAME=%s</c>.  Renames the
        /// xbdm server (visible in Neighborhood).
        /// </summary>
        public void SetServerName(string name)
            => SendTextCommand("setserver name=" + XboxExtensions.QuoteXbdm(name));

        /// <summary>
        /// DmSetConfigValue — wraps SDK
        /// <c>SETCONFIG CATEGORY=0x%08x INDEX=0x%08x VALUE=0x%08x</c>.
        /// Writes a 32-bit value into a numeric (category, index) slot in the
        /// console's xbdm config store.
        /// </summary>
        public void SetConfigValue(uint category, uint index, uint value)
            => SendTextCommand($"setconfig category=0x{category:X8} index=0x{index:X8} value=0x{value:X8}");

        /// <summary>
        /// DmSetConnectionTimeout — forwards to <see cref="XboxClient.SetConnectionTimeout"/>.
        /// </summary>
        public void SetConnectionTimeout(int sendMs, int receiveMs)
            => Client.SetConnectionTimeout(sendMs, receiveMs);

        #endregion

        #region NIC stats / network info (DmGetNicStats / DmGetNetAddresses)

        /// <summary>
        /// DmGetNicStats — wraps SDK <c>NICSTATS deviceflags=0x%08x</c>.
        /// Returns the raw 202 multi-line body (key=value rows).
        /// </summary>
        public string GetNicStatsRaw(uint deviceFlags = 0)
        {
            var resp = SendTextCommand($"nicstats deviceflags=0x{deviceFlags:X8}");
            if (!resp.IsSuccess) return null;
            return (int)resp.Status == 202 ? resp.Body : resp.StatusMessage;
        }

        /// <summary>DmGetNetAddresses — wraps SDK <c>GETNETADDRS</c>.  Returns the lines as-is.</summary>
        public string[] GetNetAddresses()
        {
            var resp = SendTextCommand("getnetaddrs");
            if (!resp.IsSuccess) return Array.Empty<string>();
            string body = (int)resp.Status == 202 ? resp.Body : resp.StatusMessage;
            return SplitMultilineBody(body);
        }

        #endregion

        #region File event capture (DmStartFileEventCapture / DmStopFileEventCapture)

        public void StartFileEventCapture() => SendTextCommand("fileevent advise");
        public void StopFileEventCapture()  => SendTextCommand("fileevent unadvise");

        #endregion

        #region Crash dump

        /// <summary>DmCrashDump — wraps SDK <c>crashdump</c>; forces a kernel dump.</summary>
        public void TriggerCrashDump() => SendTextCommand("crashdump");

        /// <summary>
        /// DmSetDumpMode — wraps xbdm <c>dumpmode &lt;mode&gt;</c>.  dmserv.c
        /// HrSetDumpMode walks the global <c>rgszDumpMode</c> array looking for
        /// the first matching positional flag (no <c>=value</c>); the canonical
        /// names are <c>enabled</c>, <c>partial</c>, and <c>disabled</c>.
        /// </summary>
        public void SetDumpMode(XboxDumpMode mode)
        {
            string flag;
            switch (mode)
            {
                case XboxDumpMode.Enabled:  flag = "enabled"; break;
                case XboxDumpMode.Partial:  flag = "partial"; break;
                case XboxDumpMode.Disabled: flag = "disabled"; break;
                default: flag = "disabled"; break;
            }
            SendTextCommand("dumpmode " + flag);
        }

        #endregion

        #region Connection dedication (DmDedicateConnection)

        /// <summary>DmDedicateConnection — wraps SDK <c>DEDICATE GLOBAL</c>.</summary>
        public void DedicateConnection() => SendTextCommand("dedicate global");

        /// <summary>DmDedicateConnection with handler — wraps SDK <c>DEDICATE HANDLER=%s</c>.</summary>
        public void DedicateConnection(string handlerName)
            => SendTextCommand("dedicate handler=" + XboxExtensions.QuoteXbdm(handlerName));

        #endregion

        #region GPU counters (DmEnableGPUCounter)

        /// <summary>
        /// DmEnableGPUCounter — toggles GPU performance counters via
        /// <c>GPUCOUNT ENABLE</c> / <c>GPUCOUNT DISABLE</c>.
        /// </summary>
        public void SetGpuCountersEnabled(bool enabled)
            => SendTextCommand(enabled ? "gpucount enable" : "gpucount disable");

        #endregion

        #region Fun / easter-egg commands

        /// <summary>
        /// xbdm <c>khoungdm</c> — calls <c>VdDisplayFatalError(69)</c> on the
        /// console.  The same red-screen-of-death path the kernel uses for a
        /// fatal error.  Use with care; the only way back is a hard reboot.
        /// </summary>
        public void KhoungDm() => SendTextCommand("khoungdm");

        /// <summary>
        /// xbdm <c>whomadethis</c> — replies with
        /// <c>"Natelx did, of course"</c> from the open-source xbdm fork.
        /// </summary>
        public string WhoMadeThis()
        {
            var resp = SendTextCommand("whomadethis");
            return resp.IsSuccess ? resp.StatusMessage?.Trim() : null;
        }

        #endregion

        #region Dump mode get / dump settings (DmGetDumpMode / DmGetDumpSettings / DmSetDumpSettings)

        /// <summary>
        /// DmGetDumpMode — wraps SDK <c>dumpmode</c> (no args).  Returns the
        /// current crash-dump mode, or <see cref="XboxDumpMode.Disabled"/>
        /// if the console refuses the query.
        /// </summary>
        public XboxDumpMode GetDumpMode()
        {
            var resp = SendTextCommand("dumpmode");
            if (!resp.IsSuccess) return XboxDumpMode.Disabled;
            string s = (resp.StatusMessage ?? string.Empty).Trim().ToLowerInvariant();
            if (s.IndexOf("partial",  StringComparison.Ordinal) >= 0) return XboxDumpMode.Partial;
            if (s.IndexOf("disabled", StringComparison.Ordinal) >= 0) return XboxDumpMode.Disabled;
            if (s.IndexOf("enabled",  StringComparison.Ordinal) >= 0) return XboxDumpMode.Enabled;
            return XboxDumpMode.Disabled;
        }

        /// <summary>
        /// DmGetDumpSettings — wraps SDK <c>dumpsettings</c>.  Returns each
        /// reported field (rpt/dst/fmt/path) as a strongly-typed bag.
        /// </summary>
        public XboxDumpSettings GetDumpSettings()
        {
            var s = new XboxDumpSettings();
            var resp = SendTextCommand("dumpsettings");
            if (!resp.IsSuccess) return s;
            string line = resp.StatusMessage ?? string.Empty;
            s.Report      = ParseKvLine(line, "rpt");
            s.Destination = ParseKvLine(line, "dst");
            s.Format      = ParseKvLine(line, "fmt");
            s.Path        = ParseKvLine(line, "path");
            s.RawLine     = line;
            return s;
        }

        /// <summary>
        /// DmSetDumpSettings — wraps SDK
        /// <c>dumpsettings rpt=%s dst=%s fmt=%s path=%s</c>.
        /// </summary>
        public void SetDumpSettings(XboxDumpSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var sb = new StringBuilder("dumpsettings");
            if (!string.IsNullOrEmpty(settings.Report))      sb.Append(" rpt=").Append(settings.Report);
            if (!string.IsNullOrEmpty(settings.Destination)) sb.Append(" dst=").Append(settings.Destination);
            if (!string.IsNullOrEmpty(settings.Format))      sb.Append(" fmt=").Append(settings.Format);
            if (!string.IsNullOrEmpty(settings.Path))        sb.Append(" path=").Append(settings.Path);
            SendTextCommand(sb.ToString());
        }

        #endregion

        #region Event defer flags (DmGetEventDeferFlags / DmSetEventDeferFlags)

        // Wire tokens from XBDM.asm sub_42F105 / sub_42F200:
        //   eventdefer set [break][debugstring][singlestep][assert][ntassert][databreak][rip]
        //   eventdefer        -> 200- key=N pairs (break=1 etc.)
        private static readonly (XboxEventDeferFlags Flag, string Token)[] EventDeferTokens = new (XboxEventDeferFlags, string)[]
        {
            (XboxEventDeferFlags.CanDeferExecutionBreak,    "break"),
            (XboxEventDeferFlags.CanDeferDebugString,       "debugstring"),
            (XboxEventDeferFlags.CanDeferSingleStep,        "singlestep"),
            (XboxEventDeferFlags.CanDeferAssertionFailed,   "assert"),
            (XboxEventDeferFlags.CanDeferAssertionFailedEx, "ntassert"),
            (XboxEventDeferFlags.CanDeferDataBreak,         "databreak"),
            (XboxEventDeferFlags.CanDeferRIP,               "rip"),
        };

        /// <summary>
        /// DmGetEventDeferFlags — wraps SDK <c>eventdefer</c>.
        /// Walks every <c>token=N</c> field in the reply line.
        /// </summary>
        public XboxEventDeferFlags GetEventDeferFlags()
        {
            var resp = SendTextCommand("eventdefer");
            if (!resp.IsSuccess) return 0;
            string line = resp.StatusMessage ?? string.Empty;
            XboxEventDeferFlags result = 0;
            foreach (var (flag, token) in EventDeferTokens)
                if (ParseUIntKvHex(line, token) != 0) result |= flag;
            return result;
        }

        /// <summary>
        /// DmSetEventDeferFlags — wraps SDK
        /// <c>eventdefer set [break][debugstring][singlestep][assert][ntassert][databreak][rip]</c>.
        /// </summary>
        public void SetEventDeferFlags(XboxEventDeferFlags flags)
        {
            var sb = new StringBuilder("eventdefer set");
            foreach (var (flag, token) in EventDeferTokens)
                if ((flags & flag) != 0) sb.Append(' ').Append(token);
            SendTextCommand(sb.ToString());
        }

        #endregion

        #region Security (DmEnableSecurity / DmIsSecurityEnabled)

        /// <summary>
        /// DmEnableSecurity — wraps SDK <c>LOCKMODE BOXID=0qHHHHHHHH...</c>
        /// (the helper signs the seed with the supplied box id and locks the
        /// console to xbdm's secure-channel mode).
        /// Pass <paramref name="enabled"/>=false to call <see cref="Unlock"/>.
        /// </summary>
        public void EnableSecurity(bool enabled, ulong boxId = 0)
        {
            if (enabled) LockToBoxId(boxId == 0 ? 0xFFFFFFFFFFFFFFFFUL : boxId);
            else Unlock();
        }

        /// <summary>
        /// DmIsSecurityEnabled — there is no dedicated wire query in xbdm.dll
        /// for this; the SDK helper inspects the locked-state bit returned
        /// from the LOCKMODE / NONCE / KEYXCHG handshake.  Here we use the
        /// presence of a non-error reply to <c>NONCE</c> as the signal:
        /// secure consoles return a fresh nonce, locked-mode consoles return
        /// 420 (Box is not locked) / 421 (Key exchange required).
        /// </summary>
        public bool IsSecurityEnabled()
        {
            var resp = SendTextCommand("nonce");
            int code = (int)resp.Status;
            // 200 -> nonce produced, security pipeline active.
            // 421 -> key exchange required (security on, not yet authed).
            // 420 -> box not locked (security disabled).
            if (code == 200 || code == 421) return true;
            return false;
        }

        #endregion

        #region Debug memory size / status (DmGetDebugMemorySize / DmGetConsoleDebugMemoryStatus)

        /// <summary>
        /// DmGetDebugMemorySize — wraps SDK <c>debugmemsize</c>.
        /// Returns the size in MB the kernel reports for the debug heap
        /// (typically 256 / 512 / 1024).  Zero on failure.
        /// </summary>
        public uint GetDebugMemorySize()
        {
            var resp = SendTextCommand("debugmemsize");
            if (!resp.IsSuccess) return 0;
            string s = resp.StatusMessage ?? string.Empty;
            uint v = ParseUIntKvHex(s, "size");
            if (v == 0) v = ParseUIntKvHex(s, "debugmemsize");
            if (v == 0 && uint.TryParse(s.Trim(),
                                        NumberStyles.Integer,
                                        CultureInfo.InvariantCulture,
                                        out uint plain))
                v = plain;
            return v;
        }

        /// <summary>
        /// DmGetConsoleDebugMemoryStatus — wraps SDK <c>consolemem</c>.
        /// Returns the raw status text (key=value form) so callers can pull
        /// out whichever fields they need (used / free / committed / etc).
        /// </summary>
        public string GetConsoleDebugMemoryStatus()
        {
            var resp = SendTextCommand("consolemem");
            return resp.IsSuccess ? resp.StatusMessage?.Trim() : null;
        }

        /// <summary>
        /// DmGetAdditionalTitleMemorySetting — derived from <c>consolemem</c>.
        /// Microsoft's xbdm.dll computes this from one of the consolemem
        /// fields (the additional title memory the user enabled in the
        /// dashboard).  We expose the raw <c>additionaltitlemem</c> /
        /// <c>extramem</c> field in MB.
        /// </summary>
        public uint GetAdditionalTitleMemorySetting()
        {
            string s = GetConsoleDebugMemoryStatus();
            if (string.IsNullOrEmpty(s)) return 0;
            uint v = ParseUIntKvHex(s, "additionaltitlemem");
            if (v == 0) v = ParseUIntKvHex(s, "extramem");
            return v;
        }

        #endregion

        #region Debugger extensions (DmLoadDebuggerExtension / DmUnloadDebuggerExtension)

        /// <summary>
        /// DmLoadDebuggerExtension — wraps SDK
        /// <c>dbgextld  name=%s</c> (note the SDK literally uses two spaces;
        /// xbdm.dll's parser tolerates that).  Returns the loaded module
        /// handle reported back by xbdm, or 0 on failure.
        /// </summary>
        public uint LoadDebuggerExtension(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            var resp = SendTextCommand("dbgextld  name=" + XboxExtensions.QuoteXbdm(name));
            if (!resp.IsSuccess) return 0;
            string s = resp.StatusMessage ?? string.Empty;
            uint h = ParseUIntKvHex(s, "module");
            if (h == 0) h = ParseUIntKvHex(s, "handle");
            return h;
        }

        /// <summary>
        /// DmUnloadDebuggerExtension — wraps SDK
        /// <c>dbgextld unload module=0x%08x</c>.
        /// </summary>
        public void UnloadDebuggerExtension(uint moduleHandle)
            => SendTextCommand($"dbgextld unload module=0x{moduleHandle:X8}");

        #endregion

        #region DmWalkDir (recursive dirlist)

        /// <summary>
        /// DmWalkDir — recursively enumerates a remote folder.  In genuine
        /// xbdm.dll this reuses <c>DIRLIST NAME="%s"</c> and walks the
        /// returned entries client-side; XDCKIT does the same.
        /// </summary>
        public List<XboxDirEntry> WalkDir(string remoteRoot, bool recursive = true)
        {
            var all = new List<XboxDirEntry>();
            WalkDirInto(remoteRoot, recursive, all);
            return all;
        }

        private void WalkDirInto(string remoteRoot, bool recursive, List<XboxDirEntry> sink)
        {
            if (string.IsNullOrEmpty(remoteRoot)) return;
            var entries = File.DirList(remoteRoot);
            foreach (var e in entries)
            {
                sink.Add(e);
                if (recursive && e.IsDirectory)
                {
                    string sub = remoteRoot.EndsWith("\\") ? (remoteRoot + e.Name) : (remoteRoot + "\\" + e.Name);
                    WalkDirInto(sub, true, sink);
                }
            }
        }

        #endregion
    }
