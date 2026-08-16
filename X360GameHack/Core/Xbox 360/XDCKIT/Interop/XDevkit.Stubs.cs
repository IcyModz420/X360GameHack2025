#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace XDevkit
{
    internal static class XDevkitInteropUtil
    {
        public static uint IpStringToUInt(string ip)
        {
            if (string.IsNullOrEmpty(ip) || ip == "0.0.0.0") return 0;
            if (!IPAddress.TryParse(ip, out var addr)) return 0;
            var b = addr.GetAddressBytes();
            if (b.Length != 4) return 0;
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        public static string UIntToIpString(uint v)
        {
            if (v == 0) return "0.0.0.0";
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}",
                (v >> 24) & 0xFF, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        }

        public static global::XboxStopOnFlags MapStopOnToXbdm(XboxStopOnFlags f)
        {
            var r = global::XboxStopOnFlags.None;
            if ((f & XboxStopOnFlags.OnThreadCreate) != 0) r |= global::XboxStopOnFlags.OnThreadCreate;
            if ((f & XboxStopOnFlags.OnFirstChanceException) != 0) r |= global::XboxStopOnFlags.OnFirstChanceException;
            if ((f & XboxStopOnFlags.OnDebugString) != 0) r |= global::XboxStopOnFlags.OnDebugString;
            if ((f & XboxStopOnFlags.OnStackTrace) != 0) r |= global::XboxStopOnFlags.OnStackTrace;
            if ((f & XboxStopOnFlags.OnModuleLoad) != 0) r |= global::XboxStopOnFlags.OnModuleLoad;
            return r;
        }

        public static global::XboxDataBreakpointType MapDataBp(XboxBreakpointType t)
        {
            switch (t)
            {
                case XboxBreakpointType.OnRead: return global::XboxDataBreakpointType.OnRead;
                case XboxBreakpointType.OnWrite: return global::XboxDataBreakpointType.OnWrite;
                case XboxBreakpointType.OnExecute:
                case XboxBreakpointType.OnExecuteHW: return global::XboxDataBreakpointType.OnExecute;
                default: return global::XboxDataBreakpointType.OnRead;
            }
        }

        public static XboxConsoleType MapConsoleType(global::XboxConsoleType t)
        {
            switch (t)
            {
                case global::XboxConsoleType.TestKit: return XboxConsoleType.TestKit;
                case global::XboxConsoleType.ReviewerKit: return XboxConsoleType.ReviewerKit;
                default: return XboxConsoleType.DevelopmentKit;
            }
        }

        public static XboxDumpMode MapDumpModeGet(global::XboxDumpMode m)
        {
            switch (m)
            {
                case global::XboxDumpMode.Partial: return XboxDumpMode.Smart;
                case global::XboxDumpMode.Enabled: return XboxDumpMode.Enabled;
                default: return XboxDumpMode.Disabled;
            }
        }

        public static global::XboxDumpMode MapDumpModeSet(XboxDumpMode m)
        {
            switch (m)
            {
                case XboxDumpMode.Smart: return global::XboxDumpMode.Partial;
                case XboxDumpMode.Enabled: return global::XboxDumpMode.Enabled;
                default: return global::XboxDumpMode.Disabled;
            }
        }

        public static XboxExecutionState MapExec(global::XboxExecutionState s)
        {
            switch (s)
            {
                case global::XboxExecutionState.Stopped: return XboxExecutionState.Stopped;
                case global::XboxExecutionState.Running: return XboxExecutionState.Running;
                case global::XboxExecutionState.Rebooting: return XboxExecutionState.Rebooting;
                case global::XboxExecutionState.Pending: return XboxExecutionState.Pending;
                case global::XboxExecutionState.TitleRebooting: return XboxExecutionState.RebootingTitle;
                case global::XboxExecutionState.TitlePending: return XboxExecutionState.PendingTitle;
                default: return XboxExecutionState.Running;
            }
        }
    }

    internal sealed class XboxConsolesList : IXboxConsoles
    {
        private readonly XboxManagerClass _owner;

        public XboxConsolesList(XboxManagerClass owner) => _owner = owner;

        public string this[int index] => _owner.Inner.Consoles[index];

        public int Count => _owner.Inner.Consoles.Count;

        public IEnumerator GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return this[i];
        }
    }

    internal sealed class XboxExecutableShim : IXboxExecutable
    {
        private readonly string _name;
        public XboxExecutableShim(string name) => _name = name;
        public string GetPEModuleName() => _name;
    }

    internal sealed class XboxSectionShim : IXboxSection
    {
        private readonly XBOX_SECTION_INFO _info;
        public XboxSectionShim(XBOX_SECTION_INFO info) => _info = info;
        public XBOX_SECTION_INFO SectionInfo => _info;
    }

    internal sealed class XboxSectionsShim : IXboxSections
    {
        private readonly List<XBOX_SECTION_INFO> _rows;

        public XboxSectionsShim(List<XBOX_SECTION_INFO> rows) => _rows = rows ?? new List<XBOX_SECTION_INFO>();

        public IXboxSection this[int index] => new XboxSectionShim(_rows[index]);

        public int Count => _rows.Count;

        public IEnumerator GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return this[i];
        }
    }

    internal sealed class XboxModuleShim : IXboxModule
    {
        private readonly global::XboxConsole _c;
        private readonly global::ModuleInfo _m;
        private IXboxSections _sections;

        public XboxModuleShim(global::XboxConsole c, global::ModuleInfo m)
        {
            _c = c;
            _m = m;
        }

        public XBOX_MODULE_INFO ModuleInfo => new XBOX_MODULE_INFO
        {
            Name = _m.Name,
            FullName = _m.Name,
            BaseAddress = _m.BaseAddress,
            Size = _m.Size,
            TimeStamp = (uint)(_m.TimeStamp.Ticks & 0xFFFFFFFF),
            CheckSum = _m.Checksum,
            Flags = XboxModuleInfoFlags.Main,
        };

        public IXboxSections Sections
        {
            get
            {
                if (_sections != null) return _sections;
                var list = new List<XBOX_SECTION_INFO>();
                try
                {
                    foreach (var s in _c.GetModuleSections(_m.Name))
                    {
                        list.Add(new XBOX_SECTION_INFO
                        {
                            Name = s.Name,
                            BaseAddress = s.Base,
                            Size = s.Size,
                            Index = s.Index,
                            Flags = XboxSectionInfoFlags.Loaded | XboxSectionInfoFlags.Readable,
                        });
                    }
                }
                catch { /* ignore */ }
                _sections = new XboxSectionsShim(list);
                return _sections;
            }
        }

        public void GetFunctionInfo(uint address, out XBOX_FUNCTION_INFO functionInfo)
        {
            functionInfo = new XBOX_FUNCTION_INFO { FunctionType = XboxFunctionType.NoPData };
        }

        public uint OriginalSize => _m.Size;

        public IXboxExecutable Executable => new XboxExecutableShim(_m.Name);

        public uint GetEntryPointAddress() => _m.BaseAddress;
    }

    internal sealed class XboxModulesShim : IXboxModules
    {
        private readonly global::XboxConsole _c;
        private List<global::ModuleInfo> _cache;

        public XboxModulesShim(global::XboxConsole c) => _c = c;

        private void Refresh()
        {
            try { _cache = _c.GetModules() ?? new List<global::ModuleInfo>(); }
            catch { _cache = new List<global::ModuleInfo>(); }
        }

        public IXboxModule this[int index]
        {
            get
            {
                Refresh();
                return new XboxModuleShim(_c, _cache[index]);
            }
        }

        public int Count
        {
            get
            {
                Refresh();
                return _cache.Count;
            }
        }

        public IEnumerator GetEnumerator()
        {
            Refresh();
            for (int i = 0; i < _cache.Count; i++)
                yield return this[i];
        }
    }

    internal sealed class XboxThreadShim : IXboxThread
    {
        private readonly global::XboxConsole _c;
        private readonly uint _id;

        public XboxThreadShim(global::XboxConsole c, uint id)
        {
            _c = c;
            _id = id;
        }

        public uint ThreadId => _id;

        public XBOX_THREAD_INFO ThreadInfo
        {
            get
            {
                var ti = _c.GetThreadInfo(_id);
                if (ti == null)
                    return new XBOX_THREAD_INFO { ThreadId = _id };
                return new XBOX_THREAD_INFO
                {
                    ThreadId = ti.ThreadId,
                    SuspendCount = ti.Suspend,
                    Priority = ti.Priority,
                    TlsBase = ti.TlsBase,
                    StartAddress = ti.StartAddress,
                    StackBase = ti.Base,
                    StackLimit = ti.Limit,
                    StackSlackSpace = ti.Slack,
                    CreateTime = ti.CreateTime,
                    Name = string.Empty,
                };
            }
        }

        public XBOX_EVENT_INFO StopEventInfo => new XBOX_EVENT_INFO
        {
            Event = XboxDebugEventType.NoEvent,
            Thread = this,
        };

        public IXboxStackFrame TopOfStack => null;

        public void Halt() => _c.HaltThread(_id);

        public void Continue(bool exception) => _c.ContinueThread(_id, exception);

        public void Suspend() => _c.SuspendThread(_id);

        public void Resume() => _c.ResumeThread(_id);

        public uint CurrentProcessor
        {
            get
            {
                var ti = _c.GetThreadInfo(_id);
                return ti?.CurrentProcessor ?? 0;
            }
        }

        public uint LastError
        {
            get
            {
                var ti = _c.GetThreadInfo(_id);
                return ti?.LastError ?? 0;
            }
        }
    }

    internal sealed class XboxThreadsShim : IXboxThreads
    {
        private readonly global::XboxConsole _c;

        public XboxThreadsShim(global::XboxConsole c) => _c = c;

        public IXboxThread this[int index]
        {
            get
            {
                var ids = _c.GetThreadIds();
                return new XboxThreadShim(_c, ids[index]);
            }
        }

        public int Count => _c.GetThreadIds().Length;

        public IEnumerator GetEnumerator()
        {
            var ids = _c.GetThreadIds();
            for (int i = 0; i < ids.Length; i++)
                yield return new XboxThreadShim(_c, ids[i]);
        }
    }

    internal sealed class XboxMemoryRegionShim : IXboxMemoryRegion
    {
        private readonly global::XboxMemoryRegion _r;
        public XboxMemoryRegionShim(global::XboxMemoryRegion r) => _r = r;
        public int BaseAddress => (int)_r.BaseAddress;
        public int RegionSize => (int)_r.Size;
        public XboxMemoryRegionFlags Flags => (XboxMemoryRegionFlags)(int)_r.Flags;
    }

    internal sealed class XboxMemoryRegionsShim : IXboxMemoryRegions
    {
        private readonly global::XboxConsole _c;

        public XboxMemoryRegionsShim(global::XboxConsole c) => _c = c;

        public IXboxMemoryRegion this[int index] => new XboxMemoryRegionShim(_c.WalkCommittedMemory()[index]);

        public int Count => _c.WalkCommittedMemory().Count;

        public IEnumerator GetEnumerator()
        {
            foreach (var r in _c.WalkCommittedMemory())
                yield return new XboxMemoryRegionShim(r);
        }
    }

    internal sealed class XboxFileEntryShim : IXboxFile
    {
        public XboxFileEntryShim(string name, global::XboxDirEntry e)
        {
            Name = name;
            _entry = e;
        }

        private readonly global::XboxDirEntry _entry;

        public string Name { get; set; }

        public object CreationTime { get => _entry.CreateTime; set { } }

        public object ChangeTime { get => _entry.ChangeTime; set { } }

        public ulong Size { get => _entry.Size; set { } }

        public bool IsReadOnly { get; set; }

        public bool IsDirectory => _entry.IsDirectory;
    }

    internal sealed class XboxFilesShim : IXboxFiles
    {
        private readonly List<IXboxFile> _files;

        public XboxFilesShim(List<IXboxFile> files) => _files = files;

        public IXboxFile this[int index] => _files[index];

        public int Count => _files.Count;

        public IEnumerator GetEnumerator() => _files.GetEnumerator();
    }

    internal sealed class XboxAutomationShim : IXboxAutomation
    {
        private readonly global::XboxConsole _c;

        public XboxAutomationShim(global::XboxConsole c) => _c = c;

        public void GetInputProcess(uint userIndex, out bool systemProcess)
            => _c.Automation.GetInputProcess((global::UserIndex)userIndex, out systemProcess);

        public void BindController(uint userIndex, uint queueLength)
            => _c.Automation.BindController((global::UserIndex)userIndex, queueLength);

        public void UnbindController(uint userIndex)
            => _c.Automation.UnbindController((global::UserIndex)userIndex);

        public void ConnectController(uint userIndex)
            => _c.Automation.ConnectController((global::UserIndex)userIndex);

        public void DisconnectController(uint userIndex)
            => _c.Automation.DisconnectController((global::UserIndex)userIndex);

        public void SetGamepadState(uint userIndex, ref XBOX_AUTOMATION_GAMEPAD gamepad)
        {
            var g = new global::XBOX_AUTOMATION_GAMEPAD
            {
                Buttons = (global::XboxAutomationButtonFlags)gamepad.Buttons,
                LeftTrigger = gamepad.LeftTrigger,
                RightTrigger = gamepad.RightTrigger,
                LeftThumbX = gamepad.LeftThumbX,
                LeftThumbY = gamepad.LeftThumbY,
                RightThumbX = gamepad.RightThumbX,
                RightThumbY = gamepad.RightThumbY,
            };
            _c.Automation.SetGamepadState((global::UserIndex)userIndex, ref g);
        }

        public void QueueGamepadState_cpp(
            uint userIndex,
            ref XBOX_AUTOMATION_GAMEPAD gamepadArray,
            ref uint timedDurationArray,
            ref uint countDurationArray,
            uint itemCount,
            out uint itemsAddedToQueue)
            => throw new NotSupportedException("QueueGamepadState_cpp is not supported by XDCKIT.");

        public bool QueueGamepadState(
            uint userIndex,
            ref XBOX_AUTOMATION_GAMEPAD gamepad,
            uint timedDuration,
            uint countDuration)
        {
            var g = new global::XBOX_AUTOMATION_GAMEPAD
            {
                Buttons = (global::XboxAutomationButtonFlags)gamepad.Buttons,
                LeftTrigger = gamepad.LeftTrigger,
                RightTrigger = gamepad.RightTrigger,
                LeftThumbX = gamepad.LeftThumbX,
                LeftThumbY = gamepad.LeftThumbY,
                RightThumbX = gamepad.RightThumbX,
                RightThumbY = gamepad.RightThumbY,
            };
            return _c.Automation.QueueGamepadState((global::UserIndex)userIndex, ref g, timedDuration, countDuration);
        }

        public void ClearGamepadQueue(uint userIndex)
            => _c.Automation.ClearGamepadQueue((global::UserIndex)userIndex);

        public void QueryGamepadQueue(
            uint userIndex,
            out uint queueLength,
            out uint itemsInQueue,
            out uint timedDurationRemaining,
            out uint countDurationRemaining)
            => _c.Automation.QueryGamepadQueue((global::UserIndex)userIndex,
                out queueLength, out itemsInQueue, out timedDurationRemaining, out countDurationRemaining);

        public void GetUserDefaultProfile(out long xuid) => _c.Automation.GetUserDefaultProfile(out xuid);

        public void SetUserDefaultProfile(long xuid) => _c.Automation.SetUserDefaultProfile(xuid);
    }
}
