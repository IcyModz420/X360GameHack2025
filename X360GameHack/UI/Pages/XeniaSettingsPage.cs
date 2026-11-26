using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>
    /// The Settings tab of the Xenia page: Xenia's own settings, set before a game starts - graphics first (resolution
    /// scale, FSR / CAS, anti-aliasing, VSync, frame rate...), then sound, controls and game options, and below them every
    /// setting Xenia's settings file has, with Xenia's own description. Each change is written into Xenia's settings file
    /// at once (only that value; the file is copied once before the first change, so it can be put back).
    /// </summary>
    public sealed class XeniaSettingsPage : PageBase
    {
        public override string Title => "Xenia settings";
        public override string Subtitle => "Graphics, sound, controls and every other setting Xenia has - chosen here before a game starts.";
        public override string PageGlyph => Glyph.Video;

        private sealed class Opt
        {
            public readonly string Value, Text;
            public Opt(string value, string text) { Value = value; Text = text; }
            public override string ToString() => Text;
        }

        private string _file;
        private List<XeniaSetting> _all = new List<XeniaSetting>();
        private bool _loading;
        private readonly List<Action> _refresh = new List<Action>();
        private readonly TextBlock _where = new TextBlock("", Theme.Small, Theme.TextDim) { Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix };
        private readonly XButton _restore;

        // every setting
        private readonly XTextBox _search = new XTextBox("Search Xenia's settings (name or description)...");
        private readonly ListView _every = ListStyle.Create(("Setting", 300), ("Value", 170), ("Section", 110));
        private readonly TextBlock _about = new TextBlock("", Theme.Small, Theme.TextDim) { Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix };
        private readonly XCombo _editChoice = new XCombo { Width = Theme.S(260) };
        private readonly XTextBox _editText = new XTextBox("Value");
        private readonly XButton _set;
        private readonly HStack _editRow;
        private XeniaSetting _editing;

        public XeniaSettingsPage()
        {
            // ---- the file
            var top = AddCard("Xenia's settings", "Changes are saved straight into Xenia's own settings file and used the next time a game starts in Xenia.", Glyph.Settings);
            top.Add(_where);
            var tb = Buttons(top,
                Btn("Reload", Glyph.Refresh, Reload, tip: "Reads Xenia's settings file again (after changing something in Xenia itself)."),
                Btn("Open its folder", Glyph.FolderOpen, () => { if (_file != null) Tools.OpenFolder(File.Exists(_file) ? _file : Path.GetDirectoryName(_file)); }));
            _restore = Btn("Put back my old settings...", Glyph.Back, RestoreOriginal, tip: "Puts Xenia's settings file back the way it was before X360GameHack first changed it.");
            tb.Controls.Add(_restore);

            // ---- graphics
            var cols = AddColumns(2, 440);
            var gfx = AddCard("Graphics", "How Xenia draws your games. Resolution scale renders at a higher resolution than the console did (upscaling); FSR and CAS sharpen the picture.", Glyph.Video, cols);
            // one click sets several of the choices below at once; any of them can still be changed afterwards
            var presets = new WrapPanel();
            presets.Add(new XButton("Xenia's defaults", Glyph.Refresh, BtnKind.Secondary, (s, e) => Preset("Xenia's defaults", Defaults)));
            presets.Add(new XButton("Sharper (2x)", Glyph.Sparkle, BtnKind.Secondary, (s, e) => Preset("Sharper", Sharper(2))));
            presets.Add(new XButton("4K (3x)", Glyph.Sparkle, BtnKind.Secondary, (s, e) => Preset("4K", Sharper(3))));
            foreach (Control b in presets.Controls)
                Tip.Set(b, b.Text.StartsWith("Xenia") ? "Every graphics choice on this card back to what Xenia uses when nothing is set."
                    : "Resolution scale " + (b.Text.StartsWith("4K") ? "3x (4K from a 720p game)" : "2x (1440p from a 720p game)") +
                      ", AMD FSR upscaling, FXAA anti-aliasing and 16x texture filtering - the rest stays as it is. Needs a strong graphics card; Xenia lowers the scale to what yours can do.");
            gfx.Add(presets);
            Choice(gfx, "Renderer", "GPU", "gpu", "any", XeniaValueKind.Text,
                new Opt("any", "Automatic (Direct3D 12 when it works)"), new Opt("d3d12", "Direct3D 12"), new Opt("vulkan", "Vulkan"));
            ScaleChoice(gfx);
            Choice(gfx, "Upscaling filter", "Display", "postprocess_scaling_and_sharpening", "", XeniaValueKind.Text,
                new Opt("", "Bilinear - plain stretching (Xenia's default)"), new Opt("fsr", "AMD FSR 1.0 - best upscaling"), new Opt("cas", "AMD CAS - sharpening"));
            Choice(gfx, "FSR sharpness", "Display", "postprocess_ffx_fsr_sharpness_reduction", "0.2", XeniaValueKind.Float,
                new Opt("0.0", "Sharpest (0)"), new Opt("0.2", "Normal (0.2, Xenia's default)"), new Opt("0.5", "Softer (0.5)"), new Opt("1.0", "Soft (1)"), new Opt("2.0", "Softest (2)"));
            Choice(gfx, "CAS extra sharpness", "Display", "postprocess_ffx_cas_additional_sharpness", "0", XeniaValueKind.Float,
                new Opt("0.0", "None (Xenia's default)"), new Opt("0.25", "A little (0.25)"), new Opt("0.5", "Medium (0.5)"), new Opt("0.75", "Strong (0.75)"), new Opt("1.0", "Strongest (1)"));
            Choice(gfx, "Anti-aliasing", "Display", "postprocess_antialiasing", "", XeniaValueKind.Text,
                new Opt("", "Off (Xenia's default)"), new Opt("fxaa", "FXAA"), new Opt("fxaa_extreme", "FXAA extreme"));
            Choice(gfx, "Texture filtering", "GPU", "anisotropic_override", "-1", XeniaValueKind.Int,
                new Opt("-1", "As the game sets it (Xenia's default)"), new Opt("0", "Anisotropic off"), new Opt("1", "Anisotropic 1x"), new Opt("2", "Anisotropic 2x"),
                new Opt("3", "Anisotropic 4x"), new Opt("4", "Anisotropic 8x"), new Opt("5", "Anisotropic 16x"));
            Choice(gfx, "Frame rate limit", "GPU", "framerate_limit", "0", XeniaValueKind.Int,
                new Opt("0", "None (60 with VSync on, Xenia's default)"), new Opt("30", "30 fps"), new Opt("60", "60 fps"), new Opt("120", "120 fps"), new Opt("144", "144 fps"), new Opt("240", "240 fps"));
            Choice(gfx, "Game's own resolution", "Video", "internal_display_resolution", "8", XeniaValueKind.Int,
                new Opt("0", "640x480"), new Opt("1", "640x576"), new Opt("2", "720x480"), new Opt("3", "720x576"), new Opt("4", "800x600"), new Opt("5", "848x480"),
                new Opt("6", "1024x768"), new Opt("7", "1152x864"), new Opt("8", "1280x720 (Xenia's default)"), new Opt("9", "1280x768"), new Opt("10", "1280x960"),
                new Opt("11", "1280x1024"), new Opt("12", "1360x768"), new Opt("13", "1440x900"), new Opt("14", "1680x1050"), new Opt("15", "1920x540"),
                new Opt("16", "1920x1080"), new Opt("17", "Custom size (internal_display_resolution_x / _y below)"));
            Check(gfx, "VSync", "GPU", "vsync", true, "Waits for the screen's refresh: no tearing. Off can raise the frame rate (with a frame rate limit above, or none).");
            Check(gfx, "Start in fullscreen", "Display", "fullscreen", false, "Xenia opens fullscreen. F11 switches in and out while a game runs.");
            Check(gfx, "Widescreen (16:9)", "Video", "widescreen", true, "Off tells games the TV is 4:3.");
            Check(gfx, "Keep the picture's shape (black bars)", "Display", "present_letterbox", true, "Off stretches the picture to the whole window.");
            Check(gfx, "Smooth gradients (dithering)", "Display", "postprocess_dither", true, null);
            Note(gfx, "Resolution scale needs a graphics card with tiled resources (Direct3D 12) or sparse binding (Vulkan); Xenia lowers it to the most your card can do. " +
                      "2x is a good start, 3x is 4K. Some effects can look wrong at a higher scale - set it back to 1x for a game that does.");

            var acc = AddCard("Accuracy and speed", "For a game that shows something wrong (missing shadows, black or flickering parts) or runs slowly.", Glyph.Speed, cols);
            Choice(acc, "Render targets (D3D12)", "GPU", "render_target_path_d3d12", "", XeniaValueKind.Text,
                new Opt("", "Automatic (Xenia's default)"), new Opt("rtv", "Fast (RTV)"), new Opt("rov", "Accurate (ROV) - slower"));
            Choice(acc, "Render targets (Vulkan)", "GPU", "render_target_path_vulkan", "", XeniaValueKind.Text,
                new Opt("", "Automatic (Xenia's default)"), new Opt("fbo", "Fast (FBO)"), new Opt("fsi", "Accurate (FSI) - slower"));
            Choice(acc, "Render-to-texture readback", "GPU", "readback_resolve", "none", XeniaValueKind.Text,
                new Opt("none", "Off (Xenia's default; some games look better)"), new Opt("fast", "Fast (one frame late)"), new Opt("full", "Full (exact, slow)"));
            Choice(acc, "Process priority", "General", "priority_class", "0", XeniaValueKind.Int,
                new Opt("0", "Normal (Xenia's default)"), new Opt("1", "Above normal"), new Opt("2", "High"));
            Check(acc, "Compile shaders in the background (less stutter)", "GPU", "async_shader_compilation", true, "Off: no brief glitches while new shaders compile, but short stutters instead.");
            Check(acc, "Keep compiled shaders (smoother next time)", "GPU", "store_shaders", true, null);
            Check(acc, "Read back memory exports (some games need it; slow)", "GPU", "readback_memexport", false, null);
            Check(acc, "Fix missing characters (Team Ninja games)", "GPU", "clear_memory_page_state", false, "Use for Ninja Gaiden / Dead or Alive games with missing models.");

            var snd = AddCard("Sound and controls", null, Glyph.Game, cols);
            Choice(snd, "Sound", "APU", "apu", "any", XeniaValueKind.Text,
                new Opt("any", "Automatic (Xenia's default)"), new Opt("xaudio2", "XAudio2"), new Opt("sdl", "SDL"), new Opt("nop", "No sound"));
            Choice(snd, "Audio decoder", "APU", "xma_decoder", "new", XeniaValueKind.Text,
                new Opt("new", "New (Xenia's default, most stable)"), new Opt("old", "Old"), new Opt("master", "Master (original Xenia)"), new Opt("fake", "None (no game audio)"));
            Check(snd, "Mute", "APU", "mute", false, null);
            Choice(snd, "Controllers", "HID", "hid", "any", XeniaValueKind.Text,
                new Opt("any", "Every kind (Xenia's default)"), new Opt("sdl", "SDL (PlayStation, Switch and most others)"), new Opt("xinput", "XInput (Xbox controllers only)"),
                new Opt("winkey", "Keyboard only"), new Opt("nop", "None"));
            Choice(snd, "Keyboard", "HID", "keyboard_mode", "0", XeniaValueKind.Int,
                new Opt("0", "Off (Xenia's default)"), new Opt("1", "On"), new Opt("2", "Passthrough (needs a controller)"));
            Choice(snd, "Left stick dead zone", "HID", "left_stick_deadzone_percentage", "0", XeniaValueKind.Float, Zones());
            Choice(snd, "Right stick dead zone", "HID", "right_stick_deadzone_percentage", "0", XeniaValueKind.Float, Zones());
            Check(snd, "Vibration", "HID", "vibration", true, null);
            Check(snd, "Guide button goes to the game", "HID", "guide_button", true, null);
            Note(snd, "With \"Every controller works in Xenia\" ticked on the Setup tab, a controller setting that leaves controllers out is put back to every kind when a game starts.");

            var game = AddCard("Games and console", null, Glyph.Console, cols);
            Choice(game, "Licence", "Content", "license_mask", "0", XeniaValueKind.Int,
                new Opt("0", "None - trial (Xenia's default)"), new Opt("1", "Full game (unlocks Arcade games)"), new Opt("-1", "Every licence (can break games)"));
            Choice(game, "Console language", "XConfig", "user_language", "1", XeniaValueKind.Int,
                new Opt("1", "English"), new Opt("2", "Japanese"), new Opt("3", "German"), new Opt("4", "French"), new Opt("5", "Spanish"), new Opt("6", "Italian"),
                new Opt("7", "Korean"), new Opt("8", "Chinese (Traditional)"), new Opt("9", "Portuguese"), new Opt("11", "Polish"), new Opt("12", "Russian"),
                new Opt("13", "Swedish"), new Opt("14", "Turkish"), new Opt("15", "Norwegian"), new Opt("16", "Dutch"), new Opt("17", "Chinese (Simplified)"));
            Choice(game, "TV system", "Video", "video_standard", "1", XeniaValueKind.Int,
                new Opt("1", "NTSC (Xenia's default)"), new Opt("2", "NTSC-J"), new Opt("3", "PAL"));
            Check(game, "Use title updates", "Kernel", "apply_title_update", true, null);
            Check(game, "Use game patches (Xenia Patches tab)", "General", "apply_patches", true, null);
            Check(game, "Load plugins / trainers (plugins\\<title ID>\\plugin.xex)", "General", "allow_plugins", false, "Xenia's own plugin loader - experimental.");
            Check(game, "Show achievement pop-ups", "UI", "show_achievement_notification", false, null);
            Check(game, "Discord status", "General", "discord", true, null);

            // ---- every setting
            var every = AddCard("Every Xenia setting", "All of Xenia's settings, with Xenia's own description. Select one, change its value, press Set.", Glyph.List);
            Field(every, null, _search);
            _every.Height = Theme.S(300);
            _every.MultiSelect = false;
            every.Add(_every);
            every.Add(_about);
            _editRow = Field(every, "Value", _editText);
            _editRow.Controls.Add(_editChoice);
            _set = new XButton("Set", Glyph.Check, BtnKind.Primary, (s, e) => SetSelected());
            _editRow.Controls.Add(_set);
            Tip.Set(_set, "Writes this value into Xenia's settings file.");
            _editText.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SetSelected(); } };
            _search.TextChanged += (s, e) => FillEvery();
            _every.SelectedIndexChanged += (s, e) => ShowSelected();
            ShowSelected();
            Reload();
        }

        /// <summary>Xenia's own value for each choice on the Graphics card (what it uses when its settings file says nothing).</summary>
        private static readonly (string Section, string Key, XeniaValueKind Kind, string Value)[] Defaults =
        {
            ("GPU", "gpu", XeniaValueKind.Text, "any"), ("GPU", "draw_resolution_scale_x", XeniaValueKind.Int, "1"), ("GPU", "draw_resolution_scale_y", XeniaValueKind.Int, "1"),
            ("Display", "postprocess_scaling_and_sharpening", XeniaValueKind.Text, ""), ("Display", "postprocess_ffx_fsr_sharpness_reduction", XeniaValueKind.Float, "0.2"),
            ("Display", "postprocess_ffx_cas_additional_sharpness", XeniaValueKind.Float, "0"), ("Display", "postprocess_antialiasing", XeniaValueKind.Text, ""),
            ("GPU", "anisotropic_override", XeniaValueKind.Int, "-1"), ("GPU", "framerate_limit", XeniaValueKind.Int, "0"), ("Video", "internal_display_resolution", XeniaValueKind.Int, "8"),
            ("GPU", "vsync", XeniaValueKind.Bool, "true"), ("Display", "fullscreen", XeniaValueKind.Bool, "false"), ("Video", "widescreen", XeniaValueKind.Bool, "true"),
            ("Display", "present_letterbox", XeniaValueKind.Bool, "true"), ("Display", "postprocess_dither", XeniaValueKind.Bool, "true")
        };

        private static (string, string, XeniaValueKind, string)[] Sharper(int scale) => new[]
        {
            ("GPU", "draw_resolution_scale_x", XeniaValueKind.Int, scale.ToString()), ("GPU", "draw_resolution_scale_y", XeniaValueKind.Int, scale.ToString()),
            ("Display", "postprocess_scaling_and_sharpening", XeniaValueKind.Text, "fsr"), ("Display", "postprocess_antialiasing", XeniaValueKind.Text, "fxaa"),
            ("GPU", "anisotropic_override", XeniaValueKind.Int, "5")
        };

        private void Preset(string name, (string Section, string Key, XeniaValueKind Kind, string Value)[] values) => Write("graphics set to " + name, values);

        private static Opt[] Zones() => new[]
        {
            new Opt("0.0", "None (Xenia's default)"), new Opt("0.05", "5%"), new Opt("0.1", "10%"), new Opt("0.15", "15%"), new Opt("0.2", "20%"), new Opt("0.25", "25%"), new Opt("0.3", "30%")
        };

        public override void OnActivated() { base.OnActivated(); Reload(); }

        // ------------------------------------------------------------ the file

        /// <summary>Xenia's settings file now (null when Xenia is not found).</summary>
        public string SettingsFile => _file;

        /// <summary>
        /// True while this Xenia runs. Xenia writes its settings file when it starts AND when it closes - with what it
        /// loaded at its start - so a change made while it runs would be undone when it closes (measured with the bundled
        /// build: a value changed in the file while it ran was back to the old one after it closed).
        /// </summary>
        private static bool XeniaRunning() => XeniaHost.IsRunning(Tools.Xenia);

        /// <summary>Reads Xenia's settings file again and shows its values everywhere on this tab.</summary>
        public void Reload()
        {
            string xenia = Tools.Xenia;
            _file = !string.IsNullOrWhiteSpace(xenia) && File.Exists(xenia) ? XeniaHost.ConfigFile(xenia) : null;
            try { _all = _file != null ? XeniaConfig.Read(_file) : new List<XeniaSetting>(); }
            catch (Exception ex) { _all = new List<XeniaSetting>(); Log.Warn("Could not read Xenia's settings file: " + ex.Message); }
            _loading = true;
            try { foreach (var r in _refresh) r(); }
            finally { _loading = false; }
            _where.Text = _file == null ? "Xenia was not found - set its path on the Setup tab."
                : (File.Exists(_file) ? "Xenia's settings file: " + _file + " (" + _all.Count + " settings)."
                                      : "Xenia has not written its settings file yet (it does the first time it starts). What you choose here goes into a new file now - Xenia adds the rest of its settings when it starts.") +
                  (XeniaRunning() ? " Xenia is running - close it to change settings here (Xenia saves its settings when it closes, which would undo a change made now)." : "");
            _restore.Visible = _file != null && File.Exists(XeniaConfig.OriginalOf(_file));
            FillEvery();
            PerformLayout();
        }

        private XeniaSetting Find(string section, string key) =>
            _all.FirstOrDefault(s => s.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        /// <summary>Writes values into Xenia's settings file (each in the kind Xenia keeps it), then shows the file again.</summary>
        private bool Write(string what, params (string Section, string Key, XeniaValueKind Kind, string Value)[] values)
        {
            if (_file == null) { Dlg.Info("Xenia settings", "Xenia was not found - set its path on the Setup tab."); Reload(); return false; }
            if (XeniaRunning()) { ShowRunning(); Reload(); return false; }
            try
            {
                var changes = values.Select(v => new XeniaConfig.Change(v.Section, v.Key, XeniaConfig.Literal(Find(v.Section, v.Key)?.Kind ?? v.Kind, v.Value))).ToList();
                int n = XeniaConfig.Write(_file, changes);
                if (n > 0)
                    Log.Success("Xenia: " + what + " - saved in Xenia's settings file; used the next time a game starts.");
                Reload();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not change Xenia's settings: " + ex.Message);
                Dlg.Error("Xenia settings", ex.Message);
                Reload();
                return false;
            }
        }

        private static void ShowRunning() =>
            Dlg.Warn("Close Xenia first", "Xenia is running. It saves its settings when it closes, and that would undo a change made now.\n\n" +
                                          "Close Xenia, then change the setting - it is used the next time a game starts.");

        private void RestoreOriginal()
        {
            if (_file == null || !File.Exists(XeniaConfig.OriginalOf(_file))) return;
            if (XeniaRunning()) { ShowRunning(); return; }
            if (!Dlg.Confirm("Put back my old settings", "Put Xenia's settings file back the way it was before X360GameHack first changed it?\n\nEvery change made here since then is undone.", "Put back")) return;
            try { XeniaConfig.RestoreOriginal(_file); Log.Success("Xenia's settings file is back the way it was before X360GameHack changed it."); }
            catch (Exception ex) { Dlg.Error("Xenia settings", ex.Message); }
            Reload();
        }

        // ------------------------------------------------------------ the chosen settings

        private static bool SameValue(string a, string b, XeniaValueKind kind)
        {
            if (kind == XeniaValueKind.Int || kind == XeniaValueKind.Float)
                return double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                       double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y) && Math.Abs(x - y) < 1e-6;
            return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private string Current(string section, string key, string fallback) => Find(section, key)?.Value ?? fallback;

        /// <summary>A drop-down of the values that make sense, with Xenia's description as its tip. A value set by hand shows as itself.</summary>
        private XCombo Choice(Card card, string label, string section, string key, string fallback, XeniaValueKind kind, params Opt[] options)
        {
            var combo = new XCombo { Width = Theme.S(320) };
            Field(card, label, combo);
            combo.Items.AddRange(options);
            combo.SelectedIndexChanged += (s, e) =>
            {
                if (_loading || !(combo.SelectedItem is Opt o)) return;
                if (SameValue(o.Value, Current(section, key, fallback), kind)) return;
                Write(label + " = " + o.Text, (section, key, kind, o.Value));
            };
            _refresh.Add(() =>
            {
                string cur = Current(section, key, fallback);
                // a value set by hand (not one of the list) is shown as it is, at the end of the list
                for (int i = combo.Items.Count - 1; i >= options.Length; i--) combo.Items.RemoveAt(i);
                int at = Array.FindIndex(options, o => SameValue(o.Value, cur, kind));
                if (at < 0) { combo.Items.Add(new Opt(cur, cur + " (set by hand)")); at = combo.Items.Count - 1; }
                combo.SelectedIndex = at;
                string about = Find(section, key)?.DescriptionText;
                Tip.Set(combo, (about ?? "") + (about != null ? "\n\n" : "") + "Xenia: [" + section + "] " + key);
            });
            return combo;
        }

        /// <summary>Resolution scale: Xenia's width and height scale, kept the same.</summary>
        private void ScaleChoice(Card card)
        {
            var options = new[]
            {
                new Opt("1", "1x - the game's own resolution (720p)"), new Opt("2", "2x - 1440p from 720p"), new Opt("3", "3x - 4K from 720p"),
                new Opt("4", "4x - 5K from 720p"), new Opt("5", "5x"), new Opt("6", "6x - 8K from 720p"), new Opt("7", "7x")
            };
            var combo = new XCombo { Width = Theme.S(320) };
            Field(card, "Resolution scale", combo);
            combo.Items.AddRange(options);
            combo.SelectedIndexChanged += (s, e) =>
            {
                if (_loading || !(combo.SelectedItem is Opt o) || o.Value.Contains("x")) return;
                if (Current("GPU", "draw_resolution_scale_x", "1") == o.Value && Current("GPU", "draw_resolution_scale_y", "1") == o.Value) return;
                Write("resolution scale " + o.Value + "x", ("GPU", "draw_resolution_scale_x", XeniaValueKind.Int, o.Value), ("GPU", "draw_resolution_scale_y", XeniaValueKind.Int, o.Value));
            };
            _refresh.Add(() =>
            {
                string x = Current("GPU", "draw_resolution_scale_x", "1"), y = Current("GPU", "draw_resolution_scale_y", "1");
                for (int i = combo.Items.Count - 1; i >= options.Length; i--) combo.Items.RemoveAt(i);
                int at = x == y ? Array.FindIndex(options, o => o.Value == x) : -1;
                if (at < 0) { combo.Items.Add(new Opt(x + "x" + y, x + " wide x " + y + " high (set by hand)")); at = combo.Items.Count - 1; }
                combo.SelectedIndex = at;
                Tip.Set(combo, (Find("GPU", "draw_resolution_scale_x")?.DescriptionText ?? "") + "\n\nXenia: [GPU] draw_resolution_scale_x and draw_resolution_scale_y");
            });
        }

        private XCheck Check(Card card, string text, string section, string key, bool fallback, string tip)
        {
            var box = new XCheck(text, fallback);
            card.Add(box);
            box.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                Write(text + (box.Checked ? " on" : " off"), (section, key, XeniaValueKind.Bool, box.Checked ? "true" : "false"));
            };
            _refresh.Add(() =>
            {
                box.Checked = string.Equals(Current(section, key, fallback ? "true" : "false"), "true", StringComparison.OrdinalIgnoreCase);
                string about = Find(section, key)?.DescriptionText;
                Tip.Set(box, (tip != null ? tip + "\n\n" : "") + (about ?? "") + (about != null ? "\n\n" : "") + "Xenia: [" + section + "] " + key);
            });
            return box;
        }

        // ------------------------------------------------------------ every setting

        private void FillEvery()
        {
            string q = _search.Text.Trim();
            string keep = _editing?.Id;
            _every.BeginUpdate();
            _every.Items.Clear();
            foreach (var s in _all)
            {
                if (q.Length > 0 && s.Key.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && s.Section.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    s.DescriptionText.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var it = new ListViewItem(new[] { s.Key, Shown(s), s.Section }) { Tag = s };
                _every.Items.Add(it);
                if (s.Id == keep) { it.Selected = true; it.Focused = true; }
            }
            _every.EndUpdate();
            if (_every.SelectedItems.Count > 0) _every.SelectedItems[0].EnsureVisible();
            else ShowSelected();
        }

        private static string Shown(XeniaSetting s) => s.Kind == XeniaValueKind.Text ? (s.Value.Length == 0 ? "(empty)" : s.Value) : s.Value;

        private void ShowSelected()
        {
            _editing = _every.SelectedItems.Count > 0 ? _every.SelectedItems[0].Tag as XeniaSetting : null;
            var s = _editing;
            _about.Text = s == null ? (_all.Count == 0 ? "" : "Select a setting to see what Xenia says about it.")
                                    : "[" + s.Section + "] " + s.Key + "\n" + s.DescriptionText;
            bool pick = s != null && (s.Kind == XeniaValueKind.Bool || s.Choices.Count > 0);
            _editChoice.Visible = pick;
            _editText.Visible = s != null && !pick;
            _set.Enabled = s != null;
            if (s != null && pick)
            {
                _editChoice.Items.Clear();
                var values = s.Kind == XeniaValueKind.Bool ? new List<string> { "true", "false" } : new List<string>(s.Choices);
                if (!values.Any(v => v.Equals(s.Value, StringComparison.OrdinalIgnoreCase))) values.Add(s.Value);
                foreach (var v in values) _editChoice.Items.Add(new Opt(v, v.Length == 0 ? "(empty)" : v));
                _editChoice.SelectedIndex = values.FindIndex(v => v.Equals(s.Value, StringComparison.OrdinalIgnoreCase));
            }
            else if (s != null) _editText.Text = s.Value;
            _editRow.PerformLayout();
            PerformLayout();
        }

        private void SetSelected()
        {
            var s = _editing;
            if (s == null) return;
            string value = _editChoice.Visible ? (_editChoice.SelectedItem as Opt)?.Value ?? s.Value : _editText.Text;
            if (s.Kind != XeniaValueKind.Text) value = value.Trim();
            try { XeniaConfig.Literal(s.Kind, value); }
            catch (FormatException ex) { Dlg.Warn("Xenia settings", s.Key + ": " + ex.Message); return; }
            if (s.Kind == XeniaValueKind.Text ? value == s.Value : SameValue(value, s.Value, s.Kind) || value.Equals(s.Value, StringComparison.OrdinalIgnoreCase)) return;
            Write(s.Key + " = " + (value.Length == 0 ? "(empty)" : value), (s.Section, s.Key, s.Kind, value));
        }

        // ------------------------------------------------------------ for tests

        /// <summary>The value Xenia's settings file has for a setting now (null when it has none).</summary>
        public string ValueOf(string section, string key) => Find(section, key)?.Value;
    }
}
