// =============================================================================
// XDCKIT.XboxDebugTarget.cs - Mirror of XDevkit's IXboxDebugTarget
// =============================================================================
// XDevkit exposes a `DebugTarget` sub-object on every IXboxConsole that
// holds the memory/breakpoint/debugger surface.  We mirror that here as
// `XboxConsole.DebugTarget`.  Methods just forward into XboxConsole so the
// caller can use either style:
//
//     console.GetMemory(addr, len, buf, out r);
//     console.DebugTarget.GetMemory(addr, len, buf, out r);   // identical
// =============================================================================
using System;

    public sealed class XboxDebugTarget
    {
        private readonly XboxConsole _console;

        internal XboxDebugTarget(XboxConsole console) => _console = console;

        public bool Connected => _console.Connected;

        #region Memory passthroughs

        public byte[] GetMemory(uint address, uint length) => _console.GetMemory(address, length);

        public void GetMemory(uint address, uint bytesToRead, byte[] data, out uint bytesRead)
            => _console.GetMemory(address, bytesToRead, data, out bytesRead);

        public void SetMemory(uint address, byte[] data) => _console.SetMemory(address, data);

        public void SetMemory(uint address, uint bytesToWrite, byte[] data, out uint bytesWritten)
            => _console.SetMemory(address, bytesToWrite, data, out bytesWritten);

        public void InvalidateMemoryCache(bool executablePages, uint address, uint size)
            => _console.InvalidateMemoryCache(executablePages, address, size);

        #endregion

        #region Breakpoint passthroughs

        /// <summary>Set a software execution breakpoint at <paramref name="address"/>.</summary>
        public void SetBreakpoint(uint address) => _console.SetBreakpoint(address);

        /// <summary>Clear a previously-set software breakpoint.</summary>
        public void RemoveBreakpoint(uint address) => _console.ClearBreakpoint(address);

        /// <summary>Clear every breakpoint.</summary>
        public void RemoveAllBreakpoints() => _console.ClearAllBreakpoints();

        /// <summary>Set a hardware data-breakpoint.</summary>
        public void SetDataBreakpoint(uint address, XboxBreakpointType type, uint size)
        {
            switch (type)
            {
                case XboxBreakpointType.OnRead:
                    _console.SetDataBreakpoint(XboxDataBreakpointType.OnRead, address, size); break;
                case XboxBreakpointType.OnWrite:
                    _console.SetDataBreakpoint(XboxDataBreakpointType.OnWrite, address, size); break;
                case XboxBreakpointType.OnExecute:
                case XboxBreakpointType.OnExecuteHW:
                    _console.SetDataBreakpoint(XboxDataBreakpointType.OnExecute, address, size); break;
                default:
                    _console.ClearDataBreakpoint(XboxDataBreakpointType.OnRead, address, size); break;
            }
        }

        /// <summary>Returns true iff a software breakpoint is currently set at <paramref name="address"/>.</summary>
        public bool IsBreak(uint address) => _console.IsBreak(address);

        #endregion

        #region Debugger attachment

        /// <summary>
        /// Returns whether xbdm thinks a debugger is currently attached and,
        /// if so, who it claims is attached.  Wraps SDK <c>ISDEBUGGER</c>
        /// (dmserv.c HrIsDebuggerAttached).
        /// </summary>
        public bool IsDebuggerConnected(out string debuggerName, out string userName)
        {
            debuggerName = userName = string.Empty;
            var resp = _console.SendTextCommand("isdebugger");
            if (resp == null) return false;

            string body = resp.StatusMessage ?? string.Empty;
            debuggerName = XboxConsole.ParseKvLine(body, "name");
            userName = XboxConsole.ParseKvLine(body, "user");
            return (int)resp.Status == 410
                || !string.IsNullOrEmpty(debuggerName)
                || !string.IsNullOrEmpty(userName);
        }

        /// <summary>Connect this XDCKIT instance as the registered debugger.</summary>
        public void ConnectAsDebugger(string name = null, string user = null, ushort port = 0, bool overrideExisting = false)
            => _console.AttachDebugger(name, user, port, overrideExisting);

        /// <summary>Disconnect from the debugger session.</summary>
        public void DisconnectAsDebugger(ushort port = 0, bool overrideExisting = false)
            => _console.DetachDebugger(port, overrideExisting);

        #endregion

        #region Stop / Go convenience

        public void Stop() => _console.Stop();
        public void Go()   => _console.Go();

        #endregion

        #region Per-thread control (passthroughs to XboxConsole)

        public void HaltThread(uint threadId) => _console.HaltThread(threadId);
        public void ContinueThread(uint threadId, bool exception = false) => _console.ContinueThread(threadId, exception);
        public bool SuspendThread(uint threadId) => _console.SuspendThread(threadId);
        public bool ResumeThread(uint threadId)  => _console.ResumeThread(threadId);
        public bool IsThreadStopped(uint threadId) => _console.IsThreadStopped(threadId);
        public string GetThreadStopReason(uint threadId) => _console.GetThreadStopReason(threadId);

        public XBOX_CONTEXT_EX GetThreadContext(uint threadId, XboxContextFlags flags = XboxContextFlags.Full)
            => _console.GetThreadContext(threadId, flags);

        public void SetThreadContext(uint threadId, XBOX_CONTEXT_EX ctx)
            => _console.SetThreadContext(threadId, ctx);

        public void StopOn(XboxStopOnFlags flags)   => _console.StopOn(flags);
        public void NoStopOn(XboxStopOnFlags flags) => _console.NoStopOn(flags);

        public bool IsDebuggerPresent() => _console.IsDebuggerPresent();
        public uint GetPid() => _console.GetPid();
        public string GetAltAddress() => _console.GetAltAddress();
        public void TriggerCrashDump() => _console.TriggerCrashDump();

        #endregion

        #region Memory walk + checksum (passthroughs)

        public uint GetMemoryChecksum(uint address, uint length, uint blockSize = 0x1000)
            => _console.GetMemoryChecksum(address, length, blockSize);

        public System.Collections.Generic.List<XboxMemoryRegion> WalkCommittedMemory()
            => _console.WalkCommittedMemory();

        #endregion
    }