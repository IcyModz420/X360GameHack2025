using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>
    /// Builds a new CON package (a save, for instance) from files: details typed in or copied from an existing
    /// package of the same game, then one click. The result is verified, signed when a keyvault is loaded, and opened.
    /// </summary>
    public sealed class PackageCreatorPage : PageBase
    {
        public override string Title => "Create Package";
        public override string Subtitle => "Build a new save or content package from your own files - hashed, verified and ready to sign.";
        public override string PageGlyph => Glyph.Add;

        private readonly XTextBox _type = new XTextBox("8 hex digits", false, true) { Width = Theme.S(140) };
        private readonly TextBlock _typeName = new TextBlock("", Theme.Body, Theme.TextDim) { Width = Theme.S(320), Height = Theme.S(30), Flags = TextFormatFlags.VerticalCenter };
        private readonly XTextBox _titleId = new XTextBox("8 hex digits, e.g. 4D5307E6", false, true) { Width = Theme.S(240) };
        private readonly XTextBox _display = new XTextBox("What the dashboard lists (up to 127 characters)");
        private readonly XTextBox _game = new XTextBox("The game's name (up to 63 characters)");
        private readonly XTextBox _description = new XTextBox("Optional (up to 127 characters)");
        private readonly XTextBox _publisher = new XTextBox("Optional (up to 63 characters)");
        private readonly XTextBox _profile = new XTextBox("16 hex digits - the profile it belongs to (optional)", false, true);
        private readonly XTextBox _device = new XTextBox("40 hex digits (optional)", false, true);
        private readonly XTextBox _console = new XTextBox("10 hex digits (optional)", false, true);
        private readonly PictureBox _picture = new PictureBox { Width = Theme.S(64), Height = Theme.S(64), SizeMode = PictureBoxSizeMode.Zoom };
        private readonly TextBlock _templateNote = new TextBlock("", Theme.Small, Theme.TextDim);
        private readonly ListView _files = ListStyle.Create(("Name in the package", 260), ("Size", 100), ("From", 520));
        private readonly Dictionary<string, string> _sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private StfsCreateOptions _template = new StfsCreateOptions();
        private XButton _create;

        public PackageCreatorPage()
        {
            var cols = AddColumns(2, 440);
            var what = AddCard("What it is", "Type the details, or copy them from an existing package of the same game (a save you already have, for example).", Glyph.Tag, cols);
            Buttons(what, Btn("Copy details from a package...", Glyph.Copy, CopyFrom, BtnKind.Primary, "Takes type, title ID, names, pictures and IDs from another save or package."));
            what.Add(_templateNote);
            Field(what, "Content type", _type, _typeName).FillChild = null;
            Field(what, "Title ID", _titleId).FillChild = null;
            Field(what, "Display name", _display);
            Field(what, "Game name", _game);
            Field(what, "Description", _description);
            Field(what, "Publisher", _publisher);
            _type.Text = "00000001";
            _type.TextChanged += (s, e) => ShowType();
            ShowType();

            var who = AddCard("Who it belongs to", "A save only loads for the profile whose ID it carries. Leave these empty for content that is not tied to a profile.", Glyph.Profile, cols);
            Field(who, "Profile ID", _profile, Btn("From a save or profile...", Glyph.Copy, TakeIds));
            Field(who, "Device ID", _device);
            Field(who, "Console ID", _console);
            var picRow = new HStack { Height = Theme.S(64) };
            picRow.Add(_picture);
            picRow.Add(Btn("Set picture...", Glyph.Image, SetPicture, tip: "Any image file; it is resized to 64x64."));
            picRow.Add(Btn("No picture", Glyph.Clear, () => { _template.ThumbnailPng = null; ShowPicture(); }));
            who.Add(picRow);

            var files = AddCard("Files", "The files to put inside. Names are at most 40 plain characters (letters, digits and common symbols).", Glyph.FolderOpen);
            files.AddAction("Add files", Glyph.Add, (s, e) => AddFiles());
            files.AddAction("Remove", Glyph.Remove, (s, e) => { foreach (ListViewItem i in _files.SelectedItems.Cast<ListViewItem>().ToList()) { _sources.Remove(i.Text); _files.Items.Remove(i); } });
            files.AddAction("Clear", Glyph.Clear, (s, e) => { _sources.Clear(); _files.Items.Clear(); });
            _files.Height = Theme.S(220);
            _files.AllowDrop = true;
            _files.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            _files.DragDrop += (s, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f) AddFiles(f); };
            files.Add(_files);
            _create = Btn("Create package...", Glyph.Package, Create, BtnKind.Primary);
            Buttons(files, _create);
            Note(files, "The new package has valid hashes. It is signed with your keyvault when one is loaded (Rehash & resign); without one it loads on RGH / JTAG consoles.");
        }

        private void ShowType()
        {
            _typeName.Text = uint.TryParse(_type.Text.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint t) && _type.Text.Trim().Length == 8
                ? StfsPackage.TypeName(t) : "8 hex digits (00000001 = saved game)";
        }

        private void ShowPicture()
        {
            _picture.Image?.Dispose();
            _picture.Image = null;
            if (_template.ThumbnailPng == null) return;
            try { using (var ms = new MemoryStream(_template.ThumbnailPng)) using (var img = System.Drawing.Image.FromStream(ms)) _picture.Image = new System.Drawing.Bitmap(img); }
            catch { /* not a picture GDI+ can show; it is still written as given */ }
        }

        private void CopyFrom()
        {
            string f = PickFile("Choose a package to copy the details from");
            if (f != null) LoadTemplate(f);
        }

        /// <summary>Fills the page with the details of an existing package.</summary>
        public void LoadTemplate(string f)
        {
            try
            {
                var o = StfsCreateOptions.From(f);
                _template = o;
                _type.Text = o.ContentType.ToString("X8");
                _titleId.Text = o.TitleId.ToString("X8");
                _display.Text = o.DisplayName; _game.Text = o.TitleName; _description.Text = o.Description; _publisher.Text = o.Publisher;
                _profile.Text = o.ProfileId.All(b => b == 0) ? "" : BE.Hex(o.ProfileId, 0, 8);
                _device.Text = o.DeviceId.All(b => b == 0) ? "" : BE.Hex(o.DeviceId, 0, 20);
                _console.Text = o.ConsoleId.All(b => b == 0) ? "" : BE.Hex(o.ConsoleId, 0, 5);
                _templateNote.Text = "Details copied from " + Path.GetFileName(f) + " (including its media ID, version and pictures).";
                ShowPicture();
                Log.Info("Package creator: details copied from " + Path.GetFileName(f) + ".");
            }
            catch (Exception ex) { Dlg.Warn("Copy details", ex.Message); }
        }

        private void TakeIds()
        {
            string f = PickFile("Pick a save or profile that belongs to the target profile");
            if (f == null) return;
            try
            {
                var (profile, device, note) = ConSignature.ReadIds(f);
                if (profile == null) { Dlg.Warn("Profile ID", "That file has no profile ID."); return; }
                _profile.Text = BitConverter.ToString(profile).Replace("-", "");
                if (device != null) _device.Text = BitConverter.ToString(device).Replace("-", "");
                Log.Info("Package creator: took the profile ID" + (device != null ? " and device ID" : "") + " from the " + note + " " + Path.GetFileName(f) + ".");
            }
            catch (Exception ex) { Dlg.Error("Profile ID", ex.Message); }
        }

        private void SetPicture()
        {
            string f = PickFile("Choose a picture (it is resized to 64x64)", "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*");
            if (f == null) return;
            try { _template.ThumbnailPng = PackagePage.PackagePicture(f); ShowPicture(); }
            catch (Exception ex) { Dlg.Warn("Picture", ex.Message); }
        }

        private void AddFiles(string[] given = null)
        {
            string[] picked = given ?? PickFiles("Choose the files to put in the package");
            if (picked == null) return;
            var problems = new List<string>();
            foreach (string f in picked)
            {
                if (!File.Exists(f)) continue;
                string name = Path.GetFileName(f);
                if (name.Length > 40 || name.Any(c => c < 0x20 || c > 0x7E)) { problems.Add(name + ": the name must be at most 40 plain characters"); continue; }
                if (_sources.ContainsKey(name)) { problems.Add(name + ": already in the list"); continue; }
                long size = new FileInfo(f).Length;
                if (size > 100L * 1024 * 1024) { problems.Add(name + ": larger than 100 MB"); continue; }
                _sources[name] = f;
                var li = new ListViewItem(name);
                li.SubItems.Add(FileSniffer.FormatSize(size));
                li.SubItems.Add(f);
                _files.Items.Add(li);
            }
            if (problems.Count > 0) Dlg.Warn("Add files", string.Join("\n", problems));
        }

        private static byte[] Hex(XTextBox box, int bytes, string what)
        {
            string hex = new string(box.Text.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
            if (hex.Length == 0) return null;
            if (hex.Length != bytes * 2 || !hex.All(Uri.IsHexDigit)) throw new FormatException("The " + what + " must be exactly " + bytes * 2 + " hex digits (or empty).");
            return BE.ParseHexBytes(hex);
        }

        /// <summary>The options as typed, on top of whatever was copied from a template package.</summary>
        private StfsCreateOptions Read()
        {
            if (_type.Text.Trim().Length != 8 || !uint.TryParse(_type.Text.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint type)) throw new FormatException("The content type is 8 hex digits (00000001 = saved game).");
            if (_titleId.Text.Trim().Length != 8 || !uint.TryParse(_titleId.Text.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint title)) throw new FormatException("The title ID is 8 hex digits.");
            if (_display.Text.Trim().Length == 0) throw new FormatException("Enter a display name.");
            var t = _template;
            return new StfsCreateOptions
            {
                ContentType = type, TitleId = title,
                DisplayName = _display.Text.Trim(), TitleName = _game.Text.Trim(), Description = _description.Text.Trim(), Publisher = _publisher.Text.Trim(),
                ProfileId = Hex(_profile, 8, "profile ID"), DeviceId = Hex(_device, 20, "device ID"), ConsoleId = Hex(_console, 5, "console ID"),
                MediaId = t.MediaId, Version = t.Version, BaseVersion = t.BaseVersion, SaveGameId = t.SaveGameId, Platform = t.Platform, ExecutableType = t.ExecutableType,
                DiscNumber = t.DiscNumber, DiscCount = t.DiscCount, TransferFlags = t.TransferFlags, CreatorXuid = t.CreatorXuid,
                ThumbnailPng = t.ThumbnailPng, TitleThumbnailPng = t.TitleThumbnailPng
            };
        }

        private void Create()
        {
            StfsCreateOptions options;
            try { options = Read(); }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException) { Dlg.Warn("Create package", ex.Message); return; }
            if (_sources.Count == 0 && !Dlg.Confirm("Create package", "No files are listed - create an empty package?", "Create")) return;
            string suggested = PathSafety.SafeName(options.DisplayName.Length > 0 ? options.DisplayName : "package");
            string output = PickSave("Save the new package as", suggested);
            if (output != null) CreateTo(output, options);
        }

        /// <summary>Creates the package at <paramref name="output"/> from the listed files, signs it when a keyvault is loaded and opens it.</summary>
        public void CreateTo(string output, StfsCreateOptions options = null)
        {
            if (options == null)
            {
                try { options = Read(); }
                catch (Exception ex) when (ex is FormatException || ex is ArgumentException) { Dlg.Warn("Create package", ex.Message); return; }
            }
            if (File.Exists(output)) { Dlg.Warn("Create package", "That file already exists. Choose a new name - the creator never overwrites a file."); return; }
            var sources = _sources.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
            var kv = KeyVaultSession.Auto();
            Run(_create, "Creating the package", async () =>
            {
                string signed = await Task.Run(() =>
                {
                    var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                    foreach (var s in sources) files[s.Key] = File.ReadAllBytes(s.Value);
                    StfsCreator.Create(output, options, files);
                    if (kv == null) return null;
                    var r = ConSignature.Apply(output, null, kv);
                    return r.Describe();
                });
                Log.Success("Created " + Path.GetFileName(output) + " with " + sources.Count + " file(s) - every hash verified. " +
                            (signed ?? "No keyvault is in use, so it is unsigned: it loads on RGH / JTAG consoles."));
                WorkingFile.Set(output);
            });
        }
    }
}
