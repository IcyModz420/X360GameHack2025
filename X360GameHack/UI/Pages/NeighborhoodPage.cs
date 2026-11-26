using System;
using System.Linq;
using System.Windows.Forms;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>Shared XBDM connect bar used by Neighborhood, Console and Peek Poker.</summary>
    public sealed class ConnectBar : Card
    {
        private readonly XTextBox _ip = new XTextBox("Console IP address") { Width = Theme.S(220) };
        private readonly XButton _connect;
        private readonly XButton _disconnect;
        private readonly XCombo _found = new XCombo { Width = Theme.S(260), Visible = false };
        private readonly TextBlock _dot = new TextBlock("●", Theme.Body, Theme.TextFaint) { Width = Theme.S(18), Height = Theme.S(30) };
        private readonly TextBlock _state = new TextBlock("", Theme.Body, Theme.TextDim) { Height = Theme.S(30), Width = Theme.S(200) };

        public ConnectBar() : base(null)
        {
            Padding = new Padding(Theme.S(14), Theme.S(12), Theme.S(14), Theme.S(12));
            var row = new HStack { Height = Theme.S(34) };
            _ip.Text = AppSettings.Current.XbdmIp;
            _connect = new XButton("Connect", Glyph.Link, BtnKind.Primary, (s, e) => Connect());
            _disconnect = new XButton("Disconnect", Glyph.Unlink, BtnKind.Secondary, (s, e) => ConsoleService.Disconnect());
            var discover = new XButton("Discover", Glyph.Search, BtnKind.Secondary, null);
            discover.Click += (s, e) => Discover(discover);
            Tip.Set(discover, "Find consoles running xbdm.xex on your network");
            _dot.Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding;
            _state.Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            row.Add(_ip); row.Add(_connect); row.Add(_disconnect); row.Add(discover); row.Add(_found); row.Add(_dot); row.Add(_state);
            row.FillChild = _state;
            Add(row);
            _ip.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Connect(); } };
            _found.SelectedIndexChanged += (s, e) =>
            {
                if (_found.SelectedItem is string f) _ip.Text = f.Split(' ')[0];
            };
            Hooks.ConsoleChanged(this, UpdateState);
            UpdateState();
        }

        private void UpdateState()
        {
            bool on = ConsoleService.IsConnected;
            if (on) _ip.Text = ConsoleService.Ip;
            _connect.Visible = !on;
            _disconnect.Visible = on;
            _ip.Enabled = !on;
            _dot.ForeColor = on ? Theme.Success : Theme.TextFaint;
            _state.ForeColor = on ? Theme.Text : Theme.TextDim;
            _state.Text = on ? "Connected to " + (ConsoleService.Name.Length > 0 ? ConsoleService.Name + "  •  " : "") + ConsoleService.Ip
                             : "Not connected - xbdm.xex must be running on the console (Console Setup > launch.ini).";
            foreach (Control c in Controls) c.PerformLayout();
            PerformLayout();
        }

        private async void Connect()
        {
            if (string.IsNullOrWhiteSpace(_ip.Text)) { Dlg.Warn("XBDM", "Enter the console IP or press Discover."); return; }
            _connect.Enabled = false;
            using (Status.Begin("Connecting to " + _ip.Text.Trim()))
            {
                bool ok = await ConsoleService.ConnectAsync(_ip.Text.Trim());
                if (!ok)
                    Dlg.Error("Could not connect",
                        (ConsoleService.LastConnectProblem.Length > 0
                            ? ConsoleService.LastConnectProblem
                            : "No XBDM response from " + _ip.Text.Trim() + " on port 730.") + "\n\n" +
                        "Also worth checking:\n" +
                        "• xbdm.xex is loaded as a DashLaunch plugin (Console Setup > launch.ini).\n" +
                        "• The PC and console are on the same network (360 Wi-Fi: 2.4 GHz WPA2 only).\n" +
                        "• X360GameHack is allowed through the Windows firewall.");
            }
            _connect.Enabled = true;
        }

        private async void Discover(XButton b)
        {
            b.Enabled = false;
            using (Status.Begin("Searching the network for consoles"))
            {
                var list = await ConsoleService.DiscoverAsync(true);
                _found.Items.Clear();
                foreach (var f in list) _found.Items.Add(f);
                _found.Visible = list.Count > 0;
                if (list.Count > 0) { _found.SelectedIndex = 0; Log.Success("Found " + list.Count + " console(s)."); }
                else Log.Warn("No consoles answered. Check that xbdm.xex is running and you are on the same network.");
                foreach (Control c in Controls) c.PerformLayout();
            }
            b.Enabled = true;
        }
    }

    public sealed class NeighborhoodPage : PageBase
    {
        public override string Title => "Neighborhood";
        public override string Subtitle => "Your console over XBDM: transfer files, launch XEX files, and find every save, profile and title update.";
        public override string PageGlyph => Glyph.Network;

        private readonly RemoteBrowser _browser = new RemoteBrowser
        {
            DetachedTitle = "Not connected",
            DetachedDetail = "Enter your console's IP address above and press Connect, or press Discover to find it. xbdm.xex must be running (Console Setup > launch.ini)."
        };
        private readonly SaveBrowser _saves = new SaveBrowser("Not connected",
            "Connect to your console above to list every save on its hard drive and USB storage - with the game and profile each one belongs to.");
        private readonly ProfileFinder _profiles = new ProfileFinder("Not connected",
            "Connect to your console above to list every gamer profile on its hard drive and USB storage - gamertag, gamerscore and where it is.");
        private readonly TitleUpdateFinder _updates = new TitleUpdateFinder("Not connected",
            "Connect to your console above to list every title update on it - which game, which update, and on which drive.");
        private readonly XbdmContentStore _store = new XbdmContentStore();
        private readonly VStack _savesTab, _profilesTab, _updatesTab;
        private bool _attached, _savesFound, _profilesFound, _updatesFound;

        public NeighborhoodPage() : base(false)
        {
            Body.Add(new ConnectBar());
            var files = AddSection("Files", Glyph.Folder);
            _savesTab = AddSection("Saves", Glyph.Save);
            _profilesTab = AddSection("Profiles", Glyph.Profile);
            _updatesTab = AddSection("Title updates", Glyph.Download);
            _browser.SetPlaces(("Drives", Glyph.Hdd, ""), ("Hdd", Glyph.Folder, "Hdd:\\"), ("Games", Glyph.Game, "Hdd:\\Games"),
                ("Content", Glyph.Package, "Hdd:\\Content\\0000000000000000"), ("Usb0", Glyph.Usb, "Usb0:\\"), ("Flash", Glyph.Flash, "Flash:\\"));
            files.Add(_browser);
            files.FillChild = _browser;
            _savesTab.Add(_saves);
            _savesTab.FillChild = _saves;
            _profilesTab.Add(_profiles);
            _profilesTab.FillChild = _profiles;
            _updatesTab.Add(_updates);
            _updatesTab.FillChild = _updates;
            Hooks.ConsoleChanged(this, Sync);
            Sync();
        }

        private void Sync()
        {
            bool on = ConsoleService.IsConnected;
            if (on && !_attached) { _browser.Attach(new XbdmFs()); _attached = true; }
            if (!on) { _browser.Detach(); _attached = false; _savesFound = _profilesFound = _updatesFound = false; }
            _saves.SetStore(on ? _store : null);
            _profiles.SetStore(on ? _store : null);
            _updates.SetStore(on ? _store : null);
            if (!on || !Visible) return;
            // each finder looks the first time its tab is opened; Find ... looks again
            if (Tabs.SelectedPanel == _savesTab && !_savesFound) { _savesFound = true; _saves.Scan(); }
            if (Tabs.SelectedPanel == _profilesTab && !_profilesFound) { _profilesFound = true; _profiles.Scan(); }
            if (Tabs.SelectedPanel == _updatesTab && !_updatesFound) { _updatesFound = true; _updates.Scan(); }
        }

        protected override void OnSectionChanged() => Sync();

        public override void OnActivated() => Sync();

        /// <summary>Opens the Saves tab (used by the Saves page's "Browse console saves").</summary>
        public void ShowSaves() => ShowSection(_savesTab);

        /// <summary>Opens the Profiles tab.</summary>
        public void ShowProfiles() => ShowSection(_profilesTab);

        /// <summary>Opens the Title updates tab.</summary>
        public void ShowTitleUpdates() => ShowSection(_updatesTab);
    }

    public sealed class ConsolePage : PageBase
    {
        public override string Title => "Console";
        public override string Subtitle => "Console information, power, notifications, screenshots and raw XBDM commands.";
        public override string PageGlyph => Glyph.Console;

        private readonly KvGrid _info = new KvGrid();
        private readonly PictureBox _shot = new PictureBox { Height = Theme.S(260), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Input };

        private readonly XTextBox _launchPath = new XTextBox(@"Hdd:\Games\MyGame\default.xex", false, true);
        private readonly XTextBox _raw = new XTextBox("Raw XBDM command, e.g. dmversion, systeminfo, drivelist", false, true);
        private readonly XCombo _shortcut = new XCombo { Width = Theme.S(220) };
        private readonly ListView _modules = ListStyle.Create(("Module", 220), ("Base", 110), ("Size", 100));
        private readonly XCombo _storage = new XCombo { Width = Theme.S(360) };

        public ConsolePage()
        {
            Body.Add(new ConnectBar());

            Into = AddSection("Console", Glyph.Console);
            var top = AddColumns(2, 420);
            var info = AddCard("Console information", "Double-click a value to copy it (CPU key, console ID...).", Glyph.Info, top);
            info.Add(_info);
            info.AddAction("Refresh", Glyph.Refresh, (s, e) => RefreshInfo());

            var power = AddCard("Power & system", null, Glyph.Power, top);
            Buttons(power,
                Btn("Reboot (cold)", Glyph.Refresh, () => Do("Cold reboot", c => c.Reboot(XboxReboot.Cold), true), BtnKind.Secondary),
                Btn("Restart title (warm)", Glyph.Refresh, RestartTitle),
                Btn("Shut down", Glyph.Power, () => Do("Shut down", c => c.ShutDown(), true), BtnKind.Danger));
            Buttons(power,
                Btn("Freeze (stop)", Glyph.Snow, () => Do("Stop", c => c.Stop())),
                Btn("Resume (go)", Glyph.Play, () => Do("Go", c => c.Go())),
                Btn("Open tray", Glyph.Eject, () => Do("Eject", c => c.DvdEject())),
                Btn("Close tray", Glyph.Disc, () => Do("Load", c => c.DvdLoad())));
            Field(power, "Launch XEX", _launchPath, Btn("Launch", Glyph.Play, () =>
            {
                if (_launchPath.Text.Trim().Length == 0) return;
                Run(null, "Launching", () => ConsoleService.LaunchAsync(_launchPath.Text.Trim()));
            }));
            foreach (XboxShortcuts v in XboxConsole.PlainShortcuts) _shortcut.Items.Add(v);      // the ones plain XBDM can open
            if (_shortcut.Items.Count > 0) _shortcut.SelectedIndex = 0;
            Field(power, "Guide shortcut", _shortcut, Btn("Open", Glyph.ChevronRight, () =>
            {
                if (_shortcut.SelectedItem is XboxShortcuts sc) Do("Opening " + sc, c => c.XboxShortcut(sc));
            })).FillChild = null;

            Into = AddSection("Screenshot", Glyph.Camera);
            var mid = AddColumns(2, 420);

            var shot = AddCard("Screenshot", null, Glyph.Camera, mid);
            shot.Add(_shot);
            Buttons(shot,
                Btn("Capture", Glyph.Camera, CaptureScreenshot, BtnKind.Primary),
                Btn("Save as PNG...", Glyph.Save, SaveShot));

            Into = AddSection("Modules & commands", Glyph.List);
            var mods = AddCard("Loaded modules", "Base addresses are handy for Peek Poker.", Glyph.List);
            _modules.Height = Theme.S(200);
            mods.Add(_modules);
            mods.AddAction("Refresh", Glyph.Refresh, (s, e) => LoadModules());
            _modules.DoubleClick += (s, e) =>
            {
                if (_modules.SelectedItems.Count == 0) return;
                try { Clipboard.SetText(_modules.SelectedItems[0].SubItems[1].Text); Log.Info("Copied base address " + _modules.SelectedItems[0].SubItems[1].Text); } catch { }
            };

            Into = AddSection("Storage backup", Glyph.Hdd);
            var dump = AddCard("Back up storage (raw)", "Copies the console's internal storage (Slim / E / Corona 4 GB) byte for byte into an image file on this PC, " +
                               "to open in Drive Explorer and get deleted files back. Read only - nothing on the console changes. Uses a development kit's own XDRPC, " +
                               "or the JRPC2 plugin on RGH / JTAG. Slow: about 1 MB a second.", Glyph.Hdd);
            Field(dump, "Storage", _storage, Btn("Back up to image...", Glyph.Save, BackUpStorage, BtnKind.Primary));
            dump.AddAction("Refresh", Glyph.Refresh, (s, e) => LoadStorage());

            var raw = AddCard("XBDM command console", "Sends a raw command and prints the reply to the log. For advanced users.", Glyph.Code);
            var send = Btn("Send", Glyph.Send, SendRaw, BtnKind.Primary);
            Field(raw, null, _raw, send);
            _raw.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendRaw(); } };

            Hooks.ConsoleChanged(this, Sync);
            Sync();
            // notifications, temperatures and the ring of light: a tab here instead of a page of their own
            AddPageSection("Notify & Sensors", Glyph.Send, new JrpcPage());
        }

        private void Sync()
        {
            bool on = ConsoleService.IsConnected;
            if (on && Visible) RefreshInfo();
        }

        public override void OnActivated()
        {
            if (ConsoleService.IsConnected) RefreshInfo();
        }

        /// <summary>Starts the running game again from its file (no full reboot).</summary>
        private void RestartTitle()
        {
            if (!ConsoleService.IsConnected) { Dlg.Info("Not connected", "Connect to a console first."); return; }
            Run(null, "Restarting the title", async () =>
            {
                string path = await ConsoleService.RestartTitleAsync();
                Log.Success("Restart title - " + path + " started again.");
            });
        }

        private void Do(string what, Action<XboxConsole> action, bool confirm = false)
        {
            if (!ConsoleService.IsConnected) { Dlg.Info("Not connected", "Connect to a console first."); return; }
            if (confirm && !Dlg.Confirm(what, what + " now?", what)) return;
            Run(null, what, async () => { await ConsoleService.RunAsync(action); Log.Success(what + " - sent."); });
        }

        private void RefreshInfo()
        {
            Run(null, "Reading console info", async () =>
            {
                var s = await ConsoleService.GetSummaryAsync();
                _info.Clear();
                _info.Set("Name", s.Name);
                _info.Set("IP address", s.Ip);
                _info.Set("Console type", s.ConsoleType);
                _info.Set("Motherboard", s.Motherboard);
                _info.Set("Platform", s.Platform);
                _info.Set("Kernel", s.Kernel);
                _info.Set("XBDM version", s.DmVersion);
                _info.Set("Console ID", s.ConsoleId);
                _info.Set("CPU key", s.CpuKey);
                _info.Set("Running title", s.RunningTitleId);
                _info.Set("State", s.ExecutionState);
                Body.PerformLayout();
            });
        }

        private void LoadModules()
        {
            Run(null, "Listing modules", async () =>
            {
                var mods = await ConsoleService.RunAsync(c => c.GetModules());
                _modules.BeginUpdate();
                _modules.Items.Clear();
                foreach (var m in mods.OrderBy(m => m.BaseAddress))
                {
                    var li = new ListViewItem(m.Name);
                    li.SubItems.Add("0x" + m.BaseAddress.ToString("X8"));
                    li.SubItems.Add(Core.Formats.FileSniffer.FormatSize(m.Size));
                    _modules.Items.Add(li);
                }
                _modules.EndUpdate();
            });
        }

        private void CaptureScreenshot()
        {
            Run(null, "Capturing screenshot", async () =>
            {
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "X360GameHack", "screenshot.png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(tmp));
                bool png = await ConsoleService.RunAsync(c => c.Screenshot(tmp));
                if (!png) throw new InvalidOperationException("The frame buffer format could not be decoded; the raw data was saved next to " + tmp);
                using (var fs = new System.IO.FileStream(tmp, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                using (var img = System.Drawing.Image.FromStream(fs))
                {
                    _shot.Image?.Dispose();
                    _shot.Image = new System.Drawing.Bitmap(img);
                }
                Log.Success("Screenshot captured.");
            });
        }

        private void SaveShot()
        {
            if (_shot.Image == null) { Dlg.Info("Screenshot", "Capture a screenshot first."); return; }
            string f = PickSave("Save screenshot", "xbox360-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", "PNG|*.png");
            if (f == null) return;
            _shot.Image.Save(f, System.Drawing.Imaging.ImageFormat.Png);
            Log.Success("Screenshot saved: " + f);
        }

        /// <summary>Fills the storage list from the console's drive list (only devices that can be copied raw).</summary>
        private void LoadStorage()
        {
            if (!ConsoleService.IsConnected) { Dlg.Info("Not connected", "Connect to a console first."); return; }
            Run(null, "Looking at the console's storage", async () =>
            {
                var list = await ConsoleService.RunAsync(c => StorageDump.Offer(c));
                _storage.Items.Clear();
                foreach (var d in list) _storage.Items.Add(d);
                if (_storage.Items.Count > 0) _storage.SelectedIndex = 0;
                Log.Info(list.Count == 0 ? "This console shows no internal storage that can be copied raw (only the Slim / E / Corona 4 GB memory)." : "Storage that can be copied: " + string.Join("; ", list.Select(d => d.ToString())));
            });
        }

        /// <summary>Copies the chosen storage raw into an image file (read only on the console), then offers to open it in Drive Explorer.</summary>
        private void BackUpStorage()
        {
            if (!ConsoleService.IsConnected) { Dlg.Info("Not connected", "Connect to a console first."); return; }
            if (!(_storage.SelectedItem is StorageDump.Device dev)) { Dlg.Info("Back up storage", "Press Refresh first and pick the storage to copy."); return; }
            string to = PickSave("Back up " + dev.Drive + " to", "xbox360-" + dev.Drive.ToLowerInvariant() + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".img", "Drive images|*.img");
            if (to == null) return;
            var cts = new System.Threading.CancellationTokenSource();
            Run(null, "Backing up " + dev.Drive, async () =>
            {
                string sha = null; long bytes = 0; StorageDump.Route? route = null; string why = null;
                var started = DateTime.Now;
                var progress = new Progress<long>(n => Status.Report(new TransferProgress { File = dev.Drive + " (raw backup)", Done = n, Total = Math.Max(n, dev.KnownSize) }));
                using (Status.Begin("Backing up " + dev.Drive, cts))
                    await ConsoleService.RunAsync(c =>
                    {
                        route = StorageDump.RouteFor(c, out why);
                        if (route == null) return;
                        bytes = StorageDump.Dump(c, route.Value, dev.Path, to, 0, progress, cts.Token, out sha);
                    }, cts.Token);
                if (route == null) { Dlg.Warn("Back up storage", why); return; }
                Log.Success(dev.Drive + " copied: " + Core.Formats.FileSniffer.FormatSize(bytes) + " in " + (int)(DateTime.Now - started).TotalMinutes + " min, SHA-256 " + sha + " -> " + to);
                if (Dlg.Confirm("Back up storage", dev.Drive + " is in\n" + to + "\n\nSHA-256 " + sha + "\n\nOpen it in Drive Explorer now (to browse it or get deleted files back)?", "Open in Drive Explorer"))
                {
                    MainForm.Instance.Navigate(typeof(DriveExplorerPage));
                    MainForm.Instance.GetPage<DriveExplorerPage>().OpenImageFile(to);
                }
            });
        }

        private void SendRaw()
        {
            string cmd = _raw.Text.Trim();
            if (cmd.Length == 0) return;
            if (!ConsoleService.IsConnected) { Dlg.Info("Not connected", "Connect to a console first."); return; }
            Run(null, "XBDM: " + cmd, async () =>
            {
                var r = await ConsoleService.RunAsync(c => c.SendTextCommand(cmd));
                Log.Info("> " + cmd);
                Log.Write(r.IsSuccess ? LogLevel.Success : LogLevel.Warning, (int)r.Status + " " + r.StatusMessage);
                if (!string.IsNullOrEmpty(r.Body))
                    foreach (var line in r.Body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) Log.Tool(line);
            });
        }
    }
}
