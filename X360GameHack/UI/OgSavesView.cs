using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// Drive Explorer's Saves tab on an ORIGINAL Xbox drive: E:\UDATA\{title ID}\{save} - each game named by its TitleMeta.xbx
    /// ("TitleName="), each save by its SaveMeta.xbx ("Name="), as the console's own dashboard shows them. Copy to PC keeps a save
    /// the way it can go back onto any original Xbox drive (the game's folder with TitleMeta.xbx and its pictures, and the save's
    /// folder); Put a save back puts such a folder into UDATA - a save of the same name already there is never replaced.
    /// </summary>
    public sealed class OgSavesView : VStack
    {
        public sealed class OgSave
        {
            public FatxVolume Volume;
            public FatxEntry Title, Save;           // Save null = the game's own files beside its saves
            public string TitleId = "", Game = "", Name = "";
            public long Bytes;
            public DateTime When;
        }

        private readonly ListView _list = ListStyle.Create(("Game", 230), ("Save", 230), ("Size", 90), ("Saved", 135), ("Folder", 260));
        private readonly TextBlock _summary = new TextBlock("", Theme.Body, Theme.TextDim) { Height = Theme.S(24) };
        private readonly XButton _copy, _put, _refresh;
        private List<FatxVolume> _vols = new List<FatxVolume>();
        private Func<bool> _canChange;
        private Func<string, string> _pickFolder;
        private Func<bool, string, IDisposable> _use;    // the page's hold on the drive (writes?, title) - null when it has to wait
        private int _scan;

        public OgSavesView()
        {
            Spacing = Theme.S(8);
            Padding = new Padding(0, Theme.S(6), 0, 0);
            Add(_summary);
            var buttons = new WrapPanel();
            _copy = buttons.Add(new XButton("Copy to PC...", Glyph.Download, BtnKind.Primary, (s, e) => CopyToPc()) { Height = Theme.S(30), Compact = true });
            Tip.Set(_copy, "The selected saves go to a folder on this PC with their game's TitleMeta.xbx and pictures - the way they can go back onto any original Xbox drive.");
            _put = buttons.Add(new XButton("Put a save back...", Glyph.Upload, BtnKind.Secondary, (s, e) => PutBack()) { Height = Theme.S(30), Compact = true });
            Tip.Set(_put, "A save copied with Copy to PC (its game's folder) goes into UDATA on this drive. Needs Allow changes. A save of the same name already there is never replaced.");
            _refresh = buttons.Add(new XButton("Refresh", Glyph.Refresh, BtnKind.Ghost, (s, e) => Scan()) { Height = Theme.S(30), Compact = true });
            Add(buttons);
            _list.Height = Theme.S(360);
            _list.Columns[2].TextAlign = HorizontalAlignment.Right;
            _list.DoubleClick += (s, e) => CopyToPc();
            Add(_list);
            FillChild = _list;
        }

        /// <summary>The drive that is open; <paramref name="canChange"/> says whether it may be changed now (and tells the user why not).</summary>
        public void SetDrive(List<FatxVolume> vols, Func<bool> canChange, Func<string, string> pickFolder, Func<bool, string, IDisposable> use = null)
        {
            _vols = vols ?? new List<FatxVolume>();
            _canChange = canChange; _pickFolder = pickFolder; _use = use;
            _scan++;
            _list.Items.Clear();
            _summary.Text = _vols.Count == 0 ? "No drive open." : "Open this tab to list the saves.";
        }

        public List<OgSave> Saves => _list.Items.Cast<ListViewItem>().Select(i => (OgSave)i.Tag).ToList();

        /// <summary>
        /// The files that describe the game itself in its UDATA folder: its name (TitleMeta.xbx), its picture (TitleImage.xbx) and
        /// the picture its saves show when they have none of their own (SaveImage.xbx beside the saves - 54 of 85 games on a real
        /// drive keep one). They go wherever its saves go; they are not "the game's own files".
        /// </summary>
        internal static bool DescribesGame(string fileName)
            => fileName.Equals("TitleMeta.xbx", StringComparison.OrdinalIgnoreCase) || fileName.Equals("TitleImage.xbx", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("SaveImage.xbx", StringComparison.OrdinalIgnoreCase);

        /// <summary>Reads every game's folder in UDATA (on every partition that has one) and its saves. Only reads.</summary>
        public async void Scan()
        {
            int scan = ++_scan;
            var vols = _vols.ToList();
            _summary.Text = "Reading the saves...";
            List<OgSave> found;
            try { found = await Task.Run(() => Read(vols)); }
            catch (Exception ex) { if (scan == _scan && !IsDisposed) _summary.Text = "The saves could not be read: " + ex.Message; return; }
            if (scan != _scan || IsDisposed) return;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var s in found.OrderBy(x => x.Game, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Save == null ? 1 : 0).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var it = new ListViewItem(s.Game) { Tag = s };
                it.SubItems.Add(s.Name);
                it.SubItems.Add(FileSniffer.FormatSize(s.Bytes));
                it.SubItems.Add(s.When == DateTime.MinValue ? "" : s.When.ToString("yyyy-MM-dd HH:mm"));
                it.SubItems.Add(s.Volume.Name.Split(' ')[0] + "\\UDATA\\" + s.TitleId + (s.Save != null ? "\\" + s.Save.Name : ""));
                if (s.Save == null) it.ForeColor = Theme.TextDim;
                _list.Items.Add(it);
            }
            _list.EndUpdate();
            int games = found.Select(x => x.TitleId).Distinct(StringComparer.OrdinalIgnoreCase).Count(), saves = found.Count(x => x.Save != null);
            _summary.Text = saves == 0 && games == 0 ? "No original Xbox saves on this drive."
                          : saves + " save" + (saves == 1 ? "" : "s") + " of " + games + " game" + (games == 1 ? "" : "s") + " - as the console lists them in UDATA.";
        }

        /// <summary>The games in UDATA and their saves (TitleMeta.xbx / SaveMeta.xbx give the names).</summary>
        internal static List<OgSave> Read(IEnumerable<FatxVolume> vols)
        {
            var list = new List<OgSave>();
            foreach (var v in vols.Where(x => !x.BigEndian))
            {
                var udata = v.ListRoot().FirstOrDefault(e => e.IsDirectory && e.Name.Equals("UDATA", StringComparison.OrdinalIgnoreCase));
                if (udata == null) continue;
                foreach (var t in v.ListDirectory(udata.FirstCluster).Where(e => e.IsDirectory))
                {
                    var inside = v.ListDirectory(t.FirstCluster);
                    string game = "";
                    var meta = inside.FirstOrDefault(e => !e.IsDirectory && e.Name.Equals("TitleMeta.xbx", StringComparison.OrdinalIgnoreCase));
                    if (meta != null) game = DriveDescriptions.XbxValue(v.ReadHead(meta, (int)Math.Min(meta.Size, 0x1000)), "TitleName");
                    if (game.Length == 0 && uint.TryParse(t.Name, System.Globalization.NumberStyles.HexNumber, null, out uint id)) game = TitleLookup.Cached(id) ?? "";
                    if (game.Length == 0) game = "Title " + t.Name.ToUpperInvariant();
                    foreach (var s in inside.Where(e => e.IsDirectory))
                    {
                        var sm = v.ListDirectory(s.FirstCluster).FirstOrDefault(e => !e.IsDirectory && e.Name.Equals("SaveMeta.xbx", StringComparison.OrdinalIgnoreCase));
                        string name = sm != null ? DriveDescriptions.XbxValue(v.ReadHead(sm, (int)Math.Min(sm.Size, 0x1000)), "Name") : "";
                        list.Add(new OgSave { Volume = v, Title = t, Save = s, TitleId = t.Name, Game = game, Name = name.Length > 0 ? name : s.Name, Bytes = v.MeasureDirectory(s), When = s.Modified });
                    }
                    // what the game keeps beside its saves (settings, its picture) - listed so it can be copied too
                    var loose = inside.Where(e => !e.IsDirectory && !DescribesGame(e.Name)).ToList();
                    if (loose.Count > 0)
                        list.Add(new OgSave { Volume = v, Title = t, TitleId = t.Name, Game = game, Name = "(the game's own files: " + loose.Count + ")", Bytes = loose.Sum(e => (long)e.Size), When = loose.Max(e => e.Modified) });
                }
            }
            return list;
        }

        /// <summary>
        /// Copies saves to <paramref name="destination"/>: per game a folder "{title ID} - {game}" with the files that describe the
        /// game (<see cref="DescribesGame"/>) and each chosen save's folder (or the game's own files). Returns the folders written.
        /// Only reads the drive.
        /// </summary>
        internal static List<string> CopyOut(IEnumerable<OgSave> saves, string destination, CancellationToken ct)
        {
            var written = new List<string>();
            foreach (var g in saves.GroupBy(s => s.Volume.Name + "|" + s.TitleId))
            {
                var first = g.First();
                var v = first.Volume;
                string folder = PathSafety.CombineUnder(destination, PathSafety.SafeName(first.TitleId + " - " + first.Game));
                Directory.CreateDirectory(folder);
                var inside = v.ListDirectory(first.Title.FirstCluster);
                foreach (var e in inside.Where(x => !x.IsDirectory))
                {
                    bool meta = DescribesGame(e.Name);
                    bool own = g.Any(s => s.Save == null);
                    if (!meta && !own) continue;
                    string target = PathSafety.CombineUnder(folder, PathSafety.SafeName(e.Name));
                    if (File.Exists(target)) File.Delete(target);
                    v.ExtractFile(e, target, null, ct);
                }
                foreach (var s in g.Where(x => x.Save != null))
                {
                    string target = PathSafety.CombineUnder(folder, PathSafety.SafeName(s.Save.Name));
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    v.ExtractDirectory(s.Save, target, null, ct);
                }
                written.Add(folder);
            }
            return written;
        }

        /// <summary>
        /// Puts a game's save folder from this PC (as Copy to PC made it: "{title ID} - {game}", or just "{title ID}") into
        /// UDATA on <paramref name="v"/>: the files that describe the game (when the drive has none yet) and each save folder.
        /// A save folder of the same name already on the drive is never replaced - those are named in the result.
        /// </summary>
        internal static (List<string> Added, List<string> Kept) PutIn(FatxVolume v, string localGame, CancellationToken ct)
        {
            string name = Path.GetFileName(localGame.TrimEnd('\\', '/'));
            string id = name.Length >= 8 ? name.Substring(0, 8) : "";
            if (!uint.TryParse(id, System.Globalization.NumberStyles.HexNumber, null, out _) || (name.Length > 8 && !name.Substring(8).StartsWith(" ")))
                throw new InvalidOperationException("Pick a game's save folder as Copy to PC makes it - its name starts with the game's Title ID (e.g. \"4541000d - Black\").");
            var bad = FatxVolume.NameProblemsIn(localGame);
            if (bad.Count > 0) throw new InvalidOperationException("Nothing was copied. These names are not accepted on an Xbox drive:\n" + string.Join("\n", bad.Take(10)));
            uint title = v.ResolveDirectory("UDATA\\" + id.ToLowerInvariant(), true);
            var there = v.ListDirectory(title);
            var added = new List<string>(); var kept = new List<string>();
            foreach (string f in Directory.GetFiles(localGame))
            {
                ct.ThrowIfCancellationRequested();
                string fn = Path.GetFileName(f);
                bool meta = DescribesGame(fn);
                if (there.Any(e => !e.IsDirectory && e.Name.Equals(fn, StringComparison.OrdinalIgnoreCase))) { if (!meta) kept.Add(fn); continue; }
                v.WriteFile(title, f, null, null, ct);
                if (!meta) added.Add(fn);
            }
            foreach (string d in Directory.GetDirectories(localGame))
            {
                ct.ThrowIfCancellationRequested();
                string dn = Path.GetFileName(d);
                if (there.Any(e => e.IsDirectory && e.Name.Equals(dn, StringComparison.OrdinalIgnoreCase))) { kept.Add(dn); continue; }
                v.WriteDirectory(title, d, dn, null, ct);
                added.Add(dn);
            }
            return (added, kept);
        }

        private List<OgSave> Selected() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (OgSave)i.Tag).ToList();

        private async void CopyToPc()
        {
            var sel = Selected();
            if (sel.Count == 0) { Dlg.Info("Copy to PC", "Select the saves to copy (Ctrl or Shift for several)."); return; }
            string to = _pickFolder?.Invoke("Copy the saves to");
            if (to == null) return;
            var use = _use?.Invoke(false, "Copy to PC");
            if (_use != null && use == null) return;
            var cts = new CancellationTokenSource();
            try
            {
                List<string> folders;
                using (Status.Begin("Copying " + sel.Count + " save(s) to this PC", cts))
                    folders = await Task.Run(() => CopyOut(sel, to, cts.Token));
                Log.Success("Copied " + sel.Count + " original Xbox save(s) to " + to + " - " + string.Join(", ", folders.Select(Path.GetFileName)) +
                            ". Each folder can go back onto any original Xbox drive with Put a save back.");
            }
            catch (OperationCanceledException) { Log.Warn("Copy to PC - cancelled."); }
            catch (Exception ex) { Log.Error("Copy to PC failed: " + ex.Message); Dlg.Error("Copy to PC", ex.Message); }
            finally { cts.Dispose(); use?.Dispose(); }
        }

        private async void PutBack()
        {
            var v = _vols.FirstOrDefault(x => !x.BigEndian && x.Name.StartsWith("E:")) ?? _vols.FirstOrDefault(x => !x.BigEndian);
            if (v == null) { Dlg.Info("Put a save back", "Open an original Xbox drive first."); return; }
            if (_canChange != null && !_canChange()) return;
            string from = _pickFolder?.Invoke("Pick the game's save folder (as Copy to PC made it: \"4541000d - Black\")");
            if (from == null) return;
            var use = _use?.Invoke(true, "Put a save back");
            if (_use != null && use == null) return;
            var cts = new CancellationTokenSource();
            try
            {
                (List<string> Added, List<string> Kept) r;
                using (Status.Begin("Putting the save back", cts))
                    r = await Task.Run(() => PutIn(v, from, cts.Token));
                Log.Success("Put " + r.Added.Count + " save item(s) into " + v.Name.Split(' ')[0] + "\\UDATA" + (r.Added.Count > 0 ? ": " + string.Join(", ", r.Added) : "") + ".");
                if (r.Kept.Count > 0)
                    Dlg.Info("Put a save back", "Already on the drive, so left as they are: " + string.Join(", ", r.Kept) + ".\n\nTo replace one, delete it on the drive first (Files tab) - then put it back.");
                Scan();
            }
            catch (OperationCanceledException) { Log.Warn("Put a save back - cancelled."); }
            catch (Exception ex) { Log.Error("Put a save back failed: " + ex.Message); Dlg.Error("Put a save back", ex.Message); }
            finally { cts.Dispose(); use?.Dispose(); }
        }
    }
}
