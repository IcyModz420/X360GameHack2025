using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>
    /// Read-only viewer for a GPD (XDBF) database - a game's achievements file, a profile's dashboard file or an
    /// avatar award file: achievements, awards, settings, games, pictures and strings, exactly as stored.
    /// </summary>
    public sealed class GpdPage : PageBase
    {
        public override string Title => "GPD Viewer";
        public override string Subtitle => "Look inside a .gpd database: achievements, avatar awards, settings, games, pictures and strings.";
        public override string PageGlyph => Glyph.List;

        private readonly TextBlock _headline = new TextBlock("", Theme.CardTitle, Theme.Text) { Height = Theme.S(34), Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis };
        private readonly TextBlock _info = new TextBlock("Open or drop a .gpd file, or use \"View GPD\" on a file inside a profile.", Theme.Small, Theme.TextDim);
        private readonly ListView _ach = ListStyle.Create(("ID", 50), ("Achievement", 230), ("G", 50), ("State", 110), ("When", 130), ("Description", 420));
        private readonly ListView _awards = ListStyle.Create(("Award", 260), ("Fits", 70), ("Earned", 70), ("When", 130), ("Entry", 150), ("Description", 320));
        private readonly ListView _settings = ListStyle.Create(("Setting", 100), ("Meaning", 210), ("Type", 80), ("Value", 520));
        private readonly ListView _titles = ListStyle.Create(("Title ID", 90), ("Game", 240), ("Achievements", 110), ("Gamerscore", 110), ("Avatar awards", 110), ("Last played", 140), ("Flags", 70));
        private readonly ListView _images = ListStyle.Create(("Picture", 130), ("Size", 100), ("Dimensions", 110), ("Used by", 320));
        private readonly ListView _strings = ListStyle.Create(("ID", 90), ("Text", 700));
        private readonly ListView _sync = ListStyle.Create(("Group", 130), ("Records", 80), ("Waiting to upload", 130), ("Next sync id", 100), ("Last synced", 160));
        private readonly ImageList _thumbs = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
        private XdbfFile _gpd;
        private string _name = "";
        private string _loadedPath;

        private static readonly Dictionary<ulong, string> Known = new Dictionary<ulong, string>
        {
            { 0x10040004, "Gamer zone" }, { 0x10040005, "Region" }, { 0x10040006, "Gamerscore" }, { 0x10040012, "Games played" }, { 0x10040013, "Achievements earned" },
            { 0x10040038, "This game: gamerscore earned" }, { 0x10040039, "This game: achievements earned" }, { 0x10040057, "Gamerscore from other platforms" },
            { 0x5004000B, "Reputation (0-100)" }, { 0x402C0011, "Motto" }, { 0x40520041, "Name" }, { 0x40520040, "Location" }, { 0x43E80043, "Bio" },
            { 0x4064000F, "Gamer picture key" }, { 0x63E80044, "Avatar appearance" }, { 0x63E83FFF, "Game data 1" }, { 0x63E83FFE, "Game data 2" }, { 0x63E83FFD, "Game data 3" }
        };

        // Not a scrolling page: the list of the selected tab takes all the height there is, so nothing has to be scrolled but the list itself.
        public GpdPage() : base(false)
        {
            Body.Controls.Add(_headline);
            Body.Controls.Add(_info);
            int size = Theme.S(48);
            _thumbs.ImageSize = new Size(size, size);
            _images.SmallImageList = _thumbs;

            void Tab(string title, string glyph, ListView list, string note, int height = 420)
            {
                var section = AddSection(title, glyph);
                var card = AddCard(title, note, glyph, section);
                card.AutoHeight = false;
                section.FillChild = card;
                card.Add(list);
                card.FillChild = list;
                if (list == _images) Buttons(card, Btn("Export pictures...", Glyph.Download, ExportImages, BtnKind.Primary));
            }
            Tab("Achievements", Glyph.Shield, _ach, "Every achievement record with its state.");
            Tab("Avatar awards", Glyph.Sparkle, _awards, "Award records (the databases inside a profile's PEC file).");
            Tab("Settings", Glyph.Settings, _settings, "Profile and game settings. Values are shown by their stored type.");
            Tab("Games", Glyph.Game, _titles, "Game list records (a profile's dashboard database, FFFE07D1.gpd).");
            Tab("Pictures", Glyph.Image, _images, "Stored PNG pictures: the game icon (8000) and achievement tiles.");
            Tab("Strings", Glyph.Document, _strings, "Stored strings (8000 = the game's name).");
            Tab("Sync", Glyph.Refresh, _sync, "What each group of records still has to upload to Xbox LIVE.", 220);
        }

        public override void OnActivated()
        {
            if (WorkingFile.Has(WorkingFileKind.Gpd) && _loadedPath != WorkingFile.Path) LoadWorkingFile();
        }

        public override void OnWorkingFileChanged()
        {
            if (WorkingFile.Kind == WorkingFileKind.Gpd) LoadWorkingFile();
        }

        private void LoadWorkingFile()
        {
            string path = WorkingFile.Path;
            _loadedPath = path;
            try
            {
                if (new FileInfo(path).Length > 64L * 1024 * 1024) throw new InvalidDataException("The file is too large to be a GPD.");
                Show(Path.GetFileName(path), File.ReadAllBytes(path));
            }
            catch (Exception ex) { Clear(Path.GetFileName(path), "Could not read this file: " + ex.Message); }
        }

        /// <summary>Shows a GPD that lives inside a package (nothing is written anywhere).</summary>
        public void Show(string name, byte[] data)
        {
            XdbfFile gpd;
            try { gpd = XdbfFile.Parse(data); }
            catch (Exception ex) { Clear(name, "Not a readable GPD: " + ex.Message); return; }
            _gpd = gpd;
            _name = name;
            _headline.Text = name;
            var game = GameTemplate.NameIn(gpd);
            _info.ForeColor = gpd.EditBlocker == null ? Theme.TextDim : Theme.Warning;
            _info.Text = (game.Length > 0 ? game + "  -  " : "") + FileSniffer.FormatSize(data.Length) + ", " + gpd.Entries.Count + " records" +
                         (gpd.EditBlocker == null ? "." : ". Its layout is unusual (" + gpd.EditBlocker + "), so the app shows it but will not edit it.");
            Fill();
            Body.PerformLayout();
        }

        private void Clear(string name, string message)
        {
            _gpd = null;
            _headline.Text = name;
            _info.ForeColor = Theme.Warning;
            _info.Text = message;
            foreach (var l in new[] { _ach, _awards, _settings, _titles, _images, _strings, _sync }) l.Items.Clear();
        }

        private static string When(DateTime? t) => t?.ToString("yyyy-MM-dd HH:mm") ?? "";

        private static ListViewItem Row(params string[] cells)
        {
            var li = new ListViewItem(cells[0]);
            for (int i = 1; i < cells.Length; i++) li.SubItems.Add(cells[i] ?? "");
            return li;
        }

        private void Fill()
        {
            var g = _gpd;
            foreach (var l in new[] { _ach, _awards, _settings, _titles, _images, _strings, _sync }) { l.BeginUpdate(); l.Items.Clear(); }
            _thumbs.Images.Clear();

            foreach (var a in g.Achievements().OrderBy(a => a.Id))
            {
                var li = Row(a.Id.ToString(), a.Name, a.Gamerscore.ToString(), a.Unlocked ? (a.UnlockedOnline ? "Earned on LIVE" : "Earned offline") : "Locked", When(a.UnlockTime),
                             a.Unlocked || a.LockedDescription.Length == 0 ? a.UnlockedDescription : a.LockedDescription);
                if (!a.Unlocked) li.ForeColor = Theme.TextDim;
                _ach.Items.Add(li);
            }
            foreach (var a in g.AvatarAwards().OrderBy(a => a.EntryId))
            {
                var li = Row(a.Name, a.Gender, a.Unlocked ? "Yes" : "No", When(a.UnlockTime), a.EntryId.ToString("X16"), a.Unlocked || a.LockedDescription.Length == 0 ? a.UnlockedDescription : a.LockedDescription);
                if (!a.Unlocked) li.ForeColor = Theme.TextDim;
                _awards.Items.Add(li);
            }
            foreach (var e in g.Records(XdbfFile.Ns.Setting))
            {
                Known.TryGetValue(e.Id, out string meaning);
                _settings.Items.Add(Row(e.Id.ToString("X8"), meaning ?? "", SettingType(e.Data), SettingValue(g, e)));
            }
            foreach (var t in g.Titles())
                _titles.Items.Add(Row(t.TitleIdHex, t.Name, t.AchievementsUnlocked + " / " + t.AchievementCount, t.GamerscoreUnlocked + " / " + t.GamerscoreTotal,
                                      t.AwardCount == 0 ? "" : t.AwardsEarned + " / " + t.AwardCount, When(t.LastPlayed), "0x" + t.Flags.ToString("X")));
            var tiles = g.Achievements().GroupBy(a => (ulong)a.ImageId).ToDictionary(x => x.Key, x => string.Join(", ", x.Select(a => a.Name).Take(3)));
            foreach (var e in g.Records(XdbfFile.Ns.Image))
            {
                string dims = "";
                int index = -1;
                try
                {
                    using (var ms = new MemoryStream(e.Data))
                    using (var img = Image.FromStream(ms, false, true))
                    {
                        dims = img.Width + " x " + img.Height;
                        _thumbs.Images.Add(new Bitmap(img, _thumbs.ImageSize));
                        index = _thumbs.Images.Count - 1;
                    }
                }
                catch { dims = "not a picture"; }
                tiles.TryGetValue(e.Id, out string used);
                var li = Row(e.Id.ToString("X"), FileSniffer.FormatSize(e.Data.Length), dims, e.Id == 0x8000 ? "The game's icon" : used ?? "");
                li.ImageIndex = index;
                li.Tag = e;
                _images.Items.Add(li);
            }
            foreach (var e in g.Records(XdbfFile.Ns.String))
                _strings.Items.Add(Row(e.Id.ToString("X"), XdbfFile.ReadWStr(e.Data, 0, out _)));
            foreach (XdbfFile.Ns ns in Enum.GetValues(typeof(XdbfFile.Ns)))
            {
                var data = g.Find(ns, XdbfFile.SyncDataIdOf(ns));
                int records = g.Records(ns).Count();
                if (data == null && records == 0) continue;
                var list = g.SyncList(ns);
                _sync.Items.Add(Row(ns.ToString(), records.ToString(), list.Count(s => s.SyncId != 0).ToString(),
                                    data != null && data.Data.Length >= 0x18 ? BE.U64(data.Data, 0).ToString() : "", data != null && data.Data.Length >= 0x18 ? When(XdbfFile.FileTime(BE.U64(data.Data, 0x10))) : ""));
            }
            foreach (var l in new[] { _ach, _awards, _settings, _titles, _images, _strings, _sync }) l.EndUpdate();
        }

        private static string SettingType(byte[] d)
        {
            if (d.Length < 9) return "";
            switch (d[8])
            {
                case GpdSetting.TypeInt32: return "Number";
                case GpdSetting.TypeInt64: return "Big number";
                case GpdSetting.TypeDouble: return "Decimal";
                case GpdSetting.TypeString: return "Text";
                case GpdSetting.TypeFloat: return "Decimal";
                case GpdSetting.TypeBinary: return "Data";
                case GpdSetting.TypeDateTime: return "Date";
                default: return "Type " + d[8];
            }
        }

        private static string SettingValue(XdbfFile g, XdbfFile.Entry e)
        {
            var d = e.Data;
            if (d.Length < 0x18) return BE.Hex(d, 0, d.Length);
            switch (d[8])
            {
                case GpdSetting.TypeInt32: return ((int)BE.U32(d, 0x10)).ToString(CultureInfo.InvariantCulture);
                case GpdSetting.TypeInt64: return ((long)BE.U64(d, 0x10)).ToString(CultureInfo.InvariantCulture);
                case GpdSetting.TypeDouble: return BitConverter.Int64BitsToDouble((long)BE.U64(d, 0x10)).ToString("R", CultureInfo.InvariantCulture);
                case GpdSetting.TypeString: return g.GetStringSetting((uint)e.Id) ?? "";
                case GpdSetting.TypeFloat: return (g.GetFloatSetting((uint)e.Id) ?? 0).ToString("R", CultureInfo.InvariantCulture);
                case GpdSetting.TypeDateTime: return When(XdbfFile.FileTime(BE.U64(d, 0x10)));
                default:
                    int length = (int)Math.Min(BE.U32(d, 0x10), (uint)(d.Length - 0x18));
                    return length + " bytes: " + BE.Hex(d, 0x18, Math.Min(length, 32)) + (length > 32 ? "..." : "");
            }
        }

        private void ExportImages()
        {
            if (_gpd == null || _images.Items.Count == 0) { Dlg.Info("Export pictures", "This GPD holds no pictures."); return; }
            string folder = PickFolder("Export the pictures to");
            if (folder == null) return;
            int written = 0;
            try
            {
                // _name is "package / file.gpd" when the GPD came from a package: the stem is the GPD's own name
                string stem = PathSafety.SafeName(Path.GetFileNameWithoutExtension(_name).Trim());
                foreach (ListViewItem li in _images.Items)
                {
                    var e = (XdbfFile.Entry)li.Tag;
                    if (li.ImageIndex < 0) continue;                     // only data that really decoded as a picture
                    File.WriteAllBytes(Path.Combine(folder, stem + "_" + e.Id.ToString("X") + ".png"), e.Data);
                    written++;
                }
                Log.Success("Exported " + written + " picture(s) from " + _name + " to " + folder);
            }
            catch (Exception ex) { Dlg.Error("Export pictures", ex.Message); }
        }
    }
}
