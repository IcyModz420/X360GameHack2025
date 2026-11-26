using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace X360GameHack.Core.Net
{
    /// <summary>One of the pictures the console can show beside a pop-up notification (the console's own numbering).</summary>
    public sealed class ConsoleNotifyIcon
    {
        public int Id;
        public string Name;
        public override string ToString() => Id + "  -  " + Name;
    }

    public enum JrpcSensor { Cpu = 0, Gpu = 1, Memory = 2, Motherboard = 3 }

    /// <summary>One quadrant of the ring of light around the power button.</summary>
    public enum JrpcLed { Off = 0x00, Red = 0x08, Green = 0x80, Orange = 0x88 }

    /// <summary>
    /// The few things plain XBDM cannot do, done through the JRPC2 plugin (JRPC2.xex loaded on the console):
    /// pop-up notifications, temperature sensors, the ring-of-light LEDs, and calling a function on the console.
    /// Everything XBDM can do by itself stays on XBDM - nothing here is used for files, memory, launching,
    /// screenshots or console information.
    ///
    /// The commands are the text ones the published JRPC client sends ("consolefeatures ver=2 type=N params=...")
    /// and are built here exactly as that client builds them (checked against its source, JRPC_Client/JRPC.cs).
    /// </summary>
    public static class Jrpc
    {
        public const int Version = 2;
        // value kinds in a params list
        private const int KInt = 1, KString = 2, KFloat = 3, KByteArray = 7, KUInt64 = 8;

        /// <summary>The notification pictures, as numbered by the console (from the JRPC client's own list).</summary>
        public static readonly IReadOnlyList<ConsoleNotifyIcon> Icons = BuildIcons();

        private static List<ConsoleNotifyIcon> BuildIcons()
        {
            string[] names =
            {
                "0=Xbox logo", "1=New message (logo)", "2=Friend request", "3=New message", "4=Flashing Xbox logo", "5=Gamertag sent you a message",
                "6=Gamertag signed out", "7=Gamertag signed in", "8=Gamertag signed in to Xbox Live", "9=Gamertag signed in offline", "10=Gamertag wants to chat",
                "11=Disconnected from Xbox Live", "12=Download", "13=Flashing music symbol", "14=Flashing happy face", "15=Flashing frowning face",
                "16=Flashing double-sided hammer", "17=Gamertag wants to chat (2)", "18=Please reinsert memory unit", "19=Please reconnect controller",
                "20=Gamertag has joined chat", "21=Gamertag has left chat", "22=Game invite sent", "23=Flash logo", "24=Page sent to", "25=Four 2", "26=Four 3",
                "27=Achievement unlocked", "28=Four 9", "29=Gamertag wants to talk in Video Kinect", "30=Video chat invite sent", "31=Ready to play",
                "32=Can't download", "33=Download stopped", "34=Flashing Xbox console", "35=Sent you a game message", "36=Device full", "37=Four 7",
                "38=Flashing chat icon", "39=Achievements unlocked", "40=Has sent you a nudge", "41=Messenger disconnected", "42=Blank", "43=Can't sign in to Messenger",
                "44=Missed Messenger conversation", "45=Family timer: time remaining", "46=Disconnected from Xbox Live: 11 minutes remaining", "47=Kinect health effects",
                "48=Four 5", "49=Gamertag wants you to join an Xbox Live party", "50=Party invite sent", "51=Game invite sent to Xbox Live party",
                "52=Kicked from Xbox Live party", "53=Nulled", "54=Disconnected from Xbox Live party", "55=Downloaded", "56=Can't connect to Xbox Live party",
                "57=Gamertag has joined Xbox Live party", "58=Gamertag has left Xbox Live party", "59=Gamer picture unlocked", "60=Avatar award unlocked",
                "61=Joined Xbox Live party", "62=Please reinsert USB storage device", "63=Player muted", "64=Player unmuted", "65=Flashing chat symbol", "76=Updating"
            };
            return names.Select(n => { int eq = n.IndexOf('='); return new ConsoleNotifyIcon { Id = int.Parse(n.Substring(0, eq)), Name = n.Substring(eq + 1) }; }).ToList();
        }

        // ------------------------------------------------------------------ building commands (no console needed)

        private static string Hex(string ascii)
        {
            var sb = new StringBuilder(ascii.Length * 2);
            foreach (char ch in ascii) sb.Append(((byte)ch).ToString("X2"));
            return sb.ToString();
        }

        private static string Head(int type, string address, int count)
            => "consolefeatures ver=" + Version + " type=" + type + " params=\"A\\" + address + "\\A\\" + count + "\\";

        /// <summary>Null when the text can be shown in a notification, else why not.</summary>
        public static string NotifyTextProblem(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Type the text to show.";
            if (text.Length > 200) return "Keep the text to 200 characters.";
            if (text.Any(c => c < 32 || c > 126)) return "Use plain letters, digits and punctuation only (no accents, emoji or line breaks).";
            return null;
        }

        public static string NotifyCommand(string text, int icon)
        {
            string problem = NotifyTextProblem(text);
            if (problem != null) throw new ArgumentException(problem);
            if (icon < 0 || icon > 255) throw new ArgumentOutOfRangeException(nameof(icon), "The picture number is between 0 and 255.");
            return Head(12, "0", 2) + KString + "/" + text.Length + "\\" + Hex(text) + "\\" + KInt + "\\" + icon + "\\\"";
        }

        public static string TemperatureCommand(JrpcSensor sensor) => Head(15, "0", 1) + KInt + "\\" + (int)sensor + "\\\"";

        public static string LedsCommand(JrpcLed topLeft, JrpcLed topRight, JrpcLed bottomLeft, JrpcLed bottomRight)
            => Head(14, "0", 4) + KInt + "\\" + (int)topLeft + "\\" + KInt + "\\" + (int)topRight + "\\" + KInt + "\\" + (int)bottomLeft + "\\" + KInt + "\\" + (int)bottomRight + "\\\"";

        public static string ResolveCommand(string module, uint ordinal)
        {
            if (string.IsNullOrEmpty(module) || module.Length > 64 || module.Any(c => !(char.IsLetterOrDigit(c) || c == '.' || c == '_') || c > 126))
                throw new ArgumentException("A module name looks like xam.xex or xboxkrnl.exe.");
            return Head(9, "0", 2) + KString + "/" + module.Length + "\\" + Hex(module) + "\\" + KInt + "\\" + ordinal + "\\\"";
        }

        /// <summary>
        /// A call of the function at <paramref name="address"/> on a title thread, returning a 32-bit value.
        /// Arguments may be int, uint, bool, byte, short, ushort, long, ulong, float, string (plain characters) or byte[].
        /// </summary>
        public static string CallCommand(uint address, bool systemThread, params object[] arguments)
        {
            arguments = arguments ?? new object[0];
            if (arguments.Length > 37) throw new ArgumentException("A call takes at most 37 arguments.");
            var sb = new StringBuilder();
            foreach (object a in arguments)
            {
                if (a is int || a is uint || a is bool || a is byte || a is short || a is ushort)
                {
                    int v = a is uint ? unchecked((int)(uint)a) : a is bool ? ((bool)a ? 1 : 0) : Convert.ToInt32(a, CultureInfo.InvariantCulture);
                    sb.Append(KInt).Append('\\').Append(v.ToString(CultureInfo.InvariantCulture)).Append('\\');
                }
                else if (a is float) sb.Append(KFloat).Append('\\').Append(((float)a).ToString(CultureInfo.InvariantCulture)).Append('\\');
                else if (a is long || a is ulong)
                {
                    ulong v = a is long ? unchecked((ulong)(long)a) : (ulong)a;
                    sb.Append(KUInt64).Append('\\').Append(v.ToString(CultureInfo.InvariantCulture)).Append('\\');
                }
                else if (a is string)
                {
                    string s = (string)a;
                    if (s.Any(c => c < 32 || c > 126)) throw new ArgumentException("Text arguments take plain characters only.");
                    sb.Append(KByteArray).Append('/').Append(s.Length).Append('\\').Append(Hex(s)).Append('\\');
                }
                else if (a is byte[])
                {
                    var b = (byte[])a;
                    sb.Append(KByteArray).Append('/').Append(b.Length).Append('\\');
                    foreach (byte x in b) sb.Append(x.ToString("X2"));
                    sb.Append('\\');
                }
                else throw new ArgumentException("An argument of type " + (a == null ? "null" : a.GetType().Name) + " cannot be sent.");
            }
            return "consolefeatures ver=" + Version + " type=" + KInt + (systemThread ? " system" : "") + " as=0 params=\"A\\" + address.ToString("X") + "\\A\\" + arguments.Length + "\\" + sb + "\"";
        }

        // ------------------------------------------------------------------ reading replies

        /// <summary>What follows the first space of a reply line ("200- 4D5307E6" gives "4D5307E6").</summary>
        public static string Value(string reply)
        {
            reply = reply ?? "";
            int sp = reply.IndexOf(' ');
            return sp < 0 ? "" : reply.Substring(sp + 1).Trim();
        }

        public static uint HexValue(string reply)
        {
            string v = Value(reply);
            uint n;
            if (!uint.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) throw new InvalidOperationException("JRPC2 answered something unexpected: " + reply);
            return n;
        }

        // ------------------------------------------------------------------ talking to the console

        public const string PluginName = "JRPC2.xex";

        /// <summary>True when JRPC2.xex is among the modules loaded on the console.</summary>
        public static bool IsLoaded(XboxConsole c)
            => (c.GetModules() ?? new List<ModuleInfo>()).Any(m => string.Equals(m.Name, PluginName, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Plugins that hang the console when JRPC2 is used next to them: with InfinityOps.xex loaded as well, the very
        /// first JRPC2 command never got an answer and the console needed switching off and on (seen twice on an RGH
        /// console, 2026-11-17/18); with InfinityOps removed every JRPC2 command worked.
        /// </summary>
        public static readonly string[] Conflicting = { "InfinityOps.xex" };

        /// <summary>
        /// Sends one command and returns the reply line. Refuses when JRPC2.xex is not loaded (XBDM would take the
        /// command for its own "consolefeatures" and the answer would mean nothing), and when a plugin known to hang
        /// the console together with JRPC2 is loaded - then nothing is sent.
        /// </summary>
        public static string Send(XboxConsole c, string command)
        {
            var names = (c.GetModules() ?? new List<ModuleInfo>()).Select(m => m.Name ?? "").ToList();
            if (!names.Any(n => n.Equals(PluginName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("JRPC2.xex is not loaded on the console. Add it as a plugin in launch.ini (Console Setup) and restart the console.");
            string clash = Conflicting.FirstOrDefault(p => names.Any(n => n.Equals(p, StringComparison.OrdinalIgnoreCase)));
            if (clash != null)
                throw new InvalidOperationException(clash + " is loaded next to JRPC2.xex. Together they hang the console on the first JRPC2 command, so nothing was sent. " +
                                                    "Take " + clash + " out of launch.ini (Console Setup > INI Editor), restart the console, and try again.");
            var r = c.SendTextCommand(command);
            string line = ((int)r.Status) + "- " + (r.StatusMessage ?? "");
            // while the plugin is still working it answers "buf_addr=<hex>"; ask again exactly as the published client
            // does: "consolefeatures buf_addr=0x<HEX>"
            for (int i = 0; i < 240 && line.Contains("buf_addr="); i++)
            {
                System.Threading.Thread.Sleep(250);
                string rest = line.Substring(line.IndexOf("buf_addr=", StringComparison.Ordinal) + 9).Trim();
                int end = rest.IndexOf(' ');
                if (end >= 0) rest = rest.Substring(0, end);
                if (rest.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) rest = rest.Substring(2);
                uint buffer;
                if (!uint.TryParse(rest, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out buffer))
                    throw new InvalidOperationException("JRPC2 answered something unexpected: " + line);
                r = c.SendTextCommand("consolefeatures buf_addr=0x" + buffer.ToString("X"));
                line = ((int)r.Status) + "- " + (r.StatusMessage ?? "");
            }
            if (line.Contains("error=")) throw new InvalidOperationException("JRPC2 reported: " + line.Substring(line.IndexOf("error=", StringComparison.Ordinal) + 6));
            if ((int)r.Status == 407) throw new InvalidOperationException("The console does not know that command - JRPC2 does not seem to be active.");
            return line;
        }

        public static void Notify(XboxConsole c, string text, int icon) => Send(c, NotifyCommand(text, icon));

        /// <summary>Degrees Celsius.</summary>
        public static int Temperature(XboxConsole c, JrpcSensor sensor) => (int)HexValue(Send(c, TemperatureCommand(sensor)));

        public static void SetLeds(XboxConsole c, JrpcLed topLeft, JrpcLed topRight, JrpcLed bottomLeft, JrpcLed bottomRight)
            => Send(c, LedsCommand(topLeft, topRight, bottomLeft, bottomRight));

        /// <summary>The address of an exported function, by module and ordinal.</summary>
        public static uint Resolve(XboxConsole c, string module, uint ordinal) => HexValue(Send(c, ResolveCommand(module, ordinal)));

        public static uint Call(XboxConsole c, uint address, bool systemThread, params object[] arguments)
        {
            if (address < 0x80000000) throw new ArgumentException("0x" + address.ToString("X8") + " is not a code address.");
            return HexValue(Send(c, CallCommand(address, systemThread, arguments)));
        }
    }
}
