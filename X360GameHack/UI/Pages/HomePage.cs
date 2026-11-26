using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public sealed class HomePage : PageBase
    {
        public override string Title => "Welcome back";
        public override string Subtitle => "Drop a game file, disc image or package to get started.";
        public override string PageGlyph => Glyph.Home;

        private readonly VStack _recent;
        private readonly TextBlock _consoleState;

        public HomePage()
        {
            var drop = new DropZone
            {
                Hint = "Drop a file here or click to browse",
                SubHint = "XEX executables  •  STFS packages (GOD, XBLA, DLC, TU)  •  Saves  •  ISO / XISO\n" +
                          "Original Xbox XBE  •  Xbox drive images (original Xbox and Xbox 360)\n" +
                          "Drop several ISOs at once for bulk extraction, or folders of packages to install them.",
                Height = Theme.S(190)
            };
            drop.Click += (s, e) => MainForm.Instance.OpenWithDialog();
            drop.FilesDropped += f => MainForm.Instance.OpenDropped(f);
            Body.Add(drop);

            var cols = AddColumns(3, 260);
            Tile(cols, Glyph.Disc, "Bulk extract ISOs", "Extract many XISOs and auto-patch every XEX for RGH or devkit.", typeof(IsoPage), () => MainForm.Instance.GetPage<IsoPage>()?.ShowBulk());
            Tile(cols, Glyph.Network, "Neighborhood", "Browse, upload and launch over XBDM with drag & drop.", typeof(NeighborhoodPage));
            Tile(cols, Glyph.Memory, "Peek Poker", "Read, write and freeze live console memory.", typeof(PeekPokePage));
            Tile(cols, Glyph.Usb, "Install to USB / console", "Drop GOD, XBLA, DLC, TUs and saves - they go to the right Content folder.", typeof(SetupPage));
            Tile(cols, Glyph.Hdd, "Xbox hard drives", "Original Xbox and Xbox 360 drives: every partition, files and saves - drag files out to Windows.", typeof(DriveExplorerPage));
            Tile(cols, Glyph.Game, "Original Xbox games", "Play them in xemu, and read or patch an XBE: Title ID, region, RAM.", typeof(XbePage));
            Tile(cols, Glyph.Globe, "FTP", "Aurora, FSD, XeXMenu, PS3 and PS4 file transfers.", typeof(FtpPage));
            Tile(cols, Glyph.Help, "Q & A", "Answers for patching, installing and console setup.", typeof(HelpPage));

            var two = AddColumns(2, 380);
            var recentCard = AddCard("Recent files", "Click to reopen.", Glyph.Library, two);
            _recent = new VStack { Spacing = Theme.S(2) };
            recentCard.Add(_recent);

            var consoleCard = AddCard("Console", null, Glyph.Console, two);
            _consoleState = new TextBlock("", Theme.Body, Theme.TextDim);
            consoleCard.Add(_consoleState);
            Buttons(consoleCard,
                Btn("Connect over XBDM", Glyph.Link, () => MainForm.Instance.Navigate(typeof(NeighborhoodPage)), BtnKind.Primary),
                Btn("Console info", Glyph.Info, () => MainForm.Instance.Navigate(typeof(ConsolePage))));
            Hooks.ConsoleChanged(this, RefreshConsole);
        }

        private void Tile(Columns parent, string glyph, string title, string text, Type page, Action then = null)
        {
            var card = new Card(title, text, glyph) { Cursor = Cursors.Hand, Interactive = true };
            card.Click += (s, e) => { MainForm.Instance.Navigate(page); then?.Invoke(); };
            parent.Add(card);
        }

        public override void OnActivated()
        {
            RefreshRecent();
            RefreshConsole();
        }

        private void RefreshConsole()
        {
            _consoleState.Text = ConsoleService.IsConnected
                ? "Connected to " + (ConsoleService.Name.Length > 0 ? ConsoleService.Name + " at " : "") + ConsoleService.Ip + "."
                : "Not connected. Load xbdm.xex as a DashLaunch plugin, then connect from the Neighborhood page.";
        }

        private void RefreshRecent()
        {
            _recent.SuspendLayout();
            foreach (Control c in _recent.Controls.Cast<Control>().ToList()) { _recent.Controls.Remove(c); c.Dispose(); }
            var files = AppSettings.Current.RecentFiles.Where(File.Exists).Take(8).ToList();
            if (files.Count == 0) _recent.Add(new TextBlock("Nothing yet - files you open show up here.", Theme.Body, Theme.TextFaint));
            foreach (var f in files)
            {
                string path = f;
                var b = new XButton(Path.GetFileName(f) + "   —   " + Path.GetDirectoryName(f), Glyph.Document, BtnKind.Ghost)
                {
                    LeftAlign = true, Height = Theme.S(32)
                };
                Tip.Set(b, f);
                b.Click += (s, e) => WorkingFile.Set(path);
                _recent.Add(b);
            }
            _recent.ResumeLayout();
            _recent.PerformLayout();
        }
    }
}
