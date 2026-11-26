using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using X360GameHack.Core.Formats;

namespace X360GameHack.Core.Services
{
    /// <summary>One gamer profile found in a Content folder: Content\{profile ID}\FFFE07D1\00010000\{profile ID}.</summary>
    public sealed class ProfileInfo
    {
        public IContentStore Store;
        public string Path = "";          // full path inside the store
        public string FileName = "";
        public string Root = "";          // the Content folder it was found in (drive)
        public string ProfileId = "";     // the folder it is in (16 hex)
        public string HeaderProfileId = "";
        /// <summary>From the header: the storage device it was last signed for (40 hex) and the console that signed it (10 hex); "" when unknown.</summary>
        public string DeviceId = "", ConsoleId = "";
        public string Gamertag = "";
        public int? Gamerscore;           // what the gamercard shows
        public int Games, AchievementsUnlocked, AchievementsTotal;
        public bool Read;                 // the profile was opened: gamertag and totals come from inside it
        public bool Live;                 // linked to Xbox LIVE
        public string Signature = "";
        public long Size;
        public DateTime Modified;
        public Image Icon;                // the gamer picture
        public string Error;
        /// <summary>A profile in Xenia's own form: a folder of files, not a console profile file.</summary>
        public bool XeniaFolder;

        public string Drive => ContentWalk.DriveOf(Root);
        public string Label => Gamertag.Length > 0 ? Gamertag : ProfileId;
    }

    /// <summary>One title update found on a drive: Content\0000000000000000\{title ID}\000B0000\tu..., or Cache\TU_...</summary>
    public sealed class TitleUpdateInfo
    {
        public IContentStore Store;
        public string Path = "", FileName = "", Root = "";
        public string TitleId = "";       // 8 hex
        public string GameName = "", DisplayName = "";
        public uint MediaId, Version, BaseVersion;
        public bool InCache;              // a cache-format update ("TU_...") in the drive's Cache folder
        public string Signature = "";
        public long Size;
        public DateTime Modified;
        public Image Icon;
        public string Error;

        public string Drive => ContentWalk.DriveOf(Root);
        public string GameLabel => GameName.Length > 0 ? GameName : "Title " + TitleId;

        /// <summary>
        /// The title update number: a "tu0000000A_00000000" package carries it in its name (hex - 10 here). A cache-format
        /// "TU_" one does not; the version in its header is that number for most games, so that is used instead.
        /// </summary>
        public int Number
        {
            get
            {
                var m = System.Text.RegularExpressions.Regex.Match(FileName ?? "", "^tu([0-9A-Fa-f]{8})_", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) return (int)Math.Min(int.MaxValue, Convert.ToUInt32(m.Groups[1].Value, 16));
                return Version > 0 && Version < 0x10000 ? (int)Version : TitleUpdateCheck.UpdateNumber(Version);
            }
        }
    }

    /// <summary>What the finders share: reading folders that may be missing, and naming the drive a Content folder is on.</summary>
    public static class ContentWalk
    {
        /// <summary>STFS metadata plus both thumbnails.</summary>
        public const int HeadBytes = 0x971A;

        public static bool IsHex(string s, int len) => s != null && s.Length == len && s.All(Uri.IsHexDigit);

        public static string DriveOf(string root) => (root ?? "").Split(':', '\\').FirstOrDefault(s => s.Length > 0) ?? "";

        public static async Task<List<StoreItem>> SafeList(IContentStore store, string dir, CancellationToken ct)
        {
            try { return await store.ListAsync(dir, ct); }
            catch (OperationCanceledException) { throw; }
            catch { return new List<StoreItem>(); }
        }

        /// <summary>The folder a Content folder sits in - Hdd:\Content gives Hdd:\, where the Cache folder is too.</summary>
        public static string ParentOf(string contentRoot)
        {
            string p = (contentRoot ?? "").TrimEnd('\\');
            int i = p.LastIndexOf('\\');
            if (i < 0) return p.EndsWith(":") ? p + "\\" : p;
            if (i == 0) return "\\";
            string parent = p.Substring(0, i);
            return parent.EndsWith(":") ? parent + "\\" : parent;
        }
    }

    /// <summary>Finds every gamer profile in the Content folders of a console (XBDM), an Xbox drive or a PC folder.</summary>
    public static class ProfileScanner
    {
        /// <summary>A profile is read whole to show its totals; a file bigger than this is not a profile.</summary>
        private const long MaxRead = 64L << 20;

        public static async Task<List<ProfileInfo>> ScanAsync(IContentStore store, IProgress<string> progress, CancellationToken ct, bool headersOnly = false)
        {
            var result = new List<ProfileInfo>();
            foreach (string root in await store.ContentRootsAsync(ct))
            {
                foreach (var folder in (await ContentWalk.SafeList(store, root, ct)).Where(d => d.IsDirectory && ContentWalk.IsHex(d.Name, 16) && d.Name.Trim('0').Length > 0))
                {
                    string dir = store.Combine(store.Combine(store.Combine(root, folder.Name), "FFFE07D1"), "00010000");
                    var inside = await ContentWalk.SafeList(store, dir, ct);
                    // a profile Xenia made itself: a folder named after the profile ID, its Account file inside
                    foreach (var d in inside.Where(x => x.IsDirectory && x.Name.Equals(folder.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        ct.ThrowIfCancellationRequested();
                        string path = store.Combine(dir, d.Name);
                        string tag = await SaveScanner.XeniaGamertagAsync(store, path, ct);
                        if (tag == null) continue;
                        var xp = new ProfileInfo
                        {
                            Store = store, Root = root, Path = path, FileName = d.Name, XeniaFolder = true, Signature = "Xenia",
                            ProfileId = folder.Name.ToUpperInvariant(), Gamertag = tag, Modified = d.Modified,
                            Size = await SaveScanner.FolderSizeAsync(store, path, 0, ct),
                            Error = tag.Length == 0 ? "Made in Xenia; its Account file could not be read" : null
                        };
                        // a profile installed into Xenia has a header file beside it (Headers\00010000\{name}.header): in the form
                        // the bundled Xenia writes it holds the profile ID only; a newer Xenia's form is the whole package header
                        try
                        {
                            byte[] bytes = await store.ReadHeadAsync(XeniaContent.HeaderOf(path), ContentWalk.HeadBytes, ct);
                            var info = XeniaContent.ReadHeader(bytes);
                            if (info != null && info.Aggregate) xp.HeaderProfileId = info.Xuid.ToString("X16");
                            else if (info != null)
                            {
                                var head = StfsPackage.FromHeader(bytes, path);
                                if (head.Valid) { xp.HeaderProfileId = head.ProfileId; xp.DeviceId = head.DeviceId; xp.ConsoleId = head.ConsoleId; }
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception) { }                     // made in Xenia itself: no header, no device or console
                        result.Add(xp);
                    }
                    foreach (var f in inside.Where(x => !x.IsDirectory))
                    {
                        ct.ThrowIfCancellationRequested();
                        var p = new ProfileInfo
                        {
                            Store = store, Root = root, Path = store.Combine(dir, f.Name), FileName = f.Name,
                            ProfileId = folder.Name.ToUpperInvariant(), Size = f.Size, Modified = f.Modified
                        };
                        progress?.Report("Reading profile " + p.Path);
                        await ReadAsync(store, p, ct, headersOnly);
                        result.Add(p);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// The header (gamer picture), then a temporary copy for the gamertag and the totals. Nothing on the source changes.
        /// <paramref name="headersOnly"/>: the header is enough (the IDs and the gamertag it shows) - no copy unless it shows no gamertag.
        /// </summary>
        public static async Task ReadAsync(IContentStore store, ProfileInfo p, CancellationToken ct, bool headersOnly = false)
        {
            try
            {
                var pkg = StfsPackage.FromHeader(await store.ReadHeadAsync(p.Path, ContentWalk.HeadBytes, ct), p.Path);
                if (!pkg.Valid) { p.Error = "Not a profile package"; return; }
                p.Signature = pkg.Signature;
                p.HeaderProfileId = pkg.ProfileId;
                p.DeviceId = pkg.DeviceId ?? "";
                p.ConsoleId = pkg.ConsoleId ?? "";
                p.Icon = pkg.Thumbnail ?? pkg.TitleThumbnail;
                if (!ReferenceEquals(p.Icon, pkg.TitleThumbnail)) pkg.TitleThumbnail?.Dispose();
                if (pkg.ContentType != 0x00010000) { p.Error = "Not a profile (" + pkg.ContentTypeName + ")"; return; }
                string shown = pkg.DisplayName.Trim();
                if (shown.Length > 0 && !ContentWalk.IsHex(shown, 16)) p.Gamertag = shown;
                if (p.Size <= 0 || p.Size > MaxRead || (headersOnly && p.Gamertag.Length > 0)) return;

                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "X360GameHack", "profile-" + Guid.NewGuid().ToString("N") + ".tmp");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(tmp));
                try
                {
                    await store.DownloadAsync(p.Path, tmp, null, ct);
                    var prof = Xbox360Profile.Open(tmp);
                    string tag = (prof.Account?.Gamertag ?? "").Trim();
                    if (tag.Length > 0) p.Gamertag = tag;
                    p.Gamerscore = prof.GamercardGamerscore ?? prof.TotalGamerscore;
                    p.Games = prof.Games.Count;
                    p.AchievementsUnlocked = prof.AchievementsUnlocked;
                    p.AchievementsTotal = prof.AchievementsTotal;
                    p.Live = prof.IsLiveLinked;
                    p.Read = true;
                }
                finally { try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { p.Error = ex.Message; }
        }
    }

    /// <summary>
    /// Finds every title update on a console (XBDM), an Xbox drive or a PC folder: lower-case "tu..." packages in
    /// Content\0000000000000000\{title ID}\000B0000 and cache-format "TU_..." ones in the drive's Cache folder.
    /// </summary>
    public static class TitleUpdateScanner
    {
        public const string Everyone = "0000000000000000", UpdatesFolder = "000B0000", CacheFolder = "Cache";

        public static async Task<List<TitleUpdateInfo>> ScanAsync(IContentStore store, IProgress<string> progress, CancellationToken ct)
        {
            var result = new List<TitleUpdateInfo>();
            foreach (string root in await store.ContentRootsAsync(ct))
            {
                string shared = store.Combine(root, Everyone);
                foreach (var title in (await ContentWalk.SafeList(store, shared, ct)).Where(d => d.IsDirectory && ContentWalk.IsHex(d.Name, 8)))
                {
                    string dir = store.Combine(store.Combine(shared, title.Name), UpdatesFolder);
                    foreach (var f in (await ContentWalk.SafeList(store, dir, ct)).Where(x => !x.IsDirectory))
                        result.Add(await ReadAsync(store, root, store.Combine(dir, f.Name), f, title.Name.ToUpperInvariant(), false, progress, ct));
                }
                string cache = store.Combine(ContentWalk.ParentOf(root), CacheFolder);
                foreach (var f in (await ContentWalk.SafeList(store, cache, ct)).Where(x => !x.IsDirectory && x.Name.StartsWith("TU_", StringComparison.Ordinal)))
                    result.Add(await ReadAsync(store, root, store.Combine(cache, f.Name), f, "", true, progress, ct));
            }
            // updates without a game name borrow it from another update of the same game
            foreach (var g in result.GroupBy(r => r.TitleId))
            {
                string name = g.Select(x => x.GameName).FirstOrDefault(n => n.Length > 0) ?? "";
                foreach (var u in g) if (u.GameName.Length == 0) u.GameName = name;
            }
            return result;
        }

        private static async Task<TitleUpdateInfo> ReadAsync(IContentStore store, string root, string path, StoreItem f, string folderTitleId, bool cache,
                                                             IProgress<string> progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var u = new TitleUpdateInfo { Store = store, Root = root, Path = path, FileName = f.Name, TitleId = folderTitleId, InCache = cache, Size = f.Size, Modified = f.Modified };
            progress?.Report("Reading " + path);
            try
            {
                var pkg = StfsPackage.FromHeader(await store.ReadHeadAsync(path, ContentWalk.HeadBytes, ct), path);
                if (!pkg.Valid) { u.Error = "Not a title update package"; return u; }
                u.Signature = pkg.Signature;
                u.GameName = pkg.TitleName.Trim();
                u.DisplayName = pkg.DisplayName.Trim();
                if (u.GameName.Length == 0)
                {
                    // some updates only name the game in their display name: "Call of Duty: World at War Title Update #7"
                    int cut = u.DisplayName.IndexOf(" Title Update", StringComparison.OrdinalIgnoreCase);
                    if (cut > 0) u.GameName = u.DisplayName.Substring(0, cut).Trim();
                }
                u.MediaId = pkg.MediaId; u.Version = pkg.Version; u.BaseVersion = pkg.BaseVersion;
                u.Icon = pkg.TitleThumbnail ?? pkg.Thumbnail;
                if (!ReferenceEquals(u.Icon, pkg.Thumbnail)) pkg.Thumbnail?.Dispose();
                if (u.TitleId.Length == 0) u.TitleId = pkg.TitleIdHex;
                if (pkg.ContentType != 0x000B0000) u.Error = "Not a title update (" + pkg.ContentTypeName + ")";
                else if (!pkg.TitleIdHex.Equals(u.TitleId, StringComparison.OrdinalIgnoreCase))
                    u.Error = "Made for title " + pkg.TitleIdHex + " but it is in the folder of " + u.TitleId + " - the console never looks for it there";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { u.Error = ex.Message; }
            return u;
        }

        /// <summary>
        /// Where a title update goes on the drive whose Content folder is <paramref name="contentRoot"/>: a "TU_" update
        /// in that drive's Cache folder, any other in Content\0000000000000000\{title ID}\000B0000. The name is kept.
        /// </summary>
        public static string Destination(IContentStore store, string contentRoot, StfsPackage pkg, string fileName)
            => fileName.StartsWith("TU_", StringComparison.Ordinal)
                ? store.Combine(store.Combine(ContentWalk.ParentOf(contentRoot), CacheFolder), fileName)
                : store.Combine(store.Combine(store.Combine(store.Combine(contentRoot, Everyone), pkg.TitleIdHex), UpdatesFolder), fileName);
    }
}
