using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace X360GameHack.UI
{
    /// <summary>
    /// A bordered editor for mod payloads (JSON with // notes), drawn like <see cref="XTextBox"/>. The text is colored as
    /// you type - notes, entry names, text, numbers and true / false / null each have their own color, and a bare word
    /// that needs "double quotes" shows in red - and a check can tint the lines it found something on.
    /// </summary>
    public sealed class CodeEditor : Panel, IMeasure
    {
        public CodeBox Box { get; }
        private bool _focused;

        public CodeEditor()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Input;
            Padding = new Padding(Theme.S(10), Theme.S(8), Theme.S(6), Theme.S(6));
            Box = new CodeBox { Dock = DockStyle.Fill };
            Box.GotFocus += (s, e) => { _focused = true; Invalidate(); };
            Box.LostFocus += (s, e) => { _focused = false; Invalidate(); };
            Controls.Add(Box);
            Height = Theme.S(110);
            Cursor = Cursors.IBeam;
            Click += (s, e) => Box.Focus();
        }

        /// <summary>The text, lines ending in \n. Setting it replaces everything as one change that Ctrl+Z takes back.</summary>
        public override string Text { get => Box.Text; set => Box.ReplaceAll(value); }
        public new event EventHandler TextChanged { add => Box.TextChanged += value; remove => Box.TextChanged -= value; }

        public int MeasureHeight(int width) => Height;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(6)))
            using (var b = new SolidBrush(Theme.Input))
            using (var p = new Pen(_focused ? Theme.Accent : Theme.Border, _focused ? 1.5f : 1f))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
        }
    }

    /// <summary>
    /// The editing part of <see cref="CodeEditor"/>. Coloring only ever changes colors, never the text, and only where
    /// something changed. The box keeps its own undo / redo (Ctrl+Z, Ctrl+Y) so that replacing the whole text can be
    /// undone too; it keeps the indent on Enter, writes two spaces for Tab, pastes plain text only and ignores the
    /// rich-text shortcuts (fonts, alignment, line spacing) - the payload is plain text.
    /// </summary>
    public sealed class CodeBox : RichTextBox
    {
        private enum Ink : byte { Plain, Name, Text, Number, Word, Note, Odd }
        private const byte Unknown = 0xFF;
        private const int MaxSteps = 200;

        private readonly int[] _ink;                 // COLORREF for each Ink
        private readonly int[] _tint;                // COLORREF for line tint 1 (problem) and 2 (note); 0 = none
        private string _shown = "";                  // the text the colors on screen belong to
        private byte[] _style = new byte[0];         // the style each character of _shown has on screen (ink | tint << 3)
        private readonly HashSet<int> _bad = new HashSet<int>(), _warn = new HashSet<int>();
        private readonly Timer _recolor = new Timer { Interval = 40 };

        private string _last = "";                   // the text after the last change: what Ctrl+Z steps back from
        private readonly List<string> _undo = new List<string>(), _redo = new List<string>();
        private DateTime _typedAt = DateTime.MinValue;
        private int _typedPos = -1;
        private bool _quiet;                         // true while the box changes its own text (undo, redo, replace)

        public CodeBox()
        {
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Input;
            ForeColor = Theme.Text;
            Font = Theme.Mono;
            WordWrap = true;
            ScrollBars = RichTextBoxScrollBars.Vertical;
            DetectUrls = false;
            HideSelection = false;
            AcceptsTab = true;
            Theme.DarkScrollbars(this);

            bool light = Theme.IsLight;
            Color[] ink = light
                ? new[] { Theme.Text, Rgb(0x0451A5), Rgb(0x9A4A00), Rgb(0x098658), Rgb(0x0000FF), Rgb(0x2E7D32), Rgb(0xE51400) }
                : new[] { Theme.Text, Rgb(0x9CDCFE), Rgb(0xCE9178), Rgb(0xB5CEA8), Rgb(0x569CD6), Rgb(0x6A9955), Rgb(0xFF5A5A) };
            _ink = ink.Select(ColorTranslator.ToWin32).ToArray();
            _tint = new[] { 0, ColorTranslator.ToWin32(Theme.Blend(Theme.Input, Theme.Danger, light ? .16f : .30f)),
                               ColorTranslator.ToWin32(Theme.Blend(Theme.Input, Theme.Warning, light ? .20f : .22f)) };
            _recolor.Tick += (s, e) => Recolor();
            ContextMenuStrip = BuildMenu();
        }

        private static Color Rgb(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        public bool CanStepBack => _undo.Count > 0;
        public bool CanStepForward => _redo.Count > 0;

        /// <summary>Steps back one change (Ctrl+Z).</summary>
        public void StepBack()
        {
            if (_undo.Count == 0) return;
            string to = Pop(_undo);
            Push(_redo, _last);
            Apply(to, false);
        }

        /// <summary>Takes the change again (Ctrl+Y).</summary>
        public void StepForward()
        {
            if (_redo.Count == 0) return;
            string to = Pop(_redo);
            Push(_undo, _last);
            Apply(to, false);
        }

        /// <summary>Replaces all of the text as one change that Ctrl+Z takes back.</summary>
        public void ReplaceAll(string text)
        {
            text = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            if (text == _last) return;
            Push(_undo, _last);
            _redo.Clear();
            if (!IsHandleCreated)
            {
                _quiet = true;
                try { base.Text = text; } finally { _quiet = false; }
                _last = base.Text;
                return;
            }
            Apply(text, true);
        }

        /// <summary>Tints lines (counted from 1): problems in red, notes in amber, until the text changes.</summary>
        public void Mark(IEnumerable<int> problems, IEnumerable<int> notes)
        {
            _bad.Clear(); _warn.Clear();
            foreach (int l in problems ?? Enumerable.Empty<int>()) if (l > 0) _bad.Add(l);
            foreach (int l in notes ?? Enumerable.Empty<int>()) if (l > 0 && !_bad.Contains(l)) _warn.Add(l);
            Recolor();
        }

        // ------------------------------------------------------------------ text changes and undo

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SendMessage(Handle, EM_SETUNDOLIMIT, IntPtr.Zero, IntPtr.Zero);   // the box's own undo is replaced by the one here
            _shown = ""; _style = new byte[0];                                  // nothing on screen is known to be colored yet
            _last = Text;
            Recolor();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            if (!_quiet) Remember(Text);
            if (_bad.Count + _warn.Count > 0) { _bad.Clear(); _warn.Clear(); }   // the lines may have moved; the next check tints again
            _recolor.Stop();
            _recolor.Start();
            base.OnTextChanged(e);
        }

        /// <summary>Records a change made by typing, pasting, cutting or deleting. Typing in one place is one step.</summary>
        private void Remember(string now)
        {
            if (now == _last) return;
            Diff(_last, now, out int at, out int removed, out int added);
            bool typing = removed + added == 1 && !(added == 1 && now[at] == '\n');
            bool join = typing && _undo.Count > 0 && (DateTime.UtcNow - _typedAt).TotalMilliseconds < 1500 && (at == _typedPos || at == _typedPos - 1);
            if (!join) Push(_undo, _last);
            _redo.Clear();
            _typedAt = typing ? DateTime.UtcNow : DateTime.MinValue;
            _typedPos = at + added;
            _last = now;
        }

        /// <summary>Changes the text to <paramref name="to"/> by replacing only the part that differs.</summary>
        private void Apply(string to, bool toStart)
        {
            Diff(Text, to, out int at, out int removed, out int added);
            _quiet = true;
            try
            {
                var range = new CHARRANGE { cpMin = at, cpMax = at + removed };
                SendMessage(Handle, EM_EXSETSEL, IntPtr.Zero, ref range);
                SendMessage(Handle, EM_REPLACESEL, IntPtr.Zero, to.Substring(at, added));
            }
            finally { _quiet = false; }
            _last = Text;
            _typedAt = DateTime.MinValue;
            Select(toStart ? 0 : Math.Min(at + added, TextLength), 0);
            ScrollToCaret();
        }

        private static void Diff(string a, string b, out int at, out int removed, out int added)
        {
            int n = Math.Min(a.Length, b.Length), p = 0;
            while (p < n && a[p] == b[p]) p++;
            int s = 0;
            while (s < n - p && a[a.Length - 1 - s] == b[b.Length - 1 - s]) s++;
            at = p; removed = a.Length - p - s; added = b.Length - p - s;
        }

        private static void Push(List<string> stack, string s) { stack.Add(s); if (stack.Count > MaxSteps) stack.RemoveAt(0); }
        private static string Pop(List<string> stack) { string s = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1); return s; }

        // ------------------------------------------------------------------ keys

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Z: case Keys.Alt | Keys.Back: StepBack(); return true;
                case Keys.Control | Keys.Y: case Keys.Control | Keys.Shift | Keys.Z: StepForward(); return true;
                case Keys.Control | Keys.V: case Keys.Shift | Keys.Insert: PastePlain(); return true;
                case Keys.Tab: SelectedText = "  "; return true;
                case Keys.Shift | Keys.Tab: Outdent(); return true;
                case Keys.Enter: NewLine(); return true;
                case Keys.Control | Keys.A: case Keys.Control | Keys.C: case Keys.Control | Keys.X:
                    return base.ProcessCmdKey(ref msg, keyData);
            }
            if ((keyData & Keys.Control) != 0 && (keyData & Keys.Alt) == 0 && CharKey(keyData & Keys.KeyCode))
            {
                base.ProcessCmdKey(ref msg, keyData);   // the window's own shortcuts still work...
                return true;                           // ...but the box never changes fonts, alignment or spacing
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static bool CharKey(Keys k) =>
            (k >= Keys.A && k <= Keys.Z) || (k >= Keys.D0 && k <= Keys.D9) || (k >= Keys.NumPad0 && k <= Keys.NumPad9) ||
            k == Keys.Oemplus || k == Keys.OemMinus || k == Keys.Add || k == Keys.Subtract || k == Keys.Oemcomma || k == Keys.OemPeriod ||
            k == Keys.OemOpenBrackets || k == Keys.OemCloseBrackets || k == Keys.OemQuestion || k == Keys.OemSemicolon ||
            k == Keys.OemPipe || k == Keys.OemQuotes || k == Keys.Oemtilde;

        private void PastePlain()
        {
            string s = null;
            try { if (Clipboard.ContainsText()) s = Clipboard.GetText(); }
            catch (ExternalException) { }
            if (!string.IsNullOrEmpty(s)) SelectedText = s.Replace("\r\n", "\n");
        }

        /// <summary>Enter keeps the indent of the line, one step deeper after an opening { or [.</summary>
        private void NewLine()
        {
            string t = Text;
            int caret = Math.Min(SelectionStart, t.Length);
            int lineStart = caret == 0 ? 0 : t.LastIndexOf('\n', caret - 1) + 1;
            int i = lineStart;
            while (i < caret && (t[i] == ' ' || t[i] == '\t')) i++;
            string indent = t.Substring(lineStart, i - lineStart);
            int j = caret - 1;
            while (j >= lineStart && (t[j] == ' ' || t[j] == '\t')) j--;
            if (j >= lineStart && (t[j] == '{' || t[j] == '[')) indent += "  ";
            SelectedText = "\n" + indent;
        }

        /// <summary>Shift+Tab takes up to two spaces off the start of the line.</summary>
        private void Outdent()
        {
            string t = Text;
            int start = SelectionStart, length = SelectionLength;
            int lineStart = start == 0 ? 0 : t.LastIndexOf('\n', Math.Min(start, t.Length) - 1) + 1;
            int n = 0;
            while (n < 2 && lineStart + n < t.Length && t[lineStart + n] == ' ') n++;
            if (n == 0) return;
            Select(lineStart, n);
            SelectedText = "";
            Select(Math.Max(lineStart, start - n), length);
        }

        private ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.Body };
            ToolStripMenuItem Item(string text, string keys, Action a)
            {
                var it = new ToolStripMenuItem(text, null, (s, e) => a()) { ShortcutKeyDisplayString = keys };
                m.Items.Add(it);
                return it;
            }
            var undo = Item("Undo", "Ctrl+Z", StepBack);
            var redo = Item("Redo", "Ctrl+Y", StepForward);
            m.Items.Add(new ToolStripSeparator());
            var cut = Item("Cut", "Ctrl+X", Cut);
            var copy = Item("Copy", "Ctrl+C", Copy);
            var paste = Item("Paste", "Ctrl+V", PastePlain);
            m.Items.Add(new ToolStripSeparator());
            Item("Select all", "Ctrl+A", () => { Focus(); SelectAll(); });
            m.Opening += (s, e) =>
            {
                undo.Enabled = _undo.Count > 0;
                redo.Enabled = _redo.Count > 0;
                cut.Enabled = copy.Enabled = SelectionLength > 0;
                bool text = false;
                try { text = Clipboard.ContainsText(); } catch (ExternalException) { }
                paste.Enabled = text;
            };
            return m;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _recolor.Dispose(); ContextMenuStrip?.Dispose(); }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ colors

        /// <summary>Brings the colors up to date with the text and the line tints, touching only what is out of date.</summary>
        private void Recolor()
        {
            _recolor.Stop();
            if (!IsHandleCreated || IsDisposed) return;
            string t = Text;
            if (t != _shown)
            {
                // characters that did not change keep the color they have on screen
                Diff(_shown, t, out int at, out int removed, out int added);
                var moved = new byte[t.Length];
                Array.Copy(_style, 0, moved, 0, at);
                for (int i = at; i < at + added; i++) moved[i] = Unknown;
                Array.Copy(_style, at + removed, moved, at + added, t.Length - at - added);
                _style = moved;
                _shown = t;
            }
            byte[] want = Styles(t);
            var runs = new List<int>();   // start, end, style
            for (int i = 0; i < t.Length;)
            {
                if (want[i] == _style[i]) { i++; continue; }
                int j = i + 1;
                while (j < t.Length && want[j] == want[i]) j++;
                runs.Add(i); runs.Add(j); runs.Add(want[i]);
                i = j;
            }
            if (runs.Count == 0) return;

            var sel = new CHARRANGE();
            SendMessage(Handle, EM_EXGETSEL, IntPtr.Zero, ref sel);
            var scroll = new POINT();
            SendMessage(Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref scroll);
            IntPtr mask = SendMessage(Handle, EM_GETEVENTMASK, IntPtr.Zero, IntPtr.Zero);
            SendMessage(Handle, EM_SETEVENTMASK, IntPtr.Zero, IntPtr.Zero);
            SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                var cf = new CHARFORMAT2 { cbSize = Marshal.SizeOf(typeof(CHARFORMAT2)), dwMask = CFM_COLOR | CFM_BACKCOLOR };
                for (int r = 0; r < runs.Count; r += 3)
                {
                    var range = new CHARRANGE { cpMin = runs[r], cpMax = runs[r + 1] };
                    SendMessage(Handle, EM_EXSETSEL, IntPtr.Zero, ref range);
                    int style = runs[r + 2], tint = style >> 3;
                    cf.crTextColor = _ink[style & 7];
                    cf.dwEffects = tint == 0 ? CFE_AUTOBACKCOLOR : 0;
                    cf.crBackColor = _tint[tint];
                    SendMessage(Handle, EM_SETCHARFORMAT, (IntPtr)SCF_SELECTION, ref cf);
                }
                _style = want;
            }
            finally
            {
                SendMessage(Handle, EM_EXSETSEL, IntPtr.Zero, ref sel);
                SendMessage(Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref scroll);
                SendMessage(Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                SendMessage(Handle, EM_SETEVENTMASK, IntPtr.Zero, mask);
                Invalidate();
            }
        }

        /// <summary>The style every character should have: what it is (note, name, text...) and the tint of its line.</summary>
        private byte[] Styles(string t)
        {
            int n = t.Length;
            var s = new byte[n];
            void Fill(int from, int to, Ink k) { for (int x = from; x < to; x++) s[x] = (byte)k; }
            int i = 0;
            while (i < n)
            {
                char c = t[i];
                if (c == '/' && i + 1 < n && t[i + 1] == '/')
                {
                    int e = t.IndexOf('\n', i); if (e < 0) e = n;
                    Fill(i, e, Ink.Note); i = e; continue;
                }
                if (c == '/' && i + 1 < n && t[i + 1] == '*')
                {
                    int e = t.IndexOf("*/", i + 2, StringComparison.Ordinal); e = e < 0 ? n : e + 2;
                    Fill(i, e, Ink.Note); i = e; continue;
                }
                if (c == '"' || c == '\'')
                {
                    int j = i + 1;
                    while (j < n && t[j] != c && t[j] != '\n') { if (t[j] == '\\' && j + 1 < n && t[j + 1] != '\n') j++; j++; }
                    bool closed = j < n && t[j] == c;
                    int e = closed ? j + 1 : j;
                    Fill(i, e, !closed ? Ink.Odd : ColonAfter(t, e) ? Ink.Name : Ink.Text);
                    i = e; continue;
                }
                if (char.IsDigit(c) || ((c == '-' || c == '+' || c == '.') && i + 1 < n && char.IsDigit(t[i + 1])))
                {
                    int j = i + 1;
                    while (j < n && (char.IsLetterOrDigit(t[j]) || t[j] == '.' || ((t[j] == '-' || t[j] == '+') && (t[j - 1] == 'e' || t[j - 1] == 'E')))) j++;
                    Fill(i, j, Ink.Number); i = j; continue;
                }
                if (char.IsLetter(c) || c == '_' || c == '$')
                {
                    int j = i + 1;
                    while (j < n && (char.IsLetterOrDigit(t[j]) || t[j] == '_' || t[j] == '$')) j++;
                    string w = t.Substring(i, j - i);
                    Fill(i, j, w == "true" || w == "false" || w == "null" ? Ink.Word : w == "NaN" || w == "Infinity" ? Ink.Number : ColonAfter(t, j) ? Ink.Name : Ink.Odd);
                    i = j; continue;
                }
                s[i++] = (byte)Ink.Plain;   // spaces, line ends and { } [ ] , :
            }
            if (_bad.Count + _warn.Count > 0)
            {
                int line = 1;
                for (int x = 0; x < n; x++)
                {
                    int tint = _bad.Contains(line) ? 1 : _warn.Contains(line) ? 2 : 0;
                    if (tint != 0) s[x] |= (byte)(tint << 3);
                    if (t[x] == '\n') line++;
                }
            }
            return s;
        }

        private static bool ColonAfter(string t, int k)
        {
            while (k < t.Length && (t[k] == ' ' || t[k] == '\t')) k++;
            return k < t.Length && t[k] == ':';
        }

        // ------------------------------------------------------------------ rich edit messages

        private const int WM_SETREDRAW = 0x000B, WM_USER = 0x0400, EM_REPLACESEL = 0x00C2;
        private const int EM_EXGETSEL = WM_USER + 52, EM_EXSETSEL = WM_USER + 55, EM_GETEVENTMASK = WM_USER + 59,
                          EM_SETCHARFORMAT = WM_USER + 68, EM_SETEVENTMASK = WM_USER + 69, EM_SETUNDOLIMIT = WM_USER + 82,
                          EM_GETSCROLLPOS = WM_USER + 221, EM_SETSCROLLPOS = WM_USER + 222;
        private const int SCF_SELECTION = 1;
        private const uint CFM_COLOR = 0x40000000, CFM_BACKCOLOR = 0x04000000, CFE_AUTOBACKCOLOR = 0x04000000;

        [StructLayout(LayoutKind.Sequential)] private struct CHARRANGE { public int cpMin, cpMax; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CHARFORMAT2
        {
            public int cbSize;
            public uint dwMask, dwEffects;
            public int yHeight, yOffset, crTextColor;
            public byte bCharSet, bPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szFaceName;
            public short wWeight, sSpacing;
            public int crBackColor, lcid, dwReserved;
            public short sStyle, wKerning;
            public byte bUnderlineType, bAnimation, bRevAuthor, bReserved1;
        }

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref CHARRANGE lParam);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref POINT lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref CHARFORMAT2 lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }
}
