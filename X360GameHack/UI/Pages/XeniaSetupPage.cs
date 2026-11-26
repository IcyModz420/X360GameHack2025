using System.IO;
using System.Linq;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>
    /// The Setup tab of the Xenia page: which Xenia runs the games (the bundled Xenia Canary unless another is chosen),
    /// where games copied from a console or an Xbox drive are kept, starting any game file, and controllers.
    /// </summary>
    public sealed class XeniaSetupPage : PageBase
    {
        public override string Title => "Xenia setup";
        public override string Subtitle => "Which Xenia runs your games, where copies are kept, and controllers.";
        public override string PageGlyph => Glyph.Settings;

        private readonly XTextBox _xenia = new XTextBox(@"Path to xenia_canary.exe");
        private readonly TextBlock _pads = new TextBlock("", Theme.Body, Theme.Text);

        public XeniaSetupPage()
        {
            var s = AppSettings.Current;
            var cols = AddColumns(2, 420);

            var xenia = AddCard("Xenia (Xbox 360)", "Runs XEX files, disc images and Games on Demand / XBLA packages. Xenia Canary comes with this program - nothing to download.", Glyph.Game, cols);
            _xenia.Text = Tools.Xenia;                  // the bundled Xenia Canary unless another one was chosen
            _xenia.TextChanged += (o, e) => { s.XeniaPath = _xenia.Text.Trim(); AppSettings.Save(); };
            Field(xenia, null, _xenia, Btn("", Glyph.FolderOpen, () => { var f = PickFile("Locate Xenia", "Xenia|xenia*.exe|Programs|*.exe"); if (f != null) _xenia.Text = f; }));
            Buttons(xenia,
                Btn("Run working file", Glyph.Play, () => RunXenia(WorkingFile.Path), BtnKind.Primary),
                Btn("Run a file...", Glyph.OpenFile, () => { var f = PickFile("Game to run in Xenia"); if (f != null) RunXenia(f); }),
                Btn("Use the bundled Xenia", Glyph.Refresh, () => { if (File.Exists(Tools.BundledXenia)) _xenia.Text = Tools.BundledXenia; else Dlg.Info("Xenia", "xenia_canary.exe is not next to X360GameHack.exe."); }, tip: "Back to the Xenia Canary that ships with X360GameHack."),
                Btn("Newer Xenia builds", Glyph.Globe, () => Tools.OpenUrl("https://github.com/xenia-canary/xenia-canary-releases/releases")));
            var games = new XTextBox(XeniaPlay.GamesDir) { Text = s.XeniaGamesDir };
            games.TextChanged += (o, e) => { s.XeniaGamesDir = games.Text.Trim(); AppSettings.Save(); };
            Field(xenia, "Games from the console", games, Btn("", Glyph.FolderOpen, () => { var f = PickFolder("Folder for games copied from the console"); if (f != null) games.Text = f; }));
            Note(xenia, "To play a game that is on your console or an Xbox drive: Neighborhood, FTP or Drive Explorer > select the game > Play in Xenia. It is copied into the folder above once, shows up on the Games tab, and starts straight away from then on.");
            Note(xenia, "Tip: Xenia runs retail XEX files unmodified - don't RGH-patch a copy you want to emulate.");

            var pads = AddCard("Controllers", "Xbox, PlayStation 4 / 5 and Switch Pro controllers - and about 2,000 others - work in Xenia over USB or Bluetooth. No DS4Windows or extra driver needed.", Glyph.Game, cols);
            var every = new XCheck("Every controller works in Xenia", s.XeniaAllControllers,
                "Whenever the program starts Xenia it keeps Xenia's input setting on \"any\" - its Xbox (XInput), SDL and keyboard drivers together. " +
                "SDL is the part that reads PlayStation, Switch and most other controllers; the list of controllers it knows ships next to xenia_canary.exe.");
            every.CheckedChanged += (o, e) => { s.XeniaAllControllers = every.Checked; AppSettings.Save(); };
            pads.Add(every);
            pads.Add(_pads);
            Buttons(pads, Btn("Check controllers", Glyph.Refresh, ShowControllers, tip: "Lists the controllers Windows sees right now."));
            Note(pads, "Bluetooth: pair the controller in Windows Settings > Bluetooth first (PS4: hold Share + PS until the light flashes; PS5: hold Create + PS). " +
                       "The first controller is player 1. If a PlayStation controller does nothing in Xenia, close DS4Windows or Steam (they can hide the real controller) and try again.");
            ShowControllers();
        }

        private void ShowControllers()
        {
            var found = GameControllers.Connected();
            _pads.Text = found.Count == 0
                ? "No controller is connected right now. Plug one in, or pair it over Bluetooth, then press Check controllers."
                : "Connected now: " + string.Join(", ", found.Select(c => c.Name)) + ".";
            _pads.ForeColor = found.Count == 0 ? Theme.TextDim : Theme.Text;
            _pads.Parent?.PerformLayout();
            if (found.Count > 0) Log.Info("Controllers: " + string.Join(", ", found.Select(c => c.Name + " [" + c.VendorId.ToString("X4") + ":" + c.ProductId.ToString("X4") + "]")));
        }

        private static void RunXenia(string game)
        {
            string xenia = Tools.Xenia;
            if (string.IsNullOrWhiteSpace(xenia) || !File.Exists(xenia)) { Dlg.Info("Xenia", "Set the path to Xenia first."); return; }
            if (string.IsNullOrEmpty(game) || !File.Exists(game)) { Dlg.Info("Xenia", "Open a XEX, ISO or package first."); return; }
            if (XeniaHost.Start(game)) XeniaLibrary.MarkPlayed(game);
        }
    }
}
