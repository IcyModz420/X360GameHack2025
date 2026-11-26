using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// Editor for one kind of console ini file (launch.ini, xbdm.ini, JRPC.ini): load it from the console or the
    /// PC, change settings in a list or as text, and send it back. The file is kept exactly as it was apart from
    /// the lines that are changed. Before anything on the console is replaced, its current file is copied to the
    /// Saves folder.
    /// </summary>
    public sealed class IniEditorCard : Card
    {
        private static readonly Encoding Bytes = Encoding.GetEncoding(28591);      // one byte per character: nothing is lost in a round trip

        private readonly IniProfile _profile;
        private readonly ListView _list = ListStyle.Create(("Section", 90), ("Setting", 130), ("Value", 230), ("What it does", 520));
        private readonly TextBlock _state = new TextBlock("", Theme.Small, Theme.TextDim) { Height = Theme.S(36), Flags = TextFormatFlags.WordBreak };
        private readonly XTextBox _text = new XTextBox(null, true, true) { Height = Theme.S(220) };
        private IniFile _file;
        private string _loadedText, _remote, _source;
        private bool _syncing;

        public IniProfile Profile => _profile;
        /// <summary>The file as it would be sent right now.</summary>
        public string CurrentText => _file == null ? "" : _file.ToString();
        public bool Dirty => _file != null && _file.ToString() != (_loadedText ?? "");
        /// <summary>Where on the console the file came from (and goes back to), or null.</summary>
        public string RemotePath => _remote;

        public IniEditorCard(IniProfile profile) : base(profile.Name, profile.What, Glyph.Settings)
        {
            _profile = profile;
            _list.Height = Theme.S(330);
            _list.MultiSelect = false;
            _list.DoubleClick += (s, e) => ChangeSelected();
            _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.SuppressKeyPress = true; ChangeSelected(); } };
            Add(_state);
            var load = new WrapPanel();
            load.Add(new XButton("Load from console", Glyph.Network, BtnKind.Primary, (s, e) => LoadFromConsole()));
            if (profile.Template != null) load.Add(new XButton("New", Glyph.Add, BtnKind.Secondary, (s, e) => { if (ConfirmDiscard()) Open(profile.Template, null, "a new file") ; }));
            Add(load);
            Add(_list);
            var edit = new WrapPanel();
            edit.Add(new XButton("Change value...", Glyph.Edit, BtnKind.Secondary, (s, e) => ChangeSelected()));
            edit.Add(new XButton("Add setting...", Glyph.Add, BtnKind.Secondary, (s, e) => AddSetting()));
            if (ReferenceEquals(profile, IniProfiles.Launch))
            {
                edit.Add(new XButton("Add plugin: XBDM", null, BtnKind.Secondary, (s, e) => AddPlugin("xbdm.xex")));
                edit.Add(new XButton("Add plugin: JRPC2", null, BtnKind.Secondary, (s, e) => AddPlugin("JRPC2.xex")));
                edit.Add(new XButton("Add plugin: XRPC", null, BtnKind.Secondary, (s, e) => AddPlugin("XRPC.xex")));
            }
            edit.Add(new XButton("Undo all changes", Glyph.Refresh, BtnKind.Secondary, (s, e) => { if (_file != null) { _file = new IniFile(_loadedText, _profile); Show(); } }));
            Add(edit);
            Add(new TextBlock("The file as text - you can edit here too, then press Use this text.", Theme.Small, Theme.TextDim));
            Add(_text);
            var send = new WrapPanel();
            send.Add(new XButton("Use this text", Glyph.Check, BtnKind.Secondary, (s, e) => UseText()));
            send.Add(new XButton("Send to console", Glyph.Send, BtnKind.Primary, (s, e) => SendToConsole()));
            send.Add(new XButton("Save as...", Glyph.Save, BtnKind.Secondary, (s, e) => SaveAs()));
            Add(send);
            Show();
        }

        // ------------------------------------------------------------------ showing

        private void Show()
        {
            _syncing = true;
            _list.BeginUpdate();
            int keep = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;
            _list.Items.Clear();
            if (_file != null)
                foreach (var e in _file.Entries())
                {
                    var known = _profile.Lookup(e.Key);
                    string does = known != null ? known.Does : e.Comment.Replace("\n", " ");
                    var li = new ListViewItem(e.Section) { Tag = e };
                    li.SubItems.Add(e.Key);
                    li.SubItems.Add(e.Value.Length == 0 ? "(empty)" : e.Value);
                    li.SubItems.Add(does);
                    if (e.Value.Length == 0) li.ForeColor = Theme.TextDim;
                    string problem = _profile.Problem(e.Key, e.Value);
                    if (problem != null) { li.ForeColor = Theme.Danger; li.SubItems[3].Text = "Problem: " + problem; }
                    _list.Items.Add(li);
                }
            if (keep >= 0 && keep < _list.Items.Count) { _list.Items[keep].Selected = true; _list.Items[keep].EnsureVisible(); }
            _list.EndUpdate();
            _text.Text = _file == null ? "" : _file.ToString();
            _syncing = false;
            if (_file == null) { _state.Text = "Nothing loaded. Connect to the console and press Load from console" + (_profile.Template != null ? ", or press New." : "."); _state.ForeColor = Theme.TextDim; }
            else
            {
                _state.Text = _profile.FileName + " from " + _source + " - " + _list.Items.Count + " setting(s)." + (Dirty ? "  Changed, not sent yet." : "") + "  Double-click a setting to change it.";
                _state.ForeColor = Dirty ? Theme.Warning : Theme.TextDim;
            }
            foreach (Control c in Controls) c.PerformLayout();
        }

        private void Open(string text, string remote, string source)
        {
            _file = new IniFile(text, _profile);
            _loadedText = remote != null ? text : null;     // a new file counts as changed until it is sent
            _remote = remote; _source = source;
            Show();
            Log.Info(_profile.FileName + " loaded from " + source + ".");
        }

        private bool ConfirmDiscard() => !Dirty || Dlg.Confirm("Unsent changes", "The changes to " + _profile.FileName + " have not been sent or saved. Throw them away?", "Throw away", true);

        // ------------------------------------------------------------------ loading

        /// <summary>The drives a console reads its ini files from, hard drive first.</summary>
        private static async Task<List<string>> Roots()
        {
            var drives = await ConsoleService.GetDrivesAsync();
            return drives.Select(d => d.DriveName.TrimEnd(':'))
                .Where(n => !n.Equals("DVD", StringComparison.OrdinalIgnoreCase) && !n.Equals("D", StringComparison.OrdinalIgnoreCase) && !n.Equals("GAME", StringComparison.OrdinalIgnoreCase) &&
                            !n.StartsWith("Sys", StringComparison.OrdinalIgnoreCase) && !n.Equals("HddX", StringComparison.OrdinalIgnoreCase) && !n.Equals("Hdd1", StringComparison.OrdinalIgnoreCase) && !n.Equals("OnBoardMU", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n.StartsWith("HDD", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(n => n)
                .Select(n => n + ":\\").ToList();
        }

        /// <summary>Finds the file on the console (hard drive first, then the other drives) and loads it.</summary>
        public async void LoadFromConsole()
        {
            if (!ConsoleService.IsConnected) { Dlg.Info("Not connected", "Connect to the console first (Neighborhood, or the bar at the top of this page)."); return; }
            if (!ConfirmDiscard()) return;
            using (Status.Begin("Looking for " + _profile.FileName + " on the console"))
            {
                try
                {
                    var found = new List<string>();
                    foreach (string root in await Roots())
                    {
                        try
                        {
                            var hit = (await ConsoleService.ListAsync(root)).FirstOrDefault(i => !i.IsDirectory && i.Name.Equals(_profile.FileName, StringComparison.OrdinalIgnoreCase));
                            if (hit != null) found.Add(root + hit.Name);
                        }
                        catch (InvalidOperationException) { /* a drive that cannot be listed */ }
                    }
                    if (found.Count == 0) { Dlg.Info(_profile.FileName, "There is no " + _profile.FileName + " in the root of any drive on the console." + (_profile.Template != null ? "\n\nPress New to start one." : "")); return; }
                    string tmp = Path.Combine(Path.GetTempPath(), "X360GameHack", "ini-" + Guid.NewGuid().ToString("N") + ".tmp");
                    Directory.CreateDirectory(Path.GetDirectoryName(tmp));
                    try
                    {
                        await ConsoleService.DownloadFileAsync(found[0], tmp, null, CancellationToken.None);
                        Open(Bytes.GetString(File.ReadAllBytes(tmp)), found[0], "the console (" + found[0] + ")");
                    }
                    finally { try { File.Delete(tmp); } catch (IOException) { } }
                    if (found.Count > 1) Log.Warn(_profile.FileName + " is also on: " + string.Join(", ", found.Skip(1)) + ". The console uses the first one it finds; this is the one on " + found[0].Substring(0, found[0].IndexOf(':') + 1));
                }
                catch (Exception ex) { Log.Error("Loading " + _profile.FileName + " failed: " + ex.Message); Dlg.Error("Load from console", ex.Message); }
            }
        }

        // ------------------------------------------------------------------ editing

        private IniEntry Selected() => _list.SelectedItems.Count == 0 ? null : (IniEntry)_list.SelectedItems[0].Tag;

        /// <summary>Sets one value, refusing what the setting cannot take. Returns the problem, or null.</summary>
        public string SetValue(string section, string key, string value)
        {
            if (_file == null) return "Load a file first.";
            string problem = _profile.Problem(key, value);
            if (problem != null) return problem;
            try { _file.Set(section, key, value); }
            catch (ArgumentException ex) { return ex.Message; }
            Show();
            return null;
        }

        private void ChangeSelected()
        {
            var e = Selected();
            if (e == null) { if (_file != null) Dlg.Info(_profile.FileName, "Select a setting in the list first."); return; }
            var known = _profile.Lookup(e.Key);
            if ((known != null && known.Kind == IniKind.Switch) || (known == null && e.IsSwitch))
            {
                string flipped = e.Value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "false" : "true";
                if (e.Value.Length > 0 && char.IsUpper(e.Value[0])) flipped = char.ToUpperInvariant(flipped[0]) + flipped.Substring(1);    // JRPC.ini writes True / False
                SetValue(e.Section, e.Key, flipped);
                return;
            }
            string help = known != null ? known.Does : e.Comment;
            string v = Dlg.Input(e.Key, (help.Length > 0 ? help + "\n\n" : "") + "Leave empty for \"not set\".", e.Value, true);
            if (v == null) return;
            string problem = SetValue(e.Section, e.Key, v);
            if (problem != null) Dlg.Warn("That value does not fit", problem);
        }

        private void AddSetting()
        {
            if (_file == null) { Dlg.Info(_profile.FileName, "Load a file first, or press New."); return; }
            var have = new HashSet<string>(_file.Entries().Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
            var missing = _profile.Settings.Where(s => !have.Contains(s.Key)).ToList();
            if (missing.Count > 0)
            {
                int pick = Dlg.Choose("Add setting", "Settings this file does not have yet:", missing.Select(s => s.Key + "  -  " + s.Does).ToList(), "Add");
                if (pick < 0) return;
                _file.Set(missing[pick].Section, missing[pick].Key, missing[pick].Default);
                Show();
                return;
            }
            string line = Dlg.Input("Add setting", "Write it as  section.name = value  (for a file without sections: name = value)", "", true);
            if (string.IsNullOrWhiteSpace(line)) return;
            int eq = line.IndexOf('=');
            if (eq < 1) { Dlg.Warn("Add setting", "Write it as name = value."); return; }
            string name = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim(), section = "";
            int dot = name.IndexOf('.');
            if (dot > 0) { section = name.Substring(0, dot); name = name.Substring(dot + 1); }
            string problem = SetValue(section, name, value);
            if (problem != null) Dlg.Warn("Add setting", problem);
        }

        /// <summary>Puts a plugin into the first free plugin slot of launch.ini (same drive as the file). Returns what happened.</summary>
        public string AddPluginToFile(string fileName)
        {
            if (_file == null) return "Load launch.ini first, or press New.";
            string drive = _remote != null && _remote.Contains(":") ? _remote.Substring(0, _remote.IndexOf(':')) : "Hdd";
            if (drive.Equals("HDD", StringComparison.OrdinalIgnoreCase)) drive = "Hdd";
            else if (drive.StartsWith("USB", StringComparison.OrdinalIgnoreCase)) drive = "Usb";
            string path = drive + ":\\" + fileName;
            var slots = Enumerable.Range(1, 5).Select(i => new { Key = "plugin" + i, Value = _file.Get("Plugins", "plugin" + i) }).ToList();
            var already = slots.FirstOrDefault(s => s.Value.EndsWith("\\" + fileName, StringComparison.OrdinalIgnoreCase));
            if (already != null) return fileName + " is already " + already.Key + " (" + already.Value + ").";
            var free = slots.FirstOrDefault(s => s.Value.Length == 0);
            if (free == null) return "All five plugin slots are in use. Empty one first.";
            _file.Set("Plugins", free.Key, path);
            Show();
            return null;
        }

        private async void AddPlugin(string fileName)
        {
            string problem = AddPluginToFile(fileName);
            if (problem != null) { Dlg.Info("Add plugin", problem); return; }
            string where = _file.Entries().First(e => e.Value.EndsWith("\\" + fileName, StringComparison.OrdinalIgnoreCase)).Value;
            Log.Info(fileName + " added as a plugin (" + where + "). Send the file and restart the console for it to load.");
            if (!ConsoleService.IsConnected) return;
            try
            {
                string root = (where.StartsWith("Hdd", StringComparison.OrdinalIgnoreCase) ? "HDD" : "USB0") + ":\\";
                bool there = (await ConsoleService.ListAsync(root)).Any(i => !i.IsDirectory && i.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                if (!there) Dlg.Warn("Plugin file missing", fileName + " is not in " + root + " on the console. Copy it there (Neighborhood) before restarting, or DashLaunch will skip it.");
            }
            catch (Exception ex) { Log.Warn("Could not check for " + fileName + " on the console: " + ex.Message); }
        }

        private void UseText()
        {
            if (_syncing) return;
            if (_file == null && _text.Text.Trim().Length == 0) return;
            var candidate = new IniFile(_text.Text.Replace("\r\n", "\n").Replace("\n", "\r\n"), _profile);
            var bad = candidate.Entries().Select(e => new { e.Key, Problem = _profile.Problem(e.Key, e.Value) }).Where(x => x.Problem != null).ToList();
            if (bad.Count > 0 && !Dlg.Confirm("Some values look wrong", string.Join("\n", bad.Take(6).Select(b => "• " + b.Problem)) + "\n\nUse the text anyway?", "Use it", true)) return;
            _file = candidate;
            if (_source == null) _source = "text typed here";
            Show();
        }

        // ------------------------------------------------------------------ sending / saving

        /// <summary>Problems that should stop the file being sent (wrong values for known settings).</summary>
        public List<string> Problems()
            => _file == null ? new List<string>() : _file.Entries().Select(e => _profile.Problem(e.Key, e.Value)).Where(p => p != null).Distinct().ToList();

        public async void SendToConsole()
        {
            if (_file == null) { Dlg.Info(_profile.FileName, "Load a file first, or press New."); return; }
            if (!ConsoleService.IsConnected) { Dlg.Info("Not connected", "Connect to the console first. (Without XBDM: Save as, then copy the file to the root of the drive by FTP or USB.)"); return; }
            var problems = Problems();
            if (problems.Count > 0) { Dlg.Warn("Fix these first", string.Join("\n", problems.Take(8).Select(p => "• " + p))); return; }
            string remote = _remote ?? "HDD:\\" + _profile.FileName;
            byte[] data = Bytes.GetBytes(_file.ToString());
            using (Status.Begin("Sending " + _profile.FileName + " to " + remote))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "X360GameHack", "ini-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(tmp));
                    // keep what is on the console now
                    string dir = ConsoleService.Parent(remote), name = remote.Substring(remote.LastIndexOf('\\') + 1), backup = null;
                    if ((await ConsoleService.ListAsync(dir)).Any(i => !i.IsDirectory && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        backup = Path.Combine(AppSettings.Current.SavesFolderResolved, "Backups", DateTime.Now.ToString("yyyy-MM-dd HHmmss"), name);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup));
                        await ConsoleService.DownloadFileAsync(remote, backup, null, CancellationToken.None);
                    }
                    File.WriteAllBytes(tmp, data);
                    await ConsoleService.UploadToPathAsync(tmp, remote, null, CancellationToken.None);
                    string check = tmp + ".check";
                    await ConsoleService.DownloadFileAsync(remote, check, null, CancellationToken.None);
                    bool same = File.ReadAllBytes(check).SequenceEqual(data);
                    try { File.Delete(check); } catch (IOException) { }
                    if (!same) throw new IOException("The file read back from the console is not what was sent." + (backup != null ? " The previous file is saved at " + backup + "." : ""));
                    _loadedText = _file.ToString(); _remote = remote; _source = "the console (" + remote + ")";
                    Show();
                    Log.Success(_profile.FileName + " sent to " + remote + " and read back identical." + (backup != null ? " Previous file kept at " + backup + "." : ""));
                    Log.Info("It takes effect the next time the console starts.");
                }
                catch (Exception ex) { Log.Error("Sending " + _profile.FileName + " failed: " + ex.Message); Dlg.Error("Send to console", ex.Message); }
                finally { try { File.Delete(tmp); } catch (IOException) { } }
            }
        }

        private void SaveAs()
        {
            if (_file == null) return;
            string path = PageBase.PickSave("Save " + _profile.FileName, _profile.FileName, "INI files|*.ini|All files|*.*");
            if (path == null) return;
            try { SaveTo(path); }
            catch (Exception ex) { Dlg.Error("Save " + _profile.FileName, ex.Message); }
        }

        public void SaveTo(string path)
        {
            File.WriteAllBytes(path, Bytes.GetBytes(_file.ToString()));
            Log.Success(_profile.FileName + " saved to " + path + ".");
        }
    }
}
