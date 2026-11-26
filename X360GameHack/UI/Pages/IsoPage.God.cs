using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>One game in the Convert list: a disc image, a Games on Demand package or a game folder (XEX version).</summary>
    internal sealed class ConvertJob
    {
        public string Source;
        public GameFormat From;
        public long Size;
        public GodTitleInfo Info;            // as read from the game, then as edited
        public string Name;                  // the name the dashboard shows / the result is named after
        public byte[] Thumbnail;             // null = no picture
        public GodPadding Padding;
        public bool KeepRebuilt;
        public bool Ready, Busy, Done, Failed;
        public GameConvertResult Result;
        public ListViewItem Item;
    }

    public sealed partial class IsoPage
    {
        private VStack _convertTab;
        private readonly ListView _cvList = ListStyle.Create(("Game", 260), ("Title ID", 84), ("Disc", 54), ("From", 120), ("Size", 84), ("Status", 320));
        private readonly List<ConvertJob> _cvJobs = new List<ConvertJob>();
        private readonly Segmented _cvTo = new Segmented("Games on Demand", "Disc image (ISO)", "XEX version (folder)");
        private readonly XTextBox _cvOut = new XTextBox();
        private readonly TextBlock _cvFree = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(20) };
        private readonly Segmented _cvMadeFor = new Segmented("As it is", "RGH / JTAG", "Devkit");
        private readonly XCheck _cvXexUnlock = new XCheck("Unlock the game's XEX (xextool)", true, "Removes the XEX's limits (media, region, ...) on a copy - the same patch Bulk extract uses. The original is not changed.");
        private readonly XCheck _cvPkgUnlock = new XCheck("Unlock the package (X360PkgTool)", true, "X360PkgTool's unlock for the console chosen above: purchase traces removed, marked as bought, retail or devkit form.");
        private readonly TextBlock _cvMadeForNote = new TextBlock("", Theme.Small, Theme.TextDim) { Flags = TextFormatFlags.WordBreak };
        // to Games on Demand
        private readonly VStack _cvGodOpts = new VStack { Spacing = Theme.S(6) };
        private readonly Segmented _cvPadding = new Segmented("None", "Partial (cropped)", "Full (rebuilt)");
        private readonly XCheck _cvDisc = new XCheck("Add \"Disc N\" to games on several discs", true, "ISO2God's \"Auto-rename multi-disc games\": Halo 3 ODST Disc 1, Disc 2 ... so the dashboard tells the discs apart.");
        private readonly XCheck _cvKeep = new XCheck("Also save the rebuilt image", false, "Full padding removal builds a new, smaller disc image on the way. Tick to keep it as an .iso as well (it is otherwise never written to disk).");
        private readonly XTextBox _cvKeepDir = new XTextBox("Empty: next to the original image");
        private HStack _cvKeepRow;
        private readonly Segmented _cvNaming = new Segmented("Like ISO2God", "Like Microsoft");
        private readonly Segmented _cvAfter = new Segmented("Nothing", "USB drive", "Console (XBDM)", "Console (FTP)");
        private readonly TextBlock _cvAfterNote = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(20) };
        // to a disc image
        private readonly VStack _cvIsoOpts = new VStack { Spacing = Theme.S(6) };
        private readonly Segmented _cvLayout = new Segmented("Compact (like god2iso)", "Every sector in place");
        private readonly XCheck _cvFix = new XCheck("Fix \"CreateIsoGood\" broken lead-ins", true, "god2iso's option: an image whose first sector says it is 2,587,648 bytes long (left so by the old CreateIsoGood tool) gets a correct lead-in. Nothing else changes.");
        private readonly XCheck _cvStealth = new XCheck("Stealth-patch each new image with abgx360", true,
            "abgx360 AutoFix runs on every new Xbox 360 image: it adds the SS, DMI, PFI and video from its verified database (Hadzz's server) and rebuilds the image to full size. Needs about 9 GB free next to the image.");
        // to a folder
        private readonly VStack _cvFolderOpts = new VStack { Spacing = Theme.S(6) };
        private readonly XCheck _cvSkipUpdate = new XCheck("Leave out $SystemUpdate", true, "The disc's system update folder: a hacked console must never install it, and leaving it out saves space.");
        // images and folders
        private readonly Segmented _cvNames = new Segmented("Game name", "Source file name");
        private HStack _cvNamesRow;
        private XButton _cvStart, _cvStop;
        private CancellationTokenSource _cvCts;

        private GameFormat Target => (GameFormat)(_cvTo.SelectedIndex == 0 ? 1 : _cvTo.SelectedIndex == 1 ? 0 : 2);

        private void BuildGodTabs()
        {
            var s = AppSettings.Current;
            Into = _convertTab = AddSection("Convert", Glyph.Package);
            var card = AddCard("Convert games",
                "ISO2God and god2iso built in, and more: any form of a game to any other. Add disc images, Games on Demand packages (the file next to its .data folder) or games in their " +
                "XEX version (an extracted folder with default.xex, or default.xbe for an original Xbox game) - mixed as you like - choose what they become, and Convert all. " +
                "The name, IDs and picture come from the game itself; double-click one to change them. Every block is hashed and every result checked before it is reported done. The originals are only read.", Glyph.Package);
            card.AddAction("Add images", Glyph.Add, (o, e) => { var f = PickFiles("Add disc images", "Disc images|*.iso;*.xiso;*.000|All files|*.*"); if (f != null) AddGodImages(f); });
            card.AddAction("Add packages", Glyph.Package, (o, e) => { var f = PickFiles("Add Games on Demand packages (the file without an extension next to its .data folder)"); if (f != null) AddGodImages(f); });
            card.AddAction("Add game folders", Glyph.Folder, (o, e) => { var d = PickFolder("Add a game folder (XEX version: default.xex or default.xbe in it), or a folder holding images, packages and game folders"); if (d != null) AddGodImages(new[] { d }); });
            card.AddAction("Edit", Glyph.Edit, (o, e) => EditJob());
            card.AddAction("Remove", Glyph.Remove, (o, e) => RemoveJobs(Selected()));
            card.AddAction("Clear finished", Glyph.Clear, (o, e) => RemoveJobs(_cvJobs.Where(j => j.Done || j.Failed).ToList()));
            _cvList.Height = Theme.S(240);
            _cvList.AllowDrop = true;
            _cvList.DragEnter += (o, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            _cvList.DragDrop += (o, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f) AddGodImages(f); };
            _cvList.DoubleClick += (o, e) => EditJob();
            _cvList.KeyDown += (o, e) => { if (e.KeyCode == Keys.Delete) RemoveJobs(Selected()); else if (e.KeyCode == Keys.Enter) EditJob(); };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Edit...", null, (o, e) => EditJob());
            menu.Items.Add("Install again", null, (o, e) => InstallAgain());
            menu.Items.Add("Show the result", null, (o, e) => { var j = Selected().FirstOrDefault(); if (j?.Result != null) Tools.OpenFolder(j.Result.Output); });
            menu.Items.Add("Browse contents", null, (o, e) =>
            {
                var j = Selected().FirstOrDefault();
                if (j == null) return;
                // the result when there is one, else the source; a folder is simply opened in Explorer
                string p = j.Result != null && (File.Exists(j.Result.Output)) ? j.Result.Output : j.Source;
                if (Directory.Exists(p)) Tools.OpenFolder(p); else BrowseSource(p);
            });
            menu.Items.Add("Remove", null, (o, e) => RemoveJobs(Selected()));
            _cvList.ContextMenuStrip = menu;
            card.Add(_cvList);

            _cvTo.SelectedIndex = Math.Max(0, Math.Min(2, s.ConvertTo));
            Field(card, "Convert to", _cvTo).FillChild = null;
            Field(card, "Save in", _cvOut, Btn("", Glyph.FolderOpen, () =>
            {
                var d = PickFolder(Target == GameFormat.Package ? "Where should the packages go? Pick a USB drive's root to put them straight onto it." : "Where should the results go?", _cvOut.Text);
                if (d != null) { _cvOut.Text = d; SaveConvertOptions(); ShowFree(); }
            }));
            card.Add(_cvFree);

            _cvMadeFor.SelectedIndex = Math.Max(0, Math.Min(2, s.ConvertMadeFor));
            Field(card, "Made for", _cvMadeFor).FillChild = null;
            _cvXexUnlock.Checked = s.GodUnlockXex;
            _cvPkgUnlock.Checked = s.GodUnlockPackage;
            var unlocks = new WrapPanel();
            unlocks.Add(_cvXexUnlock); unlocks.Add(_cvPkgUnlock);
            card.Add(unlocks);
            card.Add(_cvMadeForNote);

            // ---- to Games on Demand
            _cvPadding.SelectedIndex = Math.Max(0, Math.Min(2, s.GodPadding));
            Field(_cvGodOpts, "Padding removal", _cvPadding).FillChild = null;
            Note(_cvGodOpts, "None keeps the whole image. Partial cuts it after the last file - quick. Full packs every file together - the smallest package, and no extra time or disk space here. " +
                             "A game folder is always packed tight; a patched XEX always means Full (its size changes). Edit sets it for one game.");
            _cvDisc.Checked = s.GodAddDiscNumber;
            _cvKeep.Checked = s.GodKeepRebuilt;
            var gopts = new WrapPanel();
            gopts.Add(_cvDisc); gopts.Add(_cvKeep);
            _cvGodOpts.Add(gopts);
            _cvKeepDir.Text = s.GodRebuiltPath;
            _cvKeepRow = Field(_cvGodOpts, "Rebuilt images", _cvKeepDir, Btn("", Glyph.FolderOpen, () => { var d = PickFolder("Where should rebuilt images be saved?", _cvKeepDir.Text); if (d != null) { _cvKeepDir.Text = d; SaveConvertOptions(); } }));
            _cvNaming.SelectedIndex = s.GodNaming == 1 ? 1 : 0;
            Field(_cvGodOpts, "Package file names", _cvNaming).FillChild = null;
            Tip.Set(_cvNaming, "Like ISO2God: from the title ID, media ID and disc - converting the same disc again replaces the older package instead of adding a second one. Like Microsoft: from the header's hash.");
            _cvAfter.SelectedIndex = Math.Max(0, Math.Min(3, s.GodAfter));
            Field(_cvGodOpts, "When a package is done", _cvAfter).FillChild = null;
            _cvGodOpts.Add(_cvAfterNote);
            card.Add(_cvGodOpts);

            // ---- to a disc image
            _cvLayout.SelectedIndex = s.GodIsoLayout == 1 ? 1 : 0;
            Field(_cvIsoOpts, "From packages", _cvLayout).FillChild = null;
            Note(_cvIsoOpts, "Microsoft's own packages leave out the empty stretch between the disc's header and its first file. Compact (what god2iso writes) closes it - the folder tables get new sector numbers - " +
                             "and puts a fresh lead-in in the first 64 KB; Every sector in place puts each block back where it was. A game folder is built as extract-xiso -c builds it; an image (or a package " +
                             "whose XEX is patched) is rewritten as extract-xiso -r does, every file packed.");
            _cvFix.Checked = s.GodIsoFixCreateIsoGood;
            _cvStealth.Checked = s.ConvertStealthPatch;
            var isoChecks = new WrapPanel();
            isoChecks.Add(_cvFix); isoChecks.Add(_cvStealth);
            _cvIsoOpts.Add(isoChecks);
            Note(_cvIsoOpts, GameConvert.MadeHereNote, Theme.Warning);
            card.Add(_cvIsoOpts);

            // ---- to a folder
            _cvSkipUpdate.Checked = s.ConvertSkipSystemUpdate;
            _cvFolderOpts.Add(_cvSkipUpdate);
            card.Add(_cvFolderOpts);

            _cvNames.SelectedIndex = s.GodIsoNaming == 1 ? 1 : 0;
            _cvNamesRow = Field(card, "Names", _cvNames);
            _cvNamesRow.FillChild = null;

            _cvStart = Btn("Convert all", Glyph.Play, () => ConvertJobs(null), BtnKind.Primary);
            _cvStop = Btn("Stop", Glyph.Cancel, () => _cvCts?.Cancel());
            _cvStop.Enabled = false;
            Buttons(card, _cvStart, _cvStop, Btn("Open the output folder", Glyph.FolderOpen, () =>
            {
                string d = _cvOut.Text.Trim();
                if (d.Length == 0 || !Directory.Exists(d)) d = _cvJobs.Select(j => j.Result?.Output).FirstOrDefault(p => p != null);
                if (d != null) Tools.OpenFolder(d); else Dlg.Info("Convert", "Nothing has been converted yet.");
            }));

            _cvTo.SelectedIndexChanged += (o, e) => { SaveConvertOptions(); ShowTargetOptions(); };
            _cvMadeFor.SelectedIndexChanged += (o, e) => { SaveConvertOptions(); ShowTargetOptions(); };
            _cvXexUnlock.CheckedChanged += (o, e) => { SaveConvertOptions(); ShowTargetOptions(); };
            _cvPkgUnlock.CheckedChanged += (o, e) => SaveConvertOptions();
            _cvPadding.SelectedIndexChanged += (o, e) =>
            {
                foreach (var j in _cvJobs.Where(j => !j.Done && !j.Busy)) j.Padding = (GodPadding)_cvPadding.SelectedIndex;
                SaveConvertOptions(); ShowTargetOptions();
            };
            _cvKeep.CheckedChanged += (o, e) => { foreach (var j in _cvJobs.Where(j => !j.Done && !j.Busy)) j.KeepRebuilt = _cvKeep.Checked; SaveConvertOptions(); ShowTargetOptions(); };
            _cvDisc.CheckedChanged += (o, e) =>
            {
                // the default name follows the option until the user typed a name of their own
                foreach (var j in _cvJobs.Where(j => !j.Done && !j.Busy && j.Info != null))
                    if (j.Name == j.Info.DashboardName(j.Source, !_cvDisc.Checked)) { j.Name = j.Info.DashboardName(j.Source, _cvDisc.Checked); ShowJob(j); }
                SaveConvertOptions();
            };
            _cvNaming.SelectedIndexChanged += (o, e) => SaveConvertOptions();
            _cvAfter.SelectedIndexChanged += (o, e) => { SaveConvertOptions(); ShowAfterNote(); };
            _cvLayout.SelectedIndexChanged += (o, e) => SaveConvertOptions();
            _cvFix.CheckedChanged += (o, e) => SaveConvertOptions();
            _cvStealth.CheckedChanged += (o, e) => SaveConvertOptions();
            _cvSkipUpdate.CheckedChanged += (o, e) => SaveConvertOptions();
            _cvNames.SelectedIndexChanged += (o, e) => SaveConvertOptions();
            _cvOut.Box.Leave += (o, e) => { SaveConvertOptions(); ShowFree(); };
            _cvKeepDir.Box.Leave += (o, e) => SaveConvertOptions();
            ShowTargetOptions();
            ShowAfterNote();
        }

        /// <summary>The output box holds the folder of the chosen target (each target remembers its own).</summary>
        private string OutSetting(GameFormat t) { var s = AppSettings.Current; return t == GameFormat.Package ? s.GodOutputPath : t == GameFormat.Image ? s.GodIsoOutputPath : s.ConvertFolderOutputPath; }

        private GameFormat _shownTarget = (GameFormat)(-1);

        private void ShowTargetOptions()
        {
            var t = Target;
            if (t != _shownTarget)
            {
                _cvOut.Text = OutSetting(t) ?? "";
                _shownTarget = t;
            }
            _cvOut.Box.Text = _cvOut.Text;
            string cue = t == GameFormat.Package ? "Empty: a \"Games on Demand\" folder next to each game (a Content\\0000000000000000 tree is made inside)"
                       : t == GameFormat.Image ? "Empty: next to each game" : "Empty: next to each game (one folder per game)";
            Theme.SetCue(_cvOut.Box, cue);
            _cvGodOpts.Visible = t == GameFormat.Package;
            _cvIsoOpts.Visible = t == GameFormat.Image;
            _cvFolderOpts.Visible = t == GameFormat.Folder;
            _cvNamesRow.Visible = t != GameFormat.Package;
            _cvKeepRow.Visible = t == GameFormat.Package && _cvKeep.Checked;
            bool asIs = _cvMadeFor.SelectedIndex == 0;
            _cvXexUnlock.Enabled = !asIs;
            _cvPkgUnlock.Enabled = !asIs && t == GameFormat.Package;
            _cvMadeForNote.Text = asIs
                ? "As it is: the game is converted exactly as ISO2God, god2iso or extract-xiso would - no XEX or package is changed."
                : (_cvMadeFor.SelectedIndex == 2 ? "Devkit: the game's XEX is converted to devkit form (xextool -m d) - a devkit runs nothing else" : "RGH / JTAG: the game's XEX stays retail") +
                  (_cvXexUnlock.Checked ? ", with its limits removed (xextool -r mrbdiyvklc, as Bulk extract does)" : "") + "; " +
                  (t == GameFormat.Package ? (_cvPkgUnlock.Checked ? "the package is unlocked with X360PkgTool (-z -b 1 -m " + (_cvMadeFor.SelectedIndex == 2 ? "0, signed with the devkit key" : "1") + "). " : "the package is not unlocked. ")
                                           : "the result holds the patched XEX. ") +
                  "Patching happens on a copy; the original game is never changed. Original Xbox games have no XEX - only the package unlock applies to them.";
            foreach (var j in _cvJobs) ShowJob(j);
            _convertTab?.PerformLayout();
            ShowFree();
        }

        private void SaveConvertOptions()
        {
            var s = AppSettings.Current;
            string d = _cvOut.Text.Trim();
            switch (Target) { case GameFormat.Package: s.GodOutputPath = d; break; case GameFormat.Image: s.GodIsoOutputPath = d; break; default: s.ConvertFolderOutputPath = d; break; }
            s.ConvertTo = _cvTo.SelectedIndex;
            s.ConvertMadeFor = _cvMadeFor.SelectedIndex;
            s.GodUnlockXex = _cvXexUnlock.Checked;
            s.GodUnlockPackage = _cvPkgUnlock.Checked;
            s.GodPadding = _cvPadding.SelectedIndex;
            s.GodAddDiscNumber = _cvDisc.Checked;
            s.GodKeepRebuilt = _cvKeep.Checked;
            s.GodRebuiltPath = _cvKeepDir.Text.Trim();
            s.GodNaming = _cvNaming.SelectedIndex;
            s.GodAfter = _cvAfter.SelectedIndex;
            s.GodIsoLayout = _cvLayout.SelectedIndex;
            s.GodIsoFixCreateIsoGood = _cvFix.Checked;
            s.ConvertStealthPatch = _cvStealth.Checked;
            s.ConvertSkipSystemUpdate = _cvSkipUpdate.Checked;
            s.GodIsoNaming = _cvNames.SelectedIndex;
            AppSettings.Save();
        }

        private void ShowAfterNote()
        {
            var s = AppSettings.Current;
            switch (_cvAfter.SelectedIndex)
            {
                case 1: _cvAfterNote.Text = string.IsNullOrEmpty(s.UsbDrive) ? "Choose the USB drive on the Console Setup page first." : "Each package is copied to " + s.UsbDrive + "Content\\0000000000000000 (the USB drive chosen on Console Setup)."; break;
                case 2: _cvAfterNote.Text = "Each package is sent to the connected console's " + (s.GodXbdmDrive ?? "Hdd") + ":\\Content over XBDM (connect on the Neighborhood page first)."; break;
                case 3:
                    var p = s.FtpProfiles.ElementAtOrDefault(s.SelectedFtpProfile);
                    _cvAfterNote.Text = p == null || string.IsNullOrWhiteSpace(p.Host) ? "Set up an FTP profile on the FTP page first."
                        : "Each package is uploaded to " + p.Host + ":/" + (s.GodFtpDrive ?? "Hdd1") + "/Content/0000000000000000 (FTP profile \"" + p.Name + "\"), as ISO2God's FTP transfer.";
                    break;
                default: _cvAfterNote.Text = "The packages stay where they are written."; break;
            }
        }

        private void ShowFree()
        {
            string d = _cvOut.Text.Trim();
            try
            {
                if (d.Length == 0 || !Path.IsPathRooted(d)) { _cvFree.Text = ""; return; }
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(d)));
                _cvFree.Text = drive.IsReady ? "Free on " + drive.Name.TrimEnd('\\') + " " + FileSniffer.FormatSize(drive.AvailableFreeSpace) + " (" + drive.DriveFormat + ")" : "";
            }
            catch { _cvFree.Text = ""; }
        }

        /// <summary>Where a package for <paramref name="source"/> goes: {folder}\Content\0000000000000000 (a folder that already is one is used as it is).</summary>
        private string PackageRoot(string source)
        {
            string d = _cvOut.Text.Trim();
            if (d.Length == 0 || !Path.IsPathRooted(d)) d = Path.Combine(Path.GetDirectoryName(source.TrimEnd('\\')), "Games on Demand");
            string full = Path.GetFullPath(d).TrimEnd('\\');
            if (full.EndsWith(@"\Content\0000000000000000", StringComparison.OrdinalIgnoreCase)) return full;
            if (full.EndsWith(@"\Content", StringComparison.OrdinalIgnoreCase)) return Path.Combine(full, "0000000000000000");
            return Path.Combine(full + "\\", "Content", "0000000000000000");
        }

        /// <summary>Where an image / folder for <paramref name="j"/> goes - a new name, never an existing one.</summary>
        private string ResultPath(ConvertJob j, GameFormat t)
        {
            string d = _cvOut.Text.Trim();
            if (d.Length == 0 || !Path.IsPathRooted(d)) d = Path.GetDirectoryName(j.Source.TrimEnd('\\'));
            var info = j.Info?.Clone();
            if (info != null && !string.IsNullOrWhiteSpace(j.Name)) info.Name = j.Name;
            string name = GameConvert.OutputName(j.Source, info, _cvNames.SelectedIndex == 0);
            if (t == GameFormat.Folder && j.From == GameFormat.Folder && _cvNames.SelectedIndex == 1) name += " (converted)";
            string ext = t == GameFormat.Image ? ".iso" : "";
            string path = Path.Combine(d, name + ext);
            for (int n = 2; File.Exists(path) || Directory.Exists(path) || string.Equals(Path.GetFullPath(path).TrimEnd('\\'), Path.GetFullPath(j.Source).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); n++)
                path = Path.Combine(d, name + " (" + n + ")" + ext);
            return path;
        }

        // ---------------------------------------------------------------- the list

        /// <summary>
        /// Adds games in any form - disc images, Games on Demand packages, game folders (XEX version), or folders holding any of
        /// them (searched below) - and reads each one's details.
        /// </summary>
        public void AddGodImages(IEnumerable<string> paths, bool startWhenRead = false)
        {
            var found = new List<(string Path, GameFormat From)>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    if (GodTitleInfo.IsGameFolder(p)) { found.Add((Path.GetFullPath(p).TrimEnd('\\'), GameFormat.Folder)); continue; }
                    try
                    {
                        // images and packages anywhere below, and game folders (a game folder is not searched for more)
                        var pending = new Stack<string>();
                        pending.Push(p);
                        while (pending.Count > 0 && found.Count < 500)
                        {
                            string d = pending.Pop();
                            if (GodTitleInfo.IsGameFolder(d)) { found.Add((Path.GetFullPath(d).TrimEnd('\\'), GameFormat.Folder)); continue; }
                            foreach (var f in Directory.EnumerateFiles(d))
                            {
                                if (f.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xiso", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".000", StringComparison.OrdinalIgnoreCase))
                                    found.Add((f, GameFormat.Image));
                                else if (Path.GetExtension(f).Length == 0 && Directory.Exists(f + ".data") && GodPackage.IsGod(f))
                                    found.Add((f, GameFormat.Package));
                            }
                            foreach (var sub in Directory.EnumerateDirectories(d))
                                if (!sub.EndsWith(".data", StringComparison.OrdinalIgnoreCase)) pending.Push(sub);
                        }
                    }
                    catch (Exception ex) { Log.Warn(p + ": " + ex.Message); }
                }
                else if (File.Exists(p)) found.Add((p, GodPackage.IsGod(p) ? GameFormat.Package : GameFormat.Image));
            }
            var added = new List<ConvertJob>();
            foreach (var f in found.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                if (_cvJobs.Any(j => string.Equals(j.Source, f.Path, StringComparison.OrdinalIgnoreCase) && !j.Done)) continue;
                var job = new ConvertJob { Source = f.Path, From = f.From, Padding = (GodPadding)_cvPadding.SelectedIndex, KeepRebuilt = _cvKeep.Checked };
                job.Item = new ListViewItem(f.From == GameFormat.Image ? Path.GetFileNameWithoutExtension(f.Path) : Path.GetFileName(f.Path)) { Tag = job };
                for (int i = 0; i < 5; i++) job.Item.SubItems.Add("");
                _cvJobs.Add(job);
                _cvList.Items.Add(job.Item);
                added.Add(job);
                SetStatus(job, "Reading the game's details...");
                ShowJob(job);
            }
            if (added.Count == 0) { if (found.Count == 0) Log.Info("No disc images, packages or game folders found there."); return; }
            ShowFree();
            Run(null, "Reading " + added.Count + " game(s)", async () =>
            {
                foreach (var job in added)
                {
                    try
                    {
                        if (job.From == GameFormat.Folder)
                        {
                            job.Info = await Task.Run(() => GodTitleInfo.FromFolder(job.Source));
                            job.Size = await Task.Run(() => GodBuilder.EstimateFolderImageBytes(job.Source));
                        }
                        else
                        {
                            job.Info = await Task.Run(() => GodTitleInfo.FromImage(job.Source, CancellationToken.None));
                            job.Size = job.From == GameFormat.Package ? await Task.Run(() => ContentInstaller.PackageBytes(StfsPackage.Read(job.Source))) : new FileInfo(job.Source).Length;
                        }
                        job.Name = job.Info.DashboardName(job.Source, _cvDisc.Checked);
                        job.Thumbnail = job.Info.Thumbnail;
                        job.Ready = job.Info.TitleId != 0;
                        SetStatus(job, job.Info.TitleId == 0 ? "Needs a title ID - double-click to enter one" : job.Info.Notes.Length > 0 ? "Ready - " + job.Info.Notes : "Ready");
                    }
                    catch (Exception ex)
                    {
                        job.Failed = true;
                        SetStatus(job, "Cannot be read: " + ex.Message);
                        Log.Warn(Path.GetFileName(job.Source) + ": " + ex.Message);
                    }
                    ShowJob(job);
                }
                Log.Info(_cvJobs.Count(j => j.Ready && !j.Done) + " game(s) ready to convert.");
                if (startWhenRead) ConvertJobs(added);
            });
        }

        private List<ConvertJob> Selected() => _cvList.SelectedItems.Cast<ListViewItem>().Select(i => (ConvertJob)i.Tag).ToList();

        private void ShowJob(ConvertJob j)
        {
            if (j.Item == null) return;
            j.Item.Text = !string.IsNullOrEmpty(j.Name) ? j.Name : Path.GetFileName(j.Source);
            j.Item.SubItems[1].Text = j.Info != null ? j.Info.TitleId.ToString("X8") : "";
            j.Item.SubItems[2].Text = j.Info != null ? j.Info.DiscNumber + "/" + j.Info.DiscCount : "";
            j.Item.SubItems[3].Text = GameSources.Describe(j.From) + (j.Info != null && !j.Info.Xbox360 ? " (Xbox)" : "");
            j.Item.SubItems[4].Text = j.Size > 0 ? FileSniffer.FormatSize(j.Size) : "";
            j.Item.ToolTipText = j.Source;
            j.Item.ForeColor = j.Failed ? Theme.Danger : j.Done ? Theme.Success : Theme.Text;
        }

        private void SetStatus(ConvertJob j, string text) { if (j.Item != null) j.Item.SubItems[5].Text = text; }

        private void RemoveJobs(List<ConvertJob> jobs)
        {
            foreach (var j in jobs.Where(j => !j.Busy)) { _cvJobs.Remove(j); _cvList.Items.Remove(j.Item); }
        }

        private void EditJob()
        {
            var j = Selected().FirstOrDefault();
            if (j == null) { Dlg.Info("Edit", "Select a game in the list."); return; }
            if (j.Busy) { Dlg.Info("Edit", "This game is being converted right now."); return; }
            if (j.Info == null) { Dlg.Info("Edit", j.Failed ? "This could not be read as a game." : "Its details are still being read."); return; }
            using (var d = new GodEntryDialog(j.Source, j.Info, j.Name, j.Thumbnail, j.Padding, j.KeepRebuilt, _cvDisc.Checked))
            {
                if (d.ShowDialog(Dlg.Owner) != DialogResult.OK) return;
                j.Info = d.Info; j.Name = d.DisplayName; j.Thumbnail = d.Thumbnail; j.Padding = d.PaddingMode; j.KeepRebuilt = d.KeepRebuilt;
                j.Done = false; j.Failed = false; j.Ready = true; j.Result = null;
                SetStatus(j, "Ready (edited)");
                ShowJob(j);
            }
        }

        // ---------------------------------------------------------------- converting

        /// <summary>Converts the given games (null = every one in the list that is ready), one after another.</summary>
        private void ConvertJobs(List<ConvertJob> only)
        {
            if (_cvCts != null) { if (only == null) Dlg.Info("Convert", "Already converting - the rest of the list follows."); return; }
            var todo = (only ?? _cvJobs).Where(j => j.Ready && !j.Done && !j.Busy).ToList();
            if (todo.Count == 0)
            {
                Dlg.Info("Convert", _cvJobs.Count == 0 ? "Add or drop games on the list first." : "Nothing left to convert - every game in the list is done (or could not be read).");
                return;
            }
            var to = Target;
            int after = to == GameFormat.Package ? _cvAfter.SelectedIndex : 0;
            var s = AppSettings.Current;
            FtpService ftp = null;
            if (after == 1 && (string.IsNullOrEmpty(s.UsbDrive) || !Directory.Exists(s.UsbDrive))) { Dlg.Warn("USB drive", "Choose a USB drive on the Console Setup page first, or set \"When a package is done\" to Nothing."); return; }
            if (after == 2 && !ConsoleService.IsConnected) { Dlg.Info("XBDM", "Connect to the console on the Neighborhood page first, or set \"When a package is done\" to Nothing."); return; }
            if (after == 3)
            {
                var p = s.FtpProfiles.ElementAtOrDefault(s.SelectedFtpProfile);
                if (p == null || string.IsNullOrWhiteSpace(p.Host)) { Dlg.Info("FTP", "Set up an FTP profile on the FTP page first, or set \"When a package is done\" to Nothing."); return; }
                ftp = new FtpService(p);
            }
            bool asIs = _cvMadeFor.SelectedIndex == 0;
            var target = _cvMadeFor.SelectedIndex == 2 ? GodTarget.Devkit : GodTarget.Rgh;
            bool unlockXex = !asIs && _cvXexUnlock.Checked, unlockPkg = !asIs && _cvPkgUnlock.Checked && to == GameFormat.Package;
            bool patching = !asIs && (target == GodTarget.Devkit || unlockXex);
            if (patching && !File.Exists(Tools.XexTool) && todo.Any(j => j.Info.Xbox360)) { Dlg.Warn("xextool", "xextool.exe is missing next to X360GameHack.exe - it patches the game's XEX."); return; }
            if (unlockPkg && !File.Exists(Tools.PkgTool)) { Dlg.Warn("X360PkgTool", "X360PkgTool.exe is missing next to X360GameHack.exe - it unlocks the package."); return; }
            if (to != GameFormat.Package && todo.Any(j => j.From == to && !patching))
            {
                string same = to == GameFormat.Image ? "a disc image to a disc image rewrites it, every file packed (as extract-xiso -r)" : "a folder to a folder makes a copy";
                if (!Dlg.Confirm("Convert", "Converting " + same + ". Go on?", "Convert")) return;
            }
            SaveConvertOptions();
            string keepDir = _cvKeepDir.Text.Trim();
            _cvCts = new CancellationTokenSource();
            var ct = _cvCts.Token;
            _cvStart.Enabled = false; _cvStop.Enabled = true;
            Run(null, "Converting " + todo.Count + " game(s)", async () =>
            {
                int ok = 0;
                try
                {
                    using (Status.Begin("Converting games", _cvCts))
                        foreach (var j in todo)
                        {
                            if (ct.IsCancellationRequested) break;
                            if (await ConvertOne(j, to, target, unlockXex, unlockPkg, keepDir, ct)) ok++;
                            if (j.Done && after > 0) await InstallOne(j, after, ftp, ct);
                        }
                }
                finally
                {
                    _cvCts.Dispose(); _cvCts = null;
                    _cvStart.Enabled = true; _cvStop.Enabled = false;
                    ShowFree();
                }
                if (ct.IsCancellationRequested) Log.Warn("Converting stopped: " + ok + " of " + todo.Count + " game(s) done.");
                else Log.Success("Convert: " + ok + " of " + todo.Count + " game(s) converted to " + (to == GameFormat.Package ? "Games on Demand" : to == GameFormat.Image ? "disc images" : "XEX version folders") + ".");
            });
        }

        private async Task<bool> ConvertOne(ConvertJob j, GameFormat to, GodTarget target, bool unlockXex, bool unlockPkg, string keepDir, CancellationToken ct)
        {
            j.Busy = true;
            j.Failed = false;
            try
            {
                bool patchXex = j.Info.Xbox360 && (target == GodTarget.Devkit && (unlockXex || unlockPkg || _cvMadeFor.SelectedIndex == 2) || unlockXex);
                var opts = new GameConvertOptions
                {
                    To = to,
                    Iso = new GodIsoOptions { Layout = (GodIsoLayout)_cvLayout.SelectedIndex, FixCreateIsoGoodHeader = _cvFix.Checked },
                    SkipSystemUpdate = _cvSkipUpdate.Checked,
                    StealthPatch = to == GameFormat.Image && _cvStealth.Checked,
                    Ask = question => (bool)Invoke(new Func<bool>(() => Dlg.Confirm("abgx360 asks (" + j.Name + ")", question, "Yes"))),
                    God = new GodJobOptions
                    {
                        Build = new GodBuildOptions { Info = j.Info, DisplayName = j.Name, ThumbnailPng = j.Thumbnail ?? new byte[0], AddDiscNumber = false, Padding = j.Padding, Naming = (GodNaming)_cvNaming.SelectedIndex },
                        Target = target,
                        UnlockXex = unlockXex,
                        UnlockPackage = unlockPkg
                    }
                };
                if (_cvMadeFor.SelectedIndex == 0) { opts.God.UnlockXex = false; opts.God.UnlockPackage = false; opts.God.Target = GodTarget.Rgh; patchXex = false; }
                string output;
                SetStatus(j, "Checking free space...");
                if (to == GameFormat.Package)
                {
                    output = PackageRoot(j.Source);
                    var padding = patchXex ? GodPadding.Full : j.Padding;
                    opts.God.Build.Padding = padding;
                    if (j.From != GameFormat.Folder && padding == GodPadding.Full && j.KeepRebuilt)
                    {
                        string dir = keepDir.Length > 0 && Path.IsPathRooted(keepDir) ? keepDir : Path.GetDirectoryName(j.Source);
                        string rebuilt = Path.Combine(dir, PathSafety.SafeName(Path.GetFileNameWithoutExtension(j.Source)) + " (rebuilt" + (patchXex ? ", " + (target == GodTarget.Devkit ? "devkit" : "RGH") : "") + ").iso");
                        if (string.Equals(Path.GetFullPath(rebuilt), Path.GetFullPath(j.Source), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The rebuilt image would replace the original - choose another folder for rebuilt images.");
                        opts.God.Build.RebuiltIsoPath = rebuilt;
                    }
                    long need = j.From == GameFormat.Folder ? GodBuilder.PackageBytes((j.Size + GodPackage.BlockSize - 1) / GodPackage.BlockSize)
                                                            : await Task.Run(() => GodBuilder.EstimateBytes(j.Source, padding, ct), ct);
                    long rebuiltNeed = opts.God.Build.RebuiltIsoPath != null ? await Task.Run(() => GodBuilder.EstimateRebuiltBytes(j.Source, ct), ct) : 0;
                    CheckSpace(output, need + (rebuiltNeed > 0 && SameDrive(output, opts.God.Build.RebuiltIsoPath) ? rebuiltNeed : 0));
                    if (rebuiltNeed > 0 && !SameDrive(output, opts.God.Build.RebuiltIsoPath)) CheckSpace(Path.GetDirectoryName(opts.God.Build.RebuiltIsoPath), rebuiltNeed);
                }
                else
                {
                    output = ResultPath(j, to);
                    long need = j.From == GameFormat.Package ? GodPackage.Open(j.Source).OutputLength(opts.Iso.Layout) : j.Size;
                    CheckSpace(Path.GetDirectoryName(output), need);
                }
                var progress = new Progress<TransferProgress>(p =>
                {
                    SetStatus(j, "Converting " + p.Percent + "%" + (p.Done >= p.Total && to == GameFormat.Package ? " - checking every hash..." : " (" + p.File + ")"));
                    Status.Report(p);
                });
                SetStatus(j, patchXex ? "Patching the XEX for " + GodTargets.Name(target) + "..." : "Converting...");
                j.Result = await GameConvert.RunAsync(j.Source, output, opts, progress, ct);
                j.Done = true;
                string what = to == GameFormat.Package ? j.Result.Package.DataFiles + " data file(s), " + FileSniffer.FormatSize(j.Result.Package.DataBytes) + (j.Result.Package.RebuiltIsoPath != null ? ", rebuilt image saved" : "")
                                                       : Path.GetFileName(j.Result.Output) + ", " + j.Result.Files + " files";
                SetStatus(j, "Done - " + what + (j.Result.Notes.Length > 0 ? ". " + j.Result.Notes : ""));
                Log.Success("Converted \"" + j.Name + "\" (" + GameSources.Describe(j.From) + ") to " + GameSources.Describe(to) + ": " + j.Result.Output + (j.Result.Notes.Length > 0 ? " - " + j.Result.Notes : ""));
                return true;
            }
            catch (OperationCanceledException) { SetStatus(j, "Stopped - nothing was left behind"); return false; }
            catch (Exception ex)
            {
                j.Failed = true;
                SetStatus(j, "Failed: " + ex.Message);
                Log.Error(Path.GetFileName(j.Source) + ": " + ex.Message);
                return false;
            }
            finally { j.Busy = false; ShowJob(j); }
        }

        private static bool SameDrive(string a, string b) => b != null && string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

        private static void CheckSpace(string folder, long need)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder)));
                if (!drive.IsReady) return;
                if (drive.AvailableFreeSpace < need + 64L * 1024 * 1024)
                    throw new IOException("Not enough free space on " + drive.Name.TrimEnd('\\') + ": " + FileSniffer.FormatSize(need) + " needed, " + FileSniffer.FormatSize(drive.AvailableFreeSpace) + " free.");
            }
            catch (ArgumentException) { }
        }

        private async Task InstallOne(ConvertJob j, int after, FtpService ftp, CancellationToken ct)
        {
            var s = AppSettings.Current;
            var target = after == 1 ? InstallTarget.UsbDrive : after == 2 ? InstallTarget.ConsoleXbdm : InstallTarget.ConsoleFtp;
            string done = j.Item.SubItems[5].Text;
            try
            {
                var pkg = StfsPackage.Read(j.Result.Output);
                if (target == InstallTarget.UsbDrive) CheckSpace(s.UsbDrive, ContentInstaller.PackageBytes(pkg));
                SetStatus(j, "Installing...");
                var progress = new Progress<TransferProgress>(p => { SetStatus(j, "Installing " + p.Percent + "%"); Status.Report(p); });
                await ContentInstaller.InstallAsync(new[] { pkg }, target, s.UsbDrive, ftp, s.GodFtpDrive ?? "Hdd1", s.GodXbdmDrive ?? "Hdd", progress, ct);
                SetStatus(j, done + " - installed (" + (target == InstallTarget.UsbDrive ? "USB drive" : target == InstallTarget.ConsoleXbdm ? "console, XBDM" : "console, FTP") + ")");
            }
            catch (OperationCanceledException) { SetStatus(j, done + " - install stopped"); }
            catch (Exception ex)
            {
                SetStatus(j, done + " - install failed: " + ex.Message + " (right-click > Install again)");
                Log.Error(j.Name + ": installing failed: " + ex.Message);
            }
        }

        /// <summary>ISO2God's "Restart FTP Upload": installs the selected finished packages again.</summary>
        private void InstallAgain()
        {
            var jobs = Selected().Where(j => j.Done && j.Result?.Package != null && !j.Busy).ToList();
            if (jobs.Count == 0) { Dlg.Info("Install again", "Select Games on Demand packages that are done converting."); return; }
            int after = _cvAfter.SelectedIndex;
            if (after == 0) { Dlg.Info("Install again", "Choose where to install under \"When a package is done\" first (Convert to: Games on Demand)."); return; }
            if (_cvCts != null) { Dlg.Info("Install again", "Wait until the conversion is finished."); return; }
            var s = AppSettings.Current;
            FtpService ftp = null;
            if (after == 2 && !ConsoleService.IsConnected) { Dlg.Info("XBDM", "Connect to the console on the Neighborhood page first."); return; }
            if (after == 3)
            {
                var p = s.FtpProfiles.ElementAtOrDefault(s.SelectedFtpProfile);
                if (p == null || string.IsNullOrWhiteSpace(p.Host)) { Dlg.Info("FTP", "Set up an FTP profile on the FTP page first."); return; }
                ftp = new FtpService(p);
            }
            _cvCts = new CancellationTokenSource();
            var ct = _cvCts.Token;
            _cvStart.Enabled = false; _cvStop.Enabled = true;
            Run(null, "Installing " + jobs.Count + " package(s)", async () =>
            {
                try
                {
                    using (Status.Begin("Installing packages", _cvCts))
                        foreach (var j in jobs) { if (ct.IsCancellationRequested) break; j.Busy = true; try { await InstallOne(j, after, ftp, ct); } finally { j.Busy = false; } }
                }
                finally { _cvCts.Dispose(); _cvCts = null; _cvStart.Enabled = true; _cvStop.Enabled = false; }
            });
        }
    }
}
