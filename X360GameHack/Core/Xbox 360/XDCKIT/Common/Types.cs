// =============================================================================
// XDCKIT.Types.cs - DTOs / value types used by the library
// =============================================================================
using System;
using System.Collections.Generic;

    #region Modules / sections (XDevkit-compatible shapes)

    /// <summary>
    /// Information about a loaded module (.xex / .dll).  Matches the layout
    /// returned by xbdm's <c>modules</c> command.
    /// </summary>
    public sealed class ModuleInfo
    {
        public string Name;
        public uint BaseAddress;
        public uint Size;
        public DateTime TimeStamp;
        public uint Checksum;
        public List<ModuleSection> Sections = new List<ModuleSection>();

        public override string ToString()
            => $"{{ Name: {Name} BaseAddress: 0x{BaseAddress:X8} Size: 0x{Size:X} TimeStamp: {TimeStamp} Checksum: 0x{Checksum:X8} }}";
    }

    /// <summary>Information about a section inside a module (returned by <c>modsections</c>).</summary>
    public sealed class ModuleSection
    {
        public string Name;
        public uint Base;
        public uint Size;
        public uint Index;
        public uint Flags;

        public override string ToString()
            => $"{{ Name: {Name} Base: 0x{Base:X8} Size: 0x{Size:X} Index: {Index} Flags: 0x{Flags:X} }}";
    }

    #endregion

    #region File system

    /// <summary>One row from <c>dirlist</c>.</summary>
    public sealed class XboxDirEntry
    {
        public string Name;
        public ulong Size;
        public DateTime CreateTime;
        public DateTime ChangeTime;
        public bool IsDirectory;

        public override string ToString()
            => $"{(IsDirectory ? "[D]" : "   ")} {Name,-40} {Size,12} bytes  modified {ChangeTime}";
    }

    /// <summary>Result of <c>drivefreespace</c>.</summary>
    public struct DriveSpaceInfo
    {
        public ulong FreeBytesAvailable;
        public ulong TotalBytes;
        public ulong TotalFreeBytes;
    }

    /// <summary>One drive returned by <c>drivelist</c>.</summary>
    public sealed class XboxDrive
    {
        public string DriveName;
        public DriveSpaceInfo Space;

        public override string ToString() => $"{DriveName} ({Space.TotalFreeBytes:N0} / {Space.TotalBytes:N0} bytes free)";
    }

    #endregion

    #region Threads

    /// <summary>
    /// One row from xbdm <c>threads</c> / <c>threadinfo</c>.  Field names match
    /// the keys emitted by xbdm dmserv.c HrReportThreadInfo.
    /// </summary>
    public sealed class XboxThreadInfo
    {
        public uint ThreadId;
        public uint Suspend;
        public uint Priority;
        public uint TlsBase;
        /// <summary>The thread's start address (xbdm key <c>start</c>).</summary>
        public uint StartAddress;
        public uint Base;
        public uint Limit;
        public uint Slack;
        public DateTime CreateTime;
        public uint NameAddress;
        public uint NameLength;
        /// <summary>Current core mask (xbdm key <c>proc</c>).</summary>
        public uint CurrentProcessor;
        /// <summary>Win32 last-error value (xbdm key <c>lasterr</c>).</summary>
        public uint LastError;
    }

    #endregion

    #region Screenshots / framebuffer

    /// <summary>
    /// Header fields parsed from the xbdm <c>screenshot</c> metadata line (same keys
    /// Microsoft <c>DmScreenShot</c> reads: pitch, width, height, format, offsetx/y,
    /// framebuffersize, sw/sh, colorspace).
    /// </summary>
    /// <remarks>
    /// Typical TCP payload (ASCII metadata before the binary blob) looks like:
    /// <c>pitch=0xd80 width=0x360 height=0x1e0 format=0x18280186 offsetx=0 offsety=0,
    /// framebuffersize=0x1c0000 sw=0x500 sh=0x2d0 colorspace=0</c>.
    /// <list type="bullet">
    /// <item><description><see cref="Pitch"/> is the byte stride per row. When capturing a 32bpp surface,
    /// <c>pitch == width * 4</c> for a linearised row (e.g. 0xD80 = 864×4).</description></item>
    /// <item><description><see cref="Width"/> / <see cref="Height"/> describe the **surface** size used for
    /// linear ARGB decode (here 864×480), not necessarily the same as the TV resolution.</description></item>
    /// <item><description><see cref="SubWidth"/> / <see cref="SubHeight"/> (<c>sw</c> / <c>sh</c>) are often the
    /// **logical** front-buffer size (e.g. 1280×720). Building a full-resolution PNG from <c>sw</c>×<c>sh</c>
    /// requires GPU tile detiling — XDCKIT only guarantees correct linear decode for <c>width</c>×<c>height</c>
    /// when <c>pitch == width*4</c>.</description></item>
    /// <item><description><see cref="Format"/> is usually a packed Xenon <b>XGFormat</b> word (e.g.
    /// <c>0x18280186</c> — low byte <c>0x86</c> = <c>GPUTEXTUREFORMAT_8_8_8_8</c>), not a plain
    /// <c>D3DFORMAT</c> enum.</description></item>
    /// <item><description><see cref="FrameBufferSize"/> is the byte count of the following binary read;
    /// it may be larger than <c>pitch * height</c> (padding / tail after the last row).</description></item>
    /// </list>
    /// </remarks>
    public struct ScreenshotInfo
    {
        public uint Pitch;
        public uint Width;
        public uint Height;
        public uint Format;
        public uint FrameBufferSize;
        public uint OffsetX;
        public uint OffsetY;
        /// <summary>Logical / sub-rectangle width (xbdm key <c>sw</c>), often full HD width.</summary>
        public uint SubWidth;
        /// <summary>Logical / sub-rectangle height (xbdm key <c>sh</c>), often full HD height.</summary>
        public uint SubHeight;
        public uint ColorSpace;
    }

    #endregion

    #region 3D vectors (used by mod menus)

    /// <summary>Big-endian 3-component float vector helper.</summary>
    public struct Vector
    {
        public float X, Y, Z;

        public Vector(float x, float y, float z) { X = x; Y = y; Z = z; }

        /// <summary>Deprecated lowercase accessor kept for source compatibility.</summary>
        [System.Obsolete("Use X (PascalCase) instead.")]
        public float x { get => X; set => X = value; }

        /// <summary>Deprecated lowercase accessor kept for source compatibility.</summary>
        [System.Obsolete("Use Y (PascalCase) instead.")]
        public float y { get => Y; set => Y = value; }

        /// <summary>Deprecated lowercase accessor kept for source compatibility.</summary>
        [System.Obsolete("Use Z (PascalCase) instead.")]
        public float z { get => Z; set => Z = value; }

        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    #endregion

    #region Progress callbacks

    /// <summary>Reports 0..100 progress for long running operations.</summary>
    public delegate void ProgressUpdateCallback(int percentage);

    #endregion

    #region Thread context (PowerPC / Xenon register file)

    /// <summary>
    /// Bit flags identifying which fields of <see cref="XBOX_CONTEXT_EX"/>
    /// are valid.  Mirrors the <c>CONTEXT_</c> flags in xbdm SDK headers.
    /// </summary>
    [System.Flags]
    public enum XboxContextFlags : uint
    {
        Control = 0x10000001,   // Msr, Iar, Lr, Ctr
        Integer = 0x10000002,   // Gpr0..Gpr31
        Floating = 0x10000008,  // Fpr0..Fpr31, Fpscr
        Vector = 0x10000010,    // Vr0..Vr127, Vscr
        Full = Control | Integer | Floating
    }

    /// <summary>
    /// PowerPC / Xenon CPU thread context as returned by xbdm's <c>getcontext</c>
    /// command.  Vector regs (128 entries of 16 bytes) are tracked but only the
    /// scalar set is parsed by default for performance.
    /// </summary>
    public sealed class XBOX_CONTEXT_EX
    {
        public XboxContextFlags ContextFlags;

        // Control registers
        public uint Msr, Iar, Lr, Ctr, Cr, Xer;

        /// <summary>32 general-purpose registers (R0..R31). 64-bit on Xenon.</summary>
        public ulong[] Gpr = new ulong[32];

        /// <summary>32 floating-point registers (F0..F31).</summary>
        public double[] Fpr = new double[32];

        public uint Fpscr;

        /// <summary>
        /// 128 vector registers (V0..V127), 16 bytes each.  Lazy-allocated
        /// only when <see cref="XboxContextFlags.Vector"/> was requested.
        /// </summary>
        public byte[][] Vr;

        public uint Vscr;
    }

    #endregion

    #region File attributes

    /// <summary>Result/parameter for DmGetFileAttributes / DmSetFileAttributes.</summary>
    public sealed class XboxFileAttributes
    {
        public ulong Size;
        public DateTime CreateTime;
        public DateTime ChangeTime;
        public bool ReadOnly;
        public bool Hidden;
        public bool System;
        public bool Directory;
        public bool Archive;
    }

    #endregion

    #region Network / NIC stats

    public sealed class XboxNicStats
    {
        public ulong PacketsReceived;
        public ulong PacketsSent;
        public ulong BytesReceived;
        public ulong BytesSent;
        public ulong ReceiveErrors;
        public ulong SendErrors;
        public ulong ReceiveDropped;
        public ulong SendDropped;
    }

    #endregion

    #region User accounts

    public sealed class XboxUser
    {
        public string Name;
        public XboxAccessFlags AccessFlags;
        public bool IsAdmin;

        public override string ToString() => $"{Name} ({AccessFlags}{(IsAdmin ? ",Admin" : string.Empty)})";
    }

    #endregion

    #region Memory map walking

    public sealed class XboxMemoryRegion
    {
        public uint BaseAddress;
        public uint Size;
        public XboxMemoryRegionFlags Flags;

        public override string ToString()
            => $"0x{BaseAddress:X8} - 0x{(BaseAddress + Size):X8} ({Size:N0} bytes) [{Flags}]";
    }

    #endregion

    #region Notification sessions

    /// <summary>
    /// Result of opening an asynchronous notification session.  The TCP
    /// listener handed back here lives until you call CloseNotificationSession.
    /// </summary>
    public sealed class XboxNotificationSession : IDisposable
    {
        public System.Net.Sockets.TcpListener Listener;
        public ushort Port;
        public string LocalEndpoint => Listener?.LocalEndpoint?.ToString();
        public bool IsRunning { get; internal set; }

        /// <summary>
        /// The currently registered handler.  Swap in place via
        /// <c>RegisterNotificationProcessor</c> on <c>XboxConsole</c>.
        /// </summary>
        public XboxNotificationHandler Handler { get; internal set; }

        public void Dispose()
        {
            try { IsRunning = false; Listener?.Stop(); } catch { /* ignore */ }
        }
    }

    /// <summary>Callback invoked when an xbdm notification arrives.</summary>
    public delegate void XboxNotificationHandler(string notificationLine);

    #endregion

    #region Crash dump settings (DmGetDumpSettings / DmSetDumpSettings)

    /// <summary>
    /// Parsed result of <c>dumpsettings</c> (DmGetDumpSettings).
    /// Mirrors the xbdm.dll wire fields rpt/dst/fmt/path.
    /// </summary>
    public sealed class XboxDumpSettings
    {
        /// <summary>Report flags ("rpt=...").</summary>
        public string Report;
        /// <summary>Destination ("dst=...").</summary>
        public string Destination;
        /// <summary>Format ("fmt=...").</summary>
        public string Format;
        /// <summary>Filesystem path on the console ("path=...").</summary>
        public string Path;
        /// <summary>Original 200-line text in case extra fields are present.</summary>
        public string RawLine;
    }

    #endregion

    #region Performance counters (DmWalkPerformanceCounters)

    /// <summary>
    /// One row of <c>PCLIST</c> (DmWalkPerformanceCounters).  Field names
    /// match xbdm.dll's reply tokens.
    /// </summary>
    public sealed class XboxPerformanceCounter
    {
        public string Name;
        public uint Type;
        /// <summary>The full raw key=value line in case extra fields are needed.</summary>
        public string RawLine;
    }

    #endregion

    #region XNet introspection (DmGetSockInfo / DmGetXn*)

    /// <summary>One row from <c>GETSOCKINFO</c> (DmGetSockInfo).</summary>
    public sealed class XboxSocketInfo
    {
        public uint   Handle;
        public string OwnerType;
        public string AddrFamily;
        public string SockType;
        public string Protocol;
        public string LocalAddress;
        public uint   LocalPort;
        public string RemoteAddress;
        public uint   RemotePort;
        public string TcpState;
        public string RawLine;
    }

    /// <summary>One row from <c>GETXNSECASSOCINFO</c> (DmGetXnSecAssocInfo).</summary>
    public sealed class XboxXnSecAssocInfo
    {
        public string OwnerType;
        public string ServiceId;
        public string XnAddr;
        public string XnKid;
        public string SecAddr;
        public string NatAddr;
        public uint   NatPort;
        public string RawLine;
    }

    /// <summary>One row from <c>GETXNKEYINFO</c> (DmGetXnKeyInfo).</summary>
    public sealed class XboxXnKeyInfo
    {
        public string XnKid;
        public uint   QosBps;
        public uint   QosDataSize;
        public string RawLine;
    }

    /// <summary>One row from <c>GETXNQOSLOOKUPINFO</c> (DmGetXnQosLookupInfo).</summary>
    public sealed class XboxXnQosLookupInfo
    {
        public uint   PReq;
        public uint   PTx;
        public uint   PRx;
        public uint   DataRx;
        public uint   Bps;
        public string TargetAddress;
        public uint   TargetPort;
        public uint   Retry;
        public string RawLine;
    }

    #endregion

    #region NetSim (DmNetSim*)

    /// <summary>Settings for a NetSim queue (insert / modify / get).</summary>
    public sealed class NetSimQueueSettings
    {
        public NetSimDirection Direction;
        public uint Index;
        /// <summary>Raw <c>cfg=...</c> string (whatever the SDK encodes).</summary>
        public string Config;
        /// <summary>Original line if returned from getq.</summary>
        public string RawLine;
    }

    /// <summary>Stats for a NetSim queue (getqstats).</summary>
    public sealed class NetSimQueueStats
    {
        public uint   TotalPackets;
        public uint   TotalBytes;
        public uint   DroppedPackets;
        public uint   DroppedBytes;
        public string RawLine;
    }

    /// <summary>One ipv4 redirect entry.</summary>
    public sealed class NetSimIpv4Redirect
    {
        public uint Index;
        public string RawLine;
    }

    #endregion

    #region Screenshot D3DFORMAT helper

    /// <summary>
    /// Direct3D surface format codes used by xbdm <c>screenshot</c> in the
    /// <c>format=0xX</c> metadata field.  Only two values are ever emitted:
    /// SDR <c>D3DFMT_LE_X8R8G8B8</c> (32-bit, 8-bit per channel) and HDR
    /// <c>D3DFMT_LE_X2R10G10B10</c> (32-bit, 10-bit RGB + 2-bit alpha).
    /// </summary>
    public static class XboxScreenshotFormat
    {
        /// <summary>Standard SDR front buffer (B,G,R,A — little-endian).</summary>
        public const uint D3DFMT_LE_X8R8G8B8 = 0x86;
        /// <summary>HDR front buffer (B10,G10,R10,A2 — little-endian).</summary>
        public const uint D3DFMT_LE_X2R10G10B10 = 0xBE;

        /// <summary>True if the format is a 4-byte (32-bit) pixel format.</summary>
        public static bool Is32Bpp(uint format)
            => format == D3DFMT_LE_X8R8G8B8 || format == D3DFMT_LE_X2R10G10B10;
    }

    #endregion