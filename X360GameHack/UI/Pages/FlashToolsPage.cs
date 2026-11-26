using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public sealed class FlashToolsPage : PageBase
    {
        public override string Title => "Flash Tools";
        public override string Subtitle => "NAND tools for RGH / JTAG installs, DVD drive flashing and XeLL.";
        public override string PageGlyph => Glyph.Flash;

        private readonly XTextBox _xellIp = new XTextBox("IP shown by XeLL, e.g. 192.168.1.60") { Width = Theme.S(260) };
        private readonly XTextBox _xellOut = new XTextBox(null, true, true) { Height = Theme.S(220), ReadOnly = true };
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public FlashToolsPage()
        {
            var cols = AddColumns(2, 420);
            var nand = AddCard("NAND / RGH", "Read, build and write NAND images. Always keep several matching NAND dumps before writing.", Glyph.Flash, cols);
            Buttons(nand,
                Btn("J-Runner", Glyph.Play, () => Tools.Launch(Tools.JRunner), BtnKind.Primary, "RGH/JTAG installer, NAND reader/writer and xeBuild front-end."),
                Btn("Tools & drivers folder", Glyph.Folder, () => Tools.OpenFolder(Path.Combine(Tools.AppDir, "common"))));
            Buttons(nand, Btn("Copy SimpleNandFlasher to USB...", Glyph.Usb, CopyNandFlasher,
                tip: "Copies simplenandflasher.xex to the root of a USB drive so you can flash an updated NAND from the console."));

            var dvd = AddCard("DVD drive", "Jungle Flasher reads and spoofs Xbox 360 DVD drive firmware (for flashed drives).", Glyph.Disc, cols);
            Buttons(dvd,
                Btn("Jungle Flasher", Glyph.Play, () => Tools.Launch(Tools.JungleFlasher), BtnKind.Primary),
                Btn("Readme", Glyph.Document, () => OpenDoc("JungleFlasher.txt")),
                Btn("Firmware folder", Glyph.Folder, () => Tools.OpenFolder(Path.Combine(Tools.AppDir, "firmware"))),
                Btn("Tutorial (PDF)", Glyph.Document, () => OpenDoc(Path.Combine("Documentation", "jftut1.4.5.pdf"))));
            Note(dvd, "Jungle Flasher needs the PortIO driver on 64-bit Windows - see the readme (PortIO-Kai_Schtrom.txt).");

            var xell = AddCard("FTP2XeLL - read keys from XeLL", "When XeLL boots it shows an IP address. It serves the CPU key, DVD key and fuses over HTTP.", Glyph.Lock);
            _xellIp.Text = AppSettings.Current.XellIp;
            var read = Btn("Read", Glyph.Download, ReadXell, BtnKind.Primary);
            var row = Field(xell, "XeLL IP", _xellIp, read, Btn("Open in browser", Glyph.Globe, () =>
            {
                string ip = _xellIp.Text.Trim();
                if (ip.Length > 0) Tools.OpenUrl(ip.StartsWith("http") ? ip : "http://" + ip);
            }));
            row.FillChild = null;
            xell.Add(_xellOut);
            Buttons(xell,
                Btn("Copy CPU key", Glyph.Copy, CopyCpuKey),
                Btn("Save to file...", Glyph.Save, () =>
                {
                    if (_xellOut.Text.Length == 0) return;
                    var f = PickSave("Save XeLL info", "xell-info.txt", "Text|*.txt");
                    if (f != null) { File.WriteAllText(f, _xellOut.Text); Log.Success("Saved " + f); }
                }));
        }

        private static void OpenDoc(string relative)
        {
            string p = Path.Combine(Tools.AppDir, relative);
            if (!File.Exists(p)) p = Path.Combine(Tools.AppDir, "Documentation", Path.GetFileName(relative));
            if (File.Exists(p)) Tools.OpenUrl(p);
            else Log.Error("Not found: " + relative);
        }

        private async void ReadXell()
        {
            string ip = _xellIp.Text.Trim();
            if (ip.Length == 0) { Dlg.Warn("XeLL", "Enter the IP address XeLL shows on screen."); return; }
            AppSettings.Current.XellIp = ip;
            AppSettings.Save();
            string url = ip.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? ip : "http://" + ip;
            await RunAsync(null, "Reading XeLL", async () =>
            {
                string html = await Http.GetStringAsync(url);
                string text = Regex.Replace(html, @"<(br|/p|/div|/tr|/h\d)[^>]*>", "\n", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, "<[^>]+>", "");
                text = System.Net.WebUtility.HtmlDecode(text);
                text = Regex.Replace(text, @"\n\s*\n+", "\n").Trim();
                _xellOut.Text = text.Replace("\n", "\r\n");
                Log.Success("XeLL info read from " + url);
            });
        }

        private void CopyCpuKey()
        {
            var m = Regex.Match(_xellOut.Text, @"cpukey\s*[:=]?\s*([0-9A-Fa-f]{32})", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(_xellOut.Text, @"\b([0-9A-Fa-f]{32})\b");
            if (!m.Success) { Dlg.Info("CPU key", "No CPU key found. Read XeLL first."); return; }
            try { Clipboard.SetText(m.Groups[1].Value.ToUpperInvariant()); Log.Success("CPU key copied."); } catch { }
        }

        private void CopyNandFlasher()
        {
            string found = Directory.EnumerateFiles(Tools.AppDir, "*.xex", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileName(f).IndexOf("simplenand", StringComparison.OrdinalIgnoreCase) >= 0);
            if (found == null)
            {
                if (!Dlg.Confirm("SimpleNandFlasher", "simplenandflasher.xex is not bundled with this copy. Do you want to pick it from your computer?", "Choose file"))
                    return;
                found = PickFile("Select simplenandflasher.xex", "XEX|*.xex");
                if (found == null) return;
            }
            var drives = UsbDrives.Detect();
            if (drives.Count == 0) { Dlg.Info("USB", "Plug in a FAT32 USB drive first."); return; }
            var drive = drives.FirstOrDefault(d => string.Equals(d.Root, AppSettings.Current.UsbDrive, StringComparison.OrdinalIgnoreCase)) ?? drives[0];
            if (!Dlg.Confirm("Copy", "Copy " + Path.GetFileName(found) + " to " + drive.Root + "?", "Copy")) return;
            try
            {
                File.Copy(found, Path.Combine(drive.Root, Path.GetFileName(found)), true);
                Log.Success("Copied " + Path.GetFileName(found) + " to " + drive.Root);
            }
            catch (Exception ex) { Dlg.Error("Copy failed", ex.Message); }
        }
    }
}
