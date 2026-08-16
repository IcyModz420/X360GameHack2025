// =============================================================================
// XDCKIT.XboxManager.cs - XDevkit-style entry point
// =============================================================================
// XboxManager mirrors Microsoft's `XboxManager` COM class.  In XDevkit you'd
// write:
//
//     var mgr = new XboxManagerClass();
//     mgr.DefaultConsole = "192.168.1.71";
//     IXboxConsole console = mgr.OpenConsole(mgr.DefaultConsole);
//
// Same shape here:
//
//     var mgr = new XboxManager { DefaultConsole = "192.168.1.71" };
//     XboxConsole console = mgr.OpenConsole();           // uses DefaultConsole
//
// Plus the new conveniences:
//
//     mgr.AutoDiscover();                                // scan the LAN
//     foreach (var name in mgr.Consoles) Console.WriteLine(name);
// =============================================================================
using System;
using System.Collections.Generic;

    public sealed class XboxManager
    {
        /// <summary>The console to use when <c>OpenConsole()</c> is called without a name.</summary>
        public string DefaultConsole { get; set; }

        /// <summary>List of console IPs the manager knows about (populated by <c>AutoDiscover</c> or manually).</summary>
        public List<string> Consoles { get; } = new List<string>();

        /// <summary>Default xbdm port (730).  Override to support custom xbdm forks.</summary>
        public int Port { get; set; } = XboxClient.DefaultPort;

        /// <summary>Open a connection to <see cref="DefaultConsole"/>.</summary>
        public XboxConsole OpenConsole()
        {
            if (string.IsNullOrEmpty(DefaultConsole))
                throw new InvalidOperationException("DefaultConsole is not set.  Either set it or call OpenConsole(ip).");
            return OpenConsole(DefaultConsole);
        }

        /// <summary>
        /// Open a connection to a specific console by IP, DNS hostname, or
        /// xbdm <c>dbgname</c> (e.g. <c>"XeDevkit"</c>, <c>"Jtag"</c>).  If the
        /// argument doesn't parse as an IPv4 address and DNS doesn't resolve
        /// it, falls back to <see cref="XboxClient.ResolveXboxName"/> over UDP.
        /// </summary>
        public XboxConsole OpenConsole(string nameOrIp)
        {
            if (string.IsNullOrEmpty(nameOrIp))
                throw new ArgumentNullException(nameof(nameOrIp));

            string target = ResolveTarget(nameOrIp);
            var console = new XboxConsole(target, Port);
            console.OpenConnection();
            if (!Consoles.Contains(target)) Consoles.Add(target);
            return console;
        }

        private static string ResolveTarget(string nameOrIp)
        {
            if (System.Net.IPAddress.TryParse(nameOrIp, out _)) return nameOrIp;

            try
            {
                var addrs = System.Net.Dns.GetHostAddresses(nameOrIp);
                foreach (var a in addrs)
                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return a.ToString();
            }
            catch { /* fall through to xbdm name lookup */ }

            string ip = XboxClient.ResolveXboxName(nameOrIp);
            return string.IsNullOrEmpty(ip) ? nameOrIp : ip;
        }

        /// <summary>
        /// Try to open a connection without throwing.  Returns null on failure.
        /// </summary>
        public XboxConsole TryOpenConsole(string nameOrIp)
        {
            try { return OpenConsole(nameOrIp); }
            catch { return null; }
        }

        /// <summary>
        /// Scan the local network for xbdm-speaking consoles and populate
        /// <see cref="Consoles"/>.  Tries the fast UDP <c>FindConsole</c>
        /// broadcast first (real DmFindConsole behavior); falls back to a
        /// /24 TCP banner scan if nothing answered the broadcast.  If
        /// <see cref="DefaultConsole"/> is unset and any consoles are found,
        /// the first one is set as default.
        /// </summary>
        /// <returns>The number of consoles found.</returns>
        public int AutoDiscover(int perHostTimeoutMs = 200)
        {
            Consoles.Clear();

            foreach (var hit in XboxClient.FindConsole())
                if (!Consoles.Contains(hit.IPAddress)) Consoles.Add(hit.IPAddress);

            if (Consoles.Count == 0)
                Consoles.AddRange(XboxClient.FindAllConsolesOnLan(perHostTimeoutMs));

            if (string.IsNullOrEmpty(DefaultConsole) && Consoles.Count > 0)
                DefaultConsole = Consoles[0];
            return Consoles.Count;
        }

        /// <summary>
        /// DmFindConsoles — UDP-broadcast a list-all probe and return every
        /// xbdm console (name + IP) that answered before <paramref name="timeoutMs"/>.
        /// </summary>
        public List<XboxClient.DiscoveredConsole> FindConsole(int timeoutMs = 1500)
            => XboxClient.FindConsole(timeoutMs, Port);

        /// <summary>
        /// DmResolveXboxName — UDP-broadcast a NameRequest and return the IP
        /// of the console whose <c>dbgname</c> matches.  Null on timeout.
        /// </summary>
        public string ResolveXboxName(string consoleName, int timeoutMs = 1500)
            => XboxClient.ResolveXboxName(consoleName, timeoutMs, Port);
    }