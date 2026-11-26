using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// "Make an Xbox drive": which console, the layout (original Xbox: F: / G: the way big-drive BIOSes and XBpartitioner
    /// lay them out), for a new image also its size - with every partition it gets shown before anything is written.
    /// Returns the plan (<see cref="DriveFormatter.Plan"/>), or null when cancelled. Nothing is written here.
    /// </summary>
    public sealed class MakeDriveDialog : DialogForm
    {
        private readonly XCombo _console = new XCombo(), _layout = new XCombo(), _size = new XCombo();
        private readonly XTextBox _fSize = new XTextBox("GB") { Width = Theme.S(110) };
        private readonly HStack _sizeRow, _layoutRow, _fRow;
        private readonly ListView _preview = ListStyle.Create(("Partition", 200), ("Starts at", 130), ("Size", 100), ("Clusters", 90));
        private readonly TextBlock _notes = new TextBlock("", Theme.Small, Theme.TextDim);
        private readonly XCheck _sure;
        private readonly XButton _ok;
        private readonly List<(string Text, long Bytes)> _sizes = new List<(string, long)>();
        private readonly List<DriveLayout> _layouts = new List<DriveLayout>();
        private readonly long _fixedSize;
        private readonly bool _physical;
        private bool _filling;

        public DrivePlan Result { get; private set; }

        /// <summary>The sizes offered for a new image: the stock original Xbox drives, then common drives (their sector counts by the IDEMA formula, as drives are sold).</summary>
        internal static List<(string Text, long Bytes)> StandardSizes()
        {
            long Idema(long gb) => (97696368L + 1953504L * (gb - 50)) * 512;
            return new List<(string, long)>
            {
                ("8 GB - the original Xbox's stock drive", 15633072L * 512),
                ("10 GB - the original Xbox's stock drive (Seagate)", 19541088L * 512),
                ("60 GB", Idema(60)), ("120 GB", Idema(120)), ("250 GB", Idema(250)), ("320 GB", Idema(320)), ("500 GB", Idema(500)),
                ("1 TB", Idema(1000)), ("2 TB", Idema(2000)),
            };
        }

        /// <param name="target">What is made, in words ("Disk 3: WDC ... (931.5 GB)", "a new image file: x.img").</param>
        /// <param name="driveSize">The drive's exact size, or 0 to let the user choose one (a new image).</param>
        /// <param name="current">The layout the drive has now (to offer the same again), or null.</param>
        /// <param name="extraSizes">More sizes to offer for a new image (the drives connected to this PC).</param>
        public MakeDriveDialog(string target, long driveSize, DriveLayout? current, long currentF, bool physical, bool erases, IEnumerable<(string Text, long Bytes)> extraSizes = null)
            : base("Make an Xbox drive")
        {
            _fixedSize = driveSize; _physical = physical;
            var body = new VStack { Padding = new Padding(Theme.S(22), Theme.S(18), Theme.S(22), Theme.S(18)), Spacing = Theme.S(10) };
            body.Add(new TextBlock("Make an Xbox drive", Theme.CardTitle, Theme.Text));
            body.Add(new TextBlock(target, Theme.Body, Theme.TextDim));

            HStack Row(string label, Control c)
            {
                var r = new HStack { Height = Theme.S(32), Spacing = Theme.S(10) };
                r.Add(new TextBlock(label, Theme.Body, Theme.Text) { Width = Theme.S(90), Flags = TextFormatFlags.VerticalCenter });
                r.Add(c);
                r.FillChild = c;
                body.Add(r);
                return r;
            }
            _console.Items.Add("Original Xbox");
            _console.Items.Add("Xbox 360");
            Row("Console", _console);
            if (driveSize <= 0)
            {
                _sizes.AddRange(StandardSizes());
                if (extraSizes != null) _sizes.AddRange(extraSizes);
                foreach (var s in _sizes) _size.Items.Add(s.Text + (s.Text.Contains("(") ? "" : "  (" + (s.Bytes / 512).ToString("N0") + " sectors)"));
                _sizeRow = Row("Size", _size);
            }
            _layoutRow = Row("Layout", _layout);
            _fRow = new HStack { Height = Theme.S(32), Spacing = Theme.S(10) };
            _fRow.Add(new TextBlock("F: size", Theme.Body, Theme.Text) { Width = Theme.S(90), Flags = TextFormatFlags.VerticalCenter });
            _fRow.Add(_fSize);
            _fRow.Add(new TextBlock("GB - G: gets the rest", Theme.Body, Theme.TextDim) { Flags = TextFormatFlags.VerticalCenter, Width = Theme.S(200) });
            body.Add(_fRow);

            _preview.Height = Theme.S(190);
            _preview.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            body.Add(_preview);
            body.Add(_notes);
            _sure = new XCheck(erases ? "Everything on it now is erased - I am sure this is the right " + (physical ? "drive" : "file") : "I am sure", !erases)
            { Height = Theme.S(28) };
            _sure.Visible = erases;
            body.Add(_sure);
            var buttons = new HStack { Height = Theme.S(34), RightAlign = true };
            var cancel = new XButton("Cancel") { DialogResult = DialogResult.Cancel };
            _ok = new XButton(erases ? "Erase and make the drive" : "Make the drive", Glyph.Hdd, erases ? BtnKind.Danger : BtnKind.Primary);
            _ok.Click += (s, e) => { if (Build(out var plan, out _)) { Result = plan; DialogResult = DialogResult.OK; } };
            buttons.Add(cancel); buttons.Add(_ok);
            body.Add(buttons);
            Shrinkable = new Control[] { _preview };

            // the first choices and what they make, before the window is measured - so it opens at its full height
            _filling = true;
            _console.SelectedIndex = current == DriveLayout.Xbox360 ? 1 : 0;
            if (_sizeRow != null) _size.SelectedIndex = Math.Min(7, _sizes.Count - 1);          // 1 TB
            FillLayouts(current);
            if (current == DriveLayout.OgCustom && currentF > 0) _fSize.Text = (currentF >> 30).ToString();
            _filling = false;
            ShowPlan();
            Body(body, Theme.S(620));
            CancelButton = cancel;

            _console.SelectedIndexChanged += (s, e) => { if (!_filling) { _filling = true; FillLayouts(null); _filling = false; ShowPlan(); } };
            _size.SelectedIndexChanged += (s, e) => { if (!_filling) { _filling = true; FillLayouts(SelectedLayout()); _filling = false; ShowPlan(); } };
            _layout.SelectedIndexChanged += (s, e) => { if (!_filling) ShowPlan(); };
            _fSize.Box.TextChanged += (s, e) => { if (!_filling) ShowPlan(); };
            _sure.CheckedChanged += (s, e) => ShowPlan();
        }

        private long Size => _fixedSize > 0 ? _fixedSize : _size.SelectedIndex >= 0 ? _sizes[_size.SelectedIndex].Bytes : 0;
        private bool Xbox360 => _console.SelectedIndex == 1;
        private DriveLayout? SelectedLayout() => Xbox360 ? DriveLayout.Xbox360 : _layout.SelectedIndex >= 0 && _layout.SelectedIndex < _layouts.Count ? _layouts[_layout.SelectedIndex] : (DriveLayout?)null;

        /// <summary>The layouts that fit the size, the usual one first (or the drive's own when it has one).</summary>
        private void FillLayouts(DriveLayout? keep)
        {
            _layout.Items.Clear(); _layouts.Clear();
            long size = Size;
            if (Xbox360)
            {
                _layouts.Add(DriveLayout.Xbox360);
                _layout.Items.Add("The console's own layout");
            }
            else
            {
                bool big = size / 512 >= 0x10000000;           // over 137 GB: F: and G:
                void Add(DriveLayout l, string text) { if (DriveFormatter.Unsuitable(size, l) == null) { _layouts.Add(l); _layout.Items.Add(text); } }
                if (big) Add(DriveLayout.OgStandard, "F: up to 137 GB, G: the rest - what big-drive BIOSes use (recommended)");
                Add(DriveLayout.OgFTakesAll, big ? "F: takes the whole rest (for a BIOS set to \"F: takes all\")" : "F: takes the whole rest (recommended)");
                Add(DriveLayout.OgCustom, "F: a size you choose, G: the rest (partition table, like XBpartitioner)");
                if (big) Add(DriveLayout.OgFOnly137, "F: up to 137 GB only (a BIOS without big-drive support)");
                Add(DriveLayout.OgStock, "Stock: C: E: X: Y: Z: only, like the original 8 / 10 GB drive");
                if (_fSize.Text.Trim().Length == 0 && size > DriveFormatter.OgFStart)
                    _fSize.Text = Math.Max(1, (size - DriveFormatter.OgFStart) / 2 >> 30).ToString();
            }
            int at = keep.HasValue ? _layouts.IndexOf(keep.Value) : -1;
            _layout.SelectedIndex = _layouts.Count == 0 ? -1 : Math.Max(0, at);
            _layoutRow.Visible = !Xbox360;
        }

        private bool Build(out DrivePlan plan, out string problem)
        {
            plan = null; problem = null;
            var layout = SelectedLayout();
            long size = Size;
            if (size <= 0) { problem = "Choose a size."; return false; }
            if (layout == null)
            {
                problem = "No layout fits " + FileSniffer.FormatSize(size) + ": " + (DriveFormatter.Unsuitable(size, DriveLayout.OgStock) ?? "too small") + ".";
                return false;
            }
            long f = 0;
            if (layout == DriveLayout.OgCustom)
            {
                if (!double.TryParse(_fSize.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double gb) || gb <= 0)
                { problem = "Type F:'s size in GB."; return false; }
                f = (long)(gb * (1L << 30));
            }
            string why = DriveFormatter.Unsuitable(size, layout.Value);
            if (why != null) { problem = "This drive cannot be made that way: " + why + "."; return false; }
            try { plan = DriveFormatter.Plan(size, layout.Value, f); return true; }
            catch (ArgumentException ex) { problem = ex.Message; return false; }
        }

        /// <summary>Shows what will be made (or why not) and turns the button on only when it can be made.</summary>
        private void ShowPlan()
        {
            _fRow.Visible = SelectedLayout() == DriveLayout.OgCustom;
            bool ok = Build(out var plan, out string problem);
            _preview.BeginUpdate();
            _preview.Items.Clear();
            if (plan != null)
                foreach (var p in plan.Partitions.OrderBy(x => x.Offset))
                {
                    var it = new ListViewItem(p.Name);
                    it.SubItems.Add("sector " + (p.Offset / 512).ToString("N0"));
                    it.SubItems.Add(FileSniffer.FormatSize(p.Size));
                    it.SubItems.Add(p.ClusterSize / 1024 + " KB");
                    _preview.Items.Add(it);
                }
            _preview.EndUpdate();
            var notes = new List<string>();
            if (!ok) notes.Add(problem);
            else if (Xbox360)
            {
                notes.Add("Laid out as the console lays out its own drives (checked against a real one). Nothing in front of SysExt is written: " +
                          "the drive's security sector and the console's two cache partitions stay as they are - on a new drive the console sets its caches up itself.");
                notes.Add("A console that is not modded only accepts drives sold for it (each has its own security sector) - making the layout does not change that.");
            }
            else
            {
                notes.Add("Laid out as XBpartitioner lays a drive out, with its partition table at sector 0 describing exactly this, so BIOSes that read the table and those that do not see the same drive. " +
                          "Cluster sizes as XBpartitioner chooses them.");
                notes.Add("Every partition starts empty: before the drive goes into the Xbox, put a dashboard on C:, or it does not start. " +
                          "A drive for an Xbox that is not modded must also be locked to that console - X360GameHack does not lock drives.");
            }
            notes.Add(_physical ? "Every block written is journaled first: Drive tools > Undo changes can put the drive back as it was (until it is used in a console)."
                                : "BETA: drives made here read back correctly, but none has been used in a console or emulator yet.");
            _notes.Text = string.Join("\n\n", notes);
            _notes.ForeColor = ok ? Theme.TextDim : Theme.Warning;
            _ok.Enabled = ok && (!_sure.Visible || _sure.Checked);
            FitHeight();
        }

        /// <summary>Asks; null when cancelled.</summary>
        public static DrivePlan Ask(string target, long driveSize, DriveLayout? current, long currentF, bool physical, bool erases, IEnumerable<(string Text, long Bytes)> extraSizes = null)
        {
            using (var d = new MakeDriveDialog(target, driveSize, current, currentF, physical, erases, extraSizes))
                return d.ShowDialog(Dlg.Owner) == DialogResult.OK ? d.Result : null;
        }
    }
}
