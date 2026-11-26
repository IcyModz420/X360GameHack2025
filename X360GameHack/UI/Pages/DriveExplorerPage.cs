using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>Browse FATX / XTAF hard-drive images (Xbox 360 and original Xbox) and extract files.</summary>
    public sealed class DriveExplorerPage : PageBase
    {
        public override string Title => "Drive Explorer";
        public override string Subtitle => "Browse Xbox 360 (XTAF) and original Xbox (FATX) hard drives and drive images, copy files out, and change drive images.";
        public override string PageGlyph => Glyph.Hdd;

        private readonly XCombo _volumes = new XCombo { Width = Theme.S(250), DropDownWidth = Theme.S(440) };     // the list shows whole names
        private readonly TextBlock _path = new TextBlock("", Theme.Mono, Theme.TextDim) { Height = Theme.S(30) };
        private readonly ListView _list = ListStyle.Create(("Name", 260), ("Size", 90), ("Modified", 125), ("What it is", 360));
        private readonly XTextBox _find = new XTextBox("Find on this partition") { Width = Theme.S(210) };
        private readonly Dictionary<ListViewItem, List<(uint Cluster, string Name)>> _found = new Dictionary<ListViewItem, List<(uint, string)>>();
        private bool _showingFound;                 // the list shows what Find found (in many folders), not one folder
        private int _sortColumn;                    // the column the list is sorted by (folders always first)
        private bool _sortDesc;
        private int _listing;                       // which listing is current: work for an older one is dropped
        private string _pathText = "";
        private readonly ImageList _icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
        private readonly Stack<(uint Cluster, string Name)> _stack = new Stack<(uint, string)>();
        private List<FatxVolume> _open = new List<FatxVolume>();
        private FatxVolume _vol;
        private readonly SaveBrowser _saves = new SaveBrowser("No drive open",
            "Open an Xbox 360 hard-drive image or a physical Xbox drive above - every profile's saves on it are listed here with their games.");
        private VStack _savesTab;
        private readonly OgSavesView _ogSaves = new OgSavesView();      // the Saves tab on an original Xbox drive (UDATA)
        private readonly PartitionsView _partitions = new PartitionsView();  // the Partitions tab: every partition of the drive
        private VStack _filesTab, _partsTab;
        private readonly EmptyState _empty = new EmptyState(Glyph.Hdd, "No drive open", "Open an Xbox hard-drive image or a physical Xbox drive above. A physical drive opens ready for changes (each one journaled, so it can be undone); an image opens read-only until you press Allow changes.");

        private void ShowList(bool show)
        {
            _list.Visible = show; _empty.Visible = !show;
            if (_list.Parent is VStack v) v.FillChild = show ? (Control)_list : _empty;
        }
        private bool _savesScanned;
        private string _imagePath;      // the image file that is open (null for a physical drive)
        private bool _writable;
        // work that uses the open drive in the background - reading it, writing it (also counted in _writing), or Windows
        // copying files off it after a drag (also in _dragOuts): while any runs, the drive is not let go (no other image or
        // disk opened, Allow changes not switched) and nothing reads it while it is written
        private int _working, _writing, _dragOuts;
        private bool _busy => _working > 0;
        /// <summary>Something reads or writes the open drive now (the window asks before it closes).</summary>
        public bool InUse => _working > 0;
        /// <summary>Something writes to the open drive now.</summary>
        public bool Writing => _writing > 0;

        /// <summary>One piece of work that uses the open drive, until disposed (once). On the window's thread.</summary>
        private sealed class DriveUse : IDisposable
        {
            private DriveExplorerPage _p;
            private readonly bool _writes, _drag;
            public DriveUse(DriveExplorerPage p, bool writes, bool drag = false)
            {
                _p = p; _writes = writes; _drag = drag;
                p._working++;
                if (writes) p._writing++;
                if (drag) p._dragOuts++;
            }
            public void Dispose()
            {
                var p = _p;
                if (p == null) return;
                _p = null;
                p._working--;
                if (_writes) p._writing--;
                if (_drag) p._dragOuts--;
            }
        }

        private static async Task Holding(IDisposable use, Func<Task> work)
        {
            try { await work(); }
            finally { use.Dispose(); }
        }

        /// <summary>
        /// True when nothing uses the open drive, so it may be let go. Otherwise it says to wait - or, when only Windows is
        /// still copying files off it after a drag, asks whether to let go anyway (what is not copied yet then is not).
        /// </summary>
        private bool Idle(string title)
        {
            if (!_busy) return true;
            if (_working == _dragOuts)
                return Dlg.Confirm(title, "Windows is still copying files off this drive (see its progress window). Let go of the drive anyway? " +
                                          "The files not copied yet are then not copied.", "Let go of it", true);
            Dlg.Info(title, "Wait for what is running on this drive to finish - see the status bar. Until then the drive stays open.");
            return false;
        }

        /// <summary>True (and it says so) when anything uses the open drive - a change waits for it.</summary>
        private bool Waits(string title)
        {
            if (!_busy) return false;
            Dlg.Info(title, "Wait for what is running on this drive to finish - see the status bar.");
            return true;
        }

        /// <summary>True (and it says so) when the open drive is being written - reading it now could catch half a change.</summary>
        private bool BeingWritten(string title)
        {
            if (_writing == 0) return false;
            Dlg.Info(title, "Wait for the copy onto this drive to finish - see the status bar.");
            return true;
        }

        /// <summary>For the Saves tab: a use of the drive (null, after saying why, when it has to wait).</summary>
        private IDisposable UseFor(bool writes, string title) => (writes ? Waits(title) : BeingWritten(title)) ? null : new DriveUse(this, writes);
        private XButton _allow, _toolsToggle, _eject;
        private WrapPanel _editRow, _toolsRow;
        private DiskInfo _disk;                     // the physical drive that is open (null for an image file)
        private ElevatedDiskStream _diskStream;

        public void ShowSaves() => ShowSection(_savesTab);

        /// <summary>The Drive Explorer of this window (null until it is first shown) - other pages read the drive it has open.</summary>
        public static DriveExplorerPage Instance { get; private set; }

        /// <summary>The name of the drive that is open ("" when none): the image file's name, or the physical disk.</summary>
        public string OpenDriveName => _open.Count == 0 ? "" : _imagePath != null ? Path.GetFileName(_imagePath) : _disk?.ToString() ?? "Xbox drive";

        /// <summary>The partitions of the drive that is open (a copy of the list; empty when none). Read them, never change them.</summary>
        public List<FatxVolume> OpenVolumes => new List<FatxVolume>(_open);

        protected override void OnSectionChanged() => ScanSavesIfNeeded();

        private void ScanSavesIfNeeded()
        {
            if (_vol != null && !_savesScanned && Tabs.SelectedPanel == _savesTab)
            {
                _savesScanned = true;
                if (_ogSaves.Visible) _ogSaves.Scan(); else _saves.Scan();
            }
        }

        public DriveExplorerPage() : base(false)
        {
            Instance = this;
            // the actions in groups, so the file list keeps the room: open a drive; use what is selected; change the drive
            // (only while changes are allowed); and the drive tools that are needed now and then, behind one toggle
            var bar = new Card(null) { Padding = new Padding(Theme.S(12), Theme.S(10), Theme.S(12), Theme.S(10)) };
            XButton B(WrapPanel p, string text, string glyph, Action click, string tip = null, BtnKind kind = BtnKind.Secondary)
            {
                var b = p.Add(new XButton(text, glyph, kind, (s, e) => click()) { Height = Theme.S(30), Compact = true });
                if (tip != null) Tip.Set(b, tip);
                return b;
            }
            var open = new WrapPanel();
            B(open, "Open drive image...", Glyph.FolderOpen, OpenImage, "A whole-drive dump, a single partition dump or an image this program made.", BtnKind.Primary);
            B(open, "Open physical drive...", Glyph.Hdd, OpenPhysical, "An Xbox or Xbox 360 hard drive connected to this PC (SATA or USB). It opens ready for changes: every change is journaled first, so Drive tools > Undo changes can put it back.");
            _eject = B(open, "Safely remove", Glyph.Eject, SafelyRemove, "Closes this drive, flushes it, spins it down and has Windows let go of it - original Xbox and " +
                       "Xbox 360 drives alike - so it can be unplugged, also from an internal SATA port. Bring back drive (Drive tools) starts it again without unplugging.");
            _eject.Visible = false;                 // shown while a physical drive is open
            open.Add(_volumes);
            open.Add(new XButton("", Glyph.Up, BtnKind.Ghost, (s, e) => GoUp()) { Width = Theme.S(34), Height = Theme.S(30), Compact = true });
            open.Add(_find);
            B(open, "Find", Glyph.Search, FindOnPartition, "Finds every file and folder on the partition shown whose name holds what you typed - a Title ID such as 4156081C, default.xex, a game's folder. " +
              "Double-click one to open its folder.");
            _find.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindOnPartition(); } };
            _toolsToggle = B(open, "Drive tools", Glyph.ChevronDown, ToggleTools, "Check, back up, recover deleted files, undo changes, safely remove / bring back, and the compatibility partition.", BtnKind.Ghost);
            bar.Add(open);

            var use = new WrapPanel();
            _allow = B(use, "Allow changes", Glyph.Lock, ToggleWritable,
                       "Switches between read-only and changes allowed. A physical drive opens with changes allowed - every change journaled first, so Drive tools > " +
                       "Undo changes can put it back. A drive image opens read-only: changes to an image file cannot be undone, so keep a copy.");
            bar.Add(use);

            _editRow = new WrapPanel { Visible = false };            // shown while changes are allowed
            B(_editRow, "Add files...", Glyph.Add, () => { if (CanChange()) { var f = PickFiles("Add files to this folder"); if (f != null) AddToDrive(f); } });
            B(_editRow, "Add folder...", Glyph.Folder, () => { if (CanChange()) { var f = PickFolder("Add a folder (with everything in it) to this folder"); if (f != null) AddToDrive(new[] { f }); } });
            B(_editRow, "Install packages...", Glyph.Package, InstallPackages, "Games on Demand, arcade games, DLC, title updates, saves: each is put in the right Content folder of this drive");
            bar.Add(_editRow);

            _toolsRow = new WrapPanel { Visible = false };           // shown with Drive tools
            B(_toolsRow, "Check drive", Glyph.Search, CheckDrive, "Reads the partition's file table and folders and says whether anything is wrong. Changes nothing.");
            B(_toolsRow, "Back up whole drive...", Glyph.Save, BackUpDrive, "Copies every sector of the Xbox drive that is open into one image file on this PC, with its SHA-256. The drive is only read.");
            B(_toolsRow, "Recover deleted...", Glyph.Search, RecoverDeleted, "Finds what was deleted on this partition and can still be got back - deleted files, deleted folders with what they held, and lost data - and copies what you tick to this PC. The drive is only read.");
            B(_toolsRow, "Undo changes...", Glyph.Refresh, UndoChanges, "Puts back everything one session of changes did to this physical drive, sector by sector, from its undo journal.");
            B(_toolsRow, "Safely remove drive", Glyph.Eject, SafelyRemove, "Closes the Xbox drive, flushes it, spins it down and has Windows let go of it, so it can be unplugged - also on an internal SATA port.");
            B(_toolsRow, "Bring back drive", Glyph.Hdd, BringBack, "A drive you safely removed but did not unplug: Windows starts using it again. Also looks for drives connected since. Windows asks for permission.");
            B(_toolsRow, "Create compatibility partition", Glyph.Hdd, CreateHddx, "Xbox 360 drives only: makes the original-Xbox compatibility partition (HddX, 256 MB) when the drive has none - formatted like a console's own.");
            B(_toolsRow, "Make Xbox drive...", Glyph.Hdd, MakeDrive, "Makes (formats) an Xbox drive - original Xbox or Xbox 360 - as a new drive image file, " +
              "or on a drive connected to this PC (a new, blank one too): the layout is chosen and shown first. On a real drive every block is journaled, so Undo changes can put it back.");
            B(_toolsRow, "Install emulator files...", Glyph.Game, InstallCompat, "Copies your own backward-compatibility files (the Compatibility folder with xefu*.xex) into the drive's compatibility partition. They are Microsoft's files and do not come with X360GameHack.");
            bar.Add(_toolsRow);
            Body.Add(bar);
            var files = _filesTab = AddSection("Files", Glyph.Folder);
            _partsTab = AddSection("Partitions", Glyph.Hdd);
            var partsArea = new ScrollArea(_partitions);
            _partsTab.Add(partsArea);
            _partsTab.FillChild = partsArea;
            _partitions.OpenRequested += v => { _volumes.SelectedItem = v; ShowSection(_filesTab); };
            _savesTab = AddSection("Saves", Glyph.Save);
            _savesTab.Add(_saves);
            _savesTab.FillChild = _saves;
            _savesTab.Add(_ogSaves);
            _ogSaves.Visible = false;
            _path.Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            files.Add(_path);

            int ic = Theme.S(18);
            _icons.ImageSize = new System.Drawing.Size(ic, ic);
            _icons.Images.Add(GlyphImage.Render(Glyph.Folder, Theme.FolderIcon, ic));
            _icons.Images.Add(GlyphImage.Render(Glyph.Document, Theme.TextDim, ic));
            _icons.Images.Add(GlyphImage.Render(Glyph.Package, Theme.Accent, ic));
            _list.SmallImageList = _icons;
            _list.Columns[1].TextAlign = HorizontalAlignment.Right;
            // "What it is" takes the room the other columns leave
            _list.Resize += (s, e) =>
            {
                int used = 0;
                for (int i = 0; i < _list.Columns.Count - 1; i++) used += _list.Columns[i].Width;
                int rest = _list.ClientSize.Width - used - Theme.S(4);
                if (rest > Theme.S(160)) _list.Columns[_list.Columns.Count - 1].Width = rest;
            };
            _list.DoubleClick += (s, e) => OpenSelected();
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && e.Alt) { e.SuppressKeyPress = true; ShowProperties(); }
                else if (e.KeyCode == Keys.Enter) OpenSelected();
                else if (e.KeyCode == Keys.Back) GoUp();
                else if (e.KeyCode == Keys.F2) RenameSelected();
                else if (e.KeyCode == Keys.Delete) DeleteSelected();
                else if (e.KeyCode == Keys.F5) Refresh_();
                else if (e.KeyCode == Keys.A && e.Control) { e.SuppressKeyPress = true; _list.BeginUpdate(); foreach (ListViewItem i in _list.Items) i.Selected = true; _list.EndUpdate(); }
                else if (e.KeyCode == Keys.F && e.Control) { e.SuppressKeyPress = true; _find.Box.Focus(); _find.Box.SelectAll(); }
            };
            _list.ContextMenuStrip = BuildMenu();
            Tip.Set(_list, "Right-click a file or folder: Extract, Open, Play, Copy to Xenia, Rename, Delete, Properties. Or drag files out to Windows, and in to copy them onto the drive.");
            _list.ColumnClick += (s, e) =>
            {
                _sortDesc = _sortColumn == e.Column ? !_sortDesc : false;
                _sortColumn = e.Column;
                SortList();
            };
            _list.ItemDrag += (s, e) => { if (e.Button == MouseButtons.Left) DragOut(); };
            _list.AllowDrop = true;
            _list.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            _list.DragDrop += (s, e) =>
            {
                if (!(e.Data.GetData(DataFormats.FileDrop) is string[] f) || f.Length == 0) return;
                DropOnList(f);
            };
            _list.Visible = false;
            files.Add(_list);
            files.Add(_empty);
            files.FillChild = _empty;

            _volumes.SelectedIndexChanged += (s, e) =>
            {
                _vol = _volumes.SelectedItem as FatxVolume;
                _partitions.SetCurrent(_vol);
                Tip.Set(_volumes, _vol == null ? "" : _vol + "\nEvery partition of the drive is on the Partitions tab.");
                _stack.Clear();
                _saves.SetStore(_vol != null ? new FatxContentStore(_vol) : null);
                _savesScanned = false;
                if (_vol != null) ShowDir(_vol.RootCluster, "");
                ScanSavesIfNeeded();
            };
        }

        private void OpenPhysical()
        {
            if (!RawDisk.AppDirIsProtected())
            {
                Dlg.Info("Install required",
                    "Reading a physical drive needs a tiny helper with administrator rights (Windows requires it for raw disks).\n\n" +
                    "For your safety X360GameHack only runs that helper when it is installed in Program Files, where its files can't be " +
                    "replaced by other programs. Run X360GameHack-Setup.msi, then try again.\n\nWithout installing you can still open drive image files.");
                return;
            }
            if (!Idle("Open physical drive")) return;
            Run(null, "Looking for drives", async () =>
            {
                List<DiskInfo> disks;
                try { disks = RawDisk.List().Where(d => !d.IsSystem).ToList(); }
                catch (Exception ex) { Dlg.Error("Drives", "Could not list drives: " + ex.Message); return; }

                // a drive you safely removed earlier is parked and Windows will not list it until it is brought back - do that
                // automatically, with the usual permission prompt, then look again
                if (disks.Count == 0)
                {
                    Log.Info("No Xbox drive is visible - bringing back any drive that was safely removed...");
                    string why = await RawDisk.RemountAsync();
                    if (why != null) Log.Info("Bring back drive: " + why);
                    try { disks = RawDisk.List().Where(d => !d.IsSystem).ToList(); }
                    catch (Exception ex) { Dlg.Error("Drives", "Could not list drives: " + ex.Message); return; }
                }

                if (disks.Count == 0)
                {
                    Dlg.Info("Drives", "No Xbox drive was found. Connect the Xbox hard drive (SATA or a USB adapter) and try again.\n\n" +
                        "If you safely removed it earlier and it is still plugged in, press \"Bring back drive\" to start it again.");
                    return;
                }
                int pick = Dlg.Choose("Open physical drive", "Pick the Xbox hard drive. Windows will ask for administrator permission for 'X360GameHack Disk Reader'. " +
                    "It only opens drives that contain an Xbox file system (never the Windows disk), and closes together with X360GameHack. The drive opens " +
                    "ready for changes - original Xbox and Xbox 360 alike: before any sector changes, what it held is saved in an undo journal, so Drive tools > " +
                    "Undo changes can put it back. (Changes allowed switches it to read-only.)\n\n" +
                    "Don't see your drive? Close this and press \"Bring back drive\".", disks.Select(d => d.ToString()).ToList(), "Open");
                if (pick < 0) return;
                OpenDisk(disks[pick], true, null);
            });
        }

        /// <summary>Opens a physical Xbox drive through the Disk Reader helper - read-only, or with changes allowed (journaled).</summary>
        private void OpenDisk(DiskInfo disk, bool writable, string volumeName)
        {
            Run(null, "Opening disk " + disk.Index, async () =>
            {
                Close();          // the helper of the old session lets go of the drive first
                var stream = await RawDisk.OpenAsync(disk.Index, System.Threading.CancellationToken.None, writable);
                var vols = await Task.Run(() => FatxVolume.OpenStream(stream, "Disk " + disk.Index, true));
                if (vols.Count == 0)
                {
                    stream.Dispose();
                    Dlg.Warn("Drive Explorer", "No FATX / XTAF partitions could be read on that drive.");
                    return;
                }
                _open = vols;
                _volumes.Items.Clear();
                foreach (var v in vols) _volumes.Items.Add(v);
                int same = volumeName == null ? -1 : vols.FindIndex(v => v.Name == volumeName);
                _volumes.SelectedIndex = same >= 0 ? same : DefaultVolume(vols);
                _imagePath = null;
                _disk = disk;
                _diskStream = stream;
                SetWritable(writable && stream.CanWrite);
                LogLayout(vols, stream);        // the details first: the status bar keeps the last line, which says what opened
                Log.Success("Opened " + disk + (stream.CanWrite ? " with changes allowed - every change is journaled first (undo session " + stream.Session + ")." : " read-only") + ": " + vols.Count + " partition(s) - all of them are on the Partitions tab.");
                ShowPlayButtons();
                _partitions.SetDrive(OpenDriveName, _open, _vol);
            });
        }

        /// <summary>Copies every sector of the physical drive that is open into an image file, with its SHA-256. Read-only.</summary>
        private void BackUpDrive()
        {
            if (_diskStream == null) { Dlg.Info("Back up whole drive", "Open a physical Xbox drive first (Open physical drive...). An image file can be copied like any other file."); return; }
            if (BeingWritten("Back up whole drive")) return;
            string to = PickSave("Back up the whole Xbox drive to", "xbox-drive-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".img", "Drive images|*.img");
            if (to == null) return;
            long size = _diskStream.Length;
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(to));
                long free = new DriveInfo(root).AvailableFreeSpace;
                if (free < size + (64L << 20)) { Dlg.Warn("Back up whole drive", "That drive has " + FileSniffer.FormatSize(free) + " free; the backup needs " + FileSniffer.FormatSize(size) + "."); return; }
            }
            catch { }
            var stream = _diskStream;
            var cts = new System.Threading.CancellationTokenSource();
            var use = new DriveUse(this, false);
            Run(null, "Backing up the drive", async () =>
            {
                try
                {
                    using (Status.Begin("Backing up " + FileSniffer.FormatSize(size), cts))
                    {
                        var progress = Status.NewProgress();
                        string sha = await Task.Run(() =>
                        {
                            using (var o = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
                            using (var h = System.Security.Cryptography.SHA256.Create())
                            {
                                var buf = new byte[4 << 20];
                                long done = 0;
                                while (done < size)
                                {
                                    cts.Token.ThrowIfCancellationRequested();
                                    int want = (int)Math.Min(buf.Length, size - done), got = 0;
                                    lock (stream)
                                    {
                                        stream.Position = done;
                                        while (got < want) { int n = stream.Read(buf, got, want - got); if (n <= 0) break; got += n; }
                                    }
                                    if (got != want) throw new IOException("The drive stopped answering at " + FileSniffer.FormatSize(done) + ".");
                                    o.Write(buf, 0, got);
                                    h.TransformBlock(buf, 0, got, null, 0);
                                    done += got;
                                    progress.Report(new Core.Net.TransferProgress { Done = done, Total = size, File = Path.GetFileName(to) });
                                }
                                h.TransformFinalBlock(buf, 0, 0);
                                return BitConverter.ToString(h.Hash).Replace("-", "").ToLowerInvariant();
                            }
                        }, cts.Token);
                        File.WriteAllText(to + ".sha256", sha + " *" + Path.GetFileName(to) + Environment.NewLine);
                        Log.Success("Backed up the whole drive (" + FileSniffer.FormatSize(size) + ") to " + to + " - SHA-256 " + sha + " (also in " + Path.GetFileName(to) + ".sha256).");
                    }
                }
                catch (OperationCanceledException) { try { File.Delete(to); } catch { } Log.Warn("Backup cancelled - the partial file was deleted."); }
                finally { use.Dispose(); cts.Dispose(); }
            });
        }

        /// <summary>The stream of what is open: the physical drive, or the image file (through its first volume).</summary>
        private Stream OpenStreamOf() => _diskStream ?? (Stream)_open.FirstOrDefault()?.GetType().GetField("_s", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_open.FirstOrDefault());

        private void CreateHddx()
        {
            if (!CanChange()) return;
            if (_open.Any(v => v.Offset == FatxVolume.CompatibilityOffset)) { Dlg.Info("Compatibility partition", "This drive has its compatibility partition already (" + FatxVolume.CompatibilityName + "). Use Install emulator files... to put the emulator in it."); return; }
            if (!_open.Any(v => v.Offset == FatxVolume.DataOffset360 && v.BigEndian)) { Dlg.Warn("Compatibility partition", "This is not an Xbox 360 drive with the console's layout (its Data partition at 0x130EB0000), so no compatibility partition is made."); return; }
            if (!Dlg.Confirm("Create the compatibility partition?",
                    "A new, empty 256 MB original-Xbox compatibility partition (HddX) is made at 0x120EB0000, the place the console uses for it, formatted the way a console's own is." +
                    (_diskStream != null ? "\n\nEvery block written is journaled first, so Undo changes can put the drive back." : "\n\nThe image file is changed - keep a spare copy."), "Create", true)) return;
            var stream = OpenStreamOf();
            string keep = _vol?.Name;
            Run(null, "Creating the compatibility partition", () => Holding(new DriveUse(this, true), async () =>
            {
                await Task.Run(() => FatxVolume.CreateCompatibilityPartition(stream));
                Log.Success("Created the compatibility partition (HddX, 256 MB at 0x120EB0000).");
                if (_disk != null) OpenDisk(_disk, true, FatxVolume.CompatibilityName);
                else LoadImage(_imagePath, true, FatxVolume.CompatibilityName);
            }));
        }

        private void InstallCompat()
        {
            if (!CanChange()) return;
            var hx = _open.FirstOrDefault(v => v.Offset == FatxVolume.CompatibilityOffset);
            if (hx == null) { Dlg.Info("Install emulator files", "This drive has no compatibility partition. Press Create compatibility partition first (Xbox 360 drives only)."); return; }
            string picked = PickFolder("Pick your Compatibility folder (the one with xefu.xex in it)");
            if (picked == null) return;
            string from;
            try { from = FatxVolume.CompatibilitySource(picked); }
            catch (Exception ex) { Dlg.Warn("Install emulator files", ex.Message); return; }
            var bad = FatxVolume.NameProblemsIn(from);
            if (bad.Count > 0) { Dlg.Warn("Install emulator files", "Nothing was copied. These names are not accepted on an Xbox drive:\n" + string.Join("\n", bad.Take(10))); return; }
            long total = Core.Net.FtpService.MeasureLocal(from);
            if (total > hx.FreeBytes) { Dlg.Warn("Install emulator files", "The compatibility partition has " + FileSniffer.FormatSize(hx.FreeBytes) + " free; these files are " + FileSniffer.FormatSize(total) + "."); return; }
            Run(null, "Installing the emulator files", () => Holding(new DriveUse(this, true), async () =>
            {
                await Task.Run(() => hx.InstallCompatibility(from));
                Log.Success("Installed the emulator files (" + FileSniffer.FormatSize(total) + ") into " + FatxVolume.CompatibilityName + "\\Compatibility.");
                _volumes.SelectedItem = hx;
                ShowDir(hx.RootCluster, "");
            }));
        }

        // ------------------------------------------------------------------ making (formatting) Xbox drives

        /// <summary>
        /// Make an Xbox drive: a new drive image file, the image or drive that is open, or a drive connected to this PC (a
        /// blank one too). The layout is chosen and shown first (<see cref="MakeDriveDialog"/>); a real drive is written
        /// through the Disk Reader's format mode - every block journaled, so Undo changes can put it back.
        /// </summary>
        private void MakeDrive()
        {
            if (!Idle("Make an Xbox drive")) return;
            bool openImage = _open.Count > 0 && _imagePath != null, openDisk = _open.Count > 0 && _disk != null;
            var choices = new List<string> { "A new drive image file on this PC (for xemu, or to copy onto a drive later)" };
            if (openImage) choices.Add("The image that is open: " + Path.GetFileName(_imagePath) + " (erases it)");
            if (openDisk) choices.Add("The drive that is open: " + _disk + " (erases it)");
            choices.Add("A drive connected to this PC, a new blank one too (erases it)");
            int pick = Dlg.Choose("Make an Xbox drive", "What should become an Xbox drive?", choices, "Next");
            if (pick < 0) return;
            if (pick == 0) MakeImage();
            else if (pick == 1 && openImage) RemakeImage(_imagePath);
            else if (pick == 1 && openDisk) MakePhysical(_disk);
            else MakePhysical(null);
        }

        private void MakeImage()
        {
            var extra = new List<(string, long)>();
            try
            {
                foreach (var d in RawDisk.List().Where(x => !x.IsSystem))
                {
                    long n = RawDisk.ExactSize(d.Index);
                    if (n > 0) extra.Add(("the size of disk " + d.Index + ": " + d.Model + " (" + FileSniffer.FormatSize(n) + ")", n));
                }
            }
            catch { /* the list is only a convenience */ }
            var plan = MakeDriveDialog.Ask("A new drive image file on this PC. Only what is written takes room (a sparse file): a 1 TB drive image starts at a few MB.",
                                           0, null, 0, false, false, extra);
            if (plan == null) return;
            string to = PickSave("Save the new Xbox drive image as", (plan.Xbox360 ? "xbox360" : "xbox") + "-drive-" + (plan.DriveSize >> 30) + "GB.img", "Drive images|*.img");
            if (to == null) return;
            if (_imagePath != null && string.Equals(Path.GetFullPath(to), Path.GetFullPath(_imagePath), StringComparison.OrdinalIgnoreCase)) Close();
            Run(null, "Making the drive image", async () =>
            {
                string tmp = to + ".making";
                try
                {
                    using (Status.Begin("Making " + Path.GetFileName(to)))
                    {
                        var progress = Status.NewProgress();
                        await Task.Run(() =>
                        {
                            if (File.Exists(tmp)) File.Delete(tmp);
                            DriveFormatter.CreateImageFile(tmp, plan.DriveSize);
                            using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                                DriveFormatter.Apply(fs, plan, (d, t) => progress.Report(new Core.Net.TransferProgress { Done = d, Total = t, File = Path.GetFileName(to) }));
                            if (File.Exists(to)) File.Delete(to);
                            File.Move(tmp, to);
                        });
                    }
                }
                finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
                Log.Success("Made " + to + " (" + (plan.Xbox360 ? "Xbox 360" : "original Xbox") + ", " + FileSniffer.FormatSize(plan.DriveSize) + "): " + plan.Describe() + ".");
                LoadImage(to);
            });
        }

        private void RemakeImage(string path)
        {
            long size, f;
            DriveLayout? current;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    size = fs.Length;
                    var m = new byte[4];
                    string magic = fs.Read(m, 0, 4) == 4 ? System.Text.Encoding.ASCII.GetString(m) : "";
                    if (magic == "FATX" || magic == "XTAF")
                    {
                        Dlg.Warn("Make an Xbox drive", "This image is a single partition, not a whole drive - whole drives are made here. Make a new drive image instead.");
                        return;
                    }
                    current = DriveFormatter.Detect(fs, out f);
                }
            }
            catch (Exception ex) { Dlg.Error("Make an Xbox drive", ex.Message); return; }
            var plan = MakeDriveDialog.Ask("The image " + Path.GetFileName(path) + " (" + FileSniffer.FormatSize(size) + "). Everything on it is erased - changes to an image file " +
                                           "are not journaled, so keep a copy if you may want it back.", size, current, f, false, true);
            if (plan == null) return;
            Close();
            SetWritable(false);
            Run(null, "Making the drive", async () =>
            {
                using (Status.Begin("Making " + Path.GetFileName(path)))
                {
                    var progress = Status.NewProgress();
                    await Task.Run(() =>
                    {
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            DriveFormatter.Apply(fs, plan, (d, t) => progress.Report(new Core.Net.TransferProgress { Done = d, Total = t, File = Path.GetFileName(path) }));
                    });
                }
                Log.Success("Made " + path + " again (" + (plan.Xbox360 ? "Xbox 360" : "original Xbox") + "): " + plan.Describe() + ".");
                LoadImage(path);
            });
        }

        private void MakePhysical(DiskInfo disk)
        {
            if (!RawDisk.AppDirIsProtected())
            {
                Dlg.Info("Install required", "Making a real drive needs the Disk Reader, which X360GameHack only runs when it is installed in Program Files. " +
                         "Run X360GameHack-Setup.msi, then try again. Without installing you can still make drive image files.");
                return;
            }
            DriveLayout? current = null;
            long f = 0;
            if (disk == null)
            {
                List<DiskInfo> disks;
                try { disks = RawDisk.List().Where(d => !d.IsSystem).ToList(); }
                catch (Exception ex) { Dlg.Error("Make an Xbox drive", "Could not list drives: " + ex.Message); return; }
                if (disks.Count == 0) { Dlg.Info("Make an Xbox drive", "No drive other than the Windows disk is connected. Connect the drive (SATA or a USB adapter) and try again."); return; }
                var labels = disks.Select(d => d + (d.Partitions > 0 ? "  - Windows uses it (" + d.Partitions + " partition" + (d.Partitions == 1 ? "" : "s") + ")" : "")).ToList();
                int pick = Dlg.Choose("Make an Xbox drive", "Pick the drive that becomes an Xbox drive - check its model and size: EVERYTHING on it is erased. " +
                                      "A drive Windows uses (it has Windows partitions) is refused: copy off what you want to keep and take its partitions off first. " +
                                      "Windows asks for permission for 'X360GameHack Disk Reader'.", labels, "Next");
                if (pick < 0) return;
                disk = disks[pick];
            }
            else if (_diskStream != null && ReferenceEquals(disk, _disk))
            {
                try { current = DriveFormatter.Detect(_diskStream, out f); } catch { current = null; }
            }
            long size = RawDisk.ExactSize(disk.Index);
            if (size <= 0) size = disk.Size;
            var plan = MakeDriveDialog.Ask("Disk " + disk.Index + ": " + disk.Model + " - " + FileSniffer.FormatSize(size) + " (" + (size / 512).ToString("N0") + " sectors)",
                                           size, current, f, true, true);
            if (plan == null) return;
            Close();
            SetWritable(false);
            var cts = new System.Threading.CancellationTokenSource();
            var use = new DriveUse(this, true);
            Run(null, "Making the Xbox drive", async () =>
            {
                try
                {
                    using (var stream = await RawDisk.OpenAsync(disk.Index, cts.Token, true, plan.Xbox360 ? "x360" : "og"))
                    {
                        if (Math.Abs(stream.Length - plan.DriveSize) >= 4096)
                            throw new IOException("The drive's size (" + stream.Length.ToString("N0") + " bytes) is not the one planned (" + plan.DriveSize.ToString("N0") + ") - nothing was written.");
                        using (Status.Begin("Making disk " + disk.Index + " an Xbox drive", cts))
                        {
                            var progress = Status.NewProgress();
                            await Task.Run(() => DriveFormatter.Apply(stream, plan, (d, t) => progress.Report(new Core.Net.TransferProgress { Done = d, Total = t, File = "disk " + disk.Index }), cts.Token));
                        }
                        Log.Success("Disk " + disk.Index + " is now an " + (plan.Xbox360 ? "Xbox 360" : "original Xbox") + " drive: " + plan.Describe() +
                                    ". Every change was journaled (undo session " + stream.Session + ") - Drive tools > Undo changes can put it back.");
                    }
                }
                finally { use.Dispose(); cts.Dispose(); }
                OpenDisk(disk, true, null);
            });
        }

        /// <summary>Closes the physical drive (its helper session ends), then flushes, spins down and removes it so it can be unplugged.</summary>
        private void SafelyRemove()
        {
            if (!Idle("Safely remove drive")) return;
            DiskInfo disk = _disk;
            if (disk == null)
            {
                List<DiskInfo> disks;
                try { disks = RawDisk.List().Where(d => !d.IsSystem).ToList(); }
                catch (Exception ex) { Dlg.Error("Safely remove drive", ex.Message); return; }
                if (disks.Count == 0) { Dlg.Info("Safely remove drive", "No drive other than the Windows disk is connected."); return; }
                int pick = Dlg.Choose("Safely remove drive", "Pick the Xbox drive to remove. Only a drive with an Xbox file system is removed; Windows asks for permission.", disks.Select(d => d.ToString()).ToList(), "Remove");
                if (pick < 0) return;
                disk = disks[pick];
            }
            Close();                    // the open session lets go of the drive first
            SetWritable(false);
            Run(null, "Safely removing disk " + disk.Index, async () =>
            {
                string why = await RawDisk.EjectAsync(disk.Index);
                if (why == null)
                {
                    Log.Success(disk.Model + " flushed, spun down and released by Windows - it can be unplugged now.");
                    Dlg.Info("Safely remove drive", disk.Model + " can be unplugged now.\n\nTo use it again without unplugging it, press Bring back drive.");
                }
                else { Log.Warn("Safely remove: " + why); Dlg.Warn("Safely remove drive", why); }
            });
        }

        /// <summary>Starts again drives that were safely removed but are still connected, then says which drives Windows sees.</summary>
        private void BringBack()
        {
            if (Waits("Bring back drive")) return;
            Run(null, "Bringing back safely removed drives", async () =>
            {
                string why = await RawDisk.RemountAsync();
                if (why != null) { Log.Warn("Bring back drive: " + why); Dlg.Warn("Bring back drive", why); return; }
                List<DiskInfo> disks;
                try { disks = RawDisk.List().Where(d => !d.IsSystem).ToList(); }
                catch (Exception ex) { Dlg.Error("Bring back drive", ex.Message); return; }
                Log.Success("Windows sees " + disks.Count + " drive(s) besides the Windows disk" + (disks.Count > 0 ? ": " + string.Join("; ", disks.Select(d => d.ToString())) : "."));
                Dlg.Info("Bring back drive", disks.Count == 0
                    ? "No drive besides the Windows disk is there. If the Xbox drive is connected, check its cable and power, then try again."
                    : "Windows sees these drives now:\n\n" + string.Join("\n", disks.Select(d => d.ToString())) + "\n\nOpen physical drive... opens the Xbox drive.");
            });
        }

        /// <summary>Puts back one session of changes made to the physical drive, from the helper's undo journal.</summary>
        private void UndoChanges()
        {
            if (_disk == null || _diskStream == null) { Dlg.Info("Undo changes", "Open the physical Xbox drive first. (Changes to an image file are not journaled - keep a spare copy of it instead.)"); return; }
            if (!Idle("Undo changes")) return;
            if (!_diskStream.CanWrite)
            {
                if (!Dlg.Confirm("Undo changes", "Putting changes back writes to the drive, so it has to be opened with changes allowed (Windows asks for permission again). Do that now?", "Open for changes")) return;
                string keep = _vol?.Name;
                var d = _disk;
                OpenDisk(d, true, keep);
                return;
            }
            List<string> sessions;
            try { sessions = _diskStream.UndoSessions(); }
            catch (Exception ex) { Dlg.Error("Undo changes", ex.Message); return; }
            sessions.Remove(_diskStream.Session);                  // the open session is listed first if it changed anything
            if (_diskStream.BlocksWritten > 0) sessions.Insert(0, _diskStream.Session);
            if (sessions.Count == 0) { Dlg.Info("Undo changes", "This drive has no recorded changes."); return; }
            int pick = Dlg.Choose("Undo changes", "Pick the session to put back (newest first). Every sector it changed is restored, newest change first. " +
                                  "It is refused when anything else changed the drive since - once the drive has been in a console, delete what was added instead.",
                                  sessions.Select(x => x + (x == _diskStream.Session ? "  (this session)" : "")).ToList(), "Put back");
            if (pick < 0) return;
            string name = sessions[pick], volName = _vol?.Name;
            Run(null, "Putting changes back", () => Holding(new DriveUse(this, true), async () =>
            {
                int restored = 0, total = 0;
                string why = await Task.Run(() => _diskStream.Undo(name, out restored, out total));
                if (why == null) Log.Success("Put back " + total + " change(s) of session " + name + " - those sectors are as they were before it.");
                else { Log.Warn("Undo of " + name + ": " + why); Dlg.Warn("Undo changes", why); }
                // the volumes read the drive again so the folders shown are what is on it now
                OpenDisk(_disk, true, volName);
                await Task.CompletedTask;
            }));
        }

        /// <summary>Opens a drive or storage image (read-only) - used by the Console page after a storage backup.</summary>
        public void OpenImageFile(string path) { if (Idle("Open image")) LoadImage(path); }

        private void OpenImage()
        {
            if (!Idle("Open image")) return;
            var f = PickFile("Open an Xbox / Xbox 360 hard-drive or partition image", "Disk images|*.img;*.bin;*.raw;*.dd;*.iso|All files|*.*");
            if (f != null) LoadImage(f);
        }

        private void LoadImage(string path, bool writable = false, string volumeName = null)
        {
            Run(null, "Reading drive image", async () =>
            {
                Close();        // the file must be let go before it can be opened for changes
                var vols = await Task.Run(() => FatxVolume.OpenImage(path, writable));
                if (vols.Count == 0)
                {
                    Dlg.Warn("Drive Explorer", "No FATX / XTAF file system was found in this file.\n\nUse a raw dump of an Xbox or Xbox 360 hard drive, or a dump of a single partition.");
                    return;
                }
                _open = vols;
                _volumes.Items.Clear();
                foreach (var v in vols) _volumes.Items.Add(v);
                _imagePath = path;
                SetWritable(writable);
                int same = volumeName == null ? -1 : vols.FindIndex(v => v.Name == volumeName);
                _volumes.SelectedIndex = same >= 0 ? same : DefaultVolume(vols);
                LogLayout(vols, null);          // the details first: the status bar keeps the last line, which says what opened
                Log.Success("Found " + vols.Count + " partition(s) in " + Path.GetFileName(path) + (writable ? " - open for changes." : " - read-only.") + " All of them are on the Partitions tab.");
                ShowPlayButtons();
                _partitions.SetDrive(OpenDriveName, _open, _vol);
            });
        }

        /// <summary>
        /// The log lines that tell a drive's layout: every partition found (where, how big, cluster size) and, for an original
        /// Xbox drive, what sits at each place one keeps partitions (FatxVolume.DescribeOgLayout) - so a big drive with an
        /// unusual layout (a G: partition, a partition table) can be told apart. Only reads.
        /// </summary>
        private void LogLayout(List<FatxVolume> vols, Stream stream)
        {
            foreach (var v in vols)
                Log.Info("  " + v.Name + ": at 0x" + v.Offset.ToString("X") + ", " + FileSniffer.FormatSize(v.Length) + ", " + (v.ClusterSize / 1024) + " KB clusters" + (v.BigEndian ? "" : " (original Xbox)"));
            if (!vols.Any(v => !v.BigEndian)) return;
            try
            {
                var s = stream ?? OpenStreamOf();
                if (s != null) foreach (string line in FatxVolume.DescribeOgLayout(s)) Log.Info("  layout: " + line);
            }
            catch (Exception ex) { Log.Warn("  layout: could not read it (" + ex.Message + ")"); }
        }

        private void Close()
        {
            _saves.SetStore(null);
            foreach (var v in _open) v.Dispose();
            _open.Clear();
            _vol = null;
            _disk = null;
            _diskStream = null;
            _list.Items.Clear();
            ShowList(false);
            ShowPlayButtons();
            _partitions.SetDrive("", null, null);
        }

        /// <summary>
        /// What fits the drive that is open: an original Xbox drive gets Play in xemu and its own Saves tab (UDATA); an Xbox 360
        /// drive (or none) the Xenia buttons and the Xbox 360 saves.
        /// </summary>
        private void ShowPlayButtons()
        {
            bool og = _open.Count > 0 && _open.All(v => !v.BigEndian);
            if (_eject != null) _eject.Visible = _disk != null && _open.Count > 0;     // Safely remove: physical drives only
            _saves.Visible = !og;
            _ogSaves.Visible = og;
            _savesTab.FillChild = og ? (Control)_ogSaves : _saves;
            _ogSaves.SetDrive(og ? _open : new List<FatxVolume>(), () => CanChange(), title => PickFolder(title), UseFor);
            _savesScanned = false;
            ScanSavesIfNeeded();
        }

        // ------------------------------------------------------------------ changing a drive image

        private void SetWritable(bool on)
        {
            _writable = on;
            _allow.Text = on ? "Changes allowed" : "Allow changes";
            _allow.Kind = on ? BtnKind.Primary : BtnKind.Secondary;
            _allow.Invalidate();
            _editRow.Visible = on;                                   // the change buttons show only while changes are allowed
            Body.PerformLayout();
        }

        /// <summary>Shows or hides the drive tools row.</summary>
        private void ToggleTools()
        {
            _toolsRow.Visible = !_toolsRow.Visible;
            _toolsToggle.Glyph = _toolsRow.Visible ? Glyph.ChevronRight : Glyph.ChevronDown;
            _toolsToggle.Kind = _toolsRow.Visible ? BtnKind.Secondary : BtnKind.Ghost;
            Body.PerformLayout();
        }

        private void ToggleWritable()
        {
            if (_vol == null) { Dlg.Info("Drive Explorer", "Open a drive image first."); return; }
            if (!Idle(_writable ? "Read-only" : "Allow changes")) return;
            if (_imagePath == null)
            {
                if (_disk == null) return;
                var d = _disk; string keep = _vol.Name;
                if (_writable) { OpenDisk(d, false, keep); return; }
                if (!Dlg.Confirm("Allow changes to this Xbox hard drive?",
                        d + "\n\nFiles and folders on it can then be added, replaced, renamed and deleted. Changes go straight onto the drive, " +
                        "only inside its Xbox partitions (never the Windows disk, never the drive's security sector).\n\n" +
                        "Before any sector is changed, what it held is saved in an undo journal, so Undo changes can put it back. " +
                        "Make a full backup first anyway (Back up whole drive...).\n\nWindows asks for administrator permission again.",
                        "Allow changes", true)) return;
                OpenDisk(d, true, keep);
                return;
            }
            string path = _imagePath, volume = _vol.Name;
            if (_writable) { LoadImage(path, false, volume); return; }
            if (!Dlg.Confirm("Allow changes to this image?",
                    Path.GetFileName(path) + " will be opened so files and folders can be added, replaced, renamed and deleted. " +
                    "Changes are written straight into the image file and cannot be undone.\n\nKeep a spare copy of the image before you change it.",
                    "Allow changes", true)) return;
            LoadImage(path, true, volume);
        }

        private bool CanChange()
        {
            if (_vol == null) { Dlg.Info("Drive Explorer", "Open a drive image first."); return false; }
            if (_showingFound) { Dlg.Info("Drive Explorer", "The list shows what Find found, in many folders. Double-click an item to open its folder, then change it there."); return false; }
            if (Waits("Drive Explorer")) return false;
            if (_writable) return true;
            Dlg.Info("Read-only", "Press Allow changes first. Until then nothing on the " + (_imagePath == null ? "drive" : "image") + " can be changed.");
            return false;
        }

        private uint CurrentCluster => _stack.Count > 0 ? _stack.Peek().Cluster : _vol.RootCluster;

        private void Reload()
        {
            if (_vol == null || _stack.Count == 0) return;
            var top = _stack.Peek();
            ShowDir(top.Cluster, top.Name);
        }

        /// <summary>Copies files and folders from this PC into the folder shown.</summary>
        private void AddToDrive(string[] paths)
        {
            if (!CanChange()) return;
            var vol = _vol;
            uint cluster = CurrentCluster;
            var problems = paths.SelectMany(FatxVolume.NameProblemsIn).ToList();
            if (problems.Count > 0)
            {
                Dlg.Warn("Names the Xbox does not accept",
                    "Nothing was copied. Rename these on the PC first (42 characters at most; letters, digits, spaces and ! # $ % & ' ( ) - . @ [ ] ^ _ ` { } ~ only):\n\n" +
                    string.Join("\n", problems.Take(12)) + (problems.Count > 12 ? "\n... and " + (problems.Count - 12) + " more" : ""));
                return;
            }
            var existing = vol.ListDirectory(cluster);
            var clash = paths.Select(p => Path.GetFileName(p.TrimEnd('\\', '/'))).Where(n => existing.Any(x => !x.IsDirectory && string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase))).ToList();
            if (clash.Count > 0 && !Dlg.Confirm("Replace?", "Already in this folder and will be replaced:\n\n" + string.Join("\n", clash.Take(10)) + (clash.Count > 10 ? "\n..." : ""), "Replace", true)) return;
            var cts = new CancellationTokenSource();
            var use = new DriveUse(this, true);
            Run(null, "Copying to the drive image", async () =>
            {
                try
                {
                    using (Status.Begin("Copying " + paths.Length + " item(s) to the drive image", cts))
                    {
                        var progress = Status.NewProgress();
                        await Task.Run(() =>
                        {
                            var state = new TransferProgress { File = "drive image", Total = paths.Sum(FtpService.MeasureLocal) };
                            if (state.Total > vol.FreeBytes)
                                throw new IOException("Not enough free space on the drive: " + FileSniffer.FormatSize(state.Total) + " to copy, " + FileSniffer.FormatSize(vol.FreeBytes) + " free.");
                            Action<long> step = n => { state.Done += n; progress.Report(state); };
                            foreach (string p in paths)
                            {
                                if (Directory.Exists(p)) vol.WriteDirectory(cluster, p, null, step, cts.Token);
                                else vol.WriteFile(cluster, p, null, step, cts.Token);
                            }
                        }, cts.Token);
                        Log.Success("Copied " + paths.Length + " item(s) into " + vol.Name + " " + CurrentPath());
                    }
                }
                finally { use.Dispose(); Reload(); }
            });
        }

        /// <summary>Console Setup's install, with the open drive image as the destination.</summary>
        private void InstallPackages()
        {
            if (!CanChange()) return;
            var picked = PickFiles("Packages to install on this drive (pick files; for a folder of packages use the folder button after this)");
            string[] paths = picked;
            if (paths == null)
            {
                string folder = PickFolder("Or pick a folder: every package found in it is installed");
                if (folder == null) return;
                paths = new[] { folder };
            }
            var vol = _vol;
            var cts = new CancellationTokenSource();
            var use = new DriveUse(this, true);
            Run(null, "Installing to the drive image", async () =>
            {
                try
                {
                    var pkgs = await Task.Run(() => ContentInstaller.Collect(paths));
                    if (pkgs.Count == 0) { Dlg.Info("Install packages", "None of the chosen files is an Xbox 360 package."); return; }
                    using (Status.Begin("Installing " + pkgs.Count + " package(s) to " + vol.Name, cts))
                    {
                        var progress = Status.NewProgress();
                        await Task.Run(() => ContentInstaller.InstallToVolume(pkgs, vol, progress, cts.Token), cts.Token);
                    }
                    _saves.SetStore(new FatxContentStore(vol));
                    _savesScanned = false;
                }
                finally { use.Dispose(); Reload(); }
            });
        }

        private void NewFolder()
        {
            if (!CanChange()) return;
            string name = Dlg.Input("New folder", "Folder name (42 characters at most)", "New folder", false);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            string problem = FatxVolume.NameProblem(name);
            if (problem != null) { Dlg.Warn("New folder", problem); return; }
            var vol = _vol; uint cluster = CurrentCluster;
            Run(null, "Creating folder", () => Holding(new DriveUse(this, true), async () =>
            {
                await Task.Run(() => vol.CreateDirectory(cluster, name));
                Log.Success("Created folder " + name);
                Reload();
            }));
        }

        private void RenameSelected()
        {
            if (!CanChange()) return;
            var e = Selected().FirstOrDefault();
            if (e == null) { Dlg.Info("Rename", "Select a file or folder."); return; }
            string name = Dlg.Input("Rename", "New name (42 characters at most)", e.Name, false);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == e.Name) return;
            name = name.Trim();
            string problem = FatxVolume.NameProblem(name);
            if (problem != null) { Dlg.Warn("Rename", problem); return; }
            var vol = _vol; uint cluster = CurrentCluster; string old = e.Name;
            Run(null, "Renaming", () => Holding(new DriveUse(this, true), async () =>
            {
                await Task.Run(() => vol.Rename(cluster, e, name));
                Log.Success("Renamed " + old + " to " + name);
                Reload();
            }));
        }

        private void DeleteSelected()
        {
            if (!CanChange()) return;
            var items = Selected();
            if (items.Count == 0) { Dlg.Info("Delete", "Select files or folders."); return; }
            if (!Dlg.Confirm("Delete from the drive image?",
                    string.Join("\n", items.Take(10).Select(i => i.Name + (i.IsDirectory ? "  (folder and everything in it)" : ""))) + (items.Count > 10 ? "\n... and " + (items.Count - 10) + " more" : "") +
                    "\n\nThis cannot be undone.", "Delete", true)) return;
            var vol = _vol;
            var use = new DriveUse(this, true);
            Run(null, "Deleting", async () =>
            {
                try
                {
                    await Task.Run(() => { foreach (var e in items) vol.Delete(e); });
                    Log.Success("Deleted " + items.Count + " item(s) from " + vol.Name);
                }
                finally { use.Dispose(); Reload(); }
            });
        }

        /// <summary>Finds what was deleted on the open partition, lets the user tick what to recover and copies it to this PC. Read only on the drive.</summary>
        private void RecoverDeleted()
        {
            if (_vol == null) { Dlg.Info("Recover deleted files", "Open a drive or a drive image first, and pick the partition to search."); return; }
            if (BeingWritten("Recover deleted files")) return;
            var vol = _vol;
            var cts = new CancellationTokenSource();
            Run(null, "Looking for deleted files on " + vol.Name, () => Holding(new DriveUse(this, false), async () =>
            {
                List<FatxDeleted> found;
                using (Status.Begin("Looking for deleted files on " + vol.Name, cts))
                    found = await Task.Run(() => vol.FindDeleted(true, cts.Token), cts.Token);
                var all = FatxVolume.Flatten(found).ToList();
                Log.Info(vol.Name + ": " + all.Count(x => !x.LostChain) + " deleted file(s) / folder(s) found (" + all.Count(x => !x.LostChain && x.State == RecoveryState.Recoverable) + " recoverable), " +
                         all.Count(x => x.LostChain) + " lost data item(s).");
                if (all.Count == 0) { Dlg.Info("Recover deleted files", "Nothing deleted was found on " + vol.Name + "."); return; }
                var pick = RecoverDialog.Choose(vol.Name, found);
                if (pick == null) return;
                string to = PickFolder("Recover to which folder on this PC? (Nothing is written to the Xbox drive.)");
                if (to == null) return;
                var written = new List<string>();
                long bytes = 0;
                int files;
                using (Status.Begin("Recovering " + pick.Count + " item(s) from " + vol.Name, cts))
                    files = await Task.Run(() => vol.RecoverInto(pick, to, n => System.Threading.Interlocked.Add(ref bytes, n), cts.Token, written), cts.Token);
                Log.Success("Recovered " + files + " file(s), " + FileSniffer.FormatSize(bytes) + ", from " + vol.Name + " into " + to);
                Dlg.Info("Recover deleted files", files + " file(s) (" + FileSniffer.FormatSize(bytes) + ") were written to\n" + to + "\n\ninto the folders they were in. The drive was only read.");
                Tools.OpenFolder(to);
            }));
        }

        private void CheckDrive()
        {
            if (_vol == null) { Dlg.Info("Drive Explorer", "Open a drive first."); return; }
            if (BeingWritten("Check drive")) return;
            var vol = _vol;
            var cts = new CancellationTokenSource();
            Run(null, "Checking " + vol.Name, () => Holding(new DriveUse(this, false), async () =>
            {
                FatxCheck c;
                using (Status.Begin("Checking " + vol.Name, cts))
                    c = await Task.Run(() => vol.Check(cts.Token), cts.Token);
                Log.Info(vol.Name + ": " + c);
                foreach (string p in c.Problems.Take(50)) Log.Warn("  " + p);
                string space = "\n\nFree: " + FileSniffer.FormatSize(c.FreeClusters * vol.ClusterSize) + "   Used: " + FileSniffer.FormatSize(c.UsedClusters * vol.ClusterSize) +
                               (c.LostClusters > 0 ? "\n" + FileSniffer.FormatSize(c.LostClusters * vol.ClusterSize) + " is marked used but belongs to no file (wasted space, not damage)." : "");
                if (c.Healthy) Dlg.Info("Check drive", vol.Name + " is consistent.\n\n" + c.Files + " file(s) in " + c.Folders + " folder(s)." + space);
                else Dlg.Warn("Check drive", vol.Name + " has " + c.Problems.Count + " problem(s). Do not change this drive; the first ones are listed in the log below.\n\n" +
                                             string.Join("\n", c.Problems.Take(6)) + space);
            }));
        }

        private string CurrentPath() => "\\" + string.Join("\\", _stack.Reverse().Select(x => x.Name).Skip(1));

        private void ShowDir(uint cluster, string name, string select = null)
        {
            if (_vol == null) return;
            var vol = _vol;
            int gen = ++_listing;
            Run(null, "Listing", async () =>
            {
                var items = await Task.Run(() => vol.ListDirectory(cluster));
                if (gen != _listing) return;
                if (_stack.Count == 0 || _stack.Peek().Cluster != cluster) _stack.Push((cluster, name));
                _showingFound = false;
                _found.Clear();
                _list.BeginUpdate();
                _list.Items.Clear();
                ListViewItem chosen = null;
                foreach (var e in items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var li = Item(e);
                    li.SubItems.Add(e.IsDirectory ? DriveDescriptions.Folder(e.Name, name) : "");
                    _list.Items.Add(li);
                    if (select != null && e.Name == select) chosen = li;
                }
                _list.EndUpdate();
                SortList();
                ShowList(true);
                if (chosen != null) { chosen.Selected = true; chosen.Focused = true; chosen.EnsureVisible(); }
                _pathText = vol.Name + "   " + CurrentPath() + "   " + items.Count + (items.Count == 1 ? " item" : " items");
                _path.Text = _pathText;
                DescribeLater(vol, gen);
            });
        }

        private ListViewItem Item(FatxEntry e)
        {
            int img = e.IsDirectory ? 0 : Path.GetExtension(e.Name).Length == 0 ? 2 : 1;
            var li = new ListViewItem(e.Name, img) { Tag = e };
            li.SubItems.Add(e.IsDirectory ? "" : FileSniffer.FormatSize(e.Size));
            li.SubItems.Add(e.Modified == DateTime.MinValue ? "" : e.Modified.ToString("yyyy-MM-dd HH:mm"));
            return li;
        }

        /// <summary>
        /// After a listing shows: what each package (and program) says it is goes into the "What it is" column, and the
        /// partition's free space onto the path line - read in the background, dropped when another listing came first.
        /// </summary>
        private void DescribeLater(FatxVolume vol, int gen)
        {
            var files = _list.Items.Cast<ListViewItem>().Where(i => i.Tag is FatxEntry e && DriveDescriptions.WorthReading(e)).Take(400)
                             .Select(i => (Item: i, Entry: (FatxEntry)i.Tag)).ToList();
            // an original Xbox drive: a game's folder says the game (its default.xbe), UDATA / TDATA title folders the game
            // (TitleMeta.xbx), a save's folder the save (SaveMeta.xbx) - one folder read each, for up to 300 folders
            var folders = vol.BigEndian ? new List<(ListViewItem Item, FatxEntry Entry)>()
                : _list.Items.Cast<ListViewItem>().Where(i => i.Tag is FatxEntry e && e.IsDirectory && i.SubItems.Count > 3 && i.SubItems[3].Text.Length == 0)
                       .Take(300).Select(i => (Item: i, Entry: (FatxEntry)i.Tag)).ToList();
            Task.Run(() =>
            {
                var done = new List<(ListViewItem, string)>();
                foreach (var f in folders)
                {
                    if (gen != _listing) return;
                    try
                    {
                        string what = "";
                        var inside = vol.ListDirectory(f.Entry.FirstCluster);
                        var xbe = inside.FirstOrDefault(x => !x.IsDirectory && x.Name.Equals("default.xbe", StringComparison.OrdinalIgnoreCase));
                        var title = inside.FirstOrDefault(x => !x.IsDirectory && x.Name.Equals("TitleMeta.xbx", StringComparison.OrdinalIgnoreCase));
                        var save = inside.FirstOrDefault(x => !x.IsDirectory && x.Name.Equals("SaveMeta.xbx", StringComparison.OrdinalIgnoreCase));
                        if (xbe != null) { string t = DriveDescriptions.XbeTitle(vol.ReadHead(xbe, (int)Math.Min(xbe.Size, 0x10000))); if (t.Length > 0) what = t + "   [game]"; }
                        else if (title != null) { string t = DriveDescriptions.XbxValue(vol.ReadHead(title, (int)Math.Min(title.Size, 0x1000)), "TitleName"); if (t.Length > 0) what = t + "   [saves and settings]"; }
                        else if (save != null) { string t = DriveDescriptions.XbxValue(vol.ReadHead(save, (int)Math.Min(save.Size, 0x1000)), "Name"); if (t.Length > 0) what = t + "   [save]"; }
                        if (what.Length > 0) done.Add((f.Item, what));
                    }
                    catch { /* a folder that cannot be read says nothing */ }
                    if (done.Count >= 25) { Apply(done.ToList(), gen); done.Clear(); }
                }
                foreach (var f in files)
                {
                    if (gen != _listing) return;
                    try
                    {
                        string what = DriveDescriptions.File(f.Entry.Name, vol.ReadHead(f.Entry, (int)Math.Min(f.Entry.Size, 0x1800)));
                        if (what.Length > 0) done.Add((f.Item, what));
                    }
                    catch { /* a file that cannot be read says nothing */ }
                    if (done.Count >= 25) { Apply(done.ToList(), gen); done.Clear(); }
                }
                Apply(done, gen);
                long free = -1;
                try { free = vol.CountFreeBytes(); } catch { }
                if (free >= 0) BeginInvokeSafe(() =>
                {
                    if (gen != _listing || _vol != vol) return;
                    _path.Text = _pathText + "   " + FileSniffer.FormatSize(free) + " free";
                });
            });
        }

        private void Apply(List<(ListViewItem Item, string What)> done, int gen)
        {
            if (done.Count == 0) return;
            BeginInvokeSafe(() =>
            {
                if (gen != _listing) return;
                foreach (var d in done) if (d.Item.ListView == _list && d.Item.SubItems.Count > 3) d.Item.SubItems[3].Text = d.What;
            });
        }

        private void BeginInvokeSafe(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        /// <summary>Find: every file and folder on the partition shown whose name holds the text, with the folder it is in.</summary>
        private void FindOnPartition()
        {
            if (_vol == null) { Dlg.Info("Find", "Open a drive or a drive image first."); return; }
            string text = _find.Text.Trim();
            if (text.Length == 0) { Dlg.Info("Find", "Type part of a name to find - for example a Title ID such as 4156081C, default.xex, or a game's folder name."); return; }
            if (BeingWritten("Find")) return;
            var vol = _vol;
            int gen = ++_listing;
            var cts = new System.Threading.CancellationTokenSource();
            Run(null, "Finding", () => Holding(new DriveUse(this, false), async () =>
            {
                List<(FatxEntry Entry, List<(uint, string)> Chain)> hits;
                using (Status.Begin("Finding \"" + text + "\" on " + vol.Name, cts))
                    hits = await Task.Run(() => Search(vol, text, 1000, cts.Token));
                if (gen != _listing) return;
                _list.BeginUpdate();
                _list.Items.Clear();
                _found.Clear();
                foreach (var h in hits)
                {
                    var li = Item(h.Entry);
                    li.SubItems.Add("in \\" + string.Join("\\", h.Chain.Skip(1).Select(c => c.Item2)));
                    _list.Items.Add(li);
                    _found[li] = h.Chain;
                }
                _list.EndUpdate();
                SortList();
                ShowList(true);
                _showingFound = true;
                _pathText = vol.Name + "   " + (hits.Count == 0 ? "nothing" : hits.Count + (hits.Count >= 1000 ? " or more" : "")) + " found for \"" + text + "\"" +
                            (hits.Count > 0 ? " - double-click one to open its folder" : "");
                _path.Text = _pathText;
            }));
        }

        /// <summary>Walks the partition's folders (each once, 64 deep at most) for names holding <paramref name="text"/>.</summary>
        internal static List<(FatxEntry Entry, List<(uint, string)> Chain)> Search(FatxVolume vol, string text, int max, System.Threading.CancellationToken ct)
        {
            var hits = new List<(FatxEntry, List<(uint, string)>)>();
            var seen = new HashSet<uint>();
            void Walk(uint cluster, List<(uint, string)> chain, int depth)
            {
                if (depth > 64 || hits.Count >= max || !seen.Add(cluster)) return;
                ct.ThrowIfCancellationRequested();
                List<FatxEntry> items;
                try { items = vol.ListDirectory(cluster); } catch { return; }
                foreach (var e in items)
                    if (e.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) { hits.Add((e, chain)); if (hits.Count >= max) return; }
                foreach (var e in items.Where(x => x.IsDirectory).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    Walk(e.FirstCluster, new List<(uint, string)>(chain) { (e.FirstCluster, e.Name) }, depth + 1);
            }
            Walk(vol.RootCluster, new List<(uint, string)> { (vol.RootCluster, "") }, 0);
            return hits;
        }

        /// <summary>The right-click menu of the file list: what the buttons do, for what is selected.</summary>
        private ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.Body };
            var open = m.Items.Add("Open", null, (s, e) => OpenSelected());
            var extract = m.Items.Add("Extract...", null, (s, e) => Extract());
            var tool = m.Items.Add("Open in X360GameHack", null, (s, e) => OpenInTool());
            var xenia = m.Items.Add("Play in Xenia", null, (s, e) => PlayInXenia());
            var toXenia = m.Items.Add("Copy to Xenia", null, (s, e) => CopyToXenia());
            var xemu = m.Items.Add("Play in xemu", null, (s, e) => PlayInXemu());
            var sep1 = new ToolStripSeparator();
            m.Items.Add(sep1);
            var rename = m.Items.Add("Rename...", null, (s, e) => RenameSelected());
            rename.ToolTipText = "F2";
            var delete = m.Items.Add("Delete", null, (s, e) => DeleteSelected());
            var folder = m.Items.Add("New folder...", null, (s, e) => NewFolder());
            var sep2 = new ToolStripSeparator();
            m.Items.Add(sep2);
            var path = m.Items.Add("Copy the path", null, (s, e) =>
            {
                var item = _list.SelectedItems.Cast<ListViewItem>().FirstOrDefault();
                if (!(item?.Tag is FatxEntry en)) return;
                string where = _found.TryGetValue(item, out var chain) ? "\\" + string.Join("\\", chain.Skip(1).Select(c => c.Item2)) : CurrentPath();
                try { Clipboard.SetText(_vol?.Name + "  " + where.TrimEnd('\\') + "\\" + en.Name); } catch { }
            });
            var props = m.Items.Add("Properties", null, (s, e) => ShowProperties());
            m.Opening += (s, e) =>
            {
                var sel = Selected();
                if (_vol == null) { e.Cancel = true; return; }
                bool one = sel.Count == 1, file = one && !sel[0].IsDirectory, og = _open.Count > 0 && _open.All(v => !v.BigEndian);
                bool change = _writable && !_showingFound;
                open.Visible = one; tool.Visible = file; extract.Visible = sel.Count > 0;
                xenia.Visible = !og && sel.Count <= 1; toXenia.Visible = !og && sel.Count > 0; xemu.Visible = og && sel.Count <= 1;
                rename.Visible = change && one; delete.Visible = change && sel.Count > 0; folder.Visible = change;
                sep1.Visible = rename.Visible || delete.Visible || folder.Visible;
                path.Visible = one; props.Visible = one; sep2.Visible = path.Visible;
            };
            return m;
        }

        /// <summary>Sorts the list by the chosen column (folders always first); the header shows which, and which way.</summary>
        private void SortList()
        {
            string[] names = { "Name", "Size", "Modified", "What it is" };
            for (int i = 0; i < _list.Columns.Count && i < names.Length; i++)
                _list.Columns[i].Text = names[i] + (i == _sortColumn ? (_sortDesc ? "  \u25BC" : "  \u25B2") : "");
            var items = _list.Items.Cast<ListViewItem>().ToList();
            if (items.Count < 2) return;
            int Compare(ListViewItem a, ListViewItem b)
            {
                var x = a.Tag as FatxEntry; var y = b.Tag as FatxEntry;
                if (x == null || y == null) return 0;
                if (x.IsDirectory != y.IsDirectory) return x.IsDirectory ? -1 : 1;
                int c;
                switch (_sortColumn)
                {
                    case 1: c = x.Size.CompareTo(y.Size); break;
                    case 2: c = x.Modified.CompareTo(y.Modified); break;
                    case 3: c = string.Compare(a.SubItems.Count > 3 ? a.SubItems[3].Text : "", b.SubItems.Count > 3 ? b.SubItems[3].Text : "", StringComparison.OrdinalIgnoreCase); break;
                    default: c = 0; break;
                }
                if (c == 0) c = string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
                return _sortDesc ? -c : c;
            }
            items.Sort(Compare);
            var selected = new HashSet<ListViewItem>(_list.SelectedItems.Cast<ListViewItem>());
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Items.AddRange(items.ToArray());
            foreach (var i in selected) i.Selected = true;
            _list.EndUpdate();
        }

        /// <summary>F5: the folder shown, read again.</summary>
        private void Refresh_()
        {
            if (_vol == null || _stack.Count == 0) return;
            var here = _stack.Peek();
            var sel = _list.SelectedItems.Cast<ListViewItem>().FirstOrDefault()?.Text;
            ShowDir(here.Cluster, here.Name, sel);
        }

        /// <summary>Properties of the selected file or folder.</summary>
        private void ShowProperties()
        {
            var item = _list.SelectedItems.Cast<ListViewItem>().FirstOrDefault();
            if (_vol == null || !(item?.Tag is FatxEntry e)) { Dlg.Info("Properties", "Select a file or folder."); return; }
            var vol = _vol;
            string folder = _found.TryGetValue(item, out var chain) ? "\\" + string.Join("\\", chain.Skip(1).Select(c => c.Item2)) : CurrentPath();
            string what = item.SubItems.Count > 3 ? item.SubItems[3].Text : "";
            Run(null, "Reading " + e.Name, async () =>
            {
                var lines = await Task.Run(() =>
                {
                    var l = new List<string>
                    {
                        "Partition: " + vol.Name,
                        "Folder: " + (folder.Length > 1 ? folder : "\\ (the root)"),
                        "Kind: " + (e.IsDirectory ? "folder" : "file") + (what.Length > 0 && !what.StartsWith("in \\") ? " - " + what : "")
                    };
                    if (e.IsDirectory)
                    {
                        long bytes = vol.MeasureDirectory(e);
                        l.Add("Holds: " + FileSniffer.FormatSize(bytes) + " (" + bytes.ToString("N0") + " bytes, everything in it)");
                    }
                    else
                    {
                        l.Add("Size: " + FileSniffer.FormatSize(e.Size) + " (" + e.Size.ToString("N0") + " bytes)");
                        long clusters = (e.Size + vol.ClusterSize - 1) / vol.ClusterSize;
                        l.Add("Takes: " + clusters.ToString("N0") + " cluster" + (clusters == 1 ? "" : "s") + " of " + vol.ClusterSize / 1024 + " KB, from cluster " + e.FirstCluster);
                    }
                    if (e.Modified != DateTime.MinValue) l.Add("Modified: " + e.Modified.ToString("yyyy-MM-dd HH:mm:ss") + " (this PC's time)");
                    if (!e.IsDirectory && e.Size >= 0x1712)
                    {
                        var h = XeniaContent.ReadHeader(vol.ReadHead(e, 0x1800));
                        if (h != null && !h.Aggregate)
                        {
                            l.Add("");
                            l.Add("Package: " + StfsPackage.TypeName(h.ContentType));
                            if (h.DisplayName.Length > 0) l.Add("Name: " + h.DisplayName);
                            l.Add("Game: " + (h.TitleName.Length > 0 ? h.TitleName + " " : "") + "(Title ID " + h.TitleId.ToString("X8") + ")");
                            l.Add("Profile: " + (h.Xuid == 0 ? "none (any profile can use it)" : h.Xuid.ToString("X16")));
                        }
                    }
                    return l;
                });
                Dlg.Info(e.Name, string.Join("\n", lines));
            });
        }

        private List<FatxEntry> Selected() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (FatxEntry)i.Tag).ToList();

        private void OpenSelected()
        {
            var item = _list.SelectedItems.Cast<ListViewItem>().FirstOrDefault();
            if (!(item?.Tag is FatxEntry e)) return;
            if (_showingFound && _found.TryGetValue(item, out var chain))
            {
                // open the folder it is in, with it selected - the folders above it go on the way back up
                _stack.Clear();
                foreach (var c in chain.Take(chain.Count - 1)) _stack.Push(c);
                ShowDir(chain[chain.Count - 1].Item1, chain[chain.Count - 1].Item2, e.Name);
                return;
            }
            if (e.IsDirectory) ShowDir(e.FirstCluster, e.Name);
            else OpenInTool();
        }

        /// <summary>
        /// Drag to Windows: the selected files and folders dragged out of the list into Explorer, the desktop or any program
        /// that takes dropped files. Windows copies them straight off the drive with its own progress window (DragOutData);
        /// folders come with everything in them. The drive is only read.
        /// </summary>
        private void DragOut()
        {
            if (_vol == null || _list.SelectedItems.Count == 0) return;
            if (BeingWritten("Drag to Windows")) return;
            DragOutData data;
            var was = Cursor.Current;
            try { Cursor.Current = Cursors.WaitCursor; data = DragData(); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException) { Dlg.Info("Drag to Windows", ex.Message); return; }
            finally { Cursor.Current = was; }
            DragDropEffects done;
            using (new DriveUse(this, false, true))
                done = _list.DoDragDrop(data, DragDropEffects.Copy);
            if (done == DragDropEffects.Copy && !data.StartedAsync)
                Log.Success("Copied " + data.FileCount + " file(s) (" + FileSniffer.FormatSize(data.Bytes) + ") off the drive to where they were dropped.");
        }

        /// <summary>
        /// What a drag of the selection hands Windows, wired so the drive stays open (and the status bar says so) while
        /// Windows copies in the background. Reads the selected folders.
        /// </summary>
        internal DragOutData DragData()
        {
            var vol = _vol;
            var data = DragOutData.Build(vol, Selected());
            DriveUse use = null;
            IDisposable status = null;
            data.Started += () =>
            {
                use = new DriveUse(this, false, true);
                status = Status.Begin("Windows is copying " + data.FileCount + " file(s), " + FileSniffer.FormatSize(data.Bytes) + ", off " + vol.Name.Split(' ')[0] + " - see its progress window");
            };
            data.Ended += ok =>
            {
                status?.Dispose(); use?.Dispose();
                status = null; use = null;
                if (ok) Log.Success("Windows copied " + data.FileCount + " file(s) (" + FileSniffer.FormatSize(data.Bytes) + ") off " + vol.Name + ".");
                else Log.Warn("Windows stopped copying files off " + vol.Name + " - cancelled in its window, or a file could not be read.");
            };
            return data;
        }

        /// <summary>
        /// The partition a drive opens on: an Xbox 360 drive's Data partition; an original Xbox drive's F: (where the games
        /// are) when it has one, else E:.
        /// </summary>
        internal static int DefaultVolume(List<FatxVolume> vols)
        {
            int i = vols.FindIndex(v => v.Name.StartsWith("Content"));
            if (i < 0) i = vols.FindIndex(v => v.Name.StartsWith("F:"));
            if (i < 0) i = vols.FindIndex(v => v.Name.StartsWith("E:"));
            return i < 0 ? 0 : i;
        }

        /// <summary>
        /// Files dropped on the list. With a drive open for changes they are copied into the folder shown; with a drive open
        /// read-only the drive stays open and it says how to copy onto it; only a drive image opens as a drive (and an image
        /// never replaces the open drive by accident: that waits like any other change of drive).
        /// </summary>
        internal void DropOnList(string[] f)
        {
            if (_vol != null && _writable) { AddToDrive(f); return; }
            bool image = f.Length == 1 && File.Exists(f[0]) && LooksLikeDriveImage(f[0]);
            if (_vol != null && !image)
            {
                Dlg.Info("Copy onto the drive", "This drive is open read-only, so nothing was copied and it stays open.\n\n" +
                         "To copy files onto it, press Allow changes first, then drop them here again." +
                         (_disk != null ? " Every change to a real drive is journaled, so Drive tools > Undo changes can put it back." : ""));
                return;
            }
            if (!image)
            {
                Dlg.Info("Drive Explorer", "That is not an Xbox drive image. Open a drive first (a drive image, or Open physical drive...) - " +
                         "then press Allow changes and drop files here to copy them onto it.");
                return;
            }
            if (Idle("Open image")) LoadImage(f[0]);
        }

        private static bool LooksLikeDriveImage(string path)
        {
            try
            {
                var head = new byte[0x1000];
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    fs.Read(head, 0, head.Length);
                return FileSniffer.IsDriveImage(path, head);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
        }

        private void GoUp()
        {
            if (_showingFound && _stack.Count > 0) { var here = _stack.Peek(); ShowDir(here.Cluster, here.Name); return; }
            if (_stack.Count <= 1) return;
            _stack.Pop();
            var top = _stack.Pop();
            ShowDir(top.Cluster, top.Name);
        }

        private void Extract()
        {
            var items = Selected();
            if (_vol == null || items.Count == 0) { Dlg.Info("Extract", "Open an image and select files or folders."); return; }
            if (BeingWritten("Extract")) return;
            string dir = PickFolder("Extract to which folder?");
            if (dir == null) return;
            var vol = _vol;
            var cts = new CancellationTokenSource();
            Run(null, "Extracting", () => Holding(new DriveUse(this, false), async () =>
            {
                using (Status.Begin("Extracting " + items.Count + " item(s)", cts))
                {
                    var progress = Status.NewProgress();
                    await Task.Run(() =>
                    {
                        var state = new TransferProgress { File = "drive image" };
                        foreach (var e in items) state.Total += e.IsDirectory ? vol.MeasureDirectory(e) : e.Size;
                        Action<long> step = n => { state.Done += n; progress.Report(state); };
                        foreach (var e in items)
                        {
                            string target = Path.Combine(dir, FtpService.SafeLocalName(e.Name));
                            if (e.IsDirectory) vol.ExtractDirectory(e, target, step, cts.Token);
                            else vol.ExtractFile(e, target, step, cts.Token);
                        }
                    }, cts.Token);
                    Log.Success("Extracted " + items.Count + " item(s) to " + dir);
                }
            }));
        }

        /// <summary>
        /// The selected files and folders into Xenia, each where Xenia keeps it: downloadable content and title updates as
        /// they are, saves and profiles unpacked the way Xenia's own installer does (the same as the Xenia page's Add
        /// buttons). They are copied off the drive into a temporary folder first - the drive is only read - and that copy is
        /// removed afterwards. A game itself goes in with Play in Xenia.
        /// </summary>
        private void CopyToXenia()
        {
            var items = Selected();
            if (_vol == null || items.Count == 0) { Dlg.Info("Copy to Xenia", "Open a drive and select what to copy: DLC, title updates, saves or profiles - or a folder holding them."); return; }
            if (!XeniaTransfer.CheckReady("Copy to Xenia")) return;
            if (BeingWritten("Copy to Xenia")) return;
            var vol = _vol;
            long need = 0;
            try { foreach (var e in items) need += e.IsDirectory ? vol.MeasureDirectory(e) : e.Size; }
            catch (Exception ex) { Dlg.Error("Copy to Xenia", "Could not read the selection: " + ex.Message); return; }
            string temp = Path.Combine(Path.GetTempPath(), "X360GameHack", "to-xenia-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(temp))).AvailableFreeSpace;
            if (free < need * 2 + (512L << 20))
            {
                Dlg.Warn("Copy to Xenia", "That is " + FileSniffer.FormatSize(need) + "; it needs about " + FileSniffer.FormatSize(need * 2) + " free on this PC for a moment (a copy to read from, and Xenia's), and " +
                                          FileSniffer.FormatSize(free) + " is free. Free some space or select less.");
                return;
            }
            var cts = new CancellationTokenSource();
            var abort = _diskStream?.AbortOn(cts.Token);         // Cancel (or a drive that stops answering) abandons a blocked read at once
            var use = new DriveUse(this, false);
            Run(null, "Copying to Xenia", async () =>
            {
                bool read = false;
                try
                {
                    using (Status.Begin("Copying " + items.Count + " item(s) off the drive for Xenia", cts))
                    {
                        var progress = Status.NewProgress();
                        await Task.Run(() =>
                        {
                            Directory.CreateDirectory(temp);
                            var state = new TransferProgress { Total = need, File = "drive" };
                            Action<long> step = n => { state.Done += n; progress.Report(state); };
                            foreach (var e in items)
                            {
                                string target = Path.Combine(temp, FtpService.SafeLocalName(e.Name));
                                if (e.IsDirectory) vol.ExtractDirectory(e, target, step, cts.Token);
                                else vol.ExtractFile(e, target, step, cts.Token);
                            }
                        }, cts.Token);
                        read = true;
                    }
                }
                catch (OperationCanceledException) { Log.Warn("Copy to Xenia cancelled - nothing went into Xenia."); }
                catch (Exception ex) { Log.Error("Copy to Xenia failed: " + ex.Message); Dlg.Error("Copy to Xenia", ex.Message); }
                finally { abort?.Dispose(); use.Dispose(); }
                try
                {
                    if (read) await XeniaTransfer.AddAsync(new[] { temp }, null, "Copy to Xenia",
                                                           "No DLC, title updates, saves or profiles were found in what you selected. (A game goes into Xenia with Play in Xenia.)");
                }
                finally
                {
                    try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    cts.Dispose();
                }
                if (_diskStream != null && _diskStream.Faulted)
                {
                    Close();
                    Log.Warn("The drive connection was closed. Press \"Open physical drive\" to open it again.");
                }
            });
        }

        /// <summary>
        /// One click, as on the Neighborhood page: copy the selected game from the Xbox drive to this PC (only what is
        /// not here yet) and start it in Xenia. Xenia cannot read an Xbox-formatted drive itself.
        /// </summary>
        private async void PlayInXenia()
        {
            if (_vol == null) { Dlg.Info("Play in Xenia", "Open an Xbox drive or drive image first."); return; }
            var sel = Selected();
            if (sel.Count > 1) { Dlg.Info("Play in Xenia", "Select one game: its folder, its .xex file, or its package file."); return; }
            if (BeingWritten("Play in Xenia")) return;
            string xenia = Tools.Xenia;
            if (string.IsNullOrWhiteSpace(xenia) || !File.Exists(xenia)) { Dlg.Info("Play in Xenia", "Xenia was not found. Set its path on the Xenia page (Setup tab)."); return; }
            var fs = new FatxFs(_vol);
            string dir = CurrentPath();
            var e = sel.FirstOrDefault();
            var item = e == null ? null : new RemoteItem { Name = e.Name, IsDirectory = e.IsDirectory, Size = e.Size, Modified = e.Modified };
            var cts = new CancellationTokenSource();
            XeniaPlan plan = null;
            var abort = _diskStream?.AbortOn(cts.Token);     // Cancel (or a drive that stops answering) abandons a blocked read at once
            var use = new DriveUse(this, false);
            using (Status.Begin("Play in Xenia", cts))
            {
                try
                {
                    Status.Say("Play in Xenia:  looking at " + (item?.Name ?? "the game") + " on the drive (working out what to copy)...");
                    var planClock = System.Diagnostics.Stopwatch.StartNew();
                    long planLast = -1000;
                    plan = await XeniaPlay.PlanAsync(fs, dir, item, XeniaPlay.GamesDir, cts.Token, pl =>
                    {
                        if (planClock.ElapsedMilliseconds - planLast < 200) return;
                        planLast = planClock.ElapsedMilliseconds;
                        Status.Say("Play in Xenia:  checking " + pl.Name + " on the drive  -  " + pl.Files + " files (" + FileSniffer.FormatSize(pl.TotalBytes) + ") found, " +
                                   (pl.Missing.Count == 0 ? "all already here" : pl.Missing.Count + " still to copy") + "...");
                    });
                    string problem = XeniaPlay.SpaceProblem(plan);
                    if (problem != null) throw new InvalidOperationException(problem);
                    if (plan.Missing.Count == 0) Log.Info(plan.Name + " is already on this PC (" + plan.Files + " files, " + FileSniffer.FormatSize(plan.TotalBytes) + ").");
                    else
                    {
                        // Xenia cannot read an Xbox-formatted (FATX) drive, so the game is copied to the PC once. For a big game
                        // that is a real wait - say so up front rather than surprising them with a multi-GB copy.
                        if (plan.MissingBytes > 1L * 1024 * 1024 * 1024 &&
                            !Dlg.Confirm("Play in Xenia",
                                "Xenia can't read an Xbox-formatted drive directly, so " + plan.Name + " has to be copied to this PC first.\n\n" +
                                FileSniffer.FormatSize(plan.MissingBytes) + " in " + plan.Missing.Count + " file(s) will be copied once - after that it starts straight away, and only new files are copied if you play it again.\n\nCopy it now and play?",
                                "Copy and play"))
                            throw new OperationCanceledException();
                        Log.Info("Copying " + plan.Name + " to " + plan.LocalRoot + ": " + plan.Missing.Count + " of " + plan.Files + " file(s), " + FileSniffer.FormatSize(plan.MissingBytes) + ". Xenia starts once it is here.");
                        Status.Say("Copying " + plan.Name + " to this PC  -  " + FileSniffer.FormatSize(plan.MissingBytes) + " in " + plan.Missing.Count + " file(s)...", 0);
                        await XeniaPlay.FetchAsync(fs, plan, CopyProgress(plan), cts.Token);
                    }
                    string notGame = XeniaPlay.NotAGame(plan);
                    if (notGame != null) throw new InvalidOperationException(notGame);
                    Status.Say("Starting " + plan.Name + " in Xenia...");
                }
                catch (OperationCanceledException) { Log.Warn("Play in Xenia - cancelled. What was copied is kept; press it again to carry on."); plan = null; }
                catch (Exception ex) { Log.Error("Play in Xenia failed: " + ex.Message); Dlg.Error("Play in Xenia", ex.Message); plan = null; }
                finally { abort?.Dispose(); cts.Dispose(); use.Dispose(); }
            }
            // a cancel or a drive timeout closes the disk connection to unblock it - reset the drive view so it can be reopened
            if (_diskStream != null && _diskStream.Faulted)
            {
                Close();
                Log.Warn("The drive connection was closed. Press \"Open physical drive\" to carry on - whatever was copied is kept.");
                return;
            }
            if (plan == null) return;
            if (XeniaHost.Start(plan.LaunchPath)) { XeniaLibrary.MarkPlayed(plan.LaunchPath); Log.Success("Started " + plan.Name + " in Xenia from " + plan.LaunchPath + " - it is on the Xenia page's Games tab from now on."); }
        }

        /// <summary>
        /// Play in xemu: the selected original Xbox game is copied to this PC once (only what is not here yet), gets its disc
        /// image beside it (made again only when the folder changed) and xemu starts it. The drive is only read.
        /// </summary>
        private async void PlayInXemu()
        {
            if (_vol == null) { Dlg.Info("Play in xemu", "Open an original Xbox drive or drive image first."); return; }
            var sel = Selected();
            if (sel.Count > 1) { Dlg.Info("Play in xemu", "Select one game: its folder, its default.xbe, or its disc image."); return; }
            if (BeingWritten("Play in xemu")) return;
            if (XemuPlay.Exe == null)
            {
                string f = PickFile("Where is xemu.exe? (it is asked once)", "xemu|xemu*.exe|Programs|*.exe");
                if (f == null) return;
                AppSettings.Current.XemuPath = f;
                AppSettings.Save();
            }
            var fs = new FatxFs(_vol);
            string dir = CurrentPath();
            var e = sel.FirstOrDefault();
            var item = e == null ? null : new RemoteItem { Name = e.Name, IsDirectory = e.IsDirectory, Size = e.Size, Modified = e.Modified };
            var cts = new CancellationTokenSource();
            string iso = null, name = null;
            var abort = _diskStream?.AbortOn(cts.Token);
            var use = new DriveUse(this, false);
            using (Status.Begin("Play in xemu", cts))
            {
                try
                {
                    Status.Say("Play in xemu:  looking at " + (item?.Name ?? "the game") + " on the drive (working out what to copy)...");
                    var plan = await XeniaPlay.PlanAsync(fs, dir, item, XemuPlay.GamesDir, cts.Token, null, originalXbox: true);
                    name = plan.Name;
                    bool image = plan.LaunchPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase);
                    // room for what is still to copy and, for a game's folder, its disc image (about as big as the game)
                    long need = plan.MissingBytes + (image ? 0 : plan.TotalBytes) + XeniaPlay.SpareBytes;
                    try
                    {
                        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(plan.LocalRoot)));
                        if (drive.AvailableFreeSpace < need)
                            throw new InvalidOperationException("Not enough room on " + drive.Name + " for " + plan.Name + ": it needs " + FileSniffer.FormatSize(need) +
                                                                (image ? "" : " (the game and its disc image)") + " and " + FileSniffer.FormatSize(drive.AvailableFreeSpace) + " is free.");
                    }
                    catch (ArgumentException) { }
                    if (plan.Missing.Count > 0)
                    {
                        if (plan.MissingBytes > 1L * 1024 * 1024 * 1024 &&
                            !Dlg.Confirm("Play in xemu", "xemu can't read an Xbox-formatted drive, so " + plan.Name + " is copied to this PC first: " + FileSniffer.FormatSize(plan.MissingBytes) +
                                         " in " + plan.Missing.Count + " file(s), once - after that it starts straight away.\n\nCopy it now and play?", "Copy and play"))
                            throw new OperationCanceledException();
                        Log.Info("Copying " + plan.Name + " to " + plan.LocalRoot + ": " + plan.Missing.Count + " of " + plan.Files + " file(s), " + FileSniffer.FormatSize(plan.MissingBytes) + ".");
                        await XeniaPlay.FetchAsync(fs, plan, CopyProgress(plan), cts.Token);
                    }
                    else Log.Info(plan.Name + " is already on this PC (" + plan.Files + " files, " + FileSniffer.FormatSize(plan.TotalBytes) + ").");
                    if (image) iso = plan.LaunchPath;
                    else
                    {
                        Status.Say("Play in xemu:  making the disc image of " + plan.Name + " (xemu plays XISO images)...", 0);
                        var progress = Status.NewProgress();
                        iso = await Task.Run(() => XemuPlay.IsoFor(plan.LocalRoot, progress, cts.Token));
                    }
                }
                catch (OperationCanceledException) { Log.Warn("Play in xemu - cancelled. What was copied is kept; press it again to carry on."); iso = null; }
                catch (Exception ex) { Log.Error("Play in xemu failed: " + ex.Message); Dlg.Error("Play in xemu", ex.Message); iso = null; }
                finally { abort?.Dispose(); cts.Dispose(); use.Dispose(); }
            }
            if (_diskStream != null && _diskStream.Faulted)
            {
                Close();
                Log.Warn("The drive connection was closed. Press \"Open physical drive\" to carry on - whatever was copied is kept.");
                return;
            }
            if (iso == null) return;
            string why = XemuPlay.Start(iso);
            if (why != null) { Dlg.Warn("Play in xemu", why); return; }
            Log.Success("Started " + name + " in xemu from " + iso + " (xemu uses its own boot ROM, BIOS and hard drive image).");
        }

        /// <summary>Status-bar progress for the copy: game name, how far along in GB, the current file, the current speed and a
        /// rough time left worked out from the <em>recent</em> rate (not the average since the start, which a slow start skews).</summary>
        private static IProgress<TransferProgress> CopyProgress(XeniaPlan plan)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastMs = -1000;
            var window = new Queue<(long ms, long done)>();   // (time, bytes) samples over the last few seconds
            return new Progress<TransferProgress>(p =>
            {
                long now = sw.ElapsedMilliseconds;
                if (now - lastMs < 250 && p.Done < p.Total) return;
                lastMs = now;
                window.Enqueue((now, p.Done));
                while (window.Count > 2 && now - window.Peek().ms > 5000) window.Dequeue();
                int pct = p.Total > 0 ? (int)(p.Done * 100 / p.Total) : 0;
                string rate = "", eta = "";
                var first = window.Peek();
                double secs = (now - first.ms) / 1000.0;
                if (secs > 0.5)
                {
                    double bps = (p.Done - first.done) / secs;      // bytes per second over the recent window
                    if (bps > 1)
                    {
                        rate = "  ·  " + FileSniffer.FormatSize((long)bps) + "/s";
                        if (p.Done < p.Total) eta = "  ·  about " + FormatDuration((int)((p.Total - p.Done) / bps)) + " left";
                    }
                }
                string file = string.IsNullOrEmpty(p.File) ? "" : "  ·  " + System.IO.Path.GetFileName(p.File);
                Status.Say("Copying " + plan.Name + "  -  " + FileSniffer.FormatSize(p.Done) + " of " + FileSniffer.FormatSize(p.Total) + " (" + pct + "%)" + rate + file + eta, pct);
            });
        }

        private static string FormatDuration(int seconds)
        {
            if (seconds < 60) return seconds + " sec";
            if (seconds < 3600) return (seconds / 60) + " min" + (seconds % 60 >= 30 ? " 30 sec" : "");
            return (seconds / 3600) + " hr " + ((seconds % 3600) / 60) + " min";
        }

        /// <summary>Copies a file to a temp folder and opens it as the working file (XEX, package, save...).</summary>
        private void OpenInTool()
        {
            var e = Selected().FirstOrDefault(x => !x.IsDirectory);
            if (_vol == null || e == null) { Dlg.Info("Open", "Select a file."); return; }
            if (BeingWritten("Open")) return;
            var vol = _vol;
            Run(null, "Opening " + e.Name, () => Holding(new DriveUse(this, false), async () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "X360GameHack", "drive");
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, FtpService.SafeLocalName(e.Name));
                await Task.Run(() => vol.ExtractFile(e, target, null, CancellationToken.None));
                Log.Info("Extracted a temporary copy: " + target);
                WorkingFile.Set(target);
            }));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Close();
            base.Dispose(disposing);
        }
    }
}
