using System;
using System.Collections.Generic;
using System.Linq;

namespace X360GameHack.Core.Net
{
    /// <summary>How something was done on the console.</summary>
    public enum ConsoleRoute { Xbdm, Jrpc2, Xrpc, Xdrpc }

    /// <summary>
    /// One place that decides HOW a console feature is carried out. The order is always the same:
    ///   1. plain XBDM, whenever XBDM can do it at all;
    ///   2. the JRPC2 plugin, when XBDM cannot and JRPC2.xex is loaded;
    ///   3. the XRPC plugin, when neither of the above can and XRPC.xex is loaded;
    ///   4. XDRPC - a development kit's own xbdm "rpc" command (no plugin) - when no plugin is loaded and the console answers it.
    /// A later route is only tried when the earlier one is not available or says it cannot do it - never after
    /// the connection itself failed (a console that has stopped answering is not poked through another door).
    /// </summary>
    public static class ConsoleExtras
    {
        /// <summary>The route the last feature call went through (for the log line).</summary>
        public static ConsoleRoute LastRoute { get; private set; }

        public static string Name(ConsoleRoute r) => r == ConsoleRoute.Xbdm ? "XBDM" : r == ConsoleRoute.Jrpc2 ? "JRPC2" : r == ConsoleRoute.Xrpc ? "XRPC" : "XDRPC";

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<XboxConsole, object> XdrpcSeen = new System.Runtime.CompilerServices.ConditionalWeakTable<XboxConsole, object>();

        /// <summary>
        /// Which routes besides plain XBDM are there right now: the plugins that are loaded, and XDRPC when neither plugin is and
        /// the console's xbdm answers "rpc" (asked once per connection - only then, so a console with a plugin is never asked).
        /// </summary>
        public static List<ConsoleRoute> Plugins(XboxConsole c)
        {
            var names = (c.GetModules() ?? new List<ModuleInfo>()).Select(m => m.Name ?? "").ToList();
            var list = new List<ConsoleRoute>();
            if (names.Any(n => n.Equals(Jrpc.PluginName, StringComparison.OrdinalIgnoreCase))) list.Add(ConsoleRoute.Jrpc2);
            if (names.Any(n => n.Equals(Xrpc.PluginName, StringComparison.OrdinalIgnoreCase))) list.Add(ConsoleRoute.Xrpc);
            if (list.Count == 0)
            {
                bool rpc;
                lock (XdrpcSeen)
                {
                    if (XdrpcSeen.TryGetValue(c, out object known)) rpc = (bool)known;
                    else { rpc = Xdrpc.IsAvailable(c); XdrpcSeen.Add(c, rpc); }
                }
                if (rpc) list.Add(ConsoleRoute.Xdrpc);
            }
            return list;
        }

        private static bool LinkFailure(Exception ex) => ex is System.IO.IOException || ex is System.Net.Sockets.SocketException || ex is ObjectDisposedException || ex is TimeoutException;

        private const string NoPlugin = "XBDM has no command for this, neither JRPC2.xex nor XRPC.xex is loaded on the console, and its xbdm has no XDRPC (a development kit's has). Add JRPC2 or XRPC as a plugin in launch.ini (INI Editor), send the file and restart the console.";

        /// <summary>Runs the first route that works; <paramref name="routes"/> are tried in the order given.</summary>
        private static T Chain<T>(XboxConsole c, string what, params KeyValuePair<ConsoleRoute, Func<T>>[] routes)
        {
            var loaded = Plugins(c);
            var problems = new List<string>();
            foreach (var r in routes)
            {
                if (r.Key != ConsoleRoute.Xbdm && !loaded.Contains(r.Key)) continue;
                try
                {
                    T value = r.Value();
                    LastRoute = r.Key;
                    return value;
                }
                catch (Exception ex) when (!LinkFailure(ex) && !(ex is ArgumentException))
                {
                    problems.Add(Name(r.Key) + ": " + ex.Message);
                }
            }
            if (problems.Count == 0)
            {
                // a plugin is loaded, but not one that can do this particular thing
                var able = routes.Where(r => r.Key != ConsoleRoute.Xbdm).Select(r => r.Key == ConsoleRoute.Xdrpc ? "a development kit's XDRPC" : Name(r.Key) + ".xex").ToList();
                if (loaded.Count > 0 && able.Count > 0)
                    throw new InvalidOperationException(what + ": XBDM has no command for this and " + string.Join(" / ", loaded.Select(Name)) + " cannot do it either. It needs " + string.Join(" or ", able) + " loaded on the console (INI Editor > launch.ini > Add plugin).");
                throw new InvalidOperationException(what + ": " + NoPlugin);
            }
            throw new InvalidOperationException(what + " did not work. " + string.Join("  ", problems));
        }

        private static KeyValuePair<ConsoleRoute, Func<T>> R<T>(ConsoleRoute route, Func<T> f) => new KeyValuePair<ConsoleRoute, Func<T>>(route, f);

        /// <summary>A pop-up notification. XBDM cannot; JRPC2, then XRPC.</summary>
        public static void Notify(XboxConsole c, string text, int icon)
        {
            string problem = Jrpc.NotifyTextProblem(text);
            if (problem != null) throw new ArgumentException(problem);
            if (icon < 0 || icon > 255) throw new ArgumentOutOfRangeException(nameof(icon), "The picture number is between 0 and 255.");
            Chain(c, "The notification",
                R(ConsoleRoute.Jrpc2, () => { Jrpc.Notify(c, text, icon); return true; }),
                R(ConsoleRoute.Xrpc, () => { Xrpc.Notify(c, text, icon); return true; }),
                R(ConsoleRoute.Xdrpc, () => { XdrpcFeatures.Notify(c, text, icon); return true; }));
        }

        /// <summary>A temperature sensor in degrees Celsius. XBDM cannot; JRPC2, else XDRPC (XRPC has no such request).</summary>
        public static int Temperature(XboxConsole c, JrpcSensor sensor)
            => Chain(c, "Reading the temperature",
                R(ConsoleRoute.Jrpc2, () => Jrpc.Temperature(c, sensor)),
                R(ConsoleRoute.Xdrpc, () => XdrpcFeatures.Temperature(c, sensor)));

        /// <summary>The ring of light. XBDM cannot; JRPC2, else XDRPC.</summary>
        public static void SetLeds(XboxConsole c, JrpcLed topLeft, JrpcLed topRight, JrpcLed bottomLeft, JrpcLed bottomRight)
            => Chain(c, "Setting the ring of light",
                R(ConsoleRoute.Jrpc2, () => { Jrpc.SetLeds(c, topLeft, topRight, bottomLeft, bottomRight); return true; }),
                R(ConsoleRoute.Xdrpc, () => { XdrpcFeatures.SetLeds(c, topLeft, topRight, bottomLeft, bottomRight); return true; }));

        /// <summary>
        /// The running game's title id. XBDM first (read from the program's header in memory); if that gives
        /// nothing, JRPC2's own request; then XRPC or XDRPC calling xam.xex export 463 (XamGetCurrentTitleId).
        /// </summary>
        public static uint TitleId(XboxConsole c)
            => Chain(c, "Reading the title id",
                R(ConsoleRoute.Xbdm, () => { uint t = c.GetTitleId(); if (t == 0) throw new InvalidOperationException("the running program's header carries no title id"); return t; }),
                R(ConsoleRoute.Jrpc2, () => Jrpc.HexValue(Jrpc.Send(c, "consolefeatures ver=" + Jrpc.Version + " type=16 params=\"A\\0\\A\\0\\\""))),
                R(ConsoleRoute.Xrpc, () => Xrpc.Call(c, Xrpc.Resolve(c, "xam.xex", 463))),
                R(ConsoleRoute.Xdrpc, () => Xdrpc.Call(c, "xam.xex", 463)));

        /// <summary>Calls a function on the console. XBDM cannot; JRPC2, then XRPC, then XDRPC (whole-number / text / byte arguments).</summary>
        public static uint Call(XboxConsole c, uint address, params object[] arguments) => Call(c, address, false, arguments);

        /// <summary>The same, choosing the kind of thread the call runs on (JRPC2 and XDRPC; XRPC has one way).</summary>
        public static uint Call(XboxConsole c, uint address, bool systemThread, object[] arguments)
            => Chain(c, "Calling 0x" + address.ToString("X8"),
                R(ConsoleRoute.Jrpc2, () => Jrpc.Call(c, address, systemThread, arguments)),
                R(ConsoleRoute.Xrpc, () => Xrpc.Call(c, address, arguments)),
                R(ConsoleRoute.Xdrpc, () => XdrpcFeatures.Call(c, address, systemThread, arguments)));

        /// <summary>The address of an exported function. XBDM cannot; JRPC2, then XRPC, then XDRPC.</summary>
        public static uint Resolve(XboxConsole c, string module, uint ordinal)
            => Chain(c, "Finding " + module + " export " + ordinal,
                R(ConsoleRoute.Jrpc2, () => Jrpc.Resolve(c, module, ordinal)),
                R(ConsoleRoute.Xrpc, () => Xrpc.Resolve(c, module, ordinal)),
                R(ConsoleRoute.Xdrpc, () => XdrpcFeatures.Resolve(c, module, ordinal)));

        /// <summary>xam.xex export 599: XamContentLaunchImageFromFileInternal(package path, executable name).</summary>
        public const uint LaunchFromPackageOrdinal = 599;

        /// <summary>
        /// Starts a game that is installed as a package (Games on Demand, arcade, original Xbox) - plain XBDM can only start an
        /// .xex file ("magicboot"). xam.xex export 599, XamContentLaunchImageFromFileInternal, mounts the package and starts the
        /// executable inside it; it is called on a system thread with the package's kernel path ("\Device\Harddisk0\Partition1\
        /// Content\...") and "default.xex" (or "default.xbe" for an original Xbox game). JRPC2, then XRPC, then XDRPC.
        /// Returns the function's result (0 = started).
        /// </summary>
        public static uint LaunchPackage(XboxConsole c, string drivePath, string executable)
        {
            string device = ConsoleService.DevicePathOf(drivePath);
            if (device == null) throw new ArgumentException(drivePath + " is not on a drive a package can be started from (Hdd, Usb0-2, INTUSB / INTMMC).");
            if (device.Any(ch => ch < 32 || ch > 126) || device.Length > 250) throw new ArgumentException("The package's path has characters the console cannot take.");
            byte[] path = System.Text.Encoding.ASCII.GetBytes(device + "\0"), exe = System.Text.Encoding.ASCII.GetBytes(executable + "\0");
            uint result = Chain(c, "Starting " + drivePath,
                R(ConsoleRoute.Jrpc2, () => Jrpc.Call(c, Jrpc.Resolve(c, "xam.xex", LaunchFromPackageOrdinal), true, new object[] { path, exe })),
                R(ConsoleRoute.Xrpc, () => Xrpc.Call(c, Xrpc.Resolve(c, "xam.xex", LaunchFromPackageOrdinal), path, exe)),
                R(ConsoleRoute.Xdrpc, () =>
                {
                    var q = new Xdrpc.Request { Module = "xam.xex", Ordinal = LaunchFromPackageOrdinal, System = true };
                    q.Args.Add(q.Add(path)); q.Args.Add(q.Add(exe));
                    return Xdrpc.Call(c, q).Value32;
                }));
            if (result != 0) throw new InvalidOperationException("The console did not start the package (result 0x" + result.ToString("X8") + ").");
            return result;
        }
    }

    /// <summary>
    /// The JRPC2-only features done with XDRPC, a development kit's own remote call (see <see cref="Xdrpc"/>): the same kernel
    /// and xam.xex exports the plugins call. Ordinals from the export tables the program carries (Xenia's, BSD).
    /// </summary>
    public static class XdrpcFeatures
    {
        /// <summary>XNotifyQueueUI (xam.xex 656): picture, user 0, priority 2, the text (UTF-16 big-endian), no context - as XRPC calls it.</summary>
        public static void Notify(XboxConsole c, string text, int icon)
        {
            var q = new Xdrpc.Request { Module = "xam.xex", Ordinal = 656 };
            var t = q.Add(Xrpc.WideText(text));
            q.Args.Add((uint)icon); q.Args.Add(0u); q.Args.Add(2ul); q.Args.Add(t); q.Args.Add(0u);
            Xdrpc.Call(c, q);
        }

        /// <summary>
        /// HalSendSMCMessage (xboxkrnl 41) with the SMC's "query temperatures" (0x07): the reply holds CPU, GPU, memory and
        /// motherboard as 8.8 fixed point, little-endian, from byte 1 (the order of <see cref="JrpcSensor"/>).
        /// </summary>
        public static int Temperature(XboxConsole c, JrpcSensor sensor)
        {
            var reply = Smc(c, new byte[] { 0x07 });
            int i = 1 + 2 * (int)sensor;
            return (reply[i] | reply[i + 1] << 8) / 256;
        }

        /// <summary>The SMC's LED override (0x99, 1, bits): green in the high nibble, red in the low, one bit per quadrant (top left first).</summary>
        public static void SetLeds(XboxConsole c, JrpcLed topLeft, JrpcLed topRight, JrpcLed bottomLeft, JrpcLed bottomRight)
        {
            int bits = (int)topLeft | (int)topRight >> 1 | (int)bottomLeft >> 2 | (int)bottomRight >> 3;
            Smc(c, new byte[] { 0x99, 0x01, (byte)bits });
        }

        private static byte[] Smc(XboxConsole c, byte[] message)
        {
            var q = new Xdrpc.Request { Module = "xboxkrnl.exe", Ordinal = 41 };
            var request = new byte[16];
            Buffer.BlockCopy(message, 0, request, 0, message.Length);
            var a = q.Add(request);
            var b = q.Add(new byte[16]);
            q.Args.Add(a); q.Args.Add(b);
            Xdrpc.Call(c, q);
            return b.After;
        }

        /// <summary>Calls an address with whole-number, text (zero-terminated) or byte arguments; text and bytes travel as regions of the call buffer.</summary>
        public static uint Call(XboxConsole c, uint address, bool systemThread, object[] arguments)
        {
            var q = new Xdrpc.Request { Address = address, System = systemThread };
            foreach (object a in arguments ?? new object[0])
            {
                if (a is string s) q.Args.Add(q.Add(System.Text.Encoding.ASCII.GetBytes(s + "\0")));
                else if (a is byte[] b) q.Args.Add(q.Add((byte[])b.Clone()));
                else if (a is bool f) q.Args.Add(f ? 1u : 0u);
                else if (a is float) throw new ArgumentException("XDRPC calls here take whole numbers, text or bytes.");
                else q.Args.Add(a);
            }
            return Xdrpc.Call(c, q).Value32;
        }

        /// <summary>XexGetModuleHandle (xboxkrnl 405) then XexGetProcedureAddress (407).</summary>
        public static uint Resolve(XboxConsole c, string module, uint ordinal)
        {
            if (string.IsNullOrEmpty(module) || module.Length > 64 || module.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '.' || ch == '_') || ch > 126))
                throw new ArgumentException("A module name looks like xam.xex or xboxkrnl.exe.");
            var q = new Xdrpc.Request { Module = "xboxkrnl.exe", Ordinal = 405 };
            var name = q.Add(System.Text.Encoding.ASCII.GetBytes(module + "\0"));
            var handle = q.Add(new byte[8]);
            q.Args.Add(name); q.Args.Add(handle);
            uint status = Xdrpc.Call(c, q).Value32;
            if (status != 0) throw new InvalidOperationException(module + " is not loaded (status 0x" + status.ToString("X8") + ").");
            uint h = Xdrpc.BE32(handle.After, 0);
            var q2 = new Xdrpc.Request { Module = "xboxkrnl.exe", Ordinal = 407 };
            var address = q2.Add(new byte[8]);
            q2.Args.Add(h); q2.Args.Add(ordinal); q2.Args.Add(address);
            status = Xdrpc.Call(c, q2).Value32;
            if (status != 0) throw new InvalidOperationException(module + " has no export " + ordinal + " (status 0x" + status.ToString("X8") + ").");
            return Xdrpc.BE32(address.After, 0);
        }
    }
}
