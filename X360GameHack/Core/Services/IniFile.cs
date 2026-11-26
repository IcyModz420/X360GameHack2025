using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace X360GameHack.Core.Services
{
    /// <summary>One "key = value" line of an ini file.</summary>
    public sealed class IniEntry
    {
        public string Section = "", Key = "", Value = "";
        /// <summary>The comment lines written directly above the key in the file (without the leading ';').</summary>
        public string Comment = "";
        internal int Line;
        public bool IsSwitch => Value.Equals("true", StringComparison.OrdinalIgnoreCase) || Value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>How a kind of ini file writes its settings.</summary>
    public enum IniFormat
    {
        /// <summary>DashLaunch style: [Section] lines and "key = value".</summary>
        KeyEquals,
        /// <summary>xbdm.ini: one XBDM command per line, run when XBDM starts - dbgname name="Jtag", dumpmode smart.</summary>
        XbdmCommands,
        /// <summary>JRPC.ini: quoted blocks - "Settings" { "KV Stealer Protection" "True" }.</summary>
        KeyValues
    }

    /// <summary>
    /// An ini file (DashLaunch launch.ini, xbdm.ini, JRPC.ini ...) that can be changed without disturbing it:
    /// a file that is loaded and written back unchanged is byte for byte the same, and changing one value
    /// rewrites that one line only - comments, blank lines, order, spacing and line endings all stay.
    /// </summary>
    public sealed class IniFile
    {
        private readonly List<string> _lines = new List<string>();
        private readonly List<string> _ends = new List<string>();     // the line ending each line had ("\r\n", "\n" or "" for the last)
        private readonly string _newLine;
        private readonly IniFormat _format;
        private readonly IniProfile _profile;

        public IniFile(string text) : this(text, null) { }

        /// <summary>Reads <paramref name="text"/> in the format of <paramref name="profile"/> (DashLaunch style when null).</summary>
        public IniFile(string text, IniProfile profile)
        {
            _profile = profile;
            _format = profile?.Format ?? IniFormat.KeyEquals;
            text = text ?? "";
            int at = 0;
            while (at < text.Length)
            {
                int nl = text.IndexOf('\n', at);
                if (nl < 0) { _lines.Add(text.Substring(at)); _ends.Add(""); at = text.Length; break; }
                bool cr = nl > at && text[nl - 1] == '\r';
                _lines.Add(text.Substring(at, nl - at - (cr ? 1 : 0)));
                _ends.Add(cr ? "\r\n" : "\n");
                at = nl + 1;
            }
            _newLine = _ends.Contains("\r\n") || !_ends.Contains("\n") ? "\r\n" : "\n";
        }

        public IniFormat Format => _format;

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _lines.Count; i++) sb.Append(_lines[i]).Append(_ends[i]);
            return sb.ToString();
        }

        private static bool IsComment(string line) { string t = line.TrimStart(); return t.StartsWith(";") || t.StartsWith("#") || t.StartsWith("//"); }

        private static bool IsSection(string line, out string name)
        {
            name = null;
            string t = line.Trim();
            if (!t.StartsWith("[")) return false;
            int end = t.IndexOf(']');
            if (end < 1) return false;
            name = t.Substring(1, end - 1).Trim();
            return true;
        }

        private static readonly System.Text.RegularExpressions.Regex QuotedArg =
            new System.Text.RegularExpressions.Regex("^([A-Za-z_][A-Za-z0-9_]*)=\"([^\"]*)\"$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        private static readonly System.Text.RegularExpressions.Regex Pair =
            new System.Text.RegularExpressions.Regex("^(\\s*\"([^\"]*)\"\\s+\")([^\"]*)(\".*)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        private static readonly System.Text.RegularExpressions.Regex Name =
            new System.Text.RegularExpressions.Regex("^\\s*\"([^\"]*)\"\\s*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private bool IsKey(string line, out string key, out string value)
        {
            key = value = null;
            if (line.Trim().Length == 0 || IsComment(line)) return false;
            if (_format == IniFormat.XbdmCommands)
            {
                string t = line.Trim();
                int sp = t.IndexOfAny(new[] { ' ', '\t' });
                key = sp < 0 ? t : t.Substring(0, sp);
                string rest = sp < 0 ? "" : t.Substring(sp + 1).Trim();
                var q = QuotedArg.Match(rest);
                value = q.Success ? q.Groups[2].Value : rest;
                return key.Length > 0;
            }
            if (_format == IniFormat.KeyValues)
            {
                var m = Pair.Match(line);
                if (!m.Success) return false;
                key = m.Groups[2].Value; value = m.Groups[3].Value;
                return key.Length > 0;
            }
            if (line.TrimStart().StartsWith("[")) return false;
            int eq = line.IndexOf('=');
            if (eq <= 0) return false;
            key = line.Substring(0, eq).Trim();
            value = line.Substring(eq + 1).Trim();
            return key.Length > 0;
        }

        /// <summary>Every setting in the file, in file order.</summary>
        public List<IniEntry> Entries()
        {
            var list = new List<IniEntry>();
            string section = "", pending = null;
            int depth = 0;
            for (int i = 0; i < _lines.Count; i++)
            {
                string name, key, value;
                if (_format == IniFormat.KeyValues)
                {
                    string t = _lines[i].Trim();
                    if (t == "{") { depth++; if (depth == 1) section = pending ?? ""; pending = null; continue; }
                    if (t == "}") { depth = Math.Max(0, depth - 1); if (depth == 0) section = ""; continue; }
                    var n = Name.Match(_lines[i]);
                    if (n.Success) { pending = n.Groups[1].Value; continue; }
                }
                else if (_format == IniFormat.KeyEquals && IsSection(_lines[i], out name)) { section = name; continue; }
                if (!IsKey(_lines[i], out key, out value)) continue;
                var comment = new List<string>();
                for (int j = i - 1; j >= 0 && IsComment(_lines[j]); j--) comment.Insert(0, _lines[j].TrimStart().TrimStart(';', '#', '/').Trim());
                list.Add(new IniEntry { Section = section, Key = key, Value = value, Comment = string.Join("\n", comment), Line = i });
            }
            return list;
        }

        public IEnumerable<string> Sections()
        {
            if (_format == IniFormat.KeyValues) return Entries().Select(e => e.Section).Where(s => s.Length > 0).Distinct().ToList();
            var names = new List<string>();
            string n;
            foreach (string l in _lines) if (_format == IniFormat.KeyEquals && IsSection(l, out n)) names.Add(n);
            return names;
        }

        public IniEntry Find(string section, string key)
            => Entries().FirstOrDefault(e => e.Section.Equals(section ?? "", StringComparison.OrdinalIgnoreCase) && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        /// <summary>Finds a key in any section (ini files of this kind do not repeat key names).</summary>
        public IniEntry Find(string key) => Entries().FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        public string Get(string section, string key, string fallback = "") => Find(section, key)?.Value ?? fallback;

        /// <summary>Null when a value can be written on one line, else why not.</summary>
        public static string ValueProblem(string value)
        {
            if (value == null) return "No value.";
            if (value.IndexOfAny(new[] { '\r', '\n' }) >= 0) return "A value stays on one line.";
            if (value.Any(c => c < 32 && c != '\t')) return "A value cannot contain control characters.";
            if (value.Length > 512) return "That value is too long.";
            return null;
        }

        /// <summary>What this file's format cannot hold in a value (quotes in xbdm.ini and JRPC.ini), else null.</summary>
        public string FormatProblem(string value)
            => _format != IniFormat.KeyEquals && (value ?? "").Contains("\"") ? "A value in " + (_profile?.FileName ?? "this file") + " cannot contain a quote (\")." : null;

        private string NameProblem(string name, string what)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Give the " + what + " a name.";
            if (_format == IniFormat.KeyValues)
                return name.Any(c => c < 32 || c == '"' || c == '{' || c == '}') || name.Trim() != name ? "A " + what + " name cannot contain quotes or braces." : null;
            if (name.Any(c => c < 33 || c > 126 || c == '=' || c == '[' || c == ']' || c == ';' || c == '#' || c == '"')) return "A " + what + " name is plain letters, digits and _ only.";
            return null;
        }

        /// <summary>
        /// Sets a value. An existing key keeps its line: only its value changes (an empty value stays, it is not
        /// removed). A new key is added after the last key of its section (in JRPC.ini at the end of its block; in
        /// xbdm.ini as a new last line); a new section is added at the end of the file.
        /// </summary>
        public void Set(string section, string key, string value)
        {
            value = (value ?? "").Trim();
            string problem = ValueProblem(value) ?? FormatProblem(value) ?? NameProblem(key, "setting") ?? (string.IsNullOrEmpty(section) ? null : NameProblem(section, "section"));
            if (problem != null) throw new ArgumentException(problem);
            var e = Find(section, key);
            if (_format == IniFormat.XbdmCommands) { SetCommand(e, key, value); return; }
            if (_format == IniFormat.KeyValues) { SetPair(e, section ?? "", key, value); return; }
            if (e != null)
            {
                string line = _lines[e.Line];
                int eq = line.IndexOf('=');
                bool spaced = eq + 1 < line.Length ? line[eq + 1] == ' ' : eq > 0 && line[eq - 1] == ' ';
                _lines[e.Line] = line.Substring(0, eq + 1) + (spaced ? " " : "") + value;
                return;
            }
            string style = Entries().Any(x => _lines[x.Line].Contains(" = ") || _lines[x.Line].TrimEnd().EndsWith(" =")) || Entries().Count == 0 ? " = " : "=";
            int insert = -1; string name; bool inSection = string.IsNullOrEmpty(section);
            for (int i = 0; i < _lines.Count; i++)
            {
                if (IsSection(_lines[i], out name))
                {
                    if (inSection && !string.IsNullOrEmpty(section)) break;
                    inSection = name.Equals(section ?? "", StringComparison.OrdinalIgnoreCase);
                    if (inSection) insert = i + 1;
                    else if (string.IsNullOrEmpty(section)) break;
                    continue;
                }
                string k, v;
                if (inSection && IsKey(_lines[i], out k, out v)) insert = i + 1;
            }
            if (insert < 0)
            {
                if (string.IsNullOrEmpty(section)) insert = 0;
                else
                {
                    EndLastLine();
                    if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length > 0) { _lines.Add(""); _ends.Add(_newLine); }
                    _lines.Add("[" + section + "]"); _ends.Add(_newLine);
                    insert = _lines.Count;
                }
            }
            if (insert == _lines.Count) EndLastLine();
            _lines.Insert(insert, key + style + value);
            _ends.Insert(insert, _newLine);
        }

        /// <summary>xbdm.ini: dbgname name="value" keeps its form; a bare argument (dumpmode smart) stays bare.</summary>
        private void SetCommand(IniEntry e, string key, string value)
        {
            if (e != null)
            {
                string line = _lines[e.Line];
                int start = line.Length - line.TrimStart().Length;
                string indent = line.Substring(0, start), t = line.Trim();
                int sp = t.IndexOfAny(new[] { ' ', '\t' });
                string rest = sp < 0 ? "" : t.Substring(sp + 1).Trim();
                var q = QuotedArg.Match(rest);
                string sep = sp < 0 ? " " : t.Substring(sp, t.Length - sp - t.Substring(sp).TrimStart().Length);
                _lines[e.Line] = indent + e.Key + sep + (q.Success ? q.Groups[1].Value + "=\"" + value + "\"" : value);
                return;
            }
            string arg = _profile?.Lookup(key)?.ArgName;
            EndLastLine();
            _lines.Add(key + " " + (arg != null ? arg + "=\"" + value + "\"" : value));
            _ends.Add(_newLine);
        }

        /// <summary>JRPC.ini: only the text between the value's quotes changes; a new key goes at the end of its block.</summary>
        private void SetPair(IniEntry e, string section, string key, string value)
        {
            if (e != null)
            {
                var m = Pair.Match(_lines[e.Line]);
                _lines[e.Line] = m.Groups[1].Value + value + m.Groups[4].Value;
                return;
            }
            // the closing brace of the section's block
            string current = null, pending = null; int depth = 0, close = -1; string indent = "\t", gap = "\t";
            for (int i = 0; i < _lines.Count; i++)
            {
                string t = _lines[i].Trim();
                if (t == "{") { depth++; if (depth == 1) current = pending; pending = null; continue; }
                if (t == "}") { depth = Math.Max(0, depth - 1); if (depth == 0 && current != null && current.Equals(section, StringComparison.OrdinalIgnoreCase)) { close = i; break; } if (depth == 0) current = null; continue; }
                var n = Name.Match(_lines[i]);
                if (n.Success) { pending = n.Groups[1].Value; continue; }
                var p = Pair.Match(_lines[i]);
                if (p.Success)
                {
                    indent = _lines[i].Substring(0, _lines[i].Length - _lines[i].TrimStart().Length);
                    string afterKey = _lines[i].TrimStart().Substring(p.Groups[2].Value.Length + 2);
                    gap = afterKey.Substring(0, afterKey.Length - afterKey.TrimStart().Length);
                }
            }
            string line = indent + "\"" + key + "\"" + (gap.Length > 0 ? gap : "\t") + "\"" + value + "\"";
            if (close >= 0) { _lines.Insert(close, line); _ends.Insert(close, _newLine); return; }
            EndLastLine();
            if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length > 0) { _lines.Add(""); _ends.Add(_newLine); }
            foreach (string l in new[] { "\"" + section + "\"", "{", line, "}" }) { _lines.Add(l); _ends.Add(_newLine); }
        }

        private void EndLastLine() { if (_ends.Count > 0 && _ends[_ends.Count - 1].Length == 0) _ends[_ends.Count - 1] = _newLine; }

        /// <summary>Removes a key's line (its comment lines stay).</summary>
        public bool Remove(string section, string key)
        {
            var e = Find(section, key);
            if (e == null) return false;
            _lines.RemoveAt(e.Line); _ends.RemoveAt(e.Line);
            return true;
        }
    }

    /// <summary>What kind of value a known setting takes.</summary>
    public enum IniKind { Switch, Number, Hex, ConsolePath, Text }

    public sealed class IniSetting
    {
        public string Section, Key, Does, Default;
        public IniKind Kind;
        /// <summary>xbdm.ini: the argument name the command takes its value in (dbgname name="..."), or null for a bare value.</summary>
        public string ArgName;
        /// <summary>The only values the setting takes, or null for any value of its kind.</summary>
        public string[] Allowed;
    }

    /// <summary>What this program knows about one kind of ini file.</summary>
    public sealed class IniProfile
    {
        public string Name, FileName, What;
        public IniFormat Format = IniFormat.KeyEquals;
        public List<IniSetting> Settings = new List<IniSetting>();
        /// <summary>A complete, commented file to start from, or null when the format is not known well enough to write one.</summary>
        public string Template;

        public IniSetting Lookup(string key) => Settings.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        /// <summary>Null when the value suits the setting (or the setting is not a known one), else what is wrong.</summary>
        public string Problem(string key, string value)
        {
            value = (value ?? "").Trim();
            string basic = IniFile.ValueProblem(value);
            if (basic != null) return basic;
            if (Format != IniFormat.KeyEquals && value.Contains("\"")) return "A value in " + FileName + " cannot contain a quote (\").";
            var s = Lookup(key);
            if (s == null || value.Length == 0) return null;      // an empty value means "not set" in these files
            if (s.Allowed != null && !s.Allowed.Any(a => a.Equals(value, StringComparison.OrdinalIgnoreCase)))
                return key + " is one of: " + string.Join(", ", s.Allowed) + ".";
            switch (s.Kind)
            {
                case IniKind.Switch:
                    return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase) ? null : key + " is true or false.";
                case IniKind.Number:
                    { uint n; return uint.TryParse(value, out n) ? null : key + " is a whole number."; }
                case IniKind.Hex:
                    { uint n; return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && uint.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out n) ? null : key + " is a hexadecimal number starting with 0x, for example 0x7fff."; }
                case IniKind.ConsolePath:
                    return IniProfiles.ConsolePathProblem(value);
                default: return null;
            }
        }
    }

    public static class IniProfiles
    {
        /// <summary>The device names DashLaunch accepts in launch.ini paths (from the header of its own launch.ini).</summary>
        public static readonly string[] Devices = { "Hdd", "Usb", "Mu", "UsbMu", "FlashMu", "IntMu", "MmcMu", "Dvd", "Sfc" };

        public static string ConsolePathProblem(string value)
        {
            int colon = value.IndexOf(':');
            if (colon < 1 || !Devices.Any(d => d.Equals(value.Substring(0, colon), StringComparison.OrdinalIgnoreCase)))
                return "A path starts with a device: " + string.Join(":, ", Devices) + ": - for example Hdd:\\xbdm.xex.";
            if (value.Length < colon + 3 || value[colon + 1] != '\\') return "After the device comes a backslash and the file, for example Hdd:\\xbdm.xex.";
            if (value.IndexOfAny(new[] { '"', '<', '>', '|', '?', '*' }) >= 0 || value.Contains("..") || value.Contains("\\\\") || value.Contains("/")) return "That path has characters a console path cannot have.";
            return null;
        }

        private static IniSetting S(string section, string key, IniKind kind, string def, string does) => new IniSetting { Section = section, Key = key, Kind = kind, Default = def, Does = does };

        /// <summary>DashLaunch 3.x launch.ini: every setting its own configuration file lists, described in plain words.</summary>
        public static readonly IniProfile Launch = BuildLaunch();

        private static IniProfile BuildLaunch()
        {
            var p = new IniProfile { Name = "launch.ini", FileName = "launch.ini", What = "DashLaunch: what starts when the console is switched on, the plugins it loads (xbdm.xex, JRPC2.xex ...) and its system options." };
            const string P = "Paths", G = "Plugins", X = "Externals", T = "Settings";
            foreach (string b in new[] { "BUT_A", "BUT_B", "BUT_X", "BUT_Y", "Start", "Back", "LBump", "RThumb", "LThumb" })
                p.Settings.Add(S(P, b, IniKind.ConsolePath, "", "Program to start when this button is held while the console starts. Empty = nothing."));
            p.Settings.Add(S(P, "Default", IniKind.ConsolePath, "", "Program to start when no button is held, for example Hdd:\\Aurora\\Aurora.xex. Empty = the normal dashboard."));
            p.Settings.Add(S(P, "Guide", IniKind.ConsolePath, "", "Used instead of Default when the console is switched on with a controller's guide button."));
            p.Settings.Add(S(P, "Power", IniKind.ConsolePath, "", "Used instead of Default when the console is switched on with its own power button."));
            p.Settings.Add(S(P, "Configapp", IniKind.ConsolePath, "", "Program to start when System Settings is chosen from the guide."));
            p.Settings.Add(S(P, "Fakeanim", IniKind.ConsolePath, "", "Program run before anything else at start-up (meant for a replacement boot animation)."));
            p.Settings.Add(S(P, "Dumpfile", IniKind.ConsolePath, "", "Text file that crash details are written to, for example Usb:\\crashlog.txt."));
            for (int i = 1; i <= 5; i++) p.Settings.Add(S(G, "plugin" + i, IniKind.ConsolePath, "", "Plugin loaded at start-up, for example Hdd:\\xbdm.xex. Up to five."));
            p.Settings.Add(S(X, "ftpserv", IniKind.Switch, "false", "DashLaunch's own program starts an FTP server while it is open."));
            p.Settings.Add(S(X, "ftpport", IniKind.Number, "21", "Port for that FTP server."));
            p.Settings.Add(S(X, "updserv", IniKind.Switch, "false", "DashLaunch's own program starts the xeBuild update server."));
            p.Settings.Add(S(X, "calaunch", IniKind.Switch, "false", "DashLaunch's own program opens in launch mode."));
            p.Settings.Add(S(X, "fahrenheit", IniKind.Switch, "false", "Show temperatures in Fahrenheit."));
            p.Settings.Add(S(T, "nxemini", IniKind.Switch, "true", "Pressing Y in the guide's mini blade runs DashLaunch again."));
            p.Settings.Add(S(T, "pingpatch", IniKind.Switch, "false", "Removes the ping limit for system link play."));
            p.Settings.Add(S(T, "contpatch", IniKind.Switch, "false", "Lets add-on content (DLC) from another console run."));
            p.Settings.Add(S(T, "xblapatch", IniKind.Switch, "false", "Lets arcade games from another console run as full games."));
            p.Settings.Add(S(T, "licpatch", IniKind.Switch, "false", "Fakes licence answers; used with contpatch / xblapatch or for extracted arcade games."));
            p.Settings.Add(S(T, "fatalfreeze", IniKind.Switch, "false", "On a fatal error: true = freeze, false = restart or switch off (see fatalreboot)."));
            p.Settings.Add(S(T, "fatalreboot", IniKind.Switch, "false", "With fatalfreeze off: true = restart after a fatal error, false = switch off."));
            p.Settings.Add(S(T, "safereboot", IniKind.Switch, "true", "Soft restarts work normally. On a JTAG console only with the SMC restart fix."));
            p.Settings.Add(S(T, "regionspoof", IniKind.Switch, "false", "Holding RB while a game starts makes it see the region set below."));
            p.Settings.Add(S(T, "region", IniKind.Hex, "0x7fff", "The region used by regionspoof."));
            p.Settings.Add(S(T, "dvdexitdash", IniKind.Switch, "false", "Ejecting a disc: true = go to the normal dashboard, false = go to Default."));
            p.Settings.Add(S(T, "xblaexitdash", IniKind.Switch, "false", "Leaving an arcade game: true = go to the arcade menu, false = go to Default."));
            p.Settings.Add(S(T, "nosysexit", IniKind.Switch, "false", "System Settings from the guide does not leave to the normal dashboard."));
            p.Settings.Add(S(T, "nohud", IniKind.Switch, "false", "The guide never appears. Careful: you lose the guide button menu."));
            p.Settings.Add(S(T, "noupdater", IniKind.Switch, "true", "The console ignores system updates on discs and USB drives. Keep this on for a modified console."));
            p.Settings.Add(S(T, "debugout", IniKind.Switch, "false", "DashLaunch sends its debug text out."));
            p.Settings.Add(S(T, "exchandler", IniKind.Switch, "true", "DashLaunch handles crashes (needed for Dumpfile)."));
            p.Settings.Add(S(T, "liveblock", IniKind.Switch, "true", "Blocks the console from reaching Xbox Live. Keep on to avoid a ban."));
            p.Settings.Add(S(T, "livestrong", IniKind.Switch, "false", "With liveblock: blocks every Microsoft address, which also stops cover downloads."));
            p.Settings.Add(S(T, "remotenxe", IniKind.Switch, "false", "A media remote's guide / power button starts the normal dashboard."));
            p.Settings.Add(S(T, "hddalive", IniKind.Switch, "false", "Keeps USB drives awake by writing to alive.txt on them."));
            p.Settings.Add(S(T, "hddtimer", IniKind.Number, "210", "Seconds between those writes."));
            p.Settings.Add(S(T, "signnotice", IniKind.Switch, "false", "Hides sign-in pop-ups such as \"Live is blocked\"."));
            p.Settings.Add(S(T, "autoshut", IniKind.Switch, "false", "Holding the guide button selects Shut down instead of Cancel."));
            p.Settings.Add(S(T, "autooff", IniKind.Switch, "false", "Holding the guide button switches the console off without asking."));
            p.Settings.Add(S(T, "xhttp", IniKind.Switch, "true", "Programs can use the console's web functions without being signed in."));
            p.Settings.Add(S(T, "tempbcast", IniKind.Switch, "false", "Sends the temperature readings over the network."));
            p.Settings.Add(S(T, "temptime", IniKind.Number, "10", "Seconds between temperature broadcasts."));
            p.Settings.Add(S(T, "tempport", IniKind.Number, "7030", "Network port for temperature broadcasts."));
            p.Settings.Add(S(T, "sockpatch", IniKind.Switch, "false", "Every program may use unprotected network connections."));
            p.Settings.Add(S(T, "passlaunch", IniKind.Switch, "false", "Launch data is kept when a quick-launch program starts."));
            p.Settings.Add(S(T, "fakelive", IniKind.Switch, "false", "Makes the console believe it is on Xbox Live. Forces liveblock on."));
            p.Settings.Add(S(T, "nonetstore", IniKind.Switch, "true", "Hides network / cloud storage in storage lists."));
            p.Settings.Add(S(T, "shuttemps", IniKind.Switch, "false", "Shows temperatures in the shut-down dialog. Turns autooff off."));
            p.Settings.Add(S(T, "devprof", IniKind.Switch, "false", "Development-kit profiles work instead of showing as corrupted."));
            p.Settings.Add(S(T, "devlink", IniKind.Switch, "false", "System link works with development kits."));
            p.Settings.Add(S(T, "autoswap", IniKind.Switch, "false", "DashLaunch swaps discs itself for multi-disc games. Off if Freestyle or swap.xex does it."));
            p.Settings.Add(S(T, "nohealth", IniKind.Switch, "true", "Skips the Kinect health warning when a game starts."));
            p.Settings.Add(S(T, "nooobe", IniKind.Switch, "true", "Skips the first-time set-up screens when settings already exist."));
            p.Settings.Add(S(T, "autofake", IniKind.Switch, "false", "Turns fakelive on only for the normal dashboard and indie games."));
            for (int i = 0; i <= 9; i++) p.Settings.Add(S(T, "autofake" + i, IniKind.Hex, "0x00000000", "Title ID of a game that should also get fakelive (up to ten)."));
            p.Settings.Add(S(T, "autocont", IniKind.Switch, "false", "With autofake: contpatch applies to community games only."));

            var sb = new StringBuilder();
            sb.Append("; launch.ini for DashLaunch 3.x, written by X360GameHack\r\n; Put it on the root of Hdd: or of a FAT32 Usb: drive. It is read when the console starts.\r\n");
            string section = null;
            foreach (var s in p.Settings)
            {
                if (s.Section != section) { section = s.Section; sb.Append("\r\n[").Append(section).Append("]\r\n"); }
                sb.Append("; ").Append(s.Does).Append("\r\n").Append(s.Key).Append(" = ").Append(s.Default).Append("\r\n");
            }
            p.Template = sb.ToString();
            return p;
        }

        /// <summary>
        /// xbdm.ini: XBDM runs each line as one of its own commands when it starts (a real file: dbgname name="Jtag",
        /// setcolor name="nosidecar", dumpmode smart). The three known ones are commands of the console's xbdm.xex;
        /// any other line is shown and kept as it is.
        /// </summary>
        public static readonly IniProfile Xbdm = BuildXbdm();
        private static IniProfile BuildXbdm()
        {
            var p = new IniProfile { Name = "xbdm.ini", FileName = "xbdm.ini", Format = IniFormat.XbdmCommands, What = "Commands the XBDM plugin (xbdm.xex) runs when it starts - one per line." };
            p.Settings.Add(new IniSetting { Section = "", Key = "dbgname", ArgName = "name", Kind = IniKind.Text, Default = "Xbox360", Does = "The console's name, as PCs see it (Neighborhood, console discovery)." });
            p.Settings.Add(new IniSetting { Section = "", Key = "setcolor", ArgName = "name", Kind = IniKind.Text, Default = "black", Allowed = new[] { "black", "blue", "bluegray", "nosidecar", "white" }, Does = "The console colour PC tools show for it." });
            p.Settings.Add(new IniSetting { Section = "", Key = "dumpmode", Kind = IniKind.Text, Default = "smart", Allowed = new[] { "smart", "enabled", "disabled" }, Does = "Crash dumps: smart (only when no debugger is attached), enabled (always) or disabled (never)." });
            return p;
        }

        /// <summary>JRPC.ini: quoted blocks - "Settings" { "KV Stealer Protection" "True" } and "Plugins" { "plugin1" "Hdd:\" }.</summary>
        public static readonly IniProfile Jrpc = BuildJrpc();
        private static IniProfile BuildJrpc()
        {
            var p = new IniProfile { Name = "JRPC.ini", FileName = "JRPC.ini", Format = IniFormat.KeyValues, What = "Settings of the JRPC2 plugin, in quoted blocks." };
            p.Settings.Add(new IniSetting { Section = "Settings", Key = "KV Stealer Protection", Kind = IniKind.Switch, Default = "True", Does = "JRPC2's protection against programs that try to read this console's keyvault through it." });
            for (int i = 1; i <= 3; i++)
                p.Settings.Add(new IniSetting { Section = "Plugins", Key = "plugin" + i, Kind = IniKind.Text, Default = "Hdd:\\", Does = "A plugin JRPC2 loads after itself, for example Hdd:\\MyPlugin.xex (\"Hdd:\\\" alone = none)." });
            return p;
        }

        public static readonly IniProfile[] All = { Launch, Xbdm, Jrpc };
    }
}
