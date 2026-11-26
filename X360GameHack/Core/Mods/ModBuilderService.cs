using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using X360GameHack.Core.Formats;

namespace X360GameHack.Core.Mods
{
    public enum ModProtection { Open, Protected, ServerOnly }

    public sealed class ModBuildResult
    {
        public byte[] Pack;
        /// <summary>For a protected mod: the key card the dev must register on their licence server. Never kept by the program.</summary>
        public string KeyCard;
        /// <summary>For a server-only mod: the payload (notes removed) the dev uploads to their licence server.</summary>
        public string ServerPayload;
        public List<string> Problems = new List<string>();
        public bool Ok => Problems.Count == 0 && Pack != null;
    }

    /// <summary>One thing "Check syntax" found, with where it is.</summary>
    public sealed class PayloadProblem
    {
        public int Line, Column;
        public string Message = "";
        public override string ToString() => (Line > 0 ? "Line " + Line + (Column > 0 ? ", column " + Column : "") + ": " : "") + Message;
    }

    /// <summary>What "Check syntax" found: problems stop a build, notes are only pointed out.</summary>
    public sealed class PayloadCheck
    {
        public readonly List<PayloadProblem> Problems = new List<PayloadProblem>();
        public readonly List<PayloadProblem> Notes = new List<PayloadProblem>();
        /// <summary>"game" or "save", and the game and Title ID the payload names (as written).</summary>
        public string Kind = "", Summary = "", Game = "", TitleId = "";
        public bool Ok => Problems.Count == 0;
    }

    /// <summary>A field of the mod (id, name, game, titleId, requires, description) that is missing or not right.</summary>
    public sealed class FieldProblem
    {
        public string Field = "", Message = "";
        /// <summary>What the Mod Maker calls the field.</summary>
        public string Label => ModBuilderService.FieldLabel(Field);
        public override string ToString() => Label + ": " + Message;
    }

    /// <summary>
    /// The maker's side: turns a dev's metadata and "what it does" into a .x360mod file. Devs can build:
    ///   - Open: a free, readable mod anyone can load (payload in the clear).
    ///   - Protected: a sealed mod unlocked by a token; the program makes a one-off key, seals the file, hands the
    ///     dev the key to put on their own licence server, and keeps no copy of it.
    ///   - Server-only: no payload in the file at all; the dev's server delivers the data at unlock.
    /// The payload is checked the same way the program will read it, so a mod that builds here will load. The
    /// payload may hold // notes; they are removed before anything is sealed.
    /// </summary>
    public static class ModBuilderService
    {
        // ------------------------------------------------------------------ checking

        /// <summary>Checks a payload and returns the first problem in plain words, or null when it is sound.</summary>
        public static string ValidatePayload(string payloadJson)
        {
            var c = Check(payloadJson);
            return c.Ok ? null : c.Problems[0].ToString();
        }

        /// <summary>"game" or "save" as the payload says, or "" when it cannot be read.</summary>
        public static string KindOf(string payloadJson)
        {
            try { return ((string)(Load(payloadJson, false) as JObject)?["kind"] ?? "").Trim().ToLowerInvariant(); }
            catch (JsonException) { return ""; }
        }

        /// <summary>The payload as compact JSON, with every // note and all spacing removed. Throws when it is not valid.</summary>
        public static string Clean(string payloadJson) => Load(payloadJson, false).ToString(Formatting.None);

        private static JToken Load(string text, bool lineInfo)
        {
            using (var reader = new JsonTextReader(new System.IO.StringReader(text ?? "")) { DateParseHandling = DateParseHandling.None })
            {
                var t = JToken.ReadFrom(reader, new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    LineInfoHandling = lineInfo ? LineInfoHandling.Load : LineInfoHandling.Ignore,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                });
                while (reader.Read()) { }      // only notes may follow the closing }; anything else throws
                return t;
            }
        }

        private static int LineOf(JToken t) => t is IJsonLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;

        /// <summary>
        /// Checks a payload the way a beginner needs it checked: a JSON mistake is explained in plain words with its
        /// line and column, entries the program does not know (usually a typo) are pointed out, and every step of a
        /// game mod is worked out with sample values - without sending anything anywhere.
        /// </summary>
        public static PayloadCheck Check(string text)
        {
            var c = new PayloadCheck();
            if (string.IsNullOrWhiteSpace(text))
            {
                c.Problems.Add(new PayloadProblem { Message = "The payload is empty. Press Insert game template or Insert save template to start from a worked example." });
                return c;
            }
            JToken root;
            try { root = Load(text, true); }
            catch (JsonReaderException ex) { c.Problems.Add(new PayloadProblem { Line = ex.LineNumber, Column = ex.LinePosition, Message = Friendly(ex) }); return c; }
            if (!(root is JObject o)) { c.Problems.Add(new PayloadProblem { Line = LineOf(root), Message = "The payload must be one { } block: it starts with { and ends with }." }); return c; }
            c.Kind = ((string)o["kind"] ?? "").Trim().ToLowerInvariant();
            if (c.Kind != "game" && c.Kind != "save")
            {
                c.Problems.Add(new PayloadProblem { Line = LineOf(o.Property("kind")) , Message = "Say what kind of mod this is: \"kind\": \"game\" (it changes a running game) or \"kind\": \"save\" (it edits a save)." });
                return c;
            }
            c.Game = Str(o["game"]).Trim();
            c.TitleId = Str(o["titleId"]).Trim();
            Unknown(o, c);
            if (c.Kind == "game") CheckGame(o, text, c); else CheckSave(o, text, c);
            return c;
        }

        private static string Str(JToken t) => t is JValue v && v.Value != null ? Convert.ToString(v.Value, CultureInfo.InvariantCulture) : "";

        /// <summary>What the Mod Maker calls each field of a mod.</summary>
        public static string FieldLabel(string field)
        {
            switch (field)
            {
                case "id": return "Id";
                case "name": return "Name";
                case "game": return "Game";
                case "titleId": return "Title ID";
                case "requires": return "Needs";
                case "description": return "Description";
                default: return field;
            }
        }

        /// <summary>
        /// Every field of a mod is needed - buyers see them all, and the server knows the mod by its id. Says which are
        /// empty or not right, and whether the Title ID is the one the payload names. <paramref name="payload"/> is the
        /// payload's check, or null when there is no payload (a server-only mod built without one).
        /// </summary>
        public static List<FieldProblem> CheckFields(ModInfo info, PayloadCheck payload)
        {
            var list = new List<FieldProblem>();
            void Add(string field, string message) => list.Add(new FieldProblem { Field = field, Message = message });
            string id = (info.Id ?? "").Trim(), title = NormalTitleId(info.TitleId);
            string saysGame = payload != null && payload.Game.Length > 0 ? " The payload says \"" + payload.Game + "\"." : "";
            string saysTitle = payload != null && Hex8.IsMatch(payload.TitleId) ? " The payload says " + payload.TitleId.ToUpperInvariant() + "." : "";

            if (id.Length == 0) Add("id", "give the mod a short unique id, e.g. bo2-max-money. Your licence server knows the mod by it.");
            else if (!Regex.IsMatch(id, "^[A-Za-z0-9._-]{1,64}$")) Add("id", "use letters, digits, dot, dash and underscore only (at most 64), e.g. bo2-max-money.");
            if (string.IsNullOrWhiteSpace(info.Name)) Add("name", "give the mod a name. It is shown in the mod list.");
            if (string.IsNullOrWhiteSpace(info.Game)) Add("game", "say which game the mod is for. Buyers see it." + saysGame);
            if (title.Length == 0) Add("titleId", "give the game's Title ID: 8 hex digits, e.g. 415608C3." + saysTitle);
            else if (!Hex8.IsMatch(title)) Add("titleId", "it must be 8 hex digits (0-9 and A-F), e.g. 415608C3." + saysTitle);
            else if (payload != null && Hex8.IsMatch(payload.TitleId) && !title.Equals(payload.TitleId, StringComparison.OrdinalIgnoreCase))
                Add("titleId", title + " is not the Title ID in the payload (" + payload.TitleId.ToUpperInvariant() + "). Both must name the same game.");
            if (string.IsNullOrWhiteSpace(info.Requires)) Add("requires", "say what the mod needs, e.g. \"Latest title update\" or \"Nothing extra\". Buyers see it.");
            if (string.IsNullOrWhiteSpace(info.Description)) Add("description", "describe the mod in one line. Buyers see it.");
            return list;
        }

        /// <summary>A Title ID as 8 upper-case hex digits when it is one (0x in front is allowed), else trimmed as given.</summary>
        public static string NormalTitleId(string titleId)
        {
            string t = (titleId ?? "").Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            return Hex8.IsMatch(t) ? t.ToUpperInvariant() : t;
        }

        private static string Friendly(JsonReaderException ex)
        {
            string m = ex.Message;
            if (m.Contains("Unterminated string")) return "A piece of text is missing its closing double quote \".";
            if (m.Contains("Unexpected end")) return "The text ends too soon: a closing } or ] is missing (or a closing \" quote).";
            if (m.Contains("After parsing a value an unexpected character was encountered"))
                return "Two items follow each other without a comma between them. Put a comma at the end of the item before this one.";
            if (m.Contains("Invalid property identifier character") || m.Contains("Invalid character after parsing property name"))
                return "Expected an entry's name in double quotes followed by a colon, like \"name\": ";
            if (m.Contains("already exists in the current JSON object")) return "The same entry name appears twice in one { } block. Each name may be used once per block.";
            if (m.Contains("Bad JSON escape sequence")) return "A backslash \\ inside text starts a special character. To write a backslash itself, write two: \\\\";
            if (m.Contains("Additional text encountered after finished reading")) return "There is text after the final }. Everything must be inside the outer { } (notes starting with // are fine).";
            if (m.Contains("Unexpected character encountered while parsing value") || m.Contains("Error parsing"))
                return "This is not a value the payload understands. Text goes in \"double quotes\", numbers are written plain (100 or 0.5), and true / false / null have no quotes.";
            int cut = m.IndexOf(" Path '", StringComparison.Ordinal);
            if (cut < 0) cut = m.IndexOf(", line ", StringComparison.Ordinal);
            return cut > 0 ? m.Substring(0, cut).TrimEnd('.', ' ') + "." : m;
        }

        // the entries the program understands at each level; anything else is pointed out (it is usually a typo)
        private static readonly string[] GameRoot = { "kind", "game", "titleId", "titleIds", "titleUpdate", "notes", "options" };
        private static readonly string[] OptionKeys = { "name", "does", "group", "toggle", "inputs", "steps", "off", "writes", "calls" };
        private static readonly string[] InputKeys = { "id", "label", "type", "min", "max", "default", "maxLength", "raw", "choices" };
        private static readonly string[] ChoiceKeys = { "label", "set" };
        private static readonly string[] StepKeys = { "op", "address", "hex", "type", "value", "parts", "file", "args", "module", "ordinal", "thread", "var", "length", "ms", "when", "on", "cases", "otherwise", "returns" };
        private static readonly string[] PartKeys = { "hex", "text", "encoding" };
        private static readonly string[] OldWriteKeys = { "address", "type", "value", "off" };
        private static readonly string[] OldCallKeys = { "module", "ordinal", "address", "args" };
        private static readonly string[] SaveRoot = { "kind", "game", "what", "innerFile", "titleId", "ddl", "actions" };
        private static readonly string[] ActionKeys = { "name", "does", "ops" };
        private static readonly string[] OpKeys = { "contains", "startsWith", "endsWith", "equals", "value" };

        private static void Unknown(JObject o, PayloadCheck c)
        {
            if (c.Kind == "game")
            {
                Known(o, GameRoot, "the top of a game mod", c);
                foreach (var opt in Objects(o["options"]))
                {
                    Known(opt, OptionKeys, "an option", c);
                    foreach (var i in Objects(opt["inputs"])) { Known(i, InputKeys, "an input", c); foreach (var ch in Objects(i["choices"])) Known(ch, ChoiceKeys, "a choice", c); }
                    foreach (var w in Objects(opt["writes"])) Known(w, OldWriteKeys, "a write", c);
                    foreach (var k in Objects(opt["calls"])) Known(k, OldCallKeys, "a call", c);
                    Steps(opt["steps"], c); Steps(opt["off"], c);
                }
            }
            else
            {
                Known(o, SaveRoot, "the top of a save mod", c);
                foreach (var a in Objects(o["actions"])) { Known(a, ActionKeys, "an action", c); foreach (var op in Objects(a["ops"])) Known(op, OpKeys, "an op", c); }
            }
        }

        private static void Steps(JToken list, PayloadCheck c)
        {
            foreach (var s in Objects(list))
            {
                Known(s, StepKeys, "a step", c);
                foreach (var p in Objects(s["parts"])) Known(p, PartKeys, "a part", c);
                if (s["cases"] is JArray cases) foreach (var cs in cases) Steps(cs, c);
            }
        }

        private static IEnumerable<JObject> Objects(JToken t) => t is JArray a ? a.OfType<JObject>() : Enumerable.Empty<JObject>();

        private static void Known(JObject o, string[] known, string where, PayloadCheck c)
        {
            foreach (var p in o.Properties())
            {
                if (known.Contains(p.Name)) continue;
                string near = known.FirstOrDefault(k => string.Equals(k, p.Name, StringComparison.OrdinalIgnoreCase))
                              ?? known.Where(k => Distance(k.ToLowerInvariant(), p.Name.ToLowerInvariant()) <= Math.Max(1, k.Length / 3)).OrderBy(k => Distance(k.ToLowerInvariant(), p.Name.ToLowerInvariant())).FirstOrDefault();
                c.Notes.Add(new PayloadProblem
                {
                    Line = LineOf(p),
                    Message = "\"" + p.Name + "\" is not something " + where + " uses, so it is ignored" + (near != null ? " - did you mean \"" + near + "\"? (names are case-sensitive)" : ".")
                });
            }
        }

        private static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        private static readonly Regex Hex8 = new Regex("^[0-9A-Fa-f]{8}$");
        private static readonly string[] Ops = { "write", "call", "sleep", "read", "expect", "switch" };

        private static void Problem(PayloadCheck c, JToken at, string message) => c.Problems.Add(new PayloadProblem { Line = LineOf(at), Message = message });

        private static void CheckGame(JObject o, string text, PayloadCheck c)
        {
            if (string.IsNullOrWhiteSpace((string)o["game"])) Problem(c, o.Property("game") ?? (JToken)o, "Give the game's name: \"game\": \"Call of Duty: Black Ops II\".");
            if (!Hex8.IsMatch((string)o["titleId"] ?? "")) Problem(c, o.Property("titleId") ?? (JToken)o, "\"titleId\" must be the game's Title ID: 8 hex digits in double quotes, like \"415608C3\".");
            var options = o["options"] as JArray;
            if (options == null || options.Count == 0) { Problem(c, o.Property("options") ?? (JToken)o, "A game mod needs \"options\": a [ ] list with at least one option in it."); return; }

            // the shape of each option and step, with line numbers
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i] as JObject;
                if (opt == null) { Problem(c, options[i], "Option " + (i + 1) + " is not a { } block."); continue; }
                string name = (string)opt["name"];
                string label = "Option " + (i + 1) + (string.IsNullOrWhiteSpace(name) ? "" : " (\"" + name + "\")");
                if (string.IsNullOrWhiteSpace(name)) Problem(c, opt, label + " needs a \"name\".");
                bool hasSteps = opt["steps"] is JArray st && st.Count > 0, hasOld = opt["writes"] is JArray || opt["calls"] is JArray;
                if (!hasSteps && !hasOld && !(opt["off"] is JArray)) Problem(c, opt, label + " has nothing to do: give it \"steps\": [ ... ].");
                if (string.IsNullOrWhiteSpace((string)opt["does"])) c.Notes.Add(new PayloadProblem { Line = LineOf(opt), Message = label + " has no \"does\": one line saying what it does helps buyers." });
                var ids = new HashSet<string>();
                foreach (var input in Objects(opt["inputs"]))
                {
                    string id = (string)input["id"];
                    if (string.IsNullOrWhiteSpace(id)) Problem(c, input, label + ": an input needs an \"id\" - the name the steps use, like {amount}.");
                    else if (!ids.Add(id)) Problem(c, input, label + ": two inputs are both called \"" + id + "\".");
                    string itype = ((string)input["type"] ?? "number").Trim().ToLowerInvariant();
                    if (!new[] { "number", "text", "bool", "choice", "file" }.Contains(itype))
                        Problem(c, input.Property("type"), label + ": \"" + (string)input["type"] + "\" is not an input type. Use number, text, bool, choice or file.");
                    foreach (var key in new[] { "min", "max", "maxLength" })
                        if (input[key] != null && input[key].Type != JTokenType.Integer && input[key].Type != JTokenType.Float)
                            Problem(c, input.Property(key), label + ": \"" + key + "\" is a number - write it plain, without quotes, like 0.");
                    if (itype == "number" && input["default"] != null && input["default"].Type != JTokenType.Integer && input["default"].Type != JTokenType.Float)
                        Problem(c, input.Property("default"), label + ": the \"default\" of a number box is a number - write it plain, without quotes, like 1000.");
                    if (itype == "choice" && !(input["choices"] is JArray chs && chs.Count > 0))
                        Problem(c, input, label + ": a choice input needs \"choices\": a [ ] list of { \"label\": ..., \"set\": { ... } } entries.");
                }
                foreach (var key in new[] { "steps", "off" }) ShapeOfSteps(opt[key], label + (key == "off" ? ", turning off" : ""), c);
            }
            if (!c.Ok) return;

            // the program's own reading of the payload
            GameModKind kind;
            try { kind = GameModAdapter.Build(Encoding.UTF8.GetBytes(text)); }
            catch (JsonSerializationException ex) { c.Problems.Add(new PayloadProblem { Line = ex.LineNumber, Column = ex.LinePosition, Message = ex.Message.StartsWith("Could not convert") || ex.Message.StartsWith("Error converting") ? "This entry has the wrong kind of value: numbers are written plain, text goes in \"double quotes\"." : ex.Message }); return; }
            catch (Exception ex) { c.Problems.Add(new PayloadProblem { Message = ex.Message }); return; }

            // every step worked out with sample values: names that do not exist, sums that do not work, bad hex...
            // (the steps are taken from this check's own reading of the text, which knows each step's line)
            for (int i = 0; i < kind.Options.Count && i < options.Count; i++)
            {
                var opt = kind.Options[i];
                var mine = options[i] as JObject;
                string label = "Option \"" + opt.Name + "\"";
                var seen = new HashSet<string>();
                foreach (var sample in Samples(opt))
                {
                    Dictionary<string, object> vars;
                    try { vars = GameModRunner.Variables(opt, sample); }
                    catch (ArgumentException ex) { c.Problems.Add(new PayloadProblem { Line = LineOf(options[i]), Message = label + ": the default of an input does not fit it - " + ex.Message }); break; }
                    foreach (var side in new[] { "steps", "off" })
                        foreach (var p in ModScript.DryRun(Objects(mine?[side]), new Dictionary<string, object>(vars)))
                        {
                            string msg = label + (side == "off" ? ", turning off" : "") + ", " + p.Where + ": " + Hint(p.Message);
                            if (seen.Add(msg)) c.Problems.Add(new PayloadProblem { Line = p.Line, Message = msg });
                        }
                }
            }

            int tu = (int?)o["titleUpdate"] ?? 0;
            if (tu <= 0) c.Notes.Add(new PayloadProblem { Line = LineOf(o), Message = "Say which title update the addresses are for (\"titleUpdate\": 18), so buyers on another update are warned first." });
            foreach (var dup in kind.Options.GroupBy(x => (x.Group ?? "") + "|" + x.Name).Where(g => g.Count() > 1))
                c.Notes.Add(new PayloadProblem { Message = "Two options in the same group are both called \"" + dup.First().Name + "\"; buyers will not be able to tell them apart." });
            int groups = kind.Options.Select(x => x.Group ?? "").Distinct().Count(), inputs = kind.Options.Sum(x => x.Inputs.Count), switches = kind.Options.Count(x => x.Toggle);
            c.Summary = "Syntax OK - the payload is valid: " + kind.Options.Count + " option(s) in " + groups + " group(s)" +
                        (switches > 0 ? ", " + switches + " with an off" : "") + (inputs > 0 ? ", " + inputs + " value box(es)" : "") +
                        (tu > 0 ? ", made for title update " + tu : "") + ".";
        }

        private static void ShapeOfSteps(JToken list, string label, PayloadCheck c)
        {
            if (list == null) return;
            if (!(list is JArray steps)) { Problem(c, list, label + ": steps must be a [ ] list of { } blocks."); return; }
            int n = 0;
            foreach (var t in steps)
            {
                n++;
                string at = label + ", step " + n;
                if (!(t is JObject s)) { Problem(c, t, at + " is not a { } block."); continue; }
                string op = (string)s["op"];
                if (string.IsNullOrWhiteSpace(op)) { Problem(c, s, at + " needs \"op\": what it does - write, read, call, expect, sleep or switch."); continue; }
                if (!Ops.Contains(op)) { Problem(c, s.Property("op"), at + ": \"" + op + "\" is not a step the program knows. Use write, read, call, expect, sleep or switch."); continue; }
                bool needsAddress = op == "write" || op == "read" || op == "expect" || (op == "call" && s["module"] == null);
                if (needsAddress && string.IsNullOrWhiteSpace((string)s["address"])) Problem(c, s, at + " (" + op + ") needs an \"address\", like \"0x82000000\".");
                if (op == "write" && s["hex"] == null && s["value"] == null && s["parts"] == null && s["file"] == null)
                    Problem(c, s, at + " (write) needs what to write: \"hex\": \"60000000\", or \"type\" with \"value\".");
                if (op == "write" && s["value"] != null && string.IsNullOrWhiteSpace((string)s["type"]))
                    Problem(c, s, at + " (write) has a \"value\" but no \"type\" - say how it is stored, like \"type\": \"u32\".");
                string vtype = (string)s["type"];
                if ((op == "write" && s["value"] != null || op == "read") && !string.IsNullOrWhiteSpace(vtype) && !ModScript.KnownType(vtype) && !(op == "read" && (vtype == "ascii" || vtype == "bytes")))
                    Problem(c, s.Property("type"), at + " (" + op + "): \"" + vtype + "\" is not a value type. Use u8, u16, u32, u64, i8, i16, i32, i64, f32 or f64 (add le for little-endian)" + (op == "read" ? ", or ascii / bytes for text." : "."));
                if (op == "read" && string.IsNullOrWhiteSpace((string)s["var"])) Problem(c, s, at + " (read) needs \"var\": the name to keep the value under, like \"var\": \"now\".");
                if (op == "expect" && s["hex"] == null) Problem(c, s, at + " (expect) needs \"hex\": the bytes that must be there.");
                if (op == "sleep" && s["ms"] != null && s["ms"].Type != JTokenType.Integer) Problem(c, s.Property("ms"), at + " (sleep): \"ms\" is a plain number of milliseconds, like 100.");
                if (op == "call" && s["args"] != null && !(s["args"] is JArray)) Problem(c, s.Property("args"), at + " (call): \"args\" must be a [ ] list.");
                if (op == "switch")
                {
                    if (string.IsNullOrWhiteSpace((string)s["on"])) Problem(c, s, at + " (switch) needs \"on\": the input it picks a case by.");
                    if (!(s["cases"] is JArray cases) || cases.Count == 0) Problem(c, s, at + " (switch) needs \"cases\": a [ ] list of step lists.");
                    else { int k = 0; foreach (var cs in cases) ShapeOfSteps(cs, at + ", case " + (++k), c); }
                }
            }
        }

        private static string Hint(string message)
        {
            var m = Regex.Match(message, "^\"([^\"]+)\" has no value\\.$");
            if (m.Success) return "{" + m.Groups[1].Value + "} is used but nothing has that name. Check the spelling - it must match an input's \"id\" or a read step's \"var\" exactly.";
            if (message.StartsWith("Unknown value type")) return message + " Use u8, u16, u32, u64, i8, i16, i32, i64, f32 or f64 (add le for little-endian).";
            if (message.Contains("Hex bytes need an even number of digits")) return "hex needs two digits for every byte, like \"60000000\" (an even number of digits).";
            if (message.Contains("Could not find any recognizable digits") || message.Contains("Additional non-parsable characters")) return "hex may only hold 0-9 and A-F, two digits per byte.";
            return message;
        }

        /// <summary>Sample values for a dry run: each input's default (or its lowest value), and each entry of every drop-down.</summary>
        private static IEnumerable<Dictionary<string, string>> Samples(GameModOption opt)
        {
            var basic = new Dictionary<string, string>();
            foreach (var i in opt.Inputs)
            {
                string v = i.Default ?? "";
                if (i.Type == "number" && !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) v = i.Min.ToString(CultureInfo.InvariantCulture);
                if (i.Type == "choice" && !int.TryParse(v, out _)) v = "0";
                basic[i.Id] = v;
            }
            yield return basic;
            int runs = 0;
            foreach (var i in opt.Inputs.Where(x => x.Type == "choice"))
                for (int k = 0; k < i.Choices.Count && runs < 200; k++, runs++)
                {
                    var one = new Dictionary<string, string>(basic) { [i.Id] = k.ToString(CultureInfo.InvariantCulture) };
                    yield return one;
                }
            foreach (var i in opt.Inputs.Where(x => x.Type == "bool"))
                yield return new Dictionary<string, string>(basic) { [i.Id] = basic[i.Id] == "1" ? "0" : "1" };
        }

        private static void CheckSave(JObject o, string text, PayloadCheck c)
        {
            foreach (var need in new[] { "game", "what", "innerFile" })
                if (string.IsNullOrWhiteSpace((string)o[need])) Problem(c, o.Property(need) ?? (JToken)o, "A save mod needs \"" + need + "\"" +
                    (need == "innerFile" ? ": the name of the file inside the save package that holds the values, like \"savegame.bin\"." : need == "what" ? ": which save of the game this is, like \"Campaign career\"." : ": the game's name.") );
            if (!Hex8.IsMatch((string)o["titleId"] ?? "")) Problem(c, o.Property("titleId") ?? (JToken)o, "\"titleId\" must be the game's Title ID: 8 hex digits in double quotes, like \"41560817\".");
            if (string.IsNullOrWhiteSpace((string)o["ddl"])) { Problem(c, o.Property("ddl") ?? (JToken)o, "A save mod needs \"ddl\": where each value sits in the save (see the notes in the save template)."); return; }
            DdlDef def;
            try { def = DdlDef.Parse((string)o["ddl"]); }
            catch (Exception ex) when (ex is FormatException || ex is IndexOutOfRangeException || ex is OverflowException || ex is ArgumentException)
            {
                Problem(c, o.Property("ddl"), "The \"ddl\" has a mistake: " + (ex is FormatException && ex.Message.StartsWith("Bad") ? ex.Message : "a number in it is not written correctly.") +
                        " Each line is a def, struct or enum line, or a value line that starts with ONE space and has 7 parts.");
                return;
            }
            List<DdlField> fields;
            try { fields = def.Flatten(); }
            catch (FormatException ex) { Problem(c, o.Property("ddl"), "The \"ddl\" has a mistake: " + ex.Message); return; }

            int actions = 0;
            foreach (var a in Objects(o["actions"]))
            {
                actions++;
                string name = (string)a["name"];
                string label = "Action " + actions + (string.IsNullOrWhiteSpace(name) ? "" : " (\"" + name + "\")");
                if (string.IsNullOrWhiteSpace(name)) Problem(c, a, label + " needs a \"name\".");
                var ops = a["ops"] as JArray;
                if (ops == null || ops.Count == 0) { Problem(c, a, label + " needs \"ops\": a [ ] list saying which values to set."); continue; }
                foreach (var op in ops.OfType<JObject>())
                {
                    string[] by = { "contains", "startsWith", "endsWith", "equals" };
                    if (!by.Any(b => op[b] != null)) { Problem(c, op, label + ": an op names no values - add \"contains\", \"startsWith\", \"endsWith\" or \"equals\"."); continue; }
                    if (op["value"] == null || op["value"].Type != JTokenType.Integer || (long)op["value"] < 0) { Problem(c, op, label + ": an op needs \"value\": a whole number, 0 or more."); continue; }
                    int hits = fields.Count(f => !f.IsText && Matches(f.Path, op));
                    if (hits == 0) c.Notes.Add(new PayloadProblem { Line = LineOf(op), Message = label + ": no value in the save matches this op - check the spelling of the names." });
                }
            }
            if (!c.Ok) return;
            try { SaveModAdapter.Build(Encoding.UTF8.GetBytes(text)); }
            catch (Exception ex) { c.Problems.Add(new PayloadProblem { Message = ex.Message }); return; }
            c.Summary = "Syntax OK - the payload is valid: the save has " + fields.Count + " value(s) (stats version " + def.Version + "), " + actions + " action(s).";
        }

        private static bool Matches(string path, JObject op)
        {
            string eq = (string)op["equals"], co = (string)op["contains"], sw = (string)op["startsWith"], ew = (string)op["endsWith"];
            if (eq != null) return path.Equals(eq, StringComparison.OrdinalIgnoreCase);
            if (co != null && path.IndexOf(co, StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (sw != null && !path.StartsWith(sw, StringComparison.OrdinalIgnoreCase)) return false;
            if (ew != null && !path.EndsWith(ew, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // ------------------------------------------------------------------ building

        public static ModBuildResult Build(ModInfo info, string payloadJson, ModProtection protection)
        {
            var r = new ModBuildResult();
            // only the X360GameHack team makes mod packages: every pack is signed with the owner's key, which exists only on
            // the owner's PC, and the program loads nothing else (see ModSignature)
            var owner = OwnerKey.Load();
            if (owner == null)
            {
                r.Problems.Add("Making mod packages is reserved for the X360GameHack team: this PC does not hold the team's signing key.");
                return r;
            }
            // a server-only mod may be built without its payload (the server is given that separately)
            bool hasPayload = !(protection == ModProtection.ServerOnly && string.IsNullOrWhiteSpace(payloadJson));
            PayloadCheck check = hasPayload ? Check(payloadJson) : null;
            r.Problems.AddRange(CheckFields(info, check).Select(p => p.ToString()));     // every field is needed
            if (check != null) r.Problems.AddRange(check.Problems.Select(p => "Payload - " + p));
            if (r.Problems.Count > 0) return r;

            info.Id = info.Id.Trim(); info.Name = info.Name.Trim(); info.Game = info.Game.Trim();
            info.TitleId = NormalTitleId(info.TitleId); info.Requires = info.Requires.Trim(); info.Description = info.Description.Trim();
            info.ServerOnly = protection == ModProtection.ServerOnly;
            info.Open = protection == ModProtection.Open;
            if (check != null) info.Kind = check.Kind;
            string clean = hasPayload ? Clean(payloadJson) : null;            // notes and spacing removed
            byte[] payload = protection == ModProtection.ServerOnly ? null : Encoding.UTF8.GetBytes(clean);
            if (protection == ModProtection.ServerOnly) r.ServerPayload = clean;

            if (protection == ModProtection.Protected)
            {
                byte[] kAes = new byte[CryptoBox.KeyBytes], kMac = new byte[CryptoBox.KeyBytes];
                using (var rng = RandomNumberGenerator.Create()) { rng.GetBytes(kAes); rng.GetBytes(kMac); }
                r.Pack = ModPack.Build(info, payload, kAes, kMac);
                r.KeyCard = KeyCard(info, kAes, kMac);
                CryptoBox.Wipe(kAes); CryptoBox.Wipe(kMac);   // the program keeps no copy of the key
            }
            else
            {
                r.Pack = ModPack.Build(info, payload, null, null);
            }
            r.Pack = ModSignature.Sign(r.Pack, owner);
            return r;
        }

        /// <summary>
        /// Writes a built mod to disk, with its key card beside it for a protected mod, or the payload for the
        /// server beside it for a server-only mod. Returns the path of that extra file, or null.
        /// </summary>
        public static string WriteTo(ModBuildResult result, string outPath)
        {
            System.IO.File.WriteAllBytes(outPath, result.Pack);
            if (result.KeyCard != null)
            {
                string cardPath = outPath + ".keycard.txt";
                System.IO.File.WriteAllText(cardPath, result.KeyCard);
                return cardPath;
            }
            if (result.ServerPayload != null)
            {
                string payloadPath = outPath + ".server-payload.json";
                System.IO.File.WriteAllText(payloadPath, result.ServerPayload, new UTF8Encoding(false));
                return payloadPath;
            }
            return null;
        }

        private static string KeyCard(ModInfo info, byte[] kAes, byte[] kMac)
        {
            var reg = new { modId = info.Id, kAes = Convert.ToBase64String(kAes), kMac = Convert.ToBase64String(kMac) };
            var sb = new StringBuilder();
            sb.AppendLine("X360GameHack mod key card - KEEP THIS SECRET").AppendLine();
            sb.AppendLine("Mod: " + info.Name + "  (id: " + info.Id + ")").AppendLine();
            sb.AppendLine("Put this record on your licence server. When a buyer activates with a valid token for this");
            sb.AppendLine("mod id, and the token is not already bound to another machine, the server returns kAes/kMac.");
            sb.AppendLine("The .x360mod file does NOT contain this key. This program kept no copy of it.").AppendLine();
            sb.AppendLine(JsonConvert.SerializeObject(reg, Formatting.Indented));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ templates (with notes for beginners)

        private static string Lines(string s) => s.Replace("\r\n", "\n").Replace("\n", "\r\n");

        public static string SaveTemplate() => Lines(@"// ===================== SAVE MOD =====================
// A save mod shows a game's save as named values on the Saves page and adds one-click actions to it.
// It is for saves that keep their values as a ""stats definition"", the way Treyarch's Call of Duty games do:
// a 48-byte header, then every value packed one after another, bit by bit.
//
// Lines that start with // are notes. They are ignored and never go into the mod file.
// The check under this box updates as you type. Check syntax also selects the line with the first problem.
//
// The rules: text goes in ""double quotes"". Numbers are written plain (100).
// A comma goes between items - but not after the last item in a { } block or a [ ] list.
{
  ""kind"": ""save"",                    // always ""save"" for a save mod
  ""game"": ""Game name"",               // shown on the Saves page
  ""what"": ""Which save"",              // which of the game's saves this is, like ""Campaign career""
  ""innerFile"": ""savegame.bin"",       // the file inside the save package that holds the values (Saves > Contents lists them)
  ""titleId"": ""41560817"",             // the game's Title ID: 8 hex digits

  // ---- Where each value sits: the stats definition ----
  // One entry per line, with \n between the lines. Bit positions count from the end of the 48-byte header.
  //   def version=<v> bits=<b>
  //       v = the stats version written in the save's header (bytes 4 to 7); the Saves page says when it does not match
  //       b = how many bits all the values take together
  //   struct <number> <name> bits=<bits> members=<count>
  //       a block of values; block 0 is the whole save. The values of a block follow on the next lines.
  //    <name> <bits> <offset> <type> <block> <count> <names>
  //       one value. The line starts with ONE space and has 7 parts:
  //       <bits>   how many bits it takes (for a list: all entries together)
  //       <offset> where it starts, in bits, inside its block
  //       <type>   0 byte, 1 short, 2 unsigned int, 3 int, 4 64-bit, 5 float, 6 fixed, 7 text, 8 another block, 9 enum
  //       <block>  for type 8: the number of the block it holds; otherwise 0
  //       <count>  1 for a single value; more for a list of them
  //       <names>  the number of the enum that names the entries of a list; otherwise 0
  //   enum <number> <name> : first,second,third
  //       names for the entries of a list, for example level names
  ""ddl"": ""def version=1 bits=64\nstruct 0 root bits=64 members=1\n money 64 0 2 0 1 0"",

  // ---- One-click actions shown on the Saves page ----
  ""actions"": [
    {
      ""name"": ""Max money"",
      ""does"": ""Sets every money value to 999999."",
      // Each op picks values by their name and sets them to ""value"" (never higher than a value can hold):
      //   ""contains"": ""money""       names with money anywhere in them
      //   ""startsWith"": ""unlocks[""  names that begin with that
      //   ""endsWith"": "".kills""      names that end with that
      //   ""equals"": ""money""         exactly that one name
      // Names inside a block are written block.value, like playercareerstats.kills
      ""ops"": [ { ""contains"": ""money"", ""value"": 999999 } ]
    }
  ]
}
");

        public static string GameTemplate() => Lines(@"// ===================== GAME MOD =====================
// A game mod changes the game that is running on the console. Each option is a button the buyer presses.
//
// Lines that start with // are notes. They are ignored and never go into the mod file.
// The check under this box updates as you type. Check syntax also selects the line with the first problem.
//
// The rules: text goes in ""double quotes"". Numbers are written plain (100, 0.5), or in hex with 0x in front (0x82000000).
// true and false have no quotes. { } holds named entries (""name"": value), [ ] holds a list of items.
// A comma goes between items - but not after the last item in a { } block or a [ ] list.
{
  ""kind"": ""game"",                    // always ""game"" for a mod that changes a running game
  ""game"": ""Game name"",               // the name buyers see in the list of games
  ""titleId"": ""415608C3"",             // the game's Title ID: 8 hex digits. The mod only runs while this game is running.
  ""titleUpdate"": 18,                   // the title update the addresses are for. Buyers on another update are warned first.
  ""notes"": ""Made for title update 18."",   // shown under the game's name

  ""options"": [

    // ---- 1. A switch: ""steps"" turn it on, ""off"" puts the original bytes back ----
    {
      ""name"": ""Example switch"",
      ""group"": ""Player"",               // options with the same group are listed together
      ""does"": ""Writes 4 bytes while on, and puts the original 4 bytes back when turned off."",
      ""steps"": [ { ""op"": ""write"", ""address"": ""0x82000000"", ""hex"": ""60000000"" } ],
      ""off"":   [ { ""op"": ""write"", ""address"": ""0x82000000"", ""hex"": ""4BFFFFE5"" } ]
    },

    // ---- 2. A number the buyer types in ----
    {
      ""name"": ""Example value"",
      ""group"": ""Stats"",
      ""does"": ""Writes the number you type as a 32-bit value."",
      ""inputs"": [
        // ""id"" is the name the steps use, written as {amount}. The number is checked against min and max before anything is sent.
        { ""id"": ""amount"", ""label"": ""Amount"", ""type"": ""number"", ""min"": 0, ""max"": 999999, ""default"": 1000 }
      ],
      ""steps"": [
        // ""type"" says how the number is stored: u8 u16 u32 u64 = whole numbers from 0 up, i8 i16 i32 i64 = can be negative,
        // f32 f64 = with decimals. Numbers are stored big-endian, as the Xbox 360 does; add le (u32le) for the other order.
        { ""op"": ""write"", ""address"": ""0x83000000"", ""type"": ""u32"", ""value"": ""{amount}"" }
      ]
    },

    // ---- 3. A game command: needs the JRPC2 or XRPC plugin on the console ----
    {
      ""name"": ""Example command"",
      ""group"": ""Lobby"",
      ""does"": ""Runs a game command with the number you type."",
      ""inputs"": [ { ""id"": ""speed"", ""label"": ""Speed"", ""type"": ""number"", ""min"": 0, ""max"": 1000, ""default"": 190 } ],
      ""steps"": [
        // ""args"" are what the game function is given: plain numbers, or { ""s"": ""text"" } for text.
        // {speed} inside the text is replaced by the number the buyer typed.
        { ""op"": ""call"", ""address"": ""0x82400000"", ""args"": [ 0, { ""s"": ""g_speed {speed}"" } ] }
      ]
    }
  ]
}

// ---- More steps you can use ----
// { ""op"": ""read"",   ""address"": ""0x83000000"", ""type"": ""u32"", ""var"": ""now"" }        reads a value; use it later as {now}
// { ""op"": ""write"",  ""address"": ""0x83000000"", ""type"": ""u32"", ""value"": ""{now}+500"" }   sums work: + - * / % and ( )
// { ""op"": ""expect"", ""address"": ""0x82000000"", ""hex"": ""4BFFFFE5"" }                   stops unless those bytes are there
// { ""op"": ""sleep"",  ""ms"": 100 }                                                    waits (5000 at most)
// Any step can have ""when"": ""{tell}==1"" so it only runs when that is true.
//
// ---- More kinds of inputs ----
// { ""id"": ""name"", ""label"": ""Name"", ""type"": ""text"", ""maxLength"": 24 }       text: plain characters, no double quote
// { ""id"": ""tell"", ""label"": ""Show a message"", ""type"": ""bool"" }                a tick box: {tell} is 1 or 0
// { ""id"": ""who"", ""label"": ""Player"", ""type"": ""choice"", ""choices"": [            a drop-down: {who} is 0, 1, ...
//     { ""label"": ""Everyone"", ""set"": { ""slot"": 0 } },                               and ""set"" gives other names values: {slot}
//     { ""label"": ""Player 1"", ""set"": { ""slot"": 1 } } ] }
");
    }
}
