using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace X360GameHack.UI
{
    public enum BtnKind { Primary, Secondary, Danger, Ghost }

    /// <summary>Something that eases toward a target a frame at a time (hover fades and the like).</summary>
    public interface IAnimated
    {
        /// <summary>Advance one frame; return true while there is more to do.</summary>
        bool Tick();
    }

    /// <summary>One shared 60 fps timer for every small UI transition; it stops itself when nothing is moving.</summary>
    public static class Anim
    {
        private static readonly Timer Clock = new Timer { Interval = 16 };
        private static readonly List<IAnimated> Live = new List<IAnimated>();

        static Anim()
        {
            Clock.Tick += (s, e) =>
            {
                for (int i = Live.Count - 1; i >= 0; i--)
                {
                    bool more;
                    try { more = Live[i].Tick(); } catch { more = false; }
                    if (!more) Live.RemoveAt(i);
                }
                if (Live.Count == 0) Clock.Stop();
            };
        }

        public static void Run(IAnimated a)
        {
            if (!Live.Contains(a)) Live.Add(a);
            Clock.Start();
        }

        /// <summary>Moves <paramref name="value"/> a step toward <paramref name="target"/>; true while it has not arrived.</summary>
        public static bool Ease(ref float value, float target, float rate = .28f)
        {
            float d = target - value;
            if (Math.Abs(d) < .02f) { value = target; return false; }
            value += d * rate;
            return true;
        }
    }

    /// <summary>Rounded, owner-drawn button with an optional icon glyph.</summary>
    public class XButton : Button, IAnimated
    {
        private bool _hover, _down;
        private float _glow;   // 0 = resting, 1 = fully hovered
        private string _glyph;
        private BtnKind _kind = BtnKind.Secondary;

        public string Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }
        public BtnKind Kind { get => _kind; set { _kind = value; Invalidate(); } }
        public bool Compact { get; set; }
        /// <summary>Left-aligned text with ellipsis (list-style buttons).</summary>
        public bool LeftAlign { get; set; }

        private bool _beta;
        /// <summary>Shows a BETA pill after the label: this button's job has not been tested yet (see TestStatus).</summary>
        public bool Beta
        {
            get => _beta;
            set { if (_beta == value) return; _beta = value; Parent?.PerformLayout(); Invalidate(); }
        }

        private bool ShowsBeta => _beta && !string.IsNullOrEmpty(Text);

        public XButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            Height = Theme.S(34);
            BackColor = Color.Transparent;
        }

        public XButton(string text, string glyph = null, BtnKind kind = BtnKind.Secondary, EventHandler onClick = null) : this()
        {
            Text = text;
            Glyph = glyph;
            Kind = kind;
            if (onClick != null) Click += onClick;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int pad = Compact ? Theme.S(10) : Theme.S(16);
            int w = pad * 2;
            if (!string.IsNullOrEmpty(Text)) w += TextWidth();
            if (!string.IsNullOrEmpty(Glyph)) w += Theme.S(18) + (string.IsNullOrEmpty(Text) ? 0 : Theme.S(6));
            if (ShowsBeta) w += Theme.BetaWidth;
            if (string.IsNullOrEmpty(Text)) w = Math.Max(w, Height);
            return new Size(Math.Max(w, MinimumSize.Width), Height);       // a minimum width set on the button counts too
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Anim.Run(this); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Anim.Run(this); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Parent?.PerformLayout(); }

        bool IAnimated.Tick()
        {
            if (IsDisposed) return false;
            bool more = Anim.Ease(ref _glow, _hover ? 1f : 0f);
            Invalidate();
            return more;
        }

        /// <summary>Width of the label exactly as it is drawn ("&amp;" is a literal character here, not a shortcut marker).</summary>
        private int TextWidth() =>
            TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color under = Parent?.BackColor ?? Theme.Window;
            if (under.A == 0) under = Theme.Surface;   // Color.Transparent's RGB is white and must never be blended in
            g.Clear(Parent?.BackColor ?? Theme.Window);

            // Resting and hovered looks are blended by the hover fade; pressed wins outright.
            Color bg, fg, border = Color.Empty;
            bool raised = false;
            switch (_kind)
            {
                case BtnKind.Primary:
                    bg = _down ? Theme.AccentPressed : Theme.Blend(Theme.Accent, Theme.AccentHover, _glow);
                    fg = Theme.OnAccent; raised = true; break;
                case BtnKind.Danger:
                    bg = Theme.Blend(Theme.DangerSoft, Theme.Danger, _down ? .5f : .3f * _glow);
                    fg = Theme.DangerText; border = Theme.Blend(Theme.DangerSoft, Theme.Danger, .35f); break;
                case BtnKind.Ghost:
                    bg = _down ? Theme.Border : _glow > 0 ? Theme.Blend(under, Theme.SurfaceHover, _glow) : Color.Transparent;
                    fg = Theme.Text; break;
                default:
                    bg = _down ? Theme.Border : Theme.Blend(Theme.Input, Theme.SurfaceHover, _glow);
                    fg = Theme.Text; border = Theme.Blend(Theme.Border, Theme.BorderStrong, _glow); raised = !_down; break;
            }
            if (!Enabled)
            {
                if (bg.A > 0) bg = Theme.Blend(bg, under, _kind == BtnKind.Primary ? .45f : .6f);
                if (!border.IsEmpty) border = Theme.Blend(border, under, .5f);
                fg = Theme.TextFaint;
                raised = false;
            }

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = Theme.S(7);
            using (var path = Theme.Rounded(r, radius))
            {
                if (bg.A > 0)
                {
                    if (raised) Theme.FillRaised(g, r, radius, bg, _kind == BtnKind.Primary ? 1f : .45f);
                    else using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                }
                if (_kind == BtnKind.Primary && Enabled)
                    using (var p = new Pen(Theme.Blend(bg, Color.Black, .22f))) g.DrawPath(p, path);
                if (!border.IsEmpty) using (var p = new Pen(border)) g.DrawPath(p, path);
                if (raised && !_down) Theme.DrawTopEdge(g, r, radius);
                if (Focused && ShowFocusCues) using (var p = new Pen(Theme.Accent, 1.5f)) g.DrawPath(p, path);
            }

            bool hasText = !string.IsNullOrEmpty(Text), hasGlyph = !string.IsNullOrEmpty(_glyph);
            int textW = hasText ? TextWidth() : 0;
            int glyphW = hasGlyph ? Theme.S(18) : 0;
            int gap = hasText && hasGlyph ? Theme.S(6) : 0;
            int betaW = ShowsBeta ? Theme.BetaWidth : 0;
            int total = textW + glyphW + gap + betaW;
            int x = LeftAlign ? Theme.S(10) : (Width - total) / 2;
            if (LeftAlign) textW = Math.Max(0, Math.Min(textW, Width - x - glyphW - gap - betaW - Theme.S(10)));
            else if (total > Width - Theme.S(8)) textW = Math.Max(0, textW - (total - (Width - Theme.S(8))));
            if (hasGlyph)
            {
                TextRenderer.DrawText(g, _glyph, Theme.Icon, new Rectangle(x, 0, glyphW, Height), fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += glyphW + gap;
            }
            if (hasText)
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, textW + 2, Height), fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (betaW > 0) Theme.DrawBeta(g, x + textW, Height / 2, bg.A > 0 ? bg : under);
        }
    }

    /// <summary>Rounded surface with a title, optional subtitle and header actions; content stacks vertically.</summary>
    public class Card : VStack, IAnimated
    {
        private string _title, _subtitle, _glyph;
        private bool _hover;
        private float _glow;
        public HStack Actions { get; }

        /// <summary>A card that is itself a button (a tile): it lights up under the mouse.</summary>
        public bool Interactive { get; set; }

        private int ChipSize => Theme.S(26);

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (Interactive) { _hover = true; Anim.Run(this); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (Interactive) { _hover = false; Anim.Run(this); } }

        bool IAnimated.Tick()
        {
            if (IsDisposed) return false;
            bool more = Anim.Ease(ref _glow, _hover ? 1f : 0f);
            Invalidate();
            return more;
        }

        public Card(string title = null, string subtitle = null, string glyph = null)
        {
            _title = title; _subtitle = subtitle; _glyph = glyph;
            BackColor = Theme.Surface;
            Padding = new Padding(Theme.S(18), Theme.S(16), Theme.S(18), Theme.S(18));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Actions = new HStack { Spacing = Theme.S(6), Height = Theme.S(30), Visible = false };
            Controls.Add(Actions);
        }

        public string Title { get => _title; set { _title = value; PerformLayout(); Invalidate(); } }
        public string Subtitle { get => _subtitle; set { _subtitle = value; PerformLayout(); Invalidate(); } }

        private bool _beta;
        /// <summary>Shows a BETA pill after the title: nothing on this card has been tested yet (see TestStatus).</summary>
        public bool Beta { get => _beta; set { if (_beta == value) return; _beta = value; Invalidate(); } }

        private int SubtitleHeight(int width)
        {
            if (string.IsNullOrEmpty(_subtitle)) return 0;
            return TextRenderer.MeasureText(_subtitle, Theme.Small, new Size(Math.Max(10, width), 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        }

        protected override int HeaderHeight
        {
            get
            {
                if (string.IsNullOrEmpty(_title)) return 0;
                int w = ClientSize.Width - Padding.Horizontal - ActionsWidth();
                return Theme.S(26) + (string.IsNullOrEmpty(_subtitle) ? 0 : SubtitleHeight(w) + Theme.S(2)) + Theme.S(8);
            }
        }

        private int ActionsWidth()
        {
            if (Actions == null || Actions.Controls.Count == 0) return 0;
            int w = 0;
            foreach (Control c in Actions.Controls) if (c.Visible) w += Measure.Width(c) + Actions.Spacing;
            return w;
        }

        public override int MeasureHeight(int width)
        {
            // Header depends on width; compute with the target width.
            int inner = width - Padding.Horizontal;
            int header = string.IsNullOrEmpty(_title) ? 0 :
                Theme.S(26) + (string.IsNullOrEmpty(_subtitle) ? 0 : SubtitleHeight(inner - ActionsWidth()) + Theme.S(2)) + Theme.S(8);
            int h = Padding.Vertical + header;
            var kids = Stacked();
            for (int i = 0; i < kids.Count; i++)
            {
                h += Measure.Height(kids[i], inner);
                if (i > 0) h += Spacing;
            }
            return h;
        }

        protected override List<Control> Stacked() => Measure.Visible(this).Where(c => c != Actions).ToList();

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (Actions != null && Actions.Controls.Count > 0)
            {
                int w = ActionsWidth();
                Actions.SetBounds(ClientSize.Width - Padding.Right - w, Padding.Top - Theme.S(3), w, Theme.S(30));
            }
        }

        public XButton AddAction(string text, string glyph, EventHandler onClick, BtnKind kind = BtnKind.Ghost)
        {
            var b = new XButton(text, glyph, kind, onClick) { Height = Theme.S(30), Compact = true };
            Actions.Controls.Add(b);
            Actions.Visible = true;
            PerformLayout();
            return b;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = Theme.S(12);
            using (var path = Theme.Rounded(r, radius))
            using (var b = new SolidBrush(Theme.Blend(BackColor, Theme.SurfaceHover, _glow * .7f)))
            using (var p = new Pen(Theme.Blend(Theme.Border, Theme.Accent, _glow * .75f)))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            Theme.DrawTopEdge(g, r, radius);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (string.IsNullOrEmpty(_title)) return;
            int x = Padding.Left;
            int w = ClientSize.Width - Padding.Horizontal - ActionsWidth();
            if (!string.IsNullOrEmpty(_glyph))
            {
                Theme.DrawIconChip(e.Graphics, new Rectangle(x, Padding.Top - Theme.S(1), ChipSize, ChipSize), _glyph, Theme.Icon, _glow);
                x += ChipSize + Theme.S(10);
                w -= ChipSize + Theme.S(10);
            }
            int titleW = w;
            if (_beta)
                titleW = Math.Max(0, Math.Min(w - Theme.BetaWidth,
                    TextRenderer.MeasureText(_title, Theme.CardTitle, new Size(int.MaxValue, Theme.S(24)), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width));
            TextRenderer.DrawText(e.Graphics, _title, Theme.CardTitle, new Rectangle(x, Padding.Top, titleW + (_beta ? 2 : 0), Theme.S(24)),
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (_beta) Theme.DrawBeta(e.Graphics, x + titleW, Padding.Top + Theme.S(12), Theme.Blend(BackColor, Theme.SurfaceHover, _glow * .7f));
            if (!string.IsNullOrEmpty(_subtitle))
                TextRenderer.DrawText(e.Graphics, _subtitle, Theme.Small,
                    new Rectangle(Padding.Left, Padding.Top + Theme.S(26), ClientSize.Width - Padding.Horizontal - ActionsWidth(), 2000),
                    Theme.TextDim, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Word-wrapping text label that measures its own height.</summary>
    public class TextBlock : Control, IMeasure
    {
        public TextFormatFlags Flags { get; set; } = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        public TextBlock(string text = "", Font font = null, Color? color = null)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Text = text;
            Font = font ?? Theme.Body;
            ForeColor = color ?? Theme.TextDim;
            BackColor = Color.Transparent;
        }

        public int MeasureHeight(int width)
        {
            if (string.IsNullOrEmpty(Text)) return 0;
            return TextRenderer.MeasureText(Text, Font, new Size(Math.Max(10, width), 100000), Flags | TextFormatFlags.NoPrefix).Height + Theme.S(2);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var sz = TextRenderer.MeasureText(Text ?? "", Font, new Size(proposedSize.Width > 0 ? proposedSize.Width : 100000, 100000), Flags | TextFormatFlags.NoPrefix);
            return new Size(sz.Width + Theme.S(2), sz.Height + Theme.S(2));
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
            Parent?.PerformLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, Flags | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Bordered single- or multi-line text input.</summary>
    public class XTextBox : Panel, IMeasure
    {
        public TextBox Box { get; }
        private bool _focused;

        public XTextBox(string cue = null, bool multiline = false, bool mono = false)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Input;
            Padding = new Padding(Theme.S(10), Theme.S(8), Theme.S(6), Theme.S(6));
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Input,
                ForeColor = Theme.Text,
                Font = mono ? Theme.Mono : Theme.Body,
                Multiline = multiline,
                Dock = DockStyle.Fill
            };
            if (multiline)
            {
                Box.ScrollBars = ScrollBars.Vertical;
                Box.AcceptsReturn = true;
                Box.WordWrap = true;
                Theme.DarkScrollbars(Box);
            }
            if (cue != null && !multiline) Theme.SetCue(Box, cue);
            Box.GotFocus += (s, e) => { _focused = true; Invalidate(); };
            Box.LostFocus += (s, e) => { _focused = false; Invalidate(); };
            Box.TextChanged += (s, e) => Invalid = false;
            Controls.Add(Box);
            Height = multiline ? Theme.S(110) : Theme.S(34);
            Cursor = Cursors.IBeam;
            Click += (s, e) => Box.Focus();
        }

        private bool _invalid;
        /// <summary>Draws a red edge until the text changes - points at a field that still needs filling in.</summary>
        public bool Invalid { get => _invalid; set { if (_invalid == value) return; _invalid = value; Invalidate(); } }

        public override string Text { get => Box.Text; set => Box.Text = value; }
        public bool ReadOnly { get => Box.ReadOnly; set => Box.ReadOnly = value; }
        public new event EventHandler TextChanged { add => Box.TextChanged += value; remove => Box.TextChanged -= value; }

        public int MeasureHeight(int width) => Height;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(6)))
            using (var b = new SolidBrush(Theme.Input))
            using (var p = new Pen(_invalid ? Theme.Danger : _focused ? Theme.Accent : Theme.Border, _focused || _invalid ? 1.5f : 1f))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
        }
    }

    /// <summary>Accent-colored checkbox.</summary>
    public class XCheck : CheckBox
    {
        private static readonly Font CheckFont = new Font(Theme.Icon.FontFamily, 9f);
        private bool _hover;

        public XCheck(string text, bool isChecked = false, string tooltip = null)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = text;
            Checked = isChecked;
            Font = Theme.Body;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            AutoSize = false;
            Height = Theme.S(26);
            if (tooltip != null) Tip.Set(this, tooltip);
        }

        private bool _beta;
        /// <summary>Shows a BETA pill after the label: this option has not been proven end to end (see TestStatus).</summary>
        public bool Beta
        {
            get => _beta;
            set { if (_beta == value) return; _beta = value; Parent?.PerformLayout(); Invalidate(); }
        }

        private int LabelWidth() => TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;

        public override Size GetPreferredSize(Size proposedSize)
            => new Size(Theme.S(26) + LabelWidth() + Theme.S(4) + (_beta ? Theme.BetaWidth : 0), Theme.S(26));

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int box = Theme.S(18);
            if (_beta)
            {
                int room = Width - box - Theme.S(8);
                int textW = Math.Max(0, Math.Min(LabelWidth(), room - Theme.BetaWidth));
                Theme.DrawBeta(g, box + Theme.S(8) + textW, Height / 2, Parent?.BackColor ?? Theme.Surface);
            }
            var r = new RectangleF(1, (Height - box) / 2f, box, box);
            using (var path = Theme.Rounded(r, Theme.S(4)))
            {
                Color fill = Checked ? (Enabled ? Theme.Accent : Theme.Border) : Theme.Input;
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (!Checked) using (var p = new Pen(_hover ? Theme.BorderStrong : Theme.Border)) g.DrawPath(p, path);
            }
            if (Checked)
                TextRenderer.DrawText(g, Glyph.Check, CheckFont,
                    Rectangle.Round(r), Theme.OnAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int labelW = Width - box - Theme.S(8) - (_beta ? Theme.BetaWidth : 0);
            if (_beta) labelW = Math.Max(0, Math.Min(LabelWidth() + 2, labelW));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(box + Theme.S(8), 0, labelW, Height),
                Enabled ? ForeColor : Theme.TextFaint, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Segmented single-choice selector (replaces radio button groups).</summary>
    public class Segmented : Control
    {
        private readonly string[] _items;
        private int _selected, _hover = -1;
        public event EventHandler SelectedIndexChanged;

        public Segmented(params string[] items)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _items = items;
            Font = Theme.Body;
            Height = Theme.S(32);
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get => _selected;
            set { if (value == _selected) return; _selected = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
        }

        /// <summary>The choices offered, in order (for code that labels them).</summary>
        public IReadOnlyList<string> Items => _items;

        private readonly HashSet<int> _beta = new HashSet<int>();
        public bool IsBeta(int index) => _beta.Contains(index);
        /// <summary>Shows a BETA pill after one choice: that choice has not been proven end to end (see TestStatus).</summary>
        public void SetBeta(int index, bool beta)
        {
            if (index < 0 || index >= _items.Length) return;
            if (beta ? _beta.Add(index) : _beta.Remove(index)) { Parent?.PerformLayout(); Invalidate(); }
        }

        private int ItemWidth(int i) => TextRenderer.MeasureText(_items[i], Font).Width + Theme.S(24) + (_beta.Contains(i) ? Theme.BetaWidth : 0);

        public override Size GetPreferredSize(Size proposedSize)
            => new Size(Enumerable.Range(0, _items.Length).Sum(ItemWidth) + Theme.S(6), Height);

        private int HitTest(int x)
        {
            int pos = Theme.S(3);
            for (int i = 0; i < _items.Length; i++) { int w = ItemWidth(i); if (x >= pos && x < pos + w) return i; pos += w; }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e) { int h = HitTest(e.X); if (h != _hover) { _hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e) { int h = HitTest(e.X); if (h >= 0 && Enabled) SelectedIndex = h; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(7)))
            using (var b = new SolidBrush(Theme.Input))
            using (var p = new Pen(Theme.Border)) { g.FillPath(b, path); g.DrawPath(p, path); }
            int x = Theme.S(3);
            for (int i = 0; i < _items.Length; i++)
            {
                int w = ItemWidth(i);
                var r = new RectangleF(x, Theme.S(3), w, Height - Theme.S(6));
                if (i == _selected || i == _hover)
                    using (var path = Theme.Rounded(r, Theme.S(5)))
                    using (var b = new SolidBrush(i == _selected ? Theme.Accent : Theme.SurfaceHover)) g.FillPath(b, path);
                var textRect = Rectangle.Round(r);
                if (_beta.Contains(i)) textRect.Width -= Theme.BetaWidth;
                TextRenderer.DrawText(g, _items[i], Font, textRect, i == _selected ? Theme.OnAccent : Enabled ? Theme.TextDim : Theme.TextFaint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                if (_beta.Contains(i))
                {
                    int tw = TextRenderer.MeasureText(_items[i], Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                    int textRight = textRect.X + (textRect.Width + tw) / 2;
                    Theme.DrawBeta(g, textRight - Theme.S(2), Height / 2, i == _selected ? Theme.Accent : Theme.Input);
                }
                x += w;
            }
        }
    }

    /// <summary>Dark drop-down list (closed state is custom painted; the list itself is owner-drawn).</summary>
    public class XCombo : ComboBox, IMeasure
    {
        private bool _hover;

        public XCombo()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Input;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = Theme.S(28);
            Cursor = Cursors.Hand;
            Theme.DarkScrollbars(this);
        }

        public int MeasureHeight(int width) => Height;

        /// <summary>Items (by their shown text) that carry a BETA pill: choices not proven end to end (see TestStatus).</summary>
        public HashSet<string> BetaItems { get; } = new HashSet<string>(StringComparer.Ordinal);

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }

        /// <summary>Draws an item's text, followed by a BETA pill when it is one of <see cref="BetaItems"/>.</summary>
        private void DrawItemText(Graphics g, string text, Rectangle r, Color fg, Color background)
        {
            bool beta = BetaItems.Contains(text);
            int w = r.Width;
            if (beta)
                w = Math.Max(0, Math.Min(r.Width - Theme.BetaWidth,
                    TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, r.Height), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + 2));
            TextRenderer.DrawText(g, text, Font, new Rectangle(r.X, r.Y, w, r.Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (beta) Theme.DrawBeta(g, r.X + w, r.Y + r.Height / 2, background);
        }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(6)))
            using (var b = new SolidBrush(_hover && Enabled ? Theme.SurfaceHover : Theme.Input))
            using (var p = new Pen(Focused ? Theme.Accent : Theme.Border)) { g.FillPath(b, path); g.DrawPath(p, path); }
            string text = SelectedIndex >= 0 ? GetItemText(SelectedItem) : "";
            int arrowW = Theme.S(26);
            DrawItemText(g, text, new Rectangle(Theme.S(10), 0, Width - arrowW - Theme.S(12), Height),
                Enabled ? Theme.Text : Theme.TextFaint, _hover && Enabled ? Theme.SurfaceHover : Theme.Input);
            TextRenderer.DrawText(g, UI.Glyph.ChevronDown, ChevronFont, new Rectangle(Width - arrowW - Theme.S(4), 0, arrowW, Height),
                Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }

        private static readonly Font ChevronFont = new Font(Theme.Icon.FontFamily, 8f);

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.AccentSoft : Theme.Input)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0)
                DrawItemText(e.Graphics, GetItemText(Items[e.Index]),
                    new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y, e.Bounds.Width - Theme.S(8), e.Bounds.Height),
                    Theme.Text, sel ? Theme.AccentSoft : Theme.Input);
        }
    }

    /// <summary>Two-column key/value list. Double-click a row to copy its value.</summary>
    public class KvGrid : Control, IMeasure
    {
        private readonly List<KeyValuePair<string, string>> _rows = new List<KeyValuePair<string, string>>();
        public int Columns { get; set; } = 2;
        private int RowH => Theme.S(26);
        private int KeyW => Theme.S(120);

        public KvGrid()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.Body;
            Tip.Set(this, "Double-click a value to copy it");
        }

        public void Clear() { _rows.Clear(); Parent?.PerformLayout(); Invalidate(); }

        public void Set(string key, string value)
        {
            int i = _rows.FindIndex(r => r.Key == key);
            var kv = new KeyValuePair<string, string>(key, value ?? "");
            if (i >= 0) _rows[i] = kv; else _rows.Add(kv);
            Parent?.PerformLayout();
            Invalidate();
        }

        private int Cols(int width) => width >= Theme.S(560) ? Math.Max(1, Columns) : 1;

        public int MeasureHeight(int width)
        {
            int cols = Cols(width);
            return ((_rows.Count + cols - 1) / cols) * RowH;
        }

        private int RowAt(Point p)
        {
            int cols = Cols(Width);
            int colW = Width / cols;
            int row = p.Y / RowH, col = Math.Min(cols - 1, p.X / colW);
            int idx = col * ((_rows.Count + cols - 1) / cols) + row;
            return idx < _rows.Count ? idx : -1;
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            int i = RowAt(PointToClient(MousePosition));
            if (i >= 0 && _rows[i].Value.Length > 0)
            {
                try
                {
                    Clipboard.SetText(_rows[i].Value);
                    Core.Services.Log.Info("Copied " + _rows[i].Key + ": " + _rows[i].Value);
                }
                catch { }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            int cols = Cols(Width);
            int perCol = (_rows.Count + cols - 1) / Math.Max(1, cols);
            int colW = Width / cols;
            for (int i = 0; i < _rows.Count; i++)
            {
                int col = perCol == 0 ? 0 : i / perCol, row = perCol == 0 ? 0 : i % perCol;
                int x = col * colW, y = row * RowH;
                TextRenderer.DrawText(g, _rows[i].Key, Theme.Small, new Rectangle(x, y, KeyW, RowH), Theme.TextDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                string v = string.IsNullOrEmpty(_rows[i].Value) ? "-" : _rows[i].Value;
                TextRenderer.DrawText(g, v, Theme.Body, new Rectangle(x + KeyW, y, colW - KeyW - Theme.S(10), RowH), Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                if (row > 0)
                    using (var p = new Pen(Theme.Divider)) g.DrawLine(p, x, y, x + colW - Theme.S(14), y);
            }
        }
    }

    /// <summary>Dashed drop target.</summary>
    public class DropZone : Control, IMeasure
    {
        private bool _over, _hover;
        public string Hint { get; set; } = "Drop a file here or click to browse";
        public string SubHint { get; set; } = "";
        public string GlyphText { get; set; } = UI.Glyph.Upload;
        public event Action<string[]> FilesDropped;

        public DropZone()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AllowDrop = true;
            Height = Theme.S(170);
            Cursor = Cursors.Hand;
        }

        private int _designHeight;          // the height the page asked for - the zone only grows past it when its text needs the room

        /// <summary>The small text under the title, wrapped to the zone's width.</summary>
        private int SubHeight(int width) => string.IsNullOrEmpty(SubHint) ? 0 :
            TextRenderer.MeasureText(SubHint, Theme.Small, new Size(Math.Max(1, width - Theme.S(20)), int.MaxValue), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak).Height;

        /// <summary>The icon, the title and the small text, one under the other.</summary>
        private int BlockHeight(int width) { int sub = SubHeight(width); return Theme.S(52) + Theme.S(6) + Theme.S(26) + sub; }

        public int MeasureHeight(int width)
        {
            if (_designHeight == 0) _designHeight = Height;
            return Math.Max(_designHeight, BlockHeight(width) + Theme.S(28));
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.Copy; _over = true; Invalidate(); }
        }

        protected override void OnDragLeave(EventArgs e) { _over = false; Invalidate(); }

        protected override void OnDragDrop(DragEventArgs e)
        {
            _over = false;
            Invalidate();
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) FilesDropped?.Invoke(files);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Rounded(r, Theme.S(14)))
            {
                // A hero surface: the accent glows in from the top-left corner and fades into the card color.
                Color glow = Theme.Blend(Theme.Surface, Theme.Accent, _over ? .34f : _hover ? .22f : .15f);
                using (var b = new LinearGradientBrush(r, glow, Theme.Surface, 28f))
                {
                    b.Blend = new Blend { Positions = new[] { 0f, .55f, 1f }, Factors = new[] { 0f, .85f, 1f } };
                    g.FillPath(b, path);
                }
                using (var p = new Pen(_over ? Theme.Accent : Theme.Blend(Theme.BorderStrong, Theme.Accent, _hover ? .6f : .3f), 1.5f) { DashStyle = DashStyle.Dash })
                    g.DrawPath(p, path);
            }
            // icon, title and small text as one block, centred - however many lines the small text takes
            int chip = Theme.S(52), sub = SubHeight(Width);
            int top = Math.Max(Theme.S(12), (Height - BlockHeight(Width)) / 2);
            var chipRect = new Rectangle((Width - chip) / 2, top, chip, chip);
            using (var path = Theme.Rounded(chipRect, chip / 2f))
            using (var b = new LinearGradientBrush(chipRect, Theme.AccentHover, Theme.AccentPressed, 60f))
                g.FillPath(b, path);
            TextRenderer.DrawText(g, GlyphText, Theme.IconLarge, chipRect, Theme.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var hintRect = new Rectangle(0, chipRect.Bottom + Theme.S(6), Width, Theme.S(26));
            TextRenderer.DrawText(g, Hint, Theme.CardTitle, hintRect, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (sub > 0)
                TextRenderer.DrawText(g, SubHint, Theme.Small, new Rectangle(Theme.S(10), hintRect.Bottom, Width - Theme.S(20), sub),
                    Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>Slim progress bar (determinate or marquee).</summary>
    public class XProgress : Control
    {
        private int _value;
        private bool _marquee;
        private float _phase;
        private readonly Timer _timer = new Timer { Interval = 30 };

        public XProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(6);
            _timer.Tick += (s, e) => { _phase = (_phase + 0.02f) % 1.4f; Invalidate(); };
        }

        public int Value { get => _value; set { _value = Math.Max(0, Math.Min(100, value)); Invalidate(); } }

        public bool Marquee
        {
            get => _marquee;
            set { _marquee = value; _timer.Enabled = value; Invalidate(); }
        }

        protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Height / 2f;
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), rad))
            using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, path);
            RectangleF bar;
            if (_marquee)
            {
                float w = Width * 0.3f, x = (_phase - 0.3f) * Width;
                bar = RectangleF.Intersect(new RectangleF(x, 0, w, Height - 1), new RectangleF(0, 0, Width - 1, Height - 1));
            }
            else bar = new RectangleF(0, 0, (Width - 1) * _value / 100f, Height - 1);
            if (bar.Width > 1)
                using (var path = Theme.Rounded(bar, rad))
                using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
        }
    }

    /// <summary>Shared tooltip instance.</summary>
    public static class Tip
    {
        private static readonly ToolTip Shared = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 100 };
        public static void Set(Control c, string text) => Shared.SetToolTip(c, text);
        public static string Get(Control c) => Shared.GetToolTip(c) ?? "";
    }

    /// <summary>Renders icon glyphs to bitmaps for ImageLists.</summary>
    public static class GlyphImage
    {
        public static Bitmap Render(string glyph, Color color, int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font(Theme.Icon.FontFamily, size * 0.62f, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(color))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(glyph, f, b, new RectangleF(0, 0, size, size), sf);
            }
            return bmp;
        }
    }

    /// <summary>Dark styling for ListView in details mode.</summary>
    public static class ListStyle
    {
        public static ListView Create(params (string Name, int Width)[] columns)
        {
            var lv = new DarkListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Input,
                ForeColor = Theme.Text,
                Font = Theme.Body,
                OwnerDraw = true,
                HeaderStyle = ColumnHeaderStyle.Clickable
            };
            foreach (var c in columns) lv.Columns.Add(c.Name, Theme.S(c.Width));
            Theme.DarkScrollbars(lv);
            return lv;
        }
    }

    public class DarkListView : ListView, IMeasure
    {
        public DarkListView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Height = Theme.S(260);
        }

        public int MeasureHeight(int width) => Height;

        private bool _fitting;

        /// <summary>Stretch the last column so no unpainted header area is left on the right.</summary>
        private void FitLastColumn()
        {
            if (_fitting || View != View.Details || Columns.Count == 0 || !IsHandleCreated) return;
            _fitting = true;
            try
            {
                int others = 0;
                for (int i = 0; i < Columns.Count - 1; i++) others += Columns[i].Width;
                int w = ClientSize.Width - others;
                var last = Columns[Columns.Count - 1];
                if (w > Theme.S(60) && last.Width != w) last.Width = w;
            }
            finally { _fitting = false; }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); FitLastColumn(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.ThemeListHeader(this); FitLastColumn(); }
        protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e) { base.OnColumnWidthChanged(e); if (e.ColumnIndex != Columns.Count - 1) FitLastColumn(); }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
            }
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.SmallBold,
                new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y, e.Bounds.Width - Theme.S(10), e.Bounds.Height),
                Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e) { e.DrawDefault = View != View.Details; }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            bool sel = e.Item.Selected;
            var row = e.Bounds;
            using (var b = new SolidBrush(sel ? Theme.AccentSoft : (e.ItemIndex % 2 == 0 ? Theme.Input : Theme.Blend(Theme.Input, Theme.Surface, .5f))))
                e.Graphics.FillRectangle(b, row);
            int x = row.X + Theme.S(6);
            if (e.ColumnIndex == 0 && SmallImageList != null && e.Item.ImageIndex >= 0 && e.Item.ImageIndex < SmallImageList.Images.Count)
            {
                var img = SmallImageList.Images[e.Item.ImageIndex];
                e.Graphics.DrawImage(img, x, row.Y + (row.Height - img.Height) / 2);
                x += img.Width + Theme.S(6);
            }
            var color = e.SubItem.ForeColor == SystemColors.WindowText || e.ColumnIndex == 0 ? e.Item.ForeColor : e.SubItem.ForeColor;
            if (color == SystemColors.WindowText || color.IsEmpty) color = Theme.Text;
            if (e.ColumnIndex > 0 && e.SubItem.ForeColor == e.Item.ForeColor) color = Theme.TextDim;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (e.Header.TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.ColumnIndex == 0 ? Font : Theme.Small,
                new Rectangle(x, row.Y, row.Right - x - Theme.S(6), row.Height), color, flags);
        }
    }

    /// <summary>Underlined tab strip that switches a page between sections. Keyboard: Left / Right when focused.</summary>
    public class SectionTabs : Control, IMeasure
    {
        private readonly List<(string Text, string Glyph, Control Panel)> _tabs = new List<(string, string, Control)>();
        private readonly Dictionary<int, Color> _badges = new Dictionary<int, Color>();
        private int _selected, _hover = -1;
        public event EventHandler SelectedChanged;

        public SectionTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = Theme.S(40);
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int MeasureHeight(int width) => Height;
        public int Count => _tabs.Count;
        public Control SelectedPanel => _selected < _tabs.Count ? _tabs[_selected].Panel : null;

        public void AddTab(string text, string glyph, Control panel)
        {
            _tabs.Add((text, glyph, panel));
            panel.Visible = _tabs.Count - 1 == _selected;
            Invalidate();
        }

        public int SelectedIndex
        {
            get => _selected;
            set
            {
                if (value < 0 || value >= _tabs.Count || value == _selected) return;
                _selected = value;
                Parent?.SuspendLayout();
                for (int i = 0; i < _tabs.Count; i++) _tabs[i].Panel.Visible = i == _selected;
                Parent?.ResumeLayout(true);
                Invalidate();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Select(Control panel)
        {
            int i = _tabs.FindIndex(t => t.Panel == panel);
            if (i >= 0) SelectedIndex = i;
        }

        /// <summary>Small colored dot after a tab's title (null removes it) - e.g. "needs attention".</summary>
        public void SetBadge(Control panel, Color? color)
        {
            int i = _tabs.FindIndex(t => t.Panel == panel);
            if (i < 0) return;
            if (color == null) _badges.Remove(i); else _badges[i] = color.Value;
            Invalidate();
        }

        private readonly HashSet<Control> _beta = new HashSet<Control>();

        /// <summary>Shows a BETA pill after a tab's title: nothing in that tab has been tested yet (see TestStatus).</summary>
        public void SetBeta(Control panel, bool beta)
        {
            if (beta ? _beta.Add(panel) : _beta.Remove(panel)) Invalidate();
        }

        /// <summary>The tabs as (title, panel), for code that labels them.</summary>
        public IEnumerable<(string Text, Control Panel)> Items => _tabs.Select(t => (t.Text, t.Panel));

        private int TabWidth(int i)
        {
            int w = TextRenderer.MeasureText(_tabs[i].Text, Theme.BodyBold).Width + Theme.S(28);
            if (!string.IsNullOrEmpty(_tabs[i].Glyph)) w += Theme.S(24);
            if (_badges.ContainsKey(i)) w += Theme.S(12);
            if (_beta.Contains(_tabs[i].Panel)) w += Theme.BetaWidth;
            return w;
        }

        private Rectangle TabRect(int i)
        {
            int x = 0;
            for (int j = 0; j < i; j++) x += TabWidth(j);
            return new Rectangle(x, 0, TabWidth(i), Height);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _tabs.Count; i++) if (TabRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e) { int h = HitTest(e.Location); if (h != _hover) { _hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { int h = HitTest(e.Location); if (h >= 0) { SelectedIndex = h; Focus(); } }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override bool IsInputKey(Keys k) => k == Keys.Left || k == Keys.Right || base.IsInputKey(k);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(_tabs.Count - 1, _selected + 1);
            else if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, _selected - 1);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            for (int i = 0; i < _tabs.Count; i++)
            {
                var r = TabRect(i);
                bool sel = i == _selected, hot = i == _hover;
                if (hot && !sel)
                    using (var path = Theme.Rounded(new RectangleF(r.X + 2, r.Y + Theme.S(4), r.Width - 4, r.Height - Theme.S(10)), Theme.S(6)))
                    using (var b = new SolidBrush(Theme.SurfaceHover)) g.FillPath(b, path);
                Color fg = sel ? Theme.Text : hot ? Theme.Text : Theme.TextDim;
                int x = r.X + Theme.S(14);
                if (!string.IsNullOrEmpty(_tabs[i].Glyph))
                {
                    TextRenderer.DrawText(g, _tabs[i].Glyph, Theme.Icon, new Rectangle(x, 0, Theme.S(20), Height - Theme.S(3)),
                        sel ? Theme.Accent : fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                    x += Theme.S(24);
                }
                var font = sel ? Theme.BodyBold : Theme.Body;
                int tw = TextRenderer.MeasureText(_tabs[i].Text, font).Width;
                TextRenderer.DrawText(g, _tabs[i].Text, font, new Rectangle(x, 0, tw + Theme.S(4), Height - Theme.S(3)), fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                if (_badges.TryGetValue(i, out var badge))
                    using (var b = new SolidBrush(badge))
                        g.FillEllipse(b, x + tw + Theme.S(5), Height / 2 - Theme.S(5), Theme.S(7), Theme.S(7));
                if (_beta.Contains(_tabs[i].Panel))
                    Theme.DrawBeta(g, x + tw + (_badges.ContainsKey(i) ? Theme.S(12) : 0), (Height - Theme.S(3)) / 2, Parent?.BackColor ?? Theme.Window);
                if (sel)
                    using (var path = Theme.Rounded(new RectangleF(r.X + Theme.S(10), Height - Theme.S(3), r.Width - Theme.S(20), Theme.S(3)), 1.5f * Theme.DpiScale))
                    using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                if (sel && Focused && ShowFocusCues)
                    using (var p = new Pen(Theme.BorderStrong) { DashStyle = DashStyle.Dot })
                        g.DrawRectangle(p, r.X + 4, Theme.S(6), r.Width - 9, Height - Theme.S(14));
            }
        }
    }

    /// <summary>Centered icon + message shown in place of an empty list ("Not connected", "No saves found"...).</summary>
    public class EmptyState : Control
    {
        private string _glyph, _title, _detail;

        public EmptyState(string glyph, string title, string detail = null)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _glyph = glyph; _title = title; _detail = detail;
            BackColor = Theme.Input;
        }

        public void Set(string glyph, string title, string detail = null)
        {
            _glyph = glyph; _title = title; _detail = detail;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            int cy = Height / 2 - Theme.S(30);
            TextRenderer.DrawText(g, _glyph, Theme.IconLarge, new Rectangle(0, cy - Theme.S(40), Width, Theme.S(44)), Theme.TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, _title, Theme.CardTitle, new Rectangle(Theme.S(16), cy + Theme.S(8), Width - Theme.S(32), Theme.S(26)), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (!string.IsNullOrEmpty(_detail))
                TextRenderer.DrawText(g, _detail, Theme.Body, new Rectangle(Theme.S(40), cy + Theme.S(38), Width - Theme.S(80), Theme.S(60)), Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}
