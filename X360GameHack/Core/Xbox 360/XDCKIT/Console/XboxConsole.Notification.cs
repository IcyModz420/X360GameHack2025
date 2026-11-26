// =============================================================================
// XDCKIT.XboxConsole.Notification.cs - Async notification channel
// =============================================================================
// Wraps DmOpenNotificationSession / DmCloseNotificationSession /
// DmRegisterNotificationProcessor / DmNotify - the "push channel" xbdm
// uses to deliver asynchronous events (debug strings, module load, exec
// state change, etc.) to a tool.
//
// We start a TcpListener on a free local port and tell xbdm to phone home
// to it via the `notifyat` command.  Every line the console pushes is
// forwarded to the registered handler on a background thread.
//
// Safety: the listener is bound only to the address this PC uses to reach the
// console (not every network interface), only a connection that comes from the
// console's own address is served (anything else is closed at once), and a line
// longer than any console sends ends the session instead of filling memory.
//
// Usage:
//
//     var session = console.OpenNotificationSession("dbgstr execstate", line =>
//     {
//         Console.WriteLine("xbdm pushed: " + line);
//     });
//     // ... do stuff ...
//     console.CloseNotificationSession(session);
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

    public partial class XboxConsole
    {
        /// <summary>
        /// All currently open notification sessions on this console (used by
        /// <see cref="DmNotify"/> to fan a line out to every registered
        /// processor).  Take the list lock before iterating.
        /// </summary>
        private List<XboxNotificationSession> ActiveNotificationSessions { get; } = new List<XboxNotificationSession>();

        /// <summary>
        /// DmOpenNotificationSession + DmRegisterNotificationProcessor combined.
        /// Returns a live session that you must dispose / pass to
        /// <see cref="CloseNotificationSession"/> when done.
        /// </summary>
        /// <param name="categoryFilter">
        /// Space-separated xbdm notification categories ("debugstr exec modload"
        /// etc.).  Pass null/"" to receive everything.
        /// </param>
        /// <param name="handler">Invoked on a background thread for every pushed line.</param>
        public XboxNotificationSession OpenNotificationSession(string categoryFilter,
                                                              XboxNotificationHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!Connected) throw new InvalidOperationException("Not connected.");

            // A free port on the address this PC reaches the console from - the address xbdm calls back - and only the
            // console's own address is served.
            var socket = Client?.UnderlyingClient?.Client;
            var local = (socket?.LocalEndPoint as IPEndPoint)?.Address;
            var console = (socket?.RemoteEndPoint as IPEndPoint)?.Address;
            if (local == null || console == null) throw new InvalidOperationException("Not connected.");
            var listener = new TcpListener(local, 0);
            listener.Start();
            var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;

            var session = new XboxNotificationSession { Listener = listener, Port = port, IsRunning = true, Handler = handler, ExpectedPeer = console };
            lock (ActiveNotificationSessions) ActiveNotificationSessions.Add(session);

            // Tell xbdm to call us.
            string cmd = $"notifyat port={port}";
            if (!string.IsNullOrEmpty(categoryFilter)) cmd += " " + categoryFilter;
            SendTextCommand(cmd);

            var thread = new Thread(() => RunNotificationListener(session))
            {
                IsBackground = true,
                Name = "XDCKIT.NotifySession:" + port,
            };
            thread.Start();

            return session;
        }

        /// <summary>DmCloseNotificationSession - tear down a notification listener.</summary>
        public void CloseNotificationSession(XboxNotificationSession session)
        {
            if (session == null) return;
            try { SendTextCommand($"notifyat port={session.Port} drop"); }
            catch { /* console may already be gone */ }
            lock (ActiveNotificationSessions) ActiveNotificationSessions.Remove(session);
            session.Dispose();
        }

        /// <summary>
        /// DmRegisterNotificationProcessor — XDevkit COM routing helper.  In
        /// XDCKIT every <see cref="OpenNotificationSession"/> already takes
        /// the handler delegate, so this is a thin alias that swaps the
        /// active handler on an existing live session.
        /// </summary>
        public void RegisterNotificationProcessor(XboxNotificationSession session,
                                                  XboxNotificationHandler handler)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            session.Handler = handler;
        }

        /// <summary>
        /// DmNotify — push a custom notification line to every active
        /// notification session (the SDK form sends it to all subscribed
        /// debuggers via the kernel notification channel).  XDCKIT
        /// equivalent: write the line to every live <see cref="XboxNotificationSession"/>
        /// pump locally so registered processors see it.
        /// </summary>
        /// <summary>
        /// DmGetSendNotificationsTo — flips the *current* socket into a
        /// notify-only channel (the SDK form is
        /// <c>NOTIFY reconnectport=%d reverse</c>).  After the 205 response
        /// this XboxClient should not be used for normal commands again.
        /// </summary>
        public void ConvertToNotifySocket(ushort reconnectPort, bool reverse = false)
        {
            string cmd = $"notify reconnectport={reconnectPort}" + (reverse ? " reverse" : string.Empty);
            SendTextCommand(cmd);
        }

        public void DmNotify(string category, string text)
        {
            // We don't have a wire command for "make xbdm push to every other
            // debugger".  But the most common XDevkit usage pattern is "I
            // want my own callbacks to fire" - that we can do directly.
            string composed = string.IsNullOrEmpty(category) ? text : $"{category} {text}";
            lock (ActiveNotificationSessions)
            {
                foreach (var s in ActiveNotificationSessions)
                {
                    try { s?.Handler?.Invoke(composed); } catch { /* never propagate */ }
                }
            }
        }

        /// <summary>The longest line served; xbdm's own lines are a few hundred bytes at most.</summary>
        internal const int MaxNotificationLine = 64 * 1024;

        /// <summary>Connections from anything other than the console turned away before the session gives up.</summary>
        internal const int MaxStrangers = 32;

        /// <summary>True when <paramref name="from"/> is the console's address (also written as an IPv4-mapped IPv6 address).</summary>
        internal static bool IsExpectedPeer(IPAddress from, IPAddress expected)
        {
            if (from == null || expected == null) return false;
            if (from.IsIPv4MappedToIPv6) from = from.MapToIPv4();
            if (expected.IsIPv4MappedToIPv6) expected = expected.MapToIPv4();
            return from.Equals(expected);
        }

        internal static void RunNotificationListener(XboxNotificationSession session)
        {
            TcpClient client = null;
            try
            {
                for (int strangers = 0; client == null;)
                {
                    var c = session.Listener.AcceptTcpClient();
                    if (IsExpectedPeer((c.Client.RemoteEndPoint as IPEndPoint)?.Address, session.ExpectedPeer)) { client = c; break; }
                    c.Close();                                          // not the console: turned away
                    if (++strangers >= MaxStrangers) return;
                }
                using (client)
                using (var stream = client.GetStream())
                {
                    var line = new System.Text.StringBuilder();
                    var buf = new byte[4096];
                    int n;
                    while (session.IsRunning && (n = stream.Read(buf, 0, buf.Length)) > 0)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            byte b = buf[i];
                            if (b == (byte)'\n')
                            {
                                if (line.Length > 0 && line[line.Length - 1] == '\r') line.Length--;
                                Deliver(session, line.ToString());
                                line.Clear();
                            }
                            else if (line.Length >= MaxNotificationLine) return;     // no console sends that: the session ends
                            else line.Append(b < 0x80 ? (char)b : '?');
                        }
                    }
                }
            }
            catch
            {
                // Listener was disposed or socket died - that's the normal shutdown path.
            }
            finally
            {
                session.IsRunning = false;
            }
        }

        private static void Deliver(XboxNotificationSession session, string line)
        {
            var h = session.Handler;
            if (h == null) return;
            try { h(line); } catch { /* never let user code kill the pump */ }
        }
    }