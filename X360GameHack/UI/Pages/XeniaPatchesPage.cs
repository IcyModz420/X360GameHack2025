using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public sealed class XeniaPatchesPage : PageBase
    {
        public override string Title => "Xenia Patches";
        public override string Subtitle => "Xenia Canary game patches (.patch.toml): 60 FPS, resolution, fixes and cheats - loaded here and written to the running game.";
        public override string PageGlyph => Glyph.Wrench;

        private readonly ListView _patches = ListStyle.Create(("Patch", 300), ("Author", 140), ("Writes", 70), ("Enabled", 70));
        private readonly TextBlock _patchInfo = new TextBlock("No patch file loaded.", Theme.Small, Theme.TextDim);
        private XeniaPatchFile _patchFile;

        public XeniaPatchesPage()
        {
            var patch = AddCard("Patch file",
                "Load a Xenia Canary .patch.toml (60 FPS, resolution, fixes, cheats...), switch the patches you want on, then use them in Xenia or on a real console.",
                Glyph.Wrench);
            _patches.Height = Theme.S(200);
            _patches.DoubleClick += (o, e) => TogglePatch();
            patch.Add(_patchInfo);
            patch.Add(_patches);
            Buttons(patch,
                Btn("Load .patch.toml...", Glyph.FolderOpen, LoadPatches),
                Btn("Enable / disable", Glyph.Check, TogglePatch),
                Btn("Get game-patches", Glyph.Globe, () => Tools.OpenUrl("https://github.com/xenia-canary/game-patches"), tip: "The community collection of patch files, one per game."));
            Note(patch, "A patch file belongs to one game and one version of it (Title ID and hash at the top of the file). Double-click a patch to switch it on or off.");

            var cols = AddColumns(2, 420);
            var inXenia = AddCard("In Xenia", "Xenia applies patch files itself when a game starts - that is the reliable way, and the only one that works for code.", Glyph.Play, cols);
            Buttons(inXenia,
                Btn("Install into Xenia", Glyph.Download, InstallIntoXenia, BtnKind.Primary, "Copies the file, with your on / off choices, into Xenia's patches folder."),
                Btn("Start the open game in Xenia", Glyph.Play, StartInXenia, tip: "Runs the open XEX, ISO or package in Xenia."),
                Btn("Write into the running game", Glyph.Send, ApplyToXenia, tip: "Writes the enabled patches into the game that is running in Xenia right now."));
            Note(inXenia,
                "1. Load the patch file and switch on what you want.\n2. Install into Xenia.\n3. Start the game - Xenia's log shows \"Applying patch\" for each one.\n\n" +
                "\"Write into the running game\" changes memory at once, without a restart. It works for values and data. Xenia keeps a game's code and read-only data read-only while it runs, " +
                "so a patch that changes those is left out (you are told which) - install it and restart the game instead.");

            var onConsole = AddCard("On a real console", "The same patches written into the game running on your console, over XBDM.", Glyph.Console, cols);
            Buttons(onConsole, Btn("Apply enabled to console", Glyph.Send, ApplyPatches, BtnKind.Primary));
            Note(onConsole, "Connect on the Neighborhood page first, start the game, then apply. The game on the console must be the same version the patch file was made for. Patches last until the game restarts.");
        }

        private void LoadPatches()
        {
            var f = PickFile("Open a Xenia patch file", "Xenia patches|*.patch.toml;*.toml|All files|*.*");
            if (f == null) return;
            try
            {
                _patchFile = XeniaPatchFile.Load(f);
                _patchInfo.Text = (_patchFile.TitleName ?? Path.GetFileName(f)) + "   Title ID " + _patchFile.TitleId +
                                  (_patchFile.MediaIds.Count > 0 ? "   Media ID " + string.Join(", ", _patchFile.MediaIds) : "");
                FillPatches();
                Log.Success("Loaded " + _patchFile.Patches.Count + " patch(es) from " + Path.GetFileName(f));
            }
            catch (Exception ex) { Dlg.Error("Patch file", ex.Message); }
        }

        private void FillPatches()
        {
            _patches.BeginUpdate();
            _patches.Items.Clear();
            foreach (var p in _patchFile.Patches)
            {
                var li = new ListViewItem(p.Name) { Tag = p };
                li.SubItems.Add(p.Author ?? "");
                li.SubItems.Add(p.Operations.Count.ToString());
                li.SubItems.Add(p.IsEnabled ? "Yes" : "");
                if (!string.IsNullOrEmpty(p.Description)) li.ToolTipText = p.Description;
                _patches.Items.Add(li);
            }
            _patches.EndUpdate();
        }

        private void TogglePatch()
        {
            if (_patchFile == null) return;
            foreach (ListViewItem li in _patches.SelectedItems)
            {
                var p = (XeniaPatch)li.Tag;
                p.IsEnabled = !p.IsEnabled;
                li.SubItems[3].Text = p.IsEnabled ? "Yes" : "";
            }
        }

        private void InstallIntoXenia()
        {
            if (_patchFile == null) { Dlg.Info("Patches", "Load a .patch.toml first."); return; }
            string xenia = Tools.Xenia;
            if (string.IsNullOrEmpty(xenia) || !File.Exists(xenia)) { Dlg.Info("Xenia", "Xenia was not found. Set its path on the Xenia page (Setup tab)."); return; }
            try
            {
                string to = XeniaPatchTools.Install(_patchFile, xenia);
                int on = _patchFile.Patches.Count(p => p.IsEnabled);
                Log.Success("Patch file installed for Xenia: " + to + " (" + on + " of " + _patchFile.Patches.Count + " patches switched on). Start the game to use it.");
            }
            catch (Exception ex) { Dlg.Warn("Install into Xenia", ex.Message); }
        }

        private void StartInXenia()
        {
            string xenia = Tools.Xenia;
            if (string.IsNullOrEmpty(xenia) || !File.Exists(xenia)) { Dlg.Info("Xenia", "Xenia was not found. Set its path on the Xenia page (Setup tab)."); return; }
            if (string.IsNullOrEmpty(WorkingFile.Path) || !File.Exists(WorkingFile.Path)) { Dlg.Info("Xenia", "Open the game first: its default.xex, an ISO or a package."); return; }
            XeniaHost.Start(WorkingFile.Path);
        }

        private void ApplyToXenia()
        {
            if (_patchFile == null) { Dlg.Info("Patches", "Load a .patch.toml first."); return; }
            var file = _patchFile;
            if (!file.Patches.Any(p => p.IsEnabled)) { Dlg.Info("Patches", "No patch is switched on. Double-click a patch, or select it and use Enable / disable."); return; }
            Run(null, "Writing patches into Xenia", async () =>
            {
                try
                {
                    using (var x = await System.Threading.Tasks.Task.Run(() => XeniaProcess.Attach()))
                    {
                        string running = x.RunningTitleId, wanted = (file.TitleId ?? "").Trim().Replace("0x", "").Replace("0X", "");
                        if (running != null && wanted.Length > 0 && !running.Equals(wanted, StringComparison.OrdinalIgnoreCase) &&
                            !Dlg.Confirm("Different game", "Xenia is running " + running + " but the patch file is for " + wanted + ". Write anyway?", "Write", true))
                            return;
                        var report = await System.Threading.Tasks.Task.Run(() => XeniaPatchTools.WriteEnabled(file, x));
                        if (report.Written.Count > 0)
                            Log.Success("Wrote into the game running in Xenia: " + string.Join(", ", report.Written) + " (" + report.Writes + " memory write(s)).");
                        if (report.AlreadyThere.Count > 0)
                            Log.Info("Already in the game's memory, nothing to write: " + string.Join(", ", report.AlreadyThere) + ".");
                        if (report.NotWritable.Count > 0)
                            Dlg.Info("Write into the running game",
                                "Not written: " + string.Join(", ", report.NotWritable) + ".\n\n" +
                                "Those change the game's code or read-only data, which Xenia keeps read-only while the game runs (or memory the game has not set up yet). " +
                                "Use \"Install into Xenia\" and restart the game: Xenia applies them as it starts." +
                                (report.Written.Count > 0 ? "\n\nWritten: " + string.Join(", ", report.Written) + "." : ""));
                    }
                }
                catch (InvalidOperationException ex) { Dlg.Info("Xenia", ex.Message); }
            });
        }

        private void ApplyPatches()
        {
            if (_patchFile == null) { Dlg.Info("Patches", "Load a .patch.toml first."); return; }
            if (!ConsoleService.IsConnected) { Dlg.Info("Patches", "Connect to the console on the Neighborhood page first."); return; }
            var file = _patchFile;
            Run(null, "Applying Xenia patches", async () =>
            {
                string running = await ConsoleService.RunAsync(c => { try { return c.GetTitleId().ToString("X8"); } catch { return ""; } });
                if (!string.IsNullOrEmpty(file.TitleId) && running.Length > 0 &&
                    !string.Equals(running, file.TitleId.Trim().Replace("0x", ""), StringComparison.OrdinalIgnoreCase) &&
                    !Dlg.Confirm("Different game", "The console is running " + running + " but the patch file is for " + file.TitleId + ". Apply anyway?", "Apply", true))
                    return;
                int writes = await ConsoleService.RunAsync(c => file.ApplyEnabled(c));
                Log.Success("Applied enabled patches (" + writes + " memory write(s)).");
            });
        }
    }
}
