using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public sealed class XexPage : PageBase
    {
        public override string Title => "XEX Tool";
        public override string Subtitle => "Patch, convert and inspect Xbox 360 executables with xextool.";
        public override string PageGlyph => Glyph.Code;

        private readonly KvGrid _info = new KvGrid();
        private readonly PictureBox _icon = new PictureBox { Width = Theme.S(64), Height = Theme.S(64), SizeMode = PictureBoxSizeMode.Zoom };
        private readonly TextBlock _empty;
        private readonly Control[] _needsFile;

        // builder
        private readonly XCheck _fixPatched = new XCheck("Fix title-update-patched XEX (-u)", false, "Makes an XEX that was patched with a .xexp run without the separate patch file.");
        private readonly XCheck _allLimits = new XCheck("Remove all limits, keep Media ID (-r mrbdiyvklc)", true, "Media, region, bounding path, device/console ID, dates, keyvault, library versions and revocation checks. Unlike -r a it does NOT zero the Media ID, so title updates keep working.");
        private readonly WrapPanel _limitFlags = new WrapPanel();
        private readonly Segmented _machine = new Segmented("Unchanged", "Retail / RGH (-m r)", "Devkit (-m d)");
        private readonly Segmented _compression = new Segmented("Unchanged", "Compress (-c c)", "Uncompress (-c u)", "Binary (-c b)");
        private readonly Segmented _encryption = new Segmented("Unchanged", "Encrypt (-e e)", "Decrypt (-e u)");
        private readonly XCheck _toNewFile = new XCheck("Save to a new file instead of modifying the original (-o)");
        private readonly TextBlock _preview = new TextBlock("", Theme.Mono, Theme.AccentHover);

        private readonly XTextBox _titleId = new XTextBox("8 hex digits", false, true) { Width = Theme.S(140) };
        private readonly XTextBox _mediaId = new XTextBox("8 hex digits", false, true) { Width = Theme.S(140) };
        private readonly XTextBox _custom = new XTextBox("-r mrbdiyvklc -m r", false, true);

        private static readonly (char Flag, string Label, string Tip)[] LimitFlags =
        {
            ('m', "Media", "Allow running from any media (HDD, USB, network)"),
            ('r', "Region", "Region free"),
            ('b', "Bounding path", "Remove the bounding path name"),
            ('d', "Device ID", "Remove the bounding device ID"),
            ('i', "Console ID", "Remove the console ID restriction"),
            ('y', "Dates", "Remove date restrictions"),
            ('v', "KV privileges", "Remove keyvault privilege restriction"),
            ('k', "Signed KV only", "Remove the signed-keyvault-only limitation"),
            ('l', "Library versions", "Remove minimum library version limits"),
            ('c', "Revocation", "Remove the required revocation check"),
            ('z', "Zero media ID (breaks TUs)", "Zeroes the Media ID - title updates will no longer match this game")
        };

        private XexFile _xex;

        public XexPage()
        {
            _empty = Note(Body, "Open or drop an .xex file (default.xex, default_mp.xex, a DLL or plugin) to use these tools.", Theme.Warning);

            Into = AddSection("Patch", Glyph.Check);
            var top = AddColumns(2, 420);
            var info = AddCard("Executable", "Read straight from the XEX header. Double-click a value to copy it.", Glyph.Info, top);
            var row = new HStack { Height = Theme.S(70), Spacing = Theme.S(14) };
            var who = new TextBlock("", Theme.CardTitle, Theme.Text) { Height = Theme.S(60), Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak };
            row.Add(_icon); row.Add(who); row.FillChild = who;
            info.Add(row);
            info.Add(_info);
            info.AddAction("Refresh", Glyph.Refresh, (s, e) => Reload());
            info.AddAction("Full info", Glyph.List, (s, e) => XexTool("-l", "Listing XEX info", modifies: false));
            info.Tag = who;

            var quick = AddCard("One-click patches", "The common xextool recipes. A .bak backup is made first (see Settings).", Glyph.Flash, top);
            QuickRow(quick, "Retail / RGH / exploited", "-r mrbdiyvklc -m r", "Remove all limits (keeps the Media ID so title updates still work) and make it a retail XEX. Use this for RGH/JTAG, Bad Update and Bad Avatar.");
            QuickRow(quick, "Prototype / devkit build to RGH", "-u -r mrbdiyvklc -m r", "Converts devkit or prototype builds to retail and makes TU-patched XEX files self-contained.");
            QuickRow(quick, "Devkit", "-r mrbdiyvklc -m d", "Remove all limits (Media ID kept) and make it a devkit XEX.");
            QuickRow(quick, "Devkit, self-contained", "-u -r mrbdiyvklc -m d", "Devkit output that no longer needs the separate .xexp.");
            QuickRow(quick, "XDK debug-ready", "-r mrbdiyvklc -m d -c u -e u", "Devkit, uncompressed and decrypted - ideal for debugging with symbols.");

            // ---- builder
            var builder = AddCard("Custom patch builder", "Choose exactly what to change. Conflicting options can't be selected together.", Glyph.Wrench);
            var checks = new WrapPanel();
            checks.Add(_fixPatched); checks.Add(_allLimits);
            builder.Add(checks);
            foreach (var f in LimitFlags)
            {
                var c = new XCheck(f.Label, false, f.Tip) { Tag = f.Flag };
                c.CheckedChanged += (s, e) => UpdatePreview();
                _limitFlags.Add(c);
            }
            _limitFlags.Visible = false;
            builder.Add(_limitFlags);
            Field(builder, "Machine", _machine).FillChild = null;
            Field(builder, "Compression", _compression).FillChild = null;
            Field(builder, "Encryption", _encryption).FillChild = null;
            builder.Add(_toNewFile);
            var applyRow = new HStack { Height = Theme.S(36) };
            applyRow.Add(_preview); applyRow.FillChild = _preview;
            var apply = Btn("Apply patch", Glyph.Check, null, BtnKind.Primary);
            apply.Click += (s, e) => ApplyBuilder(apply);
            applyRow.Add(apply);
            builder.Add(applyRow);
            _preview.Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            _allLimits.CheckedChanged += (s, e) => { _limitFlags.Visible = !_allLimits.Checked; UpdatePreview(); };
            foreach (var c in new Control[] { _fixPatched, _machine, _compression, _encryption, _toNewFile })
            {
                if (c is CheckBox cb) cb.CheckedChanged += (s, e) => UpdatePreview();
                if (c is Segmented sg) sg.SelectedIndexChanged += (s, e) => UpdatePreview();
            }
            _machine.SelectedIndex = 1;

            Into = AddSection("Encryption & IDs", Glyph.Lock);
            var mid = AddColumns(2, 420);
            var crypt = AddCard("Encryption & compression", null, Glyph.Lock, mid);
            Buttons(crypt,
                Btn("Decrypt + uncompress", Glyph.Unlock, () => XexTool("-e u -c u", "Decrypting and uncompressing")),
                Btn("Encrypt + compress", Glyph.Lock, () => XexTool("-e e -c c", "Encrypting and compressing")),
                Btn("Decrypt", null, () => XexTool("-e u", "Decrypting")),
                Btn("Encrypt", null, () => XexTool("-e e", "Encrypting")),
                Btn("Uncompress", null, () => XexTool("-c u", "Uncompressing")),
                Btn("Compress", null, () => XexTool("-c c", "Compressing")));

            var ids = AddCard("Title ID & Media ID", "Patches only the execution-info header. Saves, TUs and DLC are matched by these IDs.", Glyph.Tag, mid);
            var setTitle = Btn("Set", Glyph.Check, null);
            setTitle.Click += (s, e) => PatchIds(setTitle, true);
            var setMedia = Btn("Set", Glyph.Check, null);
            setMedia.Click += (s, e) => PatchIds(setMedia, false);
            Field(ids, "Title ID", _titleId, setTitle).FillChild = null;
            Field(ids, "Media ID", _mediaId, setMedia).FillChild = null;
            Note(ids, "Tip: search the Media ID on xboxunity.net to find the matching title update.");

            Into = AddSection("Advanced", Glyph.Code);
            var bottom = AddColumns(2, 420);
            // Bad Update / Bad Avatar: the software exploits for consoles that were never opened. The game patch is the retail one.
            var exploit = AddCard("Bad Update / Bad Avatar",
                "Software exploits for unmodified retail consoles. A game needs the same patch as for RGH: every limit removed, Media ID kept, retail format.",
                Glyph.Shield, bottom);
            Buttons(exploit,
                Btn("Patch this XEX for Bad Update / Bad Avatar", Glyph.Play, () => XexTool("-r mrbdiyvklc -m r", "Patching for Bad Update / Bad Avatar"), BtnKind.Primary,
                    "The same as \"Retail / RGH / exploited\" above: -r mrbdiyvklc -m r"),
                Btn("Many discs at once", Glyph.Disc, () => MainForm.Instance.Navigate(typeof(IsoPage)), tip: "Xbox ISOs: bulk-extract and patch every XEX with \"RGH / retail\"."),
                Btn("Unlock DLC", Glyph.Unlock, () => MainForm.Instance.Navigate(typeof(PackagePage)), tip: "X360Packages: make DLC and arcade games run unlocked."),
                Btn("Bad Update project page", Glyph.Globe, () => Tools.OpenUrl("https://github.com/grimdoomer/Xbox360BadUpdate")));
            Note(exploit,
                "Never zero the Media ID (-r a): title updates would stop matching the game. Set up the USB drive the way the exploit's own instructions say, " +
                "then put patched games and packages on it with Console Setup. Title updates are applied on the Title Updates page.");

            var custom = AddCard("Custom xextool command", "Run any xextool options against this file.", Glyph.Code, bottom);
            _custom.Text = AppSettings.Current.XexCustomCommand;
            Field(custom, null, _custom);
            Buttons(custom,
                Btn("Run", Glyph.Play, () => XexTool(_custom.Text.Trim(), "Running custom xextool command"), BtnKind.Primary),
                Btn("Save as default", Glyph.Save, () => { AppSettings.Current.XexCustomCommand = _custom.Text.Trim(); AppSettings.Save(); Log.Success("Custom command saved."); }));

            var export = AddCard("Export & extras", null, Glyph.Download);
            Buttons(export,
                Btn("Export resources...", Glyph.Folder, ExportResources, tip: "Dump every embedded resource (-d)."),
                Btn("Save base file...", Glyph.Save, () => ExportTo("-b", "Base file (decrypted PE)", ".exe", "Exe|*.exe|All|*.*")),
                Btn("Save IDA .idc...", Glyph.Save, () => ExportTo("-i", "IDA script", ".idc", "IDC script|*.idc")),
                Btn("XML info to log", Glyph.List, () => XexTool("-x a", "Extracting XML info", modifies: false)),
                Btn("List special patches", Glyph.Search, () => XexTool("-s 0", "Listing special patches", modifies: false)),
                Btn("Make backup copy", Glyph.Copy, MakeBackup),
                Btn("Open folder", Glyph.FolderOpen, () => Tools.OpenFolder(WorkingFile.Path)),
                Btn("Open in XEX Disassembler", Glyph.Edit, () => MainForm.Instance.Navigate(typeof(XexHackerPage))));

            _needsFile = new Control[] { top, builder, mid, bottom, export };
            UpdatePreview();
            ShowState();
        }

        private void QuickRow(Card card, string title, string args, string description)
        {
            var b = Btn(title, Glyph.Play, () => XexTool(args, title));
            b.Width = Theme.S(250);
            b.LeftAlign = true;
            var t = new TextBlock(description + "  (" + args + ")", Theme.Small, Theme.TextDim) { Height = Theme.S(60), Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak };
            var row = new HStack { Height = Theme.S(64) };
            row.Add(b); row.Add(t); row.FillChild = t;
            card.Add(row);
        }

        private string BuildArgs()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (_fixPatched.Checked) parts.Add("-u");
            if (_allLimits.Checked) parts.Add(XexFile.SafeLimits);
            else
            {
                string flags = new string(_limitFlags.Controls.OfType<XCheck>().Where(c => c.Checked).Select(c => (char)c.Tag).ToArray());
                if (flags.Length > 0) parts.Add("-r " + flags);
            }
            if (_machine.SelectedIndex == 1) parts.Add("-m r");
            if (_machine.SelectedIndex == 2) parts.Add("-m d");
            if (_compression.SelectedIndex == 1) parts.Add("-c c");
            if (_compression.SelectedIndex == 2) parts.Add("-c u");
            if (_compression.SelectedIndex == 3) parts.Add("-c b");
            if (_encryption.SelectedIndex == 1) parts.Add("-e e");
            if (_encryption.SelectedIndex == 2) parts.Add("-e u");
            return string.Join(" ", parts);
        }

        private void UpdatePreview()
        {
            string a = BuildArgs();
            _preview.Text = a.Length == 0 ? "Nothing selected" : "xextool " + a + (_toNewFile.Checked ? " -o <new file>" : "") + " <xex>";
        }

        private void ApplyBuilder(XButton sender)
        {
            string args = BuildArgs();
            if (args.Length == 0) { Dlg.Info("Nothing to do", "Select at least one option in the patch builder."); return; }
            if (_toNewFile.Checked)
            {
                string outFile = PickSave("Save patched XEX as", Path.GetFileNameWithoutExtension(WorkingFile.Path) + "_patched.xex", "XEX|*.xex|All files|*.*");
                if (outFile == null) return;
                XexTool(args + " -o " + ToolRunner.Q(outFile), "Applying custom patch", modifies: false);
                return;
            }
            XexTool(args, "Applying custom patch");
        }

        private bool HasXex()
        {
            if (WorkingFile.Has(WorkingFileKind.Xex) || WorkingFile.Has(WorkingFileKind.Pe)) return true;
            Dlg.Warn("No XEX loaded", "Open or drop an XEX file first.");
            return false;
        }

        private void XexTool(string args, string what, bool modifies = true)
        {
            if (!HasXex() || string.IsNullOrWhiteSpace(args)) return;
            string path = WorkingFile.Path;
            var op = WorkingFile.BeginOperation(what);
            if (op == null) return;
            Run(null, what, async () =>
            {
                using (op)
                {
                if (modifies) BackupOnce(path);
                var mediaBefore = XexFile.ReadMediaIds(path);
                var r = await ToolRunner.RunAsync(Tools.XexTool, args + " " + ToolRunner.Q(path));
                if (modifies) CheckMediaId(path, mediaBefore);
                if (modifies)
                {
                    if (r.Success) Log.Success(what + " - done.");
                    else Log.Warn(what + " - xextool could not do it (exit code " + r.ExitCode + "); its message is just above.");
                    WorkingFile.Refresh();
                }
                }
            });
        }

        /// <summary>Detects xextool zeroing either Media ID field and restores it so title updates keep matching.</summary>
        private void CheckMediaId(string path, XexFile.MediaIds before)
        {
            string restored = XexFile.RestoreZeroedMediaIds(path, before);
            if (restored == null) return;
            Log.Warn("xextool ZEROED the " + restored + " - restored automatically so title updates still match.");
            Dlg.Warn("Media ID was zeroed", "xextool zeroed the " + restored + ".\n\nIt has been restored automatically (title updates are matched by Media ID). " +
                "Avoid -r a / the z flag in custom commands.");
        }

        private void PatchIds(XButton sender, bool title)
        {
            if (!HasXex()) return;
            uint value;
            try { value = BE.ParseHex32((title ? _titleId : _mediaId).Text); }
            catch (Exception ex) { Dlg.Warn("Invalid ID", ex.Message); return; }
            string path = WorkingFile.Path;
            var op = WorkingFile.BeginOperation(title ? "Changing Title ID" : "Changing Media ID");
            if (op == null) return;
            Run(sender, title ? "Changing Title ID" : "Changing Media ID", async () =>
            {
                using (op)
                {
                    BackupOnce(path);
                    await Task.Run(() => XexFile.PatchIds(path, title ? value : (uint?)null, title ? (uint?)null : value));
                    Log.Success((title ? "Title ID" : "Media ID") + " set to " + value.ToString("X8") + " (header digest updated).");
                    WorkingFile.Refresh();
                }
            });
        }

        private void ExportResources()
        {
            if (!HasXex()) return;
            string dir = PickFolder("Choose a folder for the exported resources", Path.GetDirectoryName(WorkingFile.Path));
            if (dir == null) return;
            XexTool("-d " + ToolRunner.Q(dir), "Exporting resources", modifies: false);
        }

        private void ExportTo(string flag, string what, string ext, string filter)
        {
            if (!HasXex()) return;
            string f = PickSave("Save " + what, Path.GetFileNameWithoutExtension(WorkingFile.Path) + ext, filter);
            if (f != null) XexTool(flag + " " + ToolRunner.Q(f), "Saving " + what, modifies: false);
        }

        private void MakeBackup()
        {
            if (!HasXex()) return;
            string src = WorkingFile.Path;
            string dst = src + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            try { File.Copy(src, dst); Log.Success("Backup saved: " + dst); }
            catch (Exception ex) { Dlg.Error("Backup failed", ex.Message); }
        }

        public override void OnWorkingFileChanged()
        {
            ShowState();
            if (WorkingFile.Kind == WorkingFileKind.Xex || WorkingFile.Kind == WorkingFileKind.Pe) Reload();
        }

        public override void OnActivated()
        {
            ShowState();
            bool has = WorkingFile.Kind == WorkingFileKind.Xex || WorkingFile.Kind == WorkingFileKind.Pe;
            if (has && (_xex == null || _xex.Path != WorkingFile.Path)) Reload();
        }

        private void ShowState()
        {
            bool has = WorkingFile.Kind == WorkingFileKind.Xex || WorkingFile.Kind == WorkingFileKind.Pe;
            _empty.Visible = !has;
            foreach (var c in _needsFile) c.Enabled = has;
        }

        private void Reload()
        {
            string path = WorkingFile.Path;
            Run(null, "Reading XEX", async () =>
            {
                var x = await Task.Run(() => XexFile.Read(path));
                await x.LoadToolInfoAsync();
                if (path != WorkingFile.Path) return;
                _xex = x;
                Fill();
            });
        }

        private void Fill()
        {
            var x = _xex;
            var who = (TextBlock)_info.Parent.Tag;
            who.Text = (string.IsNullOrEmpty(x.OriginalName) ? WorkingFile.Name : x.OriginalName) + "\n" +
                       (x.TitleId != 0 ? "Title " + x.TitleId.ToString("X8") : "") + "   " + x.Target;
            _icon.Image?.Dispose();
            _icon.Image = x.Icon != null ? (System.Drawing.Image)x.Icon.Clone() : GlyphImage.Render(Glyph.Code, Theme.TextFaint, Theme.S(64));
            _info.Clear();
            _info.Set("Title ID", x.TitleId == 0 ? "" : x.TitleId.ToString("X8"));
            _info.Set("Media ID", x.MediaId == 0 ? "00000000 - zeroed, title updates won't match" : x.MediaId.ToString("X8"));
            _info.Set("Version", x.Valid ? XexFile.VersionString(x.Version) : "");
            _info.Set("Base version", x.Valid ? XexFile.VersionString(x.BaseVersion) : "");
            _info.Set("Disc", x.DiscCount > 0 ? x.DiscNumber + " of " + x.DiscCount : "");
            _info.Set("Machine", x.Target);
            _info.Set("Encryption", x.Encryption);
            _info.Set("Compression", x.Compression);
            _info.Set("Module", x.ModuleType);
            _info.Set("Entry point", x.EntryPoint == 0 ? "" : "0x" + x.EntryPoint.ToString("X8"));
            _info.Set("Load address", x.LoadAddress == 0 ? "" : "0x" + x.LoadAddress.ToString("X8"));
            _info.Set("Image size", x.ImageSize == 0 ? "" : FileSniffer.FormatSize(x.ImageSize));
            _titleId.Text = x.TitleId == 0 ? "" : x.TitleId.ToString("X8");
            _mediaId.Text = x.MediaId == 0 ? "" : x.MediaId.ToString("X8");
            Body.PerformLayout();
        }
    }
}
