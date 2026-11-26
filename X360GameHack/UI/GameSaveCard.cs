using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// The values inside a game save, by name, for the games whose format this program knows
    /// (see <see cref="GameSaves"/>). Changes are kept in memory until "Write into the save" is pressed; that
    /// replaces the one file inside the package, which rehashes and verifies it (a .bak is made first).
    /// </summary>
    public sealed class GameSaveCard : Card
    {
        private readonly ListView _list = ListStyle.Create(("Value", 520), ("Now", 190), ("Was", 120));
        private readonly TextBlock _state = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(38), Flags = TextFormatFlags.WordBreak };
        private readonly XTextBox _filter = new XTextBox("Filter - for example kills, angola, intel, storypoints", false, true);
        private readonly XCombo _group = new XCombo { Width = Theme.S(230) };
        private readonly XCombo _action = new XCombo { Width = Theme.S(230) };
        private readonly XCheck _onlySet = new XCheck("Only values that are not 0", false, "Hide everything that is still zero / empty.");
        private readonly XButton _write;

        private string _path, _entry;
        private GameSaveKind _kind;
        private TreyarchStats _stats;
        private byte[] _original;
        private bool _filling;

        /// <summary>True while there are changes that have not been written into the save.</summary>
        public bool Dirty => _stats != null && _original != null && !_stats.Data.SequenceEqual(_original);

        public GameSaveCard() : base("Game values",
            "Named values from the game's own save format. Needs a save mod for the game - load or unlock one on the Mods page.", Glyph.Game)
        {
            _list.Height = Theme.S(380);
            _list.DoubleClick += (s, e) => EditSelected();
            _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EditSelected(); } };
            Add(_state);
            var top = new HStack { Height = Theme.S(34) };
            top.Add(_group); top.Add(_filter); top.Add(_onlySet);
            top.FillChild = _filter;
            Add(top);
            Add(_list);
            var row = new WrapPanel();
            row.Add(new XButton("Change value...", Glyph.Edit, BtnKind.Secondary, (s, e) => EditSelected()));
            row.Add(new XButton("Set selected to...", Glyph.Check, BtnKind.Secondary, (s, e) => SetSelected()));
            row.Add(_action);
            row.Add(new XButton("Run", Glyph.Play, BtnKind.Secondary, (s, e) => RunAction()));
            row.Add(new XButton("Undo all changes", Glyph.Refresh, BtnKind.Secondary, (s, e) => UndoAll()));
            _write = row.Add(new XButton("Write into the save", Glyph.Save, BtnKind.Primary, (s, e) => Write()));
            Add(row);
            _filter.TextChanged += (s, e) => Fill();
            _group.SelectedIndexChanged += (s, e) => Fill();
            _onlySet.CheckedChanged += (s, e) => Fill();
            Load(null);
        }

        /// <summary>Shows the game values of a save package, or says why there are none.</summary>
        public void Load(string packagePath)
        {
            _path = packagePath; _kind = null; _stats = null; _entry = null; _original = null;
            string why = "Open a save first.";
            if (packagePath != null && File.Exists(packagePath))
            {
                try
                {
                    var pkg = StfsPackage.Read(packagePath);
                    why = "No save mod for this game is loaded. Save editors are add-on mods: load or unlock one on the Mods page.";
                    if (pkg.Valid)
                        using (var v = StfsVolume.Open(packagePath))
                            foreach (var e in v.Entries.Where(x => !x.IsDirectory))
                            {
                                var kind = GameSaves.Detect(pkg.TitleId, e.Name);
                                if (kind == null) continue;
                                byte[] file = v.ReadFile(e);
                                string problem = TreyarchStats.Problem(file, kind.Def);
                                if (problem != null) { why = kind.Game + ": " + problem; continue; }
                                _kind = kind; _entry = e.Path; _stats = TreyarchStats.Open(file, kind.Def); _original = (byte[])file.Clone();
                                break;
                            }
                }
                catch (Exception ex) { why = "Could not read the save: " + ex.Message; }
            }
            _filling = true;
            _group.Items.Clear(); _action.Items.Clear();
            if (_stats != null)
            {
                _group.Items.Add("All groups");
                foreach (string g in _stats.Fields.Select(f => f.Group).Distinct()) _group.Items.Add(g);
                _group.SelectedIndex = 0;
                foreach (var a in _kind.Actions) _action.Items.Add(a.Name);
                if (_action.Items.Count > 0) _action.SelectedIndex = 0;
            }
            _filling = false;
            foreach (Control c in Controls) if (c != _state) c.Enabled = _stats != null;
            if (_stats == null) { _list.Items.Clear(); _state.Text = why; return; }
            Fill();
        }

        private void ShowState()
        {
            int changed = _stats.Fields.Count(f => f.IsText ? _stats.GetText(f) != Was(f) : _stats.GetText(f) != Was(f));
            _state.Text = _kind.Game + " - " + _kind.What + " (" + _entry + ", " + _stats.Fields.Count.ToString("N0") + " values)." +
                          (changed > 0 ? "  " + changed + " value(s) changed and not yet written." : "  Double-click a value to change it.");
            _state.ForeColor = changed > 0 ? Theme.Warning : Theme.TextDim;
            _write.Enabled = changed > 0;
        }

        private TreyarchStats _wasCache;
        private string Was(DdlField f)
        {
            if (_wasCache == null || !ReferenceEquals(_wasCacheSource, _original)) { _wasCache = TreyarchStats.Open(_original, _kind.Def); _wasCacheSource = _original; }
            return _wasCache.GetText(f);
        }
        private byte[] _wasCacheSource;

        private void Fill()
        {
            if (_filling || _stats == null) return;
            string q = _filter.Text.Trim();
            string group = _group.SelectedIndex > 0 ? (string)_group.SelectedItem : null;
            _list.BeginUpdate();
            _list.Items.Clear();
            int shown = 0, matched = 0;
            foreach (var f in _stats.Fields)
            {
                if (group != null && f.Group != group) continue;
                if (q.Length > 0 && f.Path.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string now = _stats.GetText(f);
                if (_onlySet.Checked && (now == "0" || now.Length == 0)) continue;
                matched++;
                if (shown >= 3000) continue;
                string was = Was(f);
                var li = new ListViewItem(f.Path) { Tag = f };
                li.SubItems.Add(now);
                li.SubItems.Add(was == now ? "" : was);
                if (was != now) li.ForeColor = Theme.Warning;
                _list.Items.Add(li);
                shown++;
            }
            _list.EndUpdate();
            ShowState();
            if (matched > shown) _state.Text += "  Showing the first " + shown.ToString("N0") + " of " + matched.ToString("N0") + " - type in the filter to narrow it down.";
        }

        private List<DdlField> Selected() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (DdlField)i.Tag).ToList();

        private static string Range(DdlField f) => f.IsText ? "text, up to " + (f.Bits / 8 - 1) + " characters" : "a whole number from 0 to " + f.Max.ToString("N0");

        private void EditSelected()
        {
            var sel = Selected();
            if (sel.Count == 0) { Dlg.Info("Change value", "Select a value in the list first."); return; }
            if (sel.Count > 1) { SetSelected(); return; }
            var f = sel[0];
            string v = Dlg.Input("Change value", f.Path + "\n(" + Range(f) + ")", _stats.GetText(f), true);
            if (v == null) return;
            try { _stats.SetText(f, v); }
            catch (Exception ex) { Dlg.Warn("That value does not fit", ex.Message); return; }
            Fill();
        }

        private void SetSelected()
        {
            var sel = Selected();
            if (sel.Count == 0) { Dlg.Info("Set selected", "Select one or more values in the list first."); return; }
            string v = Dlg.Input("Set " + sel.Count + " value(s)", "The same value is written to every selected line.", "", true);
            if (v == null) return;
            var before = (byte[])_stats.Data.Clone();
            try { foreach (var f in sel) _stats.SetText(f, v); }
            catch (Exception ex)
            {
                Buffer.BlockCopy(before, 0, _stats.Data, 0, before.Length);      // all or nothing
                Dlg.Warn("That value does not fit", ex.Message + "\n\nNothing was changed.");
                return;
            }
            Fill();
        }

        private void RunAction()
        {
            if (_stats == null || _action.SelectedIndex < 0) return;
            var a = _kind.Actions[_action.SelectedIndex];
            int n = a.Apply(_stats);
            Log.Info(a.Name + ": " + a.Does + " " + n + " value(s) changed (not written into the save yet).");
            Fill();
        }

        private void UndoAll()
        {
            if (_stats == null) return;
            Buffer.BlockCopy(_original, 0, _stats.Data, 0, _original.Length);
            Fill();
        }

        private async void Write()
        {
            if (_stats == null || !Dirty) return;
            var op = WorkingFile.BeginOperation("Writing game values");
            if (op == null) return;
            string pkg = _path, entry = _entry;
            byte[] data = (byte[])_stats.Data.Clone();
            using (op)
            using (Status.Begin("Writing game values into " + Path.GetFileName(pkg)))
            {
                try
                {
                    string bak = pkg + ".bak";
                    if (!File.Exists(bak)) File.Copy(pkg, bak);
                    await Task.Run(() => StfsEditor.ReplaceFile(pkg, entry, data));
                    Log.Success("Game values written into " + Path.GetFileName(pkg) + ". The save was rehashed and verified. Backup: " + bak);
                    Log.Info("Next: Rehash & resign (for a stock console), then Put back or Install save.");
                    Load(pkg);
                    WorkingFile.Refresh();
                }
                catch (Exception ex) { Log.Error("Writing game values failed: " + ex.Message); Dlg.Error("Writing game values failed", ex.Message + "\n\nThe save was not changed."); }
            }
        }
    }
}
