using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>
    /// View and edit an Xbox 360 gamer profile: gamertag, region and LIVE details from the encrypted Account file, and
    /// games, gamerscore and achievements from the GPD databases. Every edit is one click: it is written into the
    /// profile in a single verified step and then rehashed and re-signed.
    /// </summary>
    public sealed class ProfilePage : PageBase
    {
        private const WorkingFileKind Kind = WorkingFileKind.Profile;

        private readonly TextBlock _headline = new TextBlock("", Theme.CardTitle, Theme.Text)
        { Height = Theme.S(34), Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis };
        private readonly KvGrid _acct = new KvGrid();
        private readonly KvGrid _summary = new KvGrid { Columns = 1 };
        private readonly XTextBox _tagBox = new XTextBox("New gamertag (up to 15 characters)");
        private readonly XButton _tagBtn;
        private XButton _passBtn, _kindBtn, _liveBtn;
        private readonly XTextBox _when = new XTextBox("When they were unlocked, e.g. 2024-06-15 18:30 - leave empty for now");
        private readonly TextBlock _liveNote;
        private readonly XTextBox _motto = new XTextBox("Motto (up to 21 characters)");
        private readonly XTextBox _realName = new XTextBox("Name (up to 40 characters)");
        private readonly XTextBox _location = new XTextBox("Location (up to 40 characters)");
        private readonly XTextBox _bio = new XTextBox(null, true) { Height = Theme.S(90) };
        private readonly Segmented _zone = new Segmented(Xbox360Profile.ZoneNames);
        private readonly XTextBox _rep = new XTextBox("0 - 100") { Width = Theme.S(110) };
        private HStack _repRow;
        private XButton _cardBtn, _picBtn;
        private readonly PictureBox _pic64 = new PictureBox { Width = Theme.S(64), Height = Theme.S(64), SizeMode = PictureBoxSizeMode.Zoom };
        private readonly PictureBox _pic32 = new PictureBox { Width = Theme.S(32), Height = Theme.S(64), SizeMode = PictureBoxSizeMode.CenterImage };

        private readonly ListView _games = ListStyle.Create(("Game", 230), ("Gamerscore", 110), ("Achievements", 120), ("Last played", 110), ("Title ID", 84));
        private readonly ListView _ach = ListStyle.Create(("Achievement", 240), ("Score", 60), ("Unlocked", 110), ("When", 110));
        private readonly TextBlock _achState = new TextBlock("", Theme.Small, Theme.TextDim);

        private readonly ListView _awardGames = ListStyle.Create(("Game", 260), ("Awards earned", 120), ("Title ID", 84));
        private readonly ListView _awards = ListStyle.Create(("Avatar award", 260), ("Fits", 70), ("Earned", 90), ("When", 110));
        private readonly TextBlock _awardState = new TextBlock("", Theme.Small, Theme.TextDim);
        private readonly XTextBox _awardWhen = new XTextBox("When they were earned, e.g. 2024-06-15 18:30 - leave empty for now");

        private readonly TextBlock _empty;
        private readonly PackageContentsCard _contents;
        private readonly RetailSignCard _signing;
        private readonly VStack _signTab;
        private readonly Control[] _needsFile;

        private Xbox360Profile _profile;
        private string _loadedPath;
        private bool _editing;

        public override string Title => "Profiles";
        public override string Subtitle => "View and edit Xbox 360 gamer profiles - gamertag, achievements and gamerscore - and re-sign them for any console.";
        public override string PageGlyph => Glyph.Profile;

        public ProfilePage()
        {
            _empty = Note(Body, "Open or drop an Xbox 360 profile (a CON package whose content type is \"Profile\", usually a file named like E0000XXXXXXXXXXX with no extension).", Theme.Warning);

            // ---------------------------------------------------------------- Overview
            var overview = AddSection("Overview", Glyph.Info);
            overview.Controls.Add(_headline);
            _liveNote = Note(overview,
                "This profile is linked to Xbox LIVE. Achievements unlocked here are stored like any offline unlock, so they upload the next time the " +
                "profile signs in - and Microsoft resets gamerscore or bans accounts for edited achievements. Keep it offline, or edit an offline profile.",
                Theme.Warning);
            _liveNote.Visible = false;
            var cols = AddColumns(2, 420, overview);

            var acctCard = AddCard("Account", "Decrypted from the profile's Account file. Double-click a value to copy it.", Glyph.Profile, cols);
            acctCard.Add(_acct);
            _passBtn = Btn("Remove passcode", Glyph.Unlock, () => Edit(_passBtn, "Removing the passcode", p =>
            {
                var a = Account(p);
                if (!a.PasscodeEnabled) return "This profile has no passcode - nothing to change.";
                a.RemovePasscode();
                return "Passcode removed.";
            }), tip: "Turns off this profile's passcode lock - the four-button pass code that guards signing in to the profile and its parental / content controls. Clears the flag and the four button presses in the Account file.");
            _kindBtn = Btn("Convert to retail", Glyph.Refresh, () => Edit(_kindBtn, "Converting the account", p =>
            {
                var a = Account(p);
                a.Kind = a.Kind == XboxConsoleKind.Retail ? XboxConsoleKind.Devkit : XboxConsoleKind.Retail;
                return "Account re-encrypted for " + (a.Kind == XboxConsoleKind.Retail ? "retail consoles (including RGH / JTAG)." : "development kits.");
            }), tip: "Re-encrypts the Account file with the other console type's key. Nothing inside the account changes.");
            _liveBtn = Btn("Turn Xbox LIVE off", Glyph.Globe, () => Edit(_liveBtn, "Changing the Xbox LIVE flag", p =>
            {
                var a = Account(p);
                a.LiveEnabled = !a.LiveEnabled;
                return a.LiveEnabled ? "Xbox LIVE flag turned on." : "Xbox LIVE flag turned off - the console treats this as an offline profile (nothing uploads).";
            }), tip: "The account's \"set up for Xbox LIVE\" flag. Off = the console treats it as an offline profile.");
            Buttons(acctCard, _passBtn, _kindBtn, _liveBtn);

            var sumCard = AddCard("Games & gamerscore", "What the gamercard shows, and what the games stored in this profile add up to.", Glyph.Save, cols);
            sumCard.Add(_summary);
            var quick = new WrapPanel();
            quick.Add(new XButton("Rehash & resign", Glyph.Shield, BtnKind.Primary, (s, e) => { if (Has()) _signing.RehashAndResign(); }));
            quick.Add(new XButton("Install to USB / console...", Glyph.Usb, BtnKind.Secondary, (s, e) => InstallToConsole()));
            quick.Add(new XButton("Open folder", Glyph.FolderOpen, BtnKind.Secondary, (s, e) => { if (Has()) Tools.OpenFolder(WorkingFile.Path); }));
            sumCard.Add(quick);

            var tagCard = AddCard("Edit gamertag", "The gamertag is stored in the profile's encrypted Account file. One click changes it, then rehashes and re-signs the profile.", Glyph.Edit, overview);
            tagCard.Add(_tagBox);
            _tagBtn = Btn("Change gamertag", Glyph.Edit, ChangeGamertag, BtnKind.Primary);
            Buttons(tagCard, _tagBtn);

            var cardTab = AddSection("Gamercard", Glyph.Profile);
            var picCard = AddCard("Gamer picture", "The picture next to the gamertag. One click replaces it with any image (cropped square, resized to 64x64 and 32x32), then rehashes and re-signs the profile.", Glyph.Image, cardTab);
            var picRow = new HStack { Height = Theme.S(64), Spacing = Theme.S(12) };
            picRow.Add(_pic64);
            picRow.Add(_pic32);
            _picBtn = Btn("Change picture...", Glyph.Image, ChangePicture, BtnKind.Primary, tip: "Any PNG, JPG, BMP or GIF.");
            picRow.Add(_picBtn);
            picRow.Add(Btn("Save picture as...", Glyph.Save, ExportPicture, tip: "Saves the profile's current 64x64 picture as a PNG file."));
            picCard.Add(picRow);

            var card = AddCard("Gamercard", "Motto, name, location, bio and gamer zone as other players see them. One click saves them, then rehashes and re-signs the profile.", Glyph.Tag, cardTab);
            Field(card, "Motto", _motto);
            Field(card, "Name", _realName);
            Field(card, "Location", _location);
            Field(card, "Bio", _bio);
            Field(card, "Gamer zone", _zone).FillChild = null;
            _repRow = Field(card, "Reputation", _rep, new TextBlock("of 100 (five stars = 100)", Theme.Small, Theme.TextDim) { Width = Theme.S(220), Height = Theme.S(30), Flags = TextFormatFlags.VerticalCenter });
            _repRow.FillChild = null;
            _cardBtn = Btn("Save gamercard", Glyph.Check, SaveGamercard, BtnKind.Primary);
            Buttons(card, _cardBtn);

            // ---------------------------------------------------------------- Games
            var gamesTab = AddSection("Games", Glyph.Save);
            var gamesTop = AddColumns(2, 420, gamesTab);

            var all = AddCard("Every game", "One click for every achievement of every game this profile holds achievement data for.", Glyph.Shield, gamesTop);
            XButton unlockAll = null, lockAll = null;
            unlockAll = Btn("Unlock all achievements", Glyph.Shield, () =>
            {
                if (!UnlockTime(_when, out DateTime? when)) return;
                Edit(unlockAll, "Unlocking every achievement", p => Describe("Unlocked", p.UnlockAll(when)));
            }, BtnKind.Primary);
            lockAll = Btn("Lock all achievements", Glyph.List, () => Edit(lockAll, "Locking every achievement", p => Describe("Locked", p.LockAll())));
            Buttons(all, unlockAll, lockAll);
            Field(all, "Unlock date", _when);
            var natural = new XCheck("Natural unlock dates (recommended)", AppSettings.Current.NaturalUnlockDates,
                "On: every achievement and avatar award gets its own date and time, spread backwards from the unlock date like real play sessions, and is stored as earned offline. Off: all of them get exactly the unlock date.");
            Xbox360Profile.SpreadUnlockTimes = natural.Checked;
            natural.CheckedChanged += (s, e) => { Xbox360Profile.SpreadUnlockTimes = natural.Checked; AppSettings.Current.NaturalUnlockDates = natural.Checked; AppSettings.Save(); };
            all.Add(natural);
            Note(all, "Achievements are always stored as earned offline, the way a console stores them without a connection. With natural dates on they are also spread over time instead of all sharing one second, which is the most obvious sign of an edited profile. " +
                      "This lowers the risk but nothing can promise an account will not be reset or banned if an edited profile signs in to Xbox LIVE - the only certain way is to keep it offline.", Theme.TextDim);

            var adder = AddCard("Add games", "Puts a game into this profile the way a console does when it is first played: listed, with all its achievements locked. " +
                "The game comes from another profile that has it, its .gpd database file, its own XEX, or just its Title ID.", Glyph.Add, gamesTop);
            XButton addFromProfile = null, addFromGpd = null;
            addFromProfile = Btn("Add games from another profile...", Glyph.Profile, () => AddGamesFromProfile(addFromProfile), BtnKind.Primary);
            addFromGpd = Btn("Add games from .gpd files...", Glyph.Document, () => AddGamesFromGpd(addFromGpd));
            XButton addFromXex = null;
            addFromXex = Btn("Add a game from its XEX...", Glyph.Code, () => AddGameFromXex(addFromXex), tip: "Reads the achievement list the game carries in its own default.xex - no other profile needed.");
            XButton addById = null;
            addById = Btn("Add a game by Title ID...", Glyph.Search, () => AddGameById(addById, null),
                          tip: "Looks the game's name and achievement list up in the public title database (dbox.tools) - only the Title ID is sent. The achievement pictures are not included; add the game from its XEX for those.");
            Buttons(adder, addFromProfile, addFromGpd, addFromXex, addById);

            var lookup = AddCard("Title ID lookup", "A game's name from its Title ID, or the Title IDs of games whose name you type. Asks the public title database (dbox.tools) - only what you type is sent.", Glyph.Search, gamesTab);
            var lookupRow = new HStack { Height = Theme.S(36) };
            lookupRow.Add(_lookupBox); lookupRow.FillChild = _lookupBox;
            XButton lookupBtn = null;
            lookupBtn = Btn("Look up", Glyph.Search, () => LookUp(lookupBtn), BtnKind.Primary);
            lookupRow.Add(lookupBtn);
            lookup.Add(lookupRow);
            _lookupBox.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LookUp(lookupBtn); } };
            lookup.Add(_lookupResults);
            _lookupResults.DoubleClick += (s, e) => { if (_lookupResults.SelectedItem is TitleMatch m) AddGameById(lookupBtn, m.TitleId); };
            Note(lookup, "Double-click a game in the list to add it to the open profile.", Theme.TextDim);

            var gamesCard = AddCard("Games played", "Select a game to see its achievements below.", Glyph.List, gamesTab);
            _games.Height = Theme.S(230);
            _games.MultiSelect = false;          // the buttons below act on one game
            _games.Columns[1].TextAlign = HorizontalAlignment.Right;
            _games.Columns[2].TextAlign = HorizontalAlignment.Right;
            _games.SelectedIndexChanged += (s, e) => ShowAchievements();
            gamesCard.Add(_games);
            XButton lastPlayed = null, neverPlayed = null;
            lastPlayed = Btn("Set last played", Glyph.Refresh, () => SetLastPlayed(lastPlayed, false), tip: "Sets the selected game's \"last played\" time to the date in the Unlock date box above (empty = now). It decides the order of the games on the console.");
            neverPlayed = Btn("Clear last played", Glyph.Clear, () => SetLastPlayed(neverPlayed, true), tip: "Removes the selected game's \"last played\" time.");
            Buttons(gamesCard, lastPlayed, neverPlayed);

            var achCard = AddCard("Achievements", "Earned and locked achievements of the selected game. Select rows to unlock or lock just those.", Glyph.Shield, gamesTab);
            achCard.Add(_achState);
            _ach.Height = Theme.S(230);
            _ach.Columns[1].TextAlign = HorizontalAlignment.Right;
            achCard.Add(_ach);
            XButton unlockGame = null, lockGame = null, unlockSome = null, lockSome = null;
            unlockGame = Btn("Unlock all for this game", Glyph.Shield, () => EditGame(unlockGame, true, false), BtnKind.Primary);
            lockGame = Btn("Lock all for this game", Glyph.List, () => EditGame(lockGame, false, false));
            unlockSome = Btn("Unlock selected", Glyph.Shield, () => EditGame(unlockSome, true, true));
            lockSome = Btn("Lock selected", Glyph.List, () => EditGame(lockSome, false, true));
            Buttons(achCard, unlockGame, lockGame, unlockSome, lockSome);
            Note(achCard, "Unlocked achievements show the standard tile until the profile syncs with Xbox LIVE or the game unlocks them itself - the picture comes from the game, not the profile.");

            // ---------------------------------------------------------------- Avatar awards
            var awardsTab = AddSection("Avatar awards", Glyph.Sparkle);
            var allAwards = AddCard("Every game", "One click for every avatar award of every game in this profile that has them.", Glyph.Sparkle, awardsTab);
            XButton unlockAwards = null, lockAwards = null;
            unlockAwards = Btn("Unlock all avatar awards", Glyph.Sparkle, () =>
            {
                if (!UnlockTime(_awardWhen, out DateTime? when)) return;
                Edit(unlockAwards, "Unlocking every avatar award", p => DescribeAwards("Unlocked", p.UnlockAllAwards(when)));
            }, BtnKind.Primary);
            lockAwards = Btn("Lock all avatar awards", Glyph.List, () => Edit(lockAwards, "Locking every avatar award", p => DescribeAwards("Locked", p.LockAllAwards())));
            Buttons(allAwards, unlockAwards, lockAwards);
            Field(allAwards, "Earned date", _awardWhen);
            allAwards.Add(_awardState);

            var awardGames = AddCard("Games with avatar awards", "Select a game to see its awards below.", Glyph.List, awardsTab);
            _awardGames.Height = Theme.S(180);
            _awardGames.MultiSelect = false;     // the buttons below act on one game
            _awardGames.Columns[1].TextAlign = HorizontalAlignment.Right;
            _awardGames.SelectedIndexChanged += (s, e) => ShowAwards();
            awardGames.Add(_awardGames);

            var awardCard = AddCard("Awards", "Select rows to unlock or lock just those. Male and female versions of the same item are separate awards.", Glyph.Sparkle, awardsTab);
            _awards.Height = Theme.S(230);
            awardCard.Add(_awards);
            XButton unlockGameAwards = null, lockGameAwards = null, unlockSomeAwards = null, lockSomeAwards = null;
            unlockGameAwards = Btn("Unlock all for this game", Glyph.Sparkle, () => EditAwards(unlockGameAwards, true, false), BtnKind.Primary);
            lockGameAwards = Btn("Lock all for this game", Glyph.List, () => EditAwards(lockGameAwards, false, false));
            unlockSomeAwards = Btn("Unlock selected", Glyph.Sparkle, () => EditAwards(unlockSomeAwards, true, true));
            lockSomeAwards = Btn("Lock selected", Glyph.List, () => EditAwards(lockSomeAwards, false, true));
            Buttons(awardCard, unlockGameAwards, lockGameAwards, unlockSomeAwards, lockSomeAwards);
            Note(awardCard, "This records the award as earned, exactly as the console does. The item itself (the clothing or prop) is a separate file the game or Xbox LIVE delivers - it is not part of the profile.");

            // ---------------------------------------------------------------- Contents
            var contentsTab = AddSection("Contents", Glyph.FolderOpen);
            _contents = new PackageContentsCard();
            contentsTab.Add(_contents);

            // ---------------------------------------------------------------- Rehash & resign
            _signTab = AddSection("Rehash & resign", Glyph.Shield);
            _signing = new RetailSignCard();
            _signTab.Controls.Add(_signing);
            _signTab.Controls.Add(new IdPickerCard(_signing));
            _signing.NeedsAttention += bad => Tabs?.SetBadge(_signTab, bad ? Theme.Danger : (System.Drawing.Color?)null);

            _needsFile = new Control[] { overview, gamesTab, awardsTab, contentsTab, _signTab };
            ShowState();
        }

        private static bool Has() => WorkingFile.Has(Kind);

        public override void OnActivated()
        {
            ShowState();
            if (Has() && _loadedPath != WorkingFile.Path) Reload();
        }

        public override void OnWorkingFileChanged()
        {
            ShowState();
            if (WorkingFile.Kind == Kind) Reload();
        }

        private void ShowState()
        {
            bool has = WorkingFile.Kind == Kind;
            _empty.Visible = !has;
            foreach (var c in _needsFile) c.Enabled = has;
        }

        private void InstallToConsole()
        {
            if (!Has()) { Dlg.Warn("No profile", "Open or drop a profile first."); return; }
            MainForm.Instance.Navigate(typeof(SetupPage));
            MainForm.Instance.GetPage<SetupPage>().AddPackages(new[] { WorkingFile.Path });
        }

        // ---------------------------------------------------------------- load

        private void Reload()
        {
            string path = WorkingFile.Path;
            _loadedPath = path;
            uint? selected = (_games.SelectedItems.Count > 0 ? _games.SelectedItems[0].Tag as GpdTitle : null)?.TitleId;
            uint? selectedAwardGame = _awardGames.SelectedItems.Count > 0 ? (uint?)(uint)_awardGames.SelectedItems[0].Tag : null;
            Run(null, "Reading profile", async () =>
            {
                Xbox360Profile prof = null;
                string err = null;
                try { prof = await Task.Run(() => Xbox360Profile.Open(path)); }
                catch (Exception ex) { err = ex.Message; }
                if (path != WorkingFile.Path) return;

                _profile = prof;
                _acct.Clear();
                _summary.Clear();
                _games.Items.Clear();
                _ach.Items.Clear();
                _awardGames.Items.Clear();
                _awards.Items.Clear();
                _awardState.Text = "";
                _achState.Text = "";
                _liveNote.Visible = prof != null && prof.IsLiveLinked;

                if (prof == null)
                {
                    _headline.Text = WorkingFile.Name;
                    _acct.Set("Error", err ?? "Could not read this profile.");
                    _contents.Load(path);
                    _signing.Load(path);
                    Body.PerformLayout();
                    return;
                }

                var a = prof.Account;
                _headline.Text = a != null && a.Gamertag.Length > 0 ? a.Gamertag : WorkingFile.Name;
                _tagBox.Text = a?.Gamertag ?? "";
                _passBtn.Enabled = a != null && a.PasscodeEnabled;
                _kindBtn.Enabled = a != null;
                _kindBtn.Text = a != null && a.Kind == XboxConsoleKind.Retail ? "Convert to devkit" : "Convert to retail";
                _liveBtn.Enabled = a != null && (a.LiveEnabled || a.HasOnlineKey);   // only an account that has LIVE keys can be turned back on
                _liveBtn.Text = a != null && a.LiveEnabled ? "Turn Xbox LIVE off" : "Turn Xbox LIVE on";

                if (a != null)
                {
                    _acct.Set("Gamertag", a.Gamertag);
                    _acct.Set("Xbox LIVE", a.LiveEnabled ? "Enabled" : "Not set up (offline profile)");
                    _acct.Set("XUID", a.Xuid == 0 ? "None (offline profile)" : a.XuidHex + (a.IsOnlineXuid ? "  (LIVE)" : ""));
                    _acct.Set("Account type", a.Kind == XboxConsoleKind.Retail ? "Retail" : "Devkit");
                    if (a.LiveEnabled || a.CachedUserFlags != 0)
                    {
                        _acct.Set("Region", a.Country);
                        _acct.Set("Language", a.Language);
                        _acct.Set("Subscription", a.SubscriptionTier);
                    }
                    _acct.Set("Online key", a.HasOnlineKey ? "Present" : "None");
                    _acct.Set("Passcode", a.PasscodeEnabled ? a.PasscodeText : "None");
                }
                else _acct.Set("Account", prof.AccountError ?? "Could not decrypt the Account file.");
                _acct.Set("Profile ID", prof.ProfileId);

                _motto.Text = prof.Motto ?? "";
                _realName.Text = prof.RealName ?? "";
                _location.Text = prof.Location ?? "";
                _bio.Text = (prof.Bio ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
                _zone.SelectedIndex = Math.Max(0, Math.Min(Xbox360Profile.ZoneNames.Length - 1, prof.Zone ?? 0));
                _repRow.Visible = prof.Reputation.HasValue;      // offline profiles have no reputation to edit
                _rep.Text = prof.Reputation.HasValue ? prof.Reputation.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "";

                ShowPicture(_pic64, prof.GamerPicture);
                ShowPicture(_pic32, prof.GamerPictureSmall);

                int? cardScore = prof.GamercardGamerscore, cardAchievements = prof.GamercardAchievements;
                _summary.Set("Gamerscore", (cardScore ?? prof.TotalGamerscore) + " G");
                _summary.Set("Achievements", (cardAchievements ?? prof.AchievementsUnlocked).ToString());
                _summary.Set("In this profile's games", prof.TotalGamerscore + " of " + prof.PossibleGamerscore + " G, " +
                                                         prof.AchievementsUnlocked + " of " + prof.AchievementsTotal + " achievements");
                _summary.Set("Games played", prof.Games.Count.ToString());

                _games.BeginUpdate();
                foreach (var t in prof.Games)
                {
                    var li = new ListViewItem(t.Name.Length > 0 ? t.Name : t.TitleIdHex) { Tag = t };
                    li.SubItems.Add(t.GamerscoreUnlocked + " / " + t.GamerscoreTotal);
                    li.SubItems.Add(t.AchievementsUnlocked + " / " + t.AchievementCount);
                    li.SubItems.Add(t.LastPlayed?.ToString("yyyy-MM-dd") ?? "");
                    li.SubItems.Add(t.TitleIdHex);
                    _games.Items.Add(li);
                }
                _games.EndUpdate();
                var again = _games.Items.Cast<ListViewItem>().FirstOrDefault(i => ((GpdTitle)i.Tag).TitleId == selected);
                if (again != null) { again.Selected = true; again.EnsureVisible(); }
                else if (_games.Items.Count > 0) _games.Items[0].Selected = true;
                else _achState.Text = "This profile has no game data.";

                FillAwardGames(prof, selectedAwardGame);
                _contents.Load(path);
                _signing.Load(path);
                Body.PerformLayout();
            });
        }

        // ---------------------------------------------------------------- gamer picture

        private static void ShowPicture(PictureBox box, byte[] png)
        {
            var old = box.Image;
            box.Image = null;
            old?.Dispose();
            if (png == null) return;
            try { using (var ms = new System.IO.MemoryStream(png)) using (var img = System.Drawing.Image.FromStream(ms)) box.Image = new System.Drawing.Bitmap(img); }
            catch (Exception) { }
        }

        private void ChangePicture()
        {
            if (!Has() || _profile == null) { Dlg.Warn("No profile", "Open or drop a profile first."); return; }
            string f = PickFile("Choose a picture (it is cropped square and resized)", "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*");
            if (f != null) ChangePictureTo(f);
        }

        /// <summary>Gives the open profile the image at <paramref name="imagePath"/> as its gamer picture.</summary>
        public void ChangePictureTo(string imagePath)
        {
            byte[] png64, png32;
            try { png64 = PackagePage.SquarePng(imagePath, 64, true); png32 = PackagePage.SquarePng(imagePath, 32, true); }
            catch (Exception ex) { Dlg.Warn("Gamer picture", ex.Message); return; }
            Edit(_picBtn, "Changing the gamer picture", p => p.SetGamerPicture(png64, png32) ? "Gamer picture changed." : "The profile already has that picture - nothing to change.");
        }

        private void ExportPicture()
        {
            byte[] png = _profile?.GamerPicture;
            if (!Has() || png == null) { Dlg.Warn("Gamer picture", "This profile has no gamer picture to save."); return; }
            string to = PickSave("Save the gamer picture", "gamerpicture.png", "PNG picture (*.png)|*.png");
            if (to == null) return;
            try { System.IO.File.WriteAllBytes(to, png); Log.Success("Gamer picture saved to " + to); }
            catch (Exception ex) { Dlg.Error("Gamer picture", ex.Message); }
        }

        // ---------------------------------------------------------------- gamercard

        private void SaveGamercard()
        {
            string motto = _motto.Text.Trim(), name = _realName.Text.Trim(), location = _location.Text.Trim(), bio = _bio.Text.Replace("\r\n", "\n").Trim();
            foreach (var item in new[] { ("motto", motto, Xbox360Profile.MottoSetting), ("name", name, Xbox360Profile.NameSetting), ("location", location, Xbox360Profile.LocationSetting), ("bio", bio, Xbox360Profile.BioSetting) })
                if (item.Item2.Length > XdbfFile.MaxStringLength(item.Item3))
                { Dlg.Warn("Gamercard", "The " + item.Item1 + " can be at most " + XdbfFile.MaxStringLength(item.Item3) + " characters (it has " + item.Item2.Length + ")."); return; }
            float? rep = null;
            if (_repRow.Visible && _rep.Text.Trim().Length > 0)
            {
                if (!float.TryParse(_rep.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r) || r < 0 || r > 100)
                { Dlg.Warn("Gamercard", "Reputation is a number from 0 to 100."); return; }
                rep = r;
            }
            int zone = _zone.SelectedIndex;
            Edit(_cardBtn, "Saving the gamercard", p =>
            {
                int changed = p.SetGamercardText(motto, name, location, bio);
                // a zone is only written when the profile has one already or a real zone was picked
                if ((p.Zone.HasValue || zone != 0) && p.SetZone(zone)) changed++;
                if (rep.HasValue && p.Reputation.HasValue && Math.Abs(p.Reputation.Value - rep.Value) > 0.004f && p.SetReputation(rep.Value)) changed++;
                return changed == 0 ? "The gamercard already says that - nothing to change." : "Gamercard saved (" + changed + " value" + (changed == 1 ? "" : "s") + " changed).";
            });
        }

        private void SetLastPlayed(Control trigger, bool clear)
        {
            var t = SelectedTitle();
            if (t == null || _profile == null) { Dlg.Warn("No game selected", "Select a game in the list first."); return; }
            DateTime? when = null;
            if (!clear)
            {
                if (!UnlockTime(_when, out when)) return;
                when = when ?? DateTime.UtcNow;
            }
            uint titleId = t.TitleId;
            Edit(trigger, "Setting last played for " + NameOf(t), p =>
                p.SetLastPlayed(titleId, when) ? NameOf(t) + ": last played " + (when.HasValue ? "set to " + when.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "cleared") + "."
                                               : NameOf(t) + ": last played already has that value.");
        }

        // ---------------------------------------------------------------- avatar awards

        private void FillAwardGames(Xbox360Profile prof, uint? selected)
        {
            _awardGames.BeginUpdate();
            _awardGames.Items.Clear();
            foreach (var kv in prof.AwardsByTitle.OrderBy(k => AwardGameName(prof, k.Key), StringComparer.CurrentCultureIgnoreCase))
            {
                var li = new ListViewItem(AwardGameName(prof, kv.Key)) { Tag = kv.Key };
                li.SubItems.Add(kv.Value.Count(a => a.Unlocked) + " / " + kv.Value.Count);
                li.SubItems.Add(kv.Key.ToString("X8"));
                _awardGames.Items.Add(li);
            }
            _awardGames.EndUpdate();
            if (prof.PecError != null) _awardState.Text = "The avatar awards could not be read: " + prof.PecError;
            else if (!prof.HasAwardStore) _awardState.Text = "This profile has no avatar award data yet - a console creates it the first time a game with awards is played.";
            else if (prof.AwardsByTitle.Count == 0) _awardState.Text = "This profile holds no avatar awards.";
            else
            {
                var sig = prof.AwardStoreSignature;
                _awardState.Text = "Award data signature: " + (sig == PecSignature.State.Valid ? "valid (signed by a console)." :
                    sig == PecSignature.State.BadSignature ? "not from a console keyvault - RGH / JTAG consoles only. With a keyvault loaded (Rehash & resign) the next award edit signs it."
                    : "its header hash is out of date.");
            }
            var again = _awardGames.Items.Cast<ListViewItem>().FirstOrDefault(i => (uint)i.Tag == selected);
            if (again != null) { again.Selected = true; again.EnsureVisible(); }
            else if (_awardGames.Items.Count > 0) _awardGames.Items[0].Selected = true;
        }

        private static string AwardGameName(Xbox360Profile prof, uint titleId)
        {
            var t = prof.Titles.FirstOrDefault(x => x.TitleId == titleId);
            return t != null && t.Name.Length > 0 ? t.Name : titleId.ToString("X8") + "  (not in the game list)";
        }

        private void ShowAwards()
        {
            _awards.Items.Clear();
            if (_profile == null || _awardGames.SelectedItems.Count == 0) return;
            _awards.BeginUpdate();
            foreach (var a in _profile.AwardsFor((uint)_awardGames.SelectedItems[0].Tag).OrderBy(x => x.EntryId))
            {
                var li = new ListViewItem(a.Name.Length > 0 ? a.Name : "Award " + a.EntryId.ToString("X16")) { Tag = a };
                li.SubItems.Add(a.Gender);
                li.SubItems.Add(a.Unlocked ? "Yes" : "No");
                li.SubItems.Add(a.UnlockTime?.ToString("yyyy-MM-dd") ?? "");
                if (!a.Unlocked) li.ForeColor = Theme.TextDim;
                _awards.Items.Add(li);
            }
            _awards.EndUpdate();
        }

        private static string DescribeAwards(string verb, ProfileEditResult r)
        {
            if (r.Awards == 0) return "Nothing to change - every avatar award was already " + verb.ToLowerInvariant() + ".";
            return verb + " " + r.Awards + " avatar award" + (r.Awards == 1 ? "" : "s") + (r.Games > 1 ? " in " + r.Games + " games" : "") + ".";
        }

        private void EditAwards(Control trigger, bool unlock, bool selectedOnly)
        {
            if (_profile == null || _awardGames.SelectedItems.Count == 0) { Dlg.Warn("No game selected", "Select a game in the list first."); return; }
            uint titleId = (uint)_awardGames.SelectedItems[0].Tag;
            string name = AwardGameName(_profile, titleId);
            string blocker = _profile.AwardBlocker(titleId);
            if (blocker != null) { Dlg.Warn(name, blocker); return; }
            List<ulong> ids = null;
            if (selectedOnly)
            {
                ids = _awards.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag as GpdAvatarAward).Where(a => a != null).Select(a => a.EntryId).ToList();
                if (ids.Count == 0) { Dlg.Warn("Nothing selected", "Select one or more awards in the list."); return; }
            }
            DateTime? when = null;
            if (unlock && !UnlockTime(_awardWhen, out when)) return;
            Edit(trigger, (unlock ? "Unlocking" : "Locking") + " avatar awards for " + name, p =>
                name + ": " + DescribeAwards(unlock ? "Unlocked" : "Locked", unlock ? p.UnlockAwards(titleId, ids, when ?? DateTime.UtcNow) : p.LockAwards(titleId, ids)));
        }

        // ---------------------------------------------------------------- game adder

        private void AddGamesFromProfile(Control trigger)
        {
            if (!Has() || _profile == null) { Dlg.Warn("No profile", "Open or drop a profile first."); return; }
            string source = PickFile("Choose the profile that has the games");
            if (source == null) return;
            if (string.Equals(System.IO.Path.GetFullPath(source), System.IO.Path.GetFullPath(WorkingFile.Path), StringComparison.OrdinalIgnoreCase))
            { Dlg.Info("Add games", "That is the profile you have open."); return; }
            Run(trigger, "Reading the other profile", async () =>
            {
                var games = await Task.Run(() => GameTemplate.FromProfile(source));
                OfferGames(trigger, games, "that profile");
            });
        }

        private void AddGamesFromGpd(Control trigger)
        {
            if (!Has() || _profile == null) { Dlg.Warn("No profile", "Open or drop a profile first."); return; }
            string[] files = PickFiles("Choose game database files (named after the title ID, e.g. 4D5307E6.gpd)", "Game databases (*.gpd)|*.gpd|All files (*.*)|*.*");
            if (files == null || files.Length == 0) return;
            var games = new List<GameTemplate>();
            var problems = new List<string>();
            foreach (string f in files)
            {
                try { games.Add(GameTemplate.FromGpdFile(f)); }
                catch (Exception ex) { problems.Add(System.IO.Path.GetFileName(f) + ": " + ex.Message); }
            }
            if (problems.Count > 0) Dlg.Warn("Add games", string.Join("\n", problems));
            if (games.Count > 0) OfferGames(trigger, games, "those files");
        }

        private void AddGameFromXex(Control trigger)
        {
            if (!Has() || _profile == null) { Dlg.Warn("No profile", "Open or drop a profile first."); return; }
            string[] files = PickFiles("Choose the game's executable (default.xex)", "Xbox 360 executables (*.xex)|*.xex|All files (*.*)|*.*");
            if (files == null || files.Length == 0) return;
            Run(trigger, "Reading the game's title data", async () =>
            {
                var games = new List<GameTemplate>();
                var problems = new List<string>();
                await Task.Run(() =>
                {
                    foreach (string f in files)
                    {
                        try { games.Add(GameTemplate.FromXex(f)); }
                        catch (Exception ex) { problems.Add(System.IO.Path.GetFileName(f) + ": " + ex.Message); }
                    }
                });
                if (problems.Count > 0) Dlg.Warn("Add a game from its XEX", string.Join("\n", problems));
                if (games.Count > 0) OfferGames(trigger, games, "those files");
            });
        }

        /// <summary>Adds the games the profile does not have yet: all of them, or one picked from the list.</summary>
        private void OfferGames(Control trigger, List<GameTemplate> games, string from)
        {
            var profile = _profile;
            if (profile == null) return;
            var addable = games.Where(g => profile.AddGameBlocker(g) == null).ToList();
            if (addable.Count == 0)
            {
                Dlg.Info("Add games", games.Count == 0 ? "No games were found in " + from + "." : "This profile already has every game from " + from + ".");
                return;
            }
            var chosen = addable;
            if (addable.Count > 1)
            {
                var items = new List<string> { "All " + addable.Count + " games this profile does not have yet" };
                items.AddRange(addable.Select(g => g.ToString()));
                int pick = Dlg.Choose("Add games", "Which games should be added?", items, "Add");
                if (pick < 0) return;
                if (pick > 0) chosen = new List<GameTemplate> { addable[pick - 1] };
            }
            Edit(trigger, "Adding " + (chosen.Count == 1 ? chosen[0].Name : chosen.Count + " games"), p =>
            {
                int awards = 0;
                foreach (var g in chosen)
                {
                    p.AddGame(g);
                    if (g.AwardGpd != null && p.AwardsByTitle.ContainsKey(g.TitleId)) awards++;
                }
                return "Added " + (chosen.Count == 1 ? chosen[0].Name : chosen.Count + " games") + " - " + chosen.Sum(g => g.AchievementCount) + " achievements (" +
                       chosen.Sum(g => g.Gamerscore) + " G) ready to earn" + (awards > 0 ? ", avatar awards for " + awards + " of them" : "") + ".";
            });
        }

        private readonly XTextBox _lookupBox = new XTextBox("Title ID (e.g. 415608FC) or part of a game's name", false, true);
        private readonly ListBox _lookupResults = new ListBox { Height = 160, IntegralHeight = false };

        /// <summary>A Title ID gives the game's name; any other text lists the Xbox 360 games whose name contains it.</summary>
        private void LookUp(Control trigger)
        {
            string text = _lookupBox.Text.Trim();
            if (text.Length == 0) { Dlg.Info("Title ID lookup", "Type a Title ID (8 hex digits) or part of a game's name."); return; }
            Run(trigger, "Looking up " + text, async () =>
            {
                _lookupResults.Items.Clear();
                if (TitleLookup.TryParseTitleId(text, out uint id))
                {
                    string name = await TitleLookup.NameAsync(id, System.Threading.CancellationToken.None);
                    if (name == null) { Log.Warn("No game with the Title ID " + id.ToString("X8") + " is known to the title database."); _lookupResults.Items.Add("(no game with that Title ID)"); return; }
                    _lookupResults.Items.Add(new TitleMatch { TitleId = id, Name = name });
                    Log.Success(id.ToString("X8") + " is " + name + ".");
                    return;
                }
                var found = await TitleLookup.SearchAsync(text, System.Threading.CancellationToken.None);
                foreach (var m in found) _lookupResults.Items.Add(m);
                if (found.Count == 0) _lookupResults.Items.Add("(no Xbox 360 game name contains \"" + text + "\")");
                Log.Info(found.Count + " Xbox 360 game(s) found for \"" + text + "\"" + (found.Count == 25 ? " (the first 25 - type more of the name to narrow it down)." : "."));
            });
        }

        /// <summary>Adds a game by Title ID with the name and achievement list of the public title database.</summary>
        private void AddGameById(Control trigger, uint? known)
        {
            if (!Has() || _profile == null) { Dlg.Warn("No profile", "Open or drop a profile first."); return; }
            uint id;
            if (known.HasValue) id = known.Value;
            else
            {
                string text = Dlg.Input("Add a game by Title ID", "The game's Title ID - 8 hex digits, for example 415608FC. Its name and achievement list are looked up in the public title database (dbox.tools); only the Title ID is sent.", "", true);
                if (text == null) return;
                if (!TitleLookup.TryParseTitleId(text, out id)) { Dlg.Warn("Title ID", "A Title ID is 8 hex digits, for example 415608FC."); return; }
            }
            Run(trigger, "Looking up " + id.ToString("X8"), async () =>
            {
                string name = await TitleLookup.NameAsync(id, System.Threading.CancellationToken.None);
                if (name == null) { Dlg.Warn("Add a game by Title ID", "No game with the Title ID " + id.ToString("X8") + " is known to the title database."); return; }
                var achievements = await TitleLookup.AchievementsAsync(id, System.Threading.CancellationToken.None);
                if (achievements.Count == 0) { Dlg.Warn("Add a game by Title ID", name + " (" + id.ToString("X8") + "): the title database lists no Xbox 360 achievements for it, so there is nothing to add."); return; }
                GameTemplate game;
                try { game = GameTemplate.FromOnline(id, name, achievements); }
                catch (Exception ex) { Dlg.Warn("Add a game by Title ID", ex.Message); return; }
                string blocker = _profile?.AddGameBlocker(game);
                if (blocker != null) { Dlg.Info("Add a game by Title ID", blocker); return; }
                if (!Dlg.Confirm("Add " + name, name + " (" + id.ToString("X8") + "): " + game.AchievementCount + " achievements, " + game.Gamerscore + " gamerscore, all locked.\n\n" +
                                 "From the public title database, so downloadable-content achievements are included and the achievement pictures are not (add the game from its XEX for those).", "Add")) return;
                OfferGames(trigger, new List<GameTemplate> { game }, "the title database");
            });
        }

        private GpdTitle SelectedTitle() => _games.SelectedItems.Count > 0 ? _games.SelectedItems[0].Tag as GpdTitle : null;

        private static string NameOf(GpdTitle t) => t.Name.Length > 0 ? t.Name : t.TitleIdHex;

        private void ShowAchievements()
        {
            _ach.Items.Clear();
            var t = SelectedTitle();
            if (t == null || _profile == null) { _achState.Text = ""; return; }

            var list = _profile.AchievementsFor(t.TitleId);
            if (list.Count == 0)
            {
                _achState.Text = "The profile holds only the totals for \"" + NameOf(t) + "\" (" + t.AchievementsUnlocked + " of " + t.AchievementCount +
                                 " unlocked, " + t.GamerscoreUnlocked + " G) - not its achievement list, so there is nothing to edit here.";
                return;
            }

            _achState.Text = NameOf(t);
            _ach.BeginUpdate();
            foreach (var ac in list.OrderByDescending(x => x.Unlocked).ThenByDescending(x => x.Gamerscore))
            {
                var li = new ListViewItem(ac.Name.Length > 0 ? ac.Name : "Achievement " + ac.Id) { Tag = ac };
                li.SubItems.Add(ac.Gamerscore.ToString());
                li.SubItems.Add(ac.Unlocked ? (ac.UnlockedOnline ? "Yes (on LIVE)" : "Yes (offline)") : "No");
                li.SubItems.Add(ac.UnlockTime?.ToString("yyyy-MM-dd") ?? "");
                if (!ac.Unlocked) li.ForeColor = Theme.TextDim;
                _ach.Items.Add(li);
            }
            _ach.EndUpdate();
        }

        // ---------------------------------------------------------------- editing

        private static string Describe(string verb, ProfileEditResult r)
        {
            if (r.Achievements == 0) return "Nothing to change - every achievement was already " + verb.ToLowerInvariant() + ".";
            return verb + " " + r.Achievements + " achievement" + (r.Achievements == 1 ? "" : "s") +
                   (r.Games > 1 ? " in " + r.Games + " games" : "") + " (" + (verb == "Locked" ? "−" : "+") + r.Gamerscore + " G).";
        }

        private static ProfileAccount Account(Xbox360Profile p) =>
            p.Account ?? throw new InvalidOperationException(p.AccountError ?? "This profile has no readable Account file.");

        /// <summary>The unlock time typed on the Games tab (null = now). False when it cannot be used; the user is told why.</summary>
        private static bool UnlockTime(XTextBox box, out DateTime? when)
        {
            when = null;
            string text = box.Text.Trim();
            if (text.Length == 0) return true;
            if (!DateTime.TryParse(text, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.AssumeLocal, out DateTime local))
            {
                Dlg.Warn("Unlock date", "That is not a date and time. Use something like 2024-06-15 18:30, or leave the box empty to use the current time.");
                return false;
            }
            if (local < new DateTime(2005, 11, 22) || local > DateTime.Now.AddMinutes(5))
            {
                Dlg.Warn("Unlock date", "Pick a time between the Xbox 360's launch (22 November 2005) and now.");
                return false;
            }
            when = local.ToUniversalTime();
            return true;
        }

        private void ChangeGamertag()
        {
            string tag = _tagBox.Text.Trim();
            string problem = ProfileAccount.GamertagProblem(tag);
            if (problem != null) { Dlg.Warn("Gamertag", problem); return; }
            Edit(_tagBtn, "Changing the gamertag", p =>
            {
                p.SetGamertag(tag);
                return "Gamertag changed to \"" + tag + "\"." +
                       (ProfileAccount.IsStandardGamertag(tag) ? "" : " It is not a name Xbox LIVE would accept (letters, digits and single spaces, starting with a letter) - it still shows offline.");
            });
        }

        private void EditGame(Control trigger, bool unlock, bool selectedOnly)
        {
            var t = SelectedTitle();
            if (t == null || _profile == null) { Dlg.Warn("No game selected", "Select a game in the list first."); return; }
            string blocker = _profile.EditBlocker(t.TitleId);
            if (blocker != null) { Dlg.Warn(NameOf(t), blocker); return; }
            List<uint> ids = null;
            if (selectedOnly)
            {
                ids = _ach.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag as GpdAchievement).Where(a => a != null).Select(a => a.Id).ToList();
                if (ids.Count == 0) { Dlg.Warn("Nothing selected", "Select one or more achievements in the list."); return; }
            }
            uint titleId = t.TitleId;
            string verb = unlock ? "Unlocked" : "Locked";
            DateTime? when = null;
            if (unlock && !UnlockTime(_when, out when)) return;
            Edit(trigger, (unlock ? "Unlocking" : "Locking") + " achievements for " + NameOf(t), p =>
            {
                var r = unlock ? (ids == null ? p.UnlockAllForTitle(titleId, when ?? DateTime.UtcNow) : p.UnlockAchievements(titleId, ids, when ?? DateTime.UtcNow))
                               : (ids == null ? p.LockAllForTitle(titleId) : p.LockAchievements(titleId, ids));
                return NameOf(t) + ": " + Describe(verb, r);
            });
        }

        /// <summary>
        /// The one-click edit: change the profile in memory, write it into the package in one verified step (a backup is
        /// made first), then rehash and re-sign it. On any failure the file on disk is untouched and the page re-reads it.
        /// </summary>
        private async void Edit(Control trigger, string busyText, Func<Xbox360Profile, string> change)
        {
            if (_editing) return;
            var profile = _profile;
            string path = WorkingFile.Path;
            if (!Has() || profile == null || !string.Equals(profile.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                Dlg.Warn("No profile", "Open or drop a profile first.");
                return;
            }
            var op = WorkingFile.BeginOperation(busyText);
            if (op == null) return;
            _editing = true;
            bool written = false;
            try
            {
                await RunAsync(trigger, busyText, async () =>
                {
                    string message = change(profile);                       // in memory only
                    if (profile.HasChanges)
                    {
                        BackupOnce(path);
                        var kv = profile.AwardsPending ? KeyVaultSession.Auto() : null;   // only the award container needs it at this point
                        await Task.Run(() => profile.SaveChanges(kv));      // all files or none; verified before it replaces the profile
                        written = true;
                    }
                    Log.Success(message);
                });
            }
            finally { op.Dispose(); }

            try
            {
                if (written) await _signing.RehashAndResignAsync();          // hashes are already valid; this renews the signature
            }
            finally
            {
                _editing = false;
                if (WorkingFile.Path == path) Reload();                     // show what is on disk now
            }
        }
    }
}
