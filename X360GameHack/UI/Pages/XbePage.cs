using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public sealed class XbePage : PageBase
    {
        public override string Title => "Original Xbox (OG)";
        public override string Subtitle => "XBE header info, Title ID, region, RAM and CPU patches - and xemu for original Xbox discs.";
        public override string PageGlyph => Glyph.Game;

        private readonly KvGrid _info = new KvGrid();
        private readonly TextBlock _empty;
        private readonly Control[] _needsFile;
        private readonly XTextBox _titleId = new XTextBox("8 hex digits", false, true) { Width = Theme.S(140) };
        private readonly NumericUpDown _mhz = new NumericUpDown { Minimum = 733, Maximum = 2000, Value = 1000, Width = Theme.S(100), Height = Theme.S(30) };
        private readonly TextBlock _ramState = new TextBlock("", Theme.Body, Theme.TextDim);
        private readonly TextBlock _cpuState = new TextBlock("", Theme.Body, Theme.TextDim);
        private readonly XCheck[] _regions =
        {
            new XCheck("North America"), new XCheck("Japan"), new XCheck("Rest of world")
        };
        private XbeFile _xbe;

        public XbePage()
        {
            _empty = Note(Body, "Open or drop a default.xbe (or any .xbe) to use these tools. For original Xbox ISOs use the Disc Images page.", Theme.Warning);

            var top = AddColumns(2, 420);
            var info = AddCard("XBE header", "Double-click a value to copy it.", Glyph.Info, top);
            info.Add(_info);
            info.AddAction("Refresh", Glyph.Refresh, (s, e) => Reload());

            var id = AddCard("Certificate", "Title ID and region live in the XBE certificate.", Glyph.Tag, top);
            var setId = Btn("Set", Glyph.Check, null);
            setId.Click += (s, e) => Patch(setId, "Setting Title ID", p =>
            {
                uint v = BE.ParseHex32(_titleId.Text);
                XbeFile.SetTitleId(p, v);
                return "Title ID set to " + v.ToString("X8");
            });
            Field(id, "Title ID", _titleId, setId).FillChild = null;
            var regionRow = new WrapPanel();
            foreach (var r in _regions) regionRow.Add(r);
            id.Add(new TextBlock("Game region", Theme.Small, Theme.TextDim));
            id.Add(regionRow);
            var setRegion = Btn("Apply region", Glyph.Globe, null);
            setRegion.Click += (s, e) => Patch(setRegion, "Setting region", p =>
            {
                uint region = (_xbe?.GameRegion ?? 0) & 0x80000000;
                if (_regions[0].Checked) region |= 1;
                if (_regions[1].Checked) region |= 2;
                if (_regions[2].Checked) region |= 4;
                XbeFile.SetRegion(p, region);
                return "Region set to 0x" + region.ToString("X8");
            });
            var allMedia = Btn("Allow all media", Glyph.Hdd, null, tip: "Lets the XBE run from HDD, DVD and CD (allowed-media flags)");
            allMedia.Click += (s, e) => Patch(allMedia, "Allowing all media", p =>
            {
                // HDD, DVD X2, DVD/CD, CD, DVD-5/9 RO and RW
                XbeFile.SetAllowedMedia(p, (_xbe?.AllowedMedia ?? 0) | 0x000000FFu);
                return "Allowed media patched";
            });
            Buttons(id, setRegion, allMedia);

            var perf = AddColumns(2, 420);
            var ram = AddCard("RAM patch", "For consoles upgraded to 128MB with a supporting BIOS.", Glyph.Memory, perf);
            ram.Add(_ramState);
            var r128 = Btn("Use 128MB", Glyph.Add, null, BtnKind.Primary);
            r128.Click += (s, e) => Patch(r128, "RAM patch", p => { XbeFile.SetRamLimit(p, false); return "XBE can now use 128MB"; });
            var r64 = Btn("Stock 64MB", null, null);
            r64.Click += (s, e) => Patch(r64, "RAM patch", p => { XbeFile.SetRamLimit(p, true); return "XBE limited to 64MB (stock)"; });
            Buttons(ram, r128, r64);

            var cpu = AddCard("CPU clock patch", "Fixes game timing on CPU-upgraded consoles.", Glyph.Speed, perf);
            cpu.Add(_cpuState);
            Theme.DarkScrollbars(_mhz);
            _mhz.BackColor = Theme.Input; _mhz.ForeColor = Theme.Text; _mhz.BorderStyle = BorderStyle.FixedSingle;
            var setCpu = Btn("Set MHz", Glyph.Check, null, BtnKind.Primary);
            setCpu.Click += (s, e) => SetCpu(setCpu, (int)_mhz.Value);
            var cpuRow = new HStack { Height = Theme.S(34) };
            cpuRow.Add(_mhz); cpuRow.Add(setCpu);
            cpu.Add(cpuRow);
            var presets = new WrapPanel();
            foreach (var (mhz, label) in new[] { (733, "733 (stock)"), (1000, "1000 (Trusty)"), (1400, "1400 (Trusty)"), (1480, "1480 (DreamX)") })
            {
                var b = Btn(label, null, null);
                int m = mhz;
                b.Click += (s, e) => SetCpu(b, m);
                presets.Add(b);
            }
            cpu.Add(presets);

            var dumps = AddCard("XbeTool", "Header listings go to the log; dumps are written next to the XBE.", Glyph.Wrench);
            Buttons(dumps,
                Btn("All header info", Glyph.List, () => XbeTool("a", "XBE header info")),
                Btn("Certificate", null, () => XbeTool("c", "XBE certificate")),
                Btn("Sections", null, () => XbeTool("s", "XBE sections")),
                Btn("Libraries", null, () => XbeTool("l", "XBE libraries")),
                Btn("Dump to binary", Glyph.Save, () => XbeTool("b", "Dumping XBE to binary")),
                Btn("Dump to EXE", Glyph.Save, () => XbeTool("e", "Dumping XBE to EXE")),
                Btn("Dump IDA .idc", Glyph.Save, () => XbeTool("i", "Dumping IDC")),
                Btn("Open folder", Glyph.FolderOpen, () => Tools.OpenFolder(WorkingFile.Path)));

            // xemu plays original Xbox disc images (it used to sit on the Emulators page, now the Xenia page)
            var xemu = AddCard("xemu (original Xbox)", "Plays the game in xemu - its folder becomes the disc image xemu needs (xemu keeps its own BIOS / MCPX / HDD setup).", Glyph.Game);
            var xemuPath = new XTextBox(@"Path to xemu.exe") { Text = AppSettings.Current.XemuPath };
            xemuPath.TextChanged += (o, e) => { AppSettings.Current.XemuPath = xemuPath.Text.Trim(); AppSettings.Save(); };
            Field(xemu, null, xemuPath, Btn("", Glyph.FolderOpen, () => { var f = PickFile("Locate xemu", "xemu|xemu*.exe|Programs|*.exe"); if (f != null) xemuPath.Text = f; }));
            Buttons(xemu,
                Btn("Play this game", Glyph.Play, PlayWorkingGame, BtnKind.Primary,
                    tip: "The game whose default.xbe is open: its folder becomes a disc image beside it (Black -> Black.iso, made again only when the folder changed) and xemu starts it."),
                Btn("Run an ISO...", Glyph.OpenFile, () => { var f = PickFile("XISO to run in xemu", "Disc images|*.iso;*.xiso"); if (f != null) RunXemu(f); }),
                Btn("Get xemu", Glyph.Globe, () => Tools.OpenUrl("https://xemu.app")));
            Note(xemu, "Redump ISOs must be converted to XISO first: Xbox ISOs > Optimize / rewrite.");

            _needsFile = new Control[] { top, perf, dumps };
            OnWorkingFileChanged();
        }

        private static void RunXemu(string iso)
        {
            if (XemuPlay.Exe == null) { Dlg.Info("xemu", "Set the path to xemu.exe first (the box above)."); return; }
            if (string.IsNullOrEmpty(iso) || !File.Exists(iso)) { Dlg.Info("xemu", "Pick an XISO disc image."); return; }
            string why = XemuPlay.Start(iso);
            if (why != null) Dlg.Warn("xemu", why);
        }

        /// <summary>Play this game: the open default.xbe's folder as a disc image (made beside it, kept while the folder stays the same) in xemu.</summary>
        private void PlayWorkingGame()
        {
            if (XemuPlay.Exe == null) { Dlg.Info("xemu", "Set the path to xemu.exe first (the box above) - or press Get xemu."); return; }
            if (WorkingFile.Has(WorkingFileKind.Iso)) { RunXemu(WorkingFile.Path); return; }
            if (!WorkingFile.Has(WorkingFileKind.Xbe) || !string.Equals(Path.GetFileName(WorkingFile.Path), "default.xbe", StringComparison.OrdinalIgnoreCase))
            {
                Dlg.Info("xemu", "Open the game's default.xbe (in the game's own folder) - xemu plays the whole folder as a disc image.");
                return;
            }
            string folder = Path.GetDirectoryName(WorkingFile.Path);
            var cts = new System.Threading.CancellationTokenSource();
            Run(null, "Play in xemu", async () =>
            {
                string iso;
                using (Status.Begin("Making the disc image of " + Path.GetFileName(folder), cts))
                {
                    var progress = Status.NewProgress();
                    iso = await Task.Run(() => XemuPlay.IsoFor(folder, progress, cts.Token));
                }
                string why = XemuPlay.Start(iso);
                if (why != null) { Dlg.Warn("xemu", why); return; }
                Log.Success("Started " + Path.GetFileName(folder) + " in xemu from " + iso + ".");
            });
        }

        public override void OnWorkingFileChanged()
        {
            bool has = WorkingFile.Kind == WorkingFileKind.Xbe;
            _empty.Visible = !has;
            foreach (var c in _needsFile) c.Enabled = has;
            if (has) Reload();
        }

        private void Reload()
        {
            string path = WorkingFile.Path;
            Run(null, "Reading XBE", async () =>
            {
                var x = await Task.Run(() => XbeFile.Read(path));
                int cpu = await Task.Run(() => XbeFile.DetectCpuSpeed(path));
                if (!x.Valid) throw new InvalidOperationException("Not a valid XBE file.");
                _xbe = x;
                _info.Clear();
                _info.Set("Title", x.TitleName);
                _info.Set("Title ID", x.TitleIdString);
                _info.Set("Region", x.RegionString);
                _info.Set("Allowed media", x.MediaString);
                _info.Set("Version", "0x" + x.CertVersion.ToString("X8"));
                _info.Set("Disc number", x.DiscNumber.ToString());
                _info.Set("Build", x.IsDebugBuild ? "Debug / XDK" : "Retail");
                _info.Set("Timestamp", x.TimeDate.ToString("yyyy-MM-dd HH:mm"));
                _info.Set("Base address", "0x" + x.BaseAddress.ToString("X8"));
                _info.Set("Entry point", "0x" + x.EntryPoint.ToString("X8"));
                _info.Set("Image size", FileSniffer.FormatSize(x.ImageSize));
                _info.Set("Sections", x.SectionCount.ToString());
                _info.Set("Init flags", "0x" + x.InitFlags.ToString("X8"));
                _titleId.Text = x.TitleId.ToString("X8");
                _regions[0].Checked = (x.GameRegion & 1) != 0;
                _regions[1].Checked = (x.GameRegion & 2) != 0;
                _regions[2].Checked = (x.GameRegion & 4) != 0;
                _ramState.Text = x.Limit64Mb ? "Currently: limited to 64MB (stock)." : "Currently: 128MB allowed.";
                _cpuState.Text = cpu == 0 ? "No clock constant to patch (the game does not use the stock one, or it was set before - the .bak next to it is the original)."
                    : "Currently: " + (cpu == 733333333 ? "733 MHz (stock)" : (cpu / 1000000) + " MHz");
                _cpuState.Tag = cpu;
                Body.PerformLayout();
            });
        }

        private void Patch(XButton sender, string what, Func<string, string> action)
        {
            if (!WorkingFile.Has(WorkingFileKind.Xbe)) return;
            string path = WorkingFile.Path;
            var op = WorkingFile.BeginOperation(what);
            if (op == null) return;
            Run(sender, what, async () =>
            {
                using (op)
                {
                    BackupOnce(path);
                    string msg = await Task.Run(() => action(path));
                    Log.Success(msg);
                }
                Reload();
            });
        }

        private void SetCpu(XButton sender, int mhz)
        {
            int current = _cpuState.Tag is int c ? c : 0;
            if (current == 0)
            {
                Dlg.Warn("CPU patch", "This XBE has no CPU clock constant this program can patch: the game does not use the stock 733.33 MHz one, or it was set before to a value that cannot be told apart from ordinary numbers. " +
                    "To change it again, put the original XBE back (the .bak next to it).");
                return;
            }
            if (current == (mhz == 733 ? XbeFile.StockCpuConstant : mhz * 1000000)) { Log.Info("The CPU clock is already " + mhz + " MHz."); return; }
            if (mhz > 1480 && !Dlg.Confirm("CPU patch", "Values above 1480 MHz are beyond any known CPU upgrade. Continue?", "Continue", true)) return;
            if (!XbeFile.CanRefind(mhz) && !Dlg.Confirm("CPU patch",
                    mhz + " MHz cannot be found again afterwards: only the stock value, 1400 and 1480 MHz can be told apart from the game's own numbers (1000 MHz, for one, is also a number games use). " +
                    "To change it later, put the original XBE back (the .bak next to it). Patch anyway?", "Patch", true)) return;
            Patch(sender, "CPU patch", p =>
            {
                int hits = XbeFile.PatchCpuSpeed(p, mhz, current);
                return hits == 0 ? "CPU constant not found" : $"CPU clock set to {mhz} MHz ({hits} location(s))";
            });
        }

        private void XbeTool(string option, string what)
        {
            if (!WorkingFile.Has(WorkingFileKind.Xbe)) return;
            string path = WorkingFile.Path;
            Run(null, what, async () => { await ToolRunner.RunAsync(Tools.XbeTool, option + " " + ToolRunner.Q(path), Path.GetDirectoryName(path)); });
        }
    }
}
