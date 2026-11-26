using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>A clickable miniature of a theme (window, sidebar, card, accent button) with its name.</summary>
    public sealed class ThemeTile : Control
    {
        public ThemePalette Palette { get; }
        private bool _hover, _selected, _active;
        public event EventHandler Picked;
        public event EventHandler Activated;

        public ThemeTile(ThemePalette palette)
        {
            Palette = palette;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(Theme.S(188), Theme.S(132));
            Cursor = Cursors.Hand;
            Tip.Set(this, palette.Name + (palette.BuiltIn ? " (built-in)" : " (yours)") + " - double-click to apply");
        }

        public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }
        public bool IsActive { get => _active; set { _active = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { Picked?.Invoke(this, EventArgs.Empty); }
        protected override void OnDoubleClick(EventArgs e) { Activated?.Invoke(this, EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var p = Palette;
            var outer = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Rounded(outer, Theme.S(10)))
            {
                using (var b = new SolidBrush(_hover ? Theme.SurfaceHover : Theme.Input)) g.FillPath(b, path);
                using (var pen = new Pen(_selected ? Theme.Accent : Theme.Border, _selected ? 2f : 1f)) g.DrawPath(pen, path);
            }
            // miniature window
            var win = new RectangleF(Theme.S(10), Theme.S(10), Width - Theme.S(21), Theme.S(78));
            using (var path = Theme.Rounded(win, Theme.S(6)))
            using (var b = new SolidBrush(p.Get("Window"))) g.FillPath(b, path);
            using (var b = new SolidBrush(p.Get("Sidebar"))) g.FillRectangle(b, win.X + 1, win.Y + 1, Theme.S(36), win.Height - 2);
            using (var b = new SolidBrush(p.Get("Accent"))) g.FillRectangle(b, win.X + Theme.S(6), win.Y + Theme.S(12), Theme.S(3), Theme.S(10));
            for (int i = 0; i < 4; i++)
                using (var b = new SolidBrush(i == 0 ? p.Get("Text") : p.Get("TextFaint")))
                    g.FillRectangle(b, win.X + Theme.S(12), win.Y + Theme.S(14 + i * 14), Theme.S(18), Theme.S(4));
            var card = new RectangleF(win.X + Theme.S(46), win.Y + Theme.S(10), win.Width - Theme.S(54), win.Height - Theme.S(20));
            using (var path = Theme.Rounded(card, Theme.S(5)))
            using (var b = new SolidBrush(p.Get("Surface")))
            using (var pen = new Pen(p.Get("Border"))) { g.FillPath(b, path); g.DrawPath(pen, path); }
            using (var b = new SolidBrush(p.Get("Text"))) g.FillRectangle(b, card.X + Theme.S(8), card.Y + Theme.S(9), Theme.S(52), Theme.S(5));
            using (var b = new SolidBrush(p.Get("TextDim"))) g.FillRectangle(b, card.X + Theme.S(8), card.Y + Theme.S(20), Theme.S(74), Theme.S(4));
            var btn = new RectangleF(card.X + Theme.S(8), card.Bottom - Theme.S(22), Theme.S(46), Theme.S(14));
            using (var path = Theme.Rounded(btn, Theme.S(3)))
            using (var b = new SolidBrush(p.Get("Accent"))) g.FillPath(b, path);
            var btn2 = new RectangleF(btn.Right + Theme.S(6), btn.Y, Theme.S(36), btn.Height);
            using (var path = Theme.Rounded(btn2, Theme.S(3)))
            using (var b = new SolidBrush(p.Get("Input")))
            using (var pen = new Pen(p.Get("Border"))) { g.FillPath(b, path); g.DrawPath(pen, path); }
            // name + badges
            var nameRect = new Rectangle(Theme.S(12), Theme.S(94), Width - Theme.S(24), Theme.S(20));
            TextRenderer.DrawText(g, p.Name, Theme.BodyBold, nameRect, Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            string sub = (_active ? "● In use   " : "") + (p.Light ? "Light" : "Dark") + (p.BuiltIn ? "" : "  •  yours");
            TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(Theme.S(12), Theme.S(112), Width - Theme.S(24), Theme.S(16)),
                _active ? Theme.Success : Theme.TextDim, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Label + color swatch + hex; click to pick a color.</summary>
    public sealed class ColorSlot : Control
    {
        private Color _color;
        private bool _hover;
        public string Key { get; }
        public event EventHandler ColorChanged;

        public ColorSlot(string key, string label, Color color)
        {
            Key = key;
            Text = label;
            _color = color;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(Theme.S(250), Theme.S(32));
            Cursor = Cursors.Hand;
            Tip.Set(this, "Click to change " + label.ToLowerInvariant());
        }

        public Color Value { get => _color; set { _color = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }

        protected override void OnClick(EventArgs e)
        {
            using (var d = new ColorDialog { Color = _color, FullOpen = true, AnyColor = true })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                _color = Color.FromArgb(d.Color.R, d.Color.G, d.Color.B);
            }
            Invalidate();
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_hover)
                using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(6)))
                using (var b = new SolidBrush(Theme.SurfaceHover)) g.FillPath(b, path);
            var sw = new RectangleF(Theme.S(6), Theme.S(5), Theme.S(34), Height - Theme.S(10));
            using (var path = Theme.Rounded(sw, Theme.S(5)))
            using (var b = new SolidBrush(_color))
            using (var pen = new Pen(Theme.BorderStrong)) { g.FillPath(b, path); g.DrawPath(pen, path); }
            TextRenderer.DrawText(g, Text, Theme.Body, new Rectangle(Theme.S(48), 0, Width - Theme.S(130), Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, ThemePalette.ToHex(_color), Theme.Mono, new Rectangle(Width - Theme.S(82), 0, Theme.S(76), Height), Theme.TextDim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Live preview of a palette that is being edited (independent of the app's current theme).</summary>
    public sealed class ThemePreview : Control, IMeasure
    {
        private ThemePalette _p;

        public ThemePreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(220);
        }

        public int MeasureHeight(int width) => Height;
        public void ShowPalette(ThemePalette p) { _p = p; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            if (_p == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color C(string k) => _p.Get(k);
            var frame = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Rounded(frame, Theme.S(10)))
            using (var b = new SolidBrush(C("Window")))
            using (var pen = new Pen(C("Border"))) { g.FillPath(b, path); g.DrawPath(pen, path); }
            int side = Theme.S(150);
            using (var b = new SolidBrush(C("Sidebar"))) g.FillRectangle(b, 1, 1, side, Height - 2);
            string[] nav = { "Home", "XEX Tool", "Saves", "Neighborhood" };
            for (int i = 0; i < nav.Length; i++)
            {
                var r = new RectangleF(Theme.S(8), Theme.S(14 + i * 36), side - Theme.S(16), Theme.S(30));
                if (i == 2)
                {
                    using (var path = Theme.Rounded(r, Theme.S(6)))
                    using (var b = new SolidBrush(C("AccentSoft"))) g.FillPath(b, path);
                    using (var b = new SolidBrush(C("Accent"))) g.FillRectangle(b, r.X, r.Y + Theme.S(8), Theme.S(3), r.Height - Theme.S(16));
                }
                TextRenderer.DrawText(g, nav[i], i == 2 ? Theme.BodyBold : Theme.Body, Rectangle.Round(new RectangleF(r.X + Theme.S(14), r.Y, r.Width, r.Height)),
                    i == 2 ? C("Text") : C("TextDim"), TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            int x = side + Theme.S(18);
            TextRenderer.DrawText(g, "Saves", Theme.CardTitle, new Point(x, Theme.S(12)), C("Text"));
            TextRenderer.DrawText(g, "Secondary text looks like this", Theme.Small, new Point(x, Theme.S(36)), C("TextDim"));
            var card = new RectangleF(x, Theme.S(60), Width - x - Theme.S(16), Height - Theme.S(76));
            using (var path = Theme.Rounded(card, Theme.S(8)))
            using (var b = new SolidBrush(C("Surface")))
            using (var pen = new Pen(C("Border"))) { g.FillPath(b, path); g.DrawPath(pen, path); }
            int cx = (int)card.X + Theme.S(14), cy = (int)card.Y + Theme.S(12);
            TextRenderer.DrawText(g, "Sign for retail", Theme.BodyBold, new Point(cx, cy), C("Text"));
            var input = new RectangleF(cx, cy + Theme.S(28), Math.Min(Theme.S(220), card.Width - Theme.S(28)), Theme.S(28));
            using (var path = Theme.Rounded(input, Theme.S(5)))
            using (var b = new SolidBrush(C("Input")))
            using (var pen = new Pen(C("Border"))) { g.FillPath(b, path); g.DrawPath(pen, path); }
            TextRenderer.DrawText(g, "E000123456789ABC", Theme.Mono, Rectangle.Round(new RectangleF(input.X + Theme.S(8), input.Y, input.Width, input.Height)), C("Text"), TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var b1 = new RectangleF(cx, cy + Theme.S(66), Theme.S(96), Theme.S(28));
            using (var path = Theme.Rounded(b1, Theme.S(5)))
            using (var b = new SolidBrush(C("Accent"))) g.FillPath(b, path);
            TextRenderer.DrawText(g, "Resign", Theme.Body, Rectangle.Round(b1), ThemePalette.OnColor(C("Accent")), TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            var b2 = new RectangleF(b1.Right + Theme.S(8), b1.Y, Theme.S(80), b1.Height);
            using (var path = Theme.Rounded(b2, Theme.S(5)))
            using (var b = new SolidBrush(C("Input")))
            using (var pen = new Pen(C("Border"))) { g.FillPath(b, path); g.DrawPath(pen, path); }
            TextRenderer.DrawText(g, "Cancel", Theme.Body, Rectangle.Round(b2), C("Text"), TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            int chipX = cx;
            foreach (var (k, t) in new[] { ("Success", "Valid"), ("Warning", "Warning"), ("Danger", "Error"), ("Info", "Info") })
            {
                TextRenderer.DrawText(g, "● " + t, Theme.Small, new Point(chipX, (int)b1.Bottom + Theme.S(10)), C(k));
                chipX += Theme.S(72);
            }
        }
    }
}
