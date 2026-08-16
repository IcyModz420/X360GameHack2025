// =============================================================================
// XDCKIT.XboxConsole.cs - Main XboxConsole class (lifecycle + properties)
// =============================================================================
// XboxConsole is the user-facing class.  It mirrors XDevkit's IXboxConsole
// surface (Name, IPAddress, OpenConnection, CloseConnection, SendTextCommand,
// Reboot, DebugTarget, ...) and adds:
//
//   * Instance helper objects: console.Tray, console.File, console.Automation,
//                              console.DebugTarget, console.Rpc, console.Patches
//   * console.Notify(...) for XNotify toasts (no helper indirection)
//   * Typed memory R/W (split into XDCKIT.XboxConsole.Memory.cs)
//   * Remote-call surface Call<T>/CallVoid lives on console.Rpc (Helpers/XboxRpc.cs)
//   * Console features (split into XDCKIT.XboxConsole.Features.cs)
//
// Error-handling policy (applies to every partial unless noted):
//   * Transport failures (socket disposed, timeout, connection closed) THROW.
//   * Protocol errors (xbdm returns a 4xx status code) return a sentinel:
//       - reference types: null
//       - integral types : 0
//       - bool getters   : false
//     Callers who need the error code should drop down to
//     `console.Client.SendTextCommand(...)` and inspect XbdmResponse.Status.
//
// Usage (types live in the global namespace — no using alias required):
//
//     var console = new XboxConsole();
//     console.Connect();                       // auto-discover on the LAN
//     // -- or --
//     console.Connect("192.168.1.71");         // explicit IP
//     console.Connect("192.168.1.71", 730);    // explicit IP + port
//     console.Notify("hello", XNotiyLogo.FLASHING_XBOX_LOGO);
//     uint title = console.GetTitleId();
//     console.Reboot(XboxReboot.Cold);
// =============================================================================
using System;
using System.ComponentModel;

    public partial class XboxConsole : IDisposable
    {
        #region Construction

        /// <summary>Create an unconnected console.  Call <see cref="Connect()"/> later.</summary>
        public XboxConsole()
        {
            Client = new XboxClient();
            DebugTarget = new XboxDebugTarget(this);
            Tray = new Tray(this);
            File = new XboxFile(this);
            Automation = new XboxAutomation(this);
            Rpc = new XboxRpc(this);
            Patches = new XboxPatchManager(this);
        }

        /// <summary>Create a console pointed at <paramref name="ip"/>.  Does not connect yet.</summary>
        public XboxConsole(string ip) : this() { IPAddress = ip; }

        /// <summary>Create a console pointed at <paramref name="ip"/>:<paramref name="port"/>.  Does not connect yet.</summary>
        public XboxConsole(string ip, int port) : this() { IPAddress = ip; Port = port; }

        #endregion

        #region Public state

        /// <summary>Underlying xbdm transport.  Exposed so power users can drop a layer.</summary>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Advanced)]
        public XboxClient Client { get; private set; }

        /// <summary>XDevkit-style debug target (GetMemory / SetMemory / IsDebuggerConnected ...).</summary>
        public XboxDebugTarget DebugTarget { get; private set; }

        /// <summary>DVD tray helper.</summary>
        public Tray Tray { get; private set; }

        /// <summary>File system helper.</summary>
        public XboxFile File { get; private set; }

        /// <summary>Controller automation helper (autoinput).</summary>
        public XboxAutomation Automation { get; private set; }

        /// <summary>Remote-call helper (Call&lt;T&gt;, CallVoid, ResolveFunction, ...).</summary>
        public XboxRpc Rpc { get; private set; }

        /// <summary>
        /// Xenia Canary game-patches loader (<c>.patch.toml</c>).  Apply
        /// memory-patch tables to a running title with
        /// <c>console.Patches.LoadFile(...)</c> then
        /// <c>console.Patches.ApplyEnabled()</c>.  See
        /// <see href="https://github.com/xenia-canary/game-patches"/> for the file format.
        /// </summary>
        public XboxPatchManager Patches { get; private set; }

        /// <summary>Console IP address (or hostname).  XDevkit-compatible.</summary>
        public string IPAddress { get; set; } = "0.0.0.0";

        /// <summary>xbdm TCP port (almost always 730).</summary>
        public int Port { get; set; } = XboxClient.DefaultPort;

        /// <summary>True once <see cref="Connect()"/> succeeded and the socket is alive.</summary>
        public bool Connected => Client != null && Client.Connected;

        /// <summary>
        /// RPC connect timeout (ms) used when building a fresh socket inside
        /// the consolefeatures helper.  Per-instance; mutating this on one
        /// console no longer affects others.
        /// </summary>
        public int ConnectTimeoutMs { get; set; } = 4_000;

        /// <summary>
        /// RPC conversation timeout (ms) used while a remote-call result is
        /// being awaited.  Per-instance.
        /// </summary>
        public int ConversationTimeoutMs { get; set; } = 4_000;

        /// <summary>
        /// Deprecated: use <see cref="ConnectTimeoutMs"/>.  The previous static
        /// property was a multi-console hazard (one call would stomp the timeout
        /// for every other live console).  Kept as a write-only no-op for
        /// source compatibility.
        /// </summary>
        [Obsolete("Use ConnectTimeoutMs on the XboxConsole instance.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static int ConnectTimeout { get; set; } = 4_000;

        /// <summary>Deprecated: use <see cref="ConversationTimeoutMs"/>.</summary>
        [Obsolete("Use ConversationTimeoutMs on the XboxConsole instance.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static int ConversationTimeout { get; set; } = 4_000;

        #endregion

        #region Properties (XDevkit-compatible)

        /// <summary>
        /// Get/set the console's debug name (the string that shows up in the
        /// Xbox 360 Neighborhood and in <c>dbgname</c>).
        /// </summary>
        public string Name
        {
            get
            {
                if (!Connected) return "<disconnected>";
                var resp = Client.SendTextCommand("dbgname");
                return resp.IsSuccess ? resp.StatusMessage.Trim() : string.Empty;
            }
            set
            {
                if (!Connected) throw new InvalidOperationException("Not connected.");
                Client.SendTextCommand("dbgname name=" + XboxExtensions.QuoteXbdm(value));
            }
        }

        /// <summary>
        /// Console execution state (Running / Stopped / Pending / etc.).
        /// xbdm <c>getexecstate</c> returns one of the literal tokens
        /// <c>start</c>, <c>stop</c>, <c>pending</c>, <c>reboot</c>,
        /// <c>pending_title</c>, or <c>reboot_title</c> (see xbdm dmserv.c).
        /// </summary>
        public XboxExecutionState ExecutionState
        {
            get
            {
                if (!Connected) return XboxExecutionState.Unknown;
                var resp = Client.SendTextCommand("getexecstate");
                if (!resp.IsSuccess) return XboxExecutionState.Unknown;
                string s = (resp.StatusMessage ?? string.Empty).Trim().ToLowerInvariant();

                if (s.StartsWith("pending_title")) return XboxExecutionState.TitlePending;
                if (s.StartsWith("reboot_title"))  return XboxExecutionState.TitleRebooting;
                if (s.StartsWith("pending"))       return XboxExecutionState.Pending;
                if (s.StartsWith("reboot"))        return XboxExecutionState.Rebooting;
                if (s.StartsWith("stop"))          return XboxExecutionState.Stopped;
                if (s.StartsWith("start"))         return XboxExecutionState.Running;
                return XboxExecutionState.Unknown;
            }
        }

        /// <summary>
        /// Console system time as <see cref="DateTime"/>.
        /// Real xbdm replies to <c>systime</c> with <c>high=0xX low=0xX</c>
        /// (see dmserv.c HrGetSystemTime); some forks use <c>clockhi/clocklo</c>
        /// — we tolerate both.  The setter sends BOTH key pairs so it works
        /// against every known fork.
        /// </summary>
        public DateTime SystemTime
        {
            get
            {
                if (!Connected) return DateTime.MinValue;
                var resp = Client.SendTextCommand("systime");
                if (!resp.IsSuccess) return DateTime.MinValue;
                uint hi = ParseUIntKvHex(resp.StatusMessage, "high");
                uint lo = ParseUIntKvHex(resp.StatusMessage, "low");
                if (hi == 0 && lo == 0)
                {
                    hi = ParseUIntKvHex(resp.StatusMessage, "clockhi");
                    lo = ParseUIntKvHex(resp.StatusMessage, "clocklo");
                }
                long ft = ((long)hi << 32) | lo;
                try { return DateTime.FromFileTime(ft); }
                catch { return DateTime.MinValue; }
            }
            set
            {
                if (!Connected) throw new InvalidOperationException("Not connected.");
                long ft = value.ToFileTime();
                uint hi = (uint)((ft >> 32) & 0xFFFFFFFF);
                uint lo = (uint)(ft & 0xFFFFFFFF);
                // Send both keys; xbdm.dll forks accept whichever they parse first.
                Client.SendTextCommand(
                    $"setsystime clockhi=0x{hi:X} clocklo=0x{lo:X} high=0x{hi:X} low=0x{lo:X}");
            }
        }

        /// <summary>
        /// Console type (DevelopmentKit / TestKit / ReviewerKit / NotConnected).
        /// xbdm <c>consoletype</c> replies with the literal token
        /// <c>devkit</c>, <c>testkit</c>, or <c>reviewerkit</c>.
        /// </summary>
        public XboxConsoleType ConsoleType
        {
            get
            {
                if (!Connected) return XboxConsoleType.NotConnected;
                var resp = Client.SendTextCommand("consoletype");
                if (!resp.IsSuccess) return XboxConsoleType.NotConnected;
                string s = (resp.StatusMessage ?? string.Empty).Trim().ToLowerInvariant();
                if (s.StartsWith("test"))   return XboxConsoleType.TestKit;
                if (s.StartsWith("review")) return XboxConsoleType.ReviewerKit;
                if (s.StartsWith("dev"))    return XboxConsoleType.DevelopmentKit;
                return XboxConsoleType.DevelopmentKit;
            }
        }

        #endregion

        #region OpenConnection / CloseConnection / SendTextCommand (XDevkit shape)

        /// <summary>
        /// Open the xbdm session.  Mirrors <c>IXboxConsole.OpenConnection</c>:
        /// returns a "connection id" (just 1 here since we own one socket).
        /// XDCKIT does not multiplex per-handler channels — the
        /// <paramref name="handler"/> string is recorded but otherwise ignored.
        /// </summary>
        public uint OpenConnection(string handler = null)
        {
            if (string.IsNullOrEmpty(IPAddress) || IPAddress == "0.0.0.0")
                throw new InvalidOperationException("IPAddress is not set.");
            Client.Connect(IPAddress, Port);
            return 1;
        }

        /// <summary>Mirrors <c>IXboxConsole.CloseConnection</c>.</summary>
        public void CloseConnection(uint connection = 1) => Client.Disconnect();

        /// <summary>
        /// DmOpenSecureConnection — open a secured xbdm channel (NONCE +
        /// KEYXCHG handshake).  Falls back to a plain channel if the
        /// console reports security disabled.
        /// </summary>
        public uint OpenSecureConnection(string handler = null)
        {
            if (string.IsNullOrEmpty(IPAddress) || IPAddress == "0.0.0.0")
                throw new InvalidOperationException("IPAddress is not set.");
            Client.OpenSecureConnection(IPAddress, Port);
            return 1;
        }

        /// <summary>DmMakeSharedConnection — see <see cref="XboxClient.MakeSharedConnection"/>.</summary>
        public XboxClient MakeSharedConnection() => Client.MakeSharedConnection();

        /// <summary>DmUseSharedConnection — see <see cref="XboxClient.UseSharedConnection"/>.</summary>
        public XboxClient UseSharedConnection() => Client.UseSharedConnection();

        /// <summary>
        /// XDevkit-shaped wrapper.  The <paramref name="connection"/> id is
        /// ignored (XDCKIT owns one socket per console).  Returns just the
        /// status-line payload (text after the status code), NOT a serialised
        /// XbdmResponse.  Use <see cref="SendTextCommand(string)"/> for the
        /// strongly-typed result.
        /// </summary>
        public void SendTextCommand(uint connection, string command, out string response)
        {
            var resp = Client.SendTextCommand(command);
            response = resp.StatusMessage ?? string.Empty;
        }

        /// <summary>Convenient SendTextCommand without the dummy connection id.</summary>
        public XbdmResponse SendTextCommand(string command) => Client.SendTextCommand(command);

        #endregion

        #region Connect helpers (XDCKIT-master compatible)

        /// <summary>
        /// Auto-discovery connect: scan the LAN for an xbdm server and connect
        /// to the first one found.  Returns true on success.
        /// </summary>
        public bool Connect()
        {
            string found = XboxClient.FindFirstConsoleOnLan();
            if (string.IsNullOrEmpty(found)) return false;
            return Connect(found, XboxClient.DefaultPort);
        }

        /// <summary>Connect to <paramref name="ipOrName"/> on the default port (730).</summary>
        public bool Connect(string ipOrName) => Connect(ipOrName, XboxClient.DefaultPort);

        /// <summary>Connect to <paramref name="ipOrName"/>:<paramref name="port"/>.</summary>
        public bool Connect(string ipOrName, int port)
        {
            if (string.IsNullOrEmpty(ipOrName)) return false;
            try
            {
                IPAddress = ipOrName;
                Port = port;
                Client.Connect(ipOrName, port);
                return Connected;
            }
            catch { return false; }
        }

        /// <summary>Hard close, sends "bye" first, ignores errors.</summary>
        public void Disconnect() => Client.Disconnect();

        /// <summary>
        /// Disconnect and reconnect to the same address.  Waits
        /// <paramref name="reconnectDelayMs"/> between calls so the console
        /// has time to drop the half-closed socket.
        /// </summary>
        public bool Reconnect(int reconnectDelayMs = 250)
        {
            string ip = IPAddress;
            int port = Port;
            Disconnect();
            if (reconnectDelayMs > 0) System.Threading.Thread.Sleep(reconnectDelayMs);
            return Connect(ip, port);
        }

        #endregion

        #region Helpers shared across partials (parsing xbdm responses)

        /// <summary>Parse "key=0xVALUE" out of an xbdm key=value line.</summary>
        internal static uint ParseUIntKvHex(string line, string key)
            => XboxExtensions.ParseKvUIntHex(line, key);

        /// <summary>Parse "key=VALUE" out of an xbdm key=value line as a string.</summary>
        internal static string ParseKvLine(string line, string key)
            => XboxExtensions.ParseKvUnescape(line, key);

        /// <summary>Combine two 32-bit halves (big-endian uint32 hi/lo) into a uint64.</summary>
        internal static ulong CombineHiLo(uint hi, uint lo) => ((ulong)hi << 32) | lo;

        /// <summary>Split a 202 multi-line body into trimmed non-empty lines.</summary>
        internal static string[] SplitMultilineBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return new string[0];
            var lines = body.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].Trim();
            return lines;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            try { Client?.Dispose(); } catch { /* ignore */ }
        }

        #endregion
    }
