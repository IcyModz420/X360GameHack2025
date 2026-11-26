using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;
using X360GameHack.UI.Pages;

namespace X360GameHack.UI
{
    /// <summary>
    /// Under Rehash &amp; resign: every profile it can find - in Xenia, in the Saves folder, on the Xbox drive open in Drive
    /// Explorer and on the connected console (its hard drive and USB sticks) - with its profile ID, device ID and console
    /// ID, and each console drive's own device ID. Click one and its IDs go into Rehash &amp; resign. Only reads: nothing is
    /// written until Rehash &amp; resign is pressed.
    /// </summary>
    public sealed class IdPickerCard : Card
    {
        // what the last search found, shared by the cards of every page (Saves, Profiles, X360Packages)
        private static List<IdChoice> _cache;
        private static string _cacheNote = "";
        private static DateTime _cachedAt;
        private static string _cachedFrom = "";      // the places looked in then (another drive or the console connecting = look again)
        private static int _cacheVersion;
        private static Task<(List<IdChoice> Rows, string Note)> _running;

        private readonly RetailSignCard _target;
        private readonly ListView _list = ListStyle.Create(("Profile or drive", 170), ("Profile ID", 150), ("Device ID", 320), ("Console ID", 110), ("Found on", 230));
        private readonly TextBlock _summary = new TextBlock("", Theme.Small, Theme.TextDim);
        private readonly XButton _find, _folder, _use;
        private List<IdChoice> _rows = new List<IdChoice>();
        private int _shownVersion = -1;

        public IdPickerCard(RetailSignCard target) : base("Pick the IDs",
            "Click one of your profiles: its profile ID, device ID and console ID go into Rehash & resign above. It looks in Xenia, your Saves folder, " +
            "the Xbox drive open in Drive Explorer and the connected console (its hard drive and USB sticks).", Glyph.Profile)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            var row = new WrapPanel();
            _find = row.Add(new XButton("Find my profiles", Glyph.Search, BtnKind.Primary, (s, e) => Look(true)));
            Tip.Set(_find, "Looks again everywhere: Xenia, your Saves folder, the drive open in Drive Explorer and the connected console. Only reads.");
            _folder = row.Add(new XButton("A USB stick or folder...", Glyph.FolderOpen, BtnKind.Secondary, (s, e) => LookInFolder()));
            Tip.Set(_folder, "A folder on this PC that holds a console's Content folder - a copy of a drive, or a USB stick with one.");
            _use = row.Add(new XButton("Use these IDs", Glyph.Check, BtnKind.Secondary, (s, e) => UseSelected()));
            Tip.Set(_use, "Puts the selected profile's IDs (or the selected drive's device ID) into Rehash & resign. Nothing is written until you press Rehash & resign.");
            Add(row);

            _list.MultiSelect = false;
            _list.Height = Theme.S(190);
            _list.DoubleClick += (s, e) => UseSelected();
            _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) UseSelected(); else if (e.KeyCode == Keys.F5) Look(true); };
            Add(_list);
            Add(_summary);
            Add(new TextBlock(
                "The profile ID decides whose save it is. The device ID should be the drive the save will sit on - pick a profile on that drive, or the drive " +
                "itself (a drive gives only its device ID). The console ID only records which console signed it; your keyvault's signature is what stock consoles check.",
                Theme.Small, Theme.TextFaint));
            // each time the tab is shown: the last search when nothing changed since, else a new one
            VisibleChanged += (s, e) => { if (Visible && IsHandleCreated) Look(false); };
            HandleCreated += (s, e) => { if (Visible) BeginInvoke((Action)(() => { if (!IsDisposed && Visible) Look(false); })); };
        }

        /// <summary>The rows shown now (for tests).</summary>
        public IReadOnlyList<IdChoice> Rows => _rows;

        /// <summary>The search running now, or the last one.</summary>
        public Task Searching { get; private set; } = Task.CompletedTask;

        /// <summary>The places a search looks in, as they are now - when this changes, the last search is out of date.</summary>
        private static string Places()
        {
            var de = DriveExplorerPage.Instance;
            return Tools.Xenia + "|" + AppSettings.Current.SavesFolderResolved + "|" + (de != null && !de.IsDisposed ? de.OpenDriveName : "") + "|" +
                   (ConsoleService.IsConnected ? ConsoleService.Ip : "");
        }

        private async void Look(bool force)
        {
            var task = LookAsync(force);
            Searching = task;
            await task;
        }

        private async Task LookAsync(bool force)
        {
            if (!force && _cache != null && _cachedFrom == Places() && (DateTime.Now - _cachedAt).TotalMinutes < 5)
            {
                if (_shownVersion != _cacheVersion) { Show(_cache, _cacheNote); _shownVersion = _cacheVersion; }
                return;
            }
            if (!force && _running != null && !_running.IsCompleted) { await _running; if (!IsDisposed && _shownVersion != _cacheVersion) { Show(_cache, _cacheNote); _shownVersion = _cacheVersion; } return; }
            _find.Enabled = false;
            _summary.Text = "Looking for your profiles...";
            try
            {
                if (_running == null || _running.IsCompleted)
                {
                    string from = Places();
                    _running = FindAllAsync(CancellationToken.None);
                    var (found, said) = await _running;
                    _cache = found; _cacheNote = said; _cachedAt = DateTime.Now; _cachedFrom = from; _cacheVersion++;
                }
                else await _running;
                if (!IsDisposed) { Show(_cache, _cacheNote); _shownVersion = _cacheVersion; }
            }
            catch (Exception ex)
            {
                if (!IsDisposed) _summary.Text = "Could not look for profiles: " + ex.Message;
            }
            finally { if (!_find.IsDisposed) _find.Enabled = true; }
        }

        /// <summary>Every place there is to look, now. Runs on the window's thread (Drive Explorer's drive is read from there); the reading itself is off it.</summary>
        private static async Task<(List<IdChoice> Rows, string Note)> FindAllAsync(CancellationToken ct)
        {
            var all = new List<IdChoice>();
            var looked = new List<string>();
            var problems = new List<string>();
            async Task From(string what, Func<Task<List<IdChoice>>> read)
            {
                try { all.AddRange(await read()); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { problems.Add(what + ": " + ex.Message); }
            }

            string xenia = Tools.Xenia;
            if (!string.IsNullOrWhiteSpace(xenia) && File.Exists(xenia))
            {
                looked.Add("Xenia");
                await From("Xenia", () => IdFinder.FromStoreAsync(XeniaContent.Store(xenia), "Xenia", false, ct));
            }

            string saves = AppSettings.Current.SavesFolderResolved;
            looked.Add("your Saves folder");
            await From("Saves folder", () => Task.Run(() => IdFinder.FromProfileFiles(IdFinder.ProfileFilesUnder(saves, ct), "Saves folder", ct), ct));
            // only files on this PC's own drives: a network path that is gone could keep the search waiting
            var recent = (AppSettings.Current.RecentFiles ?? new List<string>())
                .Where(f => !string.IsNullOrEmpty(f) && f.Length > 2 && f[1] == ':' && !f.StartsWith(@"\\", StringComparison.Ordinal)).ToList();
            await From("files opened before", () => Task.Run(() => IdFinder.FromProfileFiles(recent, "Opened before", ct), ct));

            var de = DriveExplorerPage.Instance;
            if (de != null && !de.IsDisposed && de.OpenDriveName.Length > 0)
            {
                string name = de.OpenDriveName;
                looked.Add(name);
                foreach (var vol in de.OpenVolumes)
                    await From(name, () => IdFinder.FromStoreAsync(new FatxContentStore(vol), name, true, ct));
            }

            if (ConsoleService.IsConnected)
            {
                looked.Add("the console");
                await From("the console", () => IdFinder.FromStoreAsync(new XbdmContentStore(), "Console", true, ct));
            }

            string note = "Looked in " + Join(looked) + "." +
                          (de == null || de.IsDisposed || de.OpenDriveName.Length == 0 ? " An Xbox drive on this PC: open it in Drive Explorer, then press Find my profiles." : "") +
                          (!ConsoleService.IsConnected ? " A console: connect it, then press Find my profiles." : "") +
                          (problems.Count > 0 ? " Could not read " + string.Join("; ", problems) + "." : "");
            return (IdFinder.Merge(all), note);
        }

        private static string Join(List<string> parts) =>
            parts.Count <= 1 ? string.Join("", parts) : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts.Last();

        private void Show(List<IdChoice> rows, string note)
        {
            _rows = rows ?? new List<IdChoice>();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var r in _rows)
            {
                var it = new ListViewItem(new[] { r.Label, r.ProfileId, Spaced(r.DeviceId), r.ConsoleId, r.Where }) { Tag = r };
                if (r.IsDrive) it.ForeColor = Theme.TextDim;
                _list.Items.Add(it);
            }
            _list.EndUpdate();
            int profiles = _rows.Count(r => !r.IsDrive), drives = _rows.Count - profiles;
            _summary.Text = (profiles == 0 && drives == 0 ? "No profiles found. " : profiles + " profile(s)" + (drives > 0 ? " and " + drives + " drive(s)" : "") + " - double-click one to use its IDs. ") + note;
            PerformLayout();
        }

        /// <summary>A device ID in groups of eight, so two can be told apart at a glance.</summary>
        private static string Spaced(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return "";
            var parts = new List<string>();
            for (int i = 0; i < hex.Length; i += 8) parts.Add(hex.Substring(i, Math.Min(8, hex.Length - i)));
            return string.Join(" ", parts);
        }

        private void UseSelected()
        {
            if (_list.SelectedItems.Count == 0 || !(_list.SelectedItems[0].Tag is IdChoice c)) { Dlg.Info("Pick the IDs", "Select a profile (or a drive) in the list first."); return; }
            Use(c);
        }

        /// <summary>Puts the choice's IDs into Rehash &amp; resign: a profile gives its profile ID and - when its file has them - its device and console ID; a drive only its device ID.</summary>
        public void Use(IdChoice c)
        {
            if (c.IsDrive)
            {
                _target.SetIds(null, c.DeviceId, null);
                Log.Info("Device ID of " + c.DriveName + " filled in. Press Rehash & resign to write it into the file.");
                return;
            }
            _target.SetIds(c.ProfileId, c.DeviceId, c.ConsoleId);
            var parts = new List<string> { "profile ID" };
            if (c.DeviceId.Length > 0) parts.Add("device ID");
            if (c.ConsoleId.Length > 0) parts.Add("console ID");
            Log.Info("Took the " + Join(parts) + " of " + c.Label + " (" + string.Join(", ", c.FoundOn) + ")." +
                     (c.DeviceId.Length == 0 ? " It has no device ID (a profile made in Xenia) - the file keeps its own." : "") +
                     " Press Rehash & resign to write " + (parts.Count == 1 ? "it" : "them") + " into the file.");
        }

        /// <summary>A folder on this PC: its Content folder's profiles and drive. An Xbox-formatted USB stick (Xbox360\Data0000...) is read through the console.</summary>
        private async void LookInFolder()
        {
            string dir = PageBase.PickFolder("Choose a folder that holds a Content folder (a copy of a console drive, or a USB stick with one)");
            if (dir == null) return;
            var store = new LocalContentStore(dir);
            _folder.Enabled = false;
            try
            {
                var roots = await store.ContentRootsAsync(CancellationToken.None);
                if (roots.Count == 0)
                {
                    bool xboxStick = File.Exists(Path.Combine(dir, "Xbox360", "Data0000")) || File.Exists(Path.Combine(dir, "Data0000"));
                    Dlg.Info("Pick the IDs", xboxStick
                        ? "That is a USB stick formatted by an Xbox 360. Its files are packed in the Data0000... files, which this PC does not read. " +
                          "Plug the stick into the console, connect the console and press Find my profiles - the console's USB sticks are read too."
                        : "There is no Content folder in " + dir + ". Choose the folder that has the Content folder in it (or the Content folder itself).");
                    return;
                }
                string name = Path.GetFileName(dir.TrimEnd('\\'));
                if (name.Length == 0) name = dir;
                Task<List<IdChoice>> read = IdFinder.FromStoreAsync(store, name, true, CancellationToken.None);
                Searching = read;
                var found = await read;
                var merged = IdFinder.Merge(_rows.Select(r => r.Clone()).Concat(found));
                Show(merged, found.Count == 0 ? "No profiles in " + dir + "." : "Added what is in " + dir + ".");
            }
            catch (Exception ex) { Dlg.Error("Pick the IDs", ex.Message); }
            finally { _folder.Enabled = true; }
        }

        /// <summary>Forgets the last search (tests; a drive opened or closed elsewhere is picked up by Find my profiles).</summary>
        public static void ForgetCache() { _cache = null; _running = null; _cachedFrom = ""; }
    }
}
