using System;
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
    /// Browse / extract / replace files inside an STFS package (saves, DLC, TUs...) with full rehash.
    /// Every edit is verified by re-reading the whole hash tree; the original is backed up first.
    /// </summary>
    public sealed class PackageContentsCard : Card
    {
        private readonly ListView _list = ListStyle.Create(("File", 330), ("Size", 100), ("Blocks", 70));
        private readonly TextBlock _state = new TextBlock("", Theme.Small, Theme.TextDim);
        private string _path;

        public PackageContentsCard() : base("Contents",
            "Files stored inside the package. Replace keeps the hash tree valid (rehash); the console signature is not renewed - fine on RGH / JTAG / exploited consoles.", Glyph.FolderOpen)
        {
            _list.Height = Theme.S(200);
            _list.Columns[1].TextAlign = HorizontalAlignment.Right;
            _list.DoubleClick += (s, e) => Extract();
            Add(_state);
            Add(_list);
            var row = new WrapPanel();
            row.Add(new XButton("Extract...", Glyph.Download, BtnKind.Secondary, (s, e) => Extract()));
            row.Add(new XButton("Extract all...", Glyph.Library, BtnKind.Secondary, (s, e) => ExtractAll()));
            row.Add(new XButton("View GPD", Glyph.List, BtnKind.Secondary, (s, e) => ViewGpd()));
            row.Add(new XButton("Replace...", Glyph.Upload, BtnKind.Primary, (s, e) => Replace()));
            row.Add(new XButton("Add file...", Glyph.Add, BtnKind.Secondary, (s, e) => AddNew()));
            row.Add(new XButton("Delete", Glyph.Remove, BtnKind.Secondary, (s, e) => Delete()));
            row.Add(new XButton("Verify integrity", Glyph.Shield, BtnKind.Secondary, (s, e) => Verify()));
            Add(row);
        }

        public async void Load(string path)
        {
            _path = path;
            _list.Items.Clear();
            _state.Text = "Reading package...";
            try
            {
                var entries = await Task.Run(() => { using (var v = StfsVolume.Open(path)) return v.Entries.ToList(); });
                if (path != _path) return;
                _list.BeginUpdate();
                foreach (var e in entries.Where(x => !x.IsDirectory).OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
                {
                    var li = new ListViewItem(e.Path) { Tag = e };
                    li.SubItems.Add(FileSniffer.FormatSize(e.Size));
                    li.SubItems.Add(((e.Size + 4095) / 4096).ToString());
                    _list.Items.Add(li);
                }
                _list.EndUpdate();
                _state.Text = _list.Items.Count + " file(s).";
            }
            catch (Exception ex) { _state.Text = "Contents unavailable: " + ex.Message; }
        }

        private StfsEntry Selected() => _list.SelectedItems.Count > 0 ? (StfsEntry)_list.SelectedItems[0].Tag : null;

        private async void Extract()
        {
            var e = Selected();
            if (e == null) { Dlg.Info("Extract", "Select a file."); return; }
            {
                string target = PageBase.PickSave("Extract " + e.Name, PathSafety.SafeName(e.Name)), pkg = _path;
                if (target == null) return;
                using (Status.Begin("Extracting " + e.Name))
                {
                    try
                    {
                        await Task.Run(() => { using (var v = StfsVolume.Open(pkg)) v.ExtractFile(v.Entries.First(x => x.Path == e.Path), target, CancellationToken.None); });
                        Log.Success("Extracted " + e.Path + " (every block hash-verified) -> " + target);
                    }
                    catch (Exception ex) { Dlg.Error("Extract failed", ex.Message); }
                }
            }
        }

        /// <summary>Extracts every file into a folder, keeping the package's own folders. Each block is hash-verified on the way out.</summary>
        private async void ExtractAll()
        {
            if (_path == null || _list.Items.Count == 0) { Dlg.Info("Extract all", "There is nothing to extract."); return; }
            string folder = PageBase.PickFolder("Extract every file of the package to");
            if (folder == null) return;
            string pkg = _path;
            using (Status.Begin("Extracting the package"))
            {
                try
                {
                    int count = await Task.Run(() => ExtractAllTo(pkg, folder));
                    Log.Success("Extracted " + count + " file(s) from " + Path.GetFileName(pkg) + " (every block hash-verified) -> " + folder);
                }
                catch (Exception ex) { Log.Error("Extract all failed: " + ex.Message); Dlg.Error("Extract all failed", ex.Message); }
            }
        }

        /// <summary>
        /// Writes every file of a package below <paramref name="folder"/>. Names come from the package, so each part is
        /// made safe and the result must stay inside the chosen folder.
        /// </summary>
        public static int ExtractAllTo(string packagePath, string folder)
        {
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            int count = 0;
            using (var v = StfsVolume.Open(packagePath))
                foreach (var e in v.Entries.Where(x => !x.IsDirectory))
                {
                    string relative = string.Join(Path.DirectorySeparatorChar.ToString(), e.Path.Split(new[] { (char)92, (char)47 }, StringSplitOptions.RemoveEmptyEntries).Select(PathSafety.SafeName));
                    string target = Path.GetFullPath(Path.Combine(root, relative));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The package holds a file name that points outside the chosen folder: " + e.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    v.ExtractFile(e, target, CancellationToken.None);
                    count++;
                }
            return count;
        }

        private async void ViewGpd()
        {
            var e = Selected();
            if (e == null || !e.Name.EndsWith(".gpd", StringComparison.OrdinalIgnoreCase)) { Dlg.Info("View GPD", "Select a .gpd file in the list."); return; }
            string pkg = _path;
            try
            {
                byte[] data = await Task.Run(() => { using (var v = StfsVolume.Open(pkg)) return v.ReadFile(v.Entries.First(x => x.Path == e.Path)); });
                MainForm.Instance.Navigate(typeof(Pages.GpdPage));
                MainForm.Instance.GetPage<Pages.GpdPage>().Show(Path.GetFileName(pkg) + " / " + e.Path, data);
            }
            catch (Exception ex) { Dlg.Error("View GPD", ex.Message); }
        }

        private async void Replace()
        {
            var e = Selected();
            if (e == null) { Dlg.Info("Replace", "Select the file to replace."); return; }
            string src = PageBase.PickFile("Replace " + e.Name + " with...");
            if (src == null) return;
            var op = WorkingFile.BeginOperation("Replacing " + e.Name);
            if (op == null) return;
            string pkg = _path;
            using (op)
            using (Status.Begin("Replacing " + e.Name))
            {
                try
                {
                    byte[] data = File.ReadAllBytes(src);
                    string bak = pkg + ".bak";
                    if (!File.Exists(bak)) File.Copy(pkg, bak);
                    await Task.Run(() => StfsEditor.ReplaceFile(pkg, e.Path, data));
                    Log.Success("Replaced " + e.Path + " and rehashed the package (verified). Backup: " + bak);
                    Load(pkg);
                    WorkingFile.Refresh();
                }
                catch (Exception ex) { Log.Error("Replace failed: " + ex.Message); Dlg.Error("Replace failed", ex.Message + "\n\nThe package was not changed."); }
            }
        }

        /// <summary>Runs one package edit with the usual guard rails: one operation at a time, a backup first, reload after.</summary>
        private async void Change(string what, string done, Action<string> edit)
        {
            var op = WorkingFile.BeginOperation(what);
            if (op == null) return;
            string pkg = _path;
            using (op)
            using (Status.Begin(what))
            {
                try
                {
                    string bak = pkg + ".bak";
                    if (!File.Exists(bak)) File.Copy(pkg, bak);
                    await Task.Run(() => edit(pkg));
                    Log.Success(done + " The package was rehashed and verified. Backup: " + bak);
                    Load(pkg);
                    WorkingFile.Refresh();
                }
                catch (Exception ex) { Log.Error(what + " failed: " + ex.Message); Dlg.Error(what + " failed", ex.Message + "\n\nThe package was not changed."); }
            }
        }

        private void AddNew()
        {
            if (_path == null) return;
            string src = PageBase.PickFile("Add a file to the package");
            if (src == null) return;
            byte[] data;
            try { data = File.ReadAllBytes(src); }
            catch (Exception ex) { Dlg.Error("Add file", ex.Message); return; }
            string name = Path.GetFileName(src);
            Change("Adding " + name, "Added " + name + ".", pkg => StfsEditor.AddFile(pkg, name, data));
        }

        private void Delete()
        {
            var e = Selected();
            if (e == null) { Dlg.Info("Delete", "Select the file to delete."); return; }
            if (!Dlg.Confirm("Delete " + e.Name, "Remove " + e.Path + " from the package? A .bak backup of the package is kept.", "Delete", true)) return;
            Change("Deleting " + e.Name, "Deleted " + e.Path + ".", pkg => StfsEditor.DeleteFile(pkg, e.Path));
        }

        private async void Verify()
        {
            if (_path == null) return;
            string pkg = _path;
            using (Status.Begin("Verifying package"))
            {
                try
                {
                    var r = await Task.Run(() => ConSignature.Inspect(pkg));
                    bool ok = r.HashesChecked && r.HashesOk && r.Loads;
                    string msg = !r.HashesChecked && r.State != ConSignature.State.NotPackage ? "This package is too large to verify here (over ~115 MB)."
                               : ok ? "Every data block, the hash tree and the header hash are valid. " + r.Describe()
                               : r.Describe() + (r.HashProblem.Length > 0 ? " (" + r.HashProblem + ")" : "");
                    if (ok) Log.Success(msg); else Log.Error(msg);
                    Dlg.Info("Package integrity", msg);
                }
                catch (Exception ex) { Dlg.Error("Verify failed", ex.Message); }
            }
        }
    }
}
