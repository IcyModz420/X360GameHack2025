using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using X360GameHack.Core.Net;

namespace X360GameHack.Core.Mods
{
    /// <summary>Turns the small set of value kinds a game mod uses into the big-endian bytes the console stores.</summary>
    public static class ModValue
    {
        public static byte[] Encode(string type, string text)
        {
            type = (type ?? "").Trim().ToLowerInvariant();
            text = (text ?? "").Trim();
            switch (type)
            {
                case "u8": case "i8": case "byte": return new[] { (byte)ParseUL(text, 0xFF) };
                case "u16": case "i16": return Be(ParseUL(text, 0xFFFF), 2);
                case "u32": case "i32": return Be(ParseUL(text, 0xFFFFFFFF), 4);
                case "u64": case "i64": return Be(ParseUL(text, ulong.MaxValue), 8);
                case "f32": case "float": { byte[] b = BitConverter.GetBytes(float.Parse(text, CultureInfo.InvariantCulture)); Array.Reverse(b); return b; }
                case "f64": case "double": { byte[] b = BitConverter.GetBytes(double.Parse(text, CultureInfo.InvariantCulture)); Array.Reverse(b); return b; }
                case "bytes": case "hex": return Hex(text);
                case "ascii": case "string": return Encoding.ASCII.GetBytes(text);
                default: throw new FormatException("Unknown value type \"" + type + "\". Use u8, u16, u32, u64, f32, f64, bytes or ascii.");
            }
        }

        private static ulong ParseUL(string s, ulong max)
        {
            ulong v;
            bool ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ulong.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)
                : (s.StartsWith("-") ? ulong.TryParse(s.Substring(1), out v) : ulong.TryParse(s, out v));
            if (!ok) throw new FormatException("\"" + s + "\" is not a number.");
            if (s.StartsWith("-")) v = unchecked((ulong)(-(long)v));
            if (max != ulong.MaxValue && v > max) throw new FormatException("\"" + s + "\" does not fit in this value's size.");
            return v;
        }

        private static byte[] Be(ulong v, int n) { var b = new byte[n]; for (int i = 0; i < n; i++) b[n - 1 - i] = (byte)(v >> (8 * i)); return b; }
        private static byte[] Hex(string s)
        {
            s = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (s.Length % 2 != 0) throw new FormatException("Hex bytes need an even number of digits.");
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }
    }

    public sealed class GameModWrite { public uint Address; public byte[] On; public byte[] Off; }
    public sealed class GameModCall { public string Module; public uint Ordinal; public uint Address; public object[] Args = new object[0]; }

    /// <summary>One entry of a drop-down input, and the named values it stands for.</summary>
    public sealed class GameModChoice { public string Label = ""; public Dictionary<string, string> Set = new Dictionary<string, string>(); }

    /// <summary>Something the user fills in before an option runs: a number, a piece of text, a tick box, a drop-down or a file.</summary>
    public sealed class GameModInput
    {
        public string Id = "", Label = "", Type = "number", Default = "";
        public double Min, Max = 100;
        public int MaxLength = 64;
        /// <summary>Text that is a command for the user's own game: any plain character is allowed.</summary>
        public bool Raw;
        public List<GameModChoice> Choices = new List<GameModChoice>();
    }

    public sealed class GameModOption
    {
        public string Name = "", Does = "", Group = "";
        public bool Toggle;                 // has an "off" as well as an "on"
        public List<GameModWrite> Writes = new List<GameModWrite>();
        public List<GameModCall> Calls = new List<GameModCall>();
        public List<GameModInput> Inputs = new List<GameModInput>();
        public List<JObject> Steps = new List<JObject>(), Off = new List<JObject>();
        public bool HasSteps => Steps.Count > 0 || Off.Count > 0;
    }

    public sealed class GameModKind
    {
        public string Game = "", Notes = "";
        public uint TitleId;
        /// <summary>The title update the mod's addresses are for (0 = not stated). Compared with what the console is running.</summary>
        public int TitleUpdate;
        /// <summary>Other title IDs the same game was sold under (a bundle, for instance).</summary>
        public List<uint> OtherTitleIds = new List<uint>();
        public List<GameModOption> Options = new List<GameModOption>();
        /// <summary>The groups of options in the order they first appear ("" for options without one).</summary>
        public List<string> Groups => Options.Select(o => o.Group ?? "").Distinct().ToList();
    }

    /// <summary>Paid or free game mods loaded at runtime. Kept in memory only.</summary>
    public static class GameMods
    {
        private static readonly List<GameModKind> _mods = new List<GameModKind>();
        public static void Register(GameModKind k) { lock (_mods) { _mods.RemoveAll(m => m.Game == k.Game && m.TitleId == k.TitleId); _mods.Add(k); } }
        public static void ClearExternal() { lock (_mods) _mods.Clear(); }
        public static IReadOnlyList<GameModKind> All { get { lock (_mods) return _mods.ToList(); } }
    }

    // What a decrypted game-mod payload looks like (only ever built from bytes in memory).
    internal sealed class GameModPayload
    {
        public string kind = "game", game = "", titleId = "", notes = "";
        public List<string> titleIds = new List<string>();
        public int titleUpdate;
        public List<GameModOptionJson> options = new List<GameModOptionJson>();
    }
    internal sealed class GameModOptionJson
    {
        public string name = "", does = "", group = "";
        public bool toggle;
        public List<GameModWriteJson> writes = new List<GameModWriteJson>();
        public List<GameModCallJson> calls = new List<GameModCallJson>();
        public List<GameModInputJson> inputs = new List<GameModInputJson>();
        public List<JObject> steps = new List<JObject>(), off = new List<JObject>();
    }
    internal sealed class GameModInputJson
    {
        public string id = "", label = "", type = "number";
        public JToken @default;
        public double min, max = 100;
        public int maxLength = 64;
        public bool raw;
        public List<GameModChoiceJson> choices = new List<GameModChoiceJson>();
    }
    internal sealed class GameModChoiceJson { public string label = ""; public Dictionary<string, JToken> set = new Dictionary<string, JToken>(); }
    internal sealed class GameModWriteJson { public string address = "", type = "u32", value = "", off; }
    internal sealed class GameModCallJson { public string module; public uint ordinal; public string address; public object[] args; }

    public static class GameModAdapter
    {
        private static readonly string[] InputTypes = { "number", "text", "bool", "choice", "file" };

        public static GameModKind Build(byte[] payload)
        {
            var p = JsonConvert.DeserializeObject<GameModPayload>(Encoding.UTF8.GetString(payload)) ?? throw new InvalidOperationException("The mod data is not readable.");
            if (!string.Equals(p.kind, "game", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("That mod is not a game mod.");
            var kind = new GameModKind { Game = p.game, Notes = p.notes ?? "", TitleId = uint.Parse(p.titleId, NumberStyles.HexNumber), TitleUpdate = Math.Max(0, p.titleUpdate) };
            foreach (string other in p.titleIds ?? new List<string>()) kind.OtherTitleIds.Add(uint.Parse(other, NumberStyles.HexNumber));
            foreach (var o in p.options ?? new List<GameModOptionJson>())
            {
                var opt = new GameModOption { Name = o.name, Does = o.does, Group = o.group ?? "", Toggle = o.toggle };
                foreach (var w in o.writes ?? new List<GameModWriteJson>())
                {
                    uint addr = uint.Parse(w.address.Replace("0x", ""), NumberStyles.HexNumber);
                    var gw = new GameModWrite { Address = addr, On = ModValue.Encode(w.type, w.value) };
                    if (!string.IsNullOrEmpty(w.off)) { gw.Off = ModValue.Encode(w.type, w.off); opt.Toggle = true; }
                    opt.Writes.Add(gw);
                }
                foreach (var c in o.calls ?? new List<GameModCallJson>())
                    opt.Calls.Add(new GameModCall { Module = c.module, Ordinal = c.ordinal, Address = string.IsNullOrEmpty(c.address) ? 0 : uint.Parse(c.address.Replace("0x", ""), NumberStyles.HexNumber), Args = c.args ?? new object[0] });

                var names = new List<string>();
                foreach (var i in o.inputs ?? new List<GameModInputJson>())
                {
                    string type = (i.type ?? "").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(i.id)) throw new InvalidOperationException("\"" + o.name + "\": an input has no id.");
                    if (!InputTypes.Contains(type)) throw new InvalidOperationException("\"" + o.name + "\": the input \"" + i.id + "\" has the unknown type \"" + i.type + "\".");
                    var input = new GameModInput { Id = i.id, Label = string.IsNullOrWhiteSpace(i.label) ? i.id : i.label, Type = type, Min = i.min, Max = i.max, MaxLength = Math.Max(1, Math.Min(4096, i.maxLength)), Raw = i.raw,
                                                   Default = i.@default == null || i.@default.Type == JTokenType.Null ? "" : i.@default.Type == JTokenType.Boolean ? ((bool)i.@default ? "1" : "0") : Convert.ToString(((JValue)i.@default).Value, CultureInfo.InvariantCulture) };
                    foreach (var c in i.choices ?? new List<GameModChoiceJson>())
                    {
                        var choice = new GameModChoice { Label = c.label ?? "" };
                        foreach (var kv in c.set ?? new Dictionary<string, JToken>()) { choice.Set[kv.Key] = Convert.ToString(kv.Value is JValue v ? v.Value : kv.Value, CultureInfo.InvariantCulture); if (!names.Contains(kv.Key)) names.Add(kv.Key); }
                        input.Choices.Add(choice);
                    }
                    if (type == "choice" && input.Choices.Count == 0) throw new InvalidOperationException("\"" + o.name + "\": the drop-down \"" + i.id + "\" has no entries.");
                    names.Add(i.id);
                    opt.Inputs.Add(input);
                }
                opt.Steps.AddRange(o.steps ?? new List<JObject>());
                opt.Off.AddRange(o.off ?? new List<JObject>());
                string problem = ModScript.Problem(opt.Steps, names) ?? ModScript.Problem(opt.Off, names);
                if (problem != null) throw new InvalidOperationException("\"" + o.name + "\": " + problem);
                if (opt.Off.Count > 0) opt.Toggle = true;
                kind.Options.Add(opt);
            }
            return kind;
        }
    }

    /// <summary>
    /// Runs a game-mod option on the current memory target. Memory writes go the plain way (XBDM on a console, or
    /// Xenia) - no plugin is needed for those. A call into a game function is what needs a plugin, and it goes
    /// through the console route chain: XBDM has no "call", so JRPC2 is tried, then XRPC. Calls therefore work on a
    /// console (with a plugin loaded), not in Xenia.
    /// </summary>
    public static class GameModRunner
    {
        /// <summary>
        /// Why this mod must not be used on what the console is running right now, or null when it is the right
        /// game. A mod's addresses only mean something inside the game it was made for.
        /// </summary>
        public static string WrongGame(GameModKind kind, XboxConsole console)
        {
            if (kind == null || console == null || kind.TitleId == 0) return null;
            uint running;
            try { running = ConsoleExtras.TitleId(console); }
            catch (Exception ex) { return "The game running on the console could not be identified (" + ex.Message + "), so the mod was not applied."; }
            if (running == kind.TitleId || kind.OtherTitleIds.Contains(running)) return null;
            return "This mod is for " + kind.Game + " (title " + kind.TitleId.ToString("X8") + "), but the console is running title " + running.ToString("X8") + ". Start " + kind.Game + " first.";
        }

        /// <summary>
        /// Says when the game on the console is on a different title update than the mod was made for, or null when
        /// it is the same (or either side is not known). A mod's addresses are for one build of the game: on another
        /// they point at the wrong places. <paramref name="older"/> tells the two cases apart - an older game is the
        /// risky one; a newer number usually just means a newer update than the mod's maker knew of.
        /// </summary>
        public static string UpdateProblem(GameModKind kind, uint runningVersion, out bool older)
        {
            older = false;
            if (kind == null || kind.TitleUpdate <= 0 || runningVersion == 0) return null;
            int running = (int)((runningVersion >> 8) & 0xFFFF);
            if (running == kind.TitleUpdate) return null;
            older = running < kind.TitleUpdate;
            string now = running == 0 ? "the game as sold, with no title update" : "title update " + running;
            if (older)
                return "This mod was made for title update " + kind.TitleUpdate + " of " + kind.Game + ", the latest one. The console is running " + now + ". " +
                       "On another update the addresses point at the wrong places, which can crash the game. Install title update " + kind.TitleUpdate + " first (Title Updates page).";
            return "The console is running " + now + " of " + kind.Game + "; this mod was made for title update " + kind.TitleUpdate + ", the latest one its maker knew of. " +
                   "If the console simply has a newer update, the addresses may no longer match.";
        }

        /// <summary>The values the option's steps work with, checked against what each input allows.</summary>
        public static Dictionary<string, object> Variables(GameModOption opt, IDictionary<string, string> given)
        {
            var vars = new Dictionary<string, object>();
            foreach (var i in opt.Inputs)
            {
                string text = given != null && given.TryGetValue(i.Id, out string g) && g != null ? g : i.Default ?? "";
                switch (i.Type)
                {
                    case "number":
                    {
                        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) throw new ArgumentException(i.Label + ": \"" + text + "\" is not a number.");
                        if (d < i.Min || d > i.Max) throw new ArgumentException(i.Label + ": " + ModExpr.Text(d) + " is outside " + ModExpr.Text(i.Min) + " to " + ModExpr.Text(i.Max) + ".");
                        vars[i.Id] = Math.Abs(d - Math.Round(d)) < 1e-9 ? (object)(long)Math.Round(d) : d;
                        break;
                    }
                    case "bool":
                        vars[i.Id] = text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) ? 1L : 0L;
                        break;
                    case "choice":
                    {
                        if (!int.TryParse(text.Trim(), out int pick)) pick = Math.Max(0, i.Choices.FindIndex(c => c.Label == text));
                        if (pick < 0 || pick >= i.Choices.Count) throw new ArgumentException(i.Label + ": pick one of the entries.");
                        vars[i.Id] = (long)pick;
                        foreach (var kv in i.Choices[pick].Set) vars[kv.Key] = kv.Value;
                        break;
                    }
                    case "file":
                        vars[i.Id] = text;
                        break;
                    default:
                        if (text.Length > i.MaxLength) throw new ArgumentException(i.Label + ": at most " + i.MaxLength + " characters.");
                        if (text.Any(c => c < 32 || c > 126)) throw new ArgumentException(i.Label + ": plain characters only (letters, digits and ordinary symbols).");
                        if (!i.Raw && text.IndexOf('"') >= 0) throw new ArgumentException(i.Label + ": a double quote cannot be used here.");
                        vars[i.Id] = text;
                        break;
                }
            }
            return vars;
        }

        private const int MaxFileBytes = 8 * 1024 * 1024;

        /// <param name="enable">true applies the "on" bytes; false, for a toggle, applies the "off" bytes.</param>
        public static ModRunResult Apply(GameModOption opt, IMemoryTarget target, XboxConsole console, bool enable, IDictionary<string, string> inputs = null)
        {
            var r = new ModRunResult();
            if (opt.HasSteps)
            {
                if (!enable && opt.Off.Count == 0) throw new InvalidOperationException("This option has no \"off\" - it is applied once.");
                var vars = Variables(opt, inputs);
                ModScript.Run(enable ? opt.Steps : opt.Off, target, console, vars, r, id =>
                {
                    string path = vars.TryGetValue(id, out object v) ? Convert.ToString(v) : "";
                    if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) throw new ArgumentException("Choose the file first.");
                    if (new System.IO.FileInfo(path).Length > MaxFileBytes) throw new ArgumentException("That file is larger than " + MaxFileBytes / (1024 * 1024) + " MB.");
                    return System.IO.File.ReadAllBytes(path);
                });
                return r;
            }
            foreach (var w in opt.Writes)
            {
                byte[] bytes = enable ? w.On : (w.Off ?? w.On);
                if (bytes == null) continue;
                target.SetMemory(w.Address, bytes);
                r.Writes++;
            }
            if (!r.Routes.Contains("XBDM") && opt.Writes.Count > 0) r.Routes.Add(target is XboxConsole ? "XBDM" : "Xenia");
            if (enable)
                foreach (var c in opt.Calls)
                {
                    if (console == null) throw new InvalidOperationException("This option calls a game function, which needs the console and the JRPC2 or XRPC plugin - it cannot run in Xenia.");
                    uint addr = c.Address != 0 ? c.Address : ConsoleExtras.Resolve(console, c.Module, c.Ordinal);
                    ConsoleExtras.Call(console, addr, c.Args);
                    r.Calls++;
                    string route = ConsoleExtras.Name(ConsoleExtras.LastRoute);
                    if (!r.Routes.Contains(route)) r.Routes.Add(route);
                }
            return r;
        }
    }
}