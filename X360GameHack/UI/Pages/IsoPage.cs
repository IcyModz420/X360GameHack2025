using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public sealed partial class IsoPage : PageBase
    {
        public override string Title => "Xbox ISOs";
        public override string Subtitle => "Browse, extract, create and optimize Xbox / Xbox 360 ISOs with the built-in XISO engine.";
        public override string PageGlyph => Glyph.Disc;

        private readonly KvGrid _info = new KvGrid();
        private readonly VStack _bulkTab;
        /// <summary>Shows the Bulk extract tab (the Home page's "Bulk extract ISOs" tile).</summary>
        public void ShowBulk() { if (_bulkTab != null) ShowSection(_bulkTab); }
        private readonly Card _single; private TextBlock _noImage;
        private readonly Card _browseCard;
        private readonly ListView _browse = ListStyle.Create(("Name", 300), ("Size", 100), ("Type", 110), ("Sector", 80), ("Offset", 110), ("Attributes", 150));
        private readonly TextBlock _browsePath = new TextBlock("", Theme.Mono, Theme.TextDim) { Height = Theme.S(24) };
        private readonly XTextBox _browseFind = new XTextBox("Find in the whole image (part of a name, e.g. .xex)") { Width = Theme.S(320) };
        private readonly ImageList _icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
        private readonly Stack<(List<XisoEntry> Items, string Name)> _browseStack = new Stack<(List<XisoEntry>, string)>();
        private string _browseIso;
        private long _browsePartition;
        private List<XisoEntry> _browseRoot;

        private readonly ListView _list = ListStyle.Create(("Image", 320), ("Size", 90), ("Status", 220));
        private readonly XTextBox _dest = new XTextBox("Folder that will receive one sub-folder per game");
        private readonly XCheck _skipUpdate = new XCheck("Skip $SystemUpdate", true, "Never install disc system updates on a modded console.");
        private readonly XCheck _backupXex = new XCheck("Back up original XEX files", true);
        private readonly XCheck _fixTu = new XCheck("Fix TU-patched XEX (-u)", false);
        private readonly Segmented _patch = new Segmented("Don't patch", "RGH / retail", "Devkit", "Custom");
        private readonly XTextBox _customArgs = new XTextBox("-r mrbdiyvklc -m r", false, true) { Width = Theme.S(220) };
        private readonly XCheck _installContent = new XCheck("Install content packages found on the discs to the USB drive", false,
            "Scans each extracted disc for STFS packages (DLC / install content on multi-disc games) and installs them to the USB drive chosen in Console Setup.");
        private readonly XCheck _mediaPatch = new XCheck("Patch XBE media check (same as extract-xiso)", true,
            "Applies extract-xiso's media-enable patch to .xbe files so original Xbox games boot from HDD.");
        private readonly XButton _start;
        private readonly AbgxCard _abgx;
        /// <summary>Shows the abgx360 tab.</summary>
        public void ShowAbgx() { if (_abgx != null) { _abgx.UseOpenFile(); ShowSection((VStack)_abgx.Parent); } }

        public IsoPage()
        {
            var s = AppSettings.Current;
            int ic = Theme.S(18);
            _icons.ImageSize = new System.Drawing.Size(ic, ic);
            _icons.Images.Add(GlyphImage.Render(Glyph.Folder, Theme.FolderIcon, ic));
            _icons.Images.Add(GlyphImage.Render(Glyph.Document, Theme.TextDim, ic));
            _icons.Images.Add(GlyphImage.Render(Glyph.Code, Theme.AccentHover, ic));
            _icons.Images.Add(GlyphImage.Render(Glyph.Game, Theme.AccentHover, ic));

            Into = AddSection("This image", Glyph.Disc);
            _noImage = Note(Into, "Open or drop a disc image (.iso) to look inside it, extract files or swap files for a mod test. The other tabs work without one.", Theme.TextDim);
            _single = AddCard("This disc image", null, Glyph.Disc);
            _single.Add(_info);
            Buttons(_single,
                Btn("Extract...", Glyph.Download, ExtractSingle, BtnKind.Primary),
                Btn("Add to bulk list", Glyph.Add, () => AddIsos(new[] { WorkingFile.Path })),
                Btn("Optimize / rewrite...", Glyph.Speed, Optimize, tip: "Rebuilds a compact XISO (redump -> XISO). Byte-identical to extract-xiso -r."),
                Btn("Convert to Games on Demand", Glyph.Package, ConvertToGod, tip: "Builds a Games on Demand package from this image (Convert tab, with the options set there) - every block hashed and checked."),
                Btn("Play in Xenia", Glyph.Play, () => PlayInXenia(WorkingFile.Path), tip: "Starts this image in Xenia as it is - the untouched retail disc, nothing patched."));

            // ---------------- browser (replaces Xbox Image Browser)
            _browseCard = AddCard("Browse contents",
                "Every file and folder on the disc, where it sits (sector and byte offset) and its attributes - as Xbox Image Browser shows them. Double-click a folder to open it; " +
                "Find searches the whole image. Works for a Games on Demand package too: its disc is read straight from the package (Convert tab > right-click > Browse contents).", Glyph.FolderOpen);
            _browseCard.AddAction("Up", Glyph.Up, (o, e) => BrowseUp());
            _browseCard.AddAction("Properties", Glyph.Info, (o, e) => BrowseProperties());
            _browseCard.Add(_browsePath);
            _browsePath.Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            Field(_browseCard, "Find", _browseFind, Btn("Clear", Glyph.Clear, () => _browseFind.Text = "")).FillChild = null;
            _browseFind.TextChanged += (o, e) => FillBrowser();
            _browse.SmallImageList = _icons;
            _browse.Height = Theme.S(300);
            _browse.Columns[1].TextAlign = HorizontalAlignment.Right;
            _browse.Columns[3].TextAlign = HorizontalAlignment.Right;
            _browse.DoubleClick += (o, e) => BrowseOpen();
            _browse.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) BrowseOpen(); else if (e.KeyCode == Keys.Back) BrowseUp(); };
            var bmenu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.Body };
            bmenu.Items.Add("Open", null, (o, e) => BrowseOpen());
            bmenu.Items.Add("Extract...", null, (o, e) => BrowseExtract());
            bmenu.Items.Add("Open in X360GameHack", null, (o, e) => BrowseOpenInTool());
            bmenu.Items.Add("Swap it for a file of mine...", null, (o, e) => SwapSelected());
            bmenu.Items.Add("Properties", null, (o, e) => BrowseProperties());
            bmenu.Items.Add("Copy the path", null, (o, e) => { var x = BrowseSelected().FirstOrDefault(); if (x != null) Clipboard.SetText(x.RelativePath); });
            _browse.ContextMenuStrip = bmenu;
            _browseCard.Add(_browse);
            Buttons(_browseCard,
                Btn("Extract selected...", Glyph.Download, BrowseExtract),
                Btn("Extract everything...", Glyph.Download, BrowseExtractAll),
                Btn("Open in X360GameHack", Glyph.OpenFile, BrowseOpenInTool, BtnKind.Primary));

            // ---------------- testing a mod inside the image
            var swap = AddCard("Test a mod: swap files inside the image",
                "Makes a new image in which the files you choose hold your changed versions. Everything else on the disc stays as it is, and your original image is only read.", Glyph.Edit);
            Buttons(swap,
                Btn("Swap the selected file...", Glyph.Edit, SwapSelected, BtnKind.Primary, "Select one file in the list above, then choose the file on this PC that takes its place."),
                Btn("Swap files from a folder...", Glyph.Folder, SwapFolder, tip: "A folder laid out like the disc that holds only the files you changed (for example media\\movie.bik)."),
                Btn("Play the new image in Xenia", Glyph.Play, () => { if (_modded == null || !File.Exists(_modded)) Dlg.Info("Xenia", "Swap a file first - the new image is started from here."); else PlayInXenia(_modded); }));
            Note(swap,
                "1. Open the disc image (drop it anywhere).  2. Swap the file(s).  3. Play the new image in Xenia.\n" +
                "The new image is an ordinary retail-layout disc image, so the game loads its files exactly as from the real disc. The replacement may be smaller or larger than the original.");

            // ---------------- bulk
            Into = _bulkTab = AddSection("Bulk extract", Glyph.List);
            var bulk = AddCard("Bulk extract + patch", "Drop ISOs on the list. Each one is extracted to its own folder and every XEX in the root is patched.", Glyph.Library);
            bulk.AddAction("Add ISOs", Glyph.Add, (o, e) => { var f = PickFiles("Add disc images", "Disc images|*.iso;*.xiso|All files|*.*"); if (f != null) AddIsos(f); });
            bulk.AddAction("Remove", Glyph.Remove, (o, e) => { foreach (ListViewItem i in _list.SelectedItems.Cast<ListViewItem>().ToList()) _list.Items.Remove(i); });
            bulk.AddAction("Clear", Glyph.Clear, (o, e) => _list.Items.Clear());
            _list.Height = Theme.S(220);
            _list.AllowDrop = true;
            _list.DragEnter += (o, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            _list.DragDrop += (o, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f) AddIsos(f); };
            _list.KeyDown += (o, e) => { if (e.KeyCode == Keys.Delete) foreach (ListViewItem i in _list.SelectedItems.Cast<ListViewItem>().ToList()) _list.Items.Remove(i); };
            bulk.Add(_list);

            _dest.Text = s.ExtractPath;
            Field(bulk, "Destination", _dest, Btn("", Glyph.FolderOpen, () => { var d = PickFolder("Choose the extraction folder", _dest.Text); if (d != null) _dest.Text = d; }));
            _skipUpdate.Checked = s.ExcludeSystemUpdate;
            _fixTu.Checked = s.BulkPatchTitleUpdate;
            _patch.SelectedIndex = s.BulkUseCustom ? 3 : s.BulkPatchXdk ? 2 : s.BulkPatchRgh ? 1 : 0;
            _customArgs.Text = s.BulkCustomCommand;
            _installContent.Checked = s.BulkInstallContent;
            var patchRow = Field(bulk, "Patch XEX", _patch, _customArgs);
            patchRow.FillChild = null;
            _customArgs.Visible = _patch.SelectedIndex == 3;
            _patch.SelectedIndexChanged += (o, e) => { _customArgs.Visible = _patch.SelectedIndex == 3; patchRow.PerformLayout(); };
            var opts = new WrapPanel();
            opts.Add(_skipUpdate); opts.Add(_fixTu); opts.Add(_backupXex);
            bulk.Add(opts);
            bulk.Add(_installContent);
            _start = Btn("Extract all", Glyph.Play, null, BtnKind.Primary);
            _start.Click += (o, e) => StartBulk(_list.Items.Cast<ListViewItem>().ToList());
            Buttons(bulk, _start, Btn("Open destination", Glyph.FolderOpen, () => Tools.OpenFolder(_dest.Text)));

            // ---------------- Games on Demand both ways (ISO2God / god2iso, built in)
            BuildGodTabs();

            // ---------------- create + tools
            Into = AddSection("Create & convert", Glyph.Package);
            var cols = AddColumns(2, 420);
            var create = AddCard("Create XISO from folder", "Builds an XISO from an extracted game folder - byte-identical to extract-xiso -c.", Glyph.Add, cols);
            create.Add(_mediaPatch);
            Buttons(create, Btn("Choose folder...", Glyph.Folder, CreateIso, BtnKind.Primary));

            var tools = AddCard("Third-party tools", null, Glyph.Wrench, cols);
            Buttons(tools,
                Btn("ISO2GOD", Glyph.Package, () => Tools.Launch(Tools.Iso2God)),
                Btn("Xbox Backup Creator", Glyph.Disc, LaunchXbbc),
                Btn("abgx360 GUI (original)", Glyph.Shield, () =>
                {
                    if (Tools.AbgxGui != null) Tools.Launch(Tools.AbgxGui);
                    else Dlg.Warn("abgx360", "abgx360 is not installed. Use the abgx360 tab's \"Set up abgx360\" first.");
                }));
            Note(tools, "abgx360 and Xbox Image Browser are built in: the abgx360 tab, and \"Browse contents\" on This image (no OCX registration, no admin rights).");

            // ---------------- abgx360, built in
            Into = AddSection("abgx360", Glyph.Shield);
            _abgx = new AbgxCard();
            Into.Controls.Add(_abgx);

            OnWorkingFileChanged();
        }

        // ------------------------------------------------------------- single image + browser

        public override void OnWorkingFileChanged()
        {
            _abgx?.UseOpenFile();
            bool has = WorkingFile.Kind == WorkingFileKind.Iso;
            _single.Visible = has; if (_noImage != null) _noImage.Visible = !has;
            _browseCard.Visible = has;
            if (!has) { _browseIso = null; return; }
            _single.Title = WorkingFile.Name;
            LoadBrowser(WorkingFile.Path, true);
        }

        /// <summary>
        /// Shows any disc image, or a Games on Demand package's disc (read through its hash-checked blocks), in "Browse contents"
        /// - Xbox Image Browser's job - without making it the open file.
        /// </summary>
        public void BrowseSource(string path)
        {
            ShowSection((VStack)_browseCard.Parent);
            _noImage.Visible = false;
            _browseCard.Visible = true;
            _single.Visible = true;
            _single.Title = Path.GetFileName(path.TrimEnd('\\'));
            LoadBrowser(path, false);
        }

        private void LoadBrowser(string path, bool workingFile)
        {
            Run(null, "Reading disc image", async () =>
            {
                var r = await Task.Run(() =>
                {
                    using (var img = GameSources.OpenImage(path))
                    {
                        var root = img.ReadRoot();
                        GodTitleInfo info = null;
                        try { info = GodTitleInfo.FromImage(path, CancellationToken.None); } catch { }
                        return (img.Layout, img.Optimized, img.PartitionOffset, root, Count(root), XisoImage.TotalBytes(root, false), img.RootSector, img.RootSize, img.Length, info);
                    }
                });
                if (workingFile && path != WorkingFile.Path) return;
                var format = GameSources.FormatOf(path);
                _info.Clear();
                _info.Set("Path", path);
                _info.Set("Form", GameSources.Describe(format) + (format == GameFormat.Package ? " (its disc, read through the package's hashes)" : ""));
                _info.Set("Size", FileSniffer.FormatSize(r.Item9) + (format == GameFormat.Package ? " as a disc" : ""));
                if (r.info != null)
                {
                    _info.Set("Game", (string.IsNullOrEmpty(r.info.Name) ? "(no name)" : r.info.Name) + (r.info.Xbox360 ? "  (Xbox 360)" : "  (original Xbox)"));
                    _info.Set("Title ID / Media ID", r.info.TitleId.ToString("X8") + " / " + r.info.MediaId.ToString("X8"));
                    _info.Set("Disc", r.info.DiscNumber + " of " + r.info.DiscCount);
                }
                _info.Set("Layout", XisoImage.Describe(r.Layout));
                _info.Set("Partition offset", "0x" + r.PartitionOffset.ToString("X"));
                _info.Set("Root folder table", "sector " + r.RootSector + " (0x" + (r.PartitionOffset + (long)r.RootSector * XisoImage.SectorSize).ToString("X") + "), " + r.RootSize + " bytes");
                _info.Set("Files", r.Item5.Files + " files, " + r.Item5.Dirs + " folders");
                _info.Set("Content size", FileSniffer.FormatSize(r.Item6));
                _info.Set("Optimized XISO", r.Optimized ? "Yes" : "No");
                _info.Set("Has $SystemUpdate", r.root.Any(e => e.IsDirectory && e.Name.Contains(XisoImage.SystemUpdate)) ? "Yes" : "No");
                _browseIso = path;
                _browsePartition = r.PartitionOffset;
                _browseRoot = r.root;
                _browseStack.Clear();
                _browseStack.Push((r.root, ""));
                _browseFind.Text = "";
                FillBrowser();
                Body.PerformLayout();
            });
        }

        private static (int Files, int Dirs) Count(List<XisoEntry> items)
        {
            int f = 0, d = 0;
            foreach (var e in items)
            {
                if (e.IsDirectory) { d++; var c = Count(e.Children); f += c.Files; d += c.Dirs; }
                else f++;
            }
            return (f, d);
        }

        private void FillBrowser()
        {
            if (_browseStack.Count == 0) return;
            string find = _browseFind.Text.Trim();
            var (items, _) = _browseStack.Peek();
            IEnumerable<XisoEntry> shown = items;
            if (find.Length > 0 && _browseRoot != null)
            {
                var hits = new List<XisoEntry>();
                void Walk(List<XisoEntry> l) { foreach (var e in l) { if (e.Name.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(e); if (e.IsDirectory) Walk(e.Children); } }
                Walk(_browseRoot);
                shown = hits;
            }
            _browse.BeginUpdate();
            _browse.Items.Clear();
            int n = 0;
            foreach (var e in shown.OrderByDescending(x => x.IsDirectory).ThenBy(x => find.Length > 0 ? x.RelativePath : x.Name, StringComparer.OrdinalIgnoreCase))
            {
                string ext = Path.GetExtension(e.Name).ToLowerInvariant();
                int img = e.IsDirectory ? 0 : ext == ".xex" ? 2 : ext == ".xbe" ? 3 : 1;
                var li = new ListViewItem(find.Length > 0 ? e.RelativePath : e.Name, img) { Tag = e };
                li.SubItems.Add(e.IsDirectory ? "" : FileSniffer.FormatSize(e.Size));
                li.SubItems.Add(e.IsDirectory ? "Folder" : ext == ".xex" ? "Xbox 360 program" : ext == ".xbe" ? "Xbox program" : ext.Length > 1 ? ext.Substring(1).ToUpperInvariant() + " file" : "File");
                li.SubItems.Add(e.Sector.ToString());
                li.SubItems.Add("0x" + (_browsePartition + (long)e.Sector * XisoImage.SectorSize).ToString("X"));
                li.SubItems.Add(e.AttributeText);
                li.ToolTipText = e.RelativePath + (e.IsDirectory ? "" : "  -  " + e.Size.ToString("N0") + " bytes");
                _browse.Items.Add(li);
                n++;
            }
            _browse.EndUpdate();
            _browsePath.Text = find.Length > 0 ? "Found " + n + " item(s) named like \"" + find + "\" in the whole image"
                                                : "\\" + string.Join("\\", _browseStack.Reverse().Skip(1).Select(x => x.Name)) + "   (" + items.Count + " item(s))";
        }

        /// <summary>Everything known about the selected entry - and for a program (XEX / XBE), what it says about itself.</summary>
        private void BrowseProperties()
        {
            var e = BrowseSelected().FirstOrDefault();
            if (_browseIso == null || e == null) { Dlg.Info("Properties", "Select a file or folder in the list."); return; }
            string iso = _browseIso;
            Run(null, "Reading " + e.Name, async () =>
            {
                var lines = new List<string>
                {
                    "Path in the image:  \\" + e.RelativePath,
                    e.IsDirectory ? "Folder table:  " + e.Size.ToString("N0") + " bytes, " + Count(e.Children).Files + " files and " + Count(e.Children).Dirs + " folders inside"
                                  : "Size:  " + e.Size.ToString("N0") + " bytes (" + FileSniffer.FormatSize(e.Size) + ")",
                    "Sector:  " + e.Sector + "  (byte offset 0x" + (_browsePartition + (long)e.Sector * XisoImage.SectorSize).ToString("X") + " in the image)",
                    "Attributes:  " + e.AttributeText
                };
                string ext = Path.GetExtension(e.Name).ToLowerInvariant();
                if (!e.IsDirectory && (ext == ".xex" || ext == ".xbe"))
                {
                    byte[] head = await Task.Run(() => { using (var img = GameSources.OpenImage(iso)) return img.ReadHead(e, ext == ".xex" ? 0x10000 : (int)Math.Min(e.Size, 0x400000)); });
                    if (ext == ".xex")
                    {
                        var x = XexFile.ReadHeader(head, e.Name);
                        if (x.Valid)
                        {
                            lines.Add("");
                            lines.Add("Title ID:  " + x.TitleId.ToString("X8") + "     Media ID:  " + x.MediaId.ToString("X8"));
                            lines.Add("Version:  " + XexFile.VersionString(x.Version) + "     Base version:  " + XexFile.VersionString(x.BaseVersion));
                            lines.Add("Disc:  " + x.DiscNumber + " of " + x.DiscCount + "     Module:  " + x.ModuleType);
                            if (x.HasSecurityInfo)
                                lines.Add("Allowed media:  0x" + x.AllowedMediaTypes.ToString("X8") + (x.OriginalDiscOnly ? " (original disc only)" : x.AllowsDisc ? " (a disc is allowed)" : " (Games on Demand / package edition - no disc)"));
                        }
                        else lines.Add("Not a readable XEX2 header.");
                    }
                    else
                    {
                        string tmp = Path.Combine(Path.GetTempPath(), "X360GameHack", "props-" + Guid.NewGuid().ToString("N") + ".xbe");
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(tmp));
                            File.WriteAllBytes(tmp, head);
                            var b = XbeFile.Read(tmp);
                            lines.Add("");
                            lines.Add("Title:  " + b.TitleName);
                            lines.Add("Title ID:  " + b.TitleId.ToString("X8") + "     Version:  " + b.CertVersion.ToString("X8"));
                        }
                        catch (Exception ex) { lines.Add("XBE header: " + ex.Message); }
                        finally { try { File.Delete(tmp); } catch { } }
                    }
                }
                Dlg.Info(e.Name + " - properties", string.Join("\n", lines));
            });
        }

        /// <summary>Every file of the image (or package) into a folder - $SystemUpdate included, as Xbox Image Browser's extract.</summary>
        private void BrowseExtractAll()
        {
            if (_browseIso == null) { Dlg.Info("Extract", "Open a disc image first."); return; }
            string dir = PickFolder("Extract the whole disc into which folder?");
            if (dir == null) return;
            string iso = _browseIso;
            var cts = new CancellationTokenSource();
            Run(null, "Extracting the whole disc", async () =>
            {
                using (Status.Begin("Extracting everything", cts))
                {
                    var progress = Status.NewProgress();
                    await Task.Run(() => { using (var img = GameSources.OpenImage(iso)) img.ExtractAll(dir, false, progress, cts.Token); }, cts.Token);
                    Log.Success("Extracted the whole disc to " + dir);
                }
            });
        }

        private List<XisoEntry> BrowseSelected() => _browse.SelectedItems.Cast<ListViewItem>().Select(i => (XisoEntry)i.Tag).ToList();

        private void BrowseOpen()
        {
            var e = BrowseSelected().FirstOrDefault();
            if (e == null) return;
            if (e.IsDirectory)
            {
                if (_browseFind.Text.Trim().Length > 0 && _browseRoot != null)
                {
                    // a folder from the search results: go there along its path, then show it
                    _browseStack.Clear();
                    _browseStack.Push((_browseRoot, ""));
                    var level = _browseRoot;
                    foreach (string part in e.RelativePath.Split('\\'))
                    {
                        var d = level.FirstOrDefault(x => x.IsDirectory && x.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                        if (d == null) break;
                        _browseStack.Push((d.Children, d.Name));
                        level = d.Children;
                    }
                    _browseFind.Text = "";   // refills the list
                    return;
                }
                _browseStack.Push((e.Children, e.Name)); FillBrowser();
            }
            else BrowseOpenInTool();
        }

        private void BrowseUp()
        {
            if (_browseStack.Count <= 1) return;
            _browseStack.Pop();
            FillBrowser();
        }

        private void BrowseExtract()
        {
            var items = BrowseSelected();
            if (_browseIso == null || items.Count == 0) { Dlg.Info("Extract", "Select files or folders in the list."); return; }
            string dir = PickFolder("Extract the selected items to which folder?");
            if (dir == null) return;
            string iso = _browseIso;
            var cts = new CancellationTokenSource();
            Run(null, "Extracting from image", async () =>
            {
                using (Status.Begin("Extracting " + items.Count + " item(s)", cts))
                {
                    var progress = Status.NewProgress();
                    await Task.Run(() =>
                    {
                        using (var img = GameSources.OpenImage(iso))
                        {
                            var state = new TransferProgress { Total = XisoImage.TotalBytes(items, false) };
                            img.ExtractEntries(items, dir, false, progress, state, cts.Token);
                        }
                    }, cts.Token);
                    Log.Success("Extracted " + items.Count + " item(s) to " + dir);
                }
            });
        }

        private void BrowseOpenInTool()
        {
            var e = BrowseSelected().FirstOrDefault(x => !x.IsDirectory);
            if (_browseIso == null || e == null) { Dlg.Info("Open", "Select a file."); return; }
            string iso = _browseIso;
            Run(null, "Opening " + e.Name, async () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "X360GameHack", "from-iso", Path.GetFileNameWithoutExtension(iso));
                Directory.CreateDirectory(dir);
                string target = PathSafety.CombineUnder(dir, e.Name);
                await Task.Run(() => { using (var img = GameSources.OpenImage(iso)) img.ExtractFile(e, target, null, null, CancellationToken.None); });
                Log.Info("Extracted a working copy: " + target + " (changes do not touch the ISO).");
                WorkingFile.Set(target);
            });
        }

        // ------------------------------------------------------------- actions

        public void AddIsos(IEnumerable<string> files)
        {
            foreach (var f in files)
            {
                if (string.IsNullOrEmpty(f) || !File.Exists(f)) continue;
                if (XisoImage.Probe(f) == null) { Log.Warn(Path.GetFileName(f) + " is not an Xbox / Xbox 360 disc image - skipped."); continue; }
                if (_list.Items.Cast<ListViewItem>().Any(i => string.Equals((string)i.Tag, f, StringComparison.OrdinalIgnoreCase))) continue;
                var item = new ListViewItem(Path.GetFileName(f)) { Tag = f };
                item.SubItems.Add(FileSniffer.FormatSize(new FileInfo(f).Length));
                item.SubItems.Add("Queued");
                _list.Items.Add(item);
            }
            Log.Info(_list.Items.Count + " image(s) in the bulk list.");
        }

        private static void LaunchXbbc()
        {
            // Xbox Backup Creator needs Microsoft's Common Controls 6 (MSCOMCTL.OCX), which most PCs already have.
            // Xbox Backup Creator is a 32-bit program: look where 32-bit programs look, whichever way this one runs
            bool registered;
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32))
            using (var k = root.OpenSubKey(@"TypeLib\{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}")) registered = k != null;
            if (!registered)
            {
                Dlg.Warn("Xbox Backup Creator",
                    "Xbox Backup Creator needs Microsoft Windows Common Controls 6.0 (MSCOMCTL.OCX), which is not registered on this PC.\n\n" +
                    "X360GameHack never registers system components itself (that would need administrator rights). " +
                    "Install Microsoft's 'Visual Basic 6.0 runtime / Common Controls' redistributable, then try again.\n\n" +
                    "For browsing and building ISOs you don't need it: use the built-in tools on this page.");
                return;
            }
            Tools.Launch(Tools.XBBC);
        }

        private void ExtractSingle()
        {
            if (!WorkingFile.Has(WorkingFileKind.Iso)) return;
            string root = PickFolder("Extract to which folder? A sub-folder named after the game is created.", AppSettings.Current.ExtractPath);
            if (root == null) return;
            _dest.Text = root;
            AddIsos(new[] { WorkingFile.Path });
            var item = _list.Items.Cast<ListViewItem>().FirstOrDefault(i => string.Equals((string)i.Tag, WorkingFile.Path, StringComparison.OrdinalIgnoreCase));
            if (item != null) StartBulk(new List<ListViewItem> { item });
        }

        private string _modded;       // the image the last swap produced

        private void PlayInXenia(string iso)
        {
            string xenia = Tools.Xenia;
            if (string.IsNullOrEmpty(xenia) || !File.Exists(xenia)) { Dlg.Info("Xenia", "Xenia was not found. Set its path on the Xenia page (Setup tab)."); return; }
            if (string.IsNullOrEmpty(iso) || !File.Exists(iso)) { Dlg.Info("Xenia", "Open a disc image first."); return; }
            XeniaHost.Start(iso);
        }

        private void SwapSelected()
        {
            var e = BrowseSelected().FirstOrDefault(x => !x.IsDirectory);
            if (_browseIso == null || e == null) { Dlg.Info("Swap a file", "Open a disc image and select one file in the list above first."); return; }
            string local = PickFile("Choose the file that takes the place of " + e.Name);
            if (local == null) return;
            SwapTo(_browseIso, new Dictionary<string, string> { { e.RelativePath, local } });
        }

        private void SwapFolder()
        {
            if (_browseIso == null) { Dlg.Info("Swap files", "Open a disc image first."); return; }
            string dir = PickFolder("Choose the folder that holds your changed files, laid out like the disc");
            if (dir == null) return;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string root = Path.GetFullPath(dir).TrimEnd('\\');
                foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (map.Count >= 20000) throw new InvalidOperationException("That folder holds too many files to be a set of changes.");
                    map[f.Substring(root.Length + 1)] = f;
                }
            }
            catch (Exception ex) { Dlg.Warn("Swap files", ex.Message); return; }
            if (map.Count == 0) { Dlg.Info("Swap files", "That folder is empty."); return; }
            SwapTo(_browseIso, map);
        }

        /// <summary>Writes a copy of <paramref name="iso"/> with the given files replaced (path inside the image -> file on this PC).</summary>
        public void SwapTo(string iso, IDictionary<string, string> replacements, string output = null)
        {
            if (output == null) output = PickSave("Save the changed image as", Path.GetFileNameWithoutExtension(iso) + " (modded).iso", "Disc image|*.iso");
            if (output == null) return;
            if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(iso), StringComparison.OrdinalIgnoreCase))
            {
                Dlg.Warn("Swap files", "Choose a different file name so the original image is not overwritten.");
                return;
            }
            var cts = new CancellationTokenSource();
            Run(null, "Writing the changed image", async () =>
            {
                using (Status.Begin("Swapping " + replacements.Count + " file(s) in " + Path.GetFileName(iso), cts))
                {
                    var progress = Status.NewProgress();
                    try
                    {
                        await Task.Run(() => XisoBuilder.Rewrite(iso, output, false, replacements, progress, cts.Token), cts.Token);
                    }
                    catch (FileNotFoundException ex) { Dlg.Warn("Swap files", ex.Message); return; }      // refused before anything was written
                    _modded = output;
                    Log.Success("Changed image written: " + output + " (" + replacements.Count + " file(s) swapped, " + FileSniffer.FormatSize(new FileInfo(output).Length) + "). Press \"Play the new image in Xenia\" to test it.");
                }
            });
        }

        private void Optimize()
        {
            if (!WorkingFile.Has(WorkingFileKind.Iso)) return;
            string src = WorkingFile.Path;
            bool already;
            try { using (var img = XisoImage.Open(src)) already = img.Layout == XisoLayout.Xiso && img.Optimized; }
            catch (Exception ex) { Dlg.Warn("Optimize", ex.Message); return; }
            if (already)
            {
                // extract-xiso -r skips these too: rewriting gives a byte-identical copy.
                Dlg.Info("Optimize", "This image is already an optimized XISO, so there is nothing to rewrite.");
                return;
            }
            string outFile = PickSave("Save the optimized XISO as", Path.GetFileNameWithoutExtension(src) + " (optimized).iso", "Disc image|*.iso");
            if (outFile == null) return;
            if (string.Equals(Path.GetFullPath(outFile), Path.GetFullPath(src), StringComparison.OrdinalIgnoreCase))
            {
                Dlg.Warn("Optimize", "Choose a different file name so the original is not overwritten.");
                return;
            }
            bool media = _mediaPatch.Checked;
            var cts = new CancellationTokenSource();
            Run(null, "Optimizing XISO", async () =>
            {
                using (Status.Begin("Optimizing " + Path.GetFileName(src), cts))
                {
                    var progress = Status.NewProgress();
                    await Task.Run(() => XisoBuilder.Rewrite(src, outFile, media, progress, cts.Token), cts.Token);
                    Log.Success("Optimized XISO written: " + outFile + " (" + FileSniffer.FormatSize(new FileInfo(outFile).Length) + ")");
                }
            });
        }

        /// <summary>
        /// One click: the open image goes into the Convert list with "Convert to" set to Games on Demand and is converted straight
        /// away with the options set there (by default Content\0000000000000000\{title}\{type} in a "Games on Demand" folder next to the image).
        /// </summary>
        private void ConvertToGod()
        {
            string src = WorkingFile.Has(WorkingFileKind.Iso) ? WorkingFile.Path : PickFile("Choose a disc image", "Disc images|*.iso;*.xiso;*.000|All files|*.*");
            if (src == null) return;
            ShowConvert(GameFormat.Package);
            AddGodImages(new[] { src }, true);
        }

        /// <summary>Shows the Convert tab with "Convert to" set to <paramref name="to"/>.</summary>
        public void ShowConvert(GameFormat to)
        {
            _cvTo.SelectedIndex = to == GameFormat.Package ? 0 : to == GameFormat.Image ? 1 : 2;
            ShowSection(_convertTab);
        }

        /// <summary>Shows the Convert tab set to Games on Demand.</summary>
        public void ShowIsoToGod() => ShowConvert(GameFormat.Package);

        /// <summary>Shows the Convert tab set to disc images.</summary>
        public void ShowGodToIso() => ShowConvert(GameFormat.Image);

        private void CreateIso()
        {
            string folder = PickFolder("Choose the extracted game folder");
            if (folder == null) return;
            string output = PickSave("Save XISO as", Path.GetFileName(folder.TrimEnd('\\')) + ".iso", "Disc image|*.iso");
            if (output == null) return;
            bool media = _mediaPatch.Checked;
            var cts = new CancellationTokenSource();
            Run(null, "Creating XISO", async () =>
            {
                using (Status.Begin("Creating " + Path.GetFileName(output), cts))
                {
                    var progress = Status.NewProgress();
                    await Task.Run(() => XisoBuilder.CreateFromFolder(folder, output, media, progress, cts.Token), cts.Token);
                    Log.Success("XISO created: " + output + " (" + FileSniffer.FormatSize(new FileInfo(output).Length) + ")");
                }
            });
        }

        private string PatchArgs()
        {
            string args = _fixTu.Checked ? "-u " : "";
            switch (_patch.SelectedIndex)
            {
                case 1: return args + XexFile.SafeLimits + " -m r";
                case 2: return args + XexFile.SafeLimits + " -m d";
                case 3: return (args + _customArgs.Text).Trim();
                default: return args.Trim();
            }
        }

        private void SaveOptions()
        {
            var s = AppSettings.Current;
            s.ExtractPath = _dest.Text.Trim();
            s.ExcludeSystemUpdate = _skipUpdate.Checked;
            s.BulkPatchTitleUpdate = _fixTu.Checked;
            s.BulkPatchRgh = _patch.SelectedIndex == 1;
            s.BulkPatchXdk = _patch.SelectedIndex == 2;
            s.BulkUseCustom = _patch.SelectedIndex == 3;
            s.BulkCustomCommand = _customArgs.Text.Trim();
            s.BulkInstallContent = _installContent.Checked;
            AppSettings.Save();
        }

        private void StartBulk(List<ListViewItem> items)
        {
            if (items.Count == 0) { Dlg.Info("Bulk extract", "Add or drop some ISO files first."); return; }
            string root = _dest.Text.Trim();
            if (root.Length == 0 || !Path.IsPathRooted(root))
            {
                root = PickFolder("Choose the extraction folder");
                if (root == null) return;
                _dest.Text = root;
            }
            SaveOptions();
            string usb = AppSettings.Current.UsbDrive;
            if (_installContent.Checked && (string.IsNullOrEmpty(usb) || !Directory.Exists(usb)))
            {
                Dlg.Warn("No USB drive", "Choose a USB drive on the Console Setup page first, or untick 'Install content packages'.");
                return;
            }

            var cts = new CancellationTokenSource();
            string args = PatchArgs();
            bool skip = _skipUpdate.Checked, backup = _backupXex.Checked, install = _installContent.Checked;
            _start.Enabled = false;
            RunWithCancel(cts, async () =>
            {
                Directory.CreateDirectory(root);
                int ok = 0, n = 0;
                var progress = Status.NewProgress();
                foreach (var item in items)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    n++;
                    string iso = (string)item.Tag;
                    string name = PathSafety.SafeName(Path.GetFileNameWithoutExtension(iso));
                    string dest = Path.Combine(root, name);
                    if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any())
                    {
                        item.SubItems[2].Text = "Skipped - folder exists";
                        Log.Warn(name + ": destination already exists, skipped.");
                        continue;
                    }
                    item.SubItems[2].Text = $"Extracting ({n}/{items.Count})...";
                    try
                    {
                        await Task.Run(() => { using (var img = XisoImage.Open(iso)) img.ExtractAll(dest, skip, progress, cts.Token); }, cts.Token);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        item.SubItems[2].Text = "Extraction failed";
                        Log.Error(name + ": " + ex.Message);
                        continue;
                    }
                    var xexes = Directory.GetFiles(dest, "*.xex");
                    int notPatched = 0;
                    if (args.Length > 0 && xexes.Length > 0)
                    {
                        item.SubItems[2].Text = "Patching " + xexes.Length + " XEX...";
                        foreach (var x in xexes)
                        {
                            if (backup && !File.Exists(x + ".bak")) File.Copy(x, x + ".bak");
                            var mediaBefore = XexFile.ReadMediaIds(x);
                            var r = await ToolRunner.RunAsync(Tools.XexTool, args + " " + ToolRunner.Q(x), ct: cts.Token);
                            if (!r.Success)
                            {
                                notPatched++;
                                Log.Warn(name + ": xextool could not patch " + Path.GetFileName(x) + " (exit code " + r.ExitCode + ") - see the lines above.");
                            }
                            string restored = XexFile.RestoreZeroedMediaIds(x, mediaBefore);   // never leave a zeroed Media ID behind
                            if (restored != null) Log.Warn(Path.GetFileName(x) + ": xextool zeroed the " + restored + " - restored.");
                        }
                    }
                    if (install)
                    {
                        var pkgs = await Task.Run(() => ContentInstaller.Collect(new[] { dest }));
                        if (pkgs.Count > 0)
                        {
                            item.SubItems[2].Text = "Installing " + pkgs.Count + " package(s)...";
                            await ContentInstaller.InstallAsync(pkgs, InstallTarget.UsbDrive, usb, null, null, null, progress, cts.Token);
                        }
                    }
                    item.SubItems[2].Text = xexes.Length == 0 ? "Done (no XEX in root)" : notPatched > 0 ? "Done - " + notPatched + " XEX not patched" : "Done";
                    if (notPatched > 0) Log.Warn(name + " extracted to " + dest + ", but " + notPatched + " XEX file(s) could not be patched.");
                    else Log.Success(name + " extracted to " + dest);
                    ok++;
                }
                Log.Success($"Bulk extract finished: {ok} of {items.Count} image(s) extracted to {root}");
            });
        }

        private async void RunWithCancel(CancellationTokenSource cts, Func<Task> work)
        {
            using (Status.Begin("Extracting disc images", cts))
            {
                try { await work(); }
                catch (OperationCanceledException) { Log.Warn("Extraction cancelled."); }
                catch (Exception ex) { Log.Error("Extraction failed: " + ex.Message); Dlg.Error("Extraction failed", ex.Message); }
                finally { _start.Enabled = true; cts.Dispose(); }
            }
        }
    }
}
