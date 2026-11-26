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
using X360GameHack.UI.Pages;

namespace X360GameHack.UI
{
    /// <summary>
    /// The finder the Saves tab has, for any kind of content in the Content folders of a console (XBDM), an Xbox drive or
    /// a PC folder: a Find button, a filter, a drive choice, the actions and the list. Every overwrite and delete is
    /// backed up to the Saves folder first.
    /// </summary>
    public abstract class ContentFinder<T> : UserControl where T : class
    {
        protected sealed class Column
        {
            public string Header;
            public int Width;
            public Func<T, string> Text;
            public Func<T, IComparable> Key;
            public bool Right;
        }

        protected static Column Col(string header, int width, Func<T, string> text, Func<T, IComparable> key = null, bool right = false)
            => new Column { Header = header, Width = width, Text = text, Key = key ?? (x => text(x) ?? ""), Right = right };

        private const string AllDrives = "All drives";
        private IContentStore _store;
        private readonly XTextBox _filter;
        private readonly XCombo _drives = new XCombo { Width = Theme.S(150) };
        private readonly ListView _list;
        private readonly ImageList _icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
        private readonly EmptyState _empty;
        private readonly TextBlock _summary = new TextBlock("", Theme.Small, Theme.TextFaint);
        private readonly XButton _find;
        private readonly WrapPanel _actions = new WrapPanel { Dock = DockStyle.Top };
        private readonly List<XButton> _writers = new List<XButton>();
        private readonly Column[] _cols;
        private readonly string _glyph, _idleTitle, _idleDetail;
        private List<T> _items = new List<T>();
        private CancellationTokenSource _cts;
        private int _sortCol;
        private bool _sortDesc, _scanned;

        protected ContentFinder(string glyph, string findText, string filterCue, string idleTitle, string idleDetail, params Column[] cols)
        {
            _glyph = glyph;
            _idleTitle = idleTitle;
            _idleDetail = idleDetail;
            _cols = cols;
            BackColor = Theme.Surface;
            Padding = new Padding(Theme.S(14));
            int ic = Theme.S(28);
            _icons.ImageSize = new Size(ic, ic);
            _icons.Images.Add(GlyphImage.Render(glyph, Theme.TextDim, ic));
            _list = ListStyle.Create(cols.Select(c => (c.Header, c.Width)).ToArray());
            for (int i = 0; i < cols.Length; i++) if (cols[i].Right) _list.Columns[i].TextAlign = HorizontalAlignment.Right;
            _list.SmallImageList = _icons;
            _list.Dock = DockStyle.Fill;
            _list.MultiSelect = true;
            _empty = new EmptyState(glyph, idleTitle, idleDetail) { Dock = DockStyle.Fill };
            _filter = new XTextBox(filterCue);

            var top = new HStack { Dock = DockStyle.Top, Height = Theme.S(36) };
            _find = top.Add(new XButton(findText, Glyph.Search, BtnKind.Primary, (s, e) => Scan()));
            top.Add(_filter);
            top.FillChild = _filter;
            top.Add(_drives);

            _summary.Dock = DockStyle.Bottom;
            _summary.Height = Theme.S(24);
            _summary.Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

            Controls.Add(_list);
            Controls.Add(_empty);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.S(8) });
            Controls.Add(_actions);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.S(8) });
            Controls.Add(top);
            Controls.Add(_summary);

            _filter.TextChanged += (s, e) => Fill();
            _drives.SelectedIndexChanged += (s, e) => Fill();
            _list.DoubleClick += (s, e) => { var x = Selected().FirstOrDefault(); if (x != null) OnOpen(x); };
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { var x = Selected().FirstOrDefault(); if (x != null) OnOpen(x); }
                else if (e.KeyCode == Keys.Delete) OnDelete();
                else if (e.KeyCode == Keys.F5) Scan();
                else if (e.Control && e.KeyCode == Keys.A) foreach (ListViewItem i in _list.Items) i.Selected = true;
            };
            _list.ColumnClick += (s, e) => { _sortDesc = _sortCol == e.Column && !_sortDesc; _sortCol = e.Column; Fill(); };
            SetStore(null);
        }

        // ------------------------------------------------------------------ what each finder says

        /// <summary>Plural noun for messages: "profiles", "title updates".</summary>
        protected abstract string Plural { get; }
        protected abstract Task<List<T>> FindAsync(IContentStore store, IProgress<string> progress, CancellationToken ct);
        protected abstract string DriveOf(T item);
        protected abstract Image IconOf(T item);
        /// <summary>Why the item is odd (shown in its row), or null.</summary>
        protected abstract string ProblemOf(T item);
        protected abstract bool Matches(T item, string query);
        protected abstract string NoneFound(IContentStore store);
        protected abstract string SummaryOf(List<T> all);
        /// <summary>Double-click or Enter on a row.</summary>
        protected abstract void OnOpen(T item);
        /// <summary>The Delete key.</summary>
        protected virtual void OnDelete() { }

        protected static bool Has(string text, string q) => (text ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        // ------------------------------------------------------------------ store and scanning

        public IContentStore Store => _store;
        public IReadOnlyList<T> Items => _items;
        public bool Scanned => _scanned;
        /// <summary>The search running now, or the last one (finished).</summary>
        public Task Scanning { get; private set; } = Task.CompletedTask;

        /// <summary>Where the finder looks (null = not available, e.g. console disconnected).</summary>
        public void SetStore(IContentStore store)
        {
            if (ReferenceEquals(store, _store) && store != null) return;
            _cts?.Cancel();
            _store = store;
            _items = new List<T>();
            _scanned = false;
            foreach (var b in _writers) b.Visible = store?.CanWrite ?? true;
            _find.Enabled = store != null;
            RebuildDrives();
            Fill();
        }

        public void Scan() => Scanning = ScanAsync();

        private async Task ScanAsync()
        {
            if (_store == null || !_store.IsReady) return;
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            var store = _store;
            _find.Enabled = false;
            ShowEmpty(Glyph.Search, "Looking for " + Plural + "...", "Reading every Content folder on " + store.Title + ".");
            using (Status.Begin("Finding " + Plural + " on " + store.Title, cts))
            {
                try
                {
                    var found = await FindAsync(store, new Progress<string>(t => _summary.Text = t), cts.Token);
                    if (!ReferenceEquals(store, _store)) return;
                    _items = found;
                    _scanned = true;
                    Log.Success("Found " + found.Count + " " + Plural + " on " + store.Title + ".");
                }
                catch (OperationCanceledException) { Log.Warn("Search for " + Plural + " cancelled."); }
                catch (Exception ex) { Log.Error("Search for " + Plural + " failed: " + ex.Message); Dlg.Error("Find " + Plural, ex.Message); }
                finally { _find.Enabled = _store != null; if (_cts == cts) _cts = null; cts.Dispose(); }
            }
            RebuildDrives();
            Fill();
        }

        /// <summary>Takes items out of the list (after they were deleted).</summary>
        protected void Forget(T item) => _items.Remove(item);

        protected void Refill() { RebuildDrives(); Fill(); }

        /// <summary>The drive picked in the drive list, or null for all drives.</summary>
        protected string ChosenDrive => _drives.SelectedIndex > 0 ? _drives.SelectedItem as string : null;

        private void RebuildDrives()
        {
            string keep = _drives.SelectedItem as string;
            _drives.Items.Clear();
            _drives.Items.Add(AllDrives);
            foreach (var d in _items.Select(DriveOf).Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(d => d.StartsWith("Hdd", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(d => d, StringComparer.OrdinalIgnoreCase))
                _drives.Items.Add(d);
            _drives.SelectedItem = keep != null && _drives.Items.Contains(keep) ? keep : AllDrives;
        }

        // ------------------------------------------------------------------ the list

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Window);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(10)))
            using (var b = new SolidBrush(Theme.Surface))
            using (var p = new Pen(Theme.Border)) { e.Graphics.FillPath(b, path); e.Graphics.DrawPath(p, path); }
        }

        private void ShowEmpty(string glyph, string title, string detail)
        {
            _empty.Set(glyph, title, detail);
            _empty.Visible = true;
            _list.Visible = false;
        }

        private void Fill()
        {
            if (_store == null) { ShowEmpty(_glyph, _idleTitle, _idleDetail); _summary.Text = ""; return; }
            string q = _filter.Text.Trim();
            string drive = ChosenDrive;
            var rows = _items.Where(x => (drive == null || string.Equals(DriveOf(x), drive, StringComparison.OrdinalIgnoreCase)) && (q.Length == 0 || Matches(x, q))).ToList();
            var col = _cols[Math.Min(_sortCol, _cols.Length - 1)];
            var ordered = (_sortDesc ? rows.OrderByDescending(col.Key) : rows.OrderBy(col.Key)).ThenBy(x => _cols[0].Text(x), StringComparer.OrdinalIgnoreCase).ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            while (_icons.Images.Count > 1) _icons.Images.RemoveAt(1);
            foreach (var x in ordered)
            {
                int img = 0;
                var icon = IconOf(x);
                if (icon != null) { _icons.Images.Add(icon); img = _icons.Images.Count - 1; }
                string problem = ProblemOf(x);
                var li = new ListViewItem(_cols[0].Text(x) ?? "", img) { Tag = x, ForeColor = problem != null ? Theme.Warning : Theme.Text };
                for (int c = 1; c < _cols.Length; c++) li.SubItems.Add(c == 1 && problem != null ? "(" + problem + ")" : _cols[c].Text(x) ?? "");
                _list.Items.Add(li);
            }
            _list.EndUpdate();

            if (_items.Count == 0)
            {
                if (_scanned) ShowEmpty(_glyph, "No " + Plural + " found", NoneFound(_store));
                else ShowEmpty(_glyph, "No " + Plural + " listed yet", "Press " + _find.Text + " to look on " + _store.Title + ".");
                _summary.Text = "";
                return;
            }
            _empty.Visible = rows.Count == 0;
            _list.Visible = rows.Count > 0;
            if (rows.Count == 0) _empty.Set(Glyph.Search, "Nothing matches", "Clear the filter or pick another drive.");
            _summary.Text = SummaryOf(_items) + (rows.Count != _items.Count ? "  •  showing " + rows.Count : "");
        }

        /// <summary>The rows as shown, top to bottom (for tests).</summary>
        public List<T> Shown => _list.Items.Cast<ListViewItem>().Select(i => (T)i.Tag).ToList();

        /// <summary>Selects rows (for tests and for other pages).</summary>
        public void Select(Func<T, bool> which)
        {
            foreach (ListViewItem i in _list.Items) i.Selected = which((T)i.Tag);
        }

        protected List<T> Selected() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (T)i.Tag).ToList();

        /// <summary>The selected items, for buttons the hosting page adds.</summary>
        public List<T> SelectedItems => Selected();

        protected bool Ready()
        {
            if (_store != null && _store.IsReady) return true;
            Dlg.Info(char.ToUpperInvariant(Plural[0]) + Plural.Substring(1), "The source is not available (connect to the console or open a drive first).");
            return false;
        }

        // ------------------------------------------------------------------ actions

        /// <summary>Adds an action button; <paramref name="writes"/> ones are hidden when the source is read-only.</summary>
        protected XButton Action(string text, string glyph, BtnKind kind, Action click, bool writes = false, string tip = null)
        {
            var b = _actions.Add(new XButton(text, glyph, kind, (s, e) => click()));
            if (writes) { _writers.Add(b); b.Visible = _store?.CanWrite ?? true; }
            if (tip != null) Tip.Set(b, tip);
            return b;
        }

        /// <summary>Runs a transfer with a cancellable status line; a failure is logged and shown. True when it finished.</summary>
        protected static async Task<bool> Job(string status, string what, Func<CancellationToken, Task> work)
        {
            var cts = new CancellationTokenSource();
            using (Status.Begin(status, cts))
            {
                try { await work(cts.Token); return true; }
                catch (OperationCanceledException) { Log.Warn(what + " cancelled."); }
                catch (Exception ex) { Log.Error(what + " failed: " + ex.Message); Dlg.Error(what, ex.Message); }
                finally { cts.Dispose(); }
            }
            return false;
        }

        protected static string PickFile(string title) => PageBase.PickFile(title);

        protected static string[] PickFiles(string title) => PageBase.PickFiles(title);

        protected static string PickFolder(string description) => PageBase.PickFolder(description);

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _cts?.Cancel(); _icons.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Every gamer profile on a console, an Xbox drive or in a PC folder: gamertag, gamerscore, games, Xbox LIVE or
    /// offline, and the drive it is on. A profile opens in the profile editor and can be put back from there; it can
    /// also be downloaded, replaced or deleted. Every overwrite and delete is backed up to the Saves folder first.
    /// </summary>
    public sealed class ProfileFinder : ContentFinder<ProfileInfo>
    {
        /// <param name="offersXenia">False where the list shows Xenia's own profiles: no "Copy to Xenia" there.</param>
        public ProfileFinder(string idleTitle, string idleDetail, bool offersXenia = true)
            : base(Glyph.Profile, "Find profiles", "Filter by gamertag or profile ID...", idleTitle, idleDetail,
                   Col("Gamertag", 190, p => p.Label),
                   Col("Gamerscore", 100, p => p.Gamerscore?.ToString("N0") ?? "", p => p.Gamerscore ?? -1, true),
                   Col("Games", 64, p => p.Read ? p.Games.ToString("N0") : "", p => p.Games, true),
                   Col("Achievements", 112, p => p.Read ? p.AchievementsUnlocked.ToString("N0") + " / " + p.AchievementsTotal.ToString("N0") : "", p => p.AchievementsUnlocked, true),
                   Col("Kind", 84, p => !p.Read ? "" : p.Live ? "Xbox LIVE" : "Offline"),
                   Col("Profile ID", 150, p => p.ProfileId),
                   Col("Drive", 64, p => p.Drive),
                   Col("Size", 80, p => FileSniffer.FormatSize(p.Size), p => p.Size, true),
                   Col("Modified", 128, p => p.Modified == DateTime.MinValue ? "" : p.Modified.ToString("yyyy-MM-dd HH:mm"), p => p.Modified))
        {
            Action("Open in profile editor", Glyph.Edit, BtnKind.Secondary, () => OnOpen(Selected().FirstOrDefault()));
            Action("Download...", Glyph.Download, BtnKind.Secondary, () =>
            {
                var items = Selected();
                if (items.Count == 0) { Dlg.Info("Download", "Select one or more profiles."); return; }
                if (!Ready()) return;
                string dir = PickFolder("Download the profiles to which folder? (a sub-folder per gamertag is created)");
                if (dir != null) DownloadTo(items, dir, true);
            });
            Action("Replace with...", Glyph.Upload, BtnKind.Secondary, () =>
            {
                var p = Selected().FirstOrDefault();
                if (p == null) { Dlg.Info("Replace", "Select the profile to replace."); return; }
                if (!Ready()) return;
                string local = PickFile("Replace " + p.Label + " with...");
                if (local != null) Replace(p, local);
            }, true, "Upload a profile over the selected one (the current one is backed up to your Saves folder first).");
            Action("Delete", Glyph.Delete, BtnKind.Danger, OnDelete, true);
            if (offersXenia)
                Action("Copy to Xenia", Glyph.Play, BtnKind.Secondary, async () => { var items = Selected(); if (items.Count == 0 || Ready()) await XeniaTransfer.CopyProfilesAsync(items); },
                       tip: "Copies the selected profiles into Xenia on this PC. Xenia signs in to a copied profile when it starts, unless it already signs in to one. Nothing here is changed.");
        }

        /// <summary>A profile in Xenia's own form is a folder: it opens in Explorer, as the profile editor reads console profile files only.</summary>
        private static void ShowXeniaFolder(ProfileInfo p)
        {
            Dlg.Info("Xenia profile", p.Label + " is kept the way Xenia keeps profiles: a folder of files, not a console profile file. " +
                                      "The profile editor cannot open it and a console cannot use it. Its folder opens now.");
            Tools.OpenFolder(p.Path);
        }

        protected override string Plural => "profiles";
        protected override Task<List<ProfileInfo>> FindAsync(IContentStore store, IProgress<string> progress, CancellationToken ct) => ProfileScanner.ScanAsync(store, progress, ct);
        protected override string DriveOf(ProfileInfo p) => p.Drive;
        protected override Image IconOf(ProfileInfo p) => p.Icon;
        protected override string ProblemOf(ProfileInfo p) => p.Error;
        protected override bool Matches(ProfileInfo p, string q) => Has(p.Gamertag, q) || Has(p.ProfileId, q) || Has(p.FileName, q);
        protected override string NoneFound(IContentStore store) =>
            "There are no profiles in the Content folders of " + store.Title + " (a profile is Content\\<profile ID>\\FFFE07D1\\00010000\\<profile ID>).";

        protected override string SummaryOf(List<ProfileInfo> all) =>
            all.Count + " profile(s) on " + all.Select(p => p.Drive).Distinct(StringComparer.OrdinalIgnoreCase).Count() + " drive(s)" +
            "  •  double-click a profile to open it in the profile editor";

        /// <summary>Saves\{gamertag}\{profile ID} - beside that profile's saves.</summary>
        private static string LocalPathFor(ProfileInfo p, string root)
        {
            string dir = Path.Combine(root, PathSafety.SafeName(p.Label));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, PathSafety.SafeName(p.FileName));
        }

        protected override void OnOpen(ProfileInfo p) { if (p != null) OpenAsync(p).ToString(); }

        /// <summary>Copies the profile into the Saves folder and opens it in the profile editor, which can put it back afterwards.</summary>
        public async Task<string> OpenAsync(ProfileInfo p)
        {
            if (!Ready()) return null;
            if (p.XeniaFolder) { ShowXeniaFolder(p); return null; }
            string local = LocalPathFor(p, AppSettings.Current.SavesFolderResolved);
            bool ok = await Job("Copying the profile " + p.Label, "Open profile", async ct =>
            {
                if (File.Exists(local)) File.Copy(local, local + ".prev", true);
                await p.Store.DownloadAsync(p.Path, local, Status.NewProgress(), ct);
                SaveOrigins.Remember(local, p.Store, p.Path);
                Log.Success("Copied " + p.Path + " -> " + local);
            });
            if (!ok) return null;
            WorkingFile.Set(local);
            return local;
        }

        public async Task<bool> DownloadTo(IList<ProfileInfo> items, string dir, bool show)
        {
            bool ok = await Job("Downloading " + items.Count + " profile(s)", "Download", async ct =>
            {
                foreach (var p in items)
                {
                    ct.ThrowIfCancellationRequested();
                    if (p.XeniaFolder) { string to = LocalPathFor(p, dir); await Task.Run(() => XeniaContent.CopyFolder(p.Path, to, 0), ct); continue; }
                    await p.Store.DownloadAsync(p.Path, LocalPathFor(p, dir), Status.NewProgress(), ct);
                }
                Log.Success("Downloaded " + items.Count + " profile(s) to " + dir);
            });
            if (ok && show) Tools.OpenFolder(dir);
            return ok;
        }

        /// <summary>Puts <paramref name="local"/> in place of the profile, after saying what is odd about it and backing the old one up.</summary>
        public async Task<bool> Replace(ProfileInfo p, string local)
        {
            if (!Ready() || !p.Store.CanWrite) return false;
            var check = ConSignature.Check(local);
            if (check.State == ConSignature.State.NotPackage) { Dlg.Warn("Replace", "That file is not an Xbox 360 profile (STFS package)."); return false; }
            var pkg = StfsPackage.Read(local);
            if (pkg.ContentType != 0x00010000) { Dlg.Warn("Replace", "That package is a " + pkg.ContentTypeName + ", not a profile."); return false; }
            string warn = "";
            if (!pkg.ProfileId.Equals(p.ProfileId, StringComparison.OrdinalIgnoreCase))
                warn += "\n• It is profile " + pkg.ProfileId + ", not " + p.ProfileId + " - the console expects the profile in this folder to be " + p.ProfileId + ".";
            if (check.IsCon && check.State != ConSignature.State.Valid) warn += "\n• It is not validly signed - only RGH / JTAG consoles will load it.";
            if (!Dlg.Confirm("Replace profile", "Replace\n" + p.Path + "\nwith " + Path.GetFileName(local) + "?" + (warn.Length > 0 ? "\n" + warn : "") +
                             "\n\nThe current profile is backed up to your Saves folder first.", "Replace", warn.Length > 0))
                return false;
            bool ok = await Job("Replacing " + p.Label, "Replace", async ct =>
            {
                string backup;
                if (p.XeniaFolder) backup = await Task.Run(() => XeniaContent.Put(local, p.Path), ct);       // Xenia's own profile: a folder, swapped for the file
                else
                {
                    backup = await SaveBrowser.BackupRemoteAsync(p.Store, p.Path, ct);
                    await p.Store.UploadAsync(local, p.Path, Status.NewProgress(), ct);
                }
                Log.Success("Replaced " + p.Path + " (previous copy: " + backup + ").");
            });
            if (ok) Scan();
            return ok;
        }

        protected override void OnDelete()
        {
            var items = Selected();
            if (items.Count > 0) Delete(items).ToString();
        }

        public async Task<bool> Delete(IList<ProfileInfo> items)
        {
            if (items.Count == 0 || !Ready() || !Store.CanWrite) return false;
            string names = string.Join("\n", items.Take(8).Select(p => "• " + p.Label + "  (" + p.ProfileId + ", " + p.Drive + ")")) + (items.Count > 8 ? "\n..." : "");
            if (!Dlg.Confirm("Delete profiles", "Delete " + items.Count + " profile(s)?\n\n" + names +
                             "\n\nEach one is backed up to your Saves folder first. Their saves are not touched.", "Delete", true))
                return false;
            bool ok = await Job("Deleting " + items.Count + " profile(s)", "Delete", async ct =>
            {
                foreach (var p in items)
                {
                    string backup;
                    if (p.XeniaFolder) backup = await Task.Run(() => XeniaContent.Delete(p.Path), ct);          // a folder and Xenia's header file for it
                    else
                    {
                        backup = await SaveBrowser.BackupRemoteAsync(p.Store, p.Path, ct);
                        await p.Store.DeleteAsync(p.Path, ct);
                    }
                    Forget(p);
                    Log.Info("Deleted " + p.Path + " (backup: " + backup + ")");
                }
                Log.Success("Deleted " + items.Count + " profile(s).");
            });
            Refill();
            return ok;
        }
    }

    /// <summary>
    /// Every title update on a console, an Xbox drive or in a PC folder: which game, which update, the version it is
    /// for, and where it is. An update can be checked on the Title Updates page, downloaded or deleted, and new ones
    /// installed in the folder the console reads them from. Every overwrite and delete is backed up first.
    /// </summary>
    public sealed class TitleUpdateFinder : ContentFinder<TitleUpdateInfo>
    {
        public TitleUpdateFinder(string idleTitle, string idleDetail)
            : base(Glyph.Download, "Find title updates", "Filter by game, title ID or file...", idleTitle, idleDetail,
                   Col("Game", 250, u => u.GameLabel),
                   Col("Update", 76, u => u.Number > 0 ? "TU " + u.Number : "", u => u.Number, true),
                   Col("Media ID", 90, u => u.MediaId.ToString("X8")),
                   Col("Title ID", 84, u => u.TitleId),
                   Col("Drive", 64, u => u.Drive),
                   Col("Folder", 74, u => u.InCache ? "Cache" : "Content"),
                   Col("Size", 84, u => FileSniffer.FormatSize(u.Size), u => u.Size, true),
                   Col("Modified", 128, u => u.Modified == DateTime.MinValue ? "" : u.Modified.ToString("yyyy-MM-dd HH:mm"), u => u.Modified),
                   Col("File", 190, u => u.FileName))
        {
            Action("Check on the Title Updates page", Glyph.Check, BtnKind.Secondary, () => OnOpen(Selected().FirstOrDefault()),
                   tip: "Copies the update to your Title Updates folder and opens it there, to see what it updates and check it against your game.");
            Action("Download...", Glyph.Download, BtnKind.Secondary, () =>
            {
                var items = Selected();
                if (items.Count == 0) { Dlg.Info("Download", "Select one or more title updates."); return; }
                if (!Ready()) return;
                string dir = PickFolder("Download the title updates to which folder? (a sub-folder per game is created; the file names are kept)");
                if (dir != null) DownloadTo(items, dir, true);
            });
            Action("Install a title update...", Glyph.Upload, BtnKind.Secondary, () =>
            {
                if (!Ready()) return;
                var files = PickFiles("Choose the title update package(s) to install");
                if (files != null) Install(files);
            }, true, "Copies title update packages into the folder the console reads updates from, on the drive picked in the drive list (the hard drive when all drives are shown). The file names are kept.");
            Action("Delete", Glyph.Delete, BtnKind.Danger, OnDelete, true);
        }

        protected override string Plural => "title updates";
        protected override Task<List<TitleUpdateInfo>> FindAsync(IContentStore store, IProgress<string> progress, CancellationToken ct) => TitleUpdateScanner.ScanAsync(store, progress, ct);
        protected override string DriveOf(TitleUpdateInfo u) => u.Drive;
        protected override Image IconOf(TitleUpdateInfo u) => u.Icon;
        protected override string ProblemOf(TitleUpdateInfo u) => u.Error;
        protected override bool Matches(TitleUpdateInfo u, string q) =>
            Has(u.GameLabel, q) || Has(u.TitleId, q) || Has(u.FileName, q) || Has(u.DisplayName, q) || Has("TU " + u.Number, q) || Has(u.MediaId.ToString("X8"), q);
        protected override string NoneFound(IContentStore store) =>
            "There are no title updates on " + store.Title + ". They live in Content\\0000000000000000\\<title ID>\\000B0000 (and in the Cache folder for TU_ files).";

        protected override string SummaryOf(List<TitleUpdateInfo> all)
        {
            int games = all.Select(u => u.TitleId).Distinct().Count();
            int twice = all.Where(u => u.Error == null).GroupBy(u => u.Drive + "|" + u.TitleId, StringComparer.OrdinalIgnoreCase).Count(g => g.Count() > 1);
            return all.Count + " title update(s) for " + games + " game(s)" +
                   (twice > 0 ? "  •  " + twice + " game(s) have more than one: the console uses the one whose Media ID matches the game's disc" : "") +
                   "  •  double-click one to check it on the Title Updates page";
        }

        /// <summary>Documents\X360GameHack\Title Updates - where an update opened from the list is copied to.</summary>
        public static string MyUpdatesFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "X360GameHack", "Title Updates");

        /// <summary>{folder}\{game} [{title id}]\{file} - the name is kept: the console finds an update by it.</summary>
        private static string LocalPathFor(TitleUpdateInfo u, string root)
        {
            string dir = Path.Combine(root, PathSafety.SafeName(u.GameLabel + " [" + u.TitleId + "]"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, PathSafety.SafeName(u.FileName));
        }

        protected override void OnOpen(TitleUpdateInfo u) { if (u != null) OpenAsync(u).ToString(); }

        /// <summary>Copies the update into the Title Updates folder and opens it on the Title Updates page.</summary>
        public async Task<string> OpenAsync(TitleUpdateInfo u)
        {
            if (!Ready()) return null;
            string local = LocalPathFor(u, MyUpdatesFolder);
            bool ok = await Job("Copying " + u.FileName, "Title update", async ct =>
            {
                await u.Store.DownloadAsync(u.Path, local, Status.NewProgress(), ct);
                Log.Success("Copied " + u.Path + " -> " + local);
            });
            if (!ok) return null;
            WorkingFile.Set(local);
            MainForm.Instance?.Navigate(typeof(TitleUpdatePage));
            return local;
        }

        public async Task<bool> DownloadTo(IList<TitleUpdateInfo> items, string dir, bool show)
        {
            bool ok = await Job("Downloading " + items.Count + " title update(s)", "Download", async ct =>
            {
                foreach (var u in items)
                {
                    ct.ThrowIfCancellationRequested();
                    await u.Store.DownloadAsync(u.Path, LocalPathFor(u, dir), Status.NewProgress(), ct);
                }
                Log.Success("Downloaded " + items.Count + " title update(s) to " + dir);
            });
            if (ok && show) Tools.OpenFolder(dir);
            return ok;
        }

        /// <summary>
        /// Copies title update packages into the folder the console reads them from, on the chosen drive (or the first
        /// one, the hard drive). Anything that is not a title update is refused; an update already there is backed up
        /// before it is replaced.
        /// </summary>
        public async Task<bool> Install(IList<string> files)
        {
            if (!Ready() || !Store.CanWrite) return false;
            var store = Store;
            var packages = new List<(string File, StfsPackage Pkg)>();
            var refused = new List<string>();
            foreach (string f in files)
            {
                StfsPackage pkg;
                try { pkg = StfsPackage.Read(f); } catch { pkg = null; }
                if (pkg == null || !pkg.Valid) refused.Add(Path.GetFileName(f) + " - not an Xbox 360 package");
                else if (pkg.ContentType != 0x000B0000) refused.Add(Path.GetFileName(f) + " - a " + pkg.ContentTypeName + ", not a title update");
                else packages.Add((f, pkg));
            }
            if (refused.Count > 0)
                Dlg.Warn("Not title updates", "These were left out:\n" + string.Join("\n", refused.Select(r => "• " + r)) + (packages.Count > 0 ? "\n\nThe others are installed." : ""));
            if (packages.Count == 0) return false;

            string drive = ChosenDrive, root = null;
            var replaced = new List<string>();
            bool ok = await Job("Installing " + packages.Count + " title update(s)", "Install", async ct =>
            {
                var roots = await store.ContentRootsAsync(ct);
                root = roots.FirstOrDefault(r => drive != null && ContentWalk.DriveOf(r).Equals(drive, StringComparison.OrdinalIgnoreCase)) ?? roots.FirstOrDefault();
                if (root == null) throw new InvalidOperationException("There is no Content folder on " + store.Title + ".");
                foreach (var (file, pkg) in packages)
                {
                    ct.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(file), target = TitleUpdateScanner.Destination(store, root, pkg, name);
                    string dir = ConsoleParent(target);
                    bool there = (await ContentWalk.SafeList(store, dir, ct)).Any(x => !x.IsDirectory && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (there)
                    {
                        string backup = await SaveBrowser.BackupRemoteAsync(store, target, ct);
                        replaced.Add(name + " (previous copy: " + backup + ")");
                    }
                    await store.UploadAsync(file, target, Status.NewProgress(), ct);
                    Log.Success("Installed " + name + " (" + (pkg.TitleName.Trim().Length > 0 ? pkg.TitleName.Trim() : pkg.TitleIdHex) + ") -> " + target);
                }
                foreach (var r in replaced) Log.Info("Replaced " + r);
            });
            if (ok) Scan();
            return ok;
        }

        /// <summary>The folder part of a path inside a store (Hdd:\Content\...\000B0000\tu1 -> ...\000B0000).</summary>
        private static string ConsoleParent(string path)
        {
            string p = path.TrimEnd('\\');
            int i = p.LastIndexOf('\\');
            if (i <= 0) return "\\";
            string parent = p.Substring(0, i);
            return parent.EndsWith(":") ? parent + "\\" : parent;
        }

        protected override void OnDelete()
        {
            var items = Selected();
            if (items.Count > 0) Delete(items).ToString();
        }

        public async Task<bool> Delete(IList<TitleUpdateInfo> items)
        {
            if (items.Count == 0 || !Ready() || !Store.CanWrite) return false;
            string names = string.Join("\n", items.Take(8).Select(u => "• " + u.GameLabel + (u.Number > 0 ? " - TU " + u.Number : "") + "  (" + u.FileName + ", " + u.Drive + ")")) + (items.Count > 8 ? "\n..." : "");
            if (!Dlg.Confirm("Delete title updates", "Delete " + items.Count + " title update(s)?\n\n" + names +
                             "\n\nEach one is backed up to your Saves folder first. The game then starts without the update.", "Delete", true))
                return false;
            bool ok = await Job("Deleting " + items.Count + " title update(s)", "Delete", async ct =>
            {
                foreach (var u in items)
                {
                    string backup = await SaveBrowser.BackupRemoteAsync(u.Store, u.Path, ct);
                    await u.Store.DeleteAsync(u.Path, ct);
                    Forget(u);
                    Log.Info("Deleted " + u.Path + " (backup: " + backup + ")");
                }
                Log.Success("Deleted " + items.Count + " title update(s).");
            });
            Refill();
            return ok;
        }
    }
}
