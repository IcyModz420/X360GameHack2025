using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace X360GameHack.Core.Services
{
    public enum XeniaValueKind { Bool, Int, Float, Text }

    /// <summary>One setting in Xenia's settings file (xenia-canary.config.toml), as Xenia itself wrote it.</summary>
    public sealed class XeniaSetting
    {
        public string Section = "", Key = "";
        /// <summary>The value without quotes or escapes: any, true, 8, 0.2.</summary>
        public string Value = "";
        public XeniaValueKind Kind;
        /// <summary>Xenia's own words about it, one line each.</summary>
        public readonly List<string> Description = new List<string>();
        /// <summary>The values Xenia lists for it ("Use: [any, d3d12, vulkan, null]"), or none.</summary>
        public readonly List<string> Choices = new List<string>();
        public string Id => Section + "." + Key;
        public string DescriptionText => string.Join("\n", Description);
    }

    /// <summary>
    /// Reads and changes Xenia's settings file - the TOML file Xenia Canary writes next to its storage (see
    /// <see cref="XeniaHost.ConfigFile"/>), every setting with Xenia's own description. A change rewrites only the
    /// value on that setting's line; every other byte of the file stays as it was. Before the first change the file
    /// is copied once (<see cref="OriginalOf"/>), so the settings from before can always be put back.
    /// </summary>
    public static class XeniaConfig
    {
        /// <summary>
        /// Settings Xenia keeps as decimal numbers although it writes whole values without a decimal point ("0", "1") -
        /// they must be written as decimals or Xenia reads them as something else. (The others with a point are seen as such.)
        /// </summary>
        public static readonly HashSet<string> Decimals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "postprocess_ffx_cas_additional_sharpness", "postprocess_ffx_fsr_sharpness_reduction", "left_stick_deadzone_percentage",
            "right_stick_deadzone_percentage", "time_scalar", "kernel_display_gamma_power"
        };

        private static readonly Regex SectionLine = new Regex(@"^\s*\[([^\]]+)\]\s*$", RegexOptions.Compiled);
        private static readonly Regex KeyStart = new Regex(@"^(\s*)([A-Za-z0-9_\-\.]+)(\s*=\s*)", RegexOptions.Compiled);
        private static readonly Regex CommentLine = new Regex(@"^\s*#\s?(.*)$", RegexOptions.Compiled);
        private static readonly Regex Use = new Regex(@"Use:\s*\[([^\]]*)\]", RegexOptions.Compiled);
        private static readonly Regex Integer = new Regex(@"^[-+]?\d+$", RegexOptions.Compiled);
        private static readonly Regex Number = new Regex(@"^[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?$", RegexOptions.Compiled);

        /// <summary>The copy of the file as it was before X360GameHack first changed it.</summary>
        public static string OriginalOf(string file) => file + ".x360gamehack-original";

        /// <summary>Every setting in the file, in its order; empty when there is no file yet (Xenia writes it the first time it starts).</summary>
        public static List<XeniaSetting> Read(string file) => File.Exists(file) ? Parse(File.ReadAllText(file, Encoding.UTF8)) : new List<XeniaSetting>();

        public static List<XeniaSetting> Parse(string text)
        {
            var list = new List<XeniaSetting>();
            string section = "";
            XeniaSetting last = null;
            foreach (string raw in SplitLines(text))
            {
                string line = raw.TrimEnd('\r');
                var sm = SectionLine.Match(line);
                if (sm.Success) { section = sm.Groups[1].Value.Trim(); last = null; continue; }
                if (line.Trim().Length == 0) { last = null; continue; }
                var cm = CommentLine.Match(line);
                if (cm.Success) { last?.Description.Add(cm.Groups[1].Value.TrimEnd()); continue; }
                if (!TryValue(line, out string key, out int start, out int end, out string comment)) { last = null; continue; }
                string literal = line.Substring(start, end - start);
                var s = new XeniaSetting { Section = section, Key = key, Value = Unquote(literal), Kind = KindOf(key, literal) };
                if (comment != null) s.Description.Add(comment);
                list.Add(s);
                last = s;
            }
            foreach (var s in list)
            {
                var m = Use.Match(s.DescriptionText);
                if (m.Success) s.Choices.AddRange(m.Groups[1].Value.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0));
            }
            return list;
        }

        private static IEnumerable<string> SplitLines(string text) => (text ?? "").Split('\n');

        private static XeniaValueKind KindOf(string key, string literal)
        {
            string t = literal.Trim();
            if (t == "true" || t == "false") return XeniaValueKind.Bool;
            if (t.StartsWith("\"") || t.StartsWith("'")) return XeniaValueKind.Text;
            if (Integer.IsMatch(t)) return Decimals.Contains(key) ? XeniaValueKind.Float : XeniaValueKind.Int;
            if (Number.IsMatch(t)) return XeniaValueKind.Float;
            return XeniaValueKind.Text;
        }

        /// <summary>
        /// Finds a "key = value  # comment" line: the key, where the value's text starts and ends (a quoted value may hold
        /// a '#'), and the comment after it (null when none).
        /// </summary>
        private static bool TryValue(string line, out string key, out int start, out int end, out string comment)
        {
            key = null; start = end = 0; comment = null;
            var m = KeyStart.Match(line);
            if (!m.Success) return false;
            key = m.Groups[2].Value;
            start = m.Length;
            int i = start;
            if (i < line.Length && (line[i] == '"' || line[i] == '\''))
            {
                char q = line[i++];
                while (i < line.Length && line[i] != q) i += q == '"' && line[i] == '\\' ? 2 : 1;
                end = Math.Min(line.Length, i + 1);
            }
            else
            {
                while (i < line.Length && line[i] != '#' && !char.IsWhiteSpace(line[i])) i++;
                end = i;
            }
            int hash = line.IndexOf('#', end);
            if (hash >= 0 && line.Substring(end, hash - end).Trim().Length == 0) comment = line.Substring(hash + 1).TrimStart(' ').TrimEnd();
            return end > start;
        }

        private static string Unquote(string literal)
        {
            string t = literal.Trim();
            if (t.Length >= 2 && t[0] == '\'' && t[t.Length - 1] == '\'') return t.Substring(1, t.Length - 2);
            if (t.Length < 2 || t[0] != '"' || t[t.Length - 1] != '"') return t;
            var sb = new StringBuilder();
            for (int i = 1; i < t.Length - 1; i++)
            {
                char c = t[i];
                if (c != '\\' || i + 1 >= t.Length - 1) { sb.Append(c); continue; }
                char n = t[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'u' when i + 4 < t.Length - 1:
                        sb.Append((char)Convert.ToInt32(t.Substring(i + 1, 4), 16)); i += 4; break;
                    default: sb.Append('\\').Append(n); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// The text to write for a value of that kind - a quoted, escaped string; true / false; a whole number; a decimal
        /// (always with a point, so Xenia reads it as one). Throws FormatException for a value that is not of that kind.
        /// </summary>
        public static string Literal(XeniaValueKind kind, string value)
        {
            string v = (value ?? "").Trim();
            switch (kind)
            {
                case XeniaValueKind.Bool:
                    if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) return "true";
                    if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) return "false";
                    throw new FormatException("Use true or false.");
                case XeniaValueKind.Int:
                    if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(v.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long hx)) return hx.ToString(CultureInfo.InvariantCulture);
                    if (long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)) return n.ToString(CultureInfo.InvariantCulture);
                    throw new FormatException("Use a whole number.");
                case XeniaValueKind.Float:
                    if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d))
                        throw new FormatException("Use a number (with a point for decimals, like 0.5).");
                    string r = d.ToString("R", CultureInfo.InvariantCulture);
                    return r.IndexOfAny(new[] { '.', 'E', 'e' }) >= 0 ? r : r + ".0";
                default:
                    var sb = new StringBuilder("\"");
                    foreach (char c in value ?? "")
                    {
                        if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                        else if (c == '\n') sb.Append("\\n");
                        else if (c == '\r') sb.Append("\\r");
                        else if (c == '\t') sb.Append("\\t");
                        else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("X4"));
                        else sb.Append(c);
                    }
                    return sb.Append('"').ToString();
            }
        }

        /// <summary>One value to set: Literal is the exact text to write (see <see cref="Literal"/>).</summary>
        public struct Change
        {
            public string Section, Key, Literal;
            public Change(string section, string key, string literal) { Section = section; Key = key; Literal = literal; }
        }

        /// <summary>
        /// Writes the values into the file. Only the value on each setting's line changes (the padding is adjusted so
        /// Xenia's comments stay in their column); a setting the file does not have yet goes at the end of its section, a
        /// missing section at the end of the file, a missing file is created (Xenia adds the rest when it starts). Before the
        /// first change the file is copied once to <see cref="OriginalOf"/>. Returns how many values were different.
        /// </summary>
        public static int Write(string file, IEnumerable<Change> changes)
        {
            var want = (changes ?? Enumerable.Empty<Change>()).ToList();
            if (want.Count == 0) return 0;
            foreach (var c in want)
                if (string.IsNullOrWhiteSpace(c.Section) || string.IsNullOrWhiteSpace(c.Key) || c.Literal == null || c.Literal.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                    throw new ArgumentException("Not a setting: " + c.Section + "." + c.Key);
            bool exists = File.Exists(file);
            string text = exists ? File.ReadAllText(file, Encoding.UTF8) : "";
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Length == 0 ? new List<string>() : text.Replace("\r\n", "\n").Split('\n').ToList();
            bool endsWithNewline = lines.Count > 0 && lines[lines.Count - 1].Length == 0;
            if (endsWithNewline) lines.RemoveAt(lines.Count - 1);

            int changed = 0;
            foreach (var c in want)
            {
                int at = Find(lines, c.Section, c.Key, out int start, out int end);
                if (at >= 0)
                {
                    string line = lines[at], old = line.Substring(start, end - start);
                    if (old == c.Literal || SameNumber(old, c.Literal)) continue;     // 0.2 is what Xenia wrote as 0.20000000298023224
                    string rest = line.Substring(end);
                    // keep the comment in its column: take spaces away (or add them) as the value gets longer (or shorter)
                    int pad = 0; while (pad < rest.Length && rest[pad] == ' ') pad++;
                    if (pad < rest.Length && pad > 0) rest = new string(' ', Math.Max(1, pad - (c.Literal.Length - old.Length))) + rest.Substring(pad);
                    lines[at] = line.Substring(0, start) + c.Literal + rest;
                }
                else
                {
                    int sec = lines.FindIndex(l => SectionLine.Match(l).Success && SectionLine.Match(l).Groups[1].Value.Trim().Equals(c.Section, StringComparison.OrdinalIgnoreCase));
                    if (sec < 0)
                    {
                        if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                        lines.Add("[" + c.Section + "]");
                        lines.Add(c.Key + " = " + c.Literal);
                    }
                    else
                    {
                        int next = lines.FindIndex(sec + 1, l => SectionLine.IsMatch(l));
                        int insert = next < 0 ? lines.Count : next;
                        while (insert > sec + 1 && lines[insert - 1].Trim().Length == 0) insert--;   // before the blank lines that end the section
                        lines.Insert(insert, c.Key + " = " + c.Literal);
                    }
                }
                changed++;
            }
            if (changed == 0) return 0;
            if (exists && !File.Exists(OriginalOf(file))) File.Copy(file, OriginalOf(file));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            string output = string.Join(nl, lines) + (endsWithNewline || !exists ? nl : "");
            string tmp = file + ".x360gamehack-new";
            File.WriteAllText(tmp, output, new UTF8Encoding(false));
            if (exists) File.Replace(tmp, file, null); else File.Move(tmp, file);
            return changed;
        }

        /// <summary>Two number texts for the same value (Xenia writes a float's double, 0.20000000298023224 for 0.2): no change needed.</summary>
        private static bool SameNumber(string a, string b)
        {
            if (!Number.IsMatch(a.Trim()) || !Number.IsMatch(b.Trim())) return false;
            if (!double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) || !double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) return false;
            return Math.Abs(x - y) <= 1e-6 * Math.Max(1.0, Math.Abs(x));
        }

        /// <summary>The line of a setting in its section (-1 when absent) and where its value's text is on that line.</summary>
        private static int Find(List<string> lines, string section, string key, out int start, out int end)
        {
            start = end = 0;
            string current = "";
            for (int i = 0; i < lines.Count; i++)
            {
                var sm = SectionLine.Match(lines[i]);
                if (sm.Success) { current = sm.Groups[1].Value.Trim(); continue; }
                if (!current.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
                if (TryValue(lines[i], out string k, out start, out end, out _) && k.Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>Puts back the file as it was before X360GameHack first changed it. False when there is no such copy.</summary>
        public static bool RestoreOriginal(string file)
        {
            string original = OriginalOf(file);
            if (!File.Exists(original)) return false;
            File.Copy(original, file, true);
            File.Delete(original);
            return true;
        }
    }
}
