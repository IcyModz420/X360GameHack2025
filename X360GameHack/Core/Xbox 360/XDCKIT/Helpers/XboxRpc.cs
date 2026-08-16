// =============================================================================
// XDCKIT.XboxRpc.cs - Remote-call helper (consolefeatures RPC)
// =============================================================================
// Wraps the xbdm "consolefeatures" remote-call protocol used by debug plugins
// to invoke arbitrary 360 functions.  Lives on the console instance just like
// XNotify, XboxFile and XboxAutomation:
//
//     uint xnotifyAddr = console.Rpc.ResolveFunction("xam.xex", 0x282);
//     console.Rpc.CallVoid(xnotifyAddr, 0u, "Hello world!", 4u);
//
//     int rv = console.Rpc.Call<int>("xboxkrnl.exe", 0x199, "thread.dll", 8, 0, 0);
//
// Plus a few quality-of-life helpers (GetCPUKey, GetTemperature, SetLeds,
// GetKernelVersion, GetConsoleTypeString, ShutDownConsole) that piggyback on
// the same consolefeatures channel.
//
// Requires a "consolefeatures" remote-call plugin to be loaded on the console.
// For plain Microsoft xbdm devkits use the xbdm-native methods on XboxConsole
// (Features / Memory / Threads partials).
// =============================================================================
using System;
using System.Globalization;
using System.Text;
using System.Threading;

    /// <summary>
    /// Remote-call helper (consolefeatures RPC).  Accessed via <c>console.Rpc</c>.
    /// </summary>
    public sealed class XboxRpc
    {
        /// <summary>Type ids for the consolefeatures argument stream and reply decode.</summary>
        private static class RpcWire
        {
            public const uint Void = 0;
            public const uint Int = 1;
            public const uint String = 2;
            public const uint Float = 3;
            public const uint Byte = 4;
            public const uint IntArray = 5;
            public const uint FloatArray = 6;
            public const uint ByteArray = 7;
            public const uint Uint64 = 8;
            public const uint Uint64Array = 9;
        }

        private const string ParamsA0A0 = "params=\"A\\0\\A\\0\\\"";

        private readonly XboxConsole _console;

        internal XboxRpc(XboxConsole console) => _console = console;

        private XboxClient Client => _console.Client;

        /// <summary>Payload after the first space on an xbdm status line (after the numeric code).</summary>
        private static string PayloadAfterStatusCode(string line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;
            int sp = line.IndexOf(' ');
            return sp < 0 ? line : line.Substring(sp + 1);
        }

        private static string FeatureVer2(uint type, string paramsClause)
            => "consolefeatures ver=" + ConsoleFeaturesWire.ProtocolVersion + " type=" + type + " " + paramsClause;

        #region Public Call<T> / CallVoid surface

        /// <summary>Call a function at <paramref name="address"/> and return T.</summary>
        public T Call<T>(uint address, params object[] args) where T : struct
            => (T)CallArgs(true, RpcTypeForReturn<T>(false), typeof(T), null, 0, address, 0u, args);

        /// <summary>Call a function in a module by ordinal and return T.</summary>
        public T Call<T>(string module, int ordinal, params object[] args) where T : struct
            => (T)CallArgs(true, RpcTypeForReturn<T>(false), typeof(T), module, ordinal, 0u, 0u, args);

        /// <summary>Call a function in a module by ordinal on a specific thread.</summary>
        public T Call<T>(ThreadType thread, string module, int ordinal, params object[] args) where T : struct
            => (T)CallArgs(thread == ThreadType.System, RpcTypeForReturn<T>(false), typeof(T), module, ordinal, 0u, 0u, args);

        /// <summary>Call a function and return an array (Int/Float/Byte/UInt64) of the given length.</summary>
        public T[] CallArray<T>(uint address, uint arraySize, params object[] args) where T : struct
            => (T[])CallArgs(true, RpcTypeForReturn<T>(true), typeof(T), null, 0, address, arraySize, args);

        public T[] CallArray<T>(string module, int ordinal, uint arraySize, params object[] args) where T : struct
            => (T[])CallArgs(true, RpcTypeForReturn<T>(true), typeof(T), module, ordinal, 0u, arraySize, args);

        /// <summary>Call a function returning a string (UTF-8).</summary>
        public string CallString(uint address, params object[] args)
            => (string)CallArgs(true, RpcWire.String, typeof(string), null, 0, address, 0u, args);

        public string CallString(string module, int ordinal, params object[] args)
            => (string)CallArgs(true, RpcWire.String, typeof(string), module, ordinal, 0u, 0u, args);

        /// <summary>Call a function with no return value (or one we don't care about).</summary>
        public void CallVoid(uint address, params object[] args)
            => CallArgs(true, RpcWire.Void, typeof(void), null, 0, address, 0u, args);

        public void CallVoid(string module, int ordinal, params object[] args)
            => CallArgs(true, RpcWire.Void, typeof(void), module, ordinal, 0u, 0u, args);

        public void CallVoid(ThreadType thread, string module, int ordinal, params object[] args)
            => CallArgs(thread == ThreadType.System, RpcWire.Void, typeof(void), module, ordinal, 0u, 0u, args);

        #endregion

        #region ResolveFunction

        /// <summary>
        /// Get the runtime address of an exported ordinal in a module
        /// (e.g. <c>ResolveFunction("xam.xex", 0x282)</c> for XNotifyQueueUI).
        /// </summary>
        public uint ResolveFunction(string moduleName, uint ordinal)
        {
            if (!_console.Connected) throw new InvalidOperationException("Not connected.");
            string cmd = FeatureVer2(9, "params=\"A\\0\\A\\2\\" +
                         "2/" + moduleName.Length + "\\" + moduleName.ToHexString() + "\\" +
                         "1\\" + ordinal + "\\\"");
            var resp = Client.SendTextCommand(cmd);
            if (!resp.IsSuccess) return 0;
            return XboxExtensions.ParseHexUInt(PayloadAfterStatusCode(resp.StatusMessage));
        }

        public uint ResolveFunction(string moduleName, int ordinal) => ResolveFunction(moduleName, (uint)ordinal);

        #endregion

        #region Quality-of-life helpers (GetCPUKey / Temperature / KernelVersion / etc.)

        /// <summary>Get CPU key via consolefeatures type=10.</summary>
        public string GetCPUKey()
        {
            var resp = Client.SendTextCommand(FeatureVer2(10, ParamsA0A0));
            return resp.IsSuccess ? resp.StatusMessage.Trim() : null;
        }

        /// <summary>Shutdown via consolefeatures type=11.</summary>
        public void ShutDownConsole()
        {
            try { Client.SendTextCommand(FeatureVer2(11, ParamsA0A0)); }
            catch { /* most likely the console disappeared */ }
        }

        /// <summary>Get kernel version via consolefeatures type=13.</summary>
        public uint GetKernelVersion()
        {
            var resp = Client.SendTextCommand(FeatureVer2(13, ParamsA0A0));
            if (!resp.IsSuccess) return 0;
            string s = PayloadAfterStatusCode(resp.StatusMessage);
            return uint.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        /// <summary>Set the front-panel LEDs via consolefeatures type=14.</summary>
        public void SetLeds(LEDState topLeft, LEDState topRight, LEDState bottomLeft, LEDState bottomRight)
        {
            string cmd = FeatureVer2(14, "params=\"A\\0\\A\\4\\" +
                "1\\" + (uint)topLeft + "\\" +
                "1\\" + (uint)topRight + "\\" +
                "1\\" + (uint)bottomLeft + "\\" +
                "1\\" + (uint)bottomRight + "\\\"");
            Client.SendTextCommand(cmd);
        }

        /// <summary>Get temperature via consolefeatures type=15.</summary>
        public uint GetTemperature(TemperatureFlag which)
        {
            string cmd = FeatureVer2(15, "params=\"A\\0\\A\\1\\1\\" + (int)which + "\\\"");
            var resp = Client.SendTextCommand(cmd);
            if (!resp.IsSuccess) return 0;
            string s = PayloadAfterStatusCode(resp.StatusMessage);
            return uint.TryParse(s.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        /// <summary>Get console type string via consolefeatures type=17.</summary>
        public string GetConsoleTypeString()
        {
            var resp = Client.SendTextCommand(FeatureVer2(17, ParamsA0A0));
            if (!resp.IsSuccess) return null;
            return PayloadAfterStatusCode(resp.StatusMessage).Trim();
        }

        #endregion

        #region The shared CallArgs implementation (heart of the RPC protocol)

        /// <summary>
        /// Build a consolefeatures RPC command, send it, walk the reply.
        /// Returns boxed result of the type the caller asked for.
        /// </summary>
        private object CallArgs(bool systemThread, uint type, Type returnType,
                                string module, int ordinal, uint address, uint arraySize,
                                params object[] arguments)
        {
            if (!_console.Connected) throw new InvalidOperationException("Not connected.");
            if (arguments == null) arguments = new object[0];
            if (arguments.Length > 37)
                throw new ArgumentException("Cannot use more than 37 parameters in a Call.");

            // While the RPC is running, temporarily widen the live socket receive
            // timeout to the console's ConversationTimeoutMs.  This is per-instance
            // so concurrent consoles can't stomp each other.
            int oldRecv = Client.ReceiveTimeout;
            int oldTcpRecv = 0;
            var tcp = Client.UnderlyingClient;
            try { if (tcp != null) oldTcpRecv = tcp.ReceiveTimeout; } catch { /* ignore */ }
            int conv = Math.Max(_console.ConversationTimeoutMs, oldRecv);
            try
            {
                Client.ReceiveTimeout = conv;
                try { if (tcp != null) tcp.ReceiveTimeout = conv; } catch { /* ignore */ }

                var sb = new StringBuilder(256);
                sb.Append("consolefeatures ver=").Append(ConsoleFeaturesWire.ProtocolVersion)
                  .Append(" type=").Append(type)
                  .Append(systemThread ? " system" : string.Empty);

                if (!string.IsNullOrEmpty(module))
                    sb.Append(" module=\"").Append(module).Append("\" ord=").Append(ordinal);

                sb.Append(" as=").Append(arraySize)
                  .Append(" params=\"A\\").Append(address.ToString("X"))
                  .Append("\\A\\").Append(arguments.Length).Append("\\");

                foreach (var arg in arguments) AppendArgToken(sb, arg);

                sb.Append("\"");

                var resp = Client.SendTextCommand(sb.ToString());
                string body = resp.StatusMessage ?? string.Empty;

                // The console may stash the result in a temp buffer it asks us to fetch.
                while (body.Contains("buf_addr="))
                {
                    Thread.Sleep(250);
                    int idx = body.IndexOf("buf_addr=", StringComparison.Ordinal);
                    string after = body.Substring(idx + "buf_addr=".Length);
                    uint bufAddr = XboxExtensions.ParseHexUInt(after);
                    var resp2 = Client.SendTextCommand("consolefeatures buf_addr=0x" + bufAddr.ToString("X"));
                    body = resp2.StatusMessage ?? string.Empty;
                }

                return DecodeReply(body, type, returnType, arraySize);
            }
            finally
            {
                Client.ReceiveTimeout = oldRecv;
                try { if (tcp != null) tcp.ReceiveTimeout = oldTcpRecv; } catch { /* ignore */ }
            }
        }

        private static void AppendArgToken(StringBuilder sb, object arg)
        {
            if (arg is uint ui)
            {
                sb.Append(RpcWire.Int).Append("\\").Append(XboxExtensions.UIntToInt(ui)).Append("\\");
                return;
            }
            if (arg is bool b)
            {
                sb.Append(RpcWire.Int).Append("/").Append(b ? 1 : 0).Append("\\");
                return;
            }
            if (arg is byte by)
            {
                sb.Append(RpcWire.Int).Append("\\").Append((int)by).Append("\\");
                return;
            }
            if (arg is short sh) { sb.Append(RpcWire.Int).Append("\\").Append((int)sh).Append("\\"); return; }
            if (arg is ushort us) { sb.Append(RpcWire.Int).Append("\\").Append((int)us).Append("\\"); return; }
            if (arg is int i) { sb.Append(RpcWire.Int).Append("\\").Append(i).Append("\\"); return; }

            if (arg is int[] iArr)
            {
                var bytes = XboxExtensions.IntArrayToByte(iArr);
                sb.Append(RpcWire.ByteArray).Append("/").Append(bytes.Length).Append("\\");
                for (int k = 0; k < bytes.Length; k++) sb.Append(bytes[k].ToString("X2"));
                sb.Append("\\");
                return;
            }
            if (arg is uint[] uArr)
            {
                var bytes = new byte[uArr.Length * 4];
                for (int k = 0; k < uArr.Length; k++) BitConverter.GetBytes(uArr[k]).CopyTo(bytes, k * 4);
                sb.Append(RpcWire.ByteArray).Append("/").Append(bytes.Length).Append("\\");
                for (int k = 0; k < bytes.Length; k++) sb.Append(bytes[k].ToString("X2"));
                sb.Append("\\");
                return;
            }

            if (arg is string s)
            {
                sb.Append(RpcWire.ByteArray).Append("/").Append(s.Length).Append("\\")
                  .Append(s.ToHexString()).Append("\\");
                return;
            }

            if (arg is float f)
            {
                sb.Append(RpcWire.Float).Append("\\")
                  .Append(f.ToString(CultureInfo.InvariantCulture)).Append("\\");
                return;
            }
            if (arg is double d)
            {
                sb.Append(RpcWire.Float).Append("\\")
                  .Append(d.ToString(CultureInfo.InvariantCulture)).Append("\\");
                return;
            }

            if (arg is float[] fArr)
            {
                int payloadLen = fArr.Length * 4;
                sb.Append(RpcWire.ByteArray).Append("/").Append(payloadLen).Append("\\");
                for (int k = 0; k < fArr.Length; k++)
                {
                    var be = BitConverter.GetBytes(fArr[k]); Array.Reverse(be);
                    for (int j = 0; j < 4; j++) sb.Append(be[j].ToString("X2"));
                }
                sb.Append("\\");
                return;
            }

            if (arg is byte[] byArr)
            {
                sb.Append(RpcWire.ByteArray).Append("/").Append(byArr.Length).Append("\\");
                for (int k = 0; k < byArr.Length; k++) sb.Append(byArr[k].ToString("X2"));
                sb.Append("\\");
                return;
            }

            // Fallback: 64-bit numeric coercion.
            sb.Append(RpcWire.Uint64).Append("\\")
              .Append(XboxExtensions.ConvertToUInt64(arg)).Append("\\");
        }

        private static object DecodeReply(string body, uint type, Type t, uint arraySize)
        {
            if (string.IsNullOrEmpty(body)) return DefaultFor(t);
            string payload = PayloadAfterStatusCode(body);

            switch (type)
            {
                case RpcWire.Int:
                    {
                        if (!uint.TryParse(payload, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)) return DefaultFor(t);
                        if (t == typeof(uint)) return n;
                        if (t == typeof(int)) return XboxExtensions.UIntToInt(n);
                        if (t == typeof(short)) return (short)n;
                        if (t == typeof(ushort)) return (ushort)n;
                        return n;
                    }
                case RpcWire.String: return payload;

                case RpcWire.Float:
                    if (t == typeof(double)) return double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv) ? (object)dv : 0d;
                    if (t == typeof(float)) return float.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv) ? (object)fv : 0f;
                    return 0f;

                case RpcWire.Byte:
                    {
                        if (!byte.TryParse(payload, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var by)) return DefaultFor(t);
                        if (t == typeof(byte)) return by;
                        if (t == typeof(char)) return (char)by;
                        return by;
                    }

                case RpcWire.Uint64:
                    {
                        if (!ulong.TryParse(payload, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var ul)) return DefaultFor(t);
                        if (t == typeof(long)) return BitConverter.ToInt64(BitConverter.GetBytes(ul), 0);
                        return ul;
                    }

                case RpcWire.IntArray:
                    return ParseSemicolonHexArrayUInt(payload);

                case RpcWire.FloatArray:
                    return ParseSemicolonFloatArray(payload, arraySize);

                case RpcWire.ByteArray:
                    return ParseSemicolonByteArray(payload, arraySize);

                case RpcWire.Uint64Array:
                    {
                        var ulArr = ParseSemicolonULongArray(payload, arraySize);
                        if (t == typeof(long))
                        {
                            var asLong = new long[ulArr.Length];
                            for (int i = 0; i < ulArr.Length; i++)
                                asLong[i] = BitConverter.ToInt64(BitConverter.GetBytes(ulArr[i]), 0);
                            return asLong;
                        }
                        return ulArr;
                    }

                case RpcWire.Void: return 0u;

                default:
                    if (!ulong.TryParse(payload, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fall)) return DefaultFor(t);
                    return fall;
            }
        }

        #endregion

        #region Semicolon-separated array parsers (RPC reply format)
        //
        // Wire format produced by the consolefeatures plugin:
        //
        //     val0,val1,val2,...,valN;<trailing junk>
        //
        // The semicolon is a hard terminator.  These helpers scan once over
        // the payload using int indices; no per-character string concat.
        //

        private static uint[] ParseSemicolonHexArrayUInt(string payload)
        {
            var list = new System.Collections.Generic.List<uint>(16);
            int start = 0, n = payload?.Length ?? 0;
            for (int i = 0; i < n; i++)
            {
                char c = payload[i];
                if (c == ',' || c == ';')
                {
                    if (i > start)
                    {
                        var slice = payload.Substring(start, i - start);
                        list.Add(uint.TryParse(slice, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0u);
                    }
                    if (c == ';') return list.ToArray();
                    start = i + 1;
                }
            }
            if (n > start)
            {
                var slice = payload.Substring(start, n - start);
                list.Add(uint.TryParse(slice, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0u);
            }
            return list.ToArray();
        }

        private static float[] ParseSemicolonFloatArray(string payload, uint arraySize)
        {
            var arr = new float[arraySize];
            int idx = 0, start = 0, n = payload?.Length ?? 0;
            for (int i = 0; i < n; i++)
            {
                char c = payload[i];
                if (c == ',' || c == ';')
                {
                    if (i > start && idx < arr.Length)
                    {
                        var slice = payload.Substring(start, i - start);
                        arr[idx++] = float.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
                    }
                    if (c == ';') return arr;
                    start = i + 1;
                }
            }
            if (n > start && idx < arr.Length)
            {
                var slice = payload.Substring(start, n - start);
                arr[idx++] = float.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
            }
            return arr;
        }

        private static byte[] ParseSemicolonByteArray(string payload, uint arraySize)
        {
            var arr = new byte[arraySize];
            int idx = 0, start = 0, n = payload?.Length ?? 0;
            for (int i = 0; i < n; i++)
            {
                char c = payload[i];
                if (c == ',' || c == ';')
                {
                    if (i > start && idx < arr.Length)
                    {
                        var slice = payload.Substring(start, i - start);
                        arr[idx++] = byte.TryParse(slice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (byte)0;
                    }
                    if (c == ';') return arr;
                    start = i + 1;
                }
            }
            if (n > start && idx < arr.Length)
            {
                var slice = payload.Substring(start, n - start);
                arr[idx++] = byte.TryParse(slice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (byte)0;
            }
            return arr;
        }

        private static ulong[] ParseSemicolonULongArray(string payload, uint arraySize)
        {
            var arr = new ulong[arraySize];
            int idx = 0, start = 0, n = payload?.Length ?? 0;
            for (int i = 0; i < n; i++)
            {
                char c = payload[i];
                if (c == ',' || c == ';')
                {
                    if (i > start && idx < arr.Length)
                    {
                        var slice = payload.Substring(start, i - start);
                        arr[idx++] = ulong.TryParse(slice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0UL;
                    }
                    if (c == ';') return arr;
                    start = i + 1;
                }
            }
            if (n > start && idx < arr.Length)
            {
                var slice = payload.Substring(start, n - start);
                arr[idx++] = ulong.TryParse(slice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0UL;
            }
            return arr;
        }

        #endregion

        #region Helpers

        private static uint RpcTypeForReturn<T>(bool isArray) where T : struct
        {
            Type t = typeof(T);
            if (t == typeof(int) || t == typeof(uint) || t == typeof(short) || t == typeof(ushort))
                return isArray ? RpcWire.IntArray : RpcWire.Int;
            if (t == typeof(string) || t == typeof(char[])) return RpcWire.String;
            if (t == typeof(float) || t == typeof(double)) return isArray ? RpcWire.FloatArray : RpcWire.Float;
            if (t == typeof(byte) || t == typeof(char)) return isArray ? RpcWire.ByteArray : RpcWire.Byte;
            if (t == typeof(ulong) || t == typeof(long)) return isArray ? RpcWire.Uint64Array : RpcWire.Uint64;
            return RpcWire.Uint64;
        }

        private static object DefaultFor(Type t) => t == null || !t.IsValueType ? null : Activator.CreateInstance(t);

        #endregion
    }

    /// <summary>Shared <c>consolefeatures</c> wire builders (used by <see cref="XboxRpc"/> and other helpers).</summary>
    internal static class ConsoleFeaturesWire
    {
        internal const uint ProtocolVersion = 2;

        public static string Type12Notify(string message, XNotiyLogo logo)
        {
            return "consolefeatures ver=" + ProtocolVersion + " type=12 params=\"A\\0\\A\\2\\" +
                "2/" + message.Length + "\\" + XboxExtensions.ConvertStringToHex(message, Encoding.ASCII) + "\\" +
                "1\\" + (int)logo + "\\\"";
        }

        public static string Type18ConstantMemory(uint address, uint value, bool useIfValue, uint ifValue, bool useTitleId, uint titleId)
        {
            return $"consolefeatures ver={ProtocolVersion} type=18 params=\"A\\{address:X}\\A\\5\\" +
                $"1\\{XboxExtensions.UIntToInt(value)}\\" +
                $"1\\{(useIfValue ? 1 : 0)}\\" +
                $"1\\{ifValue}\\" +
                $"1\\{(useTitleId ? 1 : 0)}\\" +
                $"1\\{XboxExtensions.UIntToInt(titleId)}\\\"";
        }
    }
