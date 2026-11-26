using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public sealed class SettingsPage : PageBase
    {
        public override string Title => "Settings";
        public override string Subtitle => "Saved automatically to %AppData%\\X360GameHack\\settings.json.";
        public override string PageGlyph => Glyph.Settings;

        private readonly WrapPanel _tiles = new WrapPanel { HGap = Theme.S(12), VGap = Theme.S(12) };
        private readonly Card _editor;
        private readonly XTextBox _name = new XTextBox("Theme name");
        private readonly Segmented _base = new Segmented("Dark", "Light");
        private readonly ThemePreview _preview = new ThemePreview();
        private readonly TextBlock _contrast = new TextBlock("", Theme.Small, Theme.Warning);
        private readonly WrapPanel _slots = new WrapPanel { HGap = Theme.S(10), VGap = Theme.S(4) };
        private readonly TextBlock _tools = new TextBlock("", Theme.Mono, Theme.TextDim);
        private readonly TextBlock _kvState = new TextBlock("", Theme.Body, Theme.TextDim);
        private readonly XButton _edit, _delete;
        private ThemePalette _selected, _editing, _editingOriginal;
        private bool _loadingEditor;

        public SettingsPage()
        {
            var s = AppSettings.Current;

            // ================================================================ Appearance
            var look = AddSection("Appearance", Glyph.Image);
            var themes = AddCard("Themes", "Pick a theme and press Apply (or double-click it). Make your own with New theme - your themes are saved in your settings and can be exported to share.", Glyph.Image, look);
            themes.Add(_tiles);
            var themeButtons = Buttons(themes,
                Btn("Apply", Glyph.Check, () => ApplySelected(), BtnKind.Primary),
                Btn("New theme", Glyph.Add, NewTheme),
                Btn("Duplicate", Glyph.Copy, Duplicate));
            _edit = themeButtons.Add(Btn("Edit", Glyph.Edit, EditSelected));
            _delete = themeButtons.Add(Btn("Delete", Glyph.Delete, DeleteSelected, BtnKind.Danger));
            themeButtons.Add(Btn("Import...", Glyph.OpenFile, ImportTheme));
            themeButtons.Add(Btn("Export...", Glyph.Save, ExportTheme));

            _editor = AddCard("Theme editor", "Pick a base, an accent and a background - every other shade is generated for you. Fine-tune any single color below.", Glyph.Edit, look);
            _editor.Visible = false;
            var edCols = AddColumns(2, 400, _editor);
            var left = new VStack { Spacing = Theme.S(10), Padding = new Padding(0) };
            Field(left, "Name", _name);
            Field(left, "Base", _base).FillChild = null;
            var gen = Btn("Generate shades from accent + background", Glyph.Sparkle, GenerateShades);
            left.Add(new WrapPanel()).Add(gen);
            left.Add(_contrast);
            edCols.Add(left);
            edCols.Add(_preview);
            _editor.Add(new TextBlock("All colors", Theme.SmallBold, Theme.TextFaint));
            _editor.Add(_slots);
            Buttons(_editor,
                Btn("Save & apply", Glyph.Check, () => SaveEditor(true), BtnKind.Primary),
                Btn("Save", Glyph.Save, () => SaveEditor(false)),
                Btn("Cancel", Glyph.Cancel, () => { _editor.Visible = false; _editing = null; }, BtnKind.Ghost));
            _name.TextChanged += (o, e) => { if (_editing != null && !_loadingEditor) _editing.Name = _name.Text; };
            _base.SelectedIndexChanged += (o, e) =>
            {
                if (_editing == null || _loadingEditor) return;
                _editing.Light = _base.SelectedIndex == 1;
                GenerateShades();
            };
            BuildTiles();

            // ================================================================ General
            var general = AddSection("General", Glyph.Settings);
            var cols = AddColumns(2, 400, general);
            var startup = AddCard("Startup", null, Glyph.Play, cols);
            Toggle(startup, "Check for updates at startup", s.CheckForUpdates, v => s.CheckForUpdates = v);
            Toggle(startup, "Ask me to install new versions", s.AutoUpdate, v => s.AutoUpdate = v,
                   "On: when a new version is released, X360GameHack asks first, shows the download's SHA-256 and every file it adds or replaces, and installs only when you say so. Off: only the title bar says a new version is out.");
            Toggle(startup, "Show the \"faster ways to copy games\" tip again", !s.HddCopyTipShown, v => s.HddCopyTipShown = !v);
            Toggle(startup, "Open on the page I used last", s.StartOnLastPage, v => s.StartOnLastPage = v);
            Toggle(startup, "Show the activity log at startup", s.LogDrawerOpen, v => s.LogDrawerOpen = v);
            XButton checkNow = null;
            checkNow = Btn("Check for updates now", Glyph.Refresh, async () =>
            {
                checkNow.Enabled = false;
                try { await MainForm.Instance.CheckForUpdatesAsync(true); }
                finally { if (!checkNow.IsDisposed) checkNow.Enabled = true; }
            }, tip: "Asks the update server now. A version you skipped is offered again here.");
            Buttons(startup, checkNow);

            var working = AddCard("Working with files", null, Glyph.Document, cols);
            Toggle(working, "Back up files (.bak) before patching", s.BackupBeforePatch, v => s.BackupBeforePatch = v);
            Toggle(working, "Keep a log file (logs.txt, rotated at 5 MB)", s.KeepLogFile, v => s.KeepLogFile = v);
            Toggle(working, "Run tools in a visible console window instead of the log", s.ShowToolWindows, v => s.ShowToolWindows = v);
            Note(working, "By default xextool / X360PkgTool output streams into the activity log. The console-window mode only exists for tools that ask questions.");

            // ================================================================ Folders & tools
            var folders = AddSection("Folders & tools", Glyph.Folder);
            var paths = AddCard("Folders and programs", null, Glyph.Folder, folders);
            PathRow(paths, "Saves folder", () => s.SavesFolder, v => s.SavesFolder = v, true, s.SavesFolderResolved);
            PathRow(paths, "ISO extract folder", () => s.ExtractPath, v => s.ExtractPath = v, true, "Next to each ISO");
            PathRow(paths, "Xenia (xenia_canary.exe)", () => Tools.Xenia, v => s.XeniaPath = v, false, "Not set");      // preset to the bundled Xenia Canary
            PathRow(paths, "xemu (xemu.exe)", () => s.XemuPath, v => s.XemuPath = v, false, "Not set");

            var cmds = AddCard("Default tool commands", "Used by the custom-command boxes. Keep -r mrbdiyvklc (not -r a): \"a\" also zeroes the Media ID, which breaks title updates.", Glyph.Code, folders);
            TextSetting(cmds, "xextool (XEX Tool)", () => s.XexCustomCommand, v => s.XexCustomCommand = v);
            TextSetting(cmds, "xextool (bulk ISO)", () => s.BulkCustomCommand, v => s.BulkCustomCommand = v);
            TextSetting(cmds, "X360PkgTool", () => s.PkgCustomCommand, v => s.PkgCustomCommand = v);

            var tools = AddCard("Bundled tools", "Each tool's SHA-256 is checked before every run; a modified tool is never started.", Glyph.Shield, folders);
            tools.Add(_tools);
            Buttons(tools,
                Btn("Check now", Glyph.Refresh, CheckTools),
                Btn("Open tools folder", Glyph.FolderOpen, () => Tools.OpenFolder(Tools.AppDir)));

            // ================================================================ Network
            var net = AddSection("Network", Glyph.Network);
            var ncols = AddColumns(2, 400, net);
            var xbdm = AddCard("Console (XBDM)", "Used by Neighborhood, Console and Peek Poker.", Glyph.Console, ncols);
            TextSetting(xbdm, "Console IP", () => s.XbdmIp, v => s.XbdmIp = v.Trim());
            Toggle(xbdm, "Connect to this console at startup", s.XbdmAutoConnect, v => s.XbdmAutoConnect = v);
            var other = AddCard("Other", null, Glyph.Globe, ncols);
            TextSetting(other, "XeLL IP", () => s.XellIp, v => s.XellIp = v.Trim());
            Note(other, s.FtpProfiles.Count + " FTP profile(s) are saved (manage them on the FTP page). Their passwords are encrypted for your Windows account only.");
            Buttons(other, Btn("Forget saved FTP passwords", Glyph.Clear, ForgetFtpPasswords, BtnKind.Danger));

            // ================================================================ Privacy & data
            var data = AddSection("Privacy & data", Glyph.Lock);
            var dcols = AddColumns(2, 400, data);
            var sec = AddCard("Security", null, Glyph.Shield, dcols);
            Note(sec,
                "• X360GameHack never runs as administrator (the drive reader asks separately and only reads).\n" +
                "• A keyvault's key is kept in memory only - never copied, saved or sent. Only the file's location is remembered so Rehash & resign can sign with it.\n" +
                "• FTP passwords are encrypted with Windows DPAPI for your account.\n" +
                "• Nothing is uploaded anywhere; the only online calls are the update check and title-update lookups.");
            sec.Add(_kvState);
            Buttons(sec, Btn("Forget keyvault", Glyph.Clear, KeyVaultSession.Forget));
            KeyVaultSession.Watch(this, ShowKeyVault);
            ShowKeyVault();

            var files = AddCard("Your data", null, Glyph.Folder, dcols);
            Buttons(files,
                Btn("Open settings folder", Glyph.FolderOpen, () => Tools.OpenFolder(Path.GetDirectoryName(Log.LogFilePath))),
                Btn("Open log file", Glyph.Document, () => { if (File.Exists(Log.LogFilePath)) Tools.OpenUrl(Log.LogFilePath); else Log.Info("No log file yet."); }),
                Btn("Clear recent files", Glyph.Clear, () => { s.RecentFiles.Clear(); AppSettings.Save(); Log.Info("Recent files cleared."); }));
            Buttons(files,
                Btn("Export settings...", Glyph.Save, ExportSettings),
                Btn("Import settings...", Glyph.OpenFile, ImportSettings),
                Btn("Reset all settings...", Glyph.Refresh, ResetSettings, BtnKind.Danger));

            var util = AddCard("Utilities", null, Glyph.Wrench, data);
            Buttons(util, Btn("Compute SHA3-256 of a file...", Glyph.Shield, () =>
            {
                var f = PickFile("Choose a file to hash");
                if (f == null) return;
                Run(null, "Hashing " + Path.GetFileName(f), async () =>
                {
                    string h = await System.Threading.Tasks.Task.Run(() => Sha3_256.ComputeSha3_256(f));
                    try { Clipboard.SetText(h); } catch { }
                    Log.Success("SHA3-256 " + Path.GetFileName(f) + ": " + h + " (copied)");
                });
            }));
            AddPageSection("About", Glyph.Info, new AboutPage());
        }

        public override void OnActivated()
        {
            if (string.IsNullOrEmpty(_tools.Text)) CheckTools();
        }

        // ------------------------------------------------------------------ helpers

        private static void Toggle(Control card, string text, bool value, Action<bool> set, string tooltip = null)
        {
            var c = new XCheck(text, value, tooltip);
            c.CheckedChanged += (o, e) => { set(c.Checked); AppSettings.Save(); };
            card.Controls.Add(c);
        }

        private static void TextSetting(Control card, string label, Func<string> get, Action<string> set)
        {
            var box = new XTextBox(null, false, true) { Text = get() ?? "" };
            box.Box.Leave += (o, e) => { if (box.Text != (get() ?? "")) { set(box.Text); AppSettings.Save(); Log.Info(label + " saved."); } };
            box.Box.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; set(box.Text); AppSettings.Save(); Log.Info(label + " saved."); } };
            Field(card, label, box);
        }

        private void PathRow(Control card, string label, Func<string> get, Action<string> set, bool folder, string cue)
        {
            var box = new XTextBox(cue) { Text = get() ?? "", ReadOnly = true };
            var browse = Btn("Browse...", Glyph.FolderOpen, () =>
            {
                string v = folder ? PickFolder("Choose the " + label.ToLowerInvariant(), get()) : PickFile("Choose " + label, "Programs (*.exe)|*.exe");
                if (v == null) return;
                set(v); box.Text = v; AppSettings.Save(); Log.Info(label + ": " + v);
            });
            var clear = Btn("", Glyph.Clear, () => { set(""); box.Text = ""; AppSettings.Save(); });
            Tip.Set(clear, "Use the default");
            Field(card, label, box, browse, clear);
        }

        private void CheckTools()
        {
            Run(null, "Checking bundled tools", async () =>
            {
                var r = await System.Threading.Tasks.Task.Run(() => ToolIntegrity.CheckAll());
                _tools.Text = string.Join("\n", r.Select(t => (t.State == "Verified" ? "✔ " : "✖ ") + t.Name.PadRight(34) + t.State));
                _tools.ForeColor = r.All(t => t.State == "Verified") ? Theme.TextDim : Theme.Warning;
                if (r.Any(t => t.State == "MODIFIED")) Log.Error("A bundled tool was modified - it will not be started. Reinstall X360GameHack.");
            });
        }

        private void ShowKeyVault()
        {
            var kv = KeyVaultSession.Current;
            string remembered = KeyVaultSession.RememberedName;
            _kvState.ForeColor = kv != null || remembered.Length > 0 ? Theme.Text : Theme.TextDim;
            _kvState.Text = kv != null ? "Keyvault in memory: " + kv.Summary + (remembered.Length > 0 ? "  (location remembered)" : "")
                          : remembered.Length > 0 ? "Keyvault remembered: " + remembered + " (loaded when needed)"
                          : "No keyvault - Rehash & resign signs for RGH / JTAG consoles.";
        }

        private void ForgetFtpPasswords()
        {
            if (!Dlg.Confirm("FTP passwords", "Remove the saved password from every FTP profile? You will be asked again when you connect.", "Forget", true)) return;
            foreach (var p in AppSettings.Current.FtpProfiles) p.Password = "";
            AppSettings.Save();
            Log.Success("Saved FTP passwords removed.");
        }

        private void ExportSettings()
        {
            string f = PickSave("Export settings", "X360GameHack-settings.json", "Settings (*.json)|*.json");
            if (f == null) return;
            try { File.WriteAllText(f, AppSettings.Export()); Log.Success("Settings exported (without passwords) to " + f); }
            catch (Exception ex) { Dlg.Error("Export", ex.Message); }
        }

        private void ImportSettings()
        {
            string f = PickFile("Import settings", "Settings (*.json)|*.json");
            if (f == null) return;
            if (!Dlg.Confirm("Import settings", "Replace your settings with the ones in " + Path.GetFileName(f) + "? Saved passwords are not imported.", "Import")) return;
            try
            {
                if (new FileInfo(f).Length > 1024 * 1024) throw new FormatException("Not a settings file (too large).");
                AppSettings.Import(File.ReadAllText(f));
                Log.Success("Settings imported.");
                Shell.Current.ApplyTheme(ThemePalette.Active());
            }
            catch (Exception ex) { Dlg.Error("Import", ex.Message); }
        }

        private void ResetSettings()
        {
            if (!Dlg.Confirm("Reset settings", "Reset every setting to its default (theme, folders, tool commands, toggles)? FTP profiles and recent files are kept.", "Reset", true)) return;
            AppSettings.ResetToDefaults();
            Log.Success("Settings reset to defaults.");
            Shell.Current.ApplyTheme(ThemePalette.Active());
        }

        // ------------------------------------------------------------------ themes

        private void BuildTiles()
        {
            _tiles.SuspendLayout();
            foreach (Control c in _tiles.Controls.Cast<Control>().ToList()) { _tiles.Controls.Remove(c); c.Dispose(); }
            string active = AppSettings.Current.ThemeName;
            foreach (var p in ThemePalette.All())
            {
                var t = new ThemeTile(p) { IsActive = string.Equals(p.Name, active, StringComparison.OrdinalIgnoreCase) };
                t.Picked += (o, e) => Select(p);
                t.Activated += (o, e) => { Select(p); ApplySelected(); };
                _tiles.Add(t);
            }
            _tiles.ResumeLayout();
            _tiles.PerformLayout();
            Select(_selected != null ? ThemePalette.Find(_selected.Name) ?? ThemePalette.Active() : ThemePalette.Active());
        }

        private void Select(ThemePalette p)
        {
            _selected = p;
            foreach (ThemeTile t in _tiles.Controls) t.Selected = ReferenceEquals(t.Palette, p);
            _edit.Enabled = _delete.Enabled = p != null && !p.BuiltIn;
        }

        private void ApplySelected()
        {
            if (_selected == null) return;
            AppSettings.Current.ThemeName = _selected.Name;
            AppSettings.Save();
            Shell.Current.ApplyTheme(_selected);
        }

        private void NewTheme()
        {
            var p = ThemePalette.Generate(AppSettings.UniqueThemeName("My theme", AppSettings.Current.CustomThemes), Theme.IsLight, Theme.Accent, Theme.Window);
            OpenEditor(p, null);
        }

        private void Duplicate()
        {
            if (_selected == null) return;
            var p = _selected.Clone();
            p.Name = AppSettings.UniqueThemeName(_selected.Name + " copy", AppSettings.Current.CustomThemes);
            OpenEditor(p, null);
        }

        private void EditSelected()
        {
            if (_selected == null) return;
            if (_selected.BuiltIn) { Duplicate(); return; }
            OpenEditor(_selected.Clone(), _selected);
        }

        private void OpenEditor(ThemePalette p, ThemePalette original)
        {
            _editing = p;
            _editingOriginal = original;
            _loadingEditor = true;
            _name.Text = p.Name;
            _base.SelectedIndex = p.Light ? 1 : 0;
            _loadingEditor = false;
            _editor.Title = original == null ? "New theme" : "Edit theme - " + original.Name;
            BuildSlots();
            _editor.Visible = true;
            RefreshPreview();
            _editor.Focus();
        }

        private void BuildSlots()
        {
            _slots.SuspendLayout();
            foreach (Control c in _slots.Controls.Cast<Control>().ToList()) { _slots.Controls.Remove(c); c.Dispose(); }
            foreach (var (key, label) in ThemePalette.Slots)
            {
                var slot = new ColorSlot(key, label, _editing.Get(key));
                slot.ColorChanged += (o, e) => { _editing.Set(slot.Key, slot.Value); RefreshPreview(); };
                _slots.Add(slot);
            }
            _slots.ResumeLayout();
            _slots.PerformLayout();
        }

        private void GenerateShades()
        {
            if (_editing == null) return;
            var accent = _editing.Get("Accent");
            var bg = _editing.Get("Window");
            // Switching dark <-> light needs a background on the other side of the scale.
            bool bgLight = ThemePalette.Luminance(bg) > 0.4;
            if (_editing.Light != bgLight) bg = _editing.Light ? System.Drawing.Color.FromArgb(242, 244, 247) : System.Drawing.Color.FromArgb(14, 16, 20);
            var g = ThemePalette.Generate(_editing.Name, _editing.Light, accent, bg);
            foreach (var (key, _) in ThemePalette.Slots) _editing.Set(key, g.Get(key));
            foreach (ColorSlot s in _slots.Controls) s.Value = _editing.Get(s.Key);
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (_editing == null) return;
            _preview.ShowPalette(_editing);
            var warn = _editing.ContrastWarnings();
            _contrast.Text = warn.Count == 0 ? "" : "Readability: " + string.Join("; ", warn) + ".";
        }

        private void SaveEditor(bool apply)
        {
            if (_editing == null) return;
            var list = AppSettings.Current.CustomThemes;
            var others = list.Where(t => !ReferenceEquals(t, _editingOriginal)).ToList();
            _editing.Name = AppSettings.UniqueThemeName(_name.Text, others);
            if (!_editing.Sanitize()) { Dlg.Error("Theme", "The window and accent colors must be set."); return; }
            if (_editingOriginal != null)
            {
                int i = list.IndexOf(_editingOriginal);
                bool wasActive = string.Equals(AppSettings.Current.ThemeName, _editingOriginal.Name, StringComparison.OrdinalIgnoreCase);
                list[i] = _editing;
                if (wasActive) AppSettings.Current.ThemeName = _editing.Name;
            }
            else
            {
                if (list.Count >= ThemePalette.MaxCustomThemes) { Dlg.Warn("Theme", "You can keep up to " + ThemePalette.MaxCustomThemes + " themes. Delete one first."); return; }
                list.Add(_editing);
            }
            AppSettings.Save();
            var saved = _editing;
            _editor.Visible = false;
            _editing = null;
            _selected = saved;
            BuildTiles();
            Log.Success("Theme saved: " + saved.Name);
            if (apply) ApplySelected();
        }

        private void DeleteSelected()
        {
            if (_selected == null || _selected.BuiltIn) return;
            if (!Dlg.Confirm("Delete theme", "Delete the theme \"" + _selected.Name + "\"?", "Delete", true)) return;
            bool wasActive = string.Equals(AppSettings.Current.ThemeName, _selected.Name, StringComparison.OrdinalIgnoreCase);
            AppSettings.Current.CustomThemes.Remove(_selected);
            _selected = null;
            if (wasActive) AppSettings.Current.ThemeName = ThemePalette.DefaultName;
            AppSettings.Save();
            BuildTiles();
            if (wasActive) Shell.Current.ApplyTheme(ThemePalette.Active());
        }

        private void ImportTheme()
        {
            string f = PickFile("Import a theme", "X360GameHack theme (*.x360theme;*.json)|*.x360theme;*.json");
            if (f == null) return;
            try
            {
                if (new FileInfo(f).Length > 64 * 1024) throw new FormatException("Not a theme file (too large).");
                var p = ThemePalette.FromJson(File.ReadAllText(f));
                var list = AppSettings.Current.CustomThemes;
                if (list.Count >= ThemePalette.MaxCustomThemes) throw new InvalidOperationException("You can keep up to " + ThemePalette.MaxCustomThemes + " themes. Delete one first.");
                p.Name = AppSettings.UniqueThemeName(p.Name, list);
                list.Add(p);
                AppSettings.Save();
                _selected = p;
                BuildTiles();
                Log.Success("Theme imported: " + p.Name + " - press Apply to use it.");
            }
            catch (Exception ex) { Dlg.Error("Import theme", ex.Message); }
        }

        private void ExportTheme()
        {
            if (_selected == null) return;
            string f = PickSave("Export theme", PathSafety.SafeName(_selected.Name) + ".x360theme", "X360GameHack theme (*.x360theme)|*.x360theme");
            if (f == null) return;
            try { File.WriteAllText(f, _selected.ToJson()); Log.Success("Theme exported: " + f); }
            catch (Exception ex) { Dlg.Error("Export theme", ex.Message); }
        }
    }
}
