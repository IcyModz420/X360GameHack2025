using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace X360GameHack.Core.Net
{
    /// <summary>
    /// The XRPC plugin (XRPC.xex), the older of the remote-call plugins. It has no command of its own: the PC
    /// writes the call into a buffer in the console's memory with ordinary XBDM memory writes and the plugin
    /// picks it up. Layout and order are those of the published client (XRPCLib/XRPC.cs):
    ///   pointer to the buffer   : big-endian word at 0x91C088AE
    ///   buffer + 0x000          : the trigger - a function address, or 0x82000000 (call the address in slot 0
    ///                             on a system thread) or 0x82000001 (resolve module + ordinal)
    ///   buffer + 0x004          : number of arguments
    ///   buffer + 0x008          : nine 8-byte big-endian argument slots
    ///   buffer + 0x5DC / 0xC80  : scratch space for text / bytes that arguments point at
    ///   buffer + 0xFFC          : the result, read 50 ms after the trigger is written
    /// Used only as the last route, after plain XBDM and JRPC2, for what those cannot do.
    /// </summary>
    public static class Xrpc
    {
        public const string PluginName = "XRPC.xex";
        public const uint BufferPointer = 0x91C088AE;
        private const uint TriggerSystemCall = 0x82000000, TriggerResolve = 0x82000001;
        private const int Slots = 9;

        public static bool IsLoaded(XboxConsole c)
            => (c.GetModules() ?? new List<ModuleInfo>()).Any(m => string.Equals(m.Name, PluginName, StringComparison.OrdinalIgnoreCase));

        private static byte[] Be32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
        private static uint Be32(byte[] b) => (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);

        /// <summary>One prepared call: what to write where, in order. Building it needs no console.</summary>
        public sealed class Plan
        {
            public readonly List<KeyValuePair<uint, byte[]>> Writes = new List<KeyValuePair<uint, byte[]>>();
            public uint ResultAddress;
        }

        /// <summary>
        /// Lays out a call. <paramref name="trigger"/> is the function address for a call on the title thread, or
        /// one of the two special values. Arguments: whole numbers, text (plain characters) or byte[].
        /// </summary>
        public static Plan Build(uint buffer, uint trigger, bool writeCount, params object[] arguments)
        {
            arguments = arguments ?? new object[0];
            if (arguments.Length > Slots) throw new ArgumentException("XRPC passes at most " + Slots + " arguments.");
            var plan = new Plan { ResultAddress = buffer + 0xFFC };
            uint text = buffer + 0x5DC, bytes = buffer + 0xC80;
            var slots = new byte[Slots * 8];
            for (int i = 0; i < arguments.Length; i++)
            {
                object a = arguments[i];
                ulong value;
                if (a is string)
                {
                    string s = (string)a;
                    if (s.Any(ch => ch < 32 || ch > 126)) throw new ArgumentException("Text arguments take plain characters only.");
                    if (text + s.Length + 1 > buffer + 0xA8C) throw new ArgumentException("The text arguments are too long for XRPC's buffer.");
                    plan.Writes.Add(new KeyValuePair<uint, byte[]>(text, Encoding.ASCII.GetBytes(s + "\0")));
                    value = text; text += (uint)s.Length + 1;
                }
                else if (a is byte[])
                {
                    var b = (byte[])a;
                    if (bytes + b.Length + 2 > buffer + 0xFF0) throw new ArgumentException("The byte arguments are too long for XRPC's buffer.");
                    plan.Writes.Add(new KeyValuePair<uint, byte[]>(bytes, (byte[])b.Clone()));
                    value = bytes; bytes += (uint)b.Length + 2;
                }
                else if (a is uint) value = (uint)a;
                else if (a is ulong) value = (ulong)a;
                else if (a is bool) value = (bool)a ? 1UL : 0UL;
                else if (a is int || a is long || a is short || a is byte || a is ushort) value = unchecked((ulong)Convert.ToInt64(a));
                else throw new ArgumentException("An argument of type " + (a == null ? "null" : a.GetType().Name) + " cannot be sent.");
                for (int k = 0; k < 8; k++) slots[i * 8 + k] = (byte)(value >> (56 - 8 * k));
            }
            plan.Writes.Add(new KeyValuePair<uint, byte[]>(buffer + 8, slots));
            if (writeCount) plan.Writes.Add(new KeyValuePair<uint, byte[]>(buffer + 4, Be32((uint)arguments.Length)));
            plan.Writes.Add(new KeyValuePair<uint, byte[]>(buffer, Be32(trigger)));      // last: this starts it
            return plan;
        }

        /// <summary>The address of XRPC's buffer, read from the plugin's memory.</summary>
        public static uint Buffer(IMemoryTarget m)
        {
            uint buffer = Be32(m.GetMemory(BufferPointer, 4));
            if (buffer < 0x80000000 || buffer == 0xFFFFFFFF) throw new InvalidOperationException("XRPC's buffer was not found (0x" + buffer.ToString("X8") + "). Is XRPC.xex the plugin this program expects?");
            return buffer;
        }

        private static uint Run(IMemoryTarget m, Plan plan)
        {
            foreach (var w in plan.Writes) m.SetMemory(w.Key, w.Value);
            System.Threading.Thread.Sleep(50);
            return Be32(m.GetMemory(plan.ResultAddress, 4));
        }

        public static uint Resolve(IMemoryTarget m, string module, uint ordinal)
        {
            if (string.IsNullOrEmpty(module) || module.Length > 64 || module.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '.' || ch == '_') || ch > 126))
                throw new ArgumentException("A module name looks like xam.xex or xboxkrnl.exe.");
            uint address = Run(m, Build(Buffer(m), TriggerResolve, false, module, ordinal));
            if (address < 0x80000000) throw new InvalidOperationException("XRPC could not find export " + ordinal + " of " + module + ".");
            return address;
        }

        /// <summary>Calls a function on a title thread.</summary>
        public static uint Call(IMemoryTarget m, uint address, params object[] arguments)
        {
            if (address < 0x80000000 || address == TriggerSystemCall || address == TriggerResolve) throw new ArgumentException("0x" + address.ToString("X8") + " cannot be called this way.");
            return Run(m, Build(Buffer(m), address, true, arguments));
        }

        /// <summary>Calls a function on a system thread (the address travels as the first argument).</summary>
        public static uint SystemCall(IMemoryTarget m, uint address, params object[] arguments)
        {
            if (address < 0x80000000) throw new ArgumentException("0x" + address.ToString("X8") + " is not a code address.");
            var all = new object[(arguments?.Length ?? 0) + 1];
            all[0] = address;
            if (arguments != null) Array.Copy(arguments, 0, all, 1, arguments.Length);
            return Run(m, Build(Buffer(m), TriggerSystemCall, true, all));
        }

        /// <summary>The text of a notification the way the console wants it: two bytes per character, a zero pair at the end.</summary>
        public static byte[] WideText(string text)
        {
            var b = new byte[text.Length * 2 + 2];
            for (int i = 0; i < text.Length; i++) b[i * 2 + 1] = (byte)text[i];
            return b;
        }

        /// <summary>A pop-up notification: xam.xex export 656 (XNotifyQueueUI) with picture, user 0, priority 2, the text and 0.</summary>
        public static void Notify(IMemoryTarget m, string text, int icon)
        {
            string problem = Jrpc.NotifyTextProblem(text);
            if (problem != null) throw new ArgumentException(problem);
            uint function = Resolve(m, "xam.xex", 656);
            SystemCall(m, function, (uint)icon, 0, 2, WideText(text), 0);
        }
    }
}
