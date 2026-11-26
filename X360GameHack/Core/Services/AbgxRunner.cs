using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// abgx360's HTML report turned into coloured lines, fed as it arrives. abgx360 colours its output with
    /// &lt;span class=X&gt; (green, yellow, red, ...), ends lines with &lt;br&gt;, writes runs of spaces as &amp;nbsp;, other
    /// characters as &amp;#N; and only ever uses span, br, b, table/tr/td and img in the body (read from its source). Text is
    /// laid out the way a browser shows that page: white space collapses, &amp;nbsp; does not.
    /// </summary>
    public sealed class AbgxReport
    {
        public struct Piece
        {
            public string Text;
            /// <summary>abgx360's class name: normal, white, green, yellow, red, cyan, blue, darkblue, brown, darkgray, ...</summary>
            public string Style;
            public bool Bold;
        }

        /// <summary>Every line so far; the last one may still grow.</summary>
        public readonly List<List<Piece>> Lines = new List<List<Piece>> { new List<Piece>() };
        private readonly StringBuilder _pending = new StringBuilder();
        private readonly List<string> _styles = new List<string>();
        private readonly List<bool> _hiddenSpans = new List<bool>();     // per open span: opened with display:none
        private int _hidden;                                             // inside a display:none span: not shown (a browser hides it too)
        // abgx360's only tables are its avatar award and achievement lists: each row is gathered (cells -> lines -> pieces)
        // and then laid out the way its text output lays them out
        private List<List<List<Piece>>> _row;
        private bool _skipCell;
        private bool _body, _skip, _bold, _lineStart = true, _lastRawSpace;
        private readonly bool _plain;

        /// <param name="plainText">True for abgx360's text output (--stripcolors without --html): taken as it is.</param>
        public AbgxReport(bool plainText = false) { _plain = plainText; if (plainText) _body = true; }

        /// <summary>Lines that will not change any more.</summary>
        public int CompleteLines => Lines.Count - 1;

        public void Feed(string chunk)
        {
            if (string.IsNullOrEmpty(chunk)) return;
            if (_plain)
            {
                foreach (char c in chunk)
                {
                    if (c == '\n') NewLine();
                    else if (c != '\r') Add(c.ToString());
                }
                return;
            }
            _pending.Append(chunk);
            string s = _pending.ToString();
            _pending.Clear();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '<')
                {
                    int end = s.IndexOf('>', i);
                    if (end < 0) { _pending.Append(s, i, s.Length - i); break; }
                    Tag(s.Substring(i + 1, end - i - 1).Trim());
                    i = end + 1;
                }
                else if (c == '&')
                {
                    int end = s.IndexOf(';', i);
                    if (end < 0 && s.Length - i <= 12) { _pending.Append(s, i, s.Length - i); break; }
                    string e = end > i && end - i <= 12 ? Entity(s.Substring(i + 1, end - i - 1)) : null;
                    if (e == null) { Raw("&"); i++; continue; }
                    Text(e);
                    i = end + 1;
                }
                else
                {
                    int next = s.IndexOfAny(new[] { '<', '&' }, i);
                    if (next < 0) next = s.Length;
                    Raw(s.Substring(i, next - i));
                    i = next;
                }
            }
        }

        private void Tag(string t)
        {
            string lower = t.ToLowerInvariant();
            string name = lower.Split(' ', '\t', '\r', '\n', '/')[0];
            if (lower.StartsWith("/")) name = "/" + lower.Substring(1).Split(' ', '\t', '>')[0];
            switch (name)
            {
                case "body": _body = true; return;
                case "/body": case "/html": _body = false; return;
                case "script": case "style": _skip = true; return;
                case "/script": case "/style": _skip = false; return;
            }
            if (!_body || _skip) return;
            switch (name)
            {
                case "span":
                    {
                        int k = lower.IndexOf("class=", StringComparison.Ordinal);
                        string cls = k < 0 ? "normal" : lower.Substring(k + 6).Trim().Trim('"', '\'').Split(' ', '"', '\'')[0];
                        bool hide = lower.Replace(" ", "").Contains("display:none");
                        _styles.Add(cls);
                        _hiddenSpans.Add(hide);
                        if (hide) _hidden++;
                        return;
                    }
                case "/span":
                    if (_styles.Count > 0)
                    {
                        if (_hiddenSpans[_hiddenSpans.Count - 1]) _hidden--;
                        _styles.RemoveAt(_styles.Count - 1);
                        _hiddenSpans.RemoveAt(_hiddenSpans.Count - 1);
                    }
                    return;
                case "br":
                    if (_row != null) { if (_hidden == 0 && !_skipCell && _row.Count > 0) _row[_row.Count - 1].Add(new List<Piece>()); _lastRawSpace = false; _lineStart = true; }
                    else NewLine();
                    return;
                case "b": _bold = true; return;
                case "/b": _bold = false; return;
                case "tr": _row = new List<List<List<Piece>>>(); return;
                case "td":
                    if (_row != null) { _row.Add(new List<List<Piece>> { new List<Piece>() }); _skipCell = lower.Contains("class=sp"); _lastRawSpace = false; _lineStart = true; }
                    return;
                case "/td": _skipCell = false; return;
                case "/tr": if (_row != null) { var r = _row; _row = null; _skipCell = false; EmitRow(r); } return;
                case "table": case "/table": if (Lines[Lines.Count - 1].Count > 0) NewLine(); return;
                default: return;   // img, a: nothing to show as text
            }
        }

        /// <summary>
        /// One award / achievement row, laid out as abgx360 prints it in text: 5 spaces, "NN. " and the name wrapped at 43
        /// characters (further lines 9 spaces in); for an achievement the gamerscore right-aligned in 6 at column 46 plus the
        /// name's padding to 41, two spaces and the type; then the description on its own line(s), 9 spaces in, wrapped at 70.
        /// </summary>
        private void EmitRow(List<List<List<Piece>>> cells)
        {
            var content = cells.Where(c => c.Any(l => l.Count > 0)).ToList();
            if (content.Count == 0) return;
            if (Lines[Lines.Count - 1].Count > 0) NewLine();
            string CellLine(List<List<Piece>> c, int i) => i < c.Count ? string.Concat(c[i].Select(p => p.Text)) : "";
            Piece Style(List<List<Piece>> c, int i, string fallback) => i < c.Count && c[i].Count > 0 ? c[i][0] : new Piece { Style = fallback };
            var title = content[0];
            string head = CellLine(title, 0).Trim();
            var m = System.Text.RegularExpressions.Regex.Match(head, @"^(\d+\. )(.*)$");
            string number = m.Success ? m.Groups[1].Value : "", nameText = m.Success ? m.Groups[2].Value : head;
            var titleStyle = Style(title, 0, "achtitle");
            var names = Wrap(nameText, 43);
            AddStyled("     ", "white", false);
            AddStyled(number + names[0], titleStyle.Style, true);
            for (int i = 1; i < names.Count; i++) { NewLine(); AddStyled("         " + names[i], titleStyle.Style, true); }
            if (content.Count > 1)
            {
                var score = content[1];
                int pad = 41 - names[names.Count - 1].Length;
                if (pad > 0) AddStyled(new string(' ', pad), "white", false);
                var sc = Style(score, 0, "white_u");
                AddStyled(CellLine(score, 0).Trim().PadLeft(6), sc.Style, sc.Bold);
                AddStyled("  ", "white", false);
                var ty = Style(score, 1, "darkgray_u");
                AddStyled(CellLine(score, 1).Trim(), ty.Style, ty.Bold);
            }
            NewLine();
            string desc = string.Join("\n", Enumerable.Range(1, Math.Max(0, title.Count - 1)).Select(i => CellLine(title, i)));
            var descStyle = Style(title, 1, "normal_u");
            foreach (string d in Wrap(desc, 70)) { AddStyled("         " + d, descStyle.Style, descStyle.Bold); NewLine(); }
        }

        private void AddStyled(string text, string style, bool bold)
        {
            _lineStart = false;
            var line = Lines[Lines.Count - 1];
            if (line.Count > 0 && line[line.Count - 1].Style == style && line[line.Count - 1].Bold == bold)
            {
                var p = line[line.Count - 1];
                p.Text += text;
                line[line.Count - 1] = p;
            }
            else line.Add(new Piece { Text = text, Style = style, Bold = bold });
        }

        /// <summary>
        /// abgx360's findunicodedelimiter: a line longer than <paramref name="max"/> breaks at the last space at index 1..max
        /// (its search starts at max+1, but its loop only ever fills indexes 0..max, so that slot is never a collected space);
        /// a line break in the text starts a new line; no space, no break.
        /// </summary>
        public static List<string> Wrap(string s, int max)
        {
            var result = new List<string>();
            foreach (string part in (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string rest = part;
                while (rest.Length > max)
                {
                    int cut = -1;
                    for (int d = Math.Min(max, rest.Length - 1); d >= 1; d--) if (rest[d] == ' ') { cut = d; break; }
                    if (cut < 0) break;
                    result.Add(rest.Substring(0, cut));
                    rest = rest.Substring(cut + 1);
                }
                result.Add(rest);
            }
            return result;
        }

        private static string Entity(string e)
        {
            switch (e)
            {
                case "nbsp": return " ";
                case "amp": return "&";
                case "lt": return "<";
                case "gt": return ">";
                case "quot": return "\"";
                case "apos": return "'";
            }
            if (e.StartsWith("#"))
            {
                try
                {
                    int cp = e.StartsWith("#x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(e.Substring(2), 16) : int.Parse(e.Substring(1));
                    if (cp > 0 && cp <= 0x10FFFF && (cp < 0xD800 || cp > 0xDFFF)) return char.ConvertFromUtf32(cp);
                }
                catch { }
                return "?";
            }
            return null;
        }

        /// <summary>Source text between tags: runs of white space become one space, none at the start of a line.</summary>
        private void Raw(string s)
        {
            if (!_body || _skip) return;
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    if (_lineStart && sb.Length == 0) continue;
                    if (_lastRawSpace) continue;
                    sb.Append(' ');
                    _lastRawSpace = true;
                }
                else { sb.Append(c); _lastRawSpace = false; }
            }
            if (sb.Length > 0) Add(sb.ToString());
        }

        private void Text(string s)
        {
            if (!_body || _skip) return;
            _lastRawSpace = false;
            Add(s);
        }

        private void Add(string s)
        {
            if (_hidden > 0 || _skipCell) return;
            if (_row != null)
            {
                if (_row.Count == 0) _row.Add(new List<List<Piece>> { new List<Piece>() });
                var cell = _row[_row.Count - 1];
                var cl = cell[cell.Count - 1];
                string st = _styles.Count > 0 ? _styles[_styles.Count - 1] : "normal";
                if (cl.Count > 0 && cl[cl.Count - 1].Style == st && cl[cl.Count - 1].Bold == _bold) { var q = cl[cl.Count - 1]; q.Text += s; cl[cl.Count - 1] = q; }
                else cl.Add(new Piece { Text = s, Style = st, Bold = _bold });
                _lineStart = false;
                return;
            }
            _lineStart = false;
            var line = Lines[Lines.Count - 1];
            string style = _styles.Count > 0 ? _styles[_styles.Count - 1] : "normal";
            if (line.Count > 0 && line[line.Count - 1].Style == style && line[line.Count - 1].Bold == _bold)
            {
                var p = line[line.Count - 1];
                p.Text += s;
                line[line.Count - 1] = p;
            }
            else line.Add(new Piece { Text = s, Style = style, Bold = _bold });
        }

        private void NewLine()
        {
            // a browser drops the collapsible space at the end of a line
            var line = Lines[Lines.Count - 1];
            if (line.Count > 0 && _lastRawSpace)
            {
                var p = line[line.Count - 1];
                if (p.Text.EndsWith(" ")) { p.Text = p.Text.Substring(0, p.Text.Length - 1); line[line.Count - 1] = p; }
                if (p.Text.Length == 0) line.RemoveAt(line.Count - 1);
            }
            Lines.Add(new List<Piece>());
            _lineStart = true;
            _lastRawSpace = false;
        }

        public string LineText(int i) => string.Concat(Lines[i].Select(p => p.Text));

        /// <summary>The whole report as plain text (what abgx360 prints with --stripcolors).</summary>
        public string PlainText()
        {
            var lines = Lines.Select((l, i) => LineText(i)).ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return string.Join("\r\n", lines);
        }

        /// <summary>Plain text of a whole HTML report.</summary>
        public static string ToText(string html) { var r = new AbgxReport(); r.Feed(html); return r.PlainText(); }

        /// <summary>The verdict abgx360 printed, as far as the report shows it.</summary>
        public AbgxVerdict Verdict()
        {
            string all = string.Join("\n", Lines.Select((l, i) => LineText(i)));
            // abgx360's own wording (its source): "Basic Stealth check passed!", "Verification was successful, Stealth passed!",
            // "Stealth Failed!: ...", "Stealth status is uncertain", "Verification failed", "AutoFix was successful!",
            // "AutoFix Failed...", "Game partition CRC matches / does not match the verified ini!", "... was successfully rebuilt"
            bool Has(string s) => all.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
            var v = new AbgxVerdict
            {
                StealthPassed = Has("Stealth check passed") || Has("Stealth passed"),
                StealthFailed = Has("Stealth Failed"),
                StealthUncertain = Has("Stealth status is uncertain"),
                Verified = Has("Verification was successful"),
                VerifyFailed = Has("Verification failed"),
                AutoFixed = Has("AutoFix was successful"),
                AutoFixFailed = Has("AutoFix Failed"),
                GameCrcMatches = Has("Game partition CRC matches the verified ini"),
                GameCrcMismatch = Has("Game partition CRC does not match the verified ini"),
                Rebuilt = Has("was successfully rebuilt") || Has("successfully rebuilt to"),
                RedLines = Lines.Select((l, i) => l.Any(p => p.Style == "red" || p.Style == "wtfhex" || p.Style == "blackonred") ? LineText(i).Trim() : null).Where(x => !string.IsNullOrEmpty(x)).ToList(),
                YellowLines = Lines.Select((l, i) => l.Any(p => p.Style == "yellow" || p.Style == "blackonyellow") ? LineText(i).Trim() : null).Where(x => !string.IsNullOrEmpty(x)).ToList()
            };
            return v;
        }
    }

    public sealed class AbgxVerdict
    {
        public bool StealthPassed, StealthFailed, StealthUncertain, Verified, VerifyFailed, AutoFixed, AutoFixFailed, GameCrcMatches, GameCrcMismatch, Rebuilt;
        public List<string> RedLines = new List<string>(), YellowLines = new List<string>();

        public string Summary()
        {
            var s = new List<string>();
            if (AutoFixed) s.Add("AutoFix was successful");
            else if (AutoFixFailed) s.Add("AutoFix failed");
            if (Rebuilt) s.Add("rebuilt with its stealth sectors");
            if (StealthPassed) s.Add("stealth passed");
            else if (StealthFailed) s.Add("stealth failed");
            else if (StealthUncertain) s.Add("stealth uncertain");
            if (Verified) s.Add("verified");
            else if (VerifyFailed) s.Add("verification failed");
            if (GameCrcMatches) s.Add("game data matches the verified database");
            else if (GameCrcMismatch) s.Add("game data does not match the verified database");
            if (s.Count == 0) s.Add(RedLines.Count > 0 ? RedLines[0] : "finished");
            return string.Join(", ", s);
        }
    }

    public sealed class AbgxRunResult
    {
        public int ExitCode;
        public bool Cancelled;
        /// <summary>abgx360's report exactly as it wrote it (HTML when --html was given).</summary>
        public string Output = "";
        public AbgxReport Report;
        public AbgxVerdict Verdict;
    }

    /// <summary>
    /// Runs abgx360 inside this program: no console window, its report shown as it is written, and its questions ("Do you want
    /// to overwrite it? (y/n)" ...) asked in a dialog. abgx360 writes those questions to stderr and reads the answer from
    /// stdin with fgetc (readstdin), so the answer is simply written to its input. "--pause" must not be given here: it waits
    /// for a key press on a console this run does not have.
    /// </summary>
    public static class AbgxRunner
    {
        /// <param name="report">Fed with the report as it arrives (called on a worker thread).</param>
        /// <param name="status">abgx360's progress lines (downloads, CRC checks), called on a worker thread.</param>
        /// <param name="ask">Receives the question; true = yes. Called on a worker thread; null = always "no".</param>
        public static Task<AbgxRunResult> RunAsync(string exe, string arguments, string workDir, bool html,
            Action<string> report, Action<string> status, Func<string, bool> ask, CancellationToken ct)
        {
            if (arguments.IndexOf("--pause", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new ArgumentException("--pause cannot be used for a run inside the program.");
            return Task.Run(() =>
            {
                var result = new AbgxRunResult { Report = new AbgxReport(!html) };
                var cp1252 = Encoding.GetEncoding(1252);
                string dir = workDir != null && Directory.Exists(workDir) ? workDir : Tools.AppDir;
                var output = new StringBuilder();
                var guard = ToolIntegrity.LockAndVerify(exe);
                if (guard == null) throw new InvalidOperationException("abgx360 could not be opened to start it.");
                ChildProcess p;
                try { p = ChildProcess.Start(exe, arguments, dir); }
                finally { guard.Dispose(); }
                using (p)
                {
                    var outThread = new Thread(() =>
                    {
                        var reader = new StreamReader(p.Output, cp1252, false);
                        var buf = new char[4096];
                        int n;
                        while ((n = reader.Read(buf, 0, buf.Length)) > 0)
                        {
                            string chunk = new string(buf, 0, n);
                            lock (output) { output.Append(chunk); result.Report.Feed(chunk); }
                            report?.Invoke(chunk);
                        }
                    }) { IsBackground = true, Name = "abgx360 output" };
                    var errThread = new Thread(() =>
                    {
                        var reader = new StreamReader(p.Error, cp1252, false);
                        var buf = new char[1024];
                        var err = new StringBuilder();
                        int asked = -1, n;
                        while ((n = reader.Read(buf, 0, buf.Length)) > 0)
                        {
                            err.Append(buf, 0, n);
                            string all = err.ToString();
                            int cut = Math.Max(all.LastIndexOf('\n'), all.LastIndexOf('\r'));
                            string current = all.Substring(cut + 1);
                            string done = cut > 0 ? all.Substring(0, cut).Split('\n', '\r').LastOrDefault(l => l.Trim().Length > 0) : null;
                            if (current.TrimEnd().EndsWith("(y/n)", StringComparison.Ordinal) && asked != all.Length)
                            {
                                asked = all.Length;
                                // the question with the lines just above it (abgx360 explains before it asks)
                                var context = all.Split('\n').Select(l => l.Trim('\r', ' ')).Where(l => l.Length > 0).Reverse().Take(4).Reverse();
                                bool yes = ask != null && ask(string.Join("\n", context));
                                // exactly "y\n" / "n\n": readstdin takes the first byte of the line
                                try { p.Write(new[] { (byte)(yes ? 'y' : 'n'), (byte)'\n' }); } catch { }
                                status?.Invoke(current.Trim() + " " + (yes ? "y" : "n"));
                            }
                            else if (current.Trim().Length > 0) status?.Invoke(current.Trim());
                            else if (done != null) status?.Invoke(done.Trim());
                            if (err.Length > 1 << 16) { err.Remove(0, err.Length - (1 << 12)); asked = -1; }
                        }
                    }) { IsBackground = true, Name = "abgx360 progress" };
                    outThread.Start();
                    errThread.Start();
                    using (ct.Register(() => { try { if (!p.HasExited) { result.Cancelled = true; p.Kill(); } } catch { } }))
                        p.WaitForExit(-1);
                    outThread.Join(5000);
                    errThread.Join(5000);
                    result.ExitCode = p.ExitCode;
                }
                lock (output) result.Output = output.ToString();
                result.Verdict = result.Report.Verdict();
                return result;
            });
        }
    }
}
