// Managed copies of XDevkit SDK value types (see repo xdevkit/ decompilation).
// No [ComImport] — safe for XDCKIT to implement in pure C#.
#nullable disable
using System.Runtime.InteropServices;

namespace XDevkit
{
    public enum XboxRebootFlags
    {
        Title = 0,
        Wait = 1,
        Cold = 2,
        Warm = 4,
        Stop = 8,
    }

    public enum XboxConsoleType
    {
        DevelopmentKit,
        TestKit,
        ReviewerKit,
    }

    public enum XboxDumpMode
    {
        Smart,
        Enabled,
        Disabled,
    }

    public enum XboxDumpReportFlags
    {
        FormatFullHeap = 0,
        LocalDestination = 0,
        PromptToReport = 0,
        AlwaysReport = 1,
        NeverReport = 2,
        DestinationGroup = 15,
        ReportGroup = 15,
        RemoteDestination = 16,
        FormatPartialHeap = 256,
        FormatNoHeap = 512,
        FormatRetail = 1024,
        FormatGroup = 3840,
    }

    public enum XboxEventDeferFlags
    {
        CanDeferExecutionBreak = 1,
        CanDeferDebugString = 2,
        CanDeferSingleStep = 4,
        CanDeferAssertionFailed = 8,
        CanDeferAssertionFailedEx = 16,
        CanDeferDataBreak = 32,
        CanDeferRIP = 64,
    }

    public enum XboxCreateDisposition
    {
        CreateNew = 1,
        CreateAlways = 2,
        OpenExisting = 3,
        OpenAlways = 4,
    }

    public enum XboxAccessFlags
    {
        None = 0,
        Read = 1,
        Write = 2,
        Control = 4,
    }

    public enum XboxSelectConsoleFlags
    {
        None = 0,
        ShowIp = 1,
    }

    public enum XboxDebugConnectFlags
    {
        Force = 1,
        MonitorOnly = 2,
    }

    public enum XboxBreakpointType
    {
        NoBreakpoint,
        OnWrite,
        OnRead,
        OnExecuteHW,
        OnExecute,
    }

    /// <summary>Bit flags as used by the official XDevkit interop (different numeric layout than xbdm token flags).</summary>
    [System.Flags]
    public enum XboxStopOnFlags
    {
        OnThreadCreate = 1,
        OnFirstChanceException = 2,
        OnDebugString = 4,
        OnStackTrace = 8,
        OnModuleLoad = 16,
        OnTitleLaunch = 32,
        OnPgoModuleStartup = 64,
    }

    public enum XboxDumpFlags
    {
        Normal = 0,
        Full = 1,
    }

    public enum XboxConsoleFeatures
    {
        Debugging = 1,
        SecondaryNIC = 2,
        GB_RAM = 4,
    }

    public enum XboxModuleInfoFlags
    {
        Main = 1,
        Tls = 2,
        Dll = 4,
    }

    public enum XboxDebugEventType
    {
        NoEvent,
        ExecutionBreak,
        DebugString,
        ExecStateChange,
        SingleStep,
        ModuleLoad,
        ModuleUnload,
        ThreadCreate,
        ThreadDestroy,
        Exception,
        AssertionFailed,
        AssertionFailedEx,
        DataBreak,
        RIP,
        SectionLoad,
        SectionUnload,
        StackTrace,
        FiberCreate,
        FiberDestroy,
        BugCheck,
        PgoModuleStartup,
    }

    public enum XboxExecutionState
    {
        Stopped,
        Running,
        Rebooting,
        Pending,
        RebootingTitle,
        PendingTitle,
    }

    public enum XboxExceptionFlags
    {
        Noncontinuable = 1,
        FirstChance = 2,
    }

    [System.Flags]
    public enum XboxMemoryRegionFlags
    {
        NoAccess = 1,
        ReadOnly = 2,
        ReadWrite = 4,
        WriteCopy = 8,
        Execute = 16,
        ExecuteRead = 32,
        ExecuteReadWrite = 64,
        ExecuteWriteCopy = 128,
        Guard = 256,
        NoCache = 512,
        WriteCombine = 1024,
        UserReadOnly = 4096,
        UserReadWrite = 8192,
    }

    public enum XboxSectionInfoFlags
    {
        Loaded = 1,
        Readable = 2,
        Writeable = 4,
        Executable = 8,
        Uninitialized = 16,
    }

    public enum XboxFunctionType
    {
        NoPData = -1,
        SaveMillicode = 0,
        NoHandler = 1,
        RestoreMillicode = 2,
        Handler = 3,
    }

    public enum XboxRegisters32
    {
        msr, iar, lr, cr, xer,
    }

    public enum XboxRegisters64
    {
        ctr, r0, r1, r2, r3, r4, r5, r6, r7, r8, r9, r10,
        r11, r12, r13, r14, r15, r16, r17, r18, r19, r20, r21, r22, r23, r24, r25, r26, r27, r28, r29, r30, r31,
    }

    public enum XboxRegistersDouble
    {
        fp0, fp1, fp2, fp3, fp4, fp5, fp6, fp7, fp8, fp9, fp10, fp11, fp12, fp13, fp14, fp15,
        fp16, fp17, fp18, fp19, fp20, fp21, fp22, fp23, fp24, fp25, fp26, fp27, fp28, fp29, fp30, fp31, fpscr,
    }

    public enum XboxRegistersVector
    {
        v0, v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14, v15, v16, v17, v18, v19, v20,
        v21, v22, v23, v24, v25, v26, v27, v28, v29, v30, v31, v32, v33, v34, v35, v36, v37, v38, v39, v40,
        v41, v42, v43, v44, v45, v46, v47, v48, v49, v50, v51, v52, v53, v54, v55, v56, v57, v58, v59, v60,
        v61, v62, v63, v64, v65, v66, v67, v68, v69, v70, v71, v72, v73, v74, v75, v76, v77, v78, v79, v80,
        v81, v82, v83, v84, v85, v86, v87, v88, v89, v90, v91, v92, v93, v94, v95, v96, v97, v98, v99, v100,
        v101, v102, v103, v104, v105, v106, v107, v108, v109, v110, v111, v112, v113, v114, v115, v116, v117, v118, v119, v120,
        v121, v122, v123, v124, v125, v126, v127, vscr,
    }

    [System.Flags]
    public enum XboxAutomationButtonFlags
    {
        None = 0,
        DPadUp = 0x1,
        DPadDown = 0x2,
        DPadLeft = 0x4,
        DPadRight = 0x8,
        StartButton = 0x10,
        BackButton = 0x20,
        LeftThumbButton = 0x40,
        RightThumbButton = 0x80,
        LeftShoulderButton = 0x100,
        RightShoulderButton = 0x200,
        Xbox360_Button = 0x400,
        Bind_Button = 0x800,
        A_Button = 0x1000,
        B_Button = 0x2000,
        X_Button = 0x4000,
        Y_Button = 0x8000,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct XBOX_AUTOMATION_GAMEPAD
    {
        public XboxAutomationButtonFlags Buttons;
        public uint LeftTrigger;
        public uint RightTrigger;
        public int LeftThumbX;
        public int LeftThumbY;
        public int RightThumbX;
        public int RightThumbY;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct XBOX_PROCESS_INFO
    {
        public uint ProcessId;
        [MarshalAs(UnmanagedType.BStr)]
        public string ProgramName;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct XBOX_DUMP_SETTINGS
    {
        public XboxDumpReportFlags Flags;
        [MarshalAs(UnmanagedType.BStr)]
        public string NetworkPath;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct XBOX_THREAD_INFO
    {
        public uint ThreadId;
        public uint SuspendCount;
        public uint Priority;
        public uint TlsBase;
        public uint StartAddress;
        public uint StackBase;
        public uint StackLimit;
        public uint StackSlackSpace;
        [MarshalAs(UnmanagedType.Struct)]
        public object CreateTime;
        [MarshalAs(UnmanagedType.BStr)]
        public string Name;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct XBOX_MODULE_INFO
    {
        [MarshalAs(UnmanagedType.BStr)]
        public string Name;
        [MarshalAs(UnmanagedType.BStr)]
        public string FullName;
        public uint BaseAddress;
        public uint Size;
        public uint TimeStamp;
        public uint CheckSum;
        public XboxModuleInfoFlags Flags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct XBOX_SECTION_INFO
    {
        [MarshalAs(UnmanagedType.BStr)]
        public string Name;
        public uint BaseAddress;
        public uint Size;
        public uint Index;
        public XboxSectionInfoFlags Flags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct XBOX_FUNCTION_INFO
    {
        public XboxFunctionType FunctionType;
        public uint BeginAddress;
        public uint PrologEndAddress;
        public uint FunctionEndAddress;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct XBOX_EVENT_INFO
    {
        public XboxDebugEventType Event;
        public short IsThreadStopped;
        public IXboxThread Thread;
        public IXboxModule Module;
        public IXboxSection Section;
        public XboxExecutionState ExecState;
        [MarshalAs(UnmanagedType.BStr)]
        public string Message;
        public uint Code;
        public uint Address;
        public XboxExceptionFlags Flags;
        public uint ParameterCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 15)]
        public uint[] Parameters;
    }
}
