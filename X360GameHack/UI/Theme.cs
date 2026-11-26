using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace X360GameHack.UI
{
    /// <summary>Colors, fonts and native dark-mode helpers for the whole app.</summary>
    public static class Theme
    {
        // --- palette: set from the active ThemePalette (Settings > Appearance) before any window is built ---
        public static Color Window { get; private set; }
        public static Color Sidebar { get; private set; }
        public static Color Surface { get; private set; }
        public static Color SurfaceHover { get; private set; }
        public static Color Input { get; private set; }
        public static Color Border { get; private set; }
        public static Color BorderStrong { get; private set; }
        public static Color Text { get; private set; }
        public static Color TextDim { get; private set; }
        public static Color TextFaint { get; private set; }
        public static Color Accent { get; private set; }
        public static Color AccentHover { get; private set; }
        public static Color AccentPressed { get; private set; }
        public static Color AccentSoft { get; private set; }
        public static Color Danger { get; private set; }
        public static Color DangerSoft { get; private set; }
        public static Color Warning { get; private set; }
        public static Color Info { get; private set; }
        public static Color Success { get; private set; }

        // --- derived ---
        /// <summary>Label color on top of an accent fill (white, or near-black for very light accents).</summary>
        public static Color OnAccent { get; private set; }
        /// <summary>Label color on a danger-tinted button.</summary>
        public static Color DangerText { get; private set; }
        /// <summary>Hairline between list rows.</summary>
        public static Color Divider { get; private set; }
        /// <summary>Folder icons in file lists.</summary>
        public static Color FolderIcon { get; private set; }
        public static bool IsLight { get; private set; }
        public static Core.Services.ThemePalette Palette { get; private set; }

        static Theme() { Use(Core.Services.ThemePalette.BuiltIns[0]); }

        /// <summary>Switches the palette. Windows built afterwards use it; the shell rebuilds itself to apply it live.</summary>
        public static void Use(Core.Services.ThemePalette p)
        {
            Palette = p;
            IsLight = p.Light;
            Window = p.Get("Window"); Sidebar = p.Get("Sidebar"); Surface = p.Get("Surface"); SurfaceHover = p.Get("SurfaceHover");
            Input = p.Get("Input"); Border = p.Get("Border"); BorderStrong = p.Get("BorderStrong");
            Text = p.Get("Text"); TextDim = p.Get("TextDim"); TextFaint = p.Get("TextFaint");
            Accent = p.Get("Accent"); AccentHover = p.Get("AccentHover"); AccentPressed = p.Get("AccentPressed"); AccentSoft = p.Get("AccentSoft");
            Danger = p.Get("Danger"); DangerSoft = p.Get("DangerSoft"); Warning = p.Get("Warning"); Info = p.Get("Info"); Success = p.Get("Success");
            OnAccent = Core.Services.ThemePalette.OnColor(Accent);
            DangerText = IsLight ? Blend(Danger, Color.Black, .15f) : Blend(Danger, Color.White, .45f);
            Divider = Blend(Input, Border, .45f);
            FolderIcon = IsLight ? Color.FromArgb(214, 158, 36) : Color.FromArgb(240, 196, 80);
        }

        // --- fonts ---
        public static readonly Font Body = new Font("Segoe UI", 9.5f);
        public static readonly Font BodyBold = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font SmallBold = new Font("Segoe UI Semibold", 8f);
        public static readonly Font CardTitle = new Font("Segoe UI Semibold", 11f);
        public static readonly Font PageTitle = new Font("Segoe UI Semibold", 17f);
        public static readonly Font Brand = new Font("Segoe UI Semibold", 13f);
        public static readonly Font Mono = CreateMono(9.5f);
        public static readonly Font Icon = CreateIconFont(11f);
        public static readonly Font IconLarge = CreateIconFont(22f);
        public static readonly Font IconHuge = CreateIconFont(40f);

        private static Font CreateMono(float size)
        {
            foreach (var name in new[] { "Cascadia Mono", "Consolas", "Courier New" })
            {
                var f = new Font(name, size);
                if (f.Name == name) return f;
                f.Dispose();
            }
            return new Font(FontFamily.GenericMonospace, size);
        }

        private static Font CreateIconFont(float size)
        {
            foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                var f = new Font(name, size);
                if (f.Name == name) return f;
                f.Dispose();
            }
            return new Font("Segoe UI Symbol", size);
        }

        public static float DpiScale { get; private set; } = 1f;

        public static void Init(Control reference)
        {
            using (var g = reference.CreateGraphics()) DpiScale = g.DpiX / 96f;
        }

        /// <summary>Scale a 96-DPI pixel value to the current DPI.</summary>
        public static int S(int px) => (int)Math.Round(px * DpiScale);

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            // a control squeezed to nothing (a minimized window, a row narrower than its buttons): nothing to draw - GDI+
            // refuses an arc of no size, and a paint error would show for every such control
            if (!(r.Width > 0 && r.Height > 0)) return path;
            float d = radius * 2;
            if (d <= 0) { path.AddRectangle(r); return path; }
            d = Math.Min(d, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>A soft light edge along the top of a raised surface (button, card, pill).</summary>
        public static Color TopEdge => IsLight ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(26, 255, 255, 255);

        /// <summary>Fills a rounded shape with a gentle top-to-bottom gradient of <paramref name="color"/> (lighter above, darker below).</summary>
        public static void FillRaised(Graphics g, RectangleF r, float radius, Color color, float strength = 1f)
        {
            if (r.Width < 1 || r.Height < 1) return;
            using (var path = Rounded(r, radius))
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                       Blend(color, Color.White, .11f * strength), Blend(color, Color.Black, .10f * strength), 90f))
                g.FillPath(b, path);
        }

        /// <summary>The one-pixel light edge inside the top of a rounded shape.</summary>
        public static void DrawTopEdge(Graphics g, RectangleF r, float radius)
        {
            if (r.Width < radius * 2 + 2) return;
            using (var p = new Pen(TopEdge))
                g.DrawLine(p, r.X + radius, r.Y + 1, r.Right - radius, r.Y + 1);
        }

        // ------------------------------------------------------------ BETA label (parts that have not been tested yet)

        private static readonly Font BetaFont = new Font("Segoe UI Semibold", 6.75f);
        public const string BetaText = "BETA";
        public const string BetaTip = "BETA - not proven to work end to end yet. It may not work as expected: keep a backup of anything it changes.";

        private static int BetaTextWidth() =>
            TextRenderer.MeasureText(BetaText, BetaFont, new Size(1000, 100), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;

        /// <summary>Width of the BETA pill, including the gap in front of it.</summary>
        public static int BetaWidth => BetaTextWidth() + S(10) + S(6);

        /// <summary>Draws the BETA pill after a small gap from <paramref name="x"/>, centred on <paramref name="centerY"/>.</summary>
        public static void DrawBeta(Graphics g, int x, int centerY, Color background)
        {
            int h = S(15), w = BetaTextWidth() + S(10);
            var r = new RectangleF(x + S(6) + .5f, centerY - h / 2f + .5f, w - 1, h - 1);
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(r, h / 2f))
            using (var b = new SolidBrush(Blend(background, Warning, .18f)))
            using (var p = new Pen(Blend(background, Warning, .55f)))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            g.SmoothingMode = old;
            TextRenderer.DrawText(g, BetaText, BetaFont, Rectangle.Round(r), Warning,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        /// <summary>The width a red warning pill (<see cref="DrawWarn"/>) takes for the given text.</summary>
        public static int WarnWidth(string text) =>
            TextRenderer.MeasureText(text, BetaFont, new Size(1000, 100), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + S(10) + S(6);

        /// <summary>A red warning pill (e.g. "BRICKS POSSIBLE"), its right edge at <paramref name="rightX"/> - a stronger note than BETA.</summary>
        public static void DrawWarn(Graphics g, int rightX, int centerY, Color background, string text)
        {
            int tw = TextRenderer.MeasureText(text, BetaFont, new Size(1000, 100), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            int h = S(15), w = tw + S(10);
            var r = new RectangleF(rightX - w + .5f, centerY - h / 2f + .5f, w - 1, h - 1);
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(r, h / 2f))
            using (var b = new SolidBrush(Blend(background, Danger, .22f)))
            using (var p = new Pen(Blend(background, Danger, .60f)))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            g.SmoothingMode = old;
            TextRenderer.DrawText(g, text, BetaFont, Rectangle.Round(r), Danger,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        /// <summary>Rounded tile with an icon in it - the "chip" in front of card titles and on tiles.</summary>
        public static void DrawIconChip(Graphics g, Rectangle r, string glyph, Font font, float emphasis = 0f)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rf = new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1);
            using (var path = Rounded(rf, r.Height * .28f))
            using (var b = new LinearGradientBrush(rf, Blend(AccentSoft, Accent, .10f + .25f * emphasis), Blend(AccentSoft, Window, .15f), 60f))
            using (var p = new Pen(Blend(AccentSoft, Accent, .35f + .4f * emphasis)))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            g.SmoothingMode = old;
            TextRenderer.DrawText(g, glyph, font, r, Blend(Accent, AccentHover, .5f + .5f * emphasis),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static Image _logo;
        private static bool _logoLooked;

        /// <summary>
        /// The app's own logo for the sidebar and About page: "logo.png" next to the program, else the copy built into
        /// it, else null (a drawn badge is used instead).
        /// </summary>
        public static Image Logo
        {
            get
            {
                if (_logoLooked) return _logo;
                _logoLooked = true;
                try
                {
                    string side = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Theme).Assembly.Location), "logo.png");
                    if (System.IO.File.Exists(side) && new System.IO.FileInfo(side).Length < 4 * 1024 * 1024)
                        using (var ms = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(side))) _logo = new Bitmap(Image.FromStream(ms));
                    else
                        using (var s = typeof(Theme).Assembly.GetManifestResourceStream("X360GameHack.Other.logo.png"))
                            if (s != null) _logo = new Bitmap(Image.FromStream(s));
                }
                catch { _logo = null; }
                return _logo;
            }
        }

        // --- native helpers ---
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// <summary>Title bar matching the theme (Windows 10 1809+ / Windows 11): dark or light, caption and text colors on Win11.</summary>
        public static void ApplyTitleBar(Form form)
        {
            try
            {
                int dark = IsLight ? 0 : 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref dark, 4) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref dark, 4);
                int caption = ColorTranslator.ToWin32(Sidebar);
                DwmSetWindowAttribute(form.Handle, 35, ref caption, 4); // DWMWA_CAPTION_COLOR (Win11)
                int text = ColorTranslator.ToWin32(Text);
                DwmSetWindowAttribute(form.Handle, 36, ref text, 4);    // DWMWA_TEXT_COLOR (Win11)
            }
            catch { /* older Windows */ }
        }

        private static string NativeTheme => IsLight ? "Explorer" : "DarkMode_Explorer";

        /// <summary>Theme-matching scrollbars for native controls (ListView, TreeView, TextBox...).</summary>
        public static void DarkScrollbars(Control c)
        {
            try
            {
                if (c.IsHandleCreated) SetWindowTheme(c.Handle, NativeTheme, null);
                else c.HandleCreated += (s, e) => SetWindowTheme(((Control)s).Handle, NativeTheme, null);
            }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Paints the part of a ListView header to the right of the last column in the theme's colors
        /// (owner-drawn headers only paint the columns themselves).
        /// </summary>
        public static void ThemeListHeader(ListView lv)
        {
            try
            {
                // The items-view theme (what Explorer uses) gives themed scrollbars without drawing column lines in empty space.
                SetWindowTheme(lv.Handle, IsLight ? "ItemsView" : "DarkMode_ItemsView", null);
                IntPtr header = SendMessage(lv.Handle, 0x101F /* LVM_GETHEADER */, IntPtr.Zero, IntPtr.Zero);
                if (header != IntPtr.Zero) SetWindowTheme(header, IsLight ? "ItemsView" : "DarkMode_ItemsView", null);
            }
            catch { }
        }

        /// <summary>Grey placeholder text for a native TextBox.</summary>
        public static void SetCue(TextBox tb, string cue)
        {
            const int EM_SETCUEBANNER = 0x1501;
            if (tb.IsHandleCreated) SendMessage(tb.Handle, EM_SETCUEBANNER, (IntPtr)1, cue);
            else tb.HandleCreated += (s, e) => SendMessage(tb.Handle, EM_SETCUEBANNER, (IntPtr)1, cue);
        }
    }

    /// <summary>Segoe Fluent / MDL2 icon code points.</summary>
    public static class Glyph
    {
        public const string Profile = "";   // MDL2 Contact (person)
        public const string Home = "";
        public const string App = "";
        public const string Code = "";
        public const string Package = "";
        public const string Save = "";
        public const string Disc = "";
        public const string Game = "";
        public const string Edit = "";
        public const string Globe = "";
        public const string Network = "";
        public const string Memory = "";
        public const string Flash = "";
        public const string Help = "";
        public const string Settings = "";
        public const string Info = "";
        public const string Usb = "";
        public const string Folder = "";
        public const string FolderOpen = "";
        public const string Document = "";
        public const string Upload = "";
        public const string Download = "";
        public const string Refresh = "";
        public const string Delete = "";
        public const string Rename = "";
        public const string NewFolder = "";
        public const string Back = "";
        public const string Up = "";
        public const string Play = "";
        public const string Power = "";
        public const string Camera = "";
        public const string Search = "";
        public const string Link = "";
        public const string Unlink = "";
        public const string Copy = "";
        public const string OpenFile = "";
        public const string Lock = "";
        public const string Unlock = "";
        public const string Check = "";
        public const string Cancel = "";
        public const string Add = "";
        public const string Remove = "";
        public const string Warning = "";
        public const string Error = "";
        public const string Console = "";
        public const string Wrench = "";
        public const string Chat = "";
        public const string Heart = "";
        public const string Bug = "";
        public const string Video = "";
        public const string Tag = "";
        public const string Hdd = "";
        public const string Snow = "";
        public const string Send = "";
        public const string Eject = "";
        public const string Clear = "";
        public const string List = "";
        public const string Image = "";
        public const string Shield = "";
        public const string Speed = "";
        public const string Sparkle = "";
        public const string Library = "";
        public const string ChevronRight = "";
        public const string ChevronDown = "";
    }
}
