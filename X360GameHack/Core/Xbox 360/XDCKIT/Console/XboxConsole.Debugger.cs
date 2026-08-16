// =============================================================================
// XDCKIT.XboxConsole.Debugger.cs - Breakpoints + debugger session glue
// =============================================================================
// Wire formats taken straight from disassembled Microsoft xbdm.dll
// (see XBDM.asm / aBreakAddr0x08x_0 / aBreakStart / aBreakS0x08xSiz).
//
//   software bp set    : BREAK ADDR=0x%08x
//   software bp clear  : BREAK ADDR=0x%08x CLEAR
//   start of execution : BREAK START
//   data breakpoint    : BREAK <READ|WRITE|READWRITE|EXECUTE>=0x%08x SIZE=%lu [CLEAR]
//   query bp           : ISBREAK ADDR=0x%08x
//   clear all          : break clearall      (xbdm-master extension)
//   immediate halt     : break now           (xbdm-master extension)
//
//   debugger attach    : DEBUGGER CONNECT  [override] [port=N] [name=N] [user=U]
//   debugger detach    : DEBUGGER DISCONNECT [port=N] [override]
//   debugger status    : ISDEBUGGER          -> 410 if attached, 200 + name/user otherwise
//
// On the open-source xbdm fork (natelx/xbdm-master) all of the SDK forms are
// implemented by HrBreak in dmserv.c.  On real Microsoft devkits everything
// here is what the genuine debugger sends.
// =============================================================================
using System;
using System.Globalization;
using System.Text;

    public partial class XboxConsole
    {
        #region Software / data breakpoints (DmSetBreakpoint / DmDataBreakpoint / DmIsBreak)

        /// <summary>
        /// DmSetBreakpoint — set a software breakpoint at <paramref name="address"/>.
        /// </summary>
        public void SetBreakpoint(uint address)
            => SendTextCommand($"break addr=0x{address:X8}");

        /// <summary>
        /// DmRemoveBreakpoint — clear the software breakpoint at <paramref name="address"/>.
        /// </summary>
        public void ClearBreakpoint(uint address)
            => SendTextCommand($"break addr=0x{address:X8} clear");

        /// <summary>
        /// DmStartBreakpoint — set the initial breakpoint that fires the very
        /// first instruction of a pending title.  Maps to <c>BREAK START</c>.
        /// </summary>
        public void SetInitialBreakpoint() => SendTextCommand("break start");

        /// <summary>
        /// xbdm extension: clear every breakpoint at once (<c>break clearall</c>).
        /// </summary>
        public void ClearAllBreakpoints() => SendTextCommand("break clearall");

        /// <summary>
        /// xbdm extension: trigger an immediate <c>__emit(BREAKPOINT_BREAK)</c>
        /// on the title's main thread (<c>break now</c>).
        /// </summary>
        public void BreakNow() => SendTextCommand("break now");

        /// <summary>
        /// DmDataBreakpoint — install a hardware data breakpoint of
        /// <paramref name="kind"/> on a memory region.  Wire format:
        /// <c>BREAK &lt;READ|WRITE|READWRITE|EXECUTE&gt;=0x%08x SIZE=%lu</c>.
        /// </summary>
        public void SetDataBreakpoint(XboxDataBreakpointType kind, uint address, uint sizeInBytes)
        {
            string verb = DataBreakpointToken(kind);
            SendTextCommand($"break {verb}=0x{address:X8} size={sizeInBytes}");
        }

        /// <summary>Clear a previously set data breakpoint (adds the <c>CLEAR</c> token).</summary>
        public void ClearDataBreakpoint(XboxDataBreakpointType kind, uint address, uint sizeInBytes)
        {
            string verb = DataBreakpointToken(kind);
            SendTextCommand($"break {verb}=0x{address:X8} size={sizeInBytes} clear");
        }

        private static string DataBreakpointToken(XboxDataBreakpointType kind)
        {
            switch (kind)
            {
                case XboxDataBreakpointType.OnRead:      return "read";
                case XboxDataBreakpointType.OnWrite:     return "write";
                case XboxDataBreakpointType.OnReadWrite: return "readwrite";
                case XboxDataBreakpointType.OnExecute:   return "execute";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>
        /// DmIsBreak — wraps SDK <c>ISBREAK ADDR=0x%08x</c>; returns true iff
        /// a software breakpoint is installed at the given address.
        /// </summary>
        public bool IsBreak(uint address)
        {
            var resp = SendTextCommand($"isbreak addr=0x{address:X8}");
            if (!resp.IsSuccess) return false;
            string s = resp.StatusMessage ?? string.Empty;
            return s.IndexOf("set", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("yes", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion

        #region Debugger session (DmIsDebuggerPresent / DEBUGGER CONNECT|DISCONNECT)

        /// <summary>
        /// DmIsDebuggerPresent — true if any debugger is currently attached to
        /// the console.  xbdm <c>isdebugger</c> returns <c>410 already exists</c>
        /// when attached and <c>200 OK</c> (with optional name/user line) when
        /// not (see dmserv.c HrIsDebuggerAttached).  We treat the 410 status
        /// (or any non-empty response payload) as "attached".
        /// </summary>
        public bool IsDebuggerPresent()
        {
            var resp = SendTextCommand("isdebugger");
            if (resp == null) return false;
            if ((int)resp.Status == 410) return true; // XBDM_ALREADYEXISTS
            string s = resp.StatusMessage ?? string.Empty;
            return s.IndexOf("name=", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("user=", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Connect this client as the active xbdm debugger
        /// (<c>debugger connect [override] [port=N] [name=N] [user=U]</c>).
        /// </summary>
        public void AttachDebugger(string debuggerName = null, string user = null,
                                   ushort port = 0, bool overrideExisting = false)
        {
            var sb = new StringBuilder("debugger connect");
            if (overrideExisting) sb.Append(" override");
            if (port != 0) sb.Append(" port=").Append(port);
            if (!string.IsNullOrEmpty(debuggerName)) sb.Append(" name=\"").Append(debuggerName).Append("\"");
            if (!string.IsNullOrEmpty(user)) sb.Append(" user=\"").Append(user).Append("\"");
            SendTextCommand(sb.ToString());
        }

        /// <summary>Detach the active xbdm debugger (<c>debugger disconnect [port=N] [override]</c>).</summary>
        public void DetachDebugger(ushort port = 0, bool overrideExisting = false)
        {
            var sb = new StringBuilder("debugger disconnect");
            if (port != 0) sb.Append(" port=").Append(port);
            if (overrideExisting) sb.Append(" override");
            SendTextCommand(sb.ToString());
        }

        #endregion
    }
