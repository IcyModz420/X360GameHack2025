// =============================================================================
// Xbox Direct Connect Kit (XDCKIT)
// XDCKIT.Enums.cs - All enumerations used by the library
// =============================================================================
// XDCKIT is a managed library that imitates Microsoft's XDevkit COM interop
// (XboxManager / IXboxConsole / IXboxDebugTarget) while talking directly to
// the Xbox 360 debug monitor (xbdm) over TCP/730. It bundles consolefeatures
// remote-call support, typed memory R/W, file system, controller automation
// and the XNotify/XMessageBox UIs into a single, instance-based API that
// supports multiple consoles in the same process.
// =============================================================================

    #region Connection / Protocol

    /// <summary>
    /// xbdm response status codes (the leading three-digit number returned
    /// before every line). Matches XDevkit conventions.
    /// </summary>
    public enum XboxResponseType
    {
        // Successful responses ---------------------------------------------------
        SingleResponse = 200,            // OK, single line
        Connected = 201,                 // Initial banner from console
        MultiResponse = 202,             // Multi-line, terminated by "."
        BinaryResponse = 203,            // Binary stream follows
        ReadyForBinary = 204,            // Server ready to receive binary
        NowNotifySession = 205,          // Channel switched to notify session

        // Errors -----------------------------------------------------------------
        UndefinedError = 400,
        MaxConnectionsExceeded = 401,
        FileNotFound = 402,
        NoSuchModule = 403,
        MemoryNotMapped = 404,
        NoSuchThread = 405,
        ClockNotSet = 406,
        UnknownCommand = 407,
        NotStopped = 408,
        FileMustBeCopied = 409,
        FileAlreadyExists = 410,
        DirectoryNotEmpty = 411,
        BadFileName = 412,
        FileCannotBeCreated = 413,
        AccessDenied = 414,
        NoRoomOnDevice = 415,
        NotDebuggable = 416,
        TypeInvalid = 417,
        DataNotAvailable = 418,
        BoxIsNotLocked = 420,
        KeyExchangeRequired = 421,
        DedicatedConnectionRequired = 422,
        InvalidArgument = 423,
        ProfileNotStarted = 424,
        ProfileAlreadyStarted = 425,
        D3DDebugCommandNotImplemented = 480,
        D3DInvalidSurface = 481,
        VxTaskPending = 496,
        VxTooManySessions = 497,
    }

    /// <summary>How long <c>XboxClient.Wait</c> should block.</summary>
    public enum WaitType
    {
        None,
        Partial,    // wait until at least one byte is available
        Full,       // wait for data to start AND stop arriving
        Idle        // wait for incoming traffic to stop
    }

    #endregion

    #region Console identity / hardware

    public enum XboxConsoleType
    {
        DevelopmentKit,
        TestKit,
        ReviewerKit,
        NotConnected
    }

    public enum XboxConsoleFeatures
    {
        Debugging = 1,
        SecondaryNIC = 2,
        GB_RAM = 4
    }

    /// <summary>
    /// Items that can be queried from <c>systeminfo</c>.
    /// </summary>
    public enum SystemInfo
    {
        HDD,
        Type,
        Platform,
        System,
        BaseKrnlVersion,
        KrnlVersion,
        XDKVersion
    }

    public enum XboxColor
    {
        Black,
        Blue,
        BlueGray,
        White,
        NoSidecar
    }

    public enum LEDState
    {
        OFF = 0,
        RED = 8,
        GREEN = 128,
        ORANGE = 136
    }

    public enum TemperatureFlag
    {
        CPU = 0,
        GPU = 1,
        EDRAM = 2,
        MotherBoard = 3
    }

    #endregion

    #region Reboot / lifecycle

    /// <summary>Quick reboot helper enum used by <c>XboxConsole.Reboot(XboxReboot)</c>.</summary>
    public enum XboxReboot
    {
        Cold = 2,
        Warm = 4
    }

    /// <summary>Bit flags for <c>magicboot</c> (matches XDevkit XboxRebootFlags).</summary>
    [System.Flags]
    public enum XboxRebootFlags
    {
        Title = 0,
        Wait = 1,
        Cold = 2,
        Warm = 4,
        Stop = 8
    }

    public enum XboxExecutionState
    {
        Pending,
        Rebooting,
        Running,
        Stopped,
        TitlePending,
        TitleRebooting,
        Unknown
    }

    public enum ThreadType
    {
        System,
        Title
    }

    public enum TrayState
    {
        Open,
        Close
    }

    public enum TRAY_STATE
    {
        Open,
        Unknown,
        Closed,
        Opening,
        Closing
    }

    #endregion

    #region File system

    public enum XboxCreateDisposition
    {
        CreateNew = 1,
        CreateAlways = 2,
        OpenExisting = 3,
        OpenAlways = 4
    }

    public enum XboxAccessFlags
    {
        Read = 1,
        Write = 2,
        Control = 4,
        Configure = 8,
        Manage = 16
    }

    public enum XboxShareMode
    {
        ShareNone = 0,
        ShareRead = 1,
        ShareWrite = 2,
        ShareDelete = 4
    }

    /// <summary>Common Xbox 360 logical drive names.</summary>
    public enum XboxDrives
    {
        HDD,
        HDD0,
        INTUSB,
        USB0,
        USB1,
        USB2,
        USB3,
        USB4,
        CdRom0,
        DVD,
        GAME,
        D,
        DASHUSER,
        media,
        SysCache0,
        SysCache1
    }

    #endregion

    #region Debugger / module flags

    public enum XboxBreakpointType
    {
        OnWrite,
        NoBreakpoint,
        OnRead,
        OnExecuteHW,
        OnExecute
    }

    /// <summary>
    /// Flags accepted by xbdm <c>stopon</c> / <c>nostopon</c>.  The token
    /// names emitted on the wire are taken straight from
    /// dmserv.c HrDoStopOn (<c>fce</c>, <c>debugstr</c>, <c>createthread</c>,
    /// <c>stacktrace</c>, <c>modload</c>, <c>all</c>).
    /// </summary>
    [System.Flags]
    public enum XboxStopOnFlags
    {
        None = 0,
        OnFirstChanceException = 1,   // fce
        OnDebugString = 2,            // debugstr
        OnThreadCreate = 4,           // createthread
        OnStackTrace = 8,             // stacktrace
        OnModuleLoad = 16,            // modload
        All = OnFirstChanceException | OnDebugString | OnThreadCreate | OnStackTrace | OnModuleLoad
    }

    public enum XboxEventDeferFlags
    {
        CanDeferExecutionBreak = 1,
        CanDeferDebugString = 2,
        CanDeferSingleStep = 4,
        CanDeferAssertionFailed = 8,
        CanDeferAssertionFailedEx = 16,
        CanDeferDataBreak = 32,
        CanDeferRIP = 64
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
        PgoModuleStartup
    }

    public enum XboxDumpFlags
    {
        Normal = 0,
        WithDataSegs = 1,
        WithFullMemory = 2,
        WithHandleData = 4,
        FilterMemory = 8,
        ScanMemory = 16,
        WithUnloadedModules = 32,
        WithIndirectlyReferencedMemory = 64,
        FilterModulePaths = 128,
        WithProcessThreadData = 256,
        WithPrivateReadWriteMemory = 512
    }

    public enum XboxModuleInfoFlags
    {
        Main = 1,
        Tls = 2,
        Dll = 4
    }

    public enum XboxSectionInfoFlags
    {
        Loaded = 1,
        Readable = 2,
        Writeable = 4,
        Executable = 8,
        Uninitialized = 16
    }

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
        UserReadWrite = 8192
    }

    public enum XboxFunctionType
    {
        NoPData = -1,
        SaveMillicode = 0,
        NoHandler = 1,
        RestoreMillicode = 2,
        Handler = 3
    }

    public enum XboxSelectConsoleFlags
    {
        NoPromptIfDefaultExists,
        NoPromptIfOnlyOne,
        FilterByAccess
    }

    #endregion

    #region Live / sign-in

    public enum SignInState
    {
        NotSignedIn,
        SignedInLocally,
        SignedInToLive
    }

    public enum FriendStatus
    {
        Offline = -1,
        Online = 0,
        Away = 0x10000,
        Busy = 0x20000
    }

    public enum FriendRequestStatus
    {
        RequestSent,
        RequestReceived,
        RequestAccepted
    }

    public enum XboxSignIn
    {
        QuickSignin = 700
    }

    #endregion

    #region UI / messages

    public enum XMessageBoxIcons
    {
        XBM_NOICON,
        XMB_ERRORICON,
        XMB_WARNINGICON,
        XMB_ALERTICON
    }

    /// <summary>
    /// Guide / dashboard XAM ordinals.
    /// Pass these to <c>XboxConsole.XboxShortcut(...)</c>.
    /// </summary>
    public enum XboxShortcuts
    {
        // Core navigation
        Recent = 0x2C8,
        Guide_Button = 0x506,

        // Games & apps tab
        Achievements = 0x2D0,
        Awards = 0x03C6,
        My_Games,
        Active_Downloads = 0x02E7,
        Redeem_Code,

        // Main guide
        XboxHome,
        Friends = 0x2BF,
        Party = 0x0305,
        Messages = 0x2C0,
        Beacons_And_Activiy = 0xB39,
        Private_Chat = 0x2C2,
        Open_Tray = 0x60,
        Close_Tray = 0x62,

        // Media
        System_Video_Player = 2,
        System_Music_Player = 1,
        Picture_Viewer,
        Windows_Media_Center,
        Select_Music = 0,

        // Settings
        DriveSelector = 5,
        Profile = 0x2c4,
        Preferences,
        Family_Settings,
        System_Settings,
        Account_Management = 4,
        Turn_Off_Console = 0x0295,
        AvatarEditor = 0xB3A
    }

    public enum Xbox_Party_Options
    {
        CreateParty = 0xafc,
        PartySettings = 0xb08,
        InviteOnly = 1,
        Kick = 0xb02,
        OpenParty = 0,
        JoinParty = 0xb01,
        AltJoinParty = 0xb1b,
        LeaveParty = 0xafd,
        InvitePlayer = 0xb15
    }

    #endregion

    #region Debugger / breakpoint enums (DmDataBreakpoint)

    /// <summary>
    /// Hardware data-breakpoint kind passed to
    /// <c>XboxConsole.SetDataBreakpoint</c>.  Mirrors the <c>DMBREAK_*</c>
    /// constants in xbdm.dll.
    /// </summary>
    public enum XboxDataBreakpointType
    {
        OnRead,
        OnWrite,
        OnReadWrite,
        OnExecute
    }

    #endregion

    #region Crash-dump mode (DmGetDumpMode / DmSetDumpMode)

    /// <summary>
    /// Crash dump mode supported by xbdm <c>dumpmode</c>.  Names match the
    /// positional flags from dmserv.c <c>rgszDumpMode</c>.
    /// </summary>
    public enum XboxDumpMode
    {
        Enabled,
        Partial,
        Disabled
    }

    #endregion

    #region NetSim direction (DmNetSim*)

    /// <summary>Direction selector for a NetSim queue.</summary>
    public enum NetSimDirection
    {
        In,
        Out
    }

    #endregion