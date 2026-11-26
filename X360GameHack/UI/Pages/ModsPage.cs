using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Mods;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>
    /// Mods: optional add-on files for games and save games. The program itself and every console tool in it are
    /// free; a mod is a separate .x360mod file that is either free (it simply loads) or paid (unlocked with a
    /// licence token tied to this PC). A paid mod's contents are never in this open-source program - they are
    /// sealed in the file and the key comes from the seller's licence server.
    ///
    /// "All mods" is the public catalog: every mod a licence unlocks and what its options do (names and descriptions only,
    /// read from the mod server - nothing of any mod). "Game mods" uses game mods: pick the game, a group, an option, fill
    /// in its values and apply. "My mod files" is the mod files and the licence. "Mod Maker" exists only on the PC that
    /// holds the X360GameHack team's signing key: the program loads no mod the team did not sign (see ModSignature).
    /// </summary>
    public sealed class ModsPage : PageBase
    {
        public override string Title => "Mods";
        public override string Subtitle => "Add-on mods for games and save games. The program and every console tool stay free - mods are optional extras.";
        public override string PageGlyph => Glyph.Unlock;

        // ---- tab 0: every mod a licence unlocks (the public catalog)
        private readonly ListView _cat = ListStyle.Create(("Game", 230), ("Mod", 270), ("Options", 70), ("Status", 190));
        private readonly TextBlock _catSummary = new TextBlock("", Theme.Body, Theme.Text) { Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix };
        private readonly TextBox _catDetail = new TextBox
        {
            ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, WordWrap = true,
            BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Body, TabStop = false, Height = Theme.S(260)
        };
        private XButton _shopBtn;
        private VStack _catTab, _filesTab;
        private ModCatalogList _catalog;
        private bool _catLoaded, _catBusy;

        // ---- tab 1: use game mods
        private readonly XCombo _gameCombo = new XCombo { Width = Theme.S(340) };
        private readonly XTextBox _find = new XTextBox("Find an option, e.g. gravity") { Width = Theme.S(260) };
        private readonly TextBlock _gameNote = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(20) };
        private readonly ListView _groups = ListStyle.Create(("Group", 300));
        private readonly ListView _options = ListStyle.Create(("Option", 250), ("Kind", 70));
        private readonly TextBlock _optName = new TextBlock("", Theme.Body, Theme.Text) { Height = Theme.S(22) };
        private readonly TextBlock _optDoes = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(54), Flags = TextFormatFlags.WordBreak };
        private readonly VStack _inputs = new VStack { Spacing = Theme.S(6), Padding = new Padding(0) };
        private XButton _apply, _off;
        private readonly Dictionary<string, Func<string>> _readers = new Dictionary<string, Func<string>>();
        private readonly Dictionary<string, string> _remembered = new Dictionary<string, string>();   // "game|option|input" -> last value typed
        private readonly HashSet<string> _updateAccepted = new HashSet<string>();                     // games the user chose to mod on another title update

        // ---- tab 2: mod files and licences
        private readonly ListView _list = ListStyle.Create(("Mod", 260), ("Game", 240), ("Status", 170));
        private readonly XTextBox _token = new XTextBox("Your licence token, e.g. ABCDE-FGHJK-LMNPQ-RSTUV", false, true);
        private readonly XCheck _remember = new XCheck("Remember this token", true, "Keeps the token on this PC so your mods unlock by themselves when the program starts. It is tied to this PC, so it is of no use on another one.");
        private readonly TextBlock _detail = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(40), Flags = TextFormatFlags.WordBreak };

        public ModsPage()
        {
            // ------------------------------------------------------------ 0. every mod a licence unlocks
            _catTab = AddSection("All mods", Glyph.Library); Into = _catTab;
            var cat = AddCard("Every mod a licence unlocks", "One licence unlocks all of them. Until then their contents stay sealed - this list only says what each one does.", Glyph.Library);
            cat.Add(_catSummary);
            _cat.Height = Theme.S(300); _cat.FullRowSelect = true; _cat.MultiSelect = false;
            FitColumns(_cat, Theme.S(270), Theme.S(70), Theme.S(190));
            _cat.SelectedIndexChanged += (s, e) => ShowCatalogMod();
            cat.Add(_cat);
            _shopBtn = Btn("Get a licence", Glyph.Globe, OpenShop, BtnKind.Primary, "Opens where X360GameHack licences are sold.");
            Buttons(cat, _shopBtn,
                Btn("Enter my licence...", Glyph.Unlock, () => ShowSection(_filesTab), tip: "My mod files: paste your licence token there and every mod unlocks."),
                Btn("Refresh list", Glyph.Refresh, () => LoadCatalog()));
            var what = AddCard("What it does", "The selected mod's options, as the mod server lists them.", Glyph.Info);
            Theme.DarkScrollbars(_catDetail);
            what.Add(_catDetail);

            // ------------------------------------------------------------ 1. game mods
            Into = AddSection("Game mods", Glyph.Game);
            var pick = AddCard("Game", null, Glyph.Game);
            var row = Field(pick, "Mod for", _gameCombo, _find);
            row.FillChild = null;
            pick.Add(_gameNote);
            _gameCombo.SelectedIndexChanged += (s, e) => { FillGroups(); };
            _find.TextChanged += (s, e) => FillOptions();

            var cols = AddColumns(3, 300);
            var g = AddCard("1.  Group", null, Glyph.List, cols);
            _groups.Height = Theme.S(330); _groups.MultiSelect = false;
            FitColumns(_groups);
            _groups.SelectedIndexChanged += (s, e) => FillOptions();
            g.Add(_groups);

            var o = AddCard("2.  Option", null, Glyph.Check, cols);
            _options.Height = Theme.S(330); _options.MultiSelect = false;
            FitColumns(_options, Theme.S(76));
            _options.SelectedIndexChanged += (s, e) => ShowOption();
            _options.DoubleClick += (s, e) => ApplyOption(true);
            o.Add(_options);

            var d = AddCard("3.  Set and apply", null, Glyph.Play, cols);
            d.Add(_optName);
            d.Add(_optDoes);
            d.Add(_inputs);
            _apply = Btn("Apply", Glyph.Play, () => ApplyOption(true), BtnKind.Primary, "Sends this option to the game that is running on the console (or in Xenia, for options that only write memory).");
            _off = Btn("Turn off", Glyph.Cancel, () => ApplyOption(false), tip: "Puts back what this option changed. Only options with an \"off\" have this.");
            Buttons(d, _apply, _off);
            Note(d, "Values are written over XBDM. An option that runs a game command needs the JRPC2 or XRPC plugin on the console. A mod only applies while its own game is running.", Theme.TextDim);

            // ------------------------------------------------------------ 2. mod files
            _filesTab = AddSection("My mod files", Glyph.Package); Into = _filesTab;
            Note(Into,
                "X360GameHack and all of its console-modding tools are free and open source. Mods are separate files (.x360mod): free ones load by themselves, paid ones unlock with your licence token. " +
                "One token unlocks all of your paid mods. A paid mod's contents are sealed in the file and this program never stores or shares them.", Theme.TextDim);
            var two = AddColumns(2, 420);
            var mods = AddCard("Your mods", "Put .x360mod files in the mods folder (Open mods folder), then press Refresh.", Glyph.Package, two);
            _list.Height = Theme.S(250);
            _list.FullRowSelect = true; _list.MultiSelect = false;
            _list.SelectedIndexChanged += (s, e) => ShowDetail();
            mods.Add(_list);
            mods.Add(_detail);
            Buttons(mods,
                Btn("Lock selected", Glyph.Lock, LockSelected, tip: "Takes the mod out of the tools now. It stays locked until you unlock it or start the program again."),
                Btn("Refresh", Glyph.Refresh, () => { ModManager.Scan(); Fill(); AutoUnlock(true); }),
                Btn("Open mods folder", Glyph.FolderOpen, () => { System.IO.Directory.CreateDirectory(ModManager.Folder); Tools.OpenFolder(ModManager.Folder); }));

            var licence = AddCard("Licence", "Paste the token you got when you bought your mods. One token unlocks all of them.", Glyph.Unlock, two);
            Field(licence, "Token", _token);
            licence.Add(_remember);
            Buttons(licence,
                Btn("Unlock my mods", Glyph.Unlock, UnlockAll, BtnKind.Primary, "Unlocks every paid mod in your mods folder that your token covers."),
                Btn("Forget token", Glyph.Cancel, ForgetToken, tip: "Locks your paid mods and removes the remembered token from this PC, so they no longer unlock by themselves. Your licence is not affected."));
            var machine = new XTextBox(null, false, true) { ReadOnly = true };
            try { machine.Text = HardwareId.Short(); } catch (Exception ex) { machine.Text = "(could not read: " + ex.Message + ")"; }
            Field(licence, "This PC's id", machine, Btn("Copy", Glyph.Copy, () => { try { Clipboard.SetText(machine.Text); Log.Info("PC id copied."); } catch { } }));
            Note(licence, "Unlocking checks your token online. The first time, the token is tied to this PC (by the id above); after that it works only here - ask the seller if you move to a new PC. " +
                          "With \"Remember this token\" ticked, your mods unlock by themselves each time the program starts.");

            // ------------------------------------------------------------ 3. mod maker - only on the PC with the team's signing key
            Into = null;
            if (OwnerKey.Available) AddPageSection("Mod Maker", Glyph.Wrench, new ModMakerPage());

            ModManager.Changed += () => { if (IsHandleCreated) BeginInvoke((Action)(() => { Fill(); FillCatalog(); })); };
            ModManager.Scan();
            Fill();
        }

        // ---------------------------------------------------------------- game mods

        /// <summary>The first column takes the room that is left, so a list never needs a sideways scroll bar.</summary>
        private static void FitColumns(ListView lv, params int[] others)
        {
            Action fit = () =>
            {
                if (lv.Columns.Count == 0) return;
                for (int i = 0; i < others.Length && i + 1 < lv.Columns.Count; i++) lv.Columns[i + 1].Width = others[i];
                int rest = lv.ClientSize.Width - others.Sum() - 2;
                if (rest > 40) lv.Columns[0].Width = rest;
            };
            lv.Resize += (s, e) => fit();
            lv.HandleCreated += (s, e) => fit();
        }

        private void FillGameMods()
        {
            string keep = _gameCombo.SelectedItem?.ToString();
            _gameCombo.Items.Clear();
            foreach (var g in GameMods.All) _gameCombo.Items.Add(g.Game);
            if (_gameCombo.Items.Count > 0) _gameCombo.SelectedIndex = Math.Max(0, _gameCombo.Items.IndexOf(keep ?? ""));
            _gameNote.Text = _gameCombo.Items.Count == 0
                ? "No game mod is loaded yet. Free mods in the mods folder load by themselves; unlock paid ones on the \"My mod files\" tab."
                : "";
            FillGroups();
        }

        private GameModKind SelectedGameMod() => _gameCombo.SelectedIndex < 0 ? null : GameMods.All.FirstOrDefault(g => g.Game == (string)_gameCombo.SelectedItem);

        private const string AllGroups = "All options";

        private void FillGroups()
        {
            var kind = SelectedGameMod();
            _groups.BeginUpdate();
            _groups.Items.Clear();
            if (kind != null)
            {
                _groups.Items.Add(new ListViewItem(AllGroups + "  (" + kind.Options.Count + ")") { Tag = null });
                foreach (string name in kind.Groups)
                    _groups.Items.Add(new ListViewItem((name.Length == 0 ? "General" : name) + "  (" + kind.Options.Count(x => (x.Group ?? "") == name) + ")") { Tag = name });
                if (!string.IsNullOrEmpty(kind.Notes)) _gameNote.Text = kind.Notes;
            }
            _groups.EndUpdate();
            if (_groups.Items.Count > 1) _groups.Items[1].Selected = true; else if (_groups.Items.Count == 1) _groups.Items[0].Selected = true;
            FillOptions();
        }

        private void FillOptions()
        {
            var kind = SelectedGameMod();
            string find = _find.Text.Trim();
            string group = _groups.SelectedItems.Count > 0 ? (string)_groups.SelectedItems[0].Tag : null;
            bool everything = find.Length > 0 || _groups.SelectedItems.Count == 0 || _groups.SelectedItems[0].Tag == null;
            _options.BeginUpdate();
            _options.Items.Clear();
            if (kind != null)
                foreach (var opt in kind.Options)
                {
                    if (!everything && (opt.Group ?? "") != group) continue;
                    if (find.Length > 0 && opt.Name.IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0 && (opt.Group ?? "").IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var li = new ListViewItem(opt.Name) { Tag = opt };
                    li.SubItems.Add(opt.Toggle ? "on / off" : opt.Inputs.Count > 0 ? "set" : "apply");
                    _options.Items.Add(li);
                }
            _options.EndUpdate();
            if (_options.Items.Count > 0) _options.Items[0].Selected = true;
            ShowOption();
        }

        private GameModOption SelectedOption() => _options.SelectedItems.Count > 0 ? (GameModOption)_options.SelectedItems[0].Tag : null;

        private string Key(GameModOption opt, string input) => (_gameCombo.SelectedItem ?? "") + "|" + opt.Name + "|" + (opt.Group ?? "") + "|" + input;

        /// <summary>Shows the selected option: what it does and a box for each value it needs.</summary>
        private void ShowOption()
        {
            var opt = SelectedOption();
            foreach (Control c in _inputs.Controls.Cast<Control>().ToList()) { _inputs.Controls.Remove(c); c.Dispose(); }
            _readers.Clear();
            if (opt == null)
            {
                _optName.Text = SelectedGameMod() == null ? "" : "Pick an option.";
                _optDoes.Text = "";
                _apply.Enabled = _off.Enabled = false;
                _inputs.Parent?.PerformLayout();
                return;
            }
            _optName.Text = opt.Name;
            _optDoes.Text = opt.Does;
            foreach (var i in opt.Inputs)
            {
                var input = i;
                string last = _remembered.TryGetValue(Key(opt, input.Id), out string kept) ? kept : input.Default;
                switch (input.Type)
                {
                    case "bool":
                    {
                        var box = new XCheck(input.Label, last == "1");
                        _inputs.Controls.Add(box);
                        _readers[input.Id] = () => box.Checked ? "1" : "0";
                        break;
                    }
                    case "choice":
                    {
                        var combo = new XCombo { Width = Theme.S(230) };
                        foreach (var ch in input.Choices) combo.Items.Add(ch.Label);
                        int at; if (!int.TryParse(last, out at)) at = 0;
                        combo.SelectedIndex = Math.Max(0, Math.Min(combo.Items.Count - 1, at));
                        Field(_inputs, input.Label, combo).FillChild = null;
                        _readers[input.Id] = () => combo.SelectedIndex.ToString();
                        break;
                    }
                    case "file":
                    {
                        var path = new XTextBox("Choose the file") { Text = last ?? "" };
                        Field(_inputs, input.Label, path, Btn("", Glyph.FolderOpen, () => { var f = PickFile(input.Label); if (f != null) path.Text = f; }));
                        _readers[input.Id] = () => path.Text.Trim();
                        break;
                    }
                    default:
                    {
                        bool number = input.Type == "number";
                        var box = new XTextBox(number ? ModExpr.Text(input.Min) + " to " + ModExpr.Text(input.Max) : null, false, number) { Text = last ?? "" };
                        Field(_inputs, input.Label, box);
                        _readers[input.Id] = () => box.Text.Trim();
                        break;
                    }
                }
            }
            _apply.Enabled = true;
            _apply.Text = opt.Toggle ? "Turn on" : "Apply";
            _off.Enabled = opt.Toggle;
            _inputs.Parent?.PerformLayout();
        }

        private void ApplyOption(bool enable)
        {
            var kind = SelectedGameMod();
            var opt = SelectedOption();
            if (kind == null || opt == null) { Dlg.Info("Game mod", "Pick a mod and an option first."); return; }
            if (!enable && !opt.Toggle) return;
            var inputs = new Dictionary<string, string>();
            foreach (var kv in _readers) { inputs[kv.Key] = kv.Value(); _remembered[Key(opt, kv.Key)] = inputs[kv.Key]; }
            try { GameModRunner.Variables(opt, inputs); }                      // say what is wrong with a value before anything is sent
            catch (ArgumentException ex) { Dlg.Warn(opt.Name, ex.Message); return; }

            bool xenia = MemoryTargets.UseXenia;
            if (!xenia && !ConsoleService.IsConnected && XeniaProcess.Running().Count > 0)
            {
                // no console, but a game is open in Xenia: mod that one (the same as choosing Xenia on Peek Poker, which follows)
                MemoryTargets.UseXenia = xenia = true;
                Log.Info("No console is connected - using the game running in Xenia on this PC.");
            }
            if (!xenia && !ConsoleService.IsConnected && !MemoryTargets.IsReady) { Dlg.Info("Game mod", "Connect to the console (Neighborhood), or have a game open in Xenia, first."); return; }
            Run(null, (enable ? "Applying " : "Turning off ") + opt.Name, async () =>
            {
                ModRunResult r;
                if (!xenia && ConsoleService.IsConnected)
                {
                    // addresses only mean something inside the game - and the title update - they were made for
                    bool older = false;
                    string[] check = await ConsoleService.RunAsync(c =>
                    {
                        string wrong = GameModRunner.WrongGame(kind, c);
                        return new[] { wrong, wrong != null ? null : GameModRunner.UpdateProblem(kind, c.GetTitleVersion(), out older) };
                    });
                    if (check[0] != null) throw new InvalidOperationException(check[0]);
                    if (check[1] != null && !_updateAccepted.Contains(kind.Game))
                    {
                        if (!Dlg.Confirm(older ? "Older title update on the console" : "Different title update on the console", check[1] + "\n\nApply anyway?", "Apply anyway", older))
                        { Log.Warn(opt.Name + " was not applied: " + check[1]); return; }
                        _updateAccepted.Add(kind.Game);              // asked once per game while the program runs
                    }
                    r = await ConsoleService.RunAsync(c => GameModRunner.Apply(opt, c, c, enable, inputs));
                }
                else r = await MemoryTargets.RunAsync(t => GameModRunner.Apply(opt, t, null, enable, inputs));
                if (r.Stopped != null) { Log.Warn(opt.Name + ": " + r.Stopped); return; }
                Log.Success((enable ? (opt.Toggle ? "Turned on " : "Applied ") : "Turned off ") + opt.Name + " - " + r.Writes + " write(s)" + (r.Calls > 0 ? ", " + r.Calls + " call(s)" : "") +
                            ", via " + (r.Routes.Count > 0 ? string.Join(" + ", r.Routes) : "nothing") + ".");
            });
        }

        // ---------------------------------------------------------------- mod files

        public override void OnActivated() { ModManager.Scan(); Fill(); AutoUnlock(false); CatalogIfShown(); }

        protected override void OnSectionChanged() => CatalogIfShown();

        // ---------------------------------------------------------------- the catalog

        /// <summary>Reads the catalog the first time its tab is on screen.</summary>
        private void CatalogIfShown()
        {
            if (_catLoaded || !Visible || Tabs?.SelectedPanel != _catTab) return;
            _catLoaded = true;
            LoadCatalog();
        }

        private async void LoadCatalog()
        {
            if (_catBusy) return;
            _catBusy = true;
            _catSummary.Text = "Reading the list of mods...";
            _catSummary.Parent?.PerformLayout();
            try { _catalog = await Task.Run(() => ModCatalog.Load()); }
            catch (Exception ex) { _catalog = new ModCatalogList { Error = ex.Message }; }
            finally { _catBusy = false; }
            FillCatalog();
        }

        private string StatusOf(CatalogMod c)
        {
            if (c.Free) return "Free";
            var m = ModManager.Mods.FirstOrDefault(x => x.Info != null && x.Info.Id == c.ModId && x.Pack != null && x.Pack.OwnerSigned);
            if (m == null) return "Comes with a licence";
            return m.State == ModState.Unlocked ? "Unlocked" : "Locked - enter your licence";
        }

        private void FillCatalog()
        {
            if (_catalog == null) return;
            string keep = (_cat.SelectedItems.Count > 0 ? _cat.SelectedItems[0].Tag as CatalogMod : null)?.ModId;
            _cat.BeginUpdate();
            _cat.Items.Clear();
            foreach (var c in _catalog.Mods.OrderBy(x => x.Game, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var li = new ListViewItem(c.Game.Length > 0 ? c.Game : c.TitleId) { Tag = c };
                li.SubItems.Add(c.Name);
                li.SubItems.Add(c.Options.ToString());
                string status = StatusOf(c);
                li.SubItems.Add(status);
                li.ForeColor = status == "Unlocked" || status == "Free" ? Theme.Success : Theme.Text;
                _cat.Items.Add(li);
                if (keep != null && c.ModId == keep) li.Selected = true;
            }
            _cat.EndUpdate();
            if (_cat.SelectedItems.Count == 0 && _cat.Items.Count > 0) _cat.Items[0].Selected = true;

            int games = _catalog.Mods.Select(m => m.TitleId.Length > 0 ? m.TitleId : m.Game).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int options = _catalog.Mods.Sum(m => m.Options);
            bool licence = ModManager.LicenceToken.Length > 0;
            if (!_catalog.Ok)
                _catSummary.Text = "The list of mods could not be read: " + _catalog.Error;
            else
                _catSummary.Text = _catalog.Mods.Count + " mod(s) for " + games + " game(s) - " + options + " option(s) in all." +
                                   (licence ? "  Your licence is on this PC: they unlock by themselves." : "  Enter your licence on My mod files and they all unlock.") +
                                   (_catalog.FromCache ? "\nThe mod server could not be reached (" + _catalog.Error + ") - this is the list saved " + _catalog.Fetched.ToString("yyyy-MM-dd HH:mm") + "." : "");
            _shopBtn.Visible = _catalog.Shop.Length > 0;
            _catSummary.Parent?.PerformLayout();
            ShowCatalogMod();
        }

        private void ShowCatalogMod()
        {
            var c = _cat.SelectedItems.Count > 0 ? _cat.SelectedItems[0].Tag as CatalogMod : null;
            if (c == null) { _catDetail.Text = _catalog == null || _catalog.Mods.Count == 0 ? "" : "Select a mod to see what it does."; return; }
            var sb = new System.Text.StringBuilder();
            sb.Append(c.Name).Append("\r\n");
            sb.Append(c.Game).Append(c.TitleId.Length > 0 ? "  (" + c.TitleId + ")" : "").Append("   •   ").Append(c.Options).Append(" option(s)   •   ").Append(StatusOf(c)).Append("\r\n");
            if (c.Requires.Length > 0) sb.Append("Needs: ").Append(c.Requires).Append("\r\n");
            if (c.Description.Length > 0) sb.Append("\r\n").Append(c.Description).Append("\r\n");
            foreach (var g in c.Groups)
            {
                sb.Append("\r\n").Append(g.Name).Append("\r\n");
                foreach (var o in g.Options) sb.Append("    •  ").Append(o.Name).Append(o.Does.Length > 0 ? "  -  " + o.Does : "").Append("\r\n");
            }
            _catDetail.Text = sb.ToString();
            _catDetail.Select(0, 0);
        }

        private void OpenShop()
        {
            string shop = _catalog?.Shop ?? "";
            if (Uri.TryCreate(shop, UriKind.Absolute, out Uri u) && u.Scheme == Uri.UriSchemeHttps) Tools.OpenUrl(u.ToString());
        }

        /// <summary>Mods whose token was remembered unlock by themselves - nothing to press.</summary>
        private void AutoUnlock(bool retry)
        {
            Task.Run(() => ModManager.UnlockSaved(retry)).ContinueWith(t =>
            {
                if (t.IsFaulted || t.Result.Count == 0 || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    int ok = t.Result.Count(r => r.Value == null);
                    if (ok > 0) Log.Success("Unlocked " + ok + " mod(s) with your saved token.");
                    foreach (var r in t.Result.Where(r => r.Value != null))
                        Log.Warn("Could not unlock " + r.Key.Info.Name + " with the saved token: " + r.Value);
                    Fill();
                }));
            });
        }

        private void Fill()
        {
            string keepId = Selected()?.Info?.Id;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var m in ModManager.Mods)
            {
                var li = new ListViewItem(m.Info?.Name ?? m.FileName) { Tag = m };
                li.SubItems.Add(m.Info?.Game ?? "");
                li.SubItems.Add(m.State == ModState.Unlocked ? (m.Info != null && m.Info.Open ? "Free - loaded" : "Unlocked") : m.State == ModState.Error ? "Problem" : m.Info != null && m.Info.ServerOnly ? "Locked (server mod)" : "Locked");
                li.ForeColor = m.State == ModState.Unlocked ? Theme.Success : m.State == ModState.Error ? Theme.Danger : Theme.Text;
                if (m.Info?.Id != null && m.Info.Id == keepId) li.Selected = true;
                _list.Items.Add(li);
            }
            _list.EndUpdate();
            if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;
            ShowDetail();
            FillGameMods();
        }

        private LoadedMod Selected() => _list.SelectedItems.Count > 0 ? (LoadedMod)_list.SelectedItems[0].Tag : null;

        private void ShowDetail()
        {
            var m = Selected();
            if (_token.Text.Length == 0) _token.Text = ModManager.LicenceToken;
            if (m == null) { _detail.Text = ModManager.Mods.Count == 0 ? "No mods found. Press Open mods folder and put your .x360mod files there, then Refresh." : "Select a mod."; return; }
            if (m.Info == null) { _detail.Text = m.Message; return; }
            _detail.Text = m.Info.Description + (m.Info.Requires.Length > 0 ? "   Needs: " + m.Info.Requires : "") +
                           (m.State == ModState.Unlocked ? "   Ready: " + (m.Info.Kind == "game" ? "use it on the Game mods tab." : "open a save for this game on the Saves page.") : m.State == ModState.Error ? "   " + m.Message : "");
        }

        /// <summary>One token, one press: every locked paid mod it covers unlocks.</summary>
        private void UnlockAll()
        {
            string token = _token.Text.Trim(); bool remember = _remember.Checked;
            if (token.Length == 0) { Dlg.Info("Unlock", "Paste your licence token first."); return; }
            // the mod files come from the licence server (only to a licensed PC), so this runs even with none here yet
            Run(null, "Getting and unlocking your mods", async () =>
            {
                var report = await Task.Run(() => ModManager.UnlockWith(token, remember));
                var sync = report.Sync;
                if (sync != null)
                {
                    if (sync.Downloaded.Count > 0) Log.Success("Downloaded " + sync.Downloaded.Count + " mod file(s) from the licence server (each checked by SHA-256): " + string.Join(", ", sync.Downloaded) + ".");
                    else if (sync.Error == null) Log.Info("Your mod files are up to date (" + sync.UpToDate.Count + " on the licence server for this licence).");
                    foreach (var p in sync.Problems) Log.Warn("Mod file not downloaded: " + p);
                    if (sync.Error != null && report.Error == null) Log.Warn("The licence server's mod files could not be checked: " + sync.Error);
                }
                if (report.Error != null) { Log.Error("Could not unlock: " + report.Error); Dlg.Error("Could not unlock", report.Error); }
                else
                {
                    if (report.Unlocked.Count > 0) Log.Success("Unlocked " + report.Unlocked.Count + " mod(s)" + (remember ? ". They will unlock by themselves from now on." : "."));
                    foreach (var p in report.Problems) Log.Warn("Not unlocked: " + (p.Key.Info?.Name ?? p.Key.FileName) + " - " + p.Value);
                    bool anyPaid = ModManager.Mods.Any(m => m.Info != null && !m.Info.Open);
                    if (report.Unlocked.Count == 0)
                    {
                        if (!anyPaid) Dlg.Info("Nothing to unlock", "The licence server has no mod files for this token yet.");
                        else if (ModManager.Mods.Where(m => m.Info != null && !m.Info.Open).All(m => m.State == ModState.Unlocked)) Dlg.Info("Unlock", "Your paid mods are already unlocked.");
                        else Dlg.Warn("Nothing unlocked", "Your token does not cover these mods." + (report.Problems.Count > 0 ? "\n\n" + report.Problems.First().Value : ""));
                    }
                }
                Fill();
            });
        }

        private void LockSelected()
        {
            var m = Selected();
            if (m == null) return;
            if (m.Info != null && m.Info.Open) { Dlg.Info("Lock", "That is a free mod - there is nothing to lock."); return; }
            ModManager.Lock(m);
            bool remembered = m.Info != null && (ModManager.SavedToken(m.Info.Id).Length > 0 || ModManager.LicenceToken.Length > 0);
            Log.Info("Locked " + (m.Info?.Name ?? m.FileName) + ". It stays locked until you unlock it" + (remembered ? " or start the program again (your token is remembered - use Forget token to stop that)." : "."));
            Fill();
        }

        private void ForgetToken()
        {
            int n = ModManager.ForgetAllTokens();
            _token.Text = "";
            Log.Info(n > 0 ? "Locked your paid mods and forgot the token on this PC. They stay locked until the token is typed in again."
                           : "Locked your paid mods. No token was remembered on this PC.");
            Fill();
        }
    }
}
