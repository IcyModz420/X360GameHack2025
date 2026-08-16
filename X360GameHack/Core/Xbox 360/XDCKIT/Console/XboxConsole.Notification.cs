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

            // Bind to any free local port.
            var listener = new TcpListener(System.Net.IPAddress.Any, 0);
            listener.Start();
            var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;

            var session = new XboxNotificationSession { Listener = listener, Port = port, IsRunning = true, Handler = handler };
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

        private static void RunNotificationListener(XboxNotificationSession session)
        {
            try
            {
                using (var client = session.Listener.AcceptTcpClient())
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, System.Text.Encoding.ASCII))
                {
                    while (session.IsRunning && !reader.EndOfStream)
                    {
                        string line = reader.ReadLine();
                        if (line == null) break;
                        var h = session.Handler;
                        if (h == null) continue;
                        try { h(line); } catch { /* never let user code kill the pump */ }
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
    }