using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;

namespace X360GameHack.UI
{
    /// <summary>
    /// Drive Explorer's Partitions tab: every partition of the drive that is open. A map of the whole drive first (each
    /// partition's share of it, space no partition uses too), then a row per partition: its name and label, size, how much is
    /// free, file system and cluster size, and where it starts. A place the drive's layout has a partition that cannot be read
    /// says why (not formatted, the console's cache). Clicking a partition opens it in Files. Only reads (headers, and each
    /// partition's table to count its free space).
    /// </summary>
    public sealed class PartitionsView : VStack
    {
        private readonly TextBlock _summary = new TextBlock("", Theme.Body, Theme.TextDim) { Height = Theme.S(22) };
        private readonly DriveMap _map;
        private readonly VStack _rows = new VStack { Spacing = Theme.S(8), Padding = new Padding(0) };
        private List<PartitionSlot> _slots = new List<PartitionSlot>();
        private readonly Dictionary<FatxVolume, long> _free = new Dictionary<FatxVolume, long>();
        private FatxVolume _current;
        private int _gen;

        /// <summary>A partition was clicked (or chosen with Enter): open it in Files.</summary>
        public event Action<FatxVolume> OpenRequested;

        public PartitionsView()
        {
            Spacing = Theme.S(10);
            Padding = new Padding(0, Theme.S(6), Theme.S(6), Theme.S(10));
            _map = new DriveMap(this);
            Add(_summary);
            Add(_map);
            Add(_rows);
            SetDrive("", null, null);
        }

        public IReadOnlyList<PartitionSlot> Slots => _slots;
        public string Summary => _summary.Text;

        /// <summary>Free bytes counted for a partition: -1 while still counting, -2 when it could not be counted.</summary>
        public long FreeOf(FatxVolume v) => v != null && _free.TryGetValue(v, out long f) ? f : -1;

        /// <summary>Shows the partitions of the drive that opened (none: nothing open). Reads in the background.</summary>
        public async void SetDrive(string driveName, List<FatxVolume> vols, FatxVolume current)
        {
            int gen = ++_gen;
            _current = current;
            _free.Clear();
            _slots = new List<PartitionSlot>();
            if (vols == null || vols.Count == 0)
            {
                _summary.Text = "No drive open. Open a drive image or a physical Xbox drive above - every partition on it is listed here.";
                Rebuild();
                return;
            }
            _summary.Text = "Reading the partitions of " + driveName + "...";
            Rebuild();
            var list = vols.ToList();
            List<PartitionSlot> slots;
            try { slots = await Task.Run(() => FatxVolume.Layout(list)); }
            catch (Exception ex)
            {
                if (gen == _gen && !IsDisposed) _summary.Text = "The partitions could not be read: " + ex.Message;
                return;
            }
            if (gen != _gen || IsDisposed) return;
            _slots = slots;
            int read = slots.Count(s => s.Volume != null), unreadable = slots.Count(s => s.Volume == null && !s.IsUnused && !s.IsCache);
            long end = slots.Count == 0 ? 0 : slots.Max(s => s.Offset + s.Size), unused = slots.Where(s => s.IsUnused).Sum(s => s.Size);
            _summary.Text = driveName + "  -  " + FileSniffer.FormatSize(end) + "  -  " + read + " partition" + (read == 1 ? "" : "s") + " read" +
                            (unreadable > 0 ? ", " + unreadable + " that cannot be read" : "") + (unused > 0 ? ", " + FileSniffer.FormatSize(unused) + " in no partition" : "") +
                            ".  Click one to open it.";
            Rebuild();
            // the free space of each, one after another (each reads its partition's table)
            foreach (var sl in slots.Where(x => x.Volume != null).ToList())
            {
                long free;
                try { free = await Task.Run(() => sl.Volume.CountFreeBytes()); }
                catch (Exception) { free = -2; }
                if (gen != _gen || IsDisposed) return;
                _free[sl.Volume] = free;
                foreach (var r in _rows.Controls.OfType<PartitionRow>().Where(r => r.Slot == sl)) r.Refresh_();
                _map.Invalidate();
            }
        }

        /// <summary>The partition shown in Files (outlined here).</summary>
        public void SetCurrent(FatxVolume v)
        {
            _current = v;
            foreach (var r in _rows.Controls.OfType<PartitionRow>()) r.Invalidate();
            _map.Invalidate();
        }

        private void Rebuild()
        {
            SuspendLayout();
            var old = _rows.Controls.Cast<Control>().ToList();
            _rows.Controls.Clear();
            foreach (var c in old) { Tip.Set(c, null); c.Dispose(); }       // the shared tooltip lets go of the row (and its drive)
            foreach (var s in _slots) _rows.Add(new PartitionRow(this, s));
            _map.Visible = _slots.Count > 0;
            ResumeLayout();
            PerformLayout();
            _map.Invalidate();
        }

        internal void Activate(PartitionSlot s)
        {
            if (s.Volume != null) { OpenRequested?.Invoke(s.Volume); return; }
            Dlg.Info(s.Name, s.Note + ".\n\n" + FileSniffer.FormatSize(s.Size) + " at 0x" + s.Offset.ToString("X") + " (sector " + (s.Offset / 512).ToString("N0") + ").");
        }

        /// <summary>What a place is, in one line: size, file system and clusters, where it starts - or what is there instead.</summary>
        internal static string Details(PartitionSlot s)
        {
            if (s.Volume == null) return s.Note + "  ·  " + FileSniffer.FormatSize(s.Size) + " at 0x" + s.Offset.ToString("X");
            var v = s.Volume;
            return FileSniffer.FormatSize(s.Size) + "  ·  " + (v.BigEndian ? "XTAF (Xbox 360)" : "FATX (original Xbox)") + ", " + (v.ClusterSize / 1024) + " KB clusters  ·  starts at 0x" + s.Offset.ToString("X");
        }

        internal Color ColorOf(PartitionSlot s, bool hover = false)
        {
            Color c = s.Volume != null ? (s.Volume == _current ? Theme.Accent : Theme.Blend(Theme.Accent, Theme.Surface, .5f))
                    : s.IsUnused ? Theme.Input
                    : s.IsCache ? Theme.Blend(Theme.TextDim, Theme.Surface, .72f)
                    : Theme.Blend(Theme.Warning, Theme.Surface, .55f);
            return hover ? Theme.Blend(c, Theme.Text, .12f) : c;
        }

        internal Color TextOn(PartitionSlot s) => s.Volume != null && s.Volume == _current ? Theme.OnAccent : Theme.Text;
        internal bool IsCurrent(PartitionSlot s) => s.Volume != null && s.Volume == _current;

        // ------------------------------------------------------------------ the map of the whole drive

        private sealed class DriveMap : Control
        {
            private readonly PartitionsView _view;
            private readonly List<(RectangleF R, PartitionSlot S)> _hit = new List<(RectangleF, PartitionSlot)>();
            private PartitionSlot _hover;
            private readonly ToolTip _tip = new ToolTip { InitialDelay = 200, ReshowDelay = 50, AutoPopDelay = 15000 };

            public DriveMap(PartitionsView view)
            {
                _view = view;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Height = Theme.S(46);
                AccessibleRole = AccessibleRole.Graphic;
                AccessibleName = "Map of the drive's partitions";
            }

            protected override void Dispose(bool disposing) { if (disposing) _tip.Dispose(); base.Dispose(disposing); }

            /// <summary>Each place's width: its share of the drive, but never narrower than a badge - small ones stay visible.</summary>
            private List<float> Widths(Graphics g, List<PartitionSlot> slots, float room)
            {
                // wide enough for the widest label ("HDDX"), never narrower than a badge
                float min = Math.Max(Theme.S(34), slots.Where(x => !x.IsUnused).Select(x => TextRenderer.MeasureText(g, x.Tag, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.S(12))
                                                      .DefaultIfEmpty(0).Max());
                var w = new float[slots.Count];
                var small = new bool[slots.Count];
                for (int pass = 0; pass < 4; pass++)
                {
                    double bigTotal = 0; int smallCount = 0;
                    for (int i = 0; i < slots.Count; i++) { if (small[i]) smallCount++; else bigTotal += Math.Max(1, slots[i].Size); }
                    float left = Math.Max(0, room - smallCount * min);
                    bool changed = false;
                    for (int i = 0; i < slots.Count; i++)
                    {
                        if (small[i]) { w[i] = min; continue; }
                        w[i] = bigTotal <= 0 ? min : (float)(left * Math.Max(1, slots[i].Size) / bigTotal);
                        if (w[i] < min) { small[i] = true; changed = true; }
                    }
                    if (!changed) break;
                }
                float sum = w.Sum();
                if (sum > room && sum > 0) for (int i = 0; i < w.Length; i++) w[i] *= room / sum;     // more places than room: all shrink
                return w.ToList();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent?.BackColor ?? Theme.Window);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                _hit.Clear();
                var slots = _view._slots;
                if (slots.Count == 0) return;
                float gap = Theme.S(3), room = Width - 1 - gap * (slots.Count - 1);
                var widths = Widths(g, slots, room);
                float x = 0, h = Height - 1;
                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    var r = new RectangleF(x, 0, Math.Max(1, widths[i]), h);
                    _hit.Add((r, s));
                    using (var path = Theme.Rounded(r, Theme.S(6)))
                    {
                        using (var b = new SolidBrush(_view.ColorOf(s, s == _hover))) g.FillPath(b, path);
                        if (s.IsUnused)
                            using (var p = new Pen(Theme.BorderStrong) { DashStyle = DashStyle.Dash }) g.DrawPath(p, path);
                        else if (_view.IsCurrent(s))
                            using (var p = new Pen(Theme.Blend(Theme.Accent, Theme.Text, .35f), Theme.S(2))) g.DrawPath(p, path);
                    }
                    // a bar along the bottom: how full the partition is
                    long free = _view.FreeOf(s.Volume);
                    if (s.Volume != null && free >= 0 && s.Size > 0 && r.Width > Theme.S(12))
                    {
                        float used = (float)Math.Max(0, Math.Min(1, 1 - (double)free / s.Size));
                        var bar = new RectangleF(r.X + Theme.S(5), r.Bottom - Theme.S(8), (r.Width - Theme.S(10)) * used, Theme.S(3));
                        if (bar.Width >= 1)
                            using (var b = new SolidBrush(Color.FromArgb(150, _view.TextOn(s)))) g.FillRectangle(b, bar);
                    }
                    string label = s.IsUnused ? "" : s.Tag;
                    if (label.Length > 0)
                    {
                        var size = TextRenderer.MeasureText(g, label, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding);
                        if (size.Width + Theme.S(6) <= r.Width)
                            TextRenderer.DrawText(g, label, Theme.SmallBold, Rectangle.Round(new RectangleF(r.X, r.Y, r.Width, r.Height - Theme.S(4))), _view.TextOn(s),
                                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    }
                    x += widths[i] + gap;
                }
            }

            private PartitionSlot At(Point p) => _hit.FirstOrDefault(h => h.R.Contains(p)).S;

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                var s = At(e.Location);
                if (s == _hover) return;
                _hover = s;
                Cursor = s?.Volume != null ? Cursors.Hand : Cursors.Default;
                _tip.SetToolTip(this, s == null ? "" : s.Name + "\n" + Details(s));
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = null; Invalidate(); }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                var s = At(e.Location);
                if (s != null && e.Button == MouseButtons.Left) _view.Activate(s);
            }
        }

        // ------------------------------------------------------------------ one row per place

        private sealed class PartitionRow : Control
        {
            private readonly PartitionsView _view;
            public readonly PartitionSlot Slot;
            private bool _hover;

            public PartitionRow(PartitionsView view, PartitionSlot slot)
            {
                _view = view; Slot = slot;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                Height = Theme.S(62);
                TabStop = true;
                Cursor = slot.Volume != null ? Cursors.Hand : Cursors.Default;
                AccessibleRole = AccessibleRole.ListItem;
                AccessibleName = slot.Name;
                AccessibleDescription = Details(slot);
                Tip.Set(this, slot.Name + "\n" + Details(slot) + (slot.Volume != null ? "\n\nClick to open it in Files." : ""));
            }

            public void Refresh_() { AccessibleDescription = Details(Slot) + FreeText(); Invalidate(); }

            private string FreeText()
            {
                if (Slot.Volume == null) return "";
                long free = _view.FreeOf(Slot.Volume);
                return free == -1 ? "counting free space..." : free < 0 ? "free space could not be counted" : FileSniffer.FormatSize(free) + " free of " + FileSniffer.FormatSize(Slot.Size);
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                if (e.Button != MouseButtons.Left) return;
                Focus();
                _view.Activate(Slot);
            }

            protected override bool IsInputKey(Keys keyData) => keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; _view.Activate(Slot); }
                else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) { e.Handled = true; Parent?.SelectNextControl(this, e.KeyCode == Keys.Down, true, false, false); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent?.BackColor ?? Theme.Window);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool current = _view.IsCurrent(Slot), live = Slot.Volume != null;
                var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                using (var path = Theme.Rounded(box, Theme.S(8)))
                {
                    using (var b = new SolidBrush(_hover && live ? Theme.SurfaceHover : Theme.Surface)) g.FillPath(b, path);
                    using (var p = new Pen(current ? Theme.Accent : Focused ? Theme.BorderStrong : Theme.Border, current || Focused ? Theme.S(2) : 1)) g.DrawPath(p, path);
                }
                // the badge: drive letter or short code
                var badge = new RectangleF(Theme.S(12), (Height - Theme.S(40)) / 2f, Theme.S(48), Theme.S(40));
                using (var path = Theme.Rounded(badge, Theme.S(6)))
                {
                    using (var b = new SolidBrush(_view.ColorOf(Slot))) g.FillPath(b, path);
                    if (Slot.IsUnused) using (var p = new Pen(Theme.BorderStrong) { DashStyle = DashStyle.Dash }) g.DrawPath(p, path);
                }
                string tag = Slot.IsUnused ? "-" : Slot.Tag;
                TextRenderer.DrawText(g, tag, tag.Length <= 2 ? Theme.CardTitle : Theme.SmallBold, Rectangle.Round(badge), _view.TextOn(Slot),
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                int x = Theme.S(74), right = live ? Theme.S(250) : Theme.S(14);
                int textW = Math.Max(Theme.S(40), Width - x - right);
                TextRenderer.DrawText(g, Slot.Name, Theme.BodyBold, new Rectangle(x, Theme.S(11), textW, Theme.S(20)), live ? Theme.Text : Theme.TextDim,
                                      TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, Details(Slot), Theme.Small, new Rectangle(x, Theme.S(32), textW, Theme.S(18)), Theme.TextDim,
                                      TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
                if (!live) return;
                // how full it is
                long free = _view.FreeOf(Slot.Volume);
                var track = new RectangleF(Width - Theme.S(236), Theme.S(17), Theme.S(196), Theme.S(8));
                using (var path = Theme.Rounded(track, track.Height / 2))
                using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, path);
                if (free >= 0 && Slot.Size > 0)
                {
                    double used = Math.Max(0, Math.Min(1, 1 - (double)free / Slot.Size));
                    var fill = new RectangleF(track.X, track.Y, (float)(track.Width * used), track.Height);
                    if (fill.Width >= 1)
                        using (var path = Theme.Rounded(fill, track.Height / 2))
                        using (var b = new SolidBrush(used > 0.9 ? Theme.Warning : Theme.Accent)) g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, FreeText(), Theme.Small, new Rectangle((int)track.X, Theme.S(30), (int)track.Width, Theme.S(18)), Theme.TextDim,
                                      TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
                // a chevron: it opens
                TextRenderer.DrawText(g, Glyph.ChevronRight, Theme.Icon, new Rectangle(Width - Theme.S(34), 0, Theme.S(26), Height), _hover ? Theme.Text : Theme.TextDim,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
