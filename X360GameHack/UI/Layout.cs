using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace X360GameHack.UI
{
    /// <summary>Controls that can report their height for a given width.</summary>
    public interface IMeasure
    {
        int MeasureHeight(int width);
    }

    internal static class Measure
    {
        public static int Height(Control c, int width)
        {
            if (c is IMeasure m) return m.MeasureHeight(width);
            if (c is Label lbl && lbl.AutoSize) return c.GetPreferredSize(new Size(width, 0)).Height;
            if (c is CheckBox cb && cb.AutoSize) return c.GetPreferredSize(Size.Empty).Height;
            return c.Height;
        }

        public static int Width(Control c)
        {
            int w = c is XButton || c is XCheck || c is Label || c is Segmented ? c.GetPreferredSize(Size.Empty).Width : c.Width;
            return Math.Max(w, c.MinimumSize.Width);       // else a control wider than its slot runs past the row's edge
        }

        public static List<Control> Visible(Control parent)
        {
            var list = new List<Control>();
            // Controls are kept in add order (we never use BringToFront on stacked children).
            foreach (Control c in parent.Controls) if (c.Visible) list.Add(c);
            return list;
        }
    }

    /// <summary>
    /// A stack that scrolls when the room it gets is shorter than its content (it measures as tall as its content).
    /// Used as the FillChild of a window's stack so what comes after it - a question, the buttons - always stays on screen.
    /// </summary>
    public sealed class ScrollArea : Panel, IMeasure
    {
        public VStack Content { get; }

        public ScrollArea(VStack content)
        {
            Content = content;
            AutoScroll = true;
            content.AutoHeight = true;
            content.Location = Point.Empty;
            Controls.Add(content);
            Theme.DarkScrollbars(this);
            Resize += (s, e) => FitWidth();
            ClientSizeChanged += (s, e) => FitWidth();
        }

        public int MeasureHeight(int width) => Content.MeasureHeight(width);

        private void FitWidth()
        {
            int w = ClientSize.Width;
            if (w > 0 && Content.Width != w) Content.Width = w;
        }
    }

    /// <summary>Vertical stack. Children span the full width; one optional child fills leftover height.</summary>
    public class VStack : Panel, IMeasure
    {
        private Control _fill;
        public int Spacing { get; set; } = Theme.S(12);

        /// <summary>When true (default) the stack sizes its own height to its content.</summary>
        public bool AutoHeight { get; set; } = true;

        /// <summary>The child that receives the remaining height (only when AutoHeight is false).</summary>
        public Control FillChild
        {
            get => _fill;
            set { _fill = value; PerformLayout(); }
        }

        /// <summary>Set by a parent that decides our height itself (e.g. <see cref="Columns"/>).</summary>
        internal bool ExternalHeight { get; set; }
        private int _lastWant = -1;
        public event EventHandler ContentHeightChanged;

        public VStack()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected virtual int HeaderHeight => 0;

        /// <summary>Children that take part in the vertical stacking.</summary>
        protected virtual List<Control> Stacked() => Measure.Visible(this);

        public virtual int MeasureHeight(int width)
        {
            int inner = width - Padding.Horizontal;
            var kids = Stacked();
            int h = Padding.Vertical + HeaderHeight;
            for (int i = 0; i < kids.Count; i++)
            {
                h += kids[i] == _fill && !AutoHeight ? Theme.S(120) : Measure.Height(kids[i], inner);
                if (i > 0) h += Spacing;
            }
            return h;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            int inner = ClientSize.Width - Padding.Horizontal;
            if (inner <= 0) return;
            var kids = Stacked();
            var heights = new int[kids.Count];
            int used = 0;
            for (int i = 0; i < kids.Count; i++)
            {
                if (kids[i] == _fill && !AutoHeight) continue;
                heights[i] = Measure.Height(kids[i], inner);
                used += heights[i];
            }
            used += Math.Max(0, kids.Count - 1) * Spacing;
            int top = Padding.Top + HeaderHeight;
            int fillH = Math.Max(Theme.S(60), ClientSize.Height - Padding.Vertical - HeaderHeight - used);
            int y = top;
            for (int i = 0; i < kids.Count; i++)
            {
                int h = kids[i] == _fill && !AutoHeight ? fillH : heights[i];
                kids[i].SetBounds(Padding.Left, y, inner, h);
                y += h + Spacing;
            }
            if (AutoHeight)
            {
                int want = (kids.Count == 0 ? top : y - Spacing) + Padding.Bottom;
                if (ExternalHeight)
                {
                    // A Columns grid owns our height (equal row heights); just tell it the content changed.
                    if (want != _lastWant) { _lastWant = want; ContentHeightChanged?.Invoke(this, EventArgs.Empty); }
                }
                else if (Height != want) Height = want;
            }
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            e.Control.VisibleChanged += ChildChanged;
            e.Control.SizeChanged += ChildSizeChanged;
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            base.OnControlRemoved(e);
            e.Control.VisibleChanged -= ChildChanged;
            e.Control.SizeChanged -= ChildSizeChanged;
        }

        private void ChildChanged(object sender, EventArgs e) => PerformLayout();

        private void ChildSizeChanged(object sender, EventArgs e)
        {
            // A nested auto-height stack grew or shrank: re-flow.
            if (sender is VStack || sender is Columns || sender is WrapPanel) PerformLayout();
        }

        protected override void OnPaddingChanged(EventArgs e) { base.OnPaddingChanged(e); PerformLayout(); }

        public T Add<T>(T c) where T : Control { Controls.Add(c); return c; }
    }

    /// <summary>Horizontal row. Children keep their width; one optional child stretches.</summary>
    public class HStack : Panel, IMeasure
    {
        public int Spacing { get; set; } = Theme.S(8);
        public Control FillChild { get; set; }
        public bool RightAlign { get; set; }

        public HStack()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            Height = Theme.S(34);
        }

        /// <summary>Rows have an explicit height (set Height); children are centred inside it.</summary>
        public int MeasureHeight(int width) => Height;

        // Remember each child's own width so a child that was once stretched keeps its natural size later.
        private readonly Dictionary<Control, int> _natural = new Dictionary<Control, int>();
        private readonly Dictionary<Control, int> _assigned = new Dictionary<Control, int>();

        private int NaturalWidth(Control c)
        {
            if (c is XButton || c is XCheck || c is Segmented) return Measure.Width(c);
            if (!_assigned.TryGetValue(c, out int a) || c.Width != a || !_natural.ContainsKey(c)) _natural[c] = c.Width;
            return _natural[c];
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            _natural[e.Control] = e.Control.Width;
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            base.OnControlRemoved(e);
            _natural.Remove(e.Control);
            _assigned.Remove(e.Control);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            var kids = Measure.Visible(this);
            if (kids.Count == 0) return;
            int avail = ClientSize.Width - Padding.Horizontal;
            var widths = kids.Select(c => c == FillChild ? 0 : NaturalWidth(c)).ToArray();
            int used = widths.Sum() + Spacing * (kids.Count - 1);
            int fillW = Math.Max(Theme.S(40), avail - used);
            int rowH = ClientSize.Height - Padding.Vertical;
            int x = Padding.Left;
            if (RightAlign && FillChild == null) x = Math.Max(Padding.Left, ClientSize.Width - Padding.Right - used);
            for (int i = 0; i < kids.Count; i++)
            {
                int w = kids[i] == FillChild ? fillW : widths[i];
                int h = Math.Min(rowH, kids[i] is IMeasure m && !(kids[i] is HStack) && !(kids[i] is XTextBox) ? m.MeasureHeight(w) : kids[i].Height);
                kids[i].SetBounds(x, Padding.Top + (rowH - h) / 2, w, h);
                _assigned[kids[i]] = kids[i].Width;
                x += w + Spacing;
            }
        }

        public T Add<T>(T c) where T : Control { Controls.Add(c); return c; }
    }

    /// <summary>Left-to-right flow that wraps to new lines (button bars, chips).</summary>
    public class WrapPanel : Panel, IMeasure
    {
        public int HGap { get; set; } = Theme.S(8);
        public int VGap { get; set; } = Theme.S(8);

        public WrapPanel() { SetStyle(ControlStyles.OptimizedDoubleBuffer, true); }

        private readonly Dictionary<Control, int> _natural = new Dictionary<Control, int>();
        private readonly Dictionary<Control, int> _assigned = new Dictionary<Control, int>();

        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); _natural[e.Control] = e.Control.Width; }
        protected override void OnControlRemoved(ControlEventArgs e) { base.OnControlRemoved(e); _natural.Remove(e.Control); _assigned.Remove(e.Control); }

        private int NaturalWidth(Control c)
        {
            if (c is XButton || c is XCheck || c is Segmented || c is Label) return Measure.Width(c);
            if (_assigned.TryGetValue(c, out int a) && c.Width != a) _natural[c] = c.Width;   // resized by code
            return _natural.TryGetValue(c, out int n) ? n : c.Width;
        }

        private int Flow(int width, bool apply)
        {
            int x = 0, y = 0, lineH = 0;
            foreach (var c in Measure.Visible(this))
            {
                int w = Math.Min(NaturalWidth(c), Math.Max(1, width));
                int h = c.Height;
                if (x > 0 && x + w > width) { x = 0; y += lineH + VGap; lineH = 0; }
                if (apply) { c.SetBounds(x, y, w, h); _assigned[c] = c.Width; }
                x += w + HGap;
                lineH = Math.Max(lineH, h);
            }
            return y + lineH;
        }

        public int MeasureHeight(int width) => Flow(width, false);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            int h = Flow(ClientSize.Width, true);
            if (Height != h) Height = h;
        }

        public T Add<T>(T c) where T : Control { Controls.Add(c); return c; }
    }

    /// <summary>Responsive grid of cards: as many columns as fit (min column width), equal row heights.</summary>
    public class Columns : Panel, IMeasure
    {
        public int Gap { get; set; } = Theme.S(14);
        public int MinColumnWidth { get; set; } = Theme.S(380);
        public int MaxColumns { get; set; } = 2;

        public Columns() { SetStyle(ControlStyles.OptimizedDoubleBuffer, true); }

        private int ColumnCount(int width)
        {
            int n = Math.Max(1, (width + Gap) / (MinColumnWidth + Gap));
            return Math.Min(n, MaxColumns);
        }

        private int Flow(int width, bool apply)
        {
            var kids = Measure.Visible(this);
            int cols = ColumnCount(width);
            int colW = (width - Gap * (cols - 1)) / cols;
            int y = 0;
            for (int i = 0; i < kids.Count; i += cols)
            {
                int rowH = 0;
                for (int j = i; j < Math.Min(i + cols, kids.Count); j++)
                    rowH = Math.Max(rowH, Measure.Height(kids[j], colW));
                if (apply)
                    for (int j = i; j < Math.Min(i + cols, kids.Count); j++)
                        kids[j].SetBounds((j - i) * (colW + Gap), y, colW, rowH);
                y += rowH + Gap;
            }
            return Math.Max(0, y - Gap);
        }

        public int MeasureHeight(int width) => Flow(width, false);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            int h = Flow(ClientSize.Width, true);
            if (Height != h) Height = h;
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control is VStack v)
            {
                v.ExternalHeight = true;
                v.ContentHeightChanged += (s, a) => PerformLayout();
            }
            else e.Control.SizeChanged += (s, a) => { if (s is IMeasure) PerformLayout(); };
            e.Control.VisibleChanged += (s, a) => PerformLayout();
        }

        public T Add<T>(T c) where T : Control { Controls.Add(c); return c; }
    }
}
