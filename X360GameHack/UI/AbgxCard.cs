using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// abgx360 built in: everything the abgx360 GUI does - the input (a file, every image in a folder, or a disc in a DVD drive),
    /// every option, its CLI-window or report-file output - with the report shown right here in abgx360's own colours as it is
    /// written, and its questions asked in a dialog. abgx360 itself (Hadzz's build, which comes with this program) does the
    /// checking; "Set up abgx360" installs it when it is missing and puts Hadzz's server fix file in place.
    /// </summary>
    public sealed class AbgxCard : Card
    {
        // ---- input / output
        private readonly Segmented _inputKind = new Segmented("A file", "A folder", "A DVD drive");
        private readonly XTextBox _input = new XTextBox("Disc image, XEX or stealth file (or drop one here)");
        private readonly XTextBox _folder = new XTextBox("Folder holding the images");
        private readonly XCheck _subFolders = new XCheck("Include sub-folders", false, "--rec: images in folders below it are checked too.");
        private readonly XTextBox _match = new XTextBox("e.g. *.iso,*.000  (empty: every file)") { Width = Theme.S(240) };
        private readonly XCombo _drive = new XCombo { Width = Theme.S(140) };
        private HStack _fileRow, _folderRow, _matchRow, _driveRow;
        private readonly Segmented _output = new Segmented("Show the report here", "In its own window");
        private readonly TextBlock _setup = new TextBlock("", Theme.Small, Theme.TextDim) { Flags = TextFormatFlags.WordBreak };

        // ---- report
        private readonly AbgxReportView _report = new AbgxReportView { Visible = false };
        private readonly TextBlock _progress = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(20) };
        private readonly TextBlock _verdict = new TextBlock("", Theme.BodyBold, Theme.Text) { Flags = TextFormatFlags.WordBreak };
        private readonly WrapPanel _reportButtons = new WrapPanel { Visible = false };
        private XButton _stop, _verify, _fix, _runButton;
        private CancellationTokenSource _cts;
        private bool _writing;
        private string _lastHtml;
        private AbgxReport _lastReport;
        private string _lastInput;

        // ---- every option
        private readonly Segmented _verbose = new Segmented("Normal", "High", "Low");
        private readonly Segmented _ach = new Segmented("Hide", "Show", "Show, hide secret");
        private readonly Segmented _crc = new Segmented("When needed", "Always", "Never");
        private readonly Segmented _fixLevel = new Segmented("Never", "If stealth fails", "If uncertain (default)", "Always");
        private readonly Segmented _rebuild = new Segmented("Default (needs ~7.5 GB)", "Low space (risky)", "Never");
        private readonly Segmented _images = new Segmented("With HTML output", "Always", "Never");
        private readonly XCombo _lang = new XCombo { Width = Theme.S(220) };
        private readonly Dictionary<string, XCheck> _checks = new Dictionary<string, XCheck>();
        private readonly Dictionary<string, XTextBox> _boxes = new Dictionary<string, XTextBox>();
        private readonly XTextBox _command = new XTextBox(null, true, true) { Height = Theme.S(64), ReadOnly = true };
        private readonly VStack _all = new VStack { Visible = false, Padding = new Padding(0), Spacing = Theme.S(10) };
        private bool _loading;

        public AbgxCard() : base("abgx360 - verify and fix disc images",
            "Checks an Xbox 360 image against the verified database and repairs its stealth files (for images you burn, or to prove one good). " +
            "Everything the abgx360 GUI does, built in; the report appears below in abgx360's own colours - red or yellow text is important.", Glyph.Shield)
        {
            AddAction("Set up abgx360", Glyph.Download, (s, e) => SetUp());
            AddAction("StealthFiles", Glyph.FolderOpen, (s, e) => { Directory.CreateDirectory(AbgxSetup.StealthFolder); Tools.OpenFolder(AbgxSetup.StealthFolder); });
            Add(_setup);

            // ---------------------------------------------------------------- input
            var s0 = AppSettings.Current;
            PageBase.Field(this, "Check", _inputKind).FillChild = null;
            _fileRow = PageBase.Field(this, "File", _input, new XButton("", Glyph.FolderOpen, BtnKind.Secondary, (s, e) =>
            {
                string f = PageBase.PickFile("Choose a disc image, XEX or stealth file for abgx360", "Xbox 360 images and files|*.iso;*.000;*.xex;*.bin;*.dvd|All files|*.*");
                if (f != null) _input.Text = f;
            }));
            _folderRow = PageBase.Field(this, "Folder", _folder, new XButton("", Glyph.FolderOpen, BtnKind.Secondary, (s, e) =>
            {
                string d = PageBase.PickFolder("Choose the folder whose images abgx360 checks", _folder.Text);
                if (d != null) _folder.Text = d;
            }));
            _matchRow = PageBase.Field(this, "Only files like", _match, _subFolders);
            _matchRow.FillChild = null;
            _driveRow = PageBase.Field(this, "Drive", _drive, new XButton("Refresh", Glyph.Refresh, BtnKind.Ghost, (s, e) => FillDrives()));
            _driveRow.FillChild = null;
            PageBase.Field(this, "Report", _output).FillChild = null;
            _folder.Text = s0.AbgxFolder ?? "";
            _subFolders.Checked = s0.AbgxSubFolders;
            _match.Text = s0.AbgxMatch ?? "";
            _inputKind.SelectedIndex = Math.Max(0, Math.Min(2, s0.AbgxInput));
            _output.SelectedIndex = s0.AbgxOutput == 1 ? 1 : 0;
            FillDrives();

            _verify = new XButton("Verify only (changes nothing)", Glyph.Shield, BtnKind.Primary, (s, e) => { Load(Keep(AbgxOptions.VerifyOnly())); Run(); });
            _fix = new XButton("Verify + AutoFix", Glyph.Wrench, BtnKind.Secondary, (s, e) => { Load(Keep(AbgxOptions.VerifyAndFix())); Run(); });
            _stop = new XButton("Stop", Glyph.Cancel, BtnKind.Ghost, (s, e) => Stop()) { Enabled = false };
            var top = new WrapPanel();
            top.Add(_verify); top.Add(_fix); top.Add(_stop);
            Add(top);

            Add(_report);
            Add(_progress);
            Add(_verdict);
            _reportButtons.Add(new XButton("Save the report...", Glyph.Save, BtnKind.Secondary, (s, e) => SaveReport()));
            _reportButtons.Add(new XButton("Open it in the browser", Glyph.Globe, BtnKind.Ghost, (s, e) => OpenReport()));
            _reportButtons.Add(new XButton("Copy as text", Glyph.Copy, BtnKind.Ghost, (s, e) => { if (_lastReport != null) Clipboard.SetText(_lastReport.PlainText()); }));
            Add(_reportButtons);

            var more = new XCheck("Show every option");
            more.CheckedChanged += (s, e) => { _all.Visible = more.Checked; Parent?.PerformLayout(); };
            Add(more);
            Add(_all);

            // ---------------------------------------------------------------- output
            Heading("Output");
            Row("Detail", _verbose);
            Row("Achievements", _ach);
            Row("XEX pictures", _images);
            Tip.Set(_images, "The abgx360 GUI's choice: extract the game's pictures (into abgx360's Images folder) with HTML output - which a report shown here is - always, or never.");
            foreach (var l in new[] { "English", "Japanese", "German", "French", "Spanish", "Italian", "Korean", "Traditional Chinese", "Portuguese", "Simplified Chinese", "Polish", "Russian" }) _lang.Items.Add(l);
            _lang.SelectedIndex = 0;
            Row("Language", _lang);
            Checks(("terminal", "Terminal font characters", "--terminal"), ("stripcolors", "No colors", "--stripcolors"), ("html", "HTML output", "--html (a report shown here is always HTML)"),
                   ("showfiles", "List the image's files", "--showfiles"), ("aa", "Show avatar awards", "--aa"), ("embed", "Embed pictures in the HTML", "--embed"));

            // ---------------------------------------------------------------- checks
            Heading("Checks");
            Row("Game data CRC", _crc);
            Checks(("corrupt", "Always look for AnyDVD-style corruption", "--corrupt"), ("padding", "Check / fix video zero padding", "--padding"),
                   ("pL0", "Also fix XGD2 L0 padding", "--pL0 (read abgx360's warning first)"), ("nodvdcheck", "Don't check or create the .dvd file", "--nodvdcheck"),
                   ("regioncheck", "Region code only (no stealth check)", "--regioncheck"), ("noverify", "Don't verify against the database", "--noverify"));

            // ---------------------------------------------------------------- online
            Heading("Online");
            Checks(("noupdate", "Don't check for updates", "--noupdate"), ("csv", "Update GameNameLookup.csv too", "--csv"),
                   ("stayoffline", "Stay offline", "--stayoffline"), ("localonly", "Use the local StealthFiles folder only", "--localonly"));
            Box("nettimeout", "Network timeout (seconds)", "20");

            // ---------------------------------------------------------------- fixing
            Heading("Fixing");
            Row("AutoFix", _fixLevel);
            Row("Rebuild when video space is missing", _rebuild);
            Checks(("nowrite", "Never write to the image", "--nowrite"), ("keeporiginaliso", "Keep the original image after rebuilding", "--keeporiginaliso"));

            // ---------------------------------------------------------------- manual patch / extract
            Heading("Patch or extract stealth files by hand");
            foreach (var (key, label, save) in new[]
            {
                ("p-video", "Patch video from", false), ("p-pfi", "Patch PFI from", false), ("p-dmi", "Patch DMI from", false), ("p-ss", "Patch SS from", false),
                ("e-video", "Extract video to", true), ("e-pfi", "Extract PFI to", true), ("e-dmi", "Extract DMI to", true), ("e-ss", "Extract SS to", true)
            })
                FileBox(key, label, save);
            Checks(("patchitanyway", "Patch even if stealth passes", "--patchitanyway"), ("patchgarbage", "Patch even if the files are invalid", "--patchgarbage"),
                   ("clobber", "Overwrite extracted files without asking", "--clobber"));

            // ---------------------------------------------------------------- misc
            Heading("Advanced");
            Checks(("skiplb", "Skip the layer-break check", "--skiplb"), ("notrust", "Don't trust SS v2 angles", "--notrust"), ("nofixdrt", "Don't fix the SS C/R table", "--nofixdrt"),
                   ("nofixdev", "Don't fix deviating angles", "--nofixdev"), ("fixangle359", "Change 359 degree angles to 0", "--fixangle359"),
                   ("devkey", "Use the devkit key for the XEX", "--devkey"));
            Box("dev", "Fix angles deviating more than (degrees)", "3");
            Box("rgn", "Your console's region code", "e.g. 000000FF");
            Tip.Set(_boxes["rgn"].Box, "abgx360 only uses this to colour the game's region code in its report. It does not change the image.");
            Box("retries", "Read / write retries", "20");
            Box("dvdtimeout", "DVD timeout (seconds)", "20");
            Box("truncate", "Truncate / extend image to (bytes)", "leave empty");
            Heading("Its own window");
            Checks(("pause", "Keep the window open when done", "--pause (waits for a key)"), ("max", "Maximize the window", "--max (the abgx360 GUI's default)"));

            _all.Add(new TextBlock("Command", Theme.SmallBold, Theme.TextDim));
            _all.Add(_command);
            var run = new WrapPanel();
            _runButton = new XButton("Run with these options", Glyph.Play, BtnKind.Primary, (s, e) => Run());
            run.Add(_runButton);
            run.Add(new XButton("Reset to abgx360's defaults", Glyph.Refresh, BtnKind.Ghost, (s, e) => Load(AbgxOptions.GuiDefaults())));
            _all.Add(run);

            foreach (var seg in new[] { _verbose, _ach, _crc, _fixLevel, _rebuild, _images, _output }) seg.SelectedIndexChanged += (s, e) => Preview();
            _inputKind.SelectedIndexChanged += (s, e) => { ShowInput(); Preview(); };
            _lang.SelectedIndexChanged += (s, e) => Preview();
            foreach (var b in new[] { _input, _folder, _match }) b.TextChanged += (s, e) => Preview();
            _subFolders.CheckedChanged += (s, e) => Preview();
            _drive.SelectedIndexChanged += (s, e) => Preview();

            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) =>
            {
                if (!(e.Data.GetData(DataFormats.FileDrop) is string[] f) || f.Length == 0) return;
                if (Directory.Exists(f[0])) { _inputKind.SelectedIndex = 1; _folder.Text = f[0]; }
                else { _inputKind.SelectedIndex = 0; _input.Text = f[0]; }
            };

            Load(string.IsNullOrEmpty(s0.AbgxOptions) ? AbgxOptions.GuiDefaults() : AbgxOptions.Load(s0.AbgxOptions));
            ShowInput();
            ShowSetup();
        }

        /// <summary>A preset keeps the window settings the user chose.</summary>
        private AbgxOptions Keep(AbgxOptions preset)
        {
            var now = Read(out _);
            preset.PauseWhenDone = now.PauseWhenDone;
            preset.Maximize = now.Maximize;
            return preset;
        }

        private void Heading(string text) => _all.Add(new TextBlock(text, Theme.BodyBold, Theme.Text));

        private void Row(string label, Control control) => PageBase.Field(_all, label, control).FillChild = null;

        private void Checks(params (string Key, string Text, string Tip)[] items)
        {
            var w = new WrapPanel();
            foreach (var (key, text, tip) in items)
            {
                var c = new XCheck(text, false, tip);
                c.CheckedChanged += (s, e) => Preview();
                _checks[key] = c;
                w.Add(c);
            }
            _all.Add(w);
        }

        private void Box(string key, string label, string cue)
        {
            var b = new XTextBox(cue) { Width = Theme.S(200) };
            b.TextChanged += (s, e) => Preview();
            _boxes[key] = b;
            PageBase.Field(_all, label, b).FillChild = null;
        }

        private void FileBox(string key, string label, bool save)
        {
            var b = new XTextBox("file path");
            b.TextChanged += (s, e) => Preview();
            _boxes[key] = b;
            var browse = new XButton("", Glyph.FolderOpen, BtnKind.Secondary, (s, e) =>
            {
                string picked = save ? PageBase.PickSave(label, "") : PageBase.PickFile(label);
                if (picked != null) b.Text = picked;
            });
            PageBase.Field(_all, label, b, browse);
        }

        private void FillDrives()
        {
            string was = _drive.SelectedItem as string;
            _drive.Items.Clear();
            try { foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.CDRom)) _drive.Items.Add(d.Name.Substring(0, 2)); } catch { }
            if (_drive.Items.Count > 0) _drive.SelectedIndex = Math.Max(0, was == null ? 0 : _drive.Items.IndexOf(was));
        }

        private void ShowInput()
        {
            int k = _inputKind.SelectedIndex;
            _fileRow.Visible = k == 0;
            _folderRow.Visible = _matchRow.Visible = k == 1;
            _driveRow.Visible = k == 2;
            if (k == 0 && _input.Text.Length == 0 && (WorkingFile.Has(WorkingFileKind.Iso) || WorkingFile.Has(WorkingFileKind.Xex))) _input.Text = WorkingFile.Path;
            Parent?.PerformLayout();
        }

        /// <summary>Called when the open file changes: an open image or XEX becomes the file to check.</summary>
        public void UseOpenFile()
        {
            if (_cts != null) return;
            if (WorkingFile.Has(WorkingFileKind.Iso) || WorkingFile.Has(WorkingFileKind.Xex)) { _input.Text = WorkingFile.Path; Preview(); }
        }

        private void ShowSetup()
        {
            var st = AbgxSetup.Check();
            _setup.Text = st.Describe();
            _setup.ForeColor = st.Ready ? Theme.TextDim : Theme.Warning;
        }

        // ---------------------------------------------------------------- options <-> controls

        private bool On(string key) => _checks[key].Checked;
        private string Str(string key) { string t = _boxes[key].Text.Trim(); return t.Length == 0 ? null : t; }

        private static int? Int(string text, string what, List<string> problems)
        {
            if (text == null) return null;
            if (int.TryParse(text, out int v)) return v;
            problems.Add(what + " must be a whole number.");
            return null;
        }

        /// <summary>The options as currently set on the card; <paramref name="problem"/> says what to fix when they cannot be used.</summary>
        public AbgxOptions Read(out string problem)
        {
            var bad = new List<string>();
            var o = new AbgxOptions
            {
                Verbose = (AbgxOptions.Verbosity)_verbose.SelectedIndex,
                ShowAchievements = (AbgxOptions.Achievements)_ach.SelectedIndex,
                Crc = (AbgxOptions.GameCrc)_crc.SelectedIndex,
                Fix = (AbgxOptions.AutoFix)_fixLevel.SelectedIndex,
                Rebuilding = (AbgxOptions.Rebuild)_rebuild.SelectedIndex,
                XexImages = (AbgxOptions.Images)_images.SelectedIndex,
                Language = _lang.SelectedIndex + 1,
                TerminalFont = On("terminal"), StripColors = On("stripcolors"), Html = On("html"), ShowFiles = On("showfiles"), AvatarAwards = On("aa"),
                EmbedImages = On("embed"),
                CheckCorruption = On("corrupt"), CheckPadding = On("padding"), FixL0Padding = On("pL0"), NoDvdCheck = On("nodvdcheck"),
                RegionCheckOnly = On("regioncheck"), NoVerify = On("noverify"),
                NoUpdate = On("noupdate"), UpdateCsv = On("csv"), StayOffline = On("stayoffline"), LocalOnly = On("localonly"),
                NoWrite = On("nowrite"), KeepOriginalIso = On("keeporiginaliso"),
                PatchVideo = Str("p-video"), PatchPfi = Str("p-pfi"), PatchDmi = Str("p-dmi"), PatchSs = Str("p-ss"),
                ExtractVideo = Str("e-video"), ExtractPfi = Str("e-pfi"), ExtractDmi = Str("e-dmi"), ExtractSs = Str("e-ss"),
                PatchItAnyway = On("patchitanyway"), PatchGarbage = On("patchgarbage"), Clobber = On("clobber"),
                SkipLayerbreak = On("skiplb"), NoTrustSsV2 = On("notrust"), NoFixDrt = On("nofixdrt"), NoFixDeviation = On("nofixdev"),
                FixAngle359 = On("fixangle359"), DevKey = On("devkey"), PauseWhenDone = On("pause"), Maximize = On("max"),
                RegionCode = Str("rgn"),
                NetTimeout = Int(Str("nettimeout"), "The network timeout", bad),
                Deviation = Int(Str("dev"), "The angle deviation", bad),
                Retries = Int(Str("retries"), "The retry count", bad),
                DvdTimeout = Int(Str("dvdtimeout"), "The DVD timeout", bad)
            };
            string t = Str("truncate");
            if (t != null) { if (long.TryParse(t, out long size)) o.TruncateTo = size; else bad.Add("The truncate size must be a whole number of bytes."); }
            problem = bad.FirstOrDefault() ?? o.Problem();
            return o;
        }

        /// <summary>Shows <paramref name="o"/> on the card.</summary>
        public void Load(AbgxOptions o)
        {
            _loading = true;
            try
            {
                _verbose.SelectedIndex = (int)o.Verbose; _ach.SelectedIndex = (int)o.ShowAchievements; _crc.SelectedIndex = (int)o.Crc;
                _fixLevel.SelectedIndex = (int)o.Fix; _rebuild.SelectedIndex = (int)o.Rebuilding; _images.SelectedIndex = (int)o.XexImages;
                _lang.SelectedIndex = Math.Max(0, Math.Min(11, o.Language - 1));
                void C(string k, bool v) => _checks[k].Checked = v;
                void B(string k, object v) => _boxes[k].Text = v?.ToString() ?? "";
                C("terminal", o.TerminalFont); C("stripcolors", o.StripColors); C("html", o.Html); C("showfiles", o.ShowFiles); C("aa", o.AvatarAwards);
                C("embed", o.EmbedImages); C("corrupt", o.CheckCorruption); C("padding", o.CheckPadding); C("pL0", o.FixL0Padding);
                C("nodvdcheck", o.NoDvdCheck); C("regioncheck", o.RegionCheckOnly); C("noverify", o.NoVerify); C("noupdate", o.NoUpdate); C("csv", o.UpdateCsv);
                C("stayoffline", o.StayOffline); C("localonly", o.LocalOnly); C("nowrite", o.NoWrite); C("keeporiginaliso", o.KeepOriginalIso);
                C("patchitanyway", o.PatchItAnyway); C("patchgarbage", o.PatchGarbage); C("clobber", o.Clobber); C("skiplb", o.SkipLayerbreak);
                C("notrust", o.NoTrustSsV2); C("nofixdrt", o.NoFixDrt); C("nofixdev", o.NoFixDeviation); C("fixangle359", o.FixAngle359); C("devkey", o.DevKey);
                C("pause", o.PauseWhenDone); C("max", o.Maximize);
                B("p-video", o.PatchVideo); B("p-pfi", o.PatchPfi); B("p-dmi", o.PatchDmi); B("p-ss", o.PatchSs);
                B("e-video", o.ExtractVideo); B("e-pfi", o.ExtractPfi); B("e-dmi", o.ExtractDmi); B("e-ss", o.ExtractSs);
                B("rgn", o.RegionCode); B("nettimeout", o.NetTimeout); B("dev", o.Deviation); B("retries", o.Retries); B("dvdtimeout", o.DvdTimeout); B("truncate", o.TruncateTo);
            }
            finally { _loading = false; }
            Preview();
        }

        /// <summary>The command line for the current input and output, or why there is none yet.</summary>
        private string CommandFor(AbgxOptions o, out string problem)
        {
            problem = null;
            var run = _output.SelectedIndex == 0 ? o.ForApp() : o;
            switch (_inputKind.SelectedIndex)
            {
                case 1:
                    if (_folder.Text.Trim().Length == 0) { problem = "Choose the folder to check."; return run.CommandLineForFolder("<folder>", _subFolders.Checked, _match.Text); }
                    return run.CommandLineForFolder(_folder.Text.Trim(), _subFolders.Checked, _match.Text);
                case 2:
                    string d = _drive.SelectedItem as string;
                    if (d == null) { problem = "No DVD drive was found on this PC."; return run.CommandLineForDrive('D'); }
                    return run.CommandLineForDrive(d[0]);
                default:
                    string f = _input.Text.Trim();
                    if (f.Length == 0) problem = "Choose the file to check.";
                    return run.CommandLine(new[] { f.Length == 0 ? "<image>" : f });
            }
        }

        private void Preview()
        {
            if (_loading) return;
            var o = Read(out string problem);
            string cmd = CommandFor(o, out _);
            _command.Text = problem ?? "abgx360 " + cmd;
        }

        private void SaveSettings(AbgxOptions o)
        {
            var s = AppSettings.Current;
            s.AbgxOptions = o.Save();
            s.AbgxInput = _inputKind.SelectedIndex;
            s.AbgxFolder = _folder.Text.Trim();
            s.AbgxSubFolders = _subFolders.Checked;
            s.AbgxMatch = _match.Text.Trim();
            s.AbgxOutput = _output.SelectedIndex;
            AppSettings.Save();
        }

        // ---------------------------------------------------------------- setup

        private async void SetUp()
        {
            if (_cts != null) { Dlg.Info("abgx360", "abgx360 is running - wait until it is done."); return; }
            try
            {
                using (Status.Begin("Setting up abgx360"))
                {
                    var (state, done) = await AbgxSetup.SetUpAsync();
                    Log.Success("abgx360: " + done);
                    ShowSetup();
                    Dlg.Info("abgx360", done + "\n\n" + state.Describe());
                }
            }
            catch (OperationCanceledException ex) { Log.Warn(ex.Message); }
            catch (Exception ex) { Log.Error("abgx360 setup: " + ex.Message); Dlg.Warn("abgx360", ex.Message); }
        }

        /// <summary>abgx360 installed (offered when missing) with the fix file in place; null when it cannot run.</summary>
        private async Task<string> ReadyExe()
        {
            var st = AbgxSetup.Check();
            if (st.Exe == null)
            {
                if (!Dlg.Confirm("abgx360", "abgx360 is not installed yet. Install it now?\n\nHadzz's installer (it comes with this program) starts; Windows asks for permission because it installs into the Windows folder.", "Install")) return null;
                SetUp();
                return null;
            }
            try
            {
                string fix = AbgxSetup.EnsureFixFile();
                if (!fix.StartsWith("The fix file is already")) Log.Info("abgx360: " + fix);
                int v = AbgxSetup.CopyVideoFiles();
                if (v > 0) Log.Info("abgx360: " + v + " video file(s) copied into " + AbgxSetup.StealthFolder);
            }
            catch (Exception ex) { Log.Warn("abgx360: the fix file could not be written: " + ex.Message); }
            ShowSetup();
            await Task.Yield();
            return st.Exe;
        }

        // ---------------------------------------------------------------- run

        private async void Run()
        {
            if (_cts != null) { Dlg.Info("abgx360", "abgx360 is already running."); return; }
            var o = Read(out string problem);
            if (problem != null) { Dlg.Warn("abgx360 options", problem); return; }
            int kind = _inputKind.SelectedIndex;
            if (kind == 0 && _input.Text.Trim().Length == 0)
            {
                string f = PageBase.PickFile("Choose a disc image, XEX or stealth file for abgx360", "Xbox 360 images and files|*.iso;*.000;*.xex;*.bin;*.dvd|All files|*.*");
                if (f == null) return;
                _input.Text = f;
            }
            if (kind == 1 && _folder.Text.Trim().Length == 0)
            {
                string d = PageBase.PickFolder("Choose the folder whose images abgx360 checks", null);
                if (d == null) return;
                _folder.Text = d;
            }
            string args = CommandFor(o, out problem);
            if (problem != null) { Dlg.Warn("abgx360", problem); return; }
            string input = kind == 0 ? _input.Text.Trim() : kind == 1 ? _folder.Text.Trim() : null;
            if (kind == 0 && !File.Exists(input)) { Dlg.Warn("abgx360", "File not found: " + input); return; }
            if (kind == 1 && !Directory.Exists(input)) { Dlg.Warn("abgx360", "Folder not found: " + input); return; }
            SaveSettings(o);
            string exe = await ReadyExe();
            if (exe == null) return;
            string workDir = kind == 0 ? Path.GetDirectoryName(Path.GetFullPath(input)) : kind == 1 ? input : Tools.AppDir;

            // abgx360 may rewrite the image: nothing else may work on the open file while it runs.
            bool open = kind == 0 && string.Equals(WorkingFile.Path, input, StringComparison.OrdinalIgnoreCase);
            IDisposable op = open ? WorkingFile.BeginOperation("abgx360") : null;
            if (open && op == null) return;
            _writing = !o.NoWrite && kind != 2;
            try
            {
                if (_output.SelectedIndex == 1)
                {
                    Log.Info("abgx360 " + args);
                    using (Status.Begin("abgx360 is running in its own window"))
                        await ToolRunner.RunAsync(exe, args, workDir, forceVisible: true);
                }
                else await RunHere(exe, args, workDir, input);
            }
            finally { op?.Dispose(); }
            if (open && !o.NoWrite) WorkingFile.Refresh();
        }

        private async Task RunHere(string exe, string args, string workDir, string input)
        {
            _cts = new CancellationTokenSource();
            _stop.Enabled = true; _verify.Enabled = _fix.Enabled = _runButton.Enabled = false;
            var report = new AbgxReport();
            _report.Start();
            _report.Visible = true;
            _reportButtons.Visible = false;
            _verdict.Text = "";
            _progress.Text = "Starting abgx360...";
            Parent?.PerformLayout();
            Log.Info("abgx360 " + args);
            try
            {
                using (Status.Begin("abgx360 is checking " + (input != null ? Path.GetFileName(input.TrimEnd('\\')) : "the disc"), _cts))
                {
                    var res = await AbgxRunner.RunAsync(exe, args, workDir, true,
                        chunk => BeginInvoke(new Action(() => { report.Feed(chunk); _report.Show(report, false); })),
                        line => BeginInvoke(new Action(() => _progress.Text = line.Length > 160 ? line.Substring(0, 160) : line)),
                        question => (bool)Invoke(new Func<bool>(() => Dlg.Confirm("abgx360 asks", question, "Yes"))),
                        _cts.Token);
                    _report.Show(report, true);
                    _lastHtml = res.Output;
                    _lastReport = report;
                    _lastInput = input;
                    var v = report.Verdict();
                    _verdict.Text = res.Cancelled ? "Stopped." : "abgx360: " + v.Summary() + ".";
                    _verdict.ForeColor = res.Cancelled ? Theme.Warning : v.RedLines.Count > 0 || v.StealthFailed || v.VerifyFailed || v.AutoFixFailed ? Theme.Danger : v.YellowLines.Count > 0 ? Theme.Warning : Theme.Success;
                    _progress.Text = res.Cancelled ? "" : "Finished (exit code " + res.ExitCode + ").";
                    if (res.Cancelled) Log.Warn("abgx360 was stopped.");
                    else Log.Success("abgx360: " + v.Summary() + ".");
                }
            }
            catch (Exception ex) { _progress.Text = ""; Log.Error("abgx360: " + ex.Message); Dlg.Warn("abgx360", ex.Message); }
            finally
            {
                _cts.Dispose(); _cts = null;
                _stop.Enabled = false; _verify.Enabled = _fix.Enabled = _runButton.Enabled = true;
                _reportButtons.Visible = _lastReport != null;
                Parent?.PerformLayout();
            }
        }

        private void Stop()
        {
            if (_cts == null) return;
            if (_writing && !Dlg.Confirm("Stop abgx360", "abgx360 may be writing to the image right now (AutoFix, rebuilding). Stopping it in the middle can leave the image damaged.\n\nStop anyway?", "Stop", true)) return;
            _cts.Cancel();
        }

        private void SaveReport()
        {
            if (_lastReport == null) return;
            string name = (_lastInput != null ? Path.GetFileNameWithoutExtension(_lastInput.TrimEnd('\\')) : "abgx360") + " - abgx360.html";
            string f = PageBase.PickSave("Save the abgx360 report (.html = abgx360's own page, .txt = text)", PathSafety.SafeName(name), "abgx360 report (HTML)|*.html|Text|*.txt");
            if (f == null) return;
            try
            {
                if (f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) File.WriteAllText(f, _lastReport.PlainText(), new UTF8Encoding(true));
                else File.WriteAllText(f, _lastHtml ?? "", Encoding.GetEncoding(1252));   // abgx360's own page, exactly as it wrote it
                Log.Success("Report saved: " + f);
            }
            catch (Exception ex) { Dlg.Warn("Save", ex.Message); }
        }

        /// <summary>Opens a copy of the report in the browser - rebuilt from the coloured text, with nothing in it that runs.</summary>
        private void OpenReport()
        {
            if (_lastReport == null) return;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "X360GameHack");
                Directory.CreateDirectory(dir);
                string f = Path.Combine(dir, "abgx360 report " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html");
                File.WriteAllText(f, AbgxReportView.SafeHtml(_lastReport, _lastInput), new UTF8Encoding(true));
                Tools.OpenUrl(f);
            }
            catch (Exception ex) { Dlg.Warn("abgx360", ex.Message); }
        }
    }

    /// <summary>abgx360's report in its own colours (those of the Windows console it normally runs in), on black.</summary>
    public sealed class AbgxReportView : RichTextBox
    {
        private int _shown;

        public AbgxReportView()
        {
            ReadOnly = true;
            BorderStyle = BorderStyle.None;
            BackColor = Color.FromArgb(12, 12, 12);
            ForeColor = Color.FromArgb(204, 204, 204);
            Font = new Font("Consolas", 9.5f);
            WordWrap = false;
            DetectUrls = false;
            ScrollBars = RichTextBoxScrollBars.Both;
            Height = Theme.S(340);
            Theme.DarkScrollbars(this);
        }

        public void Start() { Clear(); _shown = 0; }

        /// <summary>Adds the lines not shown yet (the last, unfinished line only when <paramref name="final"/>).</summary>
        public void Show(AbgxReport r, bool final)
        {
            int upto = final ? r.Lines.Count : r.CompleteLines;
            if (upto <= _shown) return;
            SuspendLayout();
            for (int i = _shown; i < upto; i++)
            {
                foreach (var p in r.Lines[i])
                {
                    Select(TextLength, 0);
                    var (fore, back) = Colours(p.Style);
                    SelectionColor = fore;
                    SelectionBackColor = back ?? BackColor;
                    SelectionFont = p.Bold ? new Font(Font, FontStyle.Bold) : Font;
                    SelectedText = p.Text;
                }
                if (i < r.Lines.Count - 1) { Select(TextLength, 0); SelectionBackColor = BackColor; SelectedText = "\n"; }
            }
            _shown = upto;
            Select(TextLength, 0);
            ScrollToCaret();
            ResumeLayout();
        }

        /// <summary>abgx360's colour names in the Windows console palette (it sets them with SetConsoleTextAttribute there).</summary>
        public static (Color Fore, Color? Back) Colours(string style)
        {
            Color C(int rgb) => Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            switch ((style ?? "normal").Replace("_u", ""))
            {
                case "white": case "achtitle": return (C(0xF2F2F2), null);
                case "green": return (C(0x16C60C), null);
                case "yellow": return (C(0xF9F1A5), null);
                case "red": case "wtfhex": return (C(0xE74856), null);
                case "cyan": return (C(0x61D6D6), null);
                case "blue": case "arrow": case "box": return (C(0x3B78FF), null);
                case "darkblue": return (C(0x0037DA), null);
                case "brown": return (C(0xC19C00), null);
                case "darkgray": case "hexoffset": return (C(0x767676), null);
                case "wtfchar": return (C(0xF2F2F2), C(0xE74856));
                case "filename": return (C(0xCCCCCC), C(0x3B78FF));
                case "blackonyellow": return (C(0x0C0C0C), C(0xF9F1A5));
                case "blackonred": return (C(0x0C0C0C), C(0xE74856));
                default: return (C(0xCCCCCC), null);
            }
        }

        /// <summary>The report as a page of its own: every character escaped, no scripts, no pictures, nothing fetched.</summary>
        public static string SafeHtml(AbgxReport r, string input)
        {
            string Hex(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">");
            sb.Append("<title>abgx360 report").Append(input != null ? " - " + System.Net.WebUtility.HtmlEncode(Path.GetFileName(input.TrimEnd('\\'))) : "").Append("</title>");
            sb.Append("<style>body{background:#0C0C0C;color:#CCCCCC;font:14px Consolas,monospace;white-space:pre;margin:16px}</style></head><body>");
            for (int i = 0; i < r.Lines.Count; i++)
            {
                foreach (var p in r.Lines[i])
                {
                    var (fore, back) = Colours(p.Style);
                    sb.Append("<span style=\"color:").Append(Hex(fore));
                    if (back.HasValue) sb.Append(";background:").Append(Hex(back.Value));
                    if (p.Bold) sb.Append(";font-weight:bold");
                    sb.Append("\">").Append(System.Net.WebUtility.HtmlEncode(p.Text)).Append("</span>");
                }
                sb.Append('\n');
            }
            sb.Append("</body></html>");
            return sb.ToString();
        }
    }
}
