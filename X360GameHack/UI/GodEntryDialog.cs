using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;
using X360GameHack.UI.Pages;

namespace X360GameHack.UI
{
    /// <summary>
    /// The details one Games on Demand package gets, as read from its disc image - every one can be changed, as in ISO2God's
    /// "Add ISO" window: the name the dashboard shows, title and media ID, disc x of y, platform and executable type, the
    /// picture, and how much of the image goes in.
    /// </summary>
    public sealed class GodEntryDialog : DialogForm
    {
        private readonly GodTitleInfo _read;
        private readonly string _image;
        private readonly XTextBox _name = new XTextBox();
        private readonly XTextBox _title = new XTextBox("8 hex digits", false, true) { Width = Theme.S(120) };
        private readonly XTextBox _media = new XTextBox("8 hex digits", false, true) { Width = Theme.S(120) };
        private readonly XTextBox _disc = new XTextBox(null, false, true) { Width = Theme.S(56) };
        private readonly XTextBox _count = new XTextBox(null, false, true) { Width = Theme.S(56) };
        private readonly XTextBox _platform = new XTextBox(null, false, true) { Width = Theme.S(56) };
        private readonly XTextBox _type = new XTextBox(null, false, true) { Width = Theme.S(56) };
        private readonly PictureBox _pic = new PictureBox { Width = Theme.S(64), Height = Theme.S(64), SizeMode = PictureBoxSizeMode.Zoom };
        private readonly TextBlock _picNote = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(20) };
        private readonly Segmented _padding = new Segmented("None", "Partial (cropped)", "Full (rebuilt)");
        private readonly XCheck _keep = new XCheck("Also save the rebuilt image", false, "Full padding removal builds a new, smaller disc image on the way; tick to keep it as an .iso as well.");
        private byte[] _thumb;

        public GodTitleInfo Info { get; private set; }
        public string DisplayName { get; private set; }
        public byte[] Thumbnail => _thumb;
        public GodPadding PaddingMode { get; private set; }
        public bool KeepRebuilt { get; private set; }

        public GodEntryDialog(string image, GodTitleInfo info, string name, byte[] thumbnail, GodPadding padding, bool keepRebuilt, bool addDiscNumber) : base("Games on Demand details")
        {
            _read = info;
            _image = image;
            _thumb = thumbnail;
            _pic.BackColor = Theme.Input;

            var body = new VStack { Padding = new Padding(Theme.S(22), Theme.S(18), Theme.S(22), Theme.S(18)), Spacing = Theme.S(8) };
            body.Add(new TextBlock("Games on Demand details", Theme.CardTitle, Theme.Text) { Height = Theme.S(30) });
            body.Add(new TextBlock(image, Theme.Small, Theme.TextDim) { Height = Theme.S(20), Flags = TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix });
            body.Add(new TextBlock("Read from the game's own " + (info.Xbox360 ? "default.xex" : "default.xbe") + ". Change only what you need: the dashboard shows the name and picture; " +
                                   "a title update and saves follow the title ID and media ID, so keep those as the disc has them unless you know why not.", Theme.Small, Theme.TextDim) { Flags = TextFormatFlags.WordBreak });

            var picRow = new HStack { Height = Theme.S(70), Spacing = Theme.S(10) };
            picRow.Add(_pic);
            var picButtons = new VStack { Width = Theme.S(220), Spacing = Theme.S(4) };
            var change = new XButton("Change picture...", Glyph.Image, BtnKind.Secondary, (s, e) =>
            {
                string f = PageBase.PickFile("Choose a picture (it is made 64x64)", "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*");
                if (f == null) return;
                try { _thumb = StfsPageBase.PackagePicture(f); ShowPicture(); }
                catch (Exception ex) { Dlg.Warn("Picture", ex.Message); }
            });
            picButtons.Add(change);
            var row2 = new HStack { Height = Theme.S(30), Spacing = Theme.S(6) };
            row2.Add(new XButton("Game's own", null, BtnKind.Secondary, (s, e) => { _thumb = _read.Thumbnail; ShowPicture(); }));
            row2.Add(new XButton("None", null, BtnKind.Secondary, (s, e) => { _thumb = null; ShowPicture(); }));
            picButtons.Add(row2);
            picRow.Add(picButtons);
            picRow.Add(_picNote);
            picRow.FillChild = _picNote;
            body.Add(picRow);

            _name.Text = name ?? "";
            PageBase.Field(body, "Name", _name);
            _title.Text = info.TitleId.ToString("X8");
            _media.Text = info.MediaId.ToString("X8");
            PageBase.Field(body, "Title ID", _title, new TextBlock("Media ID", Theme.Body, Theme.TextDim) { Width = Theme.S(80), Height = Theme.S(30), Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Right }, _media).FillChild = null;
            _disc.Text = info.DiscNumber.ToString(CultureInfo.InvariantCulture);
            _count.Text = info.DiscCount.ToString(CultureInfo.InvariantCulture);
            PageBase.Field(body, "Disc", _disc, new TextBlock("of", Theme.Body, Theme.TextDim) { Width = Theme.S(28), Height = Theme.S(30), Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter }, _count).FillChild = null;
            _platform.Text = info.Platform.ToString(CultureInfo.InvariantCulture);
            _type.Text = info.ExecutableType.ToString(CultureInfo.InvariantCulture);
            PageBase.Field(body, "Platform", _platform, new TextBlock("Executable type", Theme.Body, Theme.TextDim) { Width = Theme.S(130), Height = Theme.S(30), Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Right }, _type).FillChild = null;
            if (!string.IsNullOrWhiteSpace(info.Notes))
                body.Add(new TextBlock(info.Notes, Theme.Small, Theme.Warning) { Flags = TextFormatFlags.WordBreak });
            if (info.DiscCount > 1 && !addDiscNumber)
                body.Add(new TextBlock("This game comes on " + info.DiscCount + " discs - give each disc its own name (for example \"" + info.DashboardName(image, true) + "\") so the dashboard tells them apart.", Theme.Small, Theme.TextDim) { Flags = TextFormatFlags.WordBreak });

            _padding.SelectedIndex = (int)padding;
            PageBase.Field(body, "Padding removal", _padding).FillChild = null;
            _keep.Checked = keepRebuilt;
            body.Add(_keep);
            _keep.Enabled = _padding.SelectedIndex == 2;
            _padding.SelectedIndexChanged += (s, e) => _keep.Enabled = _padding.SelectedIndex == 2;

            var buttons = new HStack { Height = Theme.S(34), Spacing = Theme.S(8) };
            var gap = new Panel();
            var save = new XButton("Save", Glyph.Check, BtnKind.Primary, (s, e) => Done());
            var cancel = new XButton("Cancel", null, BtnKind.Secondary) { DialogResult = DialogResult.Cancel };
            buttons.Add(gap); buttons.Add(save); buttons.Add(cancel);
            buttons.FillChild = gap;
            body.Add(buttons);
            Body(body, Theme.S(640));
            AcceptButton = save;
            CancelButton = cancel;
            ShowPicture();
        }

        private void ShowPicture()
        {
            var old = _pic.Image;
            _pic.Image = null;
            old?.Dispose();
            if (_thumb == null || _thumb.Length == 0) { _picNote.Text = "No picture - the dashboard shows a blank tile."; return; }
            try
            {
                using (var ms = new MemoryStream(_thumb))
                using (var img = Image.FromStream(ms))
                {
                    _pic.Image = new Bitmap(img);
                    _picNote.Text = img.Width + "x" + img.Height + ", " + _thumb.Length.ToString("N0") + " bytes" + (_thumb == _read.Thumbnail ? " (the game's own)" : "");
                }
            }
            catch { _picNote.Text = "The picture could not be shown."; }
        }

        private static bool Hex8(string s, out uint v)
        {
            v = 0;
            s = (s ?? "").Trim();
            return s.Length == 8 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        }

        private static bool Small(string s, int min, out byte v)
        {
            v = 0;
            if (!int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < min || n > 255) return false;
            v = (byte)n;
            return true;
        }

        private void Done()
        {
            string name = _name.Text.Trim();
            if (name.Length == 0) { Dlg.Warn("Name", "Enter the name the dashboard shows - without one the game appears as an unknown game."); return; }
            if (name.Length > 127) { Dlg.Warn("Name", "The name can be at most 127 characters (the dashboard's tile shows the first 63)."); return; }
            if (!Hex8(_title.Text, out uint title) || title == 0) { Dlg.Warn("Title ID", "The title ID is 8 hex digits (0-9, A-F), for example 4D5307E6."); return; }
            if (!Hex8(_media.Text, out uint media)) { Dlg.Warn("Media ID", "The media ID is 8 hex digits (0-9, A-F)."); return; }
            if (!Small(_disc.Text, 1, out byte disc) || !Small(_count.Text, 1, out byte count) || disc > count) { Dlg.Warn("Disc", "Disc is a number from 1 to the number of discs (at most 255)."); return; }
            if (!Small(_platform.Text, 0, out byte platform) || !Small(_type.Text, 0, out byte type)) { Dlg.Warn("Platform / executable type", "Platform and executable type are numbers from 0 to 255 (most games: 0 and 0)."); return; }
            var info = _read.Clone();
            info.TitleId = title; info.MediaId = media; info.DiscNumber = disc; info.DiscCount = count; info.Platform = platform; info.ExecutableType = type;
            Info = info;
            DisplayName = name;
            PaddingMode = (GodPadding)_padding.SelectedIndex;
            KeepRebuilt = _keep.Checked && PaddingMode == GodPadding.Full;
            DialogResult = DialogResult.OK;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _pic.Image?.Dispose();
            base.Dispose(disposing);
        }
    }
}
