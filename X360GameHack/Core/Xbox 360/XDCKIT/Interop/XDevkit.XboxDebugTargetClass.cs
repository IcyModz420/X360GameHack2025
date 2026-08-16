#nullable disable
using System;

namespace XDevkit
{
    /// <summary>
    /// Managed implementation of <see cref="IXboxDebugTarget"/> wrapping the
    /// native <see cref="global::XboxConsole"/> transport.
    /// </summary>
    public sealed class XboxDebugTargetClass : IXboxDebugTarget
    {
        private readonly XboxConsoleClass _parent;
        private readonly global::XboxConsole _host;
        private XboxModulesShim _modules;
        private XboxThreadsShim _threads;
        private XboxMemoryRegionsShim _regions;

        internal XboxDebugTargetClass(XboxConsoleClass parent, global::XboxConsole host)
        {
            _parent = parent;
            _host = host;
        }

        public string Name => _host.Name;

        public bool IsDump => false;

        public XboxManager XboxManager => _parent.Manager;

        public XboxConsole Console => _parent;

        public void ConnectAsDebugger(string debuggerName, XboxDebugConnectFlags flags)
        {
            bool force = (flags & XboxDebugConnectFlags.Force) != 0;
            _host.AttachDebugger(debuggerName, null, 0, force);
        }

        public void DisconnectAsDebugger() => _host.DetachDebugger();

        public bool IsDebuggerConnected(out string debuggerName, out string userName)
            => _host.DebugTarget.IsDebuggerConnected(out debuggerName, out userName);

        public IXboxModules Modules => _modules ??= new XboxModulesShim(_host);

        public IXboxThreads Threads => _threads ??= new XboxThreadsShim(_host);

        public void GetMemory(uint address, uint bytesToRead, byte[] data, out uint bytesRead)
            => _host.GetMemory(address, bytesToRead, data, out bytesRead);

        public void SetMemory(uint address, uint bytesToWrite, byte[] data, out uint bytesWritten)
            => _host.SetMemory(address, bytesToWrite, data, out bytesWritten);

        public void GetMemory_cpp(uint address, uint bytesToRead, out byte data, out uint bytesRead)
            => throw new NotSupportedException("GetMemory_cpp is not supported by XDCKIT.");

        public void SetMemory_cpp(uint address, uint bytesToWrite, ref byte data, out uint bytesWritten)
            => throw new NotSupportedException("SetMemory_cpp is not supported by XDCKIT.");

        public void InvalidateMemoryCache(bool executablePages, uint address, uint size)
            => _host.InvalidateMemoryCache(executablePages, address, size);

        public bool MemoryCacheEnabled { get; set; } = true;

        public IXboxMemoryRegions MemoryRegions => _regions ??= new XboxMemoryRegionsShim(_host);

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

        public void StopOn(XboxStopOnFlags stopOn, bool stop)
        {
            var mapped = XDevkitInteropUtil.MapStopOnToXbdm(stopOn);
            if (stop) _host.StopOn(mapped);
            else _host.NoStopOn(mapped);
        }

        public void Stop(out bool alreadyStopped)
        {
            alreadyStopped = _host.ExecutionState == global::XboxExecutionState.Stopped;
            _host.Stop();
        }

        public void Go(out bool notStopped)
        {
            notStopped = _host.ExecutionState != global::XboxExecutionState.Stopped;
            _host.Go();
        }

        public void SetBreakpoint(uint address) => _host.SetBreakpoint(address);

        public void RemoveBreakpoint(uint address) => _host.ClearBreakpoint(address);

        public void RemoveAllBreakpoints() => _host.ClearAllBreakpoints();

        public void SetInitialBreakpoint() => _host.SetInitialBreakpoint();

        public void SetDataBreakpoint(uint address, XboxBreakpointType type, uint size)
            => _host.SetDataBreakpoint(XDevkitInteropUtil.MapDataBp(type), address, size);

        public void IsBreakpoint(uint address, out XboxBreakpointType type)
        {
            type = _host.IsBreak(address) ? XboxBreakpointType.OnExecute : XboxBreakpointType.NoBreakpoint;
        }

        public void WriteDump(string filename, XboxDumpFlags type)
            => throw new NotSupportedException("WriteDump is not implemented in XDCKIT.");

        public void CopyEventInfo(out XBOX_EVENT_INFO eventInfoDest, ref XBOX_EVENT_INFO eventInfoSource)
            => eventInfoDest = eventInfoSource;

        public void FreeEventInfo(ref XBOX_EVENT_INFO eventInfo) { }

        public void PgoStartDataCollection(uint pgoModule) => _host.PgoStartDataCollection(pgoModule);

        public void PgoStopDataCollection(uint pgoModule) => _host.PgoStopDataCollection(pgoModule);

        public void PgoSaveSnapshot(string phase, bool reset, uint pgoModule)
            => _host.PgoSaveSnapshot(phase, reset, pgoModule);

        public void PgoSetAllocScale(uint pgoModule, uint bufferAllocScale)
            => _host.PgoSetAllocScale(pgoModule, bufferAllocScale);
    }
}
