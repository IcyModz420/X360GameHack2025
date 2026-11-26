using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;

namespace X360GameHack.Core.Services
{
    public sealed class StoreItem
    {
        public string Name;
        public bool IsDirectory;
        public long Size;
        public DateTime Modified;
    }

    /// <summary>
    /// Somewhere Xbox 360 "Content" folders live: the console over XBDM, an Xbox hard drive (image or physical,
    /// read-only), or a folder on this PC. Used by the save browser.
    /// </summary>
    public interface IContentStore
    {
        string Title { get; }
        bool CanWrite { get; }
        bool IsReady { get; }
        Task<List<string>> ContentRootsAsync(CancellationToken ct);
        Task<List<StoreItem>> ListAsync(string dir, CancellationToken ct);
        Task<byte[]> ReadHeadAsync(string file, int count, CancellationToken ct);
        Task DownloadAsync(string file, string localPath, IProgress<TransferProgress> p, CancellationToken ct);
        Task UploadAsync(string localPath, string file, IProgress<TransferProgress> p, CancellationToken ct);
        Task DeleteAsync(string file, CancellationToken ct);
        string Combine(string dir, string name);
    }

    /// <summary>The console's drives over XBDM (Hdd, USB storage, memory units...).</summary>
    public sealed class XbdmContentStore : IContentStore
    {
        public string Title => "Console " + (ConsoleService.Name.Length > 0 ? ConsoleService.Name + " " : "") + ConsoleService.Ip;
        public bool CanWrite => true;
        public bool IsReady => ConsoleService.IsConnected;
        public string Combine(string dir, string name) => ConsoleService.Combine(dir, name);

        public async Task<List<string>> ContentRootsAsync(CancellationToken ct)
        {
            var roots = new List<string>();
            var seen = new HashSet<string>();     // a console shows one drive under two names (HDD and Hdd1, INTUSB and OnBoardMU)
            var drives = await ConsoleService.GetDrivesAsync();
            // Hard drive first, then USB / memory units.
            foreach (var d in drives.OrderBy(d => d.DriveName.StartsWith("Hdd", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(d => d.DriveName))
            {
                ct.ThrowIfCancellationRequested();
                string root = d.DriveName.TrimEnd(':') + ":\\";
                try
                {
                    var items = await ConsoleService.ListAsync(root);
                    var content = items.FirstOrDefault(i => i.IsDirectory && i.Name.Equals("Content", StringComparison.OrdinalIgnoreCase));
                    if (content == null) continue;
                    // the same drive under a second name has the very same folders with the very same dates
                    var inside = await ConsoleService.ListAsync(root + content.Name);
                    string print = string.Join("|", items.Concat(inside).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(i => i.Name.ToUpperInvariant() + "/" + i.Size + "/" + i.Modified.Ticks + "/" + i.IsDirectory));
                    if (seen.Add(print)) roots.Add(root + content.Name);
                }
                catch { /* drive not readable (DVD without disc, flash...) */ }
            }
            return roots;
        }

        public async Task<List<StoreItem>> ListAsync(string dir, CancellationToken ct)
            => (await ConsoleService.ListAsync(dir)).Select(i => new StoreItem { Name = i.Name, IsDirectory = i.IsDirectory, Size = i.Size, Modified = i.Modified }).ToList();

        public Task<byte[]> ReadHeadAsync(string file, int count, CancellationToken ct) => ConsoleService.ReadHeadAsync(file, count, ct);
        public Task DownloadAsync(string file, string localPath, IProgress<TransferProgress> p, CancellationToken ct) => ConsoleService.DownloadFileAsync(file, localPath, p, ct);
        public Task UploadAsync(string localPath, string file, IProgress<TransferProgress> p, CancellationToken ct) => ConsoleService.UploadToPathAsync(localPath, file, p, ct);
        public Task DeleteAsync(string file, CancellationToken ct) => ConsoleService.DeleteFileAsync(file, ct);
    }

    /// <summary>An Xbox 360 hard drive (image or physical) through the read-only FATX reader.</summary>
    public sealed class FatxContentStore : IContentStore
    {
        private readonly FatxVolume _vol;
        private readonly Dictionary<string, List<FatxEntry>> _dirs = new Dictionary<string, List<FatxEntry>>(StringComparer.OrdinalIgnoreCase);

        public FatxContentStore(FatxVolume volume) { _vol = volume; }
        public string Title => _vol.ToString();
        public bool CanWrite => _vol.Writable;
        public bool IsReady => true;
        public string Combine(string dir, string name) => dir.TrimEnd('\\') + "\\" + name;

        private List<FatxEntry> Dir(string path)
        {
            string key = "\\" + path.Trim('\\');
            if (_dirs.TryGetValue(key, out var cached)) return cached;
            List<FatxEntry> list;
            if (key == "\\") list = _vol.ListRoot();
            else
            {
                int cut = key.LastIndexOf('\\');
                string parent = key.Substring(0, cut), name = key.Substring(cut + 1);
                var e = Dir(parent).FirstOrDefault(x => x.IsDirectory && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new DirectoryNotFoundException(key);
                list = _vol.ListDirectory(e.FirstCluster);
            }
            _dirs[key] = list;
            return list;
        }

        private FatxEntry File(string path)
        {
            string p = "\\" + path.Trim('\\');
            int cut = p.LastIndexOf('\\');
            return Dir(p.Substring(0, Math.Max(1, cut))).FirstOrDefault(x => !x.IsDirectory && x.Name.Equals(p.Substring(cut + 1), StringComparison.OrdinalIgnoreCase))
                   ?? throw new FileNotFoundException(p);
        }

        public Task<List<string>> ContentRootsAsync(CancellationToken ct) => Task.Run(() =>
        {
            var c = Dir("\\").FirstOrDefault(x => x.IsDirectory && x.Name.Equals("Content", StringComparison.OrdinalIgnoreCase));
            return c == null ? new List<string>() : new List<string> { "\\" + c.Name };
        }, ct);

        public Task<List<StoreItem>> ListAsync(string dir, CancellationToken ct) => Task.Run(() =>
            Dir(dir).Select(e => new StoreItem { Name = e.Name, IsDirectory = e.IsDirectory, Size = e.Size, Modified = e.Modified }).ToList(), ct);

        public Task<byte[]> ReadHeadAsync(string file, int count, CancellationToken ct) => Task.Run(() => _vol.ReadHead(File(file), count), ct);

        public Task DownloadAsync(string file, string localPath, IProgress<TransferProgress> p, CancellationToken ct) => Task.Run(() =>
        {
            var e = File(file);
            var state = new TransferProgress { Total = e.Size, File = e.Name };
            _vol.ExtractFile(e, localPath, n => { state.Done += n; p?.Report(state); }, ct);
        }, ct);

        public Task UploadAsync(string localPath, string file, IProgress<TransferProgress> p, CancellationToken ct) => Task.Run(() =>
        {
            if (!_vol.Writable) throw new NotSupportedException("This drive is open read-only. In Drive Explorer, open a drive image and press Allow changes.");
            string full = "\\" + file.Trim('\\');
            int cut = full.LastIndexOf('\\');
            uint dir = _vol.ResolveDirectory(full.Substring(0, cut), true);
            var state = new TransferProgress { Total = new FileInfo(localPath).Length, File = Path.GetFileName(localPath) };
            _vol.WriteFile(dir, localPath, full.Substring(cut + 1), n => { state.Done += n; p?.Report(state); }, ct);
            _dirs.Clear();
        }, ct);

        public Task DeleteAsync(string file, CancellationToken ct) => Task.Run(() =>
        {
            if (!_vol.Writable) throw new NotSupportedException("This drive is open read-only. In Drive Explorer, open a drive image and press Allow changes.");
            _vol.Delete(File(file));
            _dirs.Clear();
        }, ct);
    }

    /// <summary>A folder on this PC that contains a Content folder (or is one).</summary>
    public sealed class LocalContentStore : IContentStore
    {
        private readonly string _root, _title;
        public LocalContentStore(string folder, string title = null) { _root = Path.GetFullPath(folder); _title = title; }
        public string Title => _title ?? _root;
        /// <summary>The folder it reads (Title may be a friendlier name).</summary>
        public string Folder => _root;
        public bool CanWrite => true;
        public bool IsReady => Directory.Exists(_root);
        public string Combine(string dir, string name) => Path.Combine(dir, name);

        public Task<List<string>> ContentRootsAsync(CancellationToken ct) => Task.Run(() =>
        {
            if (Path.GetFileName(_root.TrimEnd('\\')).Equals("Content", StringComparison.OrdinalIgnoreCase)) return new List<string> { _root };
            string c = Path.Combine(_root, "Content");
            return Directory.Exists(c) ? new List<string> { c } : new List<string>();
        }, ct);

        private string Inside(string path)
        {
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(_root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) && !full.Equals(_root, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Path is outside the chosen folder.");
            return full;
        }

        public Task<List<StoreItem>> ListAsync(string dir, CancellationToken ct) => Task.Run(() =>
        {
            var d = new DirectoryInfo(Inside(dir));
            if (!d.Exists) return new List<StoreItem>();
            return d.EnumerateFileSystemInfos()
                .Where(i => (i.Attributes & FileAttributes.ReparsePoint) == 0)
                .Select(i => new StoreItem { Name = i.Name, IsDirectory = i is DirectoryInfo, Size = (i as FileInfo)?.Length ?? 0, Modified = i.LastWriteTime })
                .ToList();
        }, ct);

        public Task<byte[]> ReadHeadAsync(string file, int count, CancellationToken ct) => Task.Run(() => BE.ReadHead(Inside(file), count), ct);
        public Task DownloadAsync(string file, string localPath, IProgress<TransferProgress> p, CancellationToken ct) => Task.Run(() => System.IO.File.Copy(Inside(file), localPath, true), ct);

        public Task UploadAsync(string localPath, string file, IProgress<TransferProgress> p, CancellationToken ct) => Task.Run(() =>
        {
            string target = Inside(file);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            System.IO.File.Copy(localPath, target, true);
        }, ct);

        public Task DeleteAsync(string file, CancellationToken ct) => Task.Run(() => System.IO.File.Delete(Inside(file)), ct);
    }

    /// <summary>One saved game found in a Content folder.</summary>
    public sealed class SaveInfo
    {
        public IContentStore Store;
        public string Path = "";          // full path inside the store
        public string FileName = "";
        public string Root = "";          // the Content folder it was found in (drive)
        public string ProfileId = "";     // owning profile (folder name, 16 hex)
        public string ProfileName = "";   // gamertag, read from the profile package
        public string TitleId = "";       // 8 hex
        public string GameName = "";
        public string SaveName = "";
        public long Size;
        public DateTime Modified;
        public string Signature = "";
        public string HeaderProfileId = "";
        public Image Icon;
        public string Error;
        /// <summary>A save in Xenia's own form: a folder of the game's files (with a header file in {title}\Headers\00000001), not a console save file.</summary>
        public bool XeniaFolder;

        public string Drive => Root.Split(':', '\\').FirstOrDefault(s => s.Length > 0) ?? "";
        public string ProfileLabel => ProfileName.Length > 0 ? ProfileName : ProfileId;
        public string GameLabel => GameName.Length > 0 ? GameName : "Title " + TitleId;
    }

    /// <summary>Finds every saved game: Content\{profile}\{title}\00000001\{file}.</summary>
    public static class SaveScanner
    {
        private const int HeadBytes = 0x971A;   // metadata + both thumbnails

        private static bool IsHex(string s, int len) => s != null && s.Length == len && s.All(Uri.IsHexDigit);

        private static async Task<List<StoreItem>> SafeList(IContentStore store, string dir, CancellationToken ct)
        {
            try { return await store.ListAsync(dir, ct); }
            catch (OperationCanceledException) { throw; }
            catch { return new List<StoreItem>(); }
        }

        public static async Task<List<SaveInfo>> ScanAsync(IContentStore store, IProgress<string> progress, CancellationToken ct)
        {
            var result = new List<SaveInfo>();
            var gamertags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // profile id -> name, from whichever drive holds the profile
            foreach (string root in await store.ContentRootsAsync(ct))
            {
                foreach (var profile in (await SafeList(store, root, ct)).Where(d => d.IsDirectory && IsHex(d.Name, 16) && d.Name.Trim('0').Length > 0))
                {
                    ct.ThrowIfCancellationRequested();
                    string pdir = store.Combine(root, profile.Name);
                    progress?.Report("Reading profile " + profile.Name);
                    string gamertag = await ProfileNameAsync(store, pdir, profile.Name, ct);
                    if (gamertag.Length > 0) gamertags[profile.Name] = gamertag;
                    foreach (var title in (await SafeList(store, pdir, ct)).Where(d => d.IsDirectory && IsHex(d.Name, 8)))
                    {
                        string sdir = store.Combine(store.Combine(pdir, title.Name), "00000001");
                        var inside = await SafeList(store, sdir, ct);
                        // saves Xenia made itself: folders, each with a header file in {title}\Headers\00000001
                        foreach (var d in inside.Where(x => x.IsDirectory))
                        {
                            ct.ThrowIfCancellationRequested();
                            var made = await XeniaFolderSaveAsync(store, root, pdir, profile.Name, gamertag, title.Name, d, ct);
                            if (made != null && made.GameName.Length == 0) made.GameName = await XeniaGameNameAsync(store, pdir, profile.Name, title.Name, ct);
                            if (made != null) result.Add(made);
                        }
                        foreach (var f in inside.Where(x => !x.IsDirectory))
                        {
                            ct.ThrowIfCancellationRequested();
                            var s = new SaveInfo
                            {
                                Store = store, Root = root, Path = store.Combine(sdir, f.Name), FileName = f.Name,
                                ProfileId = profile.Name.ToUpperInvariant(), ProfileName = gamertag, TitleId = title.Name.ToUpperInvariant(),
                                Size = f.Size, Modified = f.Modified
                            };
                            progress?.Report("Reading " + s.Path);
                            try
                            {
                                var head = await store.ReadHeadAsync(s.Path, HeadBytes, ct);
                                var pkg = StfsPackage.FromHeader(head, s.Path);
                                if (pkg.Valid)
                                {
                                    s.Signature = pkg.Signature;
                                    s.GameName = pkg.TitleName.Trim();
                                    s.SaveName = pkg.DisplayName.Trim();
                                    s.HeaderProfileId = pkg.ProfileId;
                                    s.Icon = pkg.TitleThumbnail ?? pkg.Thumbnail;
                                    if (!ReferenceEquals(s.Icon, pkg.Thumbnail)) pkg.Thumbnail?.Dispose();
                                }
                                else s.Error = "Not an STFS save";
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex) { s.Error = ex.Message; }
                            result.Add(s);
                        }
                    }
                }
            }
            // A profile often sits on a different drive from its saves (profile on a USB stick, saves on the hard drive).
            foreach (var s in result)
                if (s.ProfileName.Length == 0 && gamertags.TryGetValue(s.ProfileId, out string known)) s.ProfileName = known;
            // Saves without a title name borrow it from another save of the same game.
            foreach (var g in result.GroupBy(r => r.TitleId))
            {
                string name = g.Select(x => x.GameName).FirstOrDefault(n => n.Length > 0) ?? "";
                foreach (var s in g) if (s.GameName.Length == 0) s.GameName = name;
            }
            return result;
        }

        /// <summary>
        /// The gamertag of a profile Xenia made itself - a folder whose Account file is encrypted the same way as on a
        /// console. Null when <paramref name="profilePath"/> is not such a folder.
        /// </summary>
        public static async Task<string> XeniaGamertagAsync(IContentStore store, string profilePath, CancellationToken ct)
        {
            if (!(store is LocalContentStore) || !Directory.Exists(profilePath)) return null;
            try { return (ProfileAccount.Load(await store.ReadHeadAsync(store.Combine(profilePath, "Account"), 0x1000, ct)).Gamertag ?? "").Trim(); }
            catch (OperationCanceledException) { throw; }
            catch { return ""; }
        }

        /// <summary>
        /// A game's name from the list of games in a Xenia profile (FFFE07D1.gpd in its folder) - the header file the bundled
        /// Xenia writes for a save has the save's name only. "" when the profile does not list the game.
        /// </summary>
        private static async Task<string> XeniaGameNameAsync(IContentStore store, string pdir, string profile, string title, CancellationToken ct)
        {
            try
            {
                string gpd = store.Combine(store.Combine(store.Combine(store.Combine(pdir, "FFFE07D1"), "00010000"), profile), "FFFE07D1.gpd");
                var file = XdbfFile.Parse(await store.ReadHeadAsync(gpd, 4 << 20, ct));
                uint id = Convert.ToUInt32(title, 16);
                return (file.Titles().FirstOrDefault(x => x.TitleId == id)?.Name ?? "").Trim();
            }
            catch (OperationCanceledException) { throw; }
            catch { return ""; }
        }

        /// <summary>
        /// A save in Xenia's own form (a folder), read from the header file Xenia keeps for it - either form: the one the
        /// bundled Xenia writes (XCONTENT_AGGREGATE_DATA: names, no pictures) or a newer Xenia's (the whole package header).
        /// Null when there is no header file (not a save).
        /// </summary>
        private static async Task<SaveInfo> XeniaFolderSaveAsync(IContentStore store, string root, string pdir, string profile, string gamertag, string title, StoreItem folder, CancellationToken ct)
        {
            string titleDir = store.Combine(pdir, title);
            string header = store.Combine(store.Combine(store.Combine(titleDir, "Headers"), "00000001"), folder.Name + ".header");
            byte[] bytes;
            try { bytes = await store.ReadHeadAsync(header, HeadBytes, ct); }
            catch (OperationCanceledException) { throw; }
            catch { return null; }                       // no header file: not a save
            var info = XeniaContent.ReadHeader(bytes);
            if (info == null || info.ContentType != XeniaContent.SaveType) return null;
            string path = store.Combine(store.Combine(titleDir, "00000001"), folder.Name);
            var s = new SaveInfo
            {
                Store = store, Root = root, Path = path, FileName = folder.Name, XeniaFolder = true, Signature = "Xenia",
                ProfileId = profile.ToUpperInvariant(), ProfileName = gamertag, TitleId = title.ToUpperInvariant(),
                GameName = info.TitleName, SaveName = info.DisplayName, HeaderProfileId = info.Xuid.ToString("X16"),
                Size = await FolderSizeAsync(store, path, 0, ct), Modified = folder.Modified
            };
            if (!info.Aggregate)
            {
                var pkg = StfsPackage.FromHeader(bytes, header);
                s.Icon = pkg.TitleThumbnail ?? pkg.Thumbnail;
                if (!ReferenceEquals(s.Icon, pkg.Thumbnail)) pkg.Thumbnail?.Dispose();
            }
            return s;
        }

        /// <summary>Bytes in a folder and its sub-folders.</summary>
        public static async Task<long> FolderSizeAsync(IContentStore store, string dir, int depth, CancellationToken ct)
        {
            if (depth > 16) return 0;
            long total = 0;
            foreach (var i in await SafeList(store, dir, ct))
                total += i.IsDirectory ? await FolderSizeAsync(store, store.Combine(dir, i.Name), depth + 1, ct) : i.Size;
            return total;
        }

        /// <summary>Gamertag = display name of the profile package Content\{id}\FFFE07D1\00010000\{id}.</summary>
        private static async Task<string> ProfileNameAsync(IContentStore store, string profileDir, string id, CancellationToken ct)
        {
            try
            {
                string path = store.Combine(store.Combine(store.Combine(profileDir, "FFFE07D1"), "00010000"), id);
                string xenia = await XeniaGamertagAsync(store, path, ct);
                if (xenia != null) return xenia;
                var head = await store.ReadHeadAsync(path, 0x1712, ct);
                var pkg = StfsPackage.FromHeader(head, path);
                if (!pkg.Valid) return "";
                string shown = pkg.DisplayName.Trim();
                if (shown.Length > 0 && !shown.Equals(id, StringComparison.OrdinalIgnoreCase) && !IsHex(shown, 16)) return shown;
                // The package is usually just named after its own id; the gamertag is in the Account file inside it.
                string tmp = Path.Combine(Path.GetTempPath(), "X360GameHack", "profile-" + Guid.NewGuid().ToString("N") + ".tmp");
                Directory.CreateDirectory(Path.GetDirectoryName(tmp));
                try
                {
                    await store.DownloadAsync(path, tmp, null, ct);
                    return (Xbox360Profile.Open(tmp).Account?.Gamertag ?? "").Trim();
                }
                finally { try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
            catch (OperationCanceledException) { throw; }
            catch { return ""; }
        }
    }
}
