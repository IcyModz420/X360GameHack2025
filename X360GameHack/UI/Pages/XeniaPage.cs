using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>
    /// Everything Xenia on one page: the games it can start (each copied with Play in Xenia, or added by hand), its saves -
    /// with saves from anywhere (downloaded, modded, a friend's) installed for one of its profiles - its profiles, its
    /// patches and its setup. Saves and profiles are kept exactly as on a console's hard drive (see XeniaContent).
    /// </summary>
    public sealed class XeniaPage : PageBase
    {
        public override string Title => "Xenia";
        public override string Subtitle => "Your Xbox 360 games in Xenia - play them, and give them saves, profiles, DLC and patches.";
        public override string PageGlyph => Glyph.Play;

        private const string SetPath = "Set Xenia's path on the Setup tab.";

        private readonly ListView _games = ListStyle.Create(("Game", 300), ("Title ID", 90), ("Kind", 100), ("Size", 90), ("Saves", 60), ("DLC / updates", 130), ("Last played", 140));
        private readonly TextBlock _gamesInfo = Wrapped(Theme.Small, Theme.TextDim);
        private readonly XButton _play, _gameSave, _gameDlc, _gameFromXbox, _gameFolder, _gameRemove;
        private readonly SaveBrowser _saves = new SaveBrowser("Xenia not found", SetPath, offersXenia: false);
        private readonly ProfileFinder _profiles = new ProfileFinder("Xenia not found", SetPath, offersXenia: false);
        private readonly TextBlock _savesWhere = Wrapped(Theme.Small, Theme.TextDim), _profilesWhere = Wrapped(Theme.Small, Theme.TextDim);
        private readonly VStack _gamesTab, _savesTab, _profilesTab;
        private List<XeniaGame> _list = new List<XeniaGame>();
        private string _root;
        private bool _savesFound, _profilesFound, _gamesLoaded;
        private int _scan;

        private static TextBlock Wrapped(System.Drawing.Font font, System.Drawing.Color color) =>
            new TextBlock("", font, color) { Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix };

        private static XButton Add(WrapPanel row, string text, string glyph, BtnKind kind, Action click, string tip = null)
        {
            var b = row.Add(new XButton(text, glyph, kind, (s, e) => click()));
            if (tip != null) Tip.Set(b, tip);
            return b;
        }

        public XeniaPage() : base(false)
        {
            // ---- Games
            _gamesTab = AddSection("Games", Glyph.Game);
            var gbar = new Card(null) { Padding = new Padding(Theme.S(14)) };
            var grow = new WrapPanel();
            _play = Add(grow, "Play", Glyph.Play, BtnKind.Primary, Play, "Starts the selected game in Xenia (double-click a game does the same).");
            _gameSave = Add(grow, "Install a save...", Glyph.Save, BtnKind.Secondary, InstallSaveForGame,
                            "A save for this game - a console save file, or a .zip / .rar holding one (a downloaded or modded save works too) - goes to the profile Xenia signs in to.");
            _gameDlc = Add(grow, "Add DLC / updates...", Glyph.Download, BtnKind.Secondary, AddDlcForGame,
                           "This game's downloadable content (map packs...) and title updates - package files, or a .zip / .rar holding them - go where Xenia looks for them.");
            _gameFromXbox = Add(grow, "DLC / updates from my Xbox", Glyph.Hdd, BtnKind.Secondary, async () => { if (await XeniaTransfer.AddExtrasFromXboxAsync(Selected())) LoadGames(); },
                                "One click: this game's newest title update and all its DLC, from your Xbox drive (open it in Drive Explorer) or your connected console, go into Xenia - " +
                                "only what Xenia does not have yet. Xenia uses one title update per game; an older one there is moved to your Saves folder's Backups.");
            _gameFolder = Add(grow, "Open folder", Glyph.FolderOpen, BtnKind.Secondary, () => { var g = Selected(); if (g != null) Tools.OpenFolder(g.Folder); });
            Add(grow, "Add a game...", Glyph.Add, BtnKind.Secondary, AddGame, "A game anywhere on this PC: its default.xex, a disc image (.iso) or a game package.");
            _gameRemove = Add(grow, "Remove...", Glyph.Delete, BtnKind.Secondary, RemoveGame,
                              "A game copied here: its copy goes to the Recycle Bin (its saves stay). A game you added: taken off the list only.");
            Add(grow, "Graphics settings...", Glyph.Video, BtnKind.Secondary, () => ShowHosted(typeof(XeniaSettingsPage)),
                "Resolution scale (upscaling), FSR / CAS, anti-aliasing, VSync, frame rate and every other Xenia setting - set before the game starts.");
            Add(grow, "Refresh", Glyph.Refresh, BtnKind.Ghost, LoadGames);
            gbar.Add(grow);
            gbar.Add(_gamesInfo);
            _gamesTab.Add(gbar);
            _gamesTab.Add(_games);
            _gamesTab.FillChild = _games;
            _games.MultiSelect = false;
            _games.SelectedIndexChanged += (s, e) => UpdateGameButtons();
            _games.DoubleClick += (s, e) => Play();
            _games.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Play(); } };

            // ---- Saves
            _savesTab = AddSection("Saves", Glyph.Save);
            var sbar = new Card(null) { Padding = new Padding(Theme.S(14)) };
            var srow = new WrapPanel();
            Add(srow, "Install a save...", Glyph.Add, BtnKind.Primary, () => InstallSaves(false),
                "Console save files - or a .zip / .rar holding them, as saves are shared online - go to the profile Xenia signs in to, whichever profile made them.");
            Add(srow, "Install saves from a folder...", Glyph.Folder, BtnKind.Secondary, () => InstallSaves(true),
                "Every save in a folder and its sub-folders (a USB drive's Content folder, a backup...).");
            Add(srow, "Open Xenia's content folder", Glyph.FolderOpen, BtnKind.Secondary, OpenContentFolder);
            sbar.Add(srow);
            sbar.Add(_savesWhere);
            sbar.Add(new TextBlock("Your console's saves keep their own profile instead: Neighborhood or Drive Explorer > Saves > select > Copy to Xenia (the profile comes along). " +
                                   "Whatever a save replaces in Xenia is backed up to your Saves folder first.", Theme.Small, Theme.TextDim) { Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix });
            _savesTab.Add(sbar);
            _savesTab.Add(_saves);
            _savesTab.FillChild = _saves;

            // ---- Profiles
            _profilesTab = AddSection("Profiles", Glyph.Profile);
            var pbar = new Card(null) { Padding = new Padding(Theme.S(14)) };
            var prow = new WrapPanel();
            Add(prow, "Sign in to selected", Glyph.Check, BtnKind.Primary, SignInSelected, "Xenia signs in to the selected profile when a game starts - its saves are the ones the game sees.");
            Add(prow, "Create a profile...", Glyph.Add, BtnKind.Secondary, CreateProfile, "A new offline profile, made exactly the way Xenia's own Create Profile makes it.");
            Add(prow, "Add a profile...", Glyph.OpenFile, BtnKind.Secondary, AddProfile, "Console profile files (or a .zip / .rar holding them) - unpacked as Xenia's own installer does.");
            pbar.Add(prow);
            pbar.Add(_profilesWhere);
            _profilesTab.Add(pbar);
            _profilesTab.Add(_profiles);
            _profilesTab.FillChild = _profiles;

            // ---- pages of their own, shown as tabs here
            AddPageSection("Settings", Glyph.Video, new XeniaSettingsPage());
            AddPageSection("Xenia Patches", Glyph.Wrench, new XeniaPatchesPage());
            AddPageSection("Setup", Glyph.Settings, new XeniaSetupPage());
            UpdateGameButtons();
        }

        // ------------------------------------------------------------ state

        private static string XeniaOrNull()
        {
            string xenia = Tools.Xenia;
            return !string.IsNullOrWhiteSpace(xenia) && File.Exists(xenia) ? xenia : null;
        }

        private void Sync()
        {
            string xenia = XeniaOrNull();
            string root = xenia != null ? XeniaHost.StorageRoot(xenia) : null;
            if (root != _root)
            {
                _root = root;
                var store = xenia != null ? XeniaContent.Store(xenia) : null;
                _saves.SetStore(store);
                _profiles.SetStore(store);
                _savesFound = _profilesFound = false;
            }
            string signedIn = xenia != null ? XeniaContent.SignedIn(xenia) : "";
            string who = signedIn.Length > 0 ? XeniaTransfer.LabelOf(xenia, signedIn) : "";
            _savesWhere.Text = xenia == null ? "Xenia was not found - " + SetPath
                : (who.Length > 0 ? "New saves go to " + who + " - the profile Xenia signs in to. " : "Xenia signs in to no profile yet - Install a save... asks which profile gets it. ") +
                  "A save made on another profile (downloaded, modded or a friend's) is given to that profile as it goes in: Xenia checks no signature, so nothing needs resigning.";
            _profilesWhere.Text = xenia == null ? "Xenia was not found - " + SetPath
                : (who.Length > 0 ? "Xenia signs in to " + who + " when a game starts." : "Xenia signs in to no profile yet - you are asked when a game starts.") +
                  "  •  Kept in " + XeniaContent.Root(xenia);
            _savesWhere.Parent?.PerformLayout();
            _profilesWhere.Parent?.PerformLayout();
            if (!Visible || Tabs == null) return;
            if (Tabs.SelectedPanel == _gamesTab && !_gamesLoaded) { _gamesLoaded = true; LoadGames(); }
            if (xenia == null) return;
            if (Tabs.SelectedPanel == _savesTab && !_savesFound) { _savesFound = true; _saves.Scan(); }
            if (Tabs.SelectedPanel == _profilesTab && !_profilesFound) { _profilesFound = true; _profiles.Scan(); }
        }

        protected override void OnSectionChanged() => Sync();

        public override void OnActivated() => Sync();

        // ------------------------------------------------------------ games

        private XeniaGame Selected() => _games.SelectedItems.Count > 0 ? _games.SelectedItems[0].Tag as XeniaGame : null;

        private void UpdateGameButtons()
        {
            bool any = Selected() != null;
            foreach (var b in new[] { _play, _gameSave, _gameDlc, _gameFromXbox, _gameFolder, _gameRemove }) b.Enabled = any;
        }

        private async void LoadGames()
        {
            int scan = ++_scan;
            _gamesInfo.Text = "Looking for your Xenia games...";
            List<XeniaGame> games;
            try { games = await Task.Run(() => XeniaLibrary.Scan(XeniaPlay.GamesDir, AppSettings.Current.XeniaAddedGames)); }
            catch (Exception ex) { _gamesInfo.Text = "Could not read the games folder: " + ex.Message; return; }
            if (scan != _scan) return;
            _list = games;
            Fill();
            // the slow parts afterwards: each game's own title (read from its XEX) and its size
            await Task.Run(() =>
            {
                foreach (var g in games)
                {
                    string own = XeniaLibrary.OwnTitle(g);
                    if (own != null && !own.Equals(g.Name, StringComparison.OrdinalIgnoreCase)) g.Name = own + "  (" + g.Name + ")";
                    g.Size = XeniaLibrary.SizeOf(g);
                }
            });
            if (scan == _scan) Fill();
        }

        private void Fill()
        {
            string xenia = XeniaOrNull();
            string signedIn = xenia != null ? XeniaContent.SignedIn(xenia) : "";
            string keep = Selected()?.LaunchPath;
            _games.BeginUpdate();
            _games.Items.Clear();
            foreach (var g in _list)
            {
                var it = new ListViewItem(g.Name) { Tag = g };
                it.SubItems.Add(g.TitleIdHex);
                it.SubItems.Add(g.Kind);
                it.SubItems.Add(g.Size < 0 ? "..." : FileSniffer.FormatSize(g.Size));
                it.SubItems.Add(xenia == null || signedIn.Length == 0 || g.TitleId == 0 ? "" : XeniaLibrary.SaveCount(xenia, signedIn, g.TitleId).ToString());
                it.SubItems.Add(xenia == null ? "" : XeniaLibrary.ExtrasOf(xenia, g.TitleId));
                var played = XeniaLibrary.LastPlayed(g.LaunchPath);
                it.SubItems.Add(played.HasValue ? played.Value.ToString("yyyy-MM-dd HH:mm") : "");
                _games.Items.Add(it);
                if (keep != null && XeniaLibrary.Same(keep, g.LaunchPath)) it.Selected = true;
            }
            if (_games.SelectedItems.Count == 0 && _games.Items.Count > 0) _games.Items[0].Selected = true;
            _games.EndUpdate();
            string who = signedIn.Length > 0 ? XeniaTransfer.LabelOf(xenia, signedIn) : "";
            _gamesInfo.Text = _list.Count == 0
                ? "No games yet. Play a game from your console or an Xbox drive in Xenia (Neighborhood, FTP or Drive Explorer > Play in Xenia) and it shows up here - or press Add a game... for a XEX, disc image or game package on this PC."
                : _list.Count + (_list.Count == 1 ? " game" : " games") + " - double-click one to play. Copies are kept in " + XeniaPlay.GamesDir + ". DLC and updates from your Xbox drive: Drive Explorer > select them > Copy to Xenia." +
                  (xenia == null ? "  Xenia was not found - " + SetPath : who.Length > 0 ? "  Saves are counted for " + who + ", the profile Xenia signs in to." : "");
            _gamesInfo.Parent?.PerformLayout();
            UpdateGameButtons();
        }

        private void Play()
        {
            var g = Selected();
            if (g == null) { Dlg.Info("Play", "Select a game first."); return; }
            if (!File.Exists(g.LaunchPath)) { Dlg.Warn("Play", g.Name + " is no longer at " + g.LaunchPath + "."); LoadGames(); return; }
            if (XeniaProcess.Running().Count > 0 && !Dlg.Confirm("Play", "Xenia is already running. Start " + g.Name + " in another Xenia window?", "Start it")) return;
            if (!XeniaHost.Start(g.LaunchPath)) return;
            XeniaLibrary.MarkPlayed(g.LaunchPath);
            Log.Success("Started " + g.Name + " in Xenia (" + g.LaunchPath + ").");
            Fill();
        }

        private async void InstallSaveForGame()
        {
            var g = Selected();
            if (g == null) return;
            if (g.TitleId == 0)
            {
                Dlg.Info("Install a save", "The program could not read which game " + g.Name + " is (its Title ID), so it cannot tell which saves are its own. Use Saves > Install a save... instead.");
                return;
            }
            if (!XeniaTransfer.CheckReady("Install a save")) return;
            var files = PickFiles("Saves for " + g.Name + " - console save files, or a .zip / .rar holding them", "Saves and archives (*.*)|*.*");
            if (files == null) return;
            if (!await XeniaTransfer.InstallSavesAsync(files, g.TitleId, g.Name)) return;
            _savesFound = false;
            Fill();
        }

        private async void AddDlcForGame()
        {
            var g = Selected();
            if (g == null) return;
            if (!XeniaTransfer.CheckReady("Add DLC / updates")) return;
            var files = PickFiles("Downloadable content and title updates for " + g.Name + " - package files, or a .zip / .rar holding them", "Packages and archives (*.*)|*.*");
            if (files == null) return;
            uint title = g.TitleId;
            await XeniaTransfer.AddAsync(files,
                p => (p.ContentType == XeniaContent.DlcType || p.ContentType == XeniaContent.UpdateType) && (title == 0 || p.TitleId == title),
                "Add DLC / updates",
                "No downloadable content or title updates" + (title != 0 ? " for " + g.Name + " (" + g.TitleIdHex + ")" : "") + " were found there.");
        }

        private void AddGame()
        {
            string f = PickFile("A game for Xenia: its default.xex, a disc image (.iso) or a game package", "Xbox 360 games (*.xex;*.iso;*.*)|*.xex;*.iso;*.*|All files (*.*)|*.*");
            if (f == null) return;
            var g = XeniaLibrary.FromFile(f, true);
            if (g == null) { Dlg.Info("Add a game", Path.GetFileName(f) + " is not a game Xenia can start - pick a XEX, a disc image (.iso) or a game package (Games on Demand, Xbox LIVE Arcade)."); return; }
            if (_list.Any(x => XeniaLibrary.Same(x.LaunchPath, g.LaunchPath))) { Dlg.Info("Add a game", g.Name + " is on the list already."); return; }
            var s = AppSettings.Current;
            s.XeniaAddedGames.RemoveAll(p => XeniaLibrary.Same(p, g.LaunchPath));
            s.XeniaAddedGames.Add(g.LaunchPath);
            AppSettings.Save();
            Log.Success("Added " + g.Name + " to the Xenia games (" + g.LaunchPath + ").");
            LoadGames();
        }

        private void RemoveGame()
        {
            var g = Selected();
            if (g == null) return;
            if (g.Added)
            {
                if (!Dlg.Confirm("Remove", "Take " + g.Name + " off this list? Its files stay where they are:\n" + g.LaunchPath, "Take it off")) return;
                AppSettings.Current.XeniaAddedGames.RemoveAll(p => XeniaLibrary.Same(p, g.LaunchPath));
                AppSettings.Save();
                Log.Info(g.Name + " was taken off the Xenia games.");
                LoadGames();
                return;
            }
            // only ever one of the copies in the games folder itself - never anything else
            string gamesDir = Path.GetFullPath(XeniaPlay.GamesDir).TrimEnd('\\');
            string folder = Path.GetFullPath(g.Folder).TrimEnd('\\');
            if (!string.Equals(Path.GetDirectoryName(folder), gamesDir, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(folder) ||
                (new DirectoryInfo(folder).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                Dlg.Info("Remove", g.Name + " is not one of the copies in " + gamesDir + ", so nothing is deleted.");
                return;
            }
            if (XeniaProcess.Running().Count > 0) { Dlg.Info("Remove", "Xenia is running. Close it first - it may be using " + g.Name + "'s files."); return; }
            string size = g.Size >= 0 ? " (" + FileSniffer.FormatSize(g.Size) + ")" : "";
            if (!Dlg.Confirm("Remove", "Delete the copy of " + g.Name + size + " from this PC?\n\nIt goes to the Recycle Bin. Its saves in Xenia are kept, and Play in Xenia can copy it again from your console or Xbox drive.",
                             "Move to Recycle Bin", danger: true)) return;
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                Log.Success(g.Name + " was moved to the Recycle Bin (" + folder + ").");
            }
            catch (OperationCanceledException) { Log.Warn("Remove cancelled."); }
            catch (Exception ex) { Log.Error("Could not remove " + g.Name + ": " + ex.Message); Dlg.Error("Remove", ex.Message); }
            LoadGames();
        }

        // ------------------------------------------------------------ saves

        private async void InstallSaves(bool folder)
        {
            if (!XeniaTransfer.CheckReady("Install a save")) return;
            IEnumerable<string> paths;
            if (folder)
            {
                var d = PickFolder("A folder with saves (a USB drive's Content folder, a backup, an unpacked download...)");
                if (d == null) return;
                paths = new[] { d };
            }
            else
            {
                var f = PickFiles("Saves to install - console save files, or a .zip / .rar holding them", "Saves and archives (*.*)|*.*");
                if (f == null) return;
                paths = f;
            }
            if (!await XeniaTransfer.InstallSavesAsync(paths)) return;
            _savesFound = false;
            Sync();
            if (Tabs.SelectedPanel == _savesTab) { _savesFound = true; _saves.Scan(); }
            _gamesLoaded = false;
        }

        private void OpenContentFolder()
        {
            string xenia = XeniaOrNull();
            if (xenia == null) { Dlg.Info("Xenia", "Xenia was not found. " + SetPath); return; }
            string root = XeniaContent.Root(xenia);
            Directory.CreateDirectory(root);
            Tools.OpenFolder(root);
        }

        // ------------------------------------------------------------ profiles

        private void SignInSelected()
        {
            var p = _profiles.SelectedItems.FirstOrDefault();
            if (p == null) { Dlg.Info("Sign in", "Select a profile first (press Find profiles if the list is empty)."); return; }
            if (!XeniaTransfer.CheckReady("Sign in")) return;
            string xenia = XeniaOrNull();
            try
            {
                if (!XeniaContent.HasProfile(xenia, p.ProfileId)) { Dlg.Info("Sign in", "Xenia does not have " + p.Label + "."); return; }
                XeniaContent.SignIn(xenia, p.ProfileId);
                Log.Success("Xenia signs in to " + XeniaTransfer.LabelOf(xenia, p.ProfileId) + " when a game starts.");
            }
            catch (Exception ex) { Log.Error("Signing in failed: " + ex.Message); Dlg.Error("Sign in", ex.Message); }
            Sync();
            _gamesLoaded = false;
        }

        private void CreateProfile()
        {
            if (!XeniaTransfer.CheckReady("Create a profile")) return;
            string xenia = XeniaOrNull();
            string tag = Dlg.Input("Create a profile", "Gamertag (a letter first, then letters and digits, at most 15):");
            if (string.IsNullOrWhiteSpace(tag)) return;
            tag = tag.Trim();
            string problem = XeniaContent.GamertagProblem(tag);
            if (problem != null) { Dlg.Info("Create a profile", problem); return; }
            try
            {
                string id = XeniaContent.CreateProfile(xenia, tag);
                Log.Success("Made the profile " + tag + " (" + id + ") in Xenia, the way Xenia's own Create Profile makes it.");
                string current = XeniaContent.SignedIn(xenia);
                if (current.Length == 0 || !XeniaContent.HasProfile(xenia, current))
                {
                    XeniaContent.SignIn(xenia, id);
                    Log.Info("Xenia signs in to " + tag + " when a game starts.");
                }
            }
            catch (Exception ex) { Log.Error("Creating the profile failed: " + ex.Message); Dlg.Error("Create a profile", ex.Message); return; }
            _profilesFound = false;
            Sync();
            if (Tabs.SelectedPanel == _profilesTab) { _profilesFound = true; _profiles.Scan(); }
        }

        private async void AddProfile()
        {
            if (!XeniaTransfer.CheckReady("Add a profile")) return;
            var f = PickFiles("Console profiles to add to Xenia (profile files, or a .zip / .rar holding them)", "Profiles and archives (*.*)|*.*");
            if (f == null) return;
            if (!await XeniaTransfer.AddAsync(f, p => p.ContentType == XeniaContent.ProfileType, "Add a profile", "No Xbox 360 profiles were found there.")) return;
            _profilesFound = false;
            Sync();
            if (Tabs.SelectedPanel == _profilesTab) { _profilesFound = true; _profiles.Scan(); }
        }
    }
}
