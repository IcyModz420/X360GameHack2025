using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// Messages of the update helper (X360GameHack.exe --apply-update runs after the program closed): the program's own
    /// message window in the theme saved in Settings, like every other window. Settings are only read here, never saved.
    /// </summary>
    internal static class UpdateMessage
    {
        public static void Show(string text)
        {
            try { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); } catch (InvalidOperationException) { }
            AppSettings.Load();
            using (var reference = new Form()) Theme.Init(reference);     // this screen's scale, as the main window does
            Theme.Use(ThemePalette.Active());
            Dlg.Warn("X360GameHack update", text);
        }
    }

    /// <summary>
    /// The update window: two plain questions, each answered Yes or No.
    ///   1. "Update to x now?" - Yes downloads the update and checks it against the signed update notice. Nothing is
    ///      downloaded before that.
    ///   2. "Install x now?" - asked only after the check passed, with the SHA-256 the signed notice gives, the SHA-256 of
    ///      the file that arrived and, for a portable copy, every file the update adds or replaces with its own SHA-256 on
    ///      screen. Yes installs (the helper checks the file once more right before it changes anything).
    /// No leaves everything as it is. Skip this version and the GitHub releases page are always offered too.
    /// </summary>
    public sealed class UpdateDialog : DialogForm
    {
        private enum Step { Ask, Downloading, Ready, Retry, GitHubOnly }

        private readonly UpdateManifest _m;
        private readonly UpdateRoute _route;
        private readonly UpdateFile _file;
        private readonly TextBlock _state = new TextBlock("", Theme.Body, Theme.TextDim) { Height = Theme.S(40), Flags = TextFormatFlags.WordBreak };
        private readonly TextBlock _question = new TextBlock("", Theme.BodyBold, Theme.Text) { Height = Theme.S(44), Flags = TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter };
        private readonly XProgress _bar = new XProgress { Visible = false };
        private readonly XTextBox _expected = new XTextBox(null, false, true), _got = new XTextBox(null, false, true);
        private readonly ListView _files = ListStyle.Create(("File", 250), ("Change", 80), ("Size", 80), ("SHA-256", 470));
        private readonly XButton _yes, _no, _github, _skip;
        private Step _step;
        private string _path;
        private CancellationTokenSource _cts;

        /// <summary>True when the user chose to install: the update starts as soon as X360GameHack closes, so the caller closes it.</summary>
        public bool Installing { get; private set; }

        /// <summary>Shows the window for <paramref name="m"/>. True when the user chose to install it.</summary>
        public static bool Offer(IWin32Window owner, UpdateManifest m)
        {
            using (var d = new UpdateDialog(m)) { d.ShowDialog(owner); return d.Installing; }
        }

        public UpdateDialog(UpdateManifest m) : base("Update available")
        {
            _m = m;
            _route = AppUpdater.RouteForThisCopy();
            _file = _route == UpdateRoute.Installer ? m.Installer : m.Portable;

            // what the update is (scrolls on a short screen) above the question and its answers (always on screen)
            var body = new VStack { Padding = new Padding(0, 0, 0, Theme.S(18)), Spacing = Theme.S(10) };
            var content = new VStack { Padding = new Padding(Theme.S(22), Theme.S(18), Theme.S(22), Theme.S(4)), Spacing = Theme.S(10) };
            var answer = new VStack { Padding = new Padding(Theme.S(22), 0, Theme.S(22), 0), Spacing = Theme.S(10) };
            var scroll = new ScrollArea(content) { BackColor = Theme.Surface };
            body.Add(scroll);
            body.Add(answer);
            body.FillChild = scroll;
            var head = new HStack { Height = Theme.S(36), Spacing = Theme.S(10) };
            head.Add(new TextBlock(Glyph.Download, Theme.IconLarge, Theme.Accent) { Width = Theme.S(36), Height = Theme.S(36) });
            var title = new TextBlock("X360GameHack " + m.Version + " is out", Theme.CardTitle, Theme.Text) { Height = Theme.S(30), Flags = TextFormatFlags.VerticalCenter };
            head.Add(title); head.FillChild = title;
            content.Add(head);
            content.Add(new TextBlock("You have " + Pastebin.Version + "." + (m.Published.Length > 0 ? "  Released " + m.Published.Replace("T", " ").Replace("Z", " UTC") + "." : "") +
                                   "  The update notice is signed with X360GameHack's update key, so its version and checksums can be trusted.", Theme.Small, Theme.TextDim)
                { Height = Theme.S(34), Flags = TextFormatFlags.WordBreak });

            var notes = new XTextBox(null, true) { Height = Theme.S(96) };
            notes.Box.ReadOnly = true;
            notes.Text = m.Notes.Length > 0 ? m.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n") : "(no release notes in the update notice - see the GitHub page)";
            content.Add(new TextBlock("What's new", Theme.BodyBold, Theme.Text) { Height = Theme.S(20) });
            content.Add(notes);

            string what = _route == UpdateRoute.Installer ? "the installer (Windows asks for permission when it runs)"
                        : _route == UpdateRoute.Portable ? "the portable zip (its files are copied over this program folder)"
                        : "nothing - this copy is in a folder X360GameHack cannot change";
            content.Add(new TextBlock("Download: " + (_route == UpdateRoute.None ? what : _file.Name + ", " + FileSniffer.FormatSize(_file.Size) + " - " + what), Theme.Body, Theme.Text)
                { Height = Theme.S(22), Flags = TextFormatFlags.EndEllipsis });
            content.Add(new TextBlock("SHA-256 the signed update notice gives for it", Theme.Small, Theme.TextDim) { Height = Theme.S(18) });
            _expected.Box.ReadOnly = true; _expected.Text = _route == UpdateRoute.None ? "" : _file.Sha256;
            content.Add(_expected);
            content.Add(new TextBlock("SHA-256 of the file that was downloaded", Theme.Small, Theme.TextDim) { Height = Theme.S(18) });
            _got.Box.ReadOnly = true; _got.Text = "(not downloaded yet)";
            content.Add(_got);
            content.Add(_bar);
            content.Add(new TextBlock("What installing does to this program folder (filled in after the download is checked)", Theme.Small, Theme.TextDim) { Height = Theme.S(18) });
            _files.Height = Theme.S(170);
            content.Add(_files);
            answer.Add(_state);
            answer.Add(_question);

            // the two answers on the right; the other ways on the left
            var buttons = new HStack { Height = Theme.S(34), Spacing = Theme.S(8) };
            _skip = new XButton("Skip this version", null, BtnKind.Secondary);
            _github = new XButton("Open the GitHub page", Glyph.Globe, BtnKind.Secondary);
            var gap = new Panel();
            _yes = new XButton("Yes", Glyph.Check, BtnKind.Primary);
            _no = new XButton("No", null, BtnKind.Secondary) { DialogResult = DialogResult.Cancel };
            foreach (var b in new Control[] { _skip, _github, gap, _yes, _no }) buttons.Add(b);
            buttons.FillChild = gap;
            answer.Add(buttons);

            // the texts go in before the window measures itself, so the question and the answers are always on screen
            if (_route == UpdateRoute.None)
            {
                _step = Step.GitHubOnly;
                _question.Text = "Open the GitHub page to get " + m.Version + "?";
                _state.Text = "This copy is in a folder X360GameHack cannot change by itself, so it cannot update itself. Nothing was downloaded or changed.";
                _state.ForeColor = Theme.Warning;
            }
            else
            {
                _step = Step.Ask;
                _question.Text = "Update to " + m.Version + " now?";
                _state.Text = "Yes downloads it and checks it against the signed update notice - nothing is installed until you answer Yes a second time. No leaves everything as it is.";
            }
            Shrinkable = new Control[] { _files, notes };       // on a small screen these give up height first
            Body(body, Theme.S(820));
            CancelButton = _no;          // Esc and the close box answer No; Enter answers nothing (no accidental install)

            _skip.Click += (s, e) =>
            {
                AppSettings.Current.SkippedUpdateVersion = _m.Version;
                AppSettings.Save();
                Log.Info("Skipping X360GameHack " + _m.Version + ". Settings > Check for updates now still offers it.");
                DialogResult = DialogResult.Cancel;
            };
            _github.Click += (s, e) => Tools.OpenUrl(Pastebin.ReleasesUrl);
            _yes.Click += async (s, e) => await YesAsync();
            FormClosing += (s, e) => { try { _cts?.Cancel(); } catch (ObjectDisposedException) { } };
        }

        private async Task YesAsync()
        {
            switch (_step)
            {
                case Step.Ask:
                case Step.Retry: await DownloadAsync(); break;
                case Step.Ready: Install(); break;
                case Step.GitHubOnly: Tools.OpenUrl(Pastebin.ReleasesUrl); DialogResult = DialogResult.Cancel; break;
            }
        }

        private void Retry(string why, string log)
        {
            if (IsDisposed) return;
            _bar.Visible = false;
            _state.ForeColor = Theme.Danger;
            _state.Text = why;
            if (log != null) Log.Warn(log);
            _step = Step.Retry;
            _question.Text = "Try the download again?";
            _yes.Enabled = true; _skip.Enabled = true;
            FitHeight();
        }

        private async Task DownloadAsync()
        {
            _step = Step.Downloading;
            _yes.Enabled = false; _skip.Enabled = false;
            _bar.Visible = true; _bar.Value = 0;
            _state.ForeColor = Theme.TextDim;
            _state.Text = "Downloading " + _file.Name + "...";
            _question.Text = "Downloading and checking " + _m.Version + " - No stops it.";
            FitHeight();
            _cts = new CancellationTokenSource();
            var progress = new Progress<Core.Net.TransferProgress>(p => { if (!IsDisposed) { _bar.Value = p.Percent; _state.Text = "Downloading " + _file.Name + " - " + FileSniffer.FormatSize(p.Done) + " of " + FileSniffer.FormatSize(p.Total); } });
            try { _path = await AppUpdater.DownloadAsync(_file, _m.Version, progress, _cts.Token); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                _got.Text = "(refused - nothing was kept)";
                Retry("Not downloaded: " + ex.Message + " Nothing was installed.", "Update " + _m.Version + " was not downloaded: " + ex.Message);
                return;
            }
            if (IsDisposed) return;
            // the download already had to match to be kept; hash it once more here so what is shown is what is on disk
            string got = await Task.Run(() => AppUpdater.Sha256Of(_path));
            if (IsDisposed) return;
            _got.Text = got;
            bool match = got == _file.Sha256 && new FileInfo(_path).Length == _file.Size;
            if (!match)
            {
                try { File.Delete(_path); } catch { }
                Log.Error("Update " + _m.Version + ": the download does not match the signed notice (" + got + ") - deleted.");
                Retry("The downloaded file does not match the signed update notice - it was deleted and nothing was installed.", null);
                return;
            }
            _bar.Value = 100;
            List<UpdateEntry> entries;
            try
            {
                entries = _route == UpdateRoute.Portable
                    ? await Task.Run(() => AppUpdater.ListUpdate(_path))
                    : new List<UpdateEntry> { new UpdateEntry { Path = _file.Name + " (installer)", Size = _file.Size, Sha256 = got, Change = "Runs" } };
            }
            catch (Exception ex)
            {
                Retry("The update could not be read: " + ex.Message + " Nothing was installed.", "Update " + _m.Version + " could not be read: " + ex.Message);
                return;
            }
            if (IsDisposed) return;
            _files.BeginUpdate();
            _files.Items.Clear();
            foreach (var x in entries.OrderBy(x => x.Change == "Same").ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            {
                var it = new ListViewItem(x.Path);
                it.SubItems.Add(x.Change);
                it.SubItems.Add(FileSniffer.FormatSize(x.Size));
                it.SubItems.Add(x.Sha256);
                if (x.Change == "Same") it.ForeColor = Theme.TextDim;
                _files.Items.Add(it);
            }
            _files.EndUpdate();
            int added = entries.Count(x => x.Change == "New"), replaced = entries.Count(x => x.Change == "Replaces"), same = entries.Count(x => x.Change == "Same");
            _state.ForeColor = Theme.Success;
            _state.Text = "Checked: the download's SHA-256 and size match the signed update notice.";
            _question.Text = "Install " + _m.Version + " now?  " +
                             (_route == UpdateRoute.Portable ? "It replaces " + replaced + " file(s) and adds " + added + " (" + same + " stay as they are); X360GameHack closes and starts again by itself."
                                                             : "The installer runs when X360GameHack closes (Windows asks for permission).");
            _step = Step.Ready;
            _yes.Enabled = true; _skip.Enabled = true;
            FitHeight();
            Log.Success("Update " + _m.Version + " downloaded and checked: SHA-256 " + got + ".");
        }

        private void Install()
        {
            if (_path == null || _step != Step.Ready || Installing) return;
            // never close the program under someone without asking
            bool busy = WorkingFile.Kind != WorkingFileKind.None || ConsoleService.IsConnected;
            if (busy && !Dlg.Confirm("Install the update", "X360GameHack closes to install " + _m.Version + " and starts again by itself.\n\nA file is open or a console is connected - close X360GameHack now?", "Install now"))
                return;
            try { AppUpdater.StartApply(_route, _path, _file, _m.Version, true); }
            catch (Exception ex) { Dlg.Error("Install the update", "The update could not be started: " + ex.Message + "\n\nNothing was changed."); return; }
            Log.Success("Installing X360GameHack " + _m.Version + " - it is checked once more (SHA-256) right before it is installed, and the program starts again by itself" +
                        (_route == UpdateRoute.Installer ? " (Windows asks for permission to install)." : "."));
            Installing = true;
            DialogResult = DialogResult.OK;
        }
    }
}
