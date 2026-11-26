using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;
using X360GameHack.UI.Pages;

namespace X360GameHack.UI
{
    /// <summary>
    /// Lists a game's title updates on XboxUnity. The download itself happens on XboxUnity's website (they do not
    /// allow direct downloads); the downloaded file is then verified, identified by its hash and installed from here.
    /// </summary>
    public sealed class TitleUpdateCard : Card
    {
        private readonly ListView _list = ListStyle.Create(("Media ID", 110), ("Version", 70), ("Base version", 110), ("Size", 90), ("Uploaded", 140));
        private readonly TextBlock _state = new TextBlock("", Theme.Small, Theme.TextDim);
        private readonly TextBlock _how = new TextBlock("", Theme.Small, Theme.TextDim);
        private List<TitleUpdateInfo> _found = new List<TitleUpdateInfo>();
        private uint _titleId;
        private string _mediaId = "";

        public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "X360GameHack", "Title Updates");

        public TitleUpdateCard() : base("Title updates (XboxUnity)",
            "Updates are listed per disc Media ID - the ones for your game are highlighted. Download them on XboxUnity's site, then add the file here: it is verified and identified before installing.", Glyph.Download)
        {
            _list.Height = Theme.S(170);
            _list.Columns[3].TextAlign = HorizontalAlignment.Right;
            Add(_state);
            Add(_list);
            Add(_how);
            var row = new WrapPanel();
            row.Add(new XButton("Find updates", Glyph.Search, BtnKind.Primary, (s, e) => Find()));
            row.Add(new XButton("Get it on XboxUnity", Glyph.Globe, BtnKind.Secondary, (s, e) => OpenSite()));
            row.Add(new XButton("Check my Downloads", Glyph.Refresh, BtnKind.Secondary, (s, e) => ScanDownloads()));
            row.Add(new XButton("Add downloaded file...", Glyph.OpenFile, BtnKind.Secondary, (s, e) => AddFile()));
            Add(row);
            _list.DoubleClick += (s, e) => OpenSite();
            SetGame(0, 0);
        }

        public void SetGame(uint titleId, uint mediaId)
        {
            _titleId = titleId;
            _mediaId = mediaId == 0 ? "" : mediaId.ToString("X8");
            _found = new List<TitleUpdateInfo>();
            _list.Items.Clear();
            _how.Text = "";
            _state.Text = titleId == 0 ? "Open a game XEX or package to look up its title updates."
                : "Title ID " + titleId.ToString("X8") + (_mediaId.Length > 0 ? "   Media ID " + _mediaId : "") + "   -  press Find updates.";
        }

        private async void Find()
        {
            if (_titleId == 0) { Dlg.Info("Title updates", "Open a game first."); return; }
            using (Status.Begin("Searching XboxUnity"))
            {
                try
                {
                    var all = await XboxUnity.FindAsync(_titleId, CancellationToken.None);
                    _found = all;
                    var ordered = all.OrderByDescending(t => t.MediaId == _mediaId).ThenBy(t => t.MediaId).ThenByDescending(t => t.Version).ToList();
                    _list.BeginUpdate();
                    _list.Items.Clear();
                    foreach (var t in ordered)
                    {
                        bool match = _mediaId.Length > 0 && t.MediaId == _mediaId;
                        var li = new ListViewItem(t.MediaId + (match ? "  ✔" : "")) { Tag = t, ForeColor = match ? Theme.Success : Theme.Text };
                        li.SubItems.Add("TU " + t.Version);
                        li.SubItems.Add(t.BaseVersion);
                        li.SubItems.Add(Core.Formats.FileSniffer.FormatSize(t.SizeKb * 1024));
                        li.SubItems.Add(t.UploadDate);
                        _list.Items.Add(li);
                    }
                    _list.EndUpdate();
                    int matching = ordered.Count(t => t.MediaId == _mediaId);
                    _state.Text = ordered.Count == 0 ? "XboxUnity has no title updates for " + _titleId.ToString("X8") + "."
                        : ordered.Count + " update(s) found" + (_mediaId.Length > 0 ? ", " + matching + " for your Media ID " + _mediaId + "." : ".") +
                          (matching == 0 && _mediaId.Length > 0 ? "  None match your disc - updates for another Media ID will not load." : "");
                    _how.Text = ordered.Count == 0 ? "" :
                        "To get one: press Get it on XboxUnity (the title ID is copied for the site's search), download the update there - usually the newest for your Media ID - " +
                        "then press Check my Downloads or Add downloaded file. You only need the latest update.";
                    Log.Info(_state.Text);
                }
                catch (Exception ex)
                {
                    _state.Text = "Lookup failed: " + ex.Message;
                    Log.Error("XboxUnity lookup failed: " + ex.Message);
                }
            }
        }

        private void OpenSite()
        {
            if (_titleId == 0) { Dlg.Info("Title updates", "Open a game first."); return; }
            string tid = _titleId.ToString("X8");
            try { Clipboard.SetText(tid); } catch { }
            Tools.OpenUrl(XboxUnity.SiteUrl);
            Log.Info("Opened XboxUnity. Search for title ID " + tid + " (copied to the clipboard)" +
                     (_mediaId.Length > 0 ? " and pick an update for Media ID " + _mediaId : "") + ". Then press Check my Downloads.");
        }

        // ------------------------------------------------------------------ downloaded files

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

        /// <summary>The user's Downloads folder (wherever they moved it).</summary>
        public static string DownloadsFolder()
        {
            try
            {
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out IntPtr p) == 0)
                {
                    try { return Marshal.PtrToStringUni(p); }
                    finally { Marshal.FreeCoTaskMem(p); }
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private static bool LooksLikePackage(FileInfo f)
        {
            if (f.Length < 0x2000 || f.Length > 4L * 1024 * 1024 * 1024) return false;
            try
            {
                var m = Core.Formats.BE.ReadHead(f.FullName, 4);
                string magic = System.Text.Encoding.ASCII.GetString(m);
                return magic == "LIVE" || magic == "PIRS" || magic == "CON ";
            }
            catch { return false; }
        }

        private async void ScanDownloads()
        {
            if (_found.Count == 0) { Dlg.Info("Title updates", "Press Find updates first, so the downloaded file can be matched against XboxUnity's list."); return; }
            string dir = DownloadsFolder();
            using (Status.Begin("Looking for title updates in " + dir))
            {
                var hits = await Task.Run(() =>
                {
                    var list = new List<(string Path, TitleUpdateInfo Tu)>();
                    if (!Directory.Exists(dir)) return list;
                    var recent = DateTime.Now.AddDays(-30);
                    foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                                 .Where(x => x.LastWriteTime >= recent && (x.Attributes & FileAttributes.ReparsePoint) == 0)
                                 .OrderByDescending(x => x.LastWriteTime).Take(400))
                    {
                        if (!LooksLikePackage(f)) continue;
                        try
                        {
                            var tu = XboxUnity.Identify(f.FullName, _found);
                            if (tu != null) list.Add((f.FullName, tu));
                        }
                        catch { /* incomplete or another package */ }
                    }
                    return list;
                });
                if (hits.Count == 0)
                {
                    Dlg.Info("Title updates", "No downloaded update for this game was found in " + dir + ".\n\nDownload it on XboxUnity first (Get it on XboxUnity), or use Add downloaded file if you saved it somewhere else.");
                    return;
                }
                var best = hits.OrderByDescending(h => h.Tu.MediaId == _mediaId).ThenByDescending(h => h.Tu.Version).First();
                Offer(best.Path, best.Tu);
            }
        }

        private void AddFile()
        {
            string path;
            using (var d = new OpenFileDialog { Title = "Pick the title update you downloaded", InitialDirectory = DownloadsFolder(), RestoreDirectory = true })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                path = d.FileName;
            }
            TitleUpdateInfo tu;
            try { tu = _found.Count > 0 ? XboxUnity.Identify(path, _found) : null; XboxUnity.Verify(path, null); }
            catch (Exception ex) { Dlg.Error("Title update", ex.Message); return; }
            var pkg = Core.Formats.StfsPackage.Read(path);
            if (pkg.ContentType != 0x000B0000)
            {
                Dlg.Warn("Title update", "That file is a " + pkg.ContentTypeName + ", not a title update.");
                return;
            }
            if (_titleId != 0 && pkg.TitleId != _titleId &&
                !Dlg.Confirm("Different game", "That update is for title " + pkg.TitleIdHex + ", but the open game is " + _titleId.ToString("X8") + ". Continue anyway?", "Continue", true))
                return;
            Offer(path, tu);
        }

        private void Offer(string path, TitleUpdateInfo tu)
        {
            string what = tu != null ? "TU " + tu.Version + " for Media ID " + tu.MediaId + " - matches XboxUnity's listing" : "an intact title update package (not in the listed updates)";
            bool wrongDisc = tu != null && _mediaId.Length > 0 && tu.MediaId != _mediaId;
            Log.Success("Verified " + Path.GetFileName(path) + ": " + what + ".");
            int pick = Dlg.Choose("Title update verified",
                Path.GetFileName(path) + " is " + what + "." + (wrongDisc ? "\n\nWarning: it is for Media ID " + tu.MediaId + ", your game is " + _mediaId + " - it will most likely not load." : "") +
                "\n\nWhat would you like to do?",
                new[] { "Install to USB / console (Console Setup)", "Copy to my Title Updates folder", "Show the file" }, "Continue");
            if (pick == 0)
            {
                MainForm.Instance.Navigate(typeof(SetupPage));
                MainForm.Instance.GetPage<SetupPage>().AddPackages(new[] { path });
            }
            else if (pick == 1)
            {
                try
                {
                    Directory.CreateDirectory(DefaultFolder);
                    string target = Path.Combine(DefaultFolder, PathSafety.SafeName(Path.GetFileName(path)));
                    File.Copy(path, target, true);
                    Log.Success("Copied to " + target);
                    Tools.OpenFolder(target);
                }
                catch (Exception ex) { Dlg.Error("Copy failed", ex.Message); }
            }
            else if (pick == 2) Tools.OpenFolder(path);
        }
    }
}
