// =============================================================================
// XDCKIT.XboxClient.Discovery.cs - LAN discovery
// =============================================================================
// Two flavors of "find a console":
//   1) xbdm UDP name service (DmResolveXboxName / DmFindConsoles)
//      Mirrors the AnswerName service in dmserv.c that listens on UDP/730
//      for NameRequest (1) and ListRequest (3) and replies with NameReply (2).
//   2) Plain TCP banner scan of every host on the local /24, checking the
//      first 4 bytes of the welcome line for "201" (FindAll/FirstConsoleOnLan).
// =============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public sealed partial class XboxClient
{
    #region xbdm name discovery (DmResolveXboxName / DmFindConsoles)

    /// <summary>UDP port the xbdm name service listens on (same as TCP/730).</summary>
    public const int NameServicePort = DefaultPort;

    // Wire packet for the AnswerName UDP service (see xbdm dmserv.c AnswerName):
    //
    //   struct { BYTE bRequest; BYTE cchName; char szName[256]; } nm;
    //
    //   bRequest = 1 -> NameRequest  (look up a specific name)
    //   bRequest = 2 -> NameReply    (we are this name)
    //   bRequest = 3 -> ListRequest  (broadcast list-all probe)
    private const byte NameRequestLookup = 1;
    private const byte NameReply        = 2;
    private const byte NameRequestList  = 3;

    /// <summary>
    /// One LAN xbdm console returned by <see cref="FindConsole(int,int)"/>.
    /// </summary>
    public sealed class DiscoveredConsole
    {
        /// <summary>The friendly name the console reports for itself (e.g. <c>"XeDevkit"</c>, <c>"Jtag"</c>).</summary>
        public string Name;
        /// <summary>The IPv4 address the reply came from.</summary>
        public string IPAddress;

        public override string ToString() => $"{Name} ({IPAddress})";
    }

    /// <summary>
    /// DmResolveXboxName — UDP-broadcast a NameRequest packet to every
    /// xbdm server on the LAN; the console whose <c>dbgname</c> matches
    /// <paramref name="consoleName"/> answers with its IP.  Mirrors the
    /// <c>AnswerName</c> case <c>bRequest = 1</c> in xbdm dmserv.c.
    /// Returns the IP as a dotted quad, or null on timeout.
    /// </summary>
    public static string ResolveXboxName(string consoleName, int timeoutMs = 1500, int port = NameServicePort)
    {
        if (string.IsNullOrEmpty(consoleName)) throw new ArgumentNullException(nameof(consoleName));
        byte[] nameBytes = Encoding.ASCII.GetBytes(consoleName);
        if (nameBytes.Length > 255) throw new ArgumentException("Name is too long.", nameof(consoleName));

        byte[] packet = new byte[2 + nameBytes.Length + 1];
        packet[0] = NameRequestLookup;
        packet[1] = (byte)nameBytes.Length;
        Buffer.BlockCopy(nameBytes, 0, packet, 2, nameBytes.Length);
        // packet[^1] left as 0 - matches AnswerName which reads cchName + 1 (NUL-terminated).

        foreach (var hit in BroadcastNameProbe(packet, timeoutMs, port,
                                               acceptName: r => string.Equals(r.Name, consoleName,
                                                                              StringComparison.OrdinalIgnoreCase),
                                               stopAfterFirst: true))
            return hit.IPAddress;

        return null;
    }

    /// <summary>
    /// DmFindConsoles — UDP-broadcast a ListRequest packet to every
    /// xbdm server on the LAN; every console answers with its name and IP.
    /// Mirrors the <c>AnswerName</c> case <c>bRequest = 3</c> in xbdm
    /// dmserv.c.
    /// </summary>
    /// <param name="timeoutMs">How long to listen for replies before giving up.</param>
    /// <param name="port">UDP port to broadcast to (default 730).</param>
    public static List<DiscoveredConsole> FindConsole(int timeoutMs = 1500, int port = NameServicePort)
    {
        // ListRequest = bRequest=3, cchName=0
        byte[] packet = { NameRequestList, 0 };
        return BroadcastNameProbe(packet, timeoutMs, port, acceptName: null, stopAfterFirst: false);
    }

    /// <summary>
    /// Convenience overload: <see cref="FindConsole(int,int)"/> + filter
    /// by exact name (case-insensitive).  Returns the matching console or
    /// null on timeout.
    /// </summary>
    public static DiscoveredConsole FindConsole(string consoleName, int timeoutMs = 1500,
                                                int port = NameServicePort)
    {
        string ip = ResolveXboxName(consoleName, timeoutMs, port);
        if (string.IsNullOrEmpty(ip)) return null;
        return new DiscoveredConsole { Name = consoleName, IPAddress = ip };
    }

    private static List<DiscoveredConsole> BroadcastNameProbe(
        byte[] packet, int timeoutMs, int port,
        Predicate<DiscoveredConsole> acceptName, bool stopAfterFirst)
    {
        var hits = new List<DiscoveredConsole>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var udp = new UdpClient(AddressFamily.InterNetwork))
        {
            udp.EnableBroadcast = true;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            try { udp.Client.Bind(new IPEndPoint(System.Net.IPAddress.Any, 0)); } catch { /* ephemeral ok */ }

            // 1) Targeted broadcast on the directed broadcast for every local /24.
            foreach (var ep in BuildBroadcastEndpoints(port))
            {
                try { udp.Send(packet, packet.Length, ep); } catch { /* ignore */ }
            }
            // 2) Plus the global 255.255.255.255 broadcast as a safety net.
            try { udp.Send(packet, packet.Length, new IPEndPoint(System.Net.IPAddress.Broadcast, port)); }
            catch { /* ignore */ }

            udp.Client.ReceiveTimeout = Math.Max(50, Math.Min(timeoutMs, 250));
            var sw = Stopwatch.StartNew();
            IPEndPoint sender = new IPEndPoint(System.Net.IPAddress.Any, 0);

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                byte[] reply;
                try { reply = udp.Receive(ref sender); }
                catch (SocketException) { continue; }   // timeout slice -> loop again
                catch { break; }

                if (reply == null || reply.Length < 2) continue;
                if (reply[0] != NameReply) continue;

                int cch = reply[1];
                if (cch < 0 || 2 + cch > reply.Length) continue;

                string replyName = cch == 0 ? string.Empty : Encoding.ASCII.GetString(reply, 2, cch);
                var hit = new DiscoveredConsole { Name = replyName, IPAddress = sender.Address.ToString() };
                if (acceptName != null && !acceptName(hit)) continue;
                if (!seen.Add(hit.IPAddress)) continue;
                hits.Add(hit);
                if (stopAfterFirst) break;
            }
        }
        return hits;
    }

    private static IEnumerable<IPEndPoint> BuildBroadcastEndpoints(int port)
    {
        foreach (var subnet in GetLocalSubnets())
        {
            if (System.Net.IPAddress.TryParse(subnet + "255", out var ip))
                yield return new IPEndPoint(ip, port);
        }
    }

    #endregion

    #region Auto-discovery (LAN-wide xbdm scan)

    /// <summary>
    /// Scan the local /24 (and a few common subnets) for any xbdm server
    /// answering on port 730.  Returns the first IP that responds with the
    /// "201- connected" banner.  Empty string if none found.
    /// </summary>
    public static string FindFirstConsoleOnLan(int perHostTimeoutMs = 200)
    {
        var subnets = GetLocalSubnets();
        foreach (var subnet in subnets)
        {
            for (int host = 1; host < 255; host++)
            {
                string ip = subnet + host;
                if (ProbeXbdm(ip, DefaultPort, perHostTimeoutMs)) return ip;
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// Scan the local /24 in parallel and return every IP that answered
    /// xbdm on port 730.  Useful for populating a console picker UI.
    /// </summary>
    public static List<string> FindAllConsolesOnLan(int perHostTimeoutMs = 200)
    {
        var found = new List<string>();
        var subnets = GetLocalSubnets();
        var sync = new object();
        var threads = new List<Thread>();

        foreach (var subnet in subnets)
        {
            for (int host = 1; host < 255; host++)
            {
                string ip = subnet + host;
                var t = new Thread(() =>
                {
                    if (ProbeXbdm(ip, DefaultPort, perHostTimeoutMs))
                        lock (sync) found.Add(ip);
                }) { IsBackground = true };
                t.Start();
                threads.Add(t);
            }
        }
        foreach (var t in threads) t.Join();
        return found;
    }

    private static List<string> GetLocalSubnets()
    {
        var list = new List<string>();
        try
        {
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                var bytes = ip.GetAddressBytes();
                if (bytes[0] == 127) continue;
                list.Add($"{bytes[0]}.{bytes[1]}.{bytes[2]}.");
            }
        }
        catch { /* fall through */ }

        if (list.Count == 0) list.Add("192.168.1.");
        return list;
    }

    private static bool ProbeXbdm(string ip, int port, int timeoutMs)
    {
        try
        {
            using (var probe = new TcpClient())
            {
                var ar = probe.BeginConnect(ip, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                try { probe.EndConnect(ar); } catch { return false; }
                if (!probe.Connected) return false;

                var stream = probe.GetStream();
                var sw = Stopwatch.StartNew();
                while (probe.Available < 4 && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(10);
                if (probe.Available < 4) return false;

                var buf = new byte[Math.Min(64, probe.Available)];
                stream.Read(buf, 0, buf.Length);
                string banner = Encoding.ASCII.GetString(buf);
                return banner.StartsWith("201", StringComparison.Ordinal);
            }
        }
        catch { return false; }
    }

    #endregion
}
