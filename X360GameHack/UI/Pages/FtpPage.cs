using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public sealed class FtpPage : PageBase
    {
        public override string Title => "FTP";
        public override string Subtitle => "Aurora, FSD, XeXMenu, DashLaunch, EvoX / UnleashX, PS3 webMAN and PS4 GoldHEN FTP servers.";
        public override string PageGlyph => Glyph.Globe;

        private readonly XCombo _profiles = new XCombo { Width = Theme.S(220) };
        private readonly XTextBox _host = new XTextBox("Console IP, e.g. 192.168.1.50") { Width = Theme.S(220) };
        private readonly XTextBox _port = new XTextBox("21") { Width = Theme.S(70) };
        private readonly XTextBox _user = new XTextBox("xbox") { Width = Theme.S(120) };
        private readonly XTextBox _pass = new XTextBox("xbox") { Width = Theme.S(120) };
        private readonly XCheck _passive = new XCheck("Passive mode", true, "Turn off if listings hang (some console servers only support active mode).");
        private readonly RemoteBrowser _browser = new RemoteBrowser();
        private FtpService _ftp;

        public FtpPage() : base(false)
        {
            _pass.Box.UseSystemPasswordChar = true;
            var conn = new Card(null) { Padding = new Padding(Theme.S(14)) };
            var row = new WrapPanel();
            row.Add(_profiles);
            row.Add(_host); row.Add(_port); row.Add(_user); row.Add(_pass);
            row.Add(_passive);
            var connect = new XButton("Connect", Glyph.Link, BtnKind.Primary, (s, e) => Connect());
            row.Add(connect);
            row.Add(new XButton("Save", Glyph.Save, BtnKind.Secondary, (s, e) => SaveProfile()));
            row.Add(new XButton("", Glyph.Add, BtnKind.Ghost, (s, e) => NewProfile()) { Width = Theme.S(34) });
            row.Add(new XButton("", Glyph.Delete, BtnKind.Ghost, (s, e) => DeleteProfile()) { Width = Theme.S(34) });
            row.Add(new XButton("Send game folder", Glyph.Send, BtnKind.Secondary, (s, e) => SendGame()));
            row.Add(new XButton("Help", Glyph.Help, BtnKind.Ghost, (s, e) => Dlg.Info("FTP help",
                "1. Start the FTP server on the console (Aurora/FSD: Settings > FTP; XeXMenu: it runs automatically; EvoX/UnleashX: enable FTP; PS4: GoldHEN FTP; PS3: webMAN).\n" +
                "2. Enter the IP shown on the console, port 21, and the login (Aurora: xboxftp / xboxftp unless you changed it; FreeStyle Dash, XeXMenu and original Xbox dashboards usually xbox / xbox).\n" +
                "3. Connect, then browse like Explorer. Drag files or whole folders onto the list to upload.\n\n" +
                "Xbox 360 drives appear as folders such as Hdd1 and Usb0. Extracted games usually go to Hdd1/Games/<Game Name>.")));
            Tip.Set(_profiles, "Saved connection profiles");
            Tip.Set(_host, "Console IP or host name");
            Tip.Set(_port, "Port");
            Tip.Set(_user, "User name");
            Tip.Set(_pass, "Password");
            conn.Add(row);
            Body.Add(conn);
            Body.Add(_browser);
            Body.FillChild = _browser;

            _profiles.SelectedIndexChanged += (s, e) => LoadProfile();
            _host.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Connect(); } };
            ReloadProfiles();
            _browser.Detach();
        }

        private void ReloadProfiles()
        {
            var s = AppSettings.Current;
            _profiles.Items.Clear();
            foreach (var p in s.FtpProfiles) _profiles.Items.Add(p);
            if (_profiles.Items.Count > 0)
                _profiles.SelectedIndex = Math.Max(0, Math.Min(s.SelectedFtpProfile, _profiles.Items.Count - 1));
        }

        private void LoadProfile()
        {
            if (!(_profiles.SelectedItem is FtpProfile p)) return;
            _host.Text = p.Host;
            _port.Text = p.Port.ToString();
            _user.Text = p.User;
            _pass.Text = p.Password;
            _passive.Checked = p.Passive;
            AppSettings.Current.SelectedFtpProfile = _profiles.SelectedIndex;
        }

        private FtpProfile FromFields(string name)
        {
            int port;
            return new FtpProfile
            {
                Name = name,
                Host = _host.Text.Trim(),
                Port = int.TryParse(_port.Text.Trim(), out port) && port > 0 && port < 65536 ? port : 21,
                User = _user.Text,
                Password = _pass.Text,
                Passive = _passive.Checked
            };
        }

        private void SaveProfile()
        {
            var s = AppSettings.Current;
            int i = _profiles.SelectedIndex;
            string name = i >= 0 ? s.FtpProfiles[i].Name : "Console";
            var p = FromFields(name);
            if (i >= 0) s.FtpProfiles[i] = p; else s.FtpProfiles.Add(p);
            AppSettings.Save();
            ReloadProfiles();
            _profiles.SelectedIndex = i >= 0 ? i : _profiles.Items.Count - 1;
            Log.Success("FTP profile '" + name + "' saved.");
        }

        private void NewProfile()
        {
            string name = Dlg.Input("New FTP profile", "Name for this console (e.g. 'RGH Slim', 'Original Xbox', 'PS4')", "Console " + (_profiles.Items.Count + 1));
            if (string.IsNullOrWhiteSpace(name)) return;
            AppSettings.Current.FtpProfiles.Add(FromFields(name.Trim()));
            AppSettings.Save();
            ReloadProfiles();
            _profiles.SelectedIndex = _profiles.Items.Count - 1;
        }

        private void DeleteProfile()
        {
            var s = AppSettings.Current;
            int i = _profiles.SelectedIndex;
            if (i < 0 || s.FtpProfiles.Count <= 1) { Dlg.Info("Profiles", "Keep at least one profile."); return; }
            if (!Dlg.Confirm("Delete profile", "Delete profile '" + s.FtpProfiles[i].Name + "'?", "Delete", true)) return;
            s.FtpProfiles.RemoveAt(i);
            s.SelectedFtpProfile = 0;
            AppSettings.Save();
            ReloadProfiles();
        }

        private void Connect()
        {
            if (string.IsNullOrWhiteSpace(_host.Text)) { Dlg.Warn("FTP", "Enter the console IP address."); return; }
            int i = _profiles.SelectedIndex;
            var p = FromFields(i >= 0 ? AppSettings.Current.FtpProfiles[i].Name : "Console");
            _ftp = new FtpService(p);
            Run(null, "Connecting to ftp://" + p.Host, async () =>
            {
                var root = await _ftp.ListAsync("/");
                Log.Success("FTP connected to " + p.Host + " (" + root.Count + " item(s) in /).");
                if (i >= 0) { AppSettings.Current.FtpProfiles[i] = p; AppSettings.Save(); }
                _browser.Attach(new FtpFs(_ftp));
            });
        }

        /// <summary>Uploads an extracted game folder into the Games folder of a drive (replaces the old Game Sender window).</summary>
        private void SendGame()
        {
            if (_ftp == null) { Dlg.Info("FTP", "Connect first."); return; }
            string folder = PickFolder("Choose the extracted game folder (the one containing default.xex)");
            if (folder == null) return;
            if (!File.Exists(Path.Combine(folder, "default.xex")) && !File.Exists(Path.Combine(folder, "default.xbe")) &&
                !Dlg.Confirm("Send game", "No default.xex / default.xbe in that folder. Send it anyway?", "Send"))
                return;
            string dest = Dlg.Input("Send game", "Destination folder on the console", "/Hdd1/Games");
            if (string.IsNullOrWhiteSpace(dest)) return;
            dest = "/" + dest.Trim().Trim('/');
            var cts = new System.Threading.CancellationTokenSource();
            Run(null, "Sending " + Path.GetFileName(folder), async () =>
            {
                using (Status.Begin("Sending " + Path.GetFileName(folder) + " to " + dest, cts))
                {
                    await _ftp.EnsurePathAsync(dest);
                    var state = new TransferProgress { Total = FtpService.MeasureLocal(folder) };
                    await _ftp.UploadDirectoryAsync(folder, FtpService.Combine(dest, Path.GetFileName(folder.TrimEnd('\\'))), Status.NewProgress(), state, cts.Token);
                    Log.Success("Game sent to " + dest);
                }
                _browser.Navigate(dest);
            });
        }
    }
}
