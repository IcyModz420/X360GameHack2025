using System;
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
    /// <summary>
    /// Title updates, start to finish: check that an update was made for your game (from the game's XEX, its disc
    /// image or its package), install it where the console looks for it, or merge it into the XEX for an emulator.
    /// </summary>
    public sealed class TitleUpdatePage : PageBase
    {
        public override string Title => "Title Updates";
        public override string Subtitle => "Check that an update fits your game, install it where the console looks for it, or merge it into the game's XEX.";
        public override string PageGlyph => Glyph.Download;

        private readonly KvGrid _game = new KvGrid { Columns = 1 };
        private readonly KvGrid _update = new KvGrid { Columns = 1 };
        private readonly TextBlock _verdict = new TextBlock("", Theme.Body, Theme.TextDim) { Height = Theme.S(24) };
        private readonly TextBlock _detail = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(62), Flags = TextFormatFlags.WordBreak };
        private readonly XTextBox _xexp = new XTextBox("Path to default.xexp");

        private TuGame _g;
        private TuPackage _tu;
        private string _gameProblem, _updateProblem;

        public TitleUpdatePage()
        {
            var install = AddSection("Check & install", Glyph.Check);
            var guide = AddSection("How updates work", Glyph.Help);
            var merge = AddSection("Merge into the XEX", Glyph.Download);

            // ------------------------------------------------------------ 1. check & install
            var cols = AddColumns(2, 420, install);

            var game = AddCard("1.  Your game", "Its default.xex, its disc image (.iso) or its package. Drop it anywhere in this window.", Glyph.Game, cols);
            game.Add(_game);
            Buttons(game,
                Btn("Choose the game...", Glyph.FolderOpen, ChooseGame, BtnKind.Primary, "default.xex, an Xbox 360 .iso, or an arcade / Games on Demand package."),
                Btn("Copy Title ID", Glyph.Copy, () => CopyId(true), tip: "To paste into the search box on XboxUnity."),
                Btn("Copy Media ID", Glyph.Copy, () => CopyId(false)),
                Btn("Open XboxUnity", Glyph.Globe, () => Tools.OpenUrl("https://xboxunity.net"), tip: "The site that lists title updates by Title ID and Media ID."));

            var update = AddCard("2.  The title update", "The package you downloaded for that Title ID and Media ID. Do not rename it.", Glyph.Download, cols);
            update.Add(_update);
            Buttons(update, Btn("Choose the title update...", Glyph.FolderOpen, ChooseUpdate, BtnKind.Primary));

            var go = AddCard("3.  Does it fit? Install it",
                "Once your game and its update are both chosen above, this reads the IDs out of them and tells you - in the line below - whether the update is the right one for your game. " +
                "If it fits, press one of the Install buttons and it goes where the console looks for updates; then you just start the game.", Glyph.Check, install);
            go.Add(_verdict);
            go.Add(_detail);
            Buttons(go,
                Btn("Install on the console", Glyph.Console, InstallOnConsole, BtnKind.Primary, "Copies it to the connected console's hard drive, into the folder the console reads updates from."),
                Btn("Install on a USB drive", Glyph.Usb, InstallOnUsb, tip: "Copies it to a FAT32 USB drive in the right folder. Plug the drive into the console afterwards."),
                Btn("More places (FTP, other drives)...", Glyph.Hdd, OpenInSetup, tip: "Opens Console Setup with this update already in the list."));

            // ------------------------------------------------------------ 2. guide
            var g = AddColumns(2, 420, guide);

            var what = AddCard("What a title update is", null, Glyph.Info, g);
            Note(what,
                "A title update (TU) is the official patch for an Xbox 360 game: bug fixes, balance changes, sometimes new features. " +
                "DLC, system link and most mods expect a particular one.\n\n" +
                "It is a single package file, named like \"tu00000002_00000000\" or \"TU_16L61V6_0000014000000.00000000000O2\". Inside is a patch for the game's program " +
                "(default.xexp) and, for many games, updated data files.");

            var which = AddCard("Which one fits your game", null, Glyph.Tag, g);
            Note(which,
                "Three things must match. The Check & install tab reads them out of your files and compares them for you.\n" +
                "- Title ID: which game.\n" +
                "- Media ID: which pressing of the disc. Regions, re-releases and Platinum / Classics copies each have their own, with their own updates.\n" +
                "- Game version: an update patches one exact version of default.xex, normally the game as sold.\n\n" +
                "XboxUnity lists updates by Title ID, then by Media ID. Take the highest TU number under YOUR Media ID unless a mod or DLC asks for a certain one.");

            var where = AddCard("Where it goes on the console", null, Glyph.Hdd, g);
            Note(where,
                "The file name decides the folder, which is why it must not be renamed:\n" +
                "- starts with lower-case \"tu\":   Content\\0000000000000000\\<Title ID>\\000B0000\\\n" +
                "- starts with upper-case \"TU_\":  Cache\\\n\n" +
                "Both are on the console's hard drive (Hdd1) or on a USB drive the console uses for storage. 0000000000000000 means \"for every profile\" and 000B0000 is the folder for title updates.\n\n" +
                "This program picks the folder and creates it for you - on the console over the network, or on a USB drive.");

            var how = AddCard("What the console does with it", null, Glyph.Play, g);
            Note(how,
                "Each time the game starts, the console looks in those folders for an update with the game's Title ID and Media ID that was made for the game's version. " +
                "It patches the game's program in memory and adds the update's files on top of the game's own. Nothing on the disc or in the game folder is rewritten.\n\n" +
                "- If more than one fits, the highest version is used.\n" +
                "- To go back to the game as sold, delete the update file. To change update, replace it.\n" +
                "- The same works whether the game runs from a disc, an extracted folder or a Games on Demand install.\n" +
                "- Aurora can also fetch and switch updates itself when the console is online: select the game, press Y, Title Updates.");

            var never = AddCard("Do not break the match", "The one mistake that stops every update from loading.", Glyph.Warning, g);
            Note(never,
                "Never run xextool with -r a on a game. \"a\" means every removal at once, and that includes \"z\", which zeroes the Media ID. " +
                "A game with a zeroed Media ID no longer matches any title update.\n\n" +
                "A modern RGH or JTAG console running freeBOOT starts regular retail games exactly as they are - they do not need that patch. " +
                "If a game really needs one limit removed, remove only that one (for example -r m for media or -r r for region) and leave z out. " +
                "The patches on this program's XEX Tool page keep the Media ID.");

            var wrong = AddCard("If the update does not load", null, Glyph.Help, g);
            Note(wrong,
                "- Wrong Media ID: it is for another pressing of the disc. Check it on the first tab.\n" +
                "- Wrong folder or a renamed file: install it again from the first tab.\n" +
                "- The game's default.xex was already patched with an update (merged): the console only updates the game as sold.\n" +
                "- A console signed in to Xbox LIVE downloads updates by itself; these steps are for consoles that stay offline.");

            // ------------------------------------------------------------ 3. merge
            var b = AddCard("Merge the update into the game's XEX", "For an emulator such as Xenia, a devkit, or when you want one already-updated default.xex to mod. On a console, installing the package (first tab) is the normal way.", Glyph.Download, merge);
            Note(b,
                "1. Open the game's default.xex from your extracted game folder (drop it anywhere in this window).\n" +
                "2. Press \"Extract a title update...\" and choose the package. Extract into the game's folder - the one that holds default.xex - so the update's data files land next to the game's own, in the same sub-folders.\n" +
                "3. Choose the default.xexp that is now in that folder and press Apply. If the update brought other .xexp files, open the game file with the same name and apply each one.\n" +
                "4. For a console, finish on the XEX Tool page (Retail / RGH / exploited).");
            Field(b, null, _xexp, Btn("", Glyph.FolderOpen, () =>
            {
                var f = PickFile("Select the title update patch", "XEX patch (*.xexp)|*.xexp|All files|*.*");
                if (f != null) _xexp.Text = f;
            }));
            _xexp.AllowDrop = true;
            _xexp.Box.AllowDrop = true;
            _xexp.Box.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            _xexp.Box.DragDrop += (s, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) _xexp.Text = f[0]; };
            var applyBtn = Btn("Apply .xexp to the open XEX", Glyph.Check, null, BtnKind.Primary);
            applyBtn.Click += (s, e) => ApplyXexp();
            Buttons(b,
                Btn("Extract a title update...", Glyph.Download, ExtractTu, tip: "Takes every file out of a TU package: default.xexp and the updated game files."),
                applyBtn);
            Note(b,
                "The patch and the update's data files belong together: applying only default.xexp and leaving the other files out gives a game that crashes or misses content. " +
                "A .bak copy of your XEX is kept (see Settings), and files the update replaces in the game folder are kept as .bak too. " +
                "A merged XEX and an installed package for the same game do not mix - use one of the two.", Theme.TextDim);

            Render();
        }

        public override void OnActivated() => TakeWorkingFile();
        public override void OnWorkingFileChanged()
        {
            // a game or an update dropped while this page is open is meant for this page: stay here
            var k = WorkingFile.Kind;
            if (Visible && MainForm.Instance != null && (k == WorkingFileKind.Xex || k == WorkingFileKind.Iso || k == WorkingFileKind.Package)) MainForm.Instance.KeepPage = true;
            TakeWorkingFile();
        }

        /// <summary>A file opened or dropped anywhere in the window lands in the right box by what it is.</summary>
        private void TakeWorkingFile()
        {
            string path = WorkingFile.Path;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Render(); return; }
            switch (WorkingFile.Kind)
            {
                case WorkingFileKind.Xex:
                case WorkingFileKind.Iso:
                    if (_g == null || !Same(_g.Path, path)) SetGame(path);
                    break;
                case WorkingFileKind.Package:
                    if (IsTitleUpdate(path)) { if (_tu == null || !Same(_tu.Path, path)) SetUpdate(path); }
                    else if (_g == null || !Same(_g.Path, path)) SetGame(path);
                    break;
            }
            Render();
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static bool IsTitleUpdate(string path)
        {
            try { var p = StfsPackage.Read(path); return p.Valid && p.ContentType == 0x000B0000; } catch { return false; }
        }

        private void ChooseGame()
        {
            var f = PickFile("Choose the game: default.xex, a disc image or a package", "Xbox 360 game (default.xex, *.iso, packages)|*.xex;*.iso;*|All files|*.*");
            if (f != null) { SetGame(f); Render(); }
        }

        private void ChooseUpdate()
        {
            var f = PickFile("Choose the title update package");
            if (f != null) { SetUpdate(f); Render(); }
        }

        public void SetGame(string path)
        {
            _gameProblem = null;
            try { _g = TitleUpdateCheck.GameFrom(path); }
            catch (Exception ex) { _g = null; _gameProblem = ex.Message; }
        }

        public void SetUpdate(string path)
        {
            _updateProblem = null;
            try { _tu = TitleUpdateCheck.ReadPackage(path); }
            catch (Exception ex) { _tu = null; _updateProblem = ex.Message; }
        }

        /// <summary>The verdict as shown (for tests and for the log).</summary>
        public TuVerdict Verdict => _g != null && _tu != null ? TitleUpdateCheck.Compare(_g, _tu) : null;

        private void Render()
        {
            _game.Clear();
            if (_g != null)
            {
                _game.Set("File", Path.GetFileName(_g.Path));
                _game.Set("Read from", _g.From);
                _game.Set("Title ID", _g.TitleId.ToString("X8"));
                _game.Set("Media ID", _g.MediaId == 0 ? "00000000 - zeroed: no title update will match" : _g.MediaId.ToString("X8"));
                _game.Set("Game version", !_g.VersionKnown ? "not in this file" :
                    TitleUpdateCheck.VersionText(_g.Version) + (TitleUpdateCheck.UpdateNumber(_g.Version) > 0 ? "  (title update " + TitleUpdateCheck.UpdateNumber(_g.Version) + " is merged in)" : "  (the game as sold)"));
            }
            else
            {
                _game.Set("File", _gameProblem == null ? "No game chosen yet" : "Could not be used");
                if (_gameProblem != null) _game.Set("Why", _gameProblem);
            }

            _update.Clear();
            if (_tu != null)
            {
                _update.Set("File", Path.GetFileName(_tu.Path));
                _update.Set("Made for", "Title ID " + _tu.TitleId.ToString("X8") + ",  Media ID " + _tu.MediaId.ToString("X8"));
                _update.Set("It updates", !_tu.IsTitleUpdate ? "not a title update (type " + _tu.ContentType.ToString("X8") + ")" :
                    _tu.HasPatch ? "version " + TitleUpdateCheck.VersionText(_tu.SourceVersion) + "  to  " + TitleUpdateCheck.VersionText(_tu.TargetVersion) + "   (title update " + TitleUpdateCheck.UpdateNumber(_tu.TargetVersion) + ")"
                                 : "game version " + TitleUpdateCheck.VersionText(_tu.PackageBaseVersion) + "  (data files only, no program patch)");
                _update.Set("Inside", _tu.Files + " file(s)" + (_tu.Patches.Count > 0 ? ", patches: " + string.Join(", ", _tu.Patches.Take(4)) + (_tu.Patches.Count > 4 ? ", ..." : "") : ""));
                _update.Set("Goes to", TitleUpdateCheck.Destination(_tu));
            }
            else
            {
                _update.Set("File", _updateProblem == null ? "No title update chosen yet" : "Could not be used");
                if (_updateProblem != null) _update.Set("Why", _updateProblem);
            }

            var v = Verdict;
            if (v != null)
            {
                _verdict.Text = v.Headline;
                _verdict.ForeColor = v.Ok ? Theme.Success : v.Fit == TuFit.AlreadyApplied ? Theme.Warning : Theme.Danger;
                _detail.Text = string.Join("\n", v.Lines) + (v.Ok ? "\nPress Install below and it goes to  " + TitleUpdateCheck.Destination(_tu) + "  - then just start the game and the console applies it." : "");
            }
            else if (_tu != null)
            {
                _verdict.Text = "Step 2 done.  Now choose your game above (step 1) so this can check the update fits it.";
                _verdict.ForeColor = Theme.TextDim;
                _detail.Text = "Your game is its default.xex, its disc image (.iso) or its package.\nWithout that check it can still be installed - it would go to  " + TitleUpdateCheck.Destination(_tu) + ".";
            }
            else if (_g != null)
            {
                _verdict.Text = "Step 1 done.  Now choose the title update you downloaded for this game (step 2).";
                _verdict.ForeColor = Theme.TextDim;
                _detail.Text = "Find it on XboxUnity (button above): look the game up by its Title ID " + _g.TitleId.ToString("X8") +
                    ", then take an update listed under its Media ID " + (_g.MediaId == 0 ? "- but this game's Media ID is zeroed, so no update will match it" : _g.MediaId.ToString("X8")) + ".";
            }
            else
            {
                _verdict.Text = "Do step 1 and step 2 above - then this line tells you whether the update fits your game.";
                _verdict.ForeColor = Theme.TextDim;
                _detail.Text = "1.  Choose your game:  its default.xex, disc image (.iso) or package.\n" +
                               "2.  Choose the title update you downloaded for it.\n" +
                               "Both are only read, never changed. When the line above turns green, press Install on the console or a USB drive.";
            }
            _verdict.Parent?.PerformLayout();
        }

        private void CopyId(bool title)
        {
            if (_g == null) { Dlg.Info("Title update", "Choose the game first - its default.xex, disc image or package."); return; }
            uint id = title ? _g.TitleId : _g.MediaId;
            try { Clipboard.SetText(id.ToString("X8")); Log.Success((title ? "Title ID " : "Media ID ") + id.ToString("X8") + " copied."); }
            catch (Exception ex) { Dlg.Warn("Copy", ex.Message); }
        }

        // ---------------------------------------------------------------- install

        /// <summary>True when the update may be installed: it fits, or the user said to install it regardless.</summary>
        private bool ReadyToInstall()
        {
            if (_tu == null) { Dlg.Info("Title update", "Choose the title update first."); return false; }
            if (!_tu.IsTitleUpdate) { Dlg.Warn("Title update", "That package is not a title update. Use Console Setup to install other content."); return false; }
            var v = Verdict;
            if (v == null || v.Ok) return true;
            return Dlg.Confirm("This update does not fit this game", v.Headline + "\n\n" + string.Join("\n", v.Lines) + "\n\nInstall it anyway?", "Install anyway", true);
        }

        private void InstallOnConsole()
        {
            if (!ReadyToInstall()) return;
            if (!ConsoleService.IsConnected) { Dlg.Info("Not connected", "Connect to the console on the Neighborhood page first, or use a USB drive."); return; }
            Install(InstallTarget.ConsoleXbdm, null, "Hdd:\\");
        }

        private void InstallOnUsb()
        {
            if (!ReadyToInstall()) return;
            var drives = UsbDrives.Detect();
            if (drives.Count == 0) { Dlg.Info("No USB drive found", "Plug in a FAT32 USB drive and press the button again."); return; }
            if (drives.Count > 1) { OpenInSetup(); Log.Info("More than one USB drive is plugged in - pick the one to use on Console Setup."); return; }
            Install(InstallTarget.UsbDrive, drives[0].Root, drives[0].Root);
        }

        private void OpenInSetup()
        {
            if (_tu == null) { Dlg.Info("Title update", "Choose the title update first."); return; }
            MainForm.Instance.Navigate(typeof(SetupPage));
            MainForm.Instance.GetPage<SetupPage>().AddPackages(new[] { _tu.Path });
        }

        private void Install(InstallTarget target, string usbRoot, string shownRoot)
        {
            string path = _tu.Path, where = shownRoot + TitleUpdateCheck.Destination(_tu);
            Run(null, "Installing the title update", async () =>
            {
                var pkg = await Task.Run(() => StfsPackage.Read(path));
                await ContentInstaller.InstallAsync(new[] { pkg }, target, usbRoot, null, "Hdd1", "Hdd", Status.NewProgress(), CancellationToken.None);
                Log.Success("Title update installed: " + where + ". Start the game and the console applies it.");
                _detail.Text = "Installed to  " + where + "\nStart the game - the console applies the update by itself. To remove it later, delete that file.";
            });
        }

        // ---------------------------------------------------------------- merge

        private void ExtractTu()
        {
            string package = _tu != null && File.Exists(_tu.Path) && Dlg.Confirm("Extract a title update", "Extract the update chosen on the first tab?\n\n" + Path.GetFileName(_tu.Path), "Yes, that one")
                ? _tu.Path : PickFile("Choose the title update package");
            if (package == null) return;
            string start = WorkingFile.Kind == WorkingFileKind.Xex && File.Exists(WorkingFile.Path) ? Path.GetDirectoryName(WorkingFile.Path) : null;
            string folder = PickFolder("Extract into which folder? For a merge, choose the game's folder (the one that holds default.xex).", start);
            if (folder != null) ExtractTuTo(package, folder);
        }

        /// <summary>
        /// Extracts every file of a title update package into <paramref name="folder"/>, keeping its sub-folders. A file
        /// that is already there and differs is kept as "name.bak" first (the first time only). Nothing outside the
        /// folder can be written.
        /// </summary>
        public void ExtractTuTo(string package, string folder)
        {
            Run(null, "Extracting the title update", async () =>
            {
                try
                {
                    var result = await Task.Run(() => TitleUpdateExtract.Run(package, folder));
                    Log.Success("Title update extracted to " + folder + ": " + result.Files + " file(s)" + (result.Patches.Count > 0 ? ", patches: " + string.Join(", ", result.Patches) : ", no .xexp inside") +
                                (result.BackedUp > 0 ? ". " + result.BackedUp + " existing file(s) were kept as .bak" : "") + ".");
                    string main = result.Patches.Find(p => string.Equals(Path.GetFileName(p), "default.xexp", StringComparison.OrdinalIgnoreCase)) ?? (result.Patches.Count > 0 ? result.Patches[0] : null);
                    if (main != null) _xexp.Text = Path.Combine(folder, main);
                    if (!result.IsTitleUpdate) Log.Warn("That package is not marked as a title update (content type " + result.ContentType.ToString("X8") + ") - it was extracted anyway.");
                }
                catch (Exception ex) { Dlg.Warn("Extract title update", ex.Message); }
            });
        }

        private void ApplyXexp()
        {
            if (!File.Exists(_xexp.Text)) { Dlg.Warn("Title update", "Choose the default.xexp file first."); return; }
            if (WorkingFile.Kind != WorkingFileKind.Xex && WorkingFile.Kind != WorkingFileKind.Pe)
            {
                Dlg.Warn("No XEX open", "Open or drop the game's default.xex first.");
                return;
            }
            string path = WorkingFile.Path;
            // the patch only fits one exact version of the XEX: say so before xextool is asked to try
            try
            {
                if (TitleUpdateCheck.TryReadPatch(File.ReadAllBytes(_xexp.Text), out uint source, out uint target))
                {
                    uint have = XexFile.Read(path).Version;
                    if (have == target && target != source) { Dlg.Info("Already applied", "This XEX is already version " + TitleUpdateCheck.VersionText(have) + " - this update is merged into it."); return; }
                    if (have != source && !Dlg.Confirm("This patch is for another version",
                            "The patch updates version " + TitleUpdateCheck.VersionText(source) + ", but the open XEX is version " + TitleUpdateCheck.VersionText(have) + ".\n\nIt will most likely be refused. Try anyway?", "Try anyway", true)) return;
                }
            }
            catch (Exception) { /* not readable as a patch: let xextool report it */ }
            var op = WorkingFile.BeginOperation("Applying title update");
            if (op == null) return;
            Run(null, "Applying title update", async () =>
            {
                using (op)
                {
                    BackupOnce(path);
                    var r = await ToolRunner.RunAsync(Tools.XexTool, "-p " + ToolRunner.Q(_xexp.Text) + " " + ToolRunner.Q(path));
                    if (r.Success) Log.Success("Title update applied.");
                    WorkingFile.Refresh();
                }
            });
        }
    }
}

namespace X360GameHack.UI.Pages
{
    /// <summary>Takes a title update package apart into a folder (usually the game's folder).</summary>
    public static class TitleUpdateExtract
    {
        public sealed class Result
        {
            public int Files, BackedUp;
            public uint ContentType;
            public bool IsTitleUpdate => ContentType == 0x000B0000;
            /// <summary>The .xexp patches that were extracted (paths inside the folder).</summary>
            public System.Collections.Generic.List<string> Patches = new System.Collections.Generic.List<string>();
        }

        public static Result Run(string package, string folder)
        {
            var info = X360GameHack.Core.Formats.StfsPackage.Read(package);
            if (!info.Valid) throw new System.IO.InvalidDataException("That file is not an Xbox 360 package.");
            var r = new Result { ContentType = info.ContentType };
            string root = System.IO.Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            System.IO.Directory.CreateDirectory(root);
            using (var v = X360GameHack.Core.Formats.StfsVolume.Open(package))
            {
                foreach (var e in v.Entries)
                {
                    if (e.IsDirectory) continue;
                    string relative = e.Path.Replace('/', '\\').TrimStart('\\');
                    string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new System.IO.InvalidDataException("The package holds a file name that points outside the folder: " + e.Path);
                    byte[] data = v.ReadFile(e);
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                    if (System.IO.File.Exists(target))
                    {
                        byte[] existing = new System.IO.FileInfo(target).Length == data.Length ? System.IO.File.ReadAllBytes(target) : null;
                        bool same = existing != null && System.Linq.Enumerable.SequenceEqual(existing, data);
                        if (!same && !System.IO.File.Exists(target + ".bak")) { System.IO.File.Copy(target, target + ".bak"); r.BackedUp++; }
                        if (same) { r.Files++; if (relative.EndsWith(".xexp", StringComparison.OrdinalIgnoreCase)) r.Patches.Add(relative); continue; }
                    }
                    System.IO.File.WriteAllBytes(target, data);
                    r.Files++;
                    if (relative.EndsWith(".xexp", StringComparison.OrdinalIgnoreCase)) r.Patches.Add(relative);
                }
            }
            return r;
        }
    }
}
