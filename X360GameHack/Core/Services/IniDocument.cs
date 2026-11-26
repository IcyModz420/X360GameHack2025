using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// Minimal INI document that keeps comments and ordering intact while editing values
    /// (used for DashLaunch launch.ini, JRPC.ini, xbdm.ini, ...).
    /// </summary>
    public sealed class IniDocument
    {
        private readonly List<string> _lines;

        public IniDocument(string text)
        {
            _lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();
            if (_lines.Count > 0 && _lines[_lines.Count - 1].Length == 0) _lines.RemoveAt(_lines.Count - 1);
        }

        public override string ToString() => string.Join("\r\n", _lines) + "\r\n";

        private static bool IsSection(string line, out string name)
        {
            string t = line.Trim();
            name = null;
            if (t.StartsWith("[") && t.Contains("]"))
            {
                name = t.Substring(1, t.IndexOf(']') - 1).Trim();
                return true;
            }
            return false;
        }

        private static bool IsKey(string line, out string key, out string value)
        {
            key = value = null;
            string t = line.Trim();
            if (t.Length == 0 || t.StartsWith(";") || t.StartsWith("#") || t.StartsWith("[")) return false;
            int eq = t.IndexOf('=');
            if (eq <= 0) return false;
            key = t.Substring(0, eq).Trim();
            value = t.Substring(eq + 1).Trim();
            int comment = value.IndexOf(" ;", StringComparison.Ordinal);
            if (comment >= 0) value = value.Substring(0, comment).Trim();
            return true;
        }

        private int SectionStart(string section)
        {
            for (int i = 0; i < _lines.Count; i++)
                if (IsSection(_lines[i], out string n) && string.Equals(n, section, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        public string Get(string section, string key, string fallback = "")
        {
            int s = SectionStart(section);
            if (s < 0) return fallback;
            for (int i = s + 1; i < _lines.Count; i++)
            {
                if (IsSection(_lines[i], out _)) break;
                if (IsKey(_lines[i], out string k, out string v) && string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    return v;
            }
            return fallback;
        }

        /// <summary>Sets a value; an empty or null value removes the key.</summary>
        public void Set(string section, string key, string value)
        {
            int s = SectionStart(section);
            if (s < 0)
            {
                if (string.IsNullOrEmpty(value)) return;
                if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length > 0) _lines.Add("");
                _lines.Add("[" + section + "]");
                _lines.Add(key + " = " + value);
                return;
            }
            int lastKeyLine = s;
            for (int i = s + 1; i < _lines.Count; i++)
            {
                if (IsSection(_lines[i], out _)) break;
                if (IsKey(_lines[i], out string k, out _))
                {
                    lastKeyLine = i;
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(value)) _lines.RemoveAt(i);
                        else _lines[i] = key + " = " + value;
                        return;
                    }
                }
            }
            if (!string.IsNullOrEmpty(value)) _lines.Insert(lastKeyLine + 1, key + " = " + value);
        }

        public IEnumerable<string> Sections()
        {
            foreach (var l in _lines)
                if (IsSection(l, out string n)) yield return n;
        }
    }

    /// <summary>DashLaunch launch.ini helpers and a clean, commented template.</summary>
    public static class LaunchIni
    {
        public static readonly string[] ButtonKeys =
        {
            "Default", "Fakeanim", "BUT_A", "BUT_B", "BUT_X", "BUT_Y", "Start", "Back", "LBump", "RThumb", "LThumb"
        };

        public static readonly (string Label, string Path)[] DashboardPresets =
        {
            ("Aurora", @"Usb:\Aurora\Aurora.xex"),
            ("Aurora (HDD)", @"Hdd:\Aurora\Aurora.xex"),
            ("Freestyle Dash 3", @"Hdd:\Freestyle\default.xex"),
            ("XeXMenu", @"Hdd:\XeXMenu\XeXMenu.xex"),
            ("XeXMenu (USB)", @"Usb:\XeXMenu\XeXMenu.xex"),
            ("Stock dashboard", "")
        };

        public static readonly (string Key, string Description, string Default)[] Settings =
        {
            ("pingpatch", "Remove the system link ping limit", "true"),
            ("contpatch", "Content license patch - lets XBLA/DLC from other consoles run", "true"),
            ("liveblock", "Block Xbox LIVE servers (recommended for offline modded use)", "true"),
            ("livestrong", "Block LIVE completely, including all marketplace calls", "false"),
            ("fatalfreeze", "Freeze instead of rebooting on a fatal crash", "false"),
            ("fatalreboot", "Reboot to dashboard on a fatal crash", "false"),
            ("regionspoof", "Spoof the console region (combine with region=0x7FFF)", "false"),
            ("autoswap", "Auto-swap disc for multi-disc XEX games", "true"),
            ("dvdexitdash", "Exit to the default dash when the disc is ejected", "true"),
            ("xblaexitdash", "XBLA titles exit to the default dash", "true"),
            ("nosysexit", "Disable 'exit to system' launching the stock dash", "false"),
            ("nohud", "Disable the guide button HUD (dangerous: you lose the guide)", "false"),
            ("noupdater", "Block system updates from discs and USB", "true"),
            ("autoshut", "Shut down instead of rebooting with the power button", "false"),
            ("autooff", "Auto power off after a period of inactivity", "false"),
            ("nonetstore", "Hide network storage from the device selector", "true"),
            ("signnotice", "Hide the 'signed in' notice at boot", "true"),
            ("exchandler", "Use DashLaunch's crash handler (writes crash logs)", "true"),
            ("debugout", "Send debug output to xbdm", "false"),
            ("devlink", "Allow system link play between devkits and retail", "false"),
            ("hddalive", "Keep the HDD spinning", "false"),
            ("tempbcast", "Broadcast console temperatures over UDP", "false"),
            ("passlaunch", "Pass launch data when exiting titles", "false"),
            ("remotenxe", "Enable remote NXE features", "false"),
            ("nooobe", "Skip out-of-box experience on fresh consoles", "false"),
        };

        public static string Template()
        {
            var sb = new StringBuilder();
            sb.AppendLine("; DashLaunch launch.ini generated by X360GameHack 2026");
            sb.AppendLine("; Place on the root of Hdd: or a FAT32 Usb: device.");
            sb.AppendLine();
            sb.AppendLine("[QuickLaunchButtons]");
            sb.AppendLine(@"Default = Usb:\Aurora\Aurora.xex");
            sb.AppendLine();
            sb.AppendLine("[Plugins]");
            sb.AppendLine(@"plugin1 = Usb:\xbdm.xex");
            sb.AppendLine();
            sb.AppendLine("[Settings]");
            foreach (var s in Settings) sb.AppendLine(s.Key + " = " + s.Default);
            sb.AppendLine();
            sb.AppendLine("[Externals]");
            return sb.ToString();
        }
    }
}
