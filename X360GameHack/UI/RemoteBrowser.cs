using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>Remote file system the browser can drive (FTP or XBDM).</summary>
    public interface IRemoteFs
    {
        string Root { get; }
        bool IsReady { get; }
        bool CanLaunch { get; }
        string Combine(string dir, string name);
        string Parent(string path);
        Task<List<RemoteItem>> ListAsync(string path, CancellationToken ct);
        Task UploadAsync(IEnumerable<string> local, string remoteDir, IProgress<TransferProgress> p, CancellationToken ct);
        Task DownloadAsync(IList<RemoteItem> items, string remoteDir, string localDir, IProgress<TransferProgress> p, CancellationToken ct);
        Task DeleteAsync(IList<RemoteItem> items, string remoteDir, CancellationToken ct);
        Task RenameAsync(string remoteDir, string oldName, string newName);
        Task MakeDirectoryAsync(string path);
        Task LaunchAsync(string path);
    }

    /// <summary>
    /// Shown once, ever: XBDM is a debugging link, so a whole game takes a long time to reach the console's hard
    /// drive that way. The copy still goes ahead - this only points out the faster ways.
    /// </summary>
    public static class HddCopyTip
    {
        public const long GameSize = 100L * 1024 * 1024;

        public static bool IsHardDrive(string remotePath)
        {
            string p = (remotePath ?? "").TrimStart('\\', '/');
            return p.StartsWith("hdd", StringComparison.OrdinalIgnoreCase) || p.StartsWith("device\\harddisk0", StringComparison.OrdinalIgnoreCase);
        }

        private static long SizeOf(IEnumerable<string> local, long stopAt)
        {
            long total = 0;
            try
            {
                foreach (string item in local)
                {
                    if (System.IO.File.Exists(item)) total += new System.IO.FileInfo(item).Length;
                    else if (System.IO.Directory.Exists(item))
                        foreach (string f in System.IO.Directory.EnumerateFiles(item, "*", System.IO.SearchOption.AllDirectories)) { total += new System.IO.FileInfo(f).Length; if (total >= stopAt) return total; }
                    if (total >= stopAt) return total;
                }
            }
            catch (Exception) { }
            return total;
        }

        /// <summary>Shows the tip when something game-sized is about to go to the console's hard drive over XBDM - the first time only.</summary>
        public static void ShowOnce(string remotePath, IEnumerable<string> local)
        {
            var s = AppSettings.Current;
            if (s.HddCopyTipShown || !IsHardDrive(remotePath) || SizeOf(local, GameSize) < GameSize) return;
            s.HddCopyTipShown = true;
            AppSettings.Save();
            Dlg.Info("A faster way to copy games",
                "Copying a game to the console's hard drive over XBDM works, but it is slow.\n\n" +
                "Faster: FTP with the console on an ethernet cable (FTP page), or take the hard drive out and connect it to this PC over SATA (Drive Explorer).\n\n" +
                "The copy continues now. This tip is only shown this once.");
        }
    }

    public sealed class FtpFs : IRemoteFs
    {
        private readonly FtpService _ftp;
        public FtpFs(FtpService ftp) { _ftp = ftp; }
        public string Root => "/";
        public bool IsReady => true;
        public bool CanLaunch => false;
        public string Combine(string dir, string name) => FtpService.Combine(dir, name);
        public string Parent(string path) => FtpService.Parent(path);
        public Task<List<RemoteItem>> ListAsync(string path, CancellationToken ct) => _ftp.ListAsync(path, ct);

        public async Task UploadAsync(IEnumerable<string> local, string remoteDir, IProgress<TransferProgress> p, CancellationToken ct)
        {
            var list = local.ToList();
            var state = new TransferProgress { Total = list.Sum(FtpService.MeasureLocal) };
            foreach (var l in list)
            {
                string target = Combine(remoteDir, Path.GetFileName(l.TrimEnd('\\')));
                if (Directory.Exists(l)) await _ftp.UploadDirectoryAsync(l, target, p, state, ct);
                else await _ftp.UploadFileAsync(l, target, p, state, ct);
                Log.Success("Uploaded " + Path.GetFileName(l.TrimEnd('\\')) + " -> " + target);
            }
        }

        public async Task DownloadAsync(IList<RemoteItem> items, string remoteDir, string localDir, IProgress<TransferProgress> p, CancellationToken ct)
        {
            var state = new TransferProgress();
            foreach (var it in items)
                state.Total += it.IsDirectory ? await _ftp.MeasureRemoteAsync(Combine(remoteDir, it.Name), ct) : it.Size;
            foreach (var it in items)
            {
                string remote = Combine(remoteDir, it.Name);
                string local = Path.Combine(localDir, FtpService.SafeLocalName(it.Name));
                if (it.IsDirectory) await _ftp.DownloadDirectoryAsync(remote, local, p, state, ct);
                else await _ftp.DownloadFileAsync(remote, local, p, state, ct);
                Log.Success("Downloaded " + remote + " -> " + local);
            }
        }

        public async Task DeleteAsync(IList<RemoteItem> items, string remoteDir, CancellationToken ct)
        {
            foreach (var it in items)
            {
                await _ftp.DeleteRecursiveAsync(Combine(remoteDir, it.Name), it.IsDirectory, ct);
                Log.Info("Deleted " + Combine(remoteDir, it.Name));
            }
        }

        public Task RenameAsync(string remoteDir, string oldName, string newName) => _ftp.RenameAsync(Combine(remoteDir, oldName), newName);
        public Task MakeDirectoryAsync(string path) => _ftp.MakeDirectoryAsync(path);
        public Task LaunchAsync(string path) => Task.CompletedTask;
    }

    public sealed class XbdmFs : IRemoteFs
    {
        public string Root => "";
        public bool IsReady => ConsoleService.IsConnected;
        public bool CanLaunch => true;
        public string Combine(string dir, string name) => string.IsNullOrEmpty(dir) ? name.TrimEnd(':') + ":\\" : ConsoleService.Combine(dir, name);

        public string Parent(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string t = path.TrimEnd('\\');
            if (t.EndsWith(":")) return "";
            return ConsoleService.Parent(path);
        }

        public async Task<List<RemoteItem>> ListAsync(string path, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(path))
            {
                var drives = await ConsoleService.GetDrivesAsync();
                return drives.Select(d => new RemoteItem { Name = d.DriveName + ":", IsDirectory = true, Size = (long)d.Space.TotalBytes })
                             .OrderBy(d => d.Name).ToList();
            }
            return await ConsoleService.ListAsync(path);
        }

        public Task UploadAsync(IEnumerable<string> local, string remoteDir, IProgress<TransferProgress> p, CancellationToken ct)
            => ConsoleService.UploadAsync(local, remoteDir, p, ct);
        public Task DownloadAsync(IList<RemoteItem> items, string remoteDir, string localDir, IProgress<TransferProgress> p, CancellationToken ct)
            => ConsoleService.DownloadAsync(items, remoteDir, localDir, p, ct);
        public Task DeleteAsync(IList<RemoteItem> items, string remoteDir, CancellationToken ct) => ConsoleService.DeleteAsync(items, remoteDir, ct);
        public Task RenameAsync(string remoteDir, string oldName, string newName)
            => ConsoleService.RenameAsync(Combine(remoteDir, oldName), Combine(remoteDir, newName));
        public Task MakeDirectoryAsync(string path) => ConsoleService.MakeDirectoryAsync(path);
        public Task LaunchAsync(string path) => ConsoleService.LaunchAsync(path);
    }

    /// <summary>
    /// An Xbox-formatted drive on this PC - a USB stick or hard drive plugged in, or a drive image - read-only, for
    /// Play in Xenia from Drive Explorer. Paths look like "\Content\0000000000000000"; "" is above the drive, so the
    /// drive itself is never taken for a game.
    /// </summary>
    public sealed class FatxFs : IRemoteFs
    {
        private readonly FatxVolume _vol;
        public FatxFs(FatxVolume volume) { _vol = volume ?? throw new ArgumentNullException(nameof(volume)); }
        public string Root => "";
        public bool IsReady => true;
        public bool CanLaunch => false;
        public string Combine(string dir, string name) => (dir ?? "").TrimEnd('\\') + "\\" + name;

        public string Parent(string path)
        {
            string t = (path ?? "").TrimEnd('\\');
            if (t.Length == 0) return "";
            int cut = t.LastIndexOf('\\');
            return cut <= 0 ? "\\" : t.Substring(0, cut);
        }

        private FatxEntry Entry(string path) => _vol.FindPath(path) ?? throw new FileNotFoundException("\"" + path + "\" is not on the drive.");

        public Task<List<RemoteItem>> ListAsync(string path, CancellationToken ct) => Task.Run(() =>
        {
            List<FatxEntry> entries;
            if ((path ?? "").Trim('\\').Length == 0) entries = _vol.ListRoot();
            else
            {
                var dir = Entry(path);
                if (!dir.IsDirectory) throw new IOException("\"" + path + "\" is a file, not a folder.");
                entries = _vol.ListDirectory(dir.FirstCluster);
            }
            return entries.Select(e => new RemoteItem { Name = e.Name, IsDirectory = e.IsDirectory, Size = e.Size, Modified = e.Modified }).ToList();
        }, ct);

        public Task DownloadAsync(IList<RemoteItem> items, string remoteDir, string localDir, IProgress<TransferProgress> p, CancellationToken ct) => Task.Run(() =>
        {
            // read the folder once and match the wanted files by name - resolving each file by its full path would re-read the
            // parent folders once per file (thousands of times for a big game folder), which looks like the copy has hung.
            var children = (remoteDir ?? "").Trim('\\').Length == 0 ? _vol.ListRoot() : _vol.ListDirectory(Entry(remoteDir).FirstCluster);
            var byName = new Dictionary<string, FatxEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in children) if (!byName.ContainsKey(c.Name)) byName[c.Name] = c;
            // then copy them in the order they sit on the disk (by first cluster), so a mechanical drive moves forward and the
            // disk reader's read-ahead serves many small, neighbouring files from one read.
            var entries = items.Select(i => byName.TryGetValue(i.Name, out var e) ? e : throw new FileNotFoundException("\"" + Combine(remoteDir, i.Name) + "\" is not on the drive."))
                               .OrderBy(e => e.IsDirectory ? uint.MaxValue : e.FirstCluster).ToList();
            var state = new TransferProgress();
            foreach (var e in entries) state.Total += e.IsDirectory ? _vol.MeasureDirectory(e) : e.Size;
            Action<long> step = n => { state.Done += n; p?.Report(state); };
            Directory.CreateDirectory(localDir);
            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                state.File = e.Name;
                string target = PathSafety.CombineUnder(localDir, PathSafety.SafeName(e.Name));
                if (e.IsDirectory) _vol.ExtractDirectory(e, target, step, ct);
                else _vol.ExtractFile(e, target, step, ct);
            }
        }, ct);

        private static Task ReadOnly() => Task.FromException(new NotSupportedException("This drive is only read here."));
        public Task UploadAsync(IEnumerable<string> local, string remoteDir, IProgress<TransferProgress> p, CancellationToken ct) => ReadOnly();
        public Task DeleteAsync(IList<RemoteItem> items, string remoteDir, CancellationToken ct) => ReadOnly();
        public Task RenameAsync(string remoteDir, string oldName, string newName) => ReadOnly();
        public Task MakeDirectoryAsync(string path) => ReadOnly();
        public Task LaunchAsync(string path) => ReadOnly();
    }

    /// <summary>Explorer-style remote browser with drag-and-drop uploads and recursive transfers.</summary>
    public sealed class RemoteBrowser : UserControl
    {
        private IRemoteFs _fs;
        private string _path = "";
        private readonly Stack<string> _back = new Stack<string>();
        private readonly XTextBox _pathBox = new XTextBox("Path", false, true);
        private readonly ListView _list = ListStyle.Create(("Name", 380), ("Size", 100), ("Modified", 150));
        private readonly ImageList _icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
        private readonly XButton _launch, _launchPackage;
        private readonly TextBlock _summary = new TextBlock("", Theme.Small, Theme.TextFaint);
        private readonly EmptyState _empty = new EmptyState(Glyph.Network, "Not connected") { Dock = DockStyle.Fill, AllowDrop = true };
        private readonly WrapPanel _places = new WrapPanel { Dock = DockStyle.Top, Visible = false, HGap = Theme.S(6) };
        private readonly WrapPanel _tools = new WrapPanel { Dock = DockStyle.Top };
        private List<RemoteItem> _items = new List<RemoteItem>();

        /// <summary>Shown while nothing is attached (e.g. "connect to your console first").</summary>
        public string DetachedTitle { get; set; } = "Not connected";
        public string DetachedDetail { get; set; } = "";
        private int _sortCol;
        private bool _sortDesc;
        private string _lastDownloadDir;

        public RemoteBrowser()
        {
            BackColor = Theme.Surface;
            Padding = new Padding(Theme.S(14));
            int ic = Theme.S(18);
            _icons.ImageSize = new Size(ic, ic);
            _icons.Images.Add(GlyphImage.Render(Glyph.Folder, Theme.FolderIcon, ic));
            _icons.Images.Add(GlyphImage.Render(Glyph.Document, Theme.TextDim, ic));
            _icons.Images.Add(GlyphImage.Render(Glyph.Hdd, Theme.Accent, ic));
            _icons.Images.Add(GlyphImage.Render(Glyph.Code, Theme.AccentHover, ic));
            _list.SmallImageList = _icons;
            _list.Dock = DockStyle.Fill;
            _list.Columns[1].TextAlign = HorizontalAlignment.Right;
            _list.AllowDrop = true;
            _list.LabelEdit = false;

            var nav = new HStack { Dock = DockStyle.Top, Height = Theme.S(36) };
            nav.Add(new XButton("", Glyph.Back, BtnKind.Ghost, (s, e) => GoBack()) { Width = Theme.S(34) });
            nav.Add(new XButton("", Glyph.Up, BtnKind.Ghost, (s, e) => GoUp()) { Width = Theme.S(34) });
            nav.Add(new XButton("", Glyph.Refresh, BtnKind.Ghost, (s, e) => Refresh_()) { Width = Theme.S(34) });
            nav.Add(_pathBox);
            nav.FillChild = _pathBox;
            _pathBox.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Navigate(_pathBox.Text.Trim()); } };

            var tools = _tools;
            tools.Add(new XButton("Upload files", Glyph.Upload, BtnKind.Primary, (s, e) => UploadPick(false)));
            tools.Add(new XButton("Upload folder", Glyph.Folder, BtnKind.Secondary, (s, e) => UploadPick(true)));
            tools.Add(new XButton("Download", Glyph.Download, BtnKind.Secondary, (s, e) => DownloadSelected()));
            tools.Add(new XButton("New folder", Glyph.NewFolder, BtnKind.Secondary, (s, e) => NewFolder()));
            tools.Add(new XButton("Rename", Glyph.Rename, BtnKind.Secondary, (s, e) => RenameSelected()));
            tools.Add(new XButton("Delete", Glyph.Delete, BtnKind.Danger, (s, e) => DeleteSelected()));
            _launch = tools.Add(new XButton("Launch XEX", Glyph.Play, BtnKind.Secondary, (s, e) => LaunchSelected()));
            _launchPackage = tools.Add(new XButton("Launch package", Glyph.Package, BtnKind.Secondary, (s, e) => LaunchPackageSelected()));
            Tip.Set(_launchPackage, "Starts the game in a Games on Demand / arcade / original Xbox package: select the package file (no extension) in Content\\...\\<title ID>\\00007000. " +
                                    "Needs JRPC2 (RGH / JTAG) or a development kit's XDRPC - plain XBDM cannot mount a package.");
            Tip.Set(tools.Add(new XButton("Play in Xenia", Glyph.Game, BtnKind.Secondary, (s, e) => PlayInXenia())),
                "Copy the selected game (or the game folder you are in) to this PC and start it in the Xenia emulator. Files already copied are skipped.");
            var gap = new Panel { Dock = DockStyle.Top, Height = Theme.S(8) };
            var gap2 = new Panel { Dock = DockStyle.Top, Height = Theme.S(8) };
            _summary.Dock = DockStyle.Bottom;
            _summary.Height = Theme.S(24);
            _summary.Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

            var gap3 = new Panel { Dock = DockStyle.Top, Height = Theme.S(6) };
            Controls.Add(_list);
            Controls.Add(_empty);
            Controls.Add(gap2);
            Controls.Add(tools);
            Controls.Add(gap);
            Controls.Add(_places);
            Controls.Add(gap3);
            Controls.Add(nav);
            Controls.Add(_summary);
            _empty.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && _fs != null && _fs.IsReady && !string.IsNullOrEmpty(_path) ? DragDropEffects.Copy : DragDropEffects.None;
            _empty.DragDrop += (s, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f) Upload(f); };

            _list.DoubleClick += (s, e) => OpenSelected();
            _list.KeyDown += OnListKey;
            _list.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && _fs != null && _fs.IsReady && !string.IsNullOrEmpty(_path) ? DragDropEffects.Copy : DragDropEffects.None;
            _list.DragDrop += (s, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f) Upload(f); };
            _list.ColumnClick += (s, e) => { _sortDesc = _sortCol == e.Column && !_sortDesc; _sortCol = e.Column; Fill(); };
            _list.ContextMenuStrip = BuildMenu();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Window);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(10)))
            using (var b = new SolidBrush(Theme.Surface))
            using (var p = new Pen(Theme.Border)) { e.Graphics.FillPath(b, path); e.Graphics.DrawPath(p, path); }
        }

        private ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.Body };
            m.Items.Add("Open", null, (s, e) => OpenSelected());
            m.Items.Add("Download...", null, (s, e) => DownloadSelected());
            var launch = m.Items.Add("Launch on console", null, (s, e) => LaunchSelected());
            var launchPackage = m.Items.Add("Start package on console", null, (s, e) => LaunchPackageSelected());
            m.Items.Add("Play in Xenia (on this PC)", null, (s, e) => PlayInXenia());
            m.Items.Add("Rename...", null, (s, e) => RenameSelected());
            m.Items.Add("Delete", null, (s, e) => DeleteSelected());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("New folder...", null, (s, e) => NewFolder());
            m.Items.Add("Copy path", null, (s, e) =>
            {
                var it = Selected().FirstOrDefault();
                try { Clipboard.SetText(it == null ? _path : _fs.Combine(_path, it.Name)); } catch { }
            });
            m.Items.Add("Refresh", null, (s, e) => Refresh_());
            m.Opening += (s, e) => launch.Visible = launchPackage.Visible = _fs != null && _fs.CanLaunch;
            return m;
        }

        public string CurrentPath => _path;

        /// <summary>Quick-access buttons above the list (drives, common folders...).</summary>
        public void SetPlaces(params (string Label, string Glyph, string Path)[] places)
        {
            _places.Controls.Clear();
            foreach (var p in places)
            {
                string target = p.Path;
                _places.Add(new XButton(p.Label, p.Glyph, BtnKind.Secondary, (s, e) => Navigate(target)) { Height = Theme.S(28), Compact = true });
            }
            _places.Visible = places.Length > 0;
        }

        private void ShowList(bool list)
        {
            _list.Visible = list;
            _empty.Visible = !list;
        }

        public void Attach(IRemoteFs fs)
        {
            _fs = fs;
            _launch.Visible = _launchPackage.Visible = fs.CanLaunch;
            _places.Enabled = true;
            _tools.Enabled = true;
            _back.Clear();
            _items.Clear();
            _list.Items.Clear();
            Navigate(fs.Root, false);
        }

        public void Detach()
        {
            _fs = null;
            _items.Clear();
            _list.Items.Clear();
            _places.Enabled = false;
            _tools.Enabled = false;
            _pathBox.Text = "";
            _summary.Text = "";
            _empty.Set(Glyph.Network, DetachedTitle, DetachedDetail);
            ShowList(false);
        }

        private List<RemoteItem> Selected() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (RemoteItem)i.Tag).ToList();

        private bool Ready()
        {
            if (_fs != null && _fs.IsReady) return true;
            Dlg.Info("Not connected", "Connect to a console first.");
            return false;
        }

        public void Refresh_() => Navigate(_path, false);

        public async void Navigate(string path, bool remember = true)
        {
            if (!Ready()) return;
            using (Status.Begin("Listing " + (string.IsNullOrEmpty(path) ? "drives" : path)))
            {
                try
                {
                    var items = await _fs.ListAsync(path, CancellationToken.None);
                    if (remember && _path != path) _back.Push(_path);
                    _path = path;
                    _items = items;
                    _pathBox.Text = string.IsNullOrEmpty(path) ? "(drives)" : path;
                    Fill();
                }
                catch (Exception ex)
                {
                    Log.Error("Could not list " + path + ": " + ex.Message);
                    _summary.Text = "Error: " + ex.Message;
                }
            }
        }

        private void Fill()
        {
            IEnumerable<RemoteItem> q = _items;
            Func<RemoteItem, object> key = _sortCol == 1 ? (Func<RemoteItem, object>)(i => i.Size) : _sortCol == 2 ? (i => i.Modified) : (i => i.Name.ToLowerInvariant());
            q = _sortDesc ? q.OrderByDescending(i => i.IsDirectory).ThenByDescending(key) : q.OrderByDescending(i => i.IsDirectory).ThenBy(key);
            _list.BeginUpdate();
            _list.Items.Clear();
            bool drives = string.IsNullOrEmpty(_path);
            foreach (var it in q)
            {
                int img = drives ? 2 : it.IsDirectory ? 0 : it.Name.EndsWith(".xex", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
                var li = new ListViewItem(it.Name, img) { Tag = it };
                li.SubItems.Add(it.IsDirectory && !drives ? "" : FileSniffer.FormatSize(it.Size));
                li.SubItems.Add(it.Modified == DateTime.MinValue ? "" : it.Modified.ToString("yyyy-MM-dd HH:mm"));
                _list.Items.Add(li);
            }
            _list.EndUpdate();
            ShowList(_items.Count > 0);
            if (_items.Count == 0) _empty.Set(Glyph.FolderOpen, "This folder is empty", string.IsNullOrEmpty(_path) ? "" : "Drag files or folders here from Explorer to upload them.");
            int files = _items.Count(i => !i.IsDirectory);
            _summary.Text = $"{_items.Count - files} folder(s), {files} file(s), {FileSniffer.FormatSize(_items.Where(i => !i.IsDirectory).Sum(i => i.Size))}" +
                            "   •   drag files or folders from Explorer onto the list to upload";
        }

        private void GoUp()
        {
            if (_fs == null) return;
            if (string.IsNullOrEmpty(_path) || _path == _fs.Root) return;
            Navigate(_fs.Parent(_path));
        }

        private void GoBack()
        {
            if (_back.Count > 0) Navigate(_back.Pop(), false);
        }

        private void OnListKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) OpenSelected();
            else if (e.KeyCode == Keys.Back) GoUp();
            else if (e.KeyCode == Keys.Delete) DeleteSelected();
            else if (e.KeyCode == Keys.F2) RenameSelected();
            else if (e.KeyCode == Keys.F5) Refresh_();
            else if (e.Control && e.KeyCode == Keys.A) foreach (ListViewItem i in _list.Items) i.Selected = true;
        }

        private void OpenSelected()
        {
            var it = Selected().FirstOrDefault();
            if (it == null) return;
            if (it.IsDirectory) { Navigate(_fs.Combine(_path, it.Name)); return; }
            if (_fs.CanLaunch && it.Name.EndsWith(".xex", StringComparison.OrdinalIgnoreCase)) { LaunchSelected(); return; }
            DownloadSelected();
        }

        private async Task Transfer(string what, Func<IProgress<TransferProgress>, CancellationToken, Task> work, bool refresh)
        {
            var cts = new CancellationTokenSource();
            using (Status.Begin(what, cts))
            {
                try
                {
                    await work(Status.NewProgress(), cts.Token);
                    Log.Success(what + " - complete.");
                }
                catch (OperationCanceledException) { Log.Warn(what + " - cancelled."); }
                catch (Exception ex) { Log.Error(what + " failed: " + ex.Message); Dlg.Error(what + " failed", ex.Message); }
                finally { cts.Dispose(); }
            }
            if (refresh && _fs != null && _fs.IsReady) Refresh_();
        }

        public async void Upload(string[] local)
        {
            if (!Ready() || local == null || local.Length == 0) return;
            if (string.IsNullOrEmpty(_path)) { Dlg.Info("Upload", "Open a drive or folder first."); return; }
            string dir = _path;
            if (_fs is XbdmFs) HddCopyTip.ShowOnce(dir, local);
            await Transfer("Uploading " + local.Length + " item(s) to " + dir, (p, ct) => _fs.UploadAsync(local, dir, p, ct), true);
        }

        private void UploadPick(bool folder)
        {
            if (!Ready()) return;
            if (folder)
            {
                string d = PageBase.PickFolder("Choose a folder to upload");
                if (d != null) Upload(new[] { d });
            }
            else
            {
                string[] f = PageBase.PickFiles("Choose files to upload");
                if (f != null) Upload(f);
            }
        }

        /// <summary>One click: copy the selected game from the console to this PC (only what is not here yet) and start it in Xenia.</summary>
        public async void PlayInXenia()
        {
            if (!Ready()) return;
            var sel = Selected();
            if (sel.Count > 1) { Dlg.Info("Play in Xenia", "Select one game: its folder, its .xex file, or its package file."); return; }
            string xenia = Tools.Xenia;
            if (string.IsNullOrWhiteSpace(xenia) || !File.Exists(xenia)) { Dlg.Info("Play in Xenia", "Xenia was not found. Set its path on the Xenia page (Setup tab)."); return; }
            string dir = _path;
            var cts = new CancellationTokenSource();
            XeniaPlan plan = null;
            using (Status.Begin("Play in Xenia", cts))
            {
                try
                {
                    plan = await XeniaPlay.PlanAsync(_fs, dir, sel.FirstOrDefault(), XeniaPlay.GamesDir, cts.Token);
                    string problem = XeniaPlay.SpaceProblem(plan);
                    if (problem != null) throw new InvalidOperationException(problem);
                    if (plan.MissingBytes == 0 && plan.Missing.Count == 0) Log.Info(plan.Name + " is already on this PC (" + plan.Files + " files, " + FileSniffer.FormatSize(plan.TotalBytes) + ").");
                    else
                    {
                        Log.Info("Copying " + plan.Name + " to " + plan.LocalRoot + ": " + plan.Missing.Count + " of " + plan.Files + " file(s), " + FileSniffer.FormatSize(plan.MissingBytes) + ".");
                        await XeniaPlay.FetchAsync(_fs, plan, Status.NewProgress(), cts.Token);
                    }
                    string notGame = XeniaPlay.NotAGame(plan);
                    if (notGame != null) throw new InvalidOperationException(notGame);
                }
                catch (OperationCanceledException) { Log.Warn("Play in Xenia - cancelled. What was copied is kept; press it again to carry on."); plan = null; }
                catch (Exception ex) { Log.Error("Play in Xenia failed: " + ex.Message); Dlg.Error("Play in Xenia", ex.Message); plan = null; }
                finally { cts.Dispose(); }
            }
            if (plan == null) return;
            if (XeniaHost.Start(plan.LaunchPath)) { XeniaLibrary.MarkPlayed(plan.LaunchPath); Log.Success("Started " + plan.Name + " in Xenia from " + plan.LaunchPath + " - it is on the Xenia page's Games tab from now on."); }
        }

        private async void DownloadSelected()
        {
            if (!Ready()) return;
            var items = Selected();
            if (items.Count == 0) { Dlg.Info("Download", "Select one or more files or folders."); return; }
            string dir = PageBase.PickFolder("Download to which folder?", _lastDownloadDir);
            if (dir == null) return;
            _lastDownloadDir = dir;
            string remote = _path;
            await Transfer("Downloading " + items.Count + " item(s)", (p, ct) => _fs.DownloadAsync(items, remote, dir, p, ct), false);
        }

        private async void DeleteSelected()
        {
            if (!Ready()) return;
            var items = Selected();
            if (items.Count == 0 || string.IsNullOrEmpty(_path)) return;
            string names = string.Join("\n", items.Take(10).Select(i => (i.IsDirectory ? "[folder] " : "") + i.Name)) + (items.Count > 10 ? "\n..." : "");
            if (!Dlg.Confirm("Delete from console", "Permanently delete these item(s)? Folders are deleted with everything inside.\n\n" + names, "Delete", true)) return;
            string dir = _path;
            await Transfer("Deleting " + items.Count + " item(s)", (p, ct) => _fs.DeleteAsync(items, dir, ct), true);
        }

        private async void RenameSelected()
        {
            if (!Ready()) return;
            var it = Selected().FirstOrDefault();
            if (it == null || string.IsNullOrEmpty(_path)) return;
            string name = Dlg.Input("Rename", "New name for " + it.Name, it.Name);
            if (string.IsNullOrWhiteSpace(name) || name == it.Name) return;
            string dir = _path;
            await Transfer("Renaming " + it.Name, (p, ct) => _fs.RenameAsync(dir, it.Name, name.Trim()), true);
        }

        private async void NewFolder()
        {
            if (!Ready()) return;
            if (string.IsNullOrEmpty(_path)) { Dlg.Info("New folder", "Open a drive first."); return; }
            string name = Dlg.Input("New folder", "Folder name", "New Folder");
            if (string.IsNullOrWhiteSpace(name)) return;
            string target = _fs.Combine(_path, name.Trim());
            await Transfer("Creating " + target, (p, ct) => _fs.MakeDirectoryAsync(target), true);
        }

        private async void LaunchSelected()
        {
            if (!Ready() || !_fs.CanLaunch) return;
            var it = Selected().FirstOrDefault(i => !i.IsDirectory && i.Name.EndsWith(".xex", StringComparison.OrdinalIgnoreCase));
            if (it == null) { Dlg.Info("Launch", "Select an .xex file (for a Games on Demand or arcade package use Launch package)."); return; }
            string path = _fs.Combine(_path, it.Name);
            if (!Dlg.Confirm("Launch", "Launch " + path + " on the console?", "Launch")) return;
            await Transfer("Launching " + it.Name, (p, ct) => _fs.LaunchAsync(path), false);
        }

        /// <summary>Starts an installed package (Games on Demand, arcade, original Xbox) - JRPC2 / XRPC on an RGH, XDRPC on a devkit.</summary>
        private async void LaunchPackageSelected()
        {
            if (!Ready() || !_fs.CanLaunch) return;
            var it = Selected().FirstOrDefault(i => !i.IsDirectory && !i.Name.EndsWith(".xex", StringComparison.OrdinalIgnoreCase));
            if (it == null) { Dlg.Info("Launch package", "Select a Games on Demand or arcade package - the file without an extension in Content\\0000000000000000\\<title ID>\\00007000, 000D0000 or 00005000."); return; }
            string path = _fs.Combine(_path, it.Name);
            if (!Dlg.Confirm("Launch package", "Start the game in " + path + " on the console?\n\nThe console mounts the package and starts the game in it. XBDM cannot do that by itself, " +
                                               "so it goes through JRPC2 (RGH / JTAG) or the XDRPC of a development kit. Whatever is running now is closed.", "Start")) return;
            await Transfer("Starting " + it.Name, (p, ct) => _fs.LaunchAsync(path), false);
        }
    }

    /// <summary>Dark context menus.</summary>
    public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextFaint;
            base.OnRenderItemText(e);
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Surface;
            public override Color MenuBorder => Theme.Border;
            public override Color MenuItemSelected => Theme.SurfaceHover;
            public override Color MenuItemBorder => Theme.SurfaceHover;
            public override Color MenuItemSelectedGradientBegin => Theme.SurfaceHover;
            public override Color MenuItemSelectedGradientEnd => Theme.SurfaceHover;
            public override Color ImageMarginGradientBegin => Theme.Surface;
            public override Color ImageMarginGradientMiddle => Theme.Surface;
            public override Color ImageMarginGradientEnd => Theme.Surface;
            public override Color SeparatorDark => Theme.Border;
            public override Color SeparatorLight => Theme.Border;
        }
    }
}
