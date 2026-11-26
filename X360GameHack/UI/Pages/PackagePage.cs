using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>Shared pieces of the Package and Save pages (both are STFS files).</summary>
    public abstract class StfsPageBase : PageBase
    {
        protected readonly KvGrid Info = new KvGrid();
        protected readonly PictureBox Thumb = new PictureBox { Width = Theme.S(64), Height = Theme.S(64), SizeMode = PictureBoxSizeMode.Zoom };
        protected readonly TextBlock Headline = new TextBlock("", Theme.CardTitle, Theme.Text) { Height = Theme.S(60), Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak };
        protected TextBlock EmptyNote;
        protected PackageContentsCard Contents;
        protected RetailSignCard Signing;
        protected VStack SignTab;
        protected Control[] NeedsFile = new Control[0];
        protected StfsPackage Pkg;
        protected abstract WorkingFileKind Kind { get; }

        protected Card BuildInfoCard(Control parent, string title)
        {
            var card = AddCard(title, "Read from the STFS header. Double-click a value to copy it.", Glyph.Info, parent);
            var row = new HStack { Height = Theme.S(70), Spacing = Theme.S(14) };
            row.Add(Thumb); row.Add(Headline); row.FillChild = Headline;
            card.Add(row);
            card.Add(Info);
            card.AddAction("Refresh", Glyph.Refresh, (s, e) => Reload());
            card.AddAction("Full info", Glyph.List, (s, e) => PkgTool("-p", "Reading package info", false));
            return card;
        }

        protected RetailSignCard BuildSigning(Control parent)
        {
            Signing = new RetailSignCard();
            parent.Controls.Add(Signing);
            parent.Controls.Add(new IdPickerCard(Signing));
            Signing.NeedsAttention += bad => Tabs?.SetBadge(SignTab, bad ? Theme.Danger : (System.Drawing.Color?)null);
            return Signing;
        }

        protected bool HasFile()
        {
            if (WorkingFile.Has(Kind)) return true;
            Dlg.Warn("No file loaded", Kind == WorkingFileKind.Save ? "Open or drop a save game first." : "Open or drop an STFS package first.");
            return false;
        }

        protected void PkgTool(string args, string what, bool modifies = true)
        {
            if (!HasFile()) return;
            string path = WorkingFile.Path;
            var op = modifies ? WorkingFile.BeginOperation(what) : null;
            if (modifies && op == null) return;
            Run(null, what, async () =>
            {
                using (op)
                {
                    if (modifies) BackupOnce(path);
                    var r = await ToolRunner.RunAsync(Tools.PkgTool, args + " " + ToolRunner.Q(path));
                    if (modifies)
                    {
                        if (r.Success) Log.Success(what + " - done.");
                        WorkingFile.Refresh();
                    }
                }
            });
        }

        public override void OnWorkingFileChanged()
        {
            ShowState();
            if (WorkingFile.Kind == Kind) Reload();
        }

        public override void OnActivated()
        {
            ShowState();
            if (WorkingFile.Kind == Kind && (Pkg == null || Pkg.Path != WorkingFile.Path)) Reload();
        }

        protected virtual void ShowState()
        {
            bool has = WorkingFile.Kind == Kind;
            // a tab that holds a page of its own (create a save, GPD viewer) has nothing to do with the open file
            if (EmptyNote != null) EmptyNote.Visible = !has && !HostedSelected;
            foreach (var c in NeedsFile) c.Enabled = has;
        }

        protected override void OnSectionChanged() => ShowState();

        protected void Reload()
        {
            string path = WorkingFile.Path;
            Run(null, "Reading package", async () =>
            {
                var p = await Task.Run(() => StfsPackage.Read(path));
                if (path != WorkingFile.Path) return;
                Pkg = p;
                Thumb.Image?.Dispose();
                Thumb.Image = p.Thumbnail != null ? (System.Drawing.Image)p.Thumbnail.Clone()
                    : p.TitleThumbnail != null ? (System.Drawing.Image)p.TitleThumbnail.Clone()
                    : GlyphImage.Render(Kind == WorkingFileKind.Save ? Glyph.Save : Glyph.Package, Theme.TextFaint, Theme.S(64));
                string name = !string.IsNullOrEmpty(p.DisplayName) ? p.DisplayName : WorkingFile.Name;
                Headline.Text = name + "\n" + (string.IsNullOrEmpty(p.TitleName) ? "" : p.TitleName + "  •  ") + p.ContentTypeName;
                Info.Clear();
                FillInfo(p);
                foreach (var (box, current) in _prefill) box.Text = current() ?? "";
                Contents?.Load(path);
                Signing?.Load(path);
                Body.PerformLayout();
            });
        }

        protected virtual void FillInfo(StfsPackage p)
        {
            Info.Set("Signature", p.Signature);
            Info.Set("Content type", p.ContentTypeName + "  (" + p.ContentTypeHex + ")");
            Info.Set("Title ID", p.TitleIdHex);
            Info.Set("Media ID", p.MediaId.ToString("X8"));
            Info.Set("Title name", p.TitleName);
            Info.Set("Display name", p.DisplayName);
            Info.Set("Publisher", p.Publisher);
            Info.Set("Version", p.Version.ToString("X8"));
            Info.Set("Base version", p.BaseVersion.ToString("X8"));
            Info.Set("Disc", p.DiscCount > 0 ? p.DiscNumber + " of " + p.DiscCount : "");
            Info.Set("Console ID", p.ConsoleId);
            Info.Set("Profile ID", p.ProfileId);
            Info.Set("Device ID", p.DeviceId);
            Info.Set("Install path", p.InstallDir);
            Info.Set("Data folder", p.HasDataFolder ? "Yes (" + FileSniffer.FormatSize(FtpService.MeasureLocal(p.Path + ".data")) + ")" : "No");
        }

        private readonly System.Collections.Generic.List<(XTextBox Box, Func<string> Current)> _prefill =
            new System.Collections.Generic.List<(XTextBox, Func<string>)>();

        /// <summary>Fills <paramref name="box"/> from the open package every time it is (re)read.</summary>
        protected void Prefill(XTextBox box, Func<string> current) => _prefill.Add((box, current));

        /// <summary>
        /// Native name change: writes the display name and game name into the header, then rehashes and re-signs.
        /// One click; a failure leaves the file as it was.
        /// </summary>
        protected async void ApplyNames(Control trigger, string display, string title)
        {
            if (!HasFile()) return;
            if (display.Length == 0) { Dlg.Warn("Display name", "Enter a display name."); return; }
            if (display.Length > 127 || title.Length > 63) { Dlg.Warn("Names", "The display name is at most 127 characters and the game name at most 63."); return; }
            string path = WorkingFile.Path;
            var op = WorkingFile.BeginOperation("Changing the names");
            if (op == null) return;
            bool written = false;
            try
            {
                await RunAsync(trigger, "Changing the names", async () =>
                {
                    BackupOnce(path);
                    await Task.Run(() => StfsEditor.SetNames(path, display, title));
                    written = true;
                    Log.Success("Names changed: \"" + display + "\"" + (title.Length > 0 ? " / \"" + title + "\"" : "") + ".");
                });
            }
            finally { op.Dispose(); }
            if (written && Signing != null) await Signing.RehashAndResignAsync();
            if (WorkingFile.Path == path) WorkingFile.Refresh();
        }

        /// <summary>Native header change (names, description, publisher, pictures), then rehash and re-sign. A failure leaves the file as it was.</summary>
        protected async void ApplyMetadata(Control trigger, string what, StfsMetadata m)
        {
            if (!HasFile()) return;
            string path = WorkingFile.Path;
            var op = WorkingFile.BeginOperation(what);
            if (op == null) return;
            bool written = false;
            try
            {
                await RunAsync(trigger, what, async () =>
                {
                    BackupOnce(path);
                    await Task.Run(() => StfsEditor.SetMetadata(path, m));
                    written = true;
                    Log.Success(what + ": done.");
                });
            }
            finally { op.Dispose(); }
            if (written && Signing != null) await Signing.RehashAndResignAsync();
            if (WorkingFile.Path == path) WorkingFile.Refresh();
        }

        /// <summary>Lets the user pick any image and returns it as a 64x64 PNG that fits a package header, or null.</summary>
        protected static byte[] PickPicture()
        {
            string f = PickFile("Choose a picture (it is resized to 64x64)", "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*");
            if (f == null) return null;
            try { return PackagePicture(f); }
            catch (Exception ex) { Dlg.Warn("Picture", ex.Message); return null; }
        }

        /// <summary>Any image file as a 64x64 PNG that fits a package header (at most <see cref="StfsEditor.MaxPictureBytes"/>).</summary>
        public static byte[] PackagePicture(string imagePath) => SquarePng(imagePath, 64, false);

        /// <summary>
        /// Any image file as a size x size PNG that fits a package header (at most <see cref="StfsEditor.MaxPictureBytes"/>). With <paramref name="crop"/> the proportions are kept
        /// and the longer side is cut evenly; otherwise the image is stretched.
        /// </summary>
        public static byte[] SquarePng(string imagePath, int size, bool crop)
        {
            if (new System.IO.FileInfo(imagePath).Length > 32 * 1024 * 1024) throw new InvalidOperationException("That file is too large to be used as a picture.");
            using (var source = System.Drawing.Image.FromFile(imagePath))
            {
                int side = Math.Min(source.Width, source.Height);
                var from = crop ? new System.Drawing.Rectangle((source.Width - side) / 2, (source.Height - side) / 2, side, side)
                                : new System.Drawing.Rectangle(0, 0, source.Width, source.Height);
                foreach (var format in new[] { System.Drawing.Imaging.PixelFormat.Format32bppArgb, System.Drawing.Imaging.PixelFormat.Format24bppRgb })
                    using (var bmp = new System.Drawing.Bitmap(size, size, format))
                    {
                        using (var g = System.Drawing.Graphics.FromImage(bmp))
                        using (var edges = new System.Drawing.Imaging.ImageAttributes())
                        {
                            edges.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);   // no dark fringe at the borders
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            if (format == System.Drawing.Imaging.PixelFormat.Format24bppRgb) g.Clear(System.Drawing.Color.Black);
                            g.DrawImage(source, new System.Drawing.Rectangle(0, 0, size, size), from.X, from.Y, from.Width, from.Height, System.Drawing.GraphicsUnit.Pixel, edges);
                        }
                        using (var ms = new System.IO.MemoryStream())
                        {
                            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            if (ms.Length <= StfsEditor.MaxPictureBytes) return ms.ToArray();
                        }
                    }
            }
            throw new InvalidOperationException("That picture is too detailed to fit in a package header (" + StfsEditor.MaxPictureBytes.ToString("N0") + " bytes) at " + size + "x" + size + ". Try a simpler image.");
        }

        protected HStack StringField(Control parent, string label, string flag, Func<string> current)
        {
            var box = new XTextBox();
            _prefill.Add((box, current));
            var set = Btn("Set", Glyph.Check, null);
            set.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(box.Text)) { Dlg.Warn(label, "Enter a value first."); return; }
                PkgTool(flag + " " + ToolRunner.Q(box.Text.Trim()), "Setting " + label.ToLowerInvariant());
            };
            return Field(parent, label, box, set);
        }

        protected void HexIdField(Control parent, string label, string flag, int digits, string hint, Func<string> current)
        {
            var box = new XTextBox(hint, false, true);
            _prefill.Add((box, current));
            var set = Btn("Set", Glyph.Check, null);
            set.Click += (s, e) =>
            {
                string v = new string(box.Text.Where(Uri.IsHexDigit).ToArray());
                if (v.Length != digits) { Dlg.Warn(label, $"Enter exactly {digits} hex digits."); return; }
                PkgTool(flag + " " + v, "Setting " + label.ToLowerInvariant());
            };
            Field(parent, label, box, set);
        }

        protected void InstallToConsole()
        {
            if (!HasFile()) return;
            MainForm.Instance.Navigate(typeof(SetupPage));
            MainForm.Instance.GetPage<SetupPage>().AddPackages(new[] { WorkingFile.Path });
        }
    }

    public sealed class PackagePage : StfsPageBase
    {
        public override string Title => "X360Packages";
        public override string Subtitle => "Games on Demand, XBLA, DLC, title updates, themes and more - inspect, edit, re-sign and install.";
        public override string PageGlyph => Glyph.Package;
        protected override WorkingFileKind Kind => WorkingFileKind.Package;

        private readonly Segmented _signing = new Segmented("Unchanged", "CON", "LIVE", "PIRS");
        private readonly (string Flag, string Label, Segmented Choice)[] _flags =
        {
            ("-fp", "Transfer between profiles", new Segmented("Unchanged", "Allow", "Deny")),
            ("-fd", "Transfer between devices", new Segmented("Unchanged", "Allow", "Deny")),
            ("-fn", "Network storage", new Segmented("Unchanged", "Allow", "Deny")),
            ("-fl", "Deep link", new Segmented("Unchanged", "Allow", "Deny")),
            ("-fm", "Move-only transfers", new Segmented("Unchanged", "Yes", "No")),
            ("-fk", "Kinect enabled", new Segmented("Unchanged", "Yes", "No")),
        };
        private readonly XTextBox _custom = new XTextBox("e.g. -z -b 1 -m 1", false, true);

        public PackagePage()
        {
            EmptyNote = Note(Body, "Open or drop an STFS package (CON / LIVE / PIRS - usually a file with no extension) to use these tools.", Theme.Warning);

            // ---------------------------------------------------------------- Overview
            var overview = AddSection("Overview", Glyph.Info);
            var top = AddColumns(2, 420, overview);
            BuildInfoCard(top, "Package");
            var easy = AddCard("Quick actions", "A .bak backup is made first (see Settings).", Glyph.Flash, top);
            Buttons(easy,
                Btn("Unlock DLC for RGH / exploited", Glyph.Unlock, () => PkgTool("-z -b 1 -m 1", "Unlocking DLC (retail)"), BtnKind.Primary,
                    "Remove purchase traces, mark as bought and set the retail machine format (-z -b 1 -m 1)."),
                Btn("Unlock DLC for devkit", Glyph.Unlock, () => PkgTool("-z -b 1 -m 0", "Unlocking DLC (devkit)"), BtnKind.Secondary,
                    "Remove purchase traces, mark as bought and set the devkit machine format (-z -b 1 -m 0)."));
            Buttons(easy,
                Btn("Make retail / RGH", null, () => PkgTool("-m 1", "Setting retail format"), tip: "-m 1"),
                Btn("Make devkit", null, () => PkgTool("-m 0", "Setting devkit format"), tip: "-m 0"),
                Btn("Make bought", null, () => PkgTool("-b 1", "Marking as bought"), tip: "-b 1"),
                Btn("Make trial", null, () => PkgTool("-b 0", "Marking as trial"), tip: "-b 0"),
                Btn("Remove purchase traces", Glyph.Shield, () => PkgTool("-z", "Removing purchase traces"), tip: "-z: removes the IDs of the console that downloaded it"));
            Buttons(easy,
                Btn("Install to USB / console...", Glyph.Usb, InstallToConsole, BtnKind.Secondary),
                Btn("Open folder", Glyph.FolderOpen, () => { if (HasFile()) Tools.OpenFolder(WorkingFile.Path); }));

            // ---------------------------------------------------------------- Contents
            var contents = AddSection("Contents", Glyph.FolderOpen);
            Contents = new PackageContentsCard();
            contents.Add(Contents);

            // ---------------------------------------------------------------- Edit
            var edit = AddSection("Edit", Glyph.Edit);
            var meta = AddColumns(2, 420, edit);
            var strings = AddCard("Metadata", "What the dashboard shows for this package. One click writes it, then rehashes and re-signs - built in, no external tool.", Glyph.Tag, meta);
            XTextBox mTitle = new XTextBox("Game name (up to 63 characters)"), mDisplay = new XTextBox("Display name (up to 127 characters)"),
                     mDescription = new XTextBox("Description (up to 127 characters)"), mPublisher = new XTextBox("Publisher (up to 63 characters)");
            Prefill(mTitle, () => Pkg?.TitleName); Prefill(mDisplay, () => Pkg?.DisplayName); Prefill(mDescription, () => Pkg?.Description); Prefill(mPublisher, () => Pkg?.Publisher);
            Field(strings, "Game name", mTitle);
            Field(strings, "Display name", mDisplay);
            Field(strings, "Description", mDescription);
            Field(strings, "Publisher", mPublisher);
            XButton applyMeta = null, setPicture = null, setGamePicture = null;
            applyMeta = Btn("Apply", Glyph.Check, () => ApplyMetadata(applyMeta, "Changing the package details", new StfsMetadata
            {
                TitleName = mTitle.Text.Trim(), DisplayName = mDisplay.Text.Trim(), Description = mDescription.Text.Trim(), Publisher = mPublisher.Text.Trim()
            }), BtnKind.Primary);
            setPicture = Btn("Set picture...", Glyph.Image, () => { var png = PickPicture(); if (png != null) ApplyMetadata(setPicture, "Changing the package picture", new StfsMetadata { ThumbnailPng = png }); },
                             tip: "The picture shown for this item. Any image file; it is resized to 64x64.");
            setGamePicture = Btn("Set game picture...", Glyph.Game, () => { var png = PickPicture(); if (png != null) ApplyMetadata(setGamePicture, "Changing the game picture", new StfsMetadata { TitleThumbnailPng = png }); },
                                 tip: "The picture of the game the item belongs to. Any image file; it is resized to 64x64.");
            Buttons(strings, applyMeta, setPicture, setGamePicture);

            var creator = AddCard("Creator IDs", "Re-assign the package to your console / profile (X360PkgTool). For saves and profiles use the Rehash & resign tab - it also re-signs.", Glyph.Console, meta);
            HexIdField(creator, "Console ID", "-cc", 10, "10 hex digits", () => Pkg?.ConsoleId);
            HexIdField(creator, "Profile ID", "-cp", 16, "16 hex digits", () => Pkg?.ProfileId);
            var devBox = new XTextBox("Device ID string");
            var devSet = Btn("Set", Glyph.Check, null);
            devSet.Click += (s, e) => { if (devBox.Text.Trim().Length > 0) PkgTool("-cd " + ToolRunner.Q(devBox.Text.Trim()), "Setting device ID"); };
            Field(creator, "Device ID", devBox, devSet);

            var flags = AddCard("Signing & license flags", "Only the options you change are sent to X360PkgTool. CON signing with X360PkgTool needs key00033.bin / key00036.bin next to the tool.", Glyph.Lock, edit);
            Field(flags, "Signing", _signing).FillChild = null;
            foreach (var f in _flags) Field(flags, f.Label, f.Choice).FillChild = null;
            Buttons(flags, Btn("Apply flags", Glyph.Check, ApplyFlags, BtnKind.Primary));

            // ---------------------------------------------------------------- Rehash & resign
            SignTab = AddSection("Rehash & resign", Glyph.Shield);
            BuildSigning(SignTab);

            // ---------------------------------------------------------------- Advanced
            var adv = AddSection("Advanced", Glyph.Code);
            var custom = AddCard("Custom X360PkgTool command", "Runs X360PkgTool with your arguments on the open package (a .bak backup is made first).", Glyph.Code, adv);
            _custom.Text = AppSettings.Current.PkgCustomCommand;
            Field(custom, null, _custom);
            Buttons(custom,
                Btn("Run", Glyph.Play, () => PkgTool(_custom.Text.Trim(), "Running custom X360PkgTool command"), BtnKind.Primary),
                Btn("Save as default", Glyph.Save, () => { AppSettings.Current.PkgCustomCommand = _custom.Text.Trim(); AppSettings.Save(); Log.Success("Saved."); }));

            NeedsFile = new Control[] { top, contents, edit, SignTab, adv };
            ShowState();
        }

        private void ApplyFlags()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (_signing.SelectedIndex > 0) parts.Add("-t " + "clp"[_signing.SelectedIndex - 1]);
            foreach (var f in _flags)
                if (f.Choice.SelectedIndex > 0) parts.Add(f.Flag + " " + (f.Choice.SelectedIndex == 1 ? "1" : "0"));
            if (parts.Count == 0) { Dlg.Info("Nothing to do", "Change at least one option first."); return; }
            if (_signing.SelectedIndex > 1 &&
                !Dlg.Confirm("Signing", "LIVE and PIRS packages only run on retail consoles with Microsoft's signature or on a patched (RGH/devkit) system. Continue?", "Continue"))
                return;
            PkgTool(string.Join(" ", parts), "Applying package flags");
            _signing.SelectedIndex = 0;
            foreach (var f in _flags) f.Choice.SelectedIndex = 0;
        }
    }

    public sealed class SavePage : StfsPageBase
    {
        public override string Title => "Saves";
        public override string Subtitle => "Edit, re-assign and re-sign saves for stock or modded consoles, and browse every save on a console or drive.";
        public override string PageGlyph => Glyph.Save;
        protected override WorkingFileKind Kind => WorkingFileKind.Save;

        private readonly VStack _saveTab, _browseTab;
        private readonly SaveBrowser _browser = new SaveBrowser("Pick where to look",
            "Choose your console (connected in Neighborhood) or a folder on this PC above. For an Xbox hard drive connected to this PC use Drive Explorer > Saves.")
        { Height = Theme.S(560) };
        private readonly TextBlock _source = new TextBlock("", Theme.Small, Theme.TextDim);
        private readonly GameSaveCard _game = new GameSaveCard();

        private void LoadGameValues()
        {
            if (WorkingFile.IsRefresh && _game.Dirty) return;      // do not throw away values that were changed but not written yet
            _game.Load(WorkingFile.Has(WorkingFileKind.Save) ? WorkingFile.Path : null);
        }

        public SavePage()
        {
            EmptyNote = Note(Body, "No save is open. Drop a save file anywhere, or find one under Browse saves.", Theme.Warning);

            // ---------------------------------------------------------------- Save
            _saveTab = AddSection("Save", Glyph.Save);
            var top = AddColumns(2, 420, _saveTab);
            BuildInfoCard(top, "Save game");
            var steps = AddCard("Getting a save to load", null, Glyph.Help, top);
            Note(steps,
                "1. The Title ID (and often the Media ID) must match the game you play - change them on the XEX Tool page if needed.\n" +
                "2. Edit files on the Contents tab - the package is rehashed and verified automatically.\n" +
                "3. Edited it elsewhere, or moving it to another profile? Click Rehash & resign (set the profile ID on the Rehash & resign tab first).\n" +
                "4. Install it with Console Setup, or Put back if it came from your console.");
            Buttons(steps,
                Btn("Install save...", Glyph.Usb, InstallToConsole, BtnKind.Primary),
                Btn("Rehash & resign", Glyph.Check, () => { if (HasFile()) Signing?.RehashAndResign(); }, tip: "Fix every hash and re-sign this save - one click"),
                Btn("Full info", Glyph.List, () => PkgTool("-p", "Reading save info", false)),
                Btn("Open folder", Glyph.FolderOpen, () => { if (HasFile()) Tools.OpenFolder(WorkingFile.Path); }));

            var names = AddCard("Names", "What the dashboard shows for this save. One click writes them, then rehashes and re-signs the save.", Glyph.Tag, _saveTab);
            var display = new XTextBox("Display name (the save's own name)");
            var game = new XTextBox("Game name");
            Prefill(display, () => Pkg?.DisplayName);
            Prefill(game, () => Pkg?.TitleName);
            Field(names, "Display name", display);
            Field(names, "Game name", game);
            XButton apply = null;
            apply = Btn("Apply names", Glyph.Check, () => ApplyNames(apply, display.Text.Trim(), game.Text.Trim()), BtnKind.Primary);
            Buttons(names, apply);

            // ---------------------------------------------------------------- Contents
            var contents = AddSection("Contents", Glyph.FolderOpen);
            Contents = new PackageContentsCard();
            contents.Add(Contents);

            // ---------------------------------------------------------------- Game editor (named values, for the games whose format is known)
            var gameTab = AddSection("Game editor", Glyph.Game);
            gameTab.Add(_game);

            // ---------------------------------------------------------------- Rehash & resign
            SignTab = AddSection("Rehash & resign", Glyph.Shield);
            BuildSigning(SignTab);

            // ---------------------------------------------------------------- Browse
            _browseTab = AddSection("Browse saves", Glyph.Search);
            var pick = AddCard("Where to look", "Lists every save with the game and profile it belongs to. Open one to edit it - after re-signing, Put back sends it home.", Glyph.Search, _browseTab);
            Buttons(pick,
                Btn("My console (XBDM)", Glyph.Network, BrowseConsole, BtnKind.Primary),
                Btn("A folder on this PC...", Glyph.FolderOpen, BrowseFolder),
                Btn("An Xbox hard drive...", Glyph.Hdd, () =>
                {
                    MainForm.Instance.Navigate(typeof(DriveExplorerPage));
                    MainForm.Instance.GetPage<DriveExplorerPage>().ShowSaves();
                }));
            pick.Add(_source);
            _browseTab.Add(_browser);

            // ---------------------------------------------------------------- Convert
            var convert = AddSection("Convert", Glyph.Wrench);
            var patches = AddCard("Convert with X360PkgTool", "Switch the machine format or strip console traces. A .bak backup is made first. A console save (CON) needs a console certificate and key next to X360PkgTool (key00033.bin / key00036.bin) - otherwise use Rehash & resign.", Glyph.Flash, convert);
            Buttons(patches,
                Btn("Make save RGH / retail", Glyph.Play, () => ConvertWithPkgTool("-m 1", "Converting save to retail"), BtnKind.Primary, "-m 1"),
                Btn("Make save devkit", Glyph.Play, () => ConvertWithPkgTool("-m 0", "Converting save to devkit"), BtnKind.Secondary, "-m 0"),
                Btn("Remove console traces", Glyph.Shield, () => ConvertWithPkgTool("-z", "Removing traces"), tip: "-z"));

            // ---------------------------------------------------------------- Create a save / GPD viewer (whole pages shown as tabs)
            AddPageSection("Create a save", Glyph.Add, new PackageCreatorPage());
            AddPageSection("GPD viewer", Glyph.List, new GpdPage());

            NeedsFile = new Control[] { top, contents, gameTab, SignTab, convert };
            if (!WorkingFile.Has(WorkingFileKind.Save)) ShowSection(_browseTab);
            ShowState();
            LoadGameValues();
        }

        /// <summary>
        /// X360PkgTool writes a CON package (every console save) only with a console certificate and key next to it
        /// (key00033.bin / key00036.bin, which X360GameHack never ships); without them it prints its banner and refuses.
        /// Say so before running it, and offer Rehash &amp; resign, which signs with the user's own keyvault.
        /// </summary>
        private void ConvertWithPkgTool(string args, string what)
        {
            if (!HasFile()) return;
            var pkg = Pkg;
            if (pkg != null && pkg.Path == WorkingFile.Path && pkg.Signature == "CON" && !PkgToolCanSignCon())
            {
                if (Dlg.Confirm("A console key is needed",
                        "X360PkgTool can only change a console save (CON) when a console certificate and key - key00033.bin and key00036.bin - are next to it, and X360GameHack never ships them.\n\n" +
                        "Rehash & resign does this job with your own keyvault instead. Open it?", "Open Rehash & resign"))
                    ShowSection(SignTab);
                return;
            }
            PkgTool(args, what);
        }

        private static bool PkgToolCanSignCon()
        {
            string dir = System.IO.Path.GetDirectoryName(Tools.PkgTool);
            return System.IO.File.Exists(System.IO.Path.Combine(dir, "key00033.bin")) && System.IO.File.Exists(System.IO.Path.Combine(dir, "key00036.bin"));
        }

        public override void OnWorkingFileChanged()
        {
            base.OnWorkingFileChanged();
            LoadGameValues();
            if (WorkingFile.Kind == WorkingFileKind.Save && !WorkingFile.IsRefresh && Tabs.SelectedPanel == _browseTab) ShowSection(_saveTab);
        }

        private void BrowseConsole()
        {
            if (!ConsoleService.IsConnected)
            {
                if (Dlg.Confirm("Not connected", "Connect to your console first (xbdm.xex must be running). Open Neighborhood now?", "Open Neighborhood"))
                {
                    MainForm.Instance.Navigate(typeof(NeighborhoodPage));
                    MainForm.Instance.GetPage<NeighborhoodPage>().ShowSaves();
                }
                return;
            }
            _browser.SetStore(new XbdmContentStore());
            _source.Text = "Showing: " + _browser.Store.Title;
            _browser.Scan();
        }

        private void BrowseFolder()
        {
            string dir = PickFolder("Pick a folder that contains a Content folder (or the Content folder itself)");
            if (dir == null) return;
            var store = new LocalContentStore(dir);
            if (store.ContentRootsAsync(System.Threading.CancellationToken.None).Result.Count == 0)
            {
                Dlg.Warn("No Content folder", "That folder has no Content folder. Pick the folder that contains Content\\<profile ID>\\<title ID>\\00000001.");
                return;
            }
            _browser.SetStore(store);
            _source.Text = "Showing: " + dir;
            _browser.Scan();
        }
    }
}
