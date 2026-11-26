using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public sealed class SetupPage : PageBase
    {
        public override string Title => "Console Setup";
        public override string Subtitle => "Install packages to USB or straight to the console, and edit DashLaunch's launch.ini.";
        public override string PageGlyph => Glyph.Usb;

        private readonly Segmented _target = new Segmented("USB drive", "Console (XBDM)", "Console (FTP)");
        private readonly XCombo _usb = new XCombo { Width = Theme.S(420) };
        private readonly XTextBox _xbdmDrive = new XTextBox("Hdd") { Width = Theme.S(120), Text = "Hdd" };
        private readonly XCombo _ftpProfile = new XCombo { Width = Theme.S(260) };
        private readonly XTextBox _ftpDrive = new XTextBox("Hdd1") { Width = Theme.S(120), Text = "Hdd1" };
        private readonly HStack _usbRow, _xbdmRow, _ftpRow;
        private readonly ListView _pkgs = ListStyle.Create(("Content", 260), ("Type", 150), ("Title ID", 80), ("Size", 80), ("Install path", 300), ("Status", 120));
        private readonly XButton _install;

        private readonly XTextBox _ini = new XTextBox(null, true, true) { Height = Theme.S(300) };
        private readonly XCombo _dash = new XCombo { Width = Theme.S(220) };
        private readonly XTextBox _dashPath = new XTextBox(@"Usb:\Aurora\Aurora.xex", false, true);
        private readonly XTextBox[] _plugins = Enumerable.Range(1, 5).Select(i => new XTextBox(@"plugin" + i + @", e.g. Usb:\xbdm.xex", false, true)).ToArray();
        private readonly List<XCheck> _settings = new List<XCheck>();
        private bool _syncing;

        public SetupPage()
        {
            // ---------- target
            Into = AddSection("Install content", Glyph.Usb);
            var target = AddCard("Where to install", "USB drives must be FAT32. On the console, XBDM or an FTP server must be running.", Glyph.Hdd);
            Field(target, "Install to", _target).FillChild = null;
            var refresh = Btn("", Glyph.Refresh, RefreshUsb, tip: "Re-scan USB drives");
            _usbRow = Field(target, "USB drive", _usb, refresh, Btn("Open", Glyph.FolderOpen, () => { if (UsbRoot() != null) Tools.OpenFolder(UsbRoot()); }));
            _xbdmRow = Field(target, "Console drive", _xbdmDrive, new TextBlock("Hdd, Usb0, Usb1 ... (Content goes to <drive>:\\Content)", Theme.Small, Theme.TextDim) { Width = Theme.S(380), Height = Theme.S(30), Flags = TextFormatFlags.VerticalCenter });
            _xbdmRow.FillChild = null;
            _ftpRow = Field(target, "FTP profile", _ftpProfile, new TextBlock("Drive", Theme.Small, Theme.TextDim) { Width = Theme.S(40), Height = Theme.S(30), Flags = TextFormatFlags.VerticalCenter }, _ftpDrive);
            _ftpRow.FillChild = null;
            _target.SelectedIndexChanged += (s, e) => ShowTarget();
            _usb.SelectedIndexChanged += (s, e) =>
            {
                if (_usb.SelectedItem is UsbDrive d) { AppSettings.Current.UsbDrive = d.Root; AppSettings.Save(); }
            };

            // ---------- packages
            var pk = AddCard("Install content", "Drop GOD, XBLA, DLC, title updates, themes, avatar items and saves - files or whole folders. Each one goes to Content\\<profile>\\<title ID>\\<type>.", Glyph.Package);
            pk.AddAction("Add files", Glyph.Add, (s, e) => { var f = PickFiles("Add packages"); if (f != null) AddPackages(f); });
            pk.AddAction("Add folder", Glyph.Folder, (s, e) => { var f = PickFolder("Add every package found in a folder"); if (f != null) AddPackages(new[] { f }); });
            pk.AddAction("Remove", Glyph.Remove, (s, e) => { foreach (ListViewItem i in _pkgs.SelectedItems.Cast<ListViewItem>().ToList()) _pkgs.Items.Remove(i); });
            pk.AddAction("Clear", Glyph.Clear, (s, e) => _pkgs.Items.Clear());
            _pkgs.Height = Theme.S(220);
            _pkgs.AllowDrop = true;
            _pkgs.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            _pkgs.DragDrop += (s, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f) AddPackages(f); };
            pk.Add(_pkgs);
            _install = Btn("Install all", Glyph.Download, null, BtnKind.Primary);
            _install.Click += (s, e) => InstallAll();
            Buttons(pk, _install, Btn("Copy game folder to USB...", Glyph.Folder, CopyGameToUsb, tip: "Copies an extracted game to <USB>\\Games\\<name>"));

            Into = AddSection("USB drive guide", Glyph.Help);
            var help = AddCard("Setting up a USB drive for the Xbox 360", null, Glyph.Help);
            Note(help,
                "1. Format the USB drive as FAT32 (drives larger than 32GB need a tool such as guiformat / FAT32 Format).\n" +
                "2. Packages are installed to <USB>:\\Content\\0000000000000000\\<TitleID>\\<Type>\\ - the dashboard shows them under Games or the matching section. " +
                "Upper-case TU_ title updates go to <USB>:\\Cache (handled automatically).\n" +
                "3. Extracted games (folders with default.xex) can go anywhere, e.g. <USB>:\\Games\\<Game>. Add that folder as a scan path in Aurora / FSD.\n" +
                "4. Put launch.ini on the root of the USB or Hdd:. To use XBDM features, add xbdm.xex as a plugin and reboot.\n" +
                "5. Files over 4GB cannot be stored on FAT32 - convert the game (a disc image, or its folder with default.xex) to Games on Demand on the Xbox ISOs page (Convert tab) instead: its data files are at most 170 MB each.");

            ShowTarget();
            // the ini editor lives here as a tab instead of having its own entry in the sidebar
            AddPageSection("INI Editor", Glyph.Settings, new IniPage());
        }

        public override void OnActivated()
        {
            RefreshUsb();
            _ftpProfile.Items.Clear();
            foreach (var p in AppSettings.Current.FtpProfiles) _ftpProfile.Items.Add(p);
            if (_ftpProfile.Items.Count > 0) _ftpProfile.SelectedIndex = Math.Min(AppSettings.Current.SelectedFtpProfile, _ftpProfile.Items.Count - 1);
        }

        private void ShowTarget()
        {
            _usbRow.Visible = _target.SelectedIndex == 0;
            _xbdmRow.Visible = _target.SelectedIndex == 1;
            _ftpRow.Visible = _target.SelectedIndex == 2;
        }

        private void RefreshUsb()
        {
            var drives = UsbDrives.Detect();
            _usb.Items.Clear();
            foreach (var d in drives) _usb.Items.Add(d);
            string saved = AppSettings.Current.UsbDrive;
            int idx = drives.FindIndex(d => string.Equals(d.Root, saved, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = drives.FindIndex(d => d.IsFat32);
            if (drives.Count > 0) _usb.SelectedIndex = Math.Max(0, idx);
            if (drives.Count == 0) Log.Info("No USB drives found. Plug one in and press refresh.");
        }

        private string UsbRoot()
        {
            if (_usb.SelectedItem is UsbDrive d && Directory.Exists(d.Root))
            {
                if (!d.IsFat32 && !Dlg.Confirm("Not FAT32", d.Root + " is " + d.FileSystem + ". The Xbox 360 only reads FAT32 USB drives. Continue anyway?", "Continue", true))
                    return null;
                return d.Root;
            }
            Dlg.Info("USB drive", "Plug in a FAT32 USB drive and press refresh.");
            return null;
        }

        // ------------------------------------------------------------- packages

        public void AddPackages(IEnumerable<string> paths)
        {
            var list = paths.ToList();
            Run(null, "Scanning for packages", async () =>
            {
                var pkgs = await Task.Run(() => ContentInstaller.Collect(list));
                foreach (var p in pkgs)
                {
                    if (_pkgs.Items.Cast<ListViewItem>().Any(i => ((StfsPackage)i.Tag).Path.Equals(p.Path, StringComparison.OrdinalIgnoreCase))) continue;
                    var li = new ListViewItem(ContentInstaller.Label(p).Replace(" [" + p.ContentTypeName + "]", "")) { Tag = p };
                    li.SubItems.Add(p.ContentTypeName);
                    li.SubItems.Add(p.TitleIdHex);
                    li.SubItems.Add(FileSniffer.FormatSize(ContentInstaller.PackageBytes(p)));
                    li.SubItems.Add(p.InstallDir);
                    li.SubItems.Add("Ready");
                    _pkgs.Items.Add(li);
                }
                int skipped = list.Count(f => File.Exists(f)) - pkgs.Count(p => list.Contains(p.Path));
                Log.Info(pkgs.Count + " package(s) found" + (skipped > 0 ? ", " + skipped + " file(s) were not STFS packages" : "") + ".");
            });
        }

        private void InstallAll()
        {
            var items = _pkgs.Items.Cast<ListViewItem>().ToList();
            if (items.Count == 0) { Dlg.Info("Install", "Drop some packages on the list first."); return; }
            var target = (InstallTarget)_target.SelectedIndex;
            string usb = null;
            FtpService ftp = null;
            if (target == InstallTarget.UsbDrive) { usb = UsbRoot(); if (usb == null) return; }
            if (target == InstallTarget.ConsoleXbdm && !ConsoleService.IsConnected) { Dlg.Info("XBDM", "Connect to the console on the Neighborhood page first."); return; }
            if (target == InstallTarget.ConsoleFtp)
            {
                if (!(_ftpProfile.SelectedItem is FtpProfile p) || string.IsNullOrWhiteSpace(p.Host)) { Dlg.Info("FTP", "Set up an FTP profile on the FTP page first."); return; }
                ftp = new FtpService(p);
            }
            if (target == InstallTarget.ConsoleXbdm) HddCopyTip.ShowOnce(_xbdmDrive.Text.Trim(), items.Select(i => ((StfsPackage)i.Tag).Path));
            var cts = new CancellationTokenSource();
            _install.Enabled = false;
            Run(null, "Installing packages", async () =>
            {
                try
                {
                    using (Status.Begin("Installing " + items.Count + " package(s)", cts))
                    {
                        foreach (var li in items)
                        {
                            cts.Token.ThrowIfCancellationRequested();
                            li.SubItems[5].Text = "Installing...";
                            try
                            {
                                await ContentInstaller.InstallAsync(new[] { (StfsPackage)li.Tag }, target, usb, ftp, _ftpDrive.Text.Trim(),
                                    _xbdmDrive.Text.Trim(), Status.NewProgress(), cts.Token);
                                li.SubItems[5].Text = "Installed";
                            }
                            catch (OperationCanceledException) { li.SubItems[5].Text = "Cancelled"; throw; }
                            catch (Exception ex) { li.SubItems[5].Text = "Failed"; Log.Error(li.Text + ": " + ex.Message); }
                        }
                    }
                }
                finally { _install.Enabled = true; cts.Dispose(); }
            });
        }

        private void CopyGameToUsb()
        {
            string root = UsbRoot();
            if (root == null) return;
            string folder = PickFolder("Choose the extracted game folder");
            if (folder == null) return;
            string dest = Path.Combine(root, "Games", Path.GetFileName(folder.TrimEnd('\\')));
            var cts = new CancellationTokenSource();
            Run(null, "Copying game", async () =>
            {
                using (Status.Begin("Copying " + Path.GetFileName(folder) + " to " + dest, cts))
                {
                    var state = new TransferProgress { Total = FtpService.MeasureLocal(folder) };
                    var progress = Status.NewProgress();
                    await Task.Run(() => ContentInstaller.CopyDirectory(folder, dest, progress, state, cts.Token));
                    Log.Success("Copied to " + dest);
                }
            });
        }

    }
}
