using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>Remembers where a save opened from the save browser came from, so it can be put back after editing.</summary>
    public static class SaveOrigins
    {
        private static readonly Dictionary<string, (IContentStore Store, string Remote)> Map =
            new Dictionary<string, (IContentStore, string)>(StringComparer.OrdinalIgnoreCase);

        public static void Remember(string local, IContentStore store, string remote) => Map[Path.GetFullPath(local)] = (store, remote);

        public static bool CanPutBack(string local)
            => local != null && Map.TryGetValue(Path.GetFullPath(local), out var o) && o.Store.CanWrite;

        public static string Describe(string local)
            => Map.TryGetValue(Path.GetFullPath(local), out var o) ? (o.Store is XbdmContentStore ? "console" : "source folder") : "";

        public static async Task PutBackAsync(string local)
        {
            if (!Map.TryGetValue(Path.GetFullPath(local), out var o)) return;
            if (!o.Store.IsReady) { Dlg.Info("Put back", "The source is not available (is the console still connected?)."); return; }
            if (!Dlg.Confirm("Put back", "Replace\n" + o.Remote + "\nwith your edited copy?\n\nThe current file there is backed up to your Saves folder first.", "Replace")) return;
            var cts = new CancellationTokenSource();
            using (Status.Begin("Putting " + Path.GetFileName(local) + " back", cts))
            {
                try
                {
                    string backup = await SaveBrowser.BackupRemoteAsync(o.Store, o.Remote, cts.Token);
                    await o.Store.UploadAsync(local, o.Remote, Status.NewProgress(), cts.Token);
                    Log.Success("Put back " + o.Remote + " (previous copy: " + backup + ").");
                }
                catch (OperationCanceledException) { Log.Warn("Cancelled."); }
                catch (Exception ex) { Log.Error("Put back failed: " + ex.Message); Dlg.Error("Put back failed", ex.Message); }
                finally { cts.Dispose(); }
            }
        }
    }

    /// <summary>
    /// Lists every saved game in the Content folders of a console (XBDM), an Xbox hard drive or a PC folder,
    /// with the game and profile each one belongs to. Saves can be opened in the save editor (and put back),
    /// downloaded, replaced or deleted - every overwrite / delete is backed up first.
    /// </summary>
    public sealed class SaveBrowser : UserControl
    {
        private IContentStore _store;
        private readonly XTextBox _filter = new XTextBox("Filter by game, save, profile or title ID...");
        private readonly XCombo _profiles = new XCombo { Width = Theme.S(220) };
        private readonly ListView _list = ListStyle.Create(("Game", 250), ("Save", 220), ("Profile", 150), ("Drive", 64), ("Size", 80), ("Modified", 128), ("Title ID", 84));
        private readonly ImageList _icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
        private readonly EmptyState _empty;
        private readonly TextBlock _summary = new TextBlock("", Theme.Small, Theme.TextFaint);
        private readonly XButton _scan, _replace, _delete;
        private readonly string _idleTitle, _idleDetail;
        private List<SaveInfo> _saves = new List<SaveInfo>();
        private CancellationTokenSource _cts;
        private int _sortCol;
        private bool _sortDesc, _scanned;

        /// <param name="offersXenia">False where the list shows Xenia's own saves: no "Copy to Xenia" there.</param>
        public SaveBrowser(string idleTitle, string idleDetail, bool offersXenia = true)
        {
            _idleTitle = idleTitle;
            _idleDetail = idleDetail;
            BackColor = Theme.Surface;
            Padding = new Padding(Theme.S(14));
            int ic = Theme.S(28);
            _icons.ImageSize = new Size(ic, ic);
            _icons.Images.Add(GlyphImage.Render(Glyph.Save, Theme.TextDim, ic));
            _list.SmallImageList = _icons;
            _list.Dock = DockStyle.Fill;
            _list.Columns[4].TextAlign = HorizontalAlignment.Right;
            _list.MultiSelect = true;
            _empty = new EmptyState(Glyph.Save, idleTitle, idleDetail) { Dock = DockStyle.Fill };

            var top = new HStack { Dock = DockStyle.Top, Height = Theme.S(36) };
            _scan = top.Add(new XButton("Find saves", Glyph.Search, BtnKind.Primary, (s, e) => Scan()));
            top.Add(_filter);
            top.FillChild = _filter;
            top.Add(_profiles);

            var actions = new WrapPanel { Dock = DockStyle.Top };
            actions.Add(new XButton("Open in save editor", Glyph.Edit, BtnKind.Secondary, (s, e) => OpenSelected()));
            actions.Add(new XButton("Download...", Glyph.Download, BtnKind.Secondary, (s, e) => DownloadSelected()));
            _replace = actions.Add(new XButton("Replace with...", Glyph.Upload, BtnKind.Secondary, (s, e) => ReplaceSelected()));
            _delete = actions.Add(new XButton("Delete", Glyph.Delete, BtnKind.Danger, (s, e) => DeleteSelected()));
            Tip.Set(_replace, "Upload a save over the selected one (the current one is backed up to your Saves folder first).");
            if (offersXenia)
            {
                var toXenia = actions.Add(new XButton("Copy to Xenia", Glyph.Play, BtnKind.Secondary, async (s, e) => { var items = Selected(); if (items.Count == 0 || Ready()) await XeniaTransfer.CopySavesAsync(items); }));
                Tip.Set(toXenia, "Copies the selected saves into Xenia on this PC, with their profile when Xenia does not have it yet. Nothing here is changed.");
            }

            _summary.Dock = DockStyle.Bottom;
            _summary.Height = Theme.S(24);
            _summary.Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

            Controls.Add(_list);
            Controls.Add(_empty);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.S(8) });
            Controls.Add(actions);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.S(8) });
            Controls.Add(top);
            Controls.Add(_summary);

            _filter.TextChanged += (s, e) => Fill();
            _profiles.SelectedIndexChanged += (s, e) => Fill();
            _list.DoubleClick += (s, e) => OpenSelected();
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) OpenSelected();
                else if (e.KeyCode == Keys.Delete) DeleteSelected();
                else if (e.KeyCode == Keys.F5) Scan();
                else if (e.Control && e.KeyCode == Keys.A) foreach (ListViewItem i in _list.Items) i.Selected = true;
            };
            _list.ColumnClick += (s, e) => { _sortDesc = _sortCol == e.Column && !_sortDesc; _sortCol = e.Column; Fill(); };
            SetStore(null);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Window);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(10)))
            using (var b = new SolidBrush(Theme.Surface))
            using (var p = new Pen(Theme.Border)) { e.Graphics.FillPath(b, path); e.Graphics.DrawPath(p, path); }
        }

        /// <summary>Where saves are listed from (null = not available, e.g. console disconnected).</summary>
        public void SetStore(IContentStore store)
        {
            if (ReferenceEquals(store, _store) && store != null) return;
            _cts?.Cancel();
            _store = store;
            _saves = new List<SaveInfo>();
            _scanned = false;
            _replace.Visible = _delete.Visible = store?.CanWrite ?? true;
            _scan.Enabled = store != null;
            RebuildProfiles();
            Fill();
        }

        public IContentStore Store => _store;

        /// <summary>A save in Xenia's own form is a folder: it opens in Explorer, as the save editor reads console save files only.</summary>
        private static void ShowXeniaFolder(SaveInfo s)
        {
            Dlg.Info("Xenia save", (s.SaveName.Length > 0 ? s.SaveName : s.FileName) + " is kept the way Xenia keeps saves: a folder of the game's files, not a console save file. " +
                                   "The save editor cannot open it and a console cannot use it - edit the console save, then copy it to Xenia again. Its folder opens now.");
            Tools.OpenFolder(s.Path);
        }

        public async void Scan()
        {
            if (_store == null || !_store.IsReady) return;
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            var store = _store;
            _scan.Enabled = false;
            ShowEmpty(Glyph.Search, "Looking for saves...", "Reading every Content folder on " + store.Title + ".");
            using (Status.Begin("Finding saves on " + store.Title, cts))
            {
                try
                {
                    var progress = new Progress<string>(t => _summary.Text = t);
                    var found = await SaveScanner.ScanAsync(store, progress, cts.Token);
                    if (!ReferenceEquals(store, _store)) return;
                    _saves = found;
                    _scanned = true;
                    Log.Success("Found " + found.Count + " save(s) for " + found.Select(f => f.TitleId).Distinct().Count() + " game(s) on " + store.Title + ".");
                }
                catch (OperationCanceledException) { Log.Warn("Save search cancelled."); }
                catch (Exception ex) { Log.Error("Save search failed: " + ex.Message); Dlg.Error("Find saves", ex.Message); }
                finally { _scan.Enabled = _store != null; if (_cts == cts) _cts = null; cts.Dispose(); }
            }
            RebuildProfiles();
            Fill();
        }

        private void RebuildProfiles()
        {
            string keep = _profiles.SelectedItem as string;
            _profiles.Items.Clear();
            _profiles.Items.Add("All profiles");
            foreach (var p in _saves.GroupBy(s => s.ProfileId).OrderBy(g => g.First().ProfileLabel, StringComparer.OrdinalIgnoreCase))
                _profiles.Items.Add(ProfileText(p.First()));
            _profiles.SelectedItem = keep != null && _profiles.Items.Contains(keep) ? keep : "All profiles";
        }

        private static string ProfileText(SaveInfo s) => s.ProfileName.Length > 0 ? s.ProfileName + "  (" + s.ProfileId + ")" : s.ProfileId;

        private void ShowEmpty(string glyph, string title, string detail)
        {
            _empty.Set(glyph, title, detail);
            _empty.Visible = true;
            _list.Visible = false;
        }

        private void Fill()
        {
            if (_store == null) { ShowEmpty(Glyph.Save, _idleTitle, _idleDetail); _summary.Text = ""; return; }
            string q = _filter.Text.Trim();
            string profile = _profiles.SelectedIndex > 0 ? _profiles.SelectedItem as string : null;
            var rows = _saves.Where(s => (profile == null || ProfileText(s) == profile) && (q.Length == 0 ||
                s.GameLabel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || s.SaveName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.ProfileLabel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || s.TitleId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.FileName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            Func<SaveInfo, object> key;
            switch (_sortCol)
            {
                case 1: key = s => s.SaveName; break;
                case 2: key = s => s.ProfileLabel; break;
                case 3: key = s => s.Drive; break;
                case 4: key = s => s.Size; break;
                case 5: key = s => s.Modified; break;
                case 6: key = s => s.TitleId; break;
                default: key = s => s.GameLabel; break;
            }
            var ordered = (_sortDesc ? rows.OrderByDescending(key) : rows.OrderBy(key)).ThenBy(s => s.GameLabel).ThenBy(s => s.SaveName).ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            while (_icons.Images.Count > 1) _icons.Images.RemoveAt(1);
            foreach (var s in ordered)
            {
                int img = 0;
                if (s.Icon != null) { _icons.Images.Add(s.Icon); img = _icons.Images.Count - 1; }
                var li = new ListViewItem(s.GameLabel, img) { Tag = s, ForeColor = s.Error != null ? Theme.Warning : Theme.Text };
                li.SubItems.Add(s.Error != null ? "(" + s.Error + ")" : s.SaveName.Length > 0 ? s.SaveName : s.FileName);
                li.SubItems.Add(s.ProfileLabel);
                li.SubItems.Add(s.Drive);
                li.SubItems.Add(FileSniffer.FormatSize(s.Size));
                li.SubItems.Add(s.Modified == DateTime.MinValue ? "" : s.Modified.ToString("yyyy-MM-dd HH:mm"));
                li.SubItems.Add(s.TitleId);
                _list.Items.Add(li);
            }
            _list.EndUpdate();

            if (_saves.Count == 0)
            {
                if (_scanned) ShowEmpty(Glyph.Save, "No saves found", "There are no saves in the Content folders of " + _store.Title + " (saves live in Content\\<profile>\\<title ID>\\00000001).");
                else ShowEmpty(Glyph.Save, "No saves listed yet", "Press Find saves to read every profile's saves on " + _store.Title + ".");
                _summary.Text = "";
                return;
            }
            _empty.Visible = rows.Count == 0;
            _list.Visible = rows.Count > 0;
            if (rows.Count == 0) _empty.Set(Glyph.Search, "No saves match", "Clear the filter or pick another profile.");
            int games = _saves.Select(x => x.TitleId).Distinct().Count(), profiles = _saves.Select(x => x.ProfileId).Distinct().Count();
            _summary.Text = $"{_saves.Count} save(s) for {games} game(s) on {profiles} profile(s)" + (rows.Count != _saves.Count ? $"  •  showing {rows.Count}" : "") +
                            "  •  double-click a save to open it in the save editor";
        }

        private List<SaveInfo> Selected() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (SaveInfo)i.Tag).ToList();

        private bool Ready()
        {
            if (_store != null && _store.IsReady) return true;
            Dlg.Info("Saves", "The source is not available (connect to the console or open a drive first).");
            return false;
        }

        /// <summary>Saves\{profile}\{game} [{title id}]\{file}</summary>
        private static string LocalPathFor(SaveInfo s, string root)
        {
            string dir = Path.Combine(root, PathSafety.SafeName(s.ProfileLabel), PathSafety.SafeName(s.GameLabel + " [" + s.TitleId + "]"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, PathSafety.SafeName(s.FileName));
        }

        /// <summary>
        /// Copies a remote file into Saves\Backups\{date time}\ before it is overwritten or deleted. The file keeps its
        /// name; a second backup of the same name within the same second gets a folder of its own, so none is lost.
        /// </summary>
        public static async Task<string> BackupRemoteAsync(IContentStore store, string remote, CancellationToken ct)
        {
            string root = Path.Combine(AppSettings.Current.SavesFolderResolved, "Backups"), stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss");
            string name = PathSafety.SafeName(remote.Split('\\', '/').Last());
            string dir = Path.Combine(root, stamp);
            for (int i = 2; File.Exists(Path.Combine(dir, name)); i++) dir = Path.Combine(root, stamp + " (" + i + ")");
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, name);
            await store.DownloadAsync(remote, target, null, ct);
            return target;
        }

        private async void OpenSelected()
        {
            var s = Selected().FirstOrDefault();
            if (s == null || !Ready()) return;
            if (s.XeniaFolder) { ShowXeniaFolder(s); return; }
            using (Status.Begin("Copying " + s.FileName))
            {
                try
                {
                    string local = LocalPathFor(s, AppSettings.Current.SavesFolderResolved);
                    if (File.Exists(local)) File.Copy(local, local + ".prev", true);
                    await s.Store.DownloadAsync(s.Path, local, Status.NewProgress(), CancellationToken.None);
                    SaveOrigins.Remember(local, s.Store, s.Path);
                    Log.Success("Copied " + s.Path + " -> " + local);
                    WorkingFile.Set(local);
                }
                catch (Exception ex) { Log.Error("Open failed: " + ex.Message); Dlg.Error("Open save", ex.Message); }
            }
        }

        private async void DownloadSelected()
        {
            var items = Selected();
            if (items.Count == 0) { Dlg.Info("Download", "Select one or more saves."); return; }
            if (!Ready()) return;
            string dir = PageBase.PickFolder("Download the saves to which folder? (sub-folders per profile and game are created)");
            if (dir == null) return;
            var cts = new CancellationTokenSource();
            using (Status.Begin("Downloading " + items.Count + " save(s)", cts))
            {
                try
                {
                    foreach (var s in items)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        if (s.XeniaFolder) { string to = LocalPathFor(s, dir); await Task.Run(() => XeniaContent.CopyFolder(s.Path, to, 0)); continue; }
                        await s.Store.DownloadAsync(s.Path, LocalPathFor(s, dir), Status.NewProgress(), cts.Token);
                    }
                    Log.Success("Downloaded " + items.Count + " save(s) to " + dir);
                    Tools.OpenFolder(dir);
                }
                catch (OperationCanceledException) { Log.Warn("Download cancelled."); }
                catch (Exception ex) { Log.Error("Download failed: " + ex.Message); Dlg.Error("Download", ex.Message); }
                finally { cts.Dispose(); }
            }
        }

        private async void ReplaceSelected()
        {
            var s = Selected().FirstOrDefault();
            if (s == null) { Dlg.Info("Replace", "Select the save to replace."); return; }
            if (!Ready() || !s.Store.CanWrite) return;
            string local = PageBase.PickFile("Replace " + s.SaveName + " with...");
            if (local == null) return;
            var check = ConSignature.Check(local);
            if (check.State == ConSignature.State.NotPackage) { Dlg.Warn("Replace", "That file is not an Xbox 360 save (STFS package)."); return; }
            var pkg = StfsPackage.Read(local);
            string warn = "";
            if (!pkg.TitleIdHex.Equals(s.TitleId, StringComparison.OrdinalIgnoreCase)) warn += "\n• It is for title " + pkg.TitleIdHex + ", not " + s.TitleId + " - the game will not see it.";
            if (pkg.ContentType == 1 && pkg.ProfileId != s.ProfileId) warn += "\n• Its profile ID is " + pkg.ProfileId + " (this profile is " + s.ProfileId + ") - re-assign it on the Saves page first.";
            if (check.IsCon && check.State != ConSignature.State.Valid) warn += "\n• It is not validly signed - only RGH / JTAG consoles will load it.";
            if (!Dlg.Confirm("Replace save", "Replace\n" + s.Path + "\nwith " + Path.GetFileName(local) + "?" + (warn.Length > 0 ? "\n" + warn : "") +
                             "\n\nThe current save is backed up to your Saves folder first.", "Replace", warn.Length > 0))
                return;
            var cts = new CancellationTokenSource();
            using (Status.Begin("Replacing " + s.FileName, cts))
            {
                try
                {
                    string backup;
                    if (s.XeniaFolder) backup = await Task.Run(() => XeniaContent.Put(local, s.Path));        // Xenia's own save: a folder, swapped for the file
                    else
                    {
                        backup = await BackupRemoteAsync(s.Store, s.Path, cts.Token);
                        await s.Store.UploadAsync(local, s.Path, Status.NewProgress(), cts.Token);
                    }
                    Log.Success("Replaced " + s.Path + " (previous copy: " + backup + ").");
                }
                catch (OperationCanceledException) { Log.Warn("Replace cancelled."); }
                catch (Exception ex) { Log.Error("Replace failed: " + ex.Message); Dlg.Error("Replace", ex.Message); }
                finally { cts.Dispose(); }
            }
        }

        private async void DeleteSelected()
        {
            var items = Selected();
            if (items.Count == 0 || !Ready() || !_store.CanWrite) return;
            string names = string.Join("\n", items.Take(8).Select(i => "• " + i.GameLabel + " - " + (i.SaveName.Length > 0 ? i.SaveName : i.FileName))) + (items.Count > 8 ? "\n..." : "");
            if (!Dlg.Confirm("Delete saves", "Delete " + items.Count + " save(s)?\n\n" + names + "\n\nEach one is backed up to your Saves folder first.", "Delete", true)) return;
            var cts = new CancellationTokenSource();
            using (Status.Begin("Deleting " + items.Count + " save(s)", cts))
            {
                try
                {
                    foreach (var s in items)
                    {
                        string backup;
                        if (s.XeniaFolder) backup = await Task.Run(() => XeniaContent.Delete(s.Path));           // a folder and Xenia's header file for it
                        else
                        {
                            backup = await BackupRemoteAsync(s.Store, s.Path, cts.Token);
                            await s.Store.DeleteAsync(s.Path, cts.Token);
                        }
                        _saves.Remove(s);
                        Log.Info("Deleted " + s.Path + " (backup: " + backup + ")");
                    }
                    Log.Success("Deleted " + items.Count + " save(s).");
                }
                catch (OperationCanceledException) { Log.Warn("Delete cancelled."); }
                catch (Exception ex) { Log.Error("Delete failed: " + ex.Message); Dlg.Error("Delete", ex.Message); }
                finally { cts.Dispose(); }
            }
            RebuildProfiles();
            Fill();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _cts?.Cancel(); _icons.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
