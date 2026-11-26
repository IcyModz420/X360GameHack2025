using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using X360GameHack.Core.Net;

namespace X360GameHack.Core.Mods
{
    /// <summary>
    /// The small arithmetic a mod step may use for an address or a value: whole numbers (decimal or 0x hex),
    /// decimals, + - * / %, brackets, comparisons, "and" / "or" / "not", and {name} for an input or a value read
    /// earlier. It only computes - there is nothing here that could run code or reach a file.
    /// </summary>
    public sealed class ModExpr
    {
        private readonly string _s;
        private int _i;
        private readonly IDictionary<string, object> _vars;

        private ModExpr(string s, IDictionary<string, object> vars) { _s = s ?? ""; _vars = vars; }

        public static object Eval(string text, IDictionary<string, object> vars)
        {
            var p = new ModExpr(text, vars);
            object v = p.Or();
            p.Ws();
            if (p._i != p._s.Length) throw new FormatException("Unexpected \"" + p._s.Substring(p._i) + "\" in \"" + text + "\".");
            return v;
        }

        public static long Whole(string text, IDictionary<string, object> vars)
        {
            object v = Eval(text, vars);
            if (v is long) return (long)v;
            double d = (double)v;
            if (Math.Abs(d - Math.Round(d)) > 1e-9) throw new FormatException("\"" + text + "\" does not come out as a whole number.");
            return (long)Math.Round(d);
        }

        public static double Number(string text, IDictionary<string, object> vars) { object v = Eval(text, vars); return v is long ? (long)v : (double)v; }
        public static bool True(string text, IDictionary<string, object> vars) => Number(text, vars) != 0;

        private void Ws() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private bool Take(string op)
        {
            Ws();
            if (string.CompareOrdinal(_s, _i, op, 0, op.Length) != 0) return false;
            if (char.IsLetter(op[0]) && _i + op.Length < _s.Length && char.IsLetterOrDigit(_s[_i + op.Length])) return false;
            _i += op.Length; return true;
        }

        private static double D(object o) => o is long ? (long)o : (double)o;
        private static object Bool(bool b) => b ? 1L : 0L;

        private object Or() { object a = And(); while (Take("or")) { object b = And(); a = Bool(D(a) != 0 || D(b) != 0); } return a; }
        private object And() { object a = Cmp(); while (Take("and")) { object b = Cmp(); a = Bool(D(a) != 0 && D(b) != 0); } return a; }
        private object Cmp()
        {
            object a = Sum();
            if (Take("==")) return Bool(D(a) == D(Sum()));
            if (Take("!=")) return Bool(D(a) != D(Sum()));
            if (Take("<=")) return Bool(D(a) <= D(Sum()));
            if (Take(">=")) return Bool(D(a) >= D(Sum()));
            if (Take("<")) return Bool(D(a) < D(Sum()));
            if (Take(">")) return Bool(D(a) > D(Sum()));
            return a;
        }
        private object Sum()
        {
            object a = Term();
            for (;;)
            {
                if (Take("+")) { object b = Term(); a = a is long && b is long ? (object)checked((long)a + (long)b) : D(a) + D(b); }
                else if (Take("-")) { object b = Term(); a = a is long && b is long ? (object)checked((long)a - (long)b) : D(a) - D(b); }
                else return a;
            }
        }
        private object Term()
        {
            object a = Unary();
            for (;;)
            {
                if (Take("*")) { object b = Unary(); a = a is long && b is long ? (object)checked((long)a * (long)b) : D(a) * D(b); }
                else if (Take("/"))
                {
                    object b = Unary();
                    if (D(b) == 0) throw new FormatException("Division by zero.");
                    a = a is long && b is long && (long)a % (long)b == 0 ? (object)((long)a / (long)b) : D(a) / D(b);
                }
                else if (Take("%")) { object b = Unary(); if (D(b) == 0) throw new FormatException("Division by zero."); a = a is long && b is long ? (object)((long)a % (long)b) : D(a) % D(b); }
                else return a;
            }
        }
        private object Unary()
        {
            if (Take("-")) { object v = Unary(); return v is long ? (object)(-(long)v) : -(double)v; }
            if (Take("not")) return Bool(D(Unary()) == 0);
            return Primary();
        }
        private object Primary()
        {
            Ws();
            if (_i >= _s.Length) throw new FormatException("The expression \"" + _s + "\" ends too early.");
            char c = _s[_i];
            if (c == '(') { _i++; object v = Or(); if (!Take(")")) throw new FormatException("Missing ) in \"" + _s + "\"."); return v; }
            if (c == '{')
            {
                int end = _s.IndexOf('}', _i);
                if (end < 0) throw new FormatException("Missing } in \"" + _s + "\".");
                string name = _s.Substring(_i + 1, end - _i - 1).Trim();
                _i = end + 1;
                if (_vars == null || !_vars.TryGetValue(name, out object v)) throw new FormatException("\"" + name + "\" has no value.");
                if (v is long || v is double) return v;
                if (v is bool) return Bool((bool)v);
                return ParseNumber(Convert.ToString(v, CultureInfo.InvariantCulture), name);
            }
            int start = _i;
            if (c == '0' && _i + 1 < _s.Length && (_s[_i + 1] == 'x' || _s[_i + 1] == 'X'))
            {
                _i += 2;
                while (_i < _s.Length && Uri.IsHexDigit(_s[_i])) _i++;
            }
            else while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
            if (_i == start) throw new FormatException("Unexpected \"" + _s.Substring(_i) + "\" in \"" + _s + "\".");
            return ParseNumber(_s.Substring(start, _i - start), null);
        }

        private static object ParseNumber(string t, string what)
        {
            t = (t ?? "").Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && ulong.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong h)) return unchecked((long)h);
            if (long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            throw new FormatException(what == null ? "\"" + t + "\" is not a number." : "\"" + what + "\" is \"" + t + "\", which is not a number.");
        }

        /// <summary>Fills {name} and {=expression} in a piece of text.</summary>
        public static string Fill(string template, IDictionary<string, object> vars)
        {
            if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0) return template ?? "";
            var sb = new StringBuilder();
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c != '{') { sb.Append(c); i++; continue; }
                int end = template.IndexOf('}', i);
                if (end < 0) { sb.Append(template, i, template.Length - i); break; }
                string inner = template.Substring(i + 1, end - i - 1);
                if (inner.StartsWith("=")) sb.Append(Whole(inner.Substring(1), vars).ToString(CultureInfo.InvariantCulture));
                else if (vars != null && vars.TryGetValue(inner.Trim(), out object v)) sb.Append(Text(v));
                else sb.Append(template, i, end - i + 1);          // not a placeholder: leave it as written
                i = end + 1;
            }
            return sb.ToString();
        }

        public static string Text(object v)
        {
            if (v is double) { double d = (double)v; return Math.Abs(d - Math.Round(d)) < 1e-9 ? ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture); }
            if (v is bool) return (bool)v ? "1" : "0";
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }

    public sealed class ModRunResult
    {
        public int Writes, Calls, Reads;
        public readonly List<string> Routes = new List<string>();
        /// <summary>Set when the mod stopped by itself without an error, e.g. the game's code was not what it expects.</summary>
        public string Stopped;
    }

    /// <summary>
    /// Runs the steps of a game-mod option. Memory writes and reads go the plain way (XBDM on a console, or Xenia).
    /// Only a call into a game function needs a plugin: JRPC2, then XRPC. Steps are data, checked before anything
    /// is sent, and can only write memory, read memory, call a function at an address and pause.
    /// </summary>
    public static class ModScript
    {
        private static readonly string[] Ops = { "write", "call", "sleep", "read", "expect", "switch" };

        /// <summary>
        /// For the replay tests only: when set, a call step hands its address, thread kind and arguments to this
        /// instead of a console, so a run can be compared with what the original tool sent. Never set by the program.
        /// </summary>
        internal static Func<uint, bool, object[], uint> CallSink;
        /// <summary>For the replay tests only: receives the length of a pause instead of waiting it out.</summary>
        internal static Action<int> SleepSink;

        /// <summary>Checks a list of steps without running them. Returns null when it is sound.</summary>
        public static string Problem(IEnumerable<JObject> steps, ICollection<string> known)
        {
            var names = new HashSet<string>(known ?? new string[0]);
            return Check(steps, names);
        }

        private static string Check(IEnumerable<JObject> steps, HashSet<string> names)
        {
            foreach (var s in steps ?? Enumerable.Empty<JObject>())
            {
                string op = (string)s["op"] ?? "";
                if (!Ops.Contains(op)) return "A step has the unknown operation \"" + op + "\". Steps can write, call, sleep, read, expect or switch.";
                if (op != "sleep" && op != "switch" && s["address"] == null && !(op == "call" && s["module"] != null)) return "A \"" + op + "\" step has no address.";
                if (op == "write" && s["hex"] == null && s["value"] == null && s["parts"] == null && s["file"] == null) return "A write step needs hex, a typed value, parts or a file.";
                if (op == "write" && s["value"] != null && !Types.ContainsKey(((string)s["type"] ?? "").ToLowerInvariant())) return "A write step has the unknown type \"" + (string)s["type"] + "\".";
                if (op == "read") { if (string.IsNullOrWhiteSpace((string)s["var"])) return "A read step needs a name to keep the value under (\"var\")."; names.Add((string)s["var"]); }
                if (op == "switch")
                {
                    if (!(s["cases"] is JArray cases) || cases.Count == 0) return "A switch step needs cases.";
                    foreach (var c in cases) { string p = Check(((JArray)c).OfType<JObject>(), names); if (p != null) return p; }
                }
                if (op == "call" && s["args"] is JArray args && args.Count > 37) return "A call takes at most 37 arguments.";
            }
            return null;
        }

        private static readonly Dictionary<string, int> Types = new Dictionary<string, int>
        {
            { "u8", 1 }, { "i8", 1 }, { "u16", 2 }, { "i16", 2 }, { "u32", 4 }, { "i32", 4 }, { "u64", 8 }, { "i64", 8 },
            { "u16le", 2 }, { "i16le", 2 }, { "u32le", 4 }, { "i32le", 4 }, { "u64le", 8 }, { "i64le", 8 }, { "f32", 4 }, { "f32le", 4 }, { "f64", 8 },
        };

        /// <summary>True for the value types a step can use (u8 ... f64, with or without "le").</summary>
        public static bool KnownType(string type) => Types.ContainsKey((type ?? "").Trim().ToLowerInvariant());

        /// <summary>The bytes of a typed value: big-endian unless the type ends in "le".</summary>
        public static byte[] Encode(string type, object value)
        {
            type = (type ?? "").Trim().ToLowerInvariant();
            if (!Types.TryGetValue(type, out int size)) throw new FormatException("Unknown value type \"" + type + "\".");
            bool le = type.EndsWith("le");
            string core = le ? type.Substring(0, type.Length - 2) : type;
            byte[] b;
            if (core == "f32") b = BitConverter.GetBytes((float)(value is long ? (long)value : (double)value));
            else if (core == "f64") b = BitConverter.GetBytes(value is long ? (long)value : (double)value);
            else
            {
                if (!(value is long)) { double d = (double)value; if (Math.Abs(d - Math.Round(d)) > 1e-9) throw new FormatException("A whole number is needed here."); value = (long)Math.Round(d); }
                long v = (long)value;
                bool signed = core[0] == 'i';
                if (size < 8)
                {
                    long max = signed ? (1L << (size * 8 - 1)) - 1 : (1L << (size * 8)) - 1, min = signed ? -(1L << (size * 8 - 1)) : 0;
                    if (v < min || v > max) throw new FormatException(v + " does not fit in this value (" + min + " to " + max + ").");
                }
                b = new byte[size];
                for (int i = 0; i < size; i++) b[i] = (byte)(v >> (8 * i));             // little-endian first
                return le ? b : Reverse(b);
            }
            if (!BitConverter.IsLittleEndian) Array.Reverse(b);                        // normalise to little-endian
            return le ? b : Reverse(b);
        }

        private static byte[] Reverse(byte[] b) { var r = (byte[])b.Clone(); Array.Reverse(r); return r; }

        public static byte[] Hex(string s)
        {
            s = new string((s ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (s.Length % 2 != 0) throw new FormatException("Hex bytes need an even number of digits.");
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }

        private static uint Address(JObject s, IDictionary<string, object> vars)
        {
            long a = ModExpr.Whole(ModExpr.Fill((string)s["address"], null) ?? "", vars);
            if (a < 0 || a > uint.MaxValue) throw new FormatException("An address came out as " + a + ", which is not a 32-bit address.");
            return (uint)a;
        }

        public static void Run(IEnumerable<JObject> steps, IMemoryTarget target, XboxConsole console, IDictionary<string, object> vars, ModRunResult r, Func<string, byte[]> readFile)
        {
            foreach (var s in steps)
            {
                if (r.Stopped != null) return;
                string when = (string)s["when"];
                if (!string.IsNullOrWhiteSpace(when) && !ModExpr.True(when, vars)) continue;
                switch ((string)s["op"])
                {
                    case "write": Write(s, target, vars, r, readFile); break;
                    case "call": Call(s, console, vars, r); break;
                    case "sleep":
                    {
                        int ms = (int)Math.Max(0, Math.Min(5000, (long)(s["ms"] ?? 0)));
                        if (SleepSink != null) SleepSink(ms); else System.Threading.Thread.Sleep(ms);
                        break;
                    }
                    case "read": Read(s, target, vars, r); break;
                    case "expect":
                    {
                        byte[] want = Hex(ModExpr.Fill((string)s["hex"], vars));
                        byte[] have = target.GetMemory(Address(s, vars), (uint)want.Length);
                        r.Reads++;
                        if (!have.SequenceEqual(want)) { r.Stopped = (string)s["otherwise"] ?? "The game's code at that spot is not what this option expects - it may already be on, or this is another version of the game. Nothing more was changed."; return; }
                        break;
                    }
                    case "switch":
                    {
                        var cases = (JArray)s["cases"];
                        long pick = ModExpr.Whole("{" + (string)s["on"] + "}", vars);
                        if (pick < 0 || pick >= cases.Count) throw new FormatException("\"" + (string)s["on"] + "\" is " + pick + ", outside the " + cases.Count + " cases.");
                        Run(((JArray)cases[(int)pick]).OfType<JObject>(), target, console, vars, r, readFile);
                        break;
                    }
                }
            }
        }

        private static void Note(ModRunResult r, string route) { if (!r.Routes.Contains(route)) r.Routes.Add(route); }

        private static void Write(JObject s, IMemoryTarget target, IDictionary<string, object> vars, ModRunResult r, Func<string, byte[]> readFile)
        {
            byte[] data;
            if (s["hex"] != null) data = Hex(ModExpr.Fill((string)s["hex"], vars));
            else if (s["value"] != null) data = Encode((string)s["type"], ModExpr.Eval(ModExpr.Fill(Convert.ToString(s["value"], CultureInfo.InvariantCulture), null), vars));
            else if (s["file"] != null)
            {
                if (readFile == null) throw new InvalidOperationException("This option needs a file, which is not available here.");
                data = readFile((string)s["file"]);
            }
            else
            {
                var list = new List<byte>();
                foreach (JObject part in ((JArray)s["parts"]).OfType<JObject>())
                {
                    if (part["hex"] != null) { list.AddRange(Hex(ModExpr.Fill((string)part["hex"], vars))); continue; }
                    string text = ModExpr.Fill((string)part["text"], vars);
                    switch (((string)part["encoding"] ?? "ascii").ToLowerInvariant())
                    {
                        case "utf-16-be": list.AddRange(Encoding.BigEndianUnicode.GetBytes(text)); break;
                        case "utf-16-le": list.AddRange(Encoding.Unicode.GetBytes(text)); break;
                        default: list.AddRange(Encoding.ASCII.GetBytes(text)); break;
                    }
                }
                data = list.ToArray();
            }
            if (data.Length == 0) return;
            target.SetMemory(Address(s, vars), data);
            r.Writes++;
            Note(r, target is XboxConsole ? "XBDM" : "Xenia");
        }

        private static void Read(JObject s, IMemoryTarget target, IDictionary<string, object> vars, ModRunResult r)
        {
            string type = ((string)s["type"] ?? "u32").ToLowerInvariant();
            uint address = Address(s, vars);
            string name = (string)s["var"];
            r.Reads++;
            if (type == "ascii" || type == "bytes")
            {
                int n = (int)Math.Max(1, Math.Min(4096, (long)(s["length"] ?? 32)));
                byte[] raw = target.GetMemory(address, (uint)n);
                if (type == "bytes") { vars[name] = string.Concat(raw.Select(b => b.ToString("X2"))); return; }
                int end = Array.IndexOf(raw, (byte)0);
                string text = Encoding.ASCII.GetString(raw, 0, end < 0 ? raw.Length : end);
                // text read out of a game goes into commands: keep only what a name can hold
                vars[name] = new string(text.Where(c => c >= '0' && c <= '9' || c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c == '.' || c == '_' || c == ' ').ToArray());
                return;
            }
            if (!Types.TryGetValue(type, out int size)) throw new FormatException("Unknown value type \"" + type + "\".");
            byte[] b = target.GetMemory(address, (uint)size);
            bool le = type.EndsWith("le");
            string core = le ? type.Substring(0, type.Length - 2) : type;
            if (!le) Array.Reverse(b);                                   // now little-endian
            if (core == "f32") { vars[name] = (double)BitConverter.ToSingle(Native(b), 0); return; }
            if (core == "f64") { vars[name] = BitConverter.ToDouble(Native(b), 0); return; }
            ulong u = 0;
            for (int i = 0; i < size; i++) u |= (ulong)b[i] << (8 * i);
            long v = unchecked((long)u);
            if (core[0] == 'i' && size < 8 && (u & (1UL << (size * 8 - 1))) != 0) v = unchecked((long)(u | (ulong.MaxValue << (size * 8))));
            vars[name] = v;
        }

        private static byte[] Native(byte[] littleEndian) { if (BitConverter.IsLittleEndian) return littleEndian; var c = (byte[])littleEndian.Clone(); Array.Reverse(c); return c; }

        private static void Call(JObject s, XboxConsole console, IDictionary<string, object> vars, ModRunResult r)
        {
            if (console == null && CallSink == null) throw new InvalidOperationException("This option calls a game function, which needs the console and the JRPC2 or XRPC plugin - it cannot run in Xenia.");
            var args = Args(s, vars);
            bool system = !string.Equals((string)s["thread"], "title", StringComparison.OrdinalIgnoreCase);
            uint result;
            if (CallSink != null) result = CallSink(Address(s, vars), system, args.ToArray());
            else
            {
                uint address = s["address"] != null ? Address(s, vars) : ConsoleExtras.Resolve(console, (string)s["module"], (uint)(long)s["ordinal"]);
                result = ConsoleExtras.Call(console, address, system, args.ToArray());
                Note(r, ConsoleExtras.Name(ConsoleExtras.LastRoute));
            }
            if (!string.IsNullOrWhiteSpace((string)s["var"])) vars[(string)s["var"]] = (long)result;
            r.Calls++;
        }

        /// <summary>A call step's arguments, worked out with the current values.</summary>
        private static List<object> Args(JObject s, IDictionary<string, object> vars)
        {
            var args = new List<object>();
            foreach (var a in (s["args"] as JArray) ?? new JArray())
            {
                if (a.Type == JTokenType.Integer) { long v = (long)a; args.Add(Fit(v)); continue; }
                if (a.Type == JTokenType.Float) { args.Add((float)(double)a); continue; }
                if (a.Type == JTokenType.String) { args.Add(ModExpr.Fill((string)a, vars)); continue; }
                var o = a as JObject ?? throw new FormatException("A call argument is not readable: write a number, \"text\" or { \"s\": \"text\" }.");
                if (o["s"] != null) args.Add(ModExpr.Fill((string)o["s"], vars));
                else if (o["hex"] != null) args.Add(Hex(ModExpr.Fill((string)o["hex"], vars)));
                else if (o["f"] != null) args.Add((float)ModExpr.Number(ModExpr.Fill(Convert.ToString(o["f"], CultureInfo.InvariantCulture), null), vars));
                else if (o["expr"] != null) args.Add(Fit(ModExpr.Whole((string)o["expr"], vars)));
                else if (o["u64"] != null) args.Add(unchecked((ulong)ModExpr.Whole(Convert.ToString(o["u64"], CultureInfo.InvariantCulture), vars)));
                else throw new FormatException("A call argument of that kind cannot be sent. Use a number, { \"s\": \"text\" }, { \"hex\": \"...\" }, { \"f\": 1.5 } or { \"expr\": \"...\" }.");
            }
            if (args.Count > 37) throw new FormatException("A call takes at most 37 arguments.");
            foreach (string text in args.OfType<string>())
                if (text.Any(c => c < 32 || c > 126)) throw new FormatException("Text sent to the game may hold plain characters only.");
            return args;
        }

        private static object Fit(long v)
        {
            if (v >= int.MinValue && v <= int.MaxValue) return (int)v;
            if (v >= 0 && v <= uint.MaxValue) return (uint)v;
            throw new FormatException(v + " is too large for a call argument.");
        }

        /// <summary>A problem a dry run found in one step.</summary>
        public sealed class StepProblem { public int Line; public string Where = "", Message = ""; }

        /// <summary>
        /// Works out every step without sending anything: addresses, values, text and conditions are computed with
        /// the values given, so a typo or a name that does not exist shows up before the mod is ever used. Every
        /// case of a switch is checked, and steps with a "when" are checked whatever the condition says.
        /// </summary>
        public static List<StepProblem> DryRun(IEnumerable<JObject> steps, IDictionary<string, object> vars)
        {
            var problems = new List<StepProblem>();
            DryRun(steps, vars, problems, "");
            return problems;
        }

        private static void DryRun(IEnumerable<JObject> steps, IDictionary<string, object> vars, List<StepProblem> problems, string where)
        {
            int n = 0;
            foreach (var s in steps)
            {
                n++;
                string op = (string)s["op"] ?? "";
                string at = where + "step " + n + " (" + op + ")";
                int line = s is Newtonsoft.Json.IJsonLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;
                try
                {
                    string when = (string)s["when"];
                    if (!string.IsNullOrWhiteSpace(when)) ModExpr.True(when, vars);
                    switch (op)
                    {
                        case "write":
                            Address(s, vars);
                            if (s["hex"] != null) Hex(ModExpr.Fill((string)s["hex"], vars));
                            else if (s["value"] != null) Encode((string)s["type"], ModExpr.Eval(ModExpr.Fill(Convert.ToString(s["value"], CultureInfo.InvariantCulture), null), vars));
                            else if (s["parts"] is JArray parts)
                                foreach (JObject part in parts.OfType<JObject>())
                                {
                                    if (part["hex"] != null) Hex(ModExpr.Fill((string)part["hex"], vars));
                                    else ModExpr.Fill((string)part["text"] ?? "", vars);
                                }
                            break;
                        case "read":
                        {
                            Address(s, vars);
                            string type = ((string)s["type"] ?? "u32").ToLowerInvariant();
                            if (type != "ascii" && type != "bytes" && !Types.ContainsKey(type)) throw new FormatException("Unknown value type \"" + type + "\".");
                            vars[(string)s["var"]] = type == "ascii" || type == "bytes" ? (object)"" : 0L;
                            break;
                        }
                        case "expect":
                            Address(s, vars);
                            if (Hex(ModExpr.Fill((string)s["hex"], vars)).Length == 0) throw new FormatException("An expect step needs the bytes to look for in \"hex\".");
                            break;
                        case "call":
                            if (s["address"] != null) Address(s, vars);
                            Args(s, vars);
                            if (!string.IsNullOrWhiteSpace((string)s["var"])) vars[(string)s["var"]] = 0L;
                            break;
                        case "switch":
                        {
                            var cases = (JArray)s["cases"];
                            for (int i = 0; i < cases.Count; i++)
                                DryRun(((JArray)cases[i]).OfType<JObject>(), new Dictionary<string, object>(vars), problems, at + ", case " + (i + 1) + ", ");
                            break;
                        }
                    }
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException || ex is ArgumentException || ex is NullReferenceException)
                {
                    problems.Add(new StepProblem { Line = line, Where = at, Message = ex is NullReferenceException ? "something this step needs is missing." : ex.Message });
                }
            }
        }
    }
}
