using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;
using X360GameHack.UI.Pages;

namespace X360GameHack.UI
{
    public class MainForm : Form
    {
        public static MainForm Instance { get; private set; }

        private readonly Panel _sidebar = new Panel();
        private readonly Panel _content = new Panel();
        private readonly Panel _header = new Panel();
        private readonly Panel _host = new Panel();
        private readonly Panel _logDrawer = new Panel();
        private readonly RichTextBox _log = new RichTextBox();
        private readonly Panel _statusBar = new Panel();
        private readonly XProgress _progress = new XProgress();
        // UseMnemonic off: status lines carry file names, and a Label would otherwise swallow '&' ("Halo & Co" -> "Halo  Co").
        private readonly Label _statusText = new Label { UseMnemonic = false };
        private readonly Label _connText = new Label { UseMnemonic = false };
        private readonly XButton _cancelBtn;
        private readonly XButton _logToggle;
        private readonly TextBlock _pageTitle = new TextBlock("", Theme.PageTitle, Theme.Text);
        private readonly BetaTag _pageBeta = new BetaTag();
        private readonly TextBlock _pageSubtitle = new TextBlock("", Theme.Body, Theme.TextDim);
        private readonly FileChip _fileChip = new FileChip();

        private readonly List<NavItem> _nav = new List<NavItem>();
        private readonly Dictionary<Type, PageBase> _pages = new Dictionary<Type, PageBase>();
        private PageBase _current;
        private readonly Timer _connTimer = new Timer { Interval = 15000 };
        private readonly ShellState _restore;
        private readonly Action _onConsole, _onWorkingFile;
        private static string _titleSuffix = "";

        /// <summary>Set by <see cref="Shell"/> while this window is being replaced (theme change): skip shutdown work.</summary>
        public bool Rebuilding { get; set; }

        /// <summary>
        /// For automated UI tests only (set by reflection, never in normal use): the window and every dialog work as usual
        /// but are not seen - fully transparent and not on the taskbar - so a test does not get in the way of the person
        /// using the PC.
        /// </summary>
        internal static bool TestHidden;
        /// <summary>Automated UI tests only: the hidden window still asks before closing while a drive is in use (normally it closes quietly when tests end).</summary>
        internal static bool TestAskOnClose;


        public MainForm(ShellState restore)
        {
            if (TestHidden) { Opacity = 0; ShowInTaskbar = false; }
            _restore = restore;
            Instance = this;
            Theme.Init(this);
            Dlg.Owner = this;
            XeniaHost.BeforeStart = XeniaSignIn.Ask;          // a game in Xenia starts with a signed-in profile
            AutoScaleMode = AutoScaleMode.None;
            Text = "X360GameHack 2026  " + Pastebin.Version + _titleSuffix;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var s = AppSettings.Current;
            // the screen the window will open on (where its saved position is, else the primary one)
            var area = (restore != null ? Screen.FromRectangle(restore.Bounds) : Screen.PrimaryScreen).WorkingArea;
            // a modest minimum that never forces the window to fill a small or high-DPI screen - the layout reflows and scrolls
            // below it (on a 1920x1080 screen at 200% a DPI-scaled minimum would be wider than the whole screen).
            MinimumSize = new Size(Math.Min(Theme.S(560), area.Width), Math.Min(Theme.S(420), area.Height));
            if (restore != null)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = FitToScreen(restore.Bounds, Screen.FromRectangle(restore.Bounds).WorkingArea);
                WindowState = restore.WindowState;
            }
            else if (s.WindowWidth > 400 && s.WindowHeight > 300)
            {
                // the size and place it was last left at, in this screen's pixels: saved at another scale (or by a build Windows
                // stretched, which saved in 96-DPI units and recorded no DPI) they are scaled, so the window keeps its size on screen
                float k = Theme.DpiScale / (s.WindowDpi > 0 ? s.WindowDpi / 96f : 1f);
                int Px(int v) => (int)Math.Round(v * k);
                var size = new Size(Math.Min(Px(s.WindowWidth), area.Width - Theme.S(16)), Math.Min(Px(s.WindowHeight), area.Height - Theme.S(16)));
                if (s.WindowX != int.MinValue && s.WindowY != int.MinValue)
                {
                    StartPosition = FormStartPosition.Manual;
                    var at = new Point(Px(s.WindowX), Px(s.WindowY));
                    Bounds = FitToScreen(new Rectangle(at, size), Screen.FromPoint(at).WorkingArea);
                }
                else { Size = size; StartPosition = FormStartPosition.CenterScreen; }
                if (s.WindowMaximized) WindowState = FormWindowState.Maximized;
            }
            else
            {
                // a fresh install: a comfortable window filling about two thirds of the screen - never the whole screen
                Size = new Size(Math.Min((int)(area.Width * 0.66), Theme.S(1500)), Math.Min((int)(area.Height * 0.88), Theme.S(1000)));
                StartPosition = FormStartPosition.CenterScreen;
            }

            _cancelBtn = new XButton("Cancel", Glyph.Cancel, BtnKind.Danger) { Height = Theme.S(22), Compact = true, Visible = false };
            _logToggle = new XButton("Log", Glyph.List, BtnKind.Ghost) { Height = Theme.S(22), Compact = true };

            BuildStatusBar();
            BuildLogDrawer();
            BuildSidebar();
            BuildHeader();

            _content.Dock = DockStyle.Fill;
            _content.BackColor = Theme.Window;
            _host.Dock = DockStyle.Fill;
            _host.BackColor = Theme.Window;
            _content.Controls.Add(_host);
            _content.Controls.Add(_header);

            Controls.Add(_content);
            Controls.Add(_logDrawer);
            Controls.Add(_sidebar);
            Controls.Add(_statusBar);

            if (restore != null)
            {
                if (restore.LogHeight > 0) _logDrawer.Height = restore.LogHeight;
                foreach (var entry in Log.Snapshot(1500)) AppendLog(entry, false);
            }

            _onConsole = () => UiInvoke(UpdateConnection);
            _onWorkingFile = () => UiInvoke(OnWorkingFileChanged);
            Log.Written += OnLogWritten;
            Status.Changed += OnStatusChanged;
            ConsoleService.StateChanged += _onConsole;
            WorkingFile.Changed += _onWorkingFile;
            _connTimer.Tick += async (o, e) =>
            {
                if (!ConsoleService.IsConnected || Status.IsBusy) return;
                try { await ConsoleService.RunAsync(c => c.Ping(500)); } catch { }
                UpdateConnection();
            };
            _connTimer.Start();

            DragEnter += (o, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (o, e) => OpenDropped(e.Data.GetData(DataFormats.FileDrop) as string[]);

            UpdateConnection();
            Navigate(restore?.Page ?? StartPage());
            if (restore != null) _fileChip.UpdateFromWorkingFile();
        }

        private static Type StartPage()
        {
            var s = AppSettings.Current;
            if (s.StartOnLastPage && !string.IsNullOrEmpty(s.LastPage))
            {
                var t = typeof(MainForm).Assembly.GetType("X360GameHack.UI.Pages." + s.LastPage, false);
                if (t != null && typeof(PageBase).IsAssignableFrom(t) && !t.IsAbstract) return t;
            }
            return typeof(HomePage);
        }

        /// <summary>Everything a rebuilt window needs to look the same.</summary>
        public ShellState CaptureState() => new ShellState
        {
            Page = _current?.GetType(),
            Bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds,
            WindowState = WindowState == FormWindowState.Minimized ? FormWindowState.Normal : WindowState,
            LogHeight = _logDrawer.Height
        };

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Static events outlive this window: always unsubscribe (the window may be replaced by a theme change).
            Log.Written -= OnLogWritten;
            Status.Changed -= OnStatusChanged;
            ConsoleService.StateChanged -= _onConsole;
            WorkingFile.Changed -= _onWorkingFile;
            _connTimer.Stop();
            _connTimer.Dispose();
            _logTimer?.Stop();
            _logTimer?.Dispose();
            base.OnFormClosed(e);
        }

        // ================================================================ layout pieces

        private void BuildSidebar()
        {
            _sidebar.Dock = DockStyle.Left;
            _sidebar.Width = Theme.S(274);      // room for "Flash Dumps" / "Original Xbox (OG)" plus the BRICKS POSSIBLE / BETA pill
            _sidebar.BackColor = Theme.Sidebar;
            _sidebar.Padding = new Padding(Theme.S(10), Theme.S(14), Theme.S(10), Theme.S(10));
            _sidebar.Paint += (o, e) =>
            {
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, _sidebar.Width - 1, 0, _sidebar.Width - 1, _sidebar.Height);
            };

            var stack = new VStack { Spacing = Theme.S(1), Padding = new Padding(0), Dock = DockStyle.Top };
            var brand = new Panel { Height = Theme.S(58) };
            brand.Paint += (o, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var badge = new RectangleF(Theme.S(6), Theme.S(8), Theme.S(36), Theme.S(36));
                if (Theme.Logo != null)
                {
                    // the app's own logo, scaled with high quality
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(Theme.Logo, badge);
                }
                else
                {
                    AppLogo.Draw(g, badge, AppLogo.TileTop, AppLogo.TileBottom, Theme.AccentHover, Theme.Accent, Color.White);     // our own mark, ring in the theme's accent
                }
                TextRenderer.DrawText(g, "X360GameHack", Theme.Brand, new Point(Theme.S(50), Theme.S(6)), Theme.Text);
                TextRenderer.DrawText(g, "2026  " + Pastebin.Version, Theme.Small, new Point(Theme.S(51), Theme.S(29)), Theme.TextDim);
            };
            stack.Add(brand);

            void Section(string name) => stack.Add(new TextBlock(name, Theme.SmallBold, Theme.TextFaint)
            { Height = Theme.S(22), Flags = TextFormatFlags.Bottom | TextFormatFlags.NoPadding, Padding = new Padding(Theme.S(12), 0, 0, 0) });

            void Item(string text, string glyph, Type page)
            {
                var n = new NavItem(text, glyph, page) { Beta = BetaMarks.PageIsBeta(text) };
                // Flash Dumps changes the NAND images that get flashed to a console: a stronger warning than BETA, because a bad
                // image can still brick it. Only there - the user's call (2026-11-25): Flash Tools does not carry it.
                if (text == "Flash Dumps")
                {
                    n.Beta = false;
                    n.WarnText = "BRICKS POSSIBLE";
                    Tip.Set(n, "Bricks possible still. Writing or flashing a console's NAND can brick it. Everything here works on a copy and the original is never changed - keep it as a backup, and only flash a console that is yours.");
                }
                else if (n.Beta) Tip.Set(n, Theme.BetaTip);
                n.Click += (o, e) => Navigate(page);
                _nav.Add(n);
                stack.Add(n);
            }

            Item("Home", Glyph.Home, typeof(HomePage));
            Section("GAMES");
            Item("XEX Tool", Glyph.Code, typeof(XexPage));
            Item("XEX Disassembler", Glyph.Edit, typeof(XexHackerPage));
            Item("Title Updates", Glyph.Download, typeof(TitleUpdatePage));
            Item("X360Packages", Glyph.Package, typeof(PackagePage));
            Item("Xbox ISOs", Glyph.Disc, typeof(IsoPage));
            Item("Original Xbox (OG)", Glyph.Game, typeof(XbePage));
            Item("Xenia", Glyph.Play, typeof(XeniaPage));                // games, saves, profiles, patches and setup as tabs
            Section("SAVES & CONTENT");
            Item("Saves", Glyph.Save, typeof(SavePage));             // also holds "Create a save" and the GPD viewer as tabs
            Item("Profiles", Glyph.Profile, typeof(ProfilePage));
            Item("Drive Explorer", Glyph.Hdd, typeof(DriveExplorerPage));
            Section("CONSOLE");
            Item("Neighborhood", Glyph.Network, typeof(NeighborhoodPage));
            Item("Console", Glyph.Console, typeof(ConsolePage));
            Item("Peek Poker", Glyph.Memory, typeof(PeekPokePage));
            Item("Debugger", Glyph.Bug, typeof(DebuggerPage));
            Item("FTP", Glyph.Globe, typeof(FtpPage));
            Item("Console Setup", Glyph.Usb, typeof(SetupPage));
            Item("Flash Tools", Glyph.Flash, typeof(FlashToolsPage));
            Item("Flash Dumps", Glyph.Hdd, typeof(NandPage));
            Section("APP");
            Item("Mods", Glyph.Unlock, typeof(ModsPage));
            Item("Q & A", Glyph.Help, typeof(HelpPage));
            Item("Settings", Glyph.Settings, typeof(SettingsPage));

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Sidebar };
            scroll.Controls.Add(stack);
            scroll.Resize += (o, e) => stack.Width = scroll.ClientSize.Width;
            Theme.DarkScrollbars(scroll);
            _sidebar.Controls.Add(scroll);
        }

        private void BuildHeader()
        {
            _header.Dock = DockStyle.Top;
            _header.Height = Theme.S(84);
            _header.BackColor = Theme.Window;
            _header.Padding = new Padding(Theme.S(28), Theme.S(16), Theme.S(28), Theme.S(4));
            _pageTitle.Flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            _pageSubtitle.Flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            _header.Controls.Add(_pageTitle);
            _header.Controls.Add(_pageBeta);
            Tip.Set(_pageBeta, Theme.BetaTip);
            _header.Controls.Add(_pageSubtitle);
            _header.Controls.Add(_fileChip);
            _header.Resize += (o, e) => LayoutHeader();
            _fileChip.OpenClicked += () => OpenWithDialog();
            _fileChip.ClearClicked += WorkingFile.Clear;
            _fileChip.ChipClicked += () => NavigateForKind(WorkingFile.Kind);
        }

        private void LayoutHeader()
        {
            int chipW = Math.Min(Theme.S(420), _header.Width / 2);
            _fileChip.SetBounds(_header.Width - _header.Padding.Right - chipW, Theme.S(18), chipW, Theme.S(52));
            int w = _fileChip.Left - _header.Padding.Left - Theme.S(16);
            int titleW = w;
            if (_pageBeta.Visible)
            {
                int tw = TextRenderer.MeasureText(_pageTitle.Text ?? "", Theme.PageTitle, new Size(int.MaxValue, Theme.S(36)), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + Theme.S(2);
                titleW = Math.Max(0, Math.Min(w - Theme.BetaWidth, tw));
                _pageBeta.SetBounds(_header.Padding.Left + titleW, Theme.S(14), Theme.BetaWidth + Theme.S(2), Theme.S(36));
            }
            _pageTitle.SetBounds(_header.Padding.Left, Theme.S(14), titleW, Theme.S(36));
            _pageSubtitle.SetBounds(_header.Padding.Left, Theme.S(50), w, Theme.S(22));
        }

        /// <summary>The BETA pill next to the page title (nothing on the page has been tested yet).</summary>
        private sealed class BetaTag : Control
        {
            public BetaTag()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Visible = false;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Theme.Window);
                Theme.DrawBeta(e.Graphics, 0, Height / 2 + Theme.S(1), Theme.Window);
            }
        }

        private void BuildLogDrawer()
        {
            _logDrawer.Dock = DockStyle.Bottom;
            _logDrawer.Height = Theme.S(170);
            _logDrawer.BackColor = Theme.Sidebar;
            _logDrawer.Padding = new Padding(Theme.S(12), Theme.S(4), Theme.S(4), Theme.S(4));
            // The log starts closed so pages get the room; the status bar still shows the latest line and an error opens it.
            if (!AppSettings.Current.LogStartsClosed) { AppSettings.Current.LogStartsClosed = true; AppSettings.Current.LogDrawerOpen = false; }
            _logDrawer.Visible = AppSettings.Current.LogDrawerOpen;

            var bar = new HStack { Dock = DockStyle.Top, Height = Theme.S(30), Spacing = Theme.S(4) };
            var title = new TextBlock("Activity log", Theme.SmallBold, Theme.TextDim) { Width = Theme.S(200), Height = Theme.S(24), Flags = TextFormatFlags.VerticalCenter };
            bar.Add(title);
            bar.FillChild = title;
            bar.Add(new XButton("Copy", Glyph.Copy, BtnKind.Ghost, (o, e) => { try { Clipboard.SetText(_log.Text.Length > 0 ? _log.Text : " "); } catch { } }) { Height = Theme.S(26), Compact = true });
            bar.Add(new XButton("Save", Glyph.Save, BtnKind.Ghost, (o, e) => SaveLog()) { Height = Theme.S(26), Compact = true });
            bar.Add(new XButton("Clear", Glyph.Clear, BtnKind.Ghost, (o, e) => { _log.Clear(); Log.Clear(); }) { Height = Theme.S(26), Compact = true });
            bar.Add(new XButton("", Glyph.ChevronDown, BtnKind.Ghost, (o, e) => ToggleLog()) { Height = Theme.S(26), Width = Theme.S(30), Compact = true });

            _log.Dock = DockStyle.Fill;
            _log.ReadOnly = true;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = Theme.Sidebar;
            _log.ForeColor = Theme.TextDim;
            _log.Font = Theme.Mono;
            _log.DetectUrls = false;
            _log.WordWrap = false;
            _log.ScrollBars = RichTextBoxScrollBars.Both;
            Theme.DarkScrollbars(_log);

            var splitter = new Panel { Dock = DockStyle.Top, Height = Theme.S(5), Cursor = Cursors.SizeNS, BackColor = Theme.Sidebar };
            splitter.Paint += (o, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, splitter.Width, 0); };
            int startY = 0, startH = 0;
            bool dragging = false;
            splitter.MouseDown += (o, e) => { dragging = true; startY = Cursor.Position.Y; startH = _logDrawer.Height; };
            splitter.MouseMove += (o, e) =>
            {
                if (!dragging) return;
                _logDrawer.Height = Math.Max(Theme.S(90), Math.Min(Height / 2, startH - (Cursor.Position.Y - startY)));
            };
            splitter.MouseUp += (o, e) => dragging = false;

            _logDrawer.Controls.Add(_log);
            _logDrawer.Controls.Add(bar);
            _logDrawer.Controls.Add(splitter);
        }

        private void BuildStatusBar()
        {
            _statusBar.Dock = DockStyle.Bottom;
            _statusBar.Height = Theme.S(30);
            _statusBar.BackColor = Theme.Sidebar;
            _statusBar.Paint += (o, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, _statusBar.Width, 0); };

            _statusText.AutoSize = false;
            _statusText.ForeColor = Theme.TextDim;
            _statusText.Font = Theme.Small;
            _statusText.TextAlign = ContentAlignment.MiddleLeft;
            _statusText.Text = "Ready";
            _statusText.AutoEllipsis = true;

            _connText.AutoSize = false;
            _connText.Font = Theme.Small;
            _connText.TextAlign = ContentAlignment.MiddleRight;
            _connText.Cursor = Cursors.Hand;
            _connText.Click += (o, e) => Navigate(typeof(NeighborhoodPage));

            _progress.Visible = false;
            _cancelBtn.Click += (o, e) => { Status.Cancel?.Cancel(); Log.Warn("Cancelling..."); };
            _logToggle.Click += (o, e) => ToggleLog();

            _statusBar.Controls.AddRange(new Control[] { _statusText, _progress, _cancelBtn, _connText, _logToggle });
            _statusBar.Resize += (o, e) => LayoutStatus();
        }

        private void LayoutStatus()
        {
            int h = _statusBar.Height;
            int x = Theme.S(12);
            _logToggle.SetBounds(_statusBar.Width - Theme.S(70), (h - _logToggle.Height) / 2, Theme.S(62), _logToggle.Height);
            _connText.SetBounds(_logToggle.Left - Theme.S(330), 0, Theme.S(320), h);
            if (_progress.Visible)
            {
                _progress.SetBounds(x, (h - _progress.Height) / 2, Theme.S(180), _progress.Height);
                x = _progress.Right + Theme.S(10);
            }
            if (_cancelBtn.Visible)
            {
                int cw = _cancelBtn.GetPreferredSize(Size.Empty).Width;
                _cancelBtn.SetBounds(_connText.Left - cw - Theme.S(10), (h - _cancelBtn.Height) / 2, cw, _cancelBtn.Height);
            }
            int right = _cancelBtn.Visible ? _cancelBtn.Left - Theme.S(10) : _connText.Left - Theme.S(10);
            _statusText.SetBounds(x, 0, Math.Max(10, right - x), h);
        }

        // ================================================================ behavior

        private void UiInvoke(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(a); } catch { } }
            else a();
        }

        /// <summary>Pages that are shown as a tab inside another page instead of having their own sidebar entry.</summary>
        private static readonly Dictionary<Type, Type> HostedIn = new Dictionary<Type, Type>
        {
            { typeof(PackageCreatorPage), typeof(SavePage) },
            { typeof(GpdPage), typeof(SavePage) },
            { typeof(XeniaPatchesPage), typeof(XeniaPage) },          // tabs of related pages keep the sidebar short
            { typeof(XeniaSetupPage), typeof(XeniaPage) },
            { typeof(XeniaSettingsPage), typeof(XeniaPage) },
            { typeof(JrpcPage), typeof(ConsolePage) },
            { typeof(IniPage), typeof(SetupPage) },
            { typeof(AboutPage), typeof(SettingsPage) },
            { typeof(ModMakerPage), typeof(ModsPage) },
        };

        /// <summary>Registers a page that another page created to show inside one of its tabs.</summary>
        internal void Adopt(PageBase child)
        {
            EnableFileDrop(child);
            _pages[child.GetType()] = child;
        }

        private PageBase EnsurePage(Type pageType)
        {
            if (_pages.TryGetValue(pageType, out var page)) return page;
            if (HostedIn.TryGetValue(pageType, out Type hostType))
            {
                EnsurePage(hostType);                               // the host creates and adopts it
                if (_pages.TryGetValue(pageType, out page)) return page;
            }
            page = (PageBase)Activator.CreateInstance(pageType);
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            EnableFileDrop(page);
            _pages[pageType] = page;
            _host.Controls.Add(page);
            return page;
        }

        public void Navigate(Type pageType)
        {
            if (HostedIn.TryGetValue(pageType, out Type hostType))
            {
                // a page that lives in a tab: go to the page that holds it and bring its tab forward
                Navigate(hostType);
                EnsurePage(hostType).ShowHosted(pageType);
                return;
            }
            var page = EnsurePage(pageType);
            string nav = _nav.FirstOrDefault(n => n.PageType == pageType)?.Text;
            if (_current == page) { page.OnActivated(); page.ActivateHosted(); BetaMarks.Apply(page, nav); return; }
            SuspendLayout();
            if (_current != null) { _current.OnDeactivated(); _current.Visible = false; }
            _current = page;
            page.Visible = true;
            ResumeLayout();
            _pageTitle.Text = page.Title;
            _pageSubtitle.Text = page.Subtitle;
            _pageBeta.Visible = nav != null && BetaMarks.PageIsBeta(nav);
            LayoutHeader();
            foreach (var n in _nav) n.Selected = n.PageType == pageType;
            AppSettings.Current.LastPage = pageType.Name;
            page.OnActivated();
            page.ActivateHosted();
            BetaMarks.Apply(page, nav);
        }

        public T GetPage<T>() where T : PageBase => (T)EnsurePage(typeof(T));

        /// <summary>Lets plain containers accept dropped files as the new working file.</summary>
        private void EnableFileDrop(Control root)
        {
            if (root is ListView || root is TextBoxBase || root is DropZone || root.AllowDrop && !(root is PageBase)) return;
            if (root is Panel || root is UserControl)
            {
                root.AllowDrop = true;
                root.DragEnter += (o, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
                root.DragDrop += (o, e) => OpenDropped(e.Data.GetData(DataFormats.FileDrop) as string[]);
            }
            foreach (Control c in root.Controls) EnableFileDrop(c);
        }

        public void OpenDropped(string[] files)
        {
            if (files == null || files.Length == 0) return;
            if (files.Length > 1 || Directory.Exists(files[0]))
            {
                // Disc images (or folders full of them) go to the bulk extractor; anything else is treated as packages.
                var isos = new List<string>();
                bool other = false;
                foreach (var f in files)
                {
                    if (File.Exists(f)) { if (FileSniffer.Detect(f) == WorkingFileKind.Iso) isos.Add(f); else other = true; }
                    else if (Directory.Exists(f))
                    {
                        var inside = Directory.GetFiles(f).Where(x => x.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ||
                                                                      x.EndsWith(".xiso", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (inside.Count > 0) isos.AddRange(inside); else other = true;
                    }
                }
                if (isos.Count > 0 && !other)
                {
                    Navigate(typeof(IsoPage));
                    GetPage<IsoPage>().AddIsos(isos);
                    return;
                }
                Navigate(typeof(SetupPage));
                GetPage<SetupPage>().AddPackages(files);
                return;
            }
            WorkingFile.Set(files[0]);
        }

        public void OpenWithDialog()
        {
            using (var d = new OpenFileDialog
            {
                Title = "Open a file to work on",
                Filter = "All supported|*.xex;*.iso;*.xiso;*.xbe;*.bin;*.*|XEX executables|*.xex|Disc images|*.iso;*.xiso|Original Xbox XBE|*.xbe|All files|*.*",
                RestoreDirectory = true
            })
                if (d.ShowDialog(this) == DialogResult.OK) WorkingFile.Set(d.FileName);
        }

        private void OnWorkingFileChanged()
        {
            _fileChip.UpdateFromWorkingFile();
            KeepPage = false;
            foreach (var p in _pages.Values) p.OnWorkingFileChanged();
            bool stay = KeepPage;          // the page on screen said this file is meant for it
            KeepPage = false;
            if (!stay && WorkingFile.Kind != WorkingFileKind.None && !WorkingFile.IsRefresh) NavigateForKind(WorkingFile.Kind);
        }

        /// <summary>Set by a page, while it handles a newly opened file, to keep the window on that page.</summary>
        internal bool KeepPage;

        private void NavigateForKind(WorkingFileKind k)
        {
            switch (k)
            {
                case WorkingFileKind.Xex: case WorkingFileKind.Pe: Navigate(typeof(XexPage)); break;
                case WorkingFileKind.Package: Navigate(typeof(PackagePage)); break;
                case WorkingFileKind.Save: Navigate(typeof(SavePage)); break;
                case WorkingFileKind.Profile: Navigate(typeof(ProfilePage)); break;
                case WorkingFileKind.Iso: Navigate(typeof(IsoPage)); break;
                case WorkingFileKind.Xbe: Navigate(typeof(XbePage)); break;
                case WorkingFileKind.Gpd: Navigate(typeof(GpdPage)); break;
                case WorkingFileKind.Nand: Navigate(typeof(NandPage)); break;
                case WorkingFileKind.DriveImage:
                    Navigate(typeof(DriveExplorerPage));
                    GetPage<DriveExplorerPage>().OpenImageFile(WorkingFile.Path);
                    break;
                case WorkingFileKind.Unknown:
                    Dlg.Warn("Unknown file", "X360GameHack could not identify this file.\n\nSupported: XEX, STFS packages (CON/LIVE/PIRS, usually no extension), saves, ISO/XISO disc images, XBE files, " +
                             "flash (NAND) dumps and Xbox hard drive images.\n\nYou can still inspect it in the XEX Disassembler hex editor.");
                    break;
            }
        }

        // Tools can print thousands of lines; queue them and append in batches so the UI stays responsive.
        private readonly System.Collections.Concurrent.ConcurrentQueue<LogEntry> _logQueue = new System.Collections.Concurrent.ConcurrentQueue<LogEntry>();
        private Timer _logTimer;

        private void OnLogWritten(LogEntry e)
        {
            _logQueue.Enqueue(e);
            if (_logTimer == null && IsHandleCreated)
                UiInvoke(() =>
                {
                    if (_logTimer != null) return;
                    _logTimer = new Timer { Interval = 100 };
                    _logTimer.Tick += (o, a) => FlushLog();
                    _logTimer.Start();
                });
        }

        private void FlushLog()
        {
            if (_logQueue.IsEmpty || _log.IsDisposed) return;
            _log.SuspendLayout();
            int n = 0;
            bool problem = false;
            while (n++ < 2000 && _logQueue.TryDequeue(out var e)) { AppendLog(e, true); problem |= e.Level == LogLevel.Error; }
            if (problem && !_logDrawer.Visible) _logDrawer.Visible = true;       // something failed: show what
            _log.ResumeLayout();
            _log.ScrollToCaret();
        }

        private void AppendLog(LogEntry e, bool updateStatus)
        {
            {
                Color c;
                switch (e.Level)
                {
                    case LogLevel.Success: c = Theme.Success; break;
                    case LogLevel.Warning: c = Theme.Warning; break;
                    case LogLevel.Error: c = Theme.Danger; break;
                    case LogLevel.Tool: c = Theme.TextFaint; break;
                    default: c = Theme.TextDim; break;
                }
                if (_log.TextLength > 400000) { _log.Select(0, 100000); _log.SelectedText = ""; }
                _log.SelectionStart = _log.TextLength;
                _log.SelectionLength = 0;
                _log.SelectionColor = Theme.TextFaint;
                _log.AppendText(e.Time.ToString("HH:mm:ss") + "  ");
                _log.SelectionColor = c;
                _log.AppendText(e.Text + "\n");
                if (updateStatus && e.Level != LogLevel.Tool && !Status.IsBusy)
                {
                    _statusText.ForeColor = c == Theme.TextDim ? Theme.TextDim : c;
                    _statusText.Text = e.Text;
                }
            }
        }

        private void OnStatusChanged()
        {
            UiInvoke(() =>
            {
                bool busy = Status.IsBusy;
                _progress.Visible = busy;
                _progress.Marquee = busy && Status.Percent < 0;
                if (Status.Percent >= 0) _progress.Value = Status.Percent;
                _cancelBtn.Visible = busy && Status.Cancel != null;
                _statusText.ForeColor = Theme.TextDim;
                _statusText.Text = busy ? Status.Text : "Ready";
                UseWaitCursor = false;
                LayoutStatus();
            });
        }

        private void UpdateConnection()
        {
            bool on = ConsoleService.IsConnected;
            _connText.ForeColor = on ? Theme.Success : Theme.TextFaint;
            _connText.Text = on
                ? "●  XBDM  " + (ConsoleService.Name.Length > 0 ? ConsoleService.Name + "  " : "") + ConsoleService.Ip
                : "○  No console connected";
        }

        private void ToggleLog()
        {
            _logDrawer.Visible = !_logDrawer.Visible;
            AppSettings.Current.LogDrawerOpen = _logDrawer.Visible;
        }

        private void SaveLog()
        {
            string to = PageBase.PickSave("Save the log", "X360GameHack-log.txt", "Text|*.txt");
            if (to == null) return;
            try { File.WriteAllText(to, Log.Export()); Log.Success("Log saved to " + to); }
            catch (Exception ex) { Log.Error("Could not save the log: " + ex.Message); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(this);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // started minimized from outside (Windows restarting the program after an update, a shortcut set to "Minimized"):
            // the window still comes up where it was left, so nobody is left looking for it
            if (WindowState == FormWindowState.Minimized && !TestHidden)
            {
                WindowState = AppSettings.Current.WindowMaximized ? FormWindowState.Maximized : FormWindowState.Normal;
                Activate();
            }
            // mods: free ones load now, and paid ones with a remembered token unlock by themselves (off the UI thread)
            var modsStart = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    X360GameHack.Core.Mods.ModManager.Scan();
                    foreach (var r in X360GameHack.Core.Mods.ModManager.UnlockSaved())
                        if (r.Value == null) Log.Success("Unlocked " + r.Key.Info.Name + " with your saved token.");
                        else Log.Warn("Could not unlock " + r.Key.Info.Name + " with the saved token: " + r.Value);
                }
                catch (Exception ex) { Log.Warn("Mods could not be loaded: " + ex.Message); }
            });
            LayoutHeader();
            LayoutStatus();
            if (_restore != null) return;      // rebuilt for a theme change: startup work already happened
            Log.Info("X360GameHack 2026 " + Pastebin.Version + " ready. Drop a file anywhere to begin.");
            HandleStartupArgs();
            foreach (var t in new[] { Tools.XexTool, Tools.PkgTool, Tools.ExtractXiso })
                if (!File.Exists(t)) Log.Warn("Missing tool: " + Path.GetFileName(t) + " (keep it next to X360GameHack.exe)");

            var s = AppSettings.Current;
            if (s.XbdmAutoConnect && !string.IsNullOrWhiteSpace(s.XbdmIp))
                await ConsoleService.ConnectAsync(s.XbdmIp);

            if (s.CheckForUpdates) await CheckForUpdatesAsync(false);
        }

        /// <summary>
        /// The update check - at start-up, and from Settings > Check for updates now (<paramref name="byHand"/>). Sets the
        /// title bar ("Offline" only when Windows says this PC has no internet; otherwise what really happened) and, when a
        /// newer version signed with X360GameHack's update key is out, asks in the update window. Nothing is downloaded or
        /// installed without the user's yes.
        /// </summary>
        public async System.Threading.Tasks.Task CheckForUpdatesAsync(bool byHand)
        {
            var s = AppSettings.Current;
            // both from the program's own Cloudflare address: the signed update notice (the released version) and the open counter
            var checkTask = AppUpdater.CheckAsync(System.Threading.CancellationToken.None);
            var opensTask = byHand ? System.Threading.Tasks.Task.FromResult<long?>(null) : AppUpdater.CountOpenAsync(System.Threading.CancellationToken.None);
            var check = await checkTask;
            long? opens = await opensTask;
            if (IsDisposed) return;
            var info = check.State == UpdateCheckState.Ok ? Pastebin.Evaluate(check.Manifest.Version, Pastebin.Version) : new UpdateInfo();
            // how often the program has been opened: the fresh number, else the one seen last time
            if (opens.HasValue) info.Opens = opens.Value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            if (info.Opens.Length > 0 && info.Opens != s.LastOpens) { s.LastOpens = info.Opens; AppSettings.Save(); }
            if (_titleSuffix.Length > 0 && Text.EndsWith(_titleSuffix, StringComparison.Ordinal)) Text = Text.Substring(0, Text.Length - _titleSuffix.Length);
            _titleSuffix = TitleSuffix(info, s.LastOpens, check);
            Text += _titleSuffix;
            if (check.State != UpdateCheckState.Ok)
            {
                if (check.State == UpdateCheckState.NotTrusted) Log.Warn(check.Explain); else Log.Info(check.Explain);
                if (byHand) Dlg.Info("Check for updates", check.Explain);
                return;
            }
            var m = check.Manifest;
            if (info.State != VersionState.Outdated)
            {
                string same = info.State == VersionState.Latest
                    ? "You have the latest version (" + Pastebin.Version + ")."
                    : "This is a preview version: " + Pastebin.Version + " is newer than the released " + info.Latest + ".";
                if (info.State == VersionState.Preview || byHand) Log.Info(same);
                if (byHand) Dlg.Info("Check for updates", same);
                return;
            }
            Log.Warn("X360GameHack " + m.Version + " is out (this copy is " + Pastebin.Version + "). It is also on " + Pastebin.ReleasesUrl);
            if (!byHand)
            {
                if (!s.AutoUpdate) { Log.Info("Asking to install new versions is off (Settings > General)."); return; }
                if (string.Equals(s.SkippedUpdateVersion, m.Version, StringComparison.OrdinalIgnoreCase)) { Log.Info("You chose to skip " + m.Version + " - Settings > Check for updates now still offers it."); return; }
                if (AppUpdater.LastApplied == m.Version)
                {
                    Log.Warn("X360GameHack " + m.Version + " was installed already, but this copy still says " + Pastebin.Version + " - not offering it again by itself. Get it from " + Pastebin.ReleasesUrl);
                    return;
                }
            }
            if (UpdateDialog.Offer(this, m)) { await System.Threading.Tasks.Task.Delay(500); Close(); }      // the helper installs it once this program has closed
        }

        /// <summary>What follows the name and version in the title bar: how this copy compares with the released one, and how often the program has been opened.</summary>
        public static string TitleSuffix(UpdateInfo info, string opens) => TitleSuffix(info, opens, null);

        /// <summary>As above; without a version to compare with, <paramref name="check"/> says why ("Offline" only when this PC has no internet).</summary>
        public static string TitleSuffix(UpdateInfo info, string opens, UpdateCheckResult check)
        {
            string state;
            switch (info.State)
            {
                case VersionState.Latest: state = "Latest Version"; break;
                case VersionState.Preview: state = "Preview Version"; break;
                case VersionState.Outdated: state = "Not The Latest Version (" + info.Latest + " is out)"; break;
                default: state = check != null && check.State != UpdateCheckState.Ok ? check.Short : "Offline"; break;
            }
            return "  ||  " + state + (string.IsNullOrEmpty(opens) ? "" : "  ||  Opened " + opens + " times!");
        }

        /// <summary>Supports "Open with" (a file path argument) and --page &lt;Name&gt; to start on a page.</summary>
        private void HandleStartupArgs()
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToList();
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == "--page" && i + 1 < args.Count)
                {
                    var t = typeof(MainForm).Assembly.GetType("X360GameHack.UI.Pages." + args[++i], false, true);
                    if (t != null && typeof(PageBase).IsAssignableFrom(t)) Navigate(t);
                }
                else if (File.Exists(args[i])) WorkingFile.Set(args[i]);
            }
        }

        /// <summary>Shrinks a window rectangle to fit a screen's working area and nudges it fully on-screen.</summary>
        private static Rectangle FitToScreen(Rectangle b, Rectangle area)
        {
            int w = Math.Min(b.Width, area.Width), h = Math.Min(b.Height, area.Height);
            int x = Math.Max(area.Left, Math.Min(b.X, area.Right - w));
            int y = Math.Max(area.Top, Math.Min(b.Y, area.Bottom - h));
            return new Rectangle(x, y, w, h);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // closing while files go onto an Xbox drive (or Windows copies them off it after a drag) cuts that short - ask first
            if (e.CloseReason == CloseReason.UserClosing && !Rebuilding && (!TestHidden || TestAskOnClose) &&
                _pages.TryGetValue(typeof(DriveExplorerPage), out var dp) && dp is DriveExplorerPage dx && dx.InUse)
            {
                string what = dx.Writing ? "Files are still being written to the Xbox drive - closing now can leave them damaged."
                                         : "The Xbox drive is still being read (a copy off it is running) - closing now stops it.";
                if (!Dlg.Confirm("Close X360GameHack?", what + "\n\nWait for it to finish (see the status bar), or close anyway?", "Close anyway", true))
                {
                    e.Cancel = true;
                    return;
                }
            }
            var s = AppSettings.Current;
            s.WindowMaximized = WindowState == FormWindowState.Maximized;
            if (WindowState == FormWindowState.Normal)
            {
                s.WindowWidth = Width; s.WindowHeight = Height; s.WindowX = Left; s.WindowY = Top;
                s.WindowDpi = (int)Math.Round(Theme.DpiScale * 96);              // the pixels these are in
            }
            AppSettings.Save();
            _logTimer?.Stop();
            if (!Rebuilding)
            {
                FlushLog();
                if (ConsoleService.IsConnected) ConsoleService.Disconnect();
                X360GameHack.Core.Mods.ModManager.LockAll();
                KeyVaultSession.Release();
            }
            base.OnFormClosing(e);
        }

        // ================================================================ small shell controls

        private sealed class NavItem : Control, IAnimated
        {
            private readonly string _glyph;
            private bool _hover, _selected;
            private float _glow, _on;   // hover fade, selection fade
            public Type PageType { get; }
            /// <summary>Nothing on this page has been tested yet: a BETA pill sits at the end of the row.</summary>
            public bool Beta { get; set; }
            /// <summary>A stronger red warning pill instead of BETA (Flash Dumps: a changed NAND image, once flashed, can still brick a console).</summary>
            public string WarnText { get; set; }

            bool IAnimated.Tick()
            {
                if (IsDisposed) return false;
                bool a = Anim.Ease(ref _glow, _hover ? 1f : 0f), b = Anim.Ease(ref _on, _selected ? 1f : 0f);
                Invalidate();
                return a || b;
            }

            public NavItem(string text, string glyph, Type page)
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Text = text;
                _glyph = glyph;
                PageType = page;
                Height = Theme.S(29);
                Cursor = Cursors.Hand;
                Font = Theme.Body;
            }

            public bool Selected
            {
                get => _selected;
                set
                {
                    if (_selected == value) return;
                    _selected = value;
                    if (IsHandleCreated && Visible) Anim.Run(this);
                    else { _on = value ? 1f : 0f; Invalidate(); }
                }
            }
            protected override void OnMouseEnter(EventArgs e) { _hover = true; Anim.Run(this); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Anim.Run(this); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Sidebar);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var pill = new RectangleF(0, 1, Width - 1, Height - 3);
                if (_on > 0 || _glow > 0)
                {
                    // hover = a quiet lift; selected = the accent tint fading out to the right
                    Color hover = Theme.Blend(Theme.Sidebar, Theme.SurfaceHover, _glow);
                    Color left = Theme.Blend(hover, Theme.AccentSoft, _on), right = Theme.Blend(hover, Theme.Blend(Theme.AccentSoft, Theme.Sidebar, .65f), _on);
                    using (var path = Theme.Rounded(pill, Theme.S(8)))
                    using (var b = new LinearGradientBrush(new RectangleF(pill.X - 1, pill.Y, pill.Width + 2, pill.Height), left, right, 0f)) g.FillPath(b, path);
                }
                if (_on > 0)
                {
                    float h = (Height - Theme.S(18)) * _on;
                    using (var path = Theme.Rounded(new RectangleF(0, (Height - h) / 2f, Theme.S(3), h), 1.5f))
                    using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                }
                var fg = Theme.Blend(Theme.TextDim, Theme.Text, Math.Max(_on, _glow));
                TextRenderer.DrawText(g, _glyph, Theme.Icon, new Rectangle(Theme.S(12), 0, Theme.S(22), Height),
                    Theme.Blend(fg, Theme.AccentHover, _on), TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                int rightReserve = WarnText != null ? Theme.WarnWidth(WarnText) + Theme.S(4) : (Beta ? Theme.BetaWidth + Theme.S(4) : 0);
                int textW = Width - Theme.S(48) - rightReserve;
                TextRenderer.DrawText(g, Text, _selected ? Theme.BodyBold : Theme.Body, new Rectangle(Theme.S(44), 0, textW, Height),
                    fg, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (WarnText != null) Theme.DrawWarn(g, Width - Theme.S(8), Height / 2, Theme.Sidebar, WarnText);
                else if (Beta) Theme.DrawBeta(g, Width - Theme.BetaWidth - Theme.S(8), Height / 2, Theme.Sidebar);
            }
        }

        /// <summary>Header pill showing the working file with Open / Clear actions.</summary>
        private sealed class FileChip : Control
        {
            private Rectangle _openRect, _clearRect;
            private int _hover; // 0 none, 1 chip, 2 open, 3 clear
            public event Action OpenClicked, ClearClicked, ChipClicked;

            public FileChip()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
                AllowDrop = true;
                DragEnter += (o, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
                DragDrop += (o, e) => Instance?.OpenDropped(e.Data.GetData(DataFormats.FileDrop) as string[]);
                Tip.Set(this, "Working file - drop a file here or anywhere in the window");
            }

            public void UpdateFromWorkingFile() => Invalidate();

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int h = _openRect.Contains(e.Location) ? 2 : _clearRect.Contains(e.Location) ? 3 : 1;
                if (h != _hover) { _hover = h; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { _hover = 0; Invalidate(); }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                if (_openRect.Contains(e.Location)) OpenClicked?.Invoke();
                else if (_clearRect.Contains(e.Location)) ClearClicked?.Invoke();
                else if (WorkingFile.Kind != WorkingFileKind.None) ChipClicked?.Invoke();
                else OpenClicked?.Invoke();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Window);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(10)))
                using (var b = new SolidBrush(_hover == 1 ? Theme.SurfaceHover : Theme.Surface))
                using (var p = new Pen(Theme.Border)) { g.FillPath(b, path); g.DrawPath(p, path); }

                bool has = WorkingFile.Kind != WorkingFileKind.None;
                string glyph = Glyph.Document;
                switch (WorkingFile.Kind)
                {
                    case WorkingFileKind.Xex: glyph = Glyph.Code; break;
                    case WorkingFileKind.Package: glyph = Glyph.Package; break;
                    case WorkingFileKind.Save: glyph = Glyph.Save; break;
                    case WorkingFileKind.Profile: glyph = Glyph.Profile; break;
                    case WorkingFileKind.Iso: glyph = Glyph.Disc; break;
                    case WorkingFileKind.Xbe: glyph = Glyph.Game; break;
                    case WorkingFileKind.Gpd: glyph = Glyph.List; break;
                    case WorkingFileKind.Nand: glyph = Glyph.Hdd; break;
                    case WorkingFileKind.DriveImage: glyph = Glyph.Hdd; break;
                }
                TextRenderer.DrawText(g, glyph, Theme.IconLarge, new Rectangle(Theme.S(8), 0, Theme.S(40), Height),
                    has ? Theme.Accent : Theme.TextFaint, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

                int btn = Theme.S(30);
                _clearRect = has ? new Rectangle(Width - btn - Theme.S(10), (Height - btn) / 2, btn, btn) : Rectangle.Empty;
                _openRect = new Rectangle((has ? _clearRect.Left : Width - Theme.S(10)) - btn - Theme.S(4), (Height - btn) / 2, btn, btn);
                DrawIconBtn(g, _openRect, Glyph.FolderOpen, _hover == 2);
                if (has) DrawIconBtn(g, _clearRect, Glyph.Cancel, _hover == 3);

                int tx = Theme.S(52), tw = _openRect.Left - tx - Theme.S(6);
                string name = has ? WorkingFile.Name : "No working file";
                string sub = has ? FileSniffer.Describe(WorkingFile.Kind) + "  •  " + WorkingFile.SizeText : "Drop a file or click to open";
                TextRenderer.DrawText(g, name, Theme.BodyBold, new Rectangle(tx, Theme.S(8), tw, Theme.S(20)), has ? Theme.Text : Theme.TextDim,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(tx, Theme.S(28), tw, Theme.S(18)), Theme.TextDim,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }

            private static void DrawIconBtn(Graphics g, Rectangle r, string glyph, bool hover)
            {
                if (hover)
                    using (var path = Theme.Rounded(r, Theme.S(6)))
                    using (var b = new SolidBrush(Theme.Border)) g.FillPath(b, path);
                TextRenderer.DrawText(g, glyph, Theme.Icon, r, hover ? Theme.Text : Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
