using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace X360GameHack.Core.Net
{
    /// <summary>
    /// Microsoft's own remote procedure call (XDRPC), built into a development kit's xbdm: functions on the console are
    /// called through the "rpc" command, no plugin needed (on RGH / JTAG consoles the JRPC2 plugin does this job, see Jrpc).
    ///
    /// One call is one buffer that travels to the console and back:
    ///   rpc system|title version=4 buf_size=N processor=5 thread=     -> "204- buf_addr=XXXXXXXX" (where the console put it)
    ///   the N bytes                                                    -> a "203" line
    ///   8-byte big-endian words: 997 while the call runs, then its completion status (0 = done)
    ///   the N bytes again, as the call left them.
    /// The buffer: 8-byte big-endian slots - [0] HRESULT (out), [1] return value (out), [2] 0, [3] flags (bit 0: float
    /// return), [4] integer argument count, [5] float argument count (0), [6] pointer to the module name or 0, [7] ordinal
    /// or address; then up to 8 integer arguments; then the module name and data regions, each 8-aligned, that pointer
    /// arguments point into (buf_addr + offset). Whatever the function writes into a region comes back with the buffer -
    /// a handle, a status block, file data.
    /// The protocol as published by the Unify360 project (checked there against a devkit's xbdm 2.0.17489.0); written
    /// here from that description.
    /// </summary>
    public static class Xdrpc
    {
        private const int Slot = 8, HeaderSlots = 8;
        private const ulong Pending = 997;          // ERROR_IO_PENDING
        public const int MaxIntegerArgs = 8;

        /// <summary>A region of the call buffer: sent as <see cref="Data"/>, read back into <see cref="After"/>.</summary>
        public sealed class Region
        {
            public byte[] Data;
            internal int Offset;
            /// <summary>Its address on the console during the call (known once the console has placed the buffer).</summary>
            public uint Address { get; internal set; }
            /// <summary>What the region holds after the call.</summary>
            public byte[] After { get; internal set; }
        }

        /// <summary>One call: what to call, its arguments (numbers or regions) and the regions themselves.</summary>
        public sealed class Request
        {
            /// <summary>Module and ordinal (the console resolves them), or <see cref="Address"/> when Module is null.</summary>
            public string Module;
            public uint Ordinal, Address;
            /// <summary>Run on a system thread (true) or a title thread.</summary>
            public bool System = true;
            /// <summary>Up to 8: ulong / uint / int values, or a <see cref="Region"/> (its address is passed).</summary>
            public readonly List<object> Args = new List<object>();
            public readonly List<Region> Regions = new List<Region>();
            /// <summary>Runs once every region's address is known - for structures that point at other regions.</summary>
            public Action<Request> Fill;

            public Region Add(int size) { var r = new Region { Data = new byte[size] }; Regions.Add(r); return r; }
            public Region Add(byte[] data) { var r = new Region { Data = data ?? new byte[0] }; Regions.Add(r); return r; }
        }

        public sealed class Result
        {
            public uint HResult;
            public ulong Value;
            public uint Value32 => (uint)Value;
        }

        private static int Align8(int n) => (n + 7) & ~7;

        public static void PutBE64(byte[] b, int o, ulong v) { for (int i = 7; i >= 0; i--) { b[o + i] = (byte)v; v >>= 8; } }
        public static void PutBE32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        public static ulong BE64(byte[] b, int o) { ulong v = 0; for (int i = 0; i < 8; i++) v = v << 8 | b[o + i]; return v; }
        public static uint BE32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

        /// <summary>
        /// True when the console's xbdm answers the "rpc" command (a development kit; some RGH plugins answer it too).
        /// The bare command only asks - it starts nothing.
        /// </summary>
        public static bool IsAvailable(XboxConsole c)
        {
            var r = c.SendTextCommand("rpc");
            int code = (int)r.Status;
            return (code >= 200 && code < 300) || (r.StatusMessage ?? "").IndexOf("error=", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Makes one call. Throws when the console refuses it or the call reports a failure HRESULT.</summary>
        public static Result Call(XboxConsole c, Request q)
        {
            int argc = q.Args.Count;
            if (argc > MaxIntegerArgs) throw new ArgumentException("A call takes at most " + MaxIntegerArgs + " integer arguments.");
            int valueRegion = (HeaderSlots + argc) * Slot, pos = valueRegion;
            byte[] module = null;
            int moduleAt = 0;
            if (q.Module != null)
            {
                if (q.Module.Length == 0 || q.Module.Length > 64) throw new ArgumentException("A module name looks like xboxkrnl.exe or xam.xex.");
                module = Encoding.ASCII.GetBytes(q.Module + "\0");
                moduleAt = pos; pos += Align8(module.Length);
            }
            foreach (var r in q.Regions) { r.Offset = pos; pos += Align8(r.Data.Length); }
            int size = pos;

            var start = c.SendTextCommand("rpc " + (q.System ? "system" : "title") + " version=4 buf_size=" + size.ToString(CultureInfo.InvariantCulture) + " processor=5 thread=");
            if ((int)start.Status != 204) throw new InvalidOperationException("The console did not take the call: " + start);
            var m = Regex.Match(start.StatusMessage ?? "", "buf_addr=(?:0x)?([0-9a-fA-F]{1,8})");
            if (!m.Success) throw new InvalidOperationException("The console did not say where the call buffer is: " + start);
            uint bufAddr = uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            foreach (var r in q.Regions) r.Address = bufAddr + (uint)r.Offset;
            q.Fill?.Invoke(q);

            var data = new byte[size];
            PutBE64(data, 2 * Slot, 0);                 // no call after the call
            PutBE64(data, 3 * Slot, 0);                 // integer return
            PutBE64(data, 4 * Slot, (ulong)argc);
            PutBE64(data, 5 * Slot, 0);                 // no float arguments
            if (module != null)
            {
                PutBE64(data, 6 * Slot, bufAddr + (uint)moduleAt);
                PutBE64(data, 7 * Slot, q.Ordinal);
                Buffer.BlockCopy(module, 0, data, moduleAt, module.Length);
            }
            else
            {
                if (q.Address < 0x80000000) throw new ArgumentException("0x" + q.Address.ToString("X8") + " is not a code address.");
                PutBE64(data, 6 * Slot, 0);
                PutBE64(data, 7 * Slot, q.Address);
            }
            for (int i = 0; i < argc; i++)
            {
                object a = q.Args[i];
                ulong v = a is Region rg ? rg.Address : a is int n ? unchecked((uint)n) : Convert.ToUInt64(a, CultureInfo.InvariantCulture);
                PutBE64(data, (HeaderSlots + i) * Slot, v);
            }
            foreach (var r in q.Regions) Buffer.BlockCopy(r.Data, 0, data, r.Offset, r.Data.Length);

            c.Client.SendRaw(data);
            string line = c.Client.ReadLine();
            if (line == null || !line.StartsWith("203", StringComparison.Ordinal)) throw new InvalidOperationException("The console did not accept the call data: " + line);
            ulong status;
            int waits = 0;
            while ((status = BE64(c.Client.ReadExact(8), 0)) == Pending)
                if (++waits > 600) throw new TimeoutException("The call did not finish on the console.");
            if (status != 0) throw new InvalidOperationException("The call ended with status 0x" + status.ToString("X"));
            var back = c.Client.ReadExact(size);
            foreach (var r in q.Regions) { r.After = new byte[r.Data.Length]; Buffer.BlockCopy(back, r.Offset, r.After, 0, r.Data.Length); }
            var result = new Result { HResult = (uint)BE64(back, 0), Value = BE64(back, Slot) };
            if (result.HResult != 0) throw new InvalidOperationException("The call failed on the console (HRESULT 0x" + result.HResult.ToString("X8") + ").");
            return result;
        }

        /// <summary>Calls an exported function by module and ordinal with number arguments; its 32-bit return value.</summary>
        public static uint Call(XboxConsole c, string module, uint ordinal, params object[] args)
        {
            var q = new Request { Module = module, Ordinal = ordinal };
            q.Args.AddRange(args);
            return Call(c, q).Value32;
        }
    }
}
