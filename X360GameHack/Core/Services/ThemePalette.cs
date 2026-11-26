using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// A named color scheme. Built-in themes ship with the app; custom ones are saved in settings.json and can be
    /// exported / imported as small JSON files (every value is validated on load - only #RRGGBB colors are accepted).
    /// </summary>
    public sealed class ThemePalette
    {
        public const int MaxCustomThemes = 40;

        public string Name { get; set; } = "Custom theme";
        public bool Light { get; set; }
        public string Window { get; set; }
        public string Sidebar { get; set; }
        public string Surface { get; set; }
        public string SurfaceHover { get; set; }
        public string Input { get; set; }
        public string Border { get; set; }
        public string BorderStrong { get; set; }
        public string Text { get; set; }
        public string TextDim { get; set; }
        public string TextFaint { get; set; }
        public string Accent { get; set; }
        public string AccentHover { get; set; }
        public string AccentPressed { get; set; }
        public string AccentSoft { get; set; }
        public string Danger { get; set; }
        public string DangerSoft { get; set; }
        public string Warning { get; set; }
        public string Info { get; set; }
        public string Success { get; set; }

        [JsonIgnore] public bool BuiltIn { get; private set; }

        /// <summary>Editable color slots, in display order: (property, friendly label).</summary>
        public static readonly (string Key, string Label)[] Slots =
        {
            ("Window", "Window background"), ("Sidebar", "Sidebar & status bar"), ("Surface", "Cards"), ("SurfaceHover", "Hover"),
            ("Input", "Inputs & lists"), ("Border", "Borders"), ("BorderStrong", "Strong borders"),
            ("Text", "Text"), ("TextDim", "Secondary text"), ("TextFaint", "Faint text"),
            ("Accent", "Accent"), ("AccentHover", "Accent hover"), ("AccentPressed", "Accent pressed"), ("AccentSoft", "Accent tint"),
            ("Danger", "Danger"), ("DangerSoft", "Danger tint"), ("Warning", "Warning"), ("Info", "Info"), ("Success", "Success"),
        };

        public Color Get(string key) => Parse((string)typeof(ThemePalette).GetProperty(key).GetValue(this)) ?? Color.Magenta;
        public void Set(string key, Color c) => typeof(ThemePalette).GetProperty(key).SetValue(this, ToHex(c));

        public ThemePalette Clone()
        {
            var p = (ThemePalette)MemberwiseClone();
            p.BuiltIn = false;
            return p;
        }

        // ------------------------------------------------------------------ built-in themes

        public static IReadOnlyList<ThemePalette> BuiltIns { get; } = CreateBuiltIns();

        /// <summary>The theme a fresh install starts on.</summary>
        public const string DefaultName = "OLED Black";

        /// <summary>Built-in themes followed by the user's saved themes.</summary>
        public static IEnumerable<ThemePalette> All() => BuiltIns.Concat(AppSettings.Current.CustomThemes ?? new List<ThemePalette>());

        public static ThemePalette Find(string name) => All().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The theme chosen in Settings (falls back to the default).</summary>
        public static ThemePalette Active() => Find(AppSettings.Current.ThemeName) ?? Find(DefaultName) ?? BuiltIns[0];

        private static List<ThemePalette> CreateBuiltIns()
        {
            var list = new List<ThemePalette>
            {
                new ThemePalette
                {
                    Name = "Xbox Green", Light = false,
                    Window = "#0E1014", Sidebar = "#13161B", Surface = "#191D23", SurfaceHover = "#21262E", Input = "#20252D",
                    Border = "#2E343E", BorderStrong = "#404854", Text = "#E7EAEF", TextDim = "#969FAC", TextFaint = "#68707D",
                    Accent = "#22A64B", AccentHover = "#2EBE5C", AccentPressed = "#1A883D", AccentSoft = "#1C3C28",
                    Danger = "#E5484D", DangerSoft = "#482024", Warning = "#F5A524", Info = "#4D94FF", Success = "#46C86E"
                },
                Generate("Midnight Blue", false, Color.FromArgb(59, 130, 246), Color.FromArgb(12, 16, 26)),
                Generate("Crimson", false, Color.FromArgb(225, 45, 70), Color.FromArgb(19, 13, 16)),
                Generate("Royal Purple", false, Color.FromArgb(139, 92, 246), Color.FromArgb(16, 13, 25)),
                Generate("Amber", false, Color.FromArgb(222, 128, 16), Color.FromArgb(19, 16, 12)),
                Generate("Teal", false, Color.FromArgb(20, 170, 160), Color.FromArgb(11, 18, 19)),
                Generate("Graphite", false, Color.FromArgb(150, 160, 175), Color.FromArgb(20, 21, 23)),
                Generate("OLED Black", false, Color.FromArgb(34, 166, 75), Color.FromArgb(0, 0, 0)),
                Generate("Arctic Light", true, Color.FromArgb(37, 99, 235), Color.FromArgb(242, 244, 247)),
                Generate("Daylight Green", true, Color.FromArgb(22, 128, 61), Color.FromArgb(243, 246, 243)),
            };
            foreach (var p in list) p.BuiltIn = true;
            return list;
        }

        /// <summary>Builds a full, readable palette from a base (dark / light), an accent and a background color.</summary>
        public static ThemePalette Generate(string name, bool light, Color accent, Color background)
        {
            var p = new ThemePalette { Name = name, Light = light };
            Color bg = background;
            if (!light)
            {
                p.Window = ToHex(bg);
                p.Sidebar = ToHex(Mix(bg, Color.White, .022f));
                p.Surface = ToHex(Mix(bg, Color.White, .045f));
                p.SurfaceHover = ToHex(Mix(bg, Color.White, .08f));
                p.Input = ToHex(Mix(bg, Color.White, .075f));
                p.Border = ToHex(Mix(bg, Color.White, .135f));
                p.BorderStrong = ToHex(Mix(bg, Color.White, .21f));
                p.Text = "#E7EAEF"; p.TextDim = "#969FAC"; p.TextFaint = "#68707D";
                p.Accent = ToHex(accent);
                p.AccentHover = ToHex(Mix(accent, Color.White, .14f));
                p.AccentPressed = ToHex(Mix(accent, Color.Black, .18f));
                p.AccentSoft = ToHex(Mix(Mix(bg, Color.White, .045f), accent, .2f));
                p.Danger = "#E5484D"; p.DangerSoft = ToHex(Mix(bg, Color.FromArgb(229, 72, 77), .24f));
                p.Warning = "#F5A524"; p.Info = "#4D94FF"; p.Success = "#46C86E";
            }
            else
            {
                p.Window = ToHex(bg);
                p.Sidebar = ToHex(Mix(bg, Color.Black, .035f));
                p.Surface = "#FFFFFF";
                p.SurfaceHover = ToHex(Mix(bg, Color.Black, .05f));
                p.Input = ToHex(Mix(Color.White, bg, .55f));
                p.Border = ToHex(Mix(bg, Color.Black, .12f));
                p.BorderStrong = ToHex(Mix(bg, Color.Black, .24f));
                p.Text = "#171A1F"; p.TextDim = "#4E5662"; p.TextFaint = "#7D8591";
                p.Accent = ToHex(accent);
                p.AccentHover = ToHex(Mix(accent, Color.Black, .1f));
                p.AccentPressed = ToHex(Mix(accent, Color.Black, .22f));
                p.AccentSoft = ToHex(Mix(Color.White, accent, .14f));
                p.Danger = "#C8323C"; p.DangerSoft = ToHex(Mix(Color.White, Color.FromArgb(200, 50, 60), .12f));
                p.Warning = "#B26B00"; p.Info = "#1F63D6"; p.Success = "#15803D";
            }
            return p;
        }

        // ------------------------------------------------------------------ validation / JSON

        private static readonly Regex HexColor = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

        public static Color? Parse(string hex)
        {
            if (hex == null || !HexColor.IsMatch(hex)) return null;
            int v = int.Parse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        }

        public static string ToHex(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        public static Color Mix(Color a, Color b, float t)
            => Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

        public static string CleanName(string name)
        {
            string n = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (n.Length > 40) n = n.Substring(0, 40).Trim();
            return n.Length == 0 ? "Custom theme" : n;
        }

        /// <summary>
        /// Repairs a palette from an untrusted source: cleans the name and replaces any missing or malformed color with
        /// the value generated from its accent and window colors. Returns false if it is unusable (no valid window/accent).
        /// </summary>
        public bool Sanitize()
        {
            Name = CleanName(Name);
            var bg = Parse(Window);
            var accent = Parse(Accent);
            if (bg == null || accent == null) return false;
            var gen = Generate(Name, Light, accent.Value, bg.Value);
            foreach (var (key, _) in Slots)
            {
                var prop = typeof(ThemePalette).GetProperty(key);
                if (Parse((string)prop.GetValue(this)) == null) prop.SetValue(this, prop.GetValue(gen));
                else prop.SetValue(this, ((string)prop.GetValue(this)).ToUpperInvariant());
            }
            return true;
        }

        public static ThemePalette FromJson(string json)
        {
            if (json == null || json.Length > 64 * 1024) throw new FormatException("Not a theme file (too large).");
            ThemePalette p;
            try
            {
                p = JsonConvert.DeserializeObject<ThemePalette>(json, new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    TypeNameHandling = TypeNameHandling.None,
                    MaxDepth = 4
                });
            }
            catch (JsonException) { throw new FormatException("Not a theme file (invalid JSON)."); }
            if (p == null || !p.Sanitize()) throw new FormatException("Not a theme file (it needs at least valid Window and Accent colors).");
            return p;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        // ------------------------------------------------------------------ contrast

        public static double Luminance(Color c)
        {
            double Lin(int v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }

        public static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>Text color for labels drawn on top of the accent color.</summary>
        public static Color OnColor(Color fill) => Contrast(fill, Color.White) >= Contrast(fill, Color.FromArgb(18, 18, 18)) ? Color.White : Color.FromArgb(18, 18, 18);

        /// <summary>Readability problems worth telling the user about (empty when fine).</summary>
        public List<string> ContrastWarnings()
        {
            var w = new List<string>();
            void Need(string what, Color fg, Color bg, double min)
            {
                double c = Contrast(fg, bg);
                if (c < min) w.Add($"{what}: contrast {c:0.0}:1 (aim for {min:0.#}:1 or more)");
            }
            Need("Text on cards", Get("Text"), Get("Surface"), 4.5);
            Need("Text on window", Get("Text"), Get("Window"), 4.5);
            Need("Secondary text on cards", Get("TextDim"), Get("Surface"), 3.0);
            Need("Text in inputs", Get("Text"), Get("Input"), 4.5);
            Need("Accent icons on cards", Get("Accent"), Get("Surface"), 2.0);
            return w;
        }
    }
}
