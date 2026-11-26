using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using X360GameHack.Core.Formats;

namespace X360GameHack.Core.Services
{
    /// <summary>A title update or downloadable content (DLC) package for one game, found on a console or an Xbox drive.</summary>
    public sealed class GameExtra
    {
        public IContentStore Store;
        public string Path = "", FileName = "", DisplayName = "", Where = "";
        public uint TitleId, ContentType, Version;
        public long Size;
        public bool IsUpdate => ContentType == XeniaContent.UpdateType;
        public string Label => DisplayName.Length > 0 ? DisplayName : FileName;
    }

    /// <summary>
    /// Finds a game's title updates and DLC where a console keeps them - Content\0000000000000000\{title ID}\000B0000 and
    /// \00000002 on every drive, and the "TU_" updates in each drive's Cache folder - and picks what Xenia should get: every
    /// DLC, and the NEWEST title update only (Xenia applies the first title update it finds for a game, so it must have just
    /// one - see <see cref="XeniaContent.SetAsideOtherUpdates"/>). Only reads.
    /// </summary>
    public static class GameExtras
    {
        private const int HeadBytes = 0x1712;

        public static async Task<List<GameExtra>> FindAsync(IContentStore store, uint titleId, string where, CancellationToken ct)
        {
            var found = new List<GameExtra>();
            string title = titleId.ToString("X8");
            foreach (string root in await store.ContentRootsAsync(ct))
            {
                string drive = ContentWalk.DriveOf(root);
                string place = store is XbdmContentStore && drive.Length > 0 ? where + " " + drive : where;
                string gameDir = store.Combine(store.Combine(root, XeniaContent.Everyone), title);
                foreach (string type in new[] { "000B0000", "00000002" })
                {
                    string dir = store.Combine(gameDir, type);
                    foreach (var f in (await ContentWalk.SafeList(store, dir, ct)).Where(x => !x.IsDirectory && x.Size >= HeadBytes))
                        await ConsiderAsync(store, store.Combine(dir, f.Name), f, titleId, place, found, ct);
                }
                string cache = store.Combine(ContentWalk.ParentOf(root), TitleUpdateScanner.CacheFolder);
                foreach (var f in (await ContentWalk.SafeList(store, cache, ct)).Where(x => !x.IsDirectory && x.Name.StartsWith("TU_", StringComparison.Ordinal) && x.Size >= HeadBytes))
                    await ConsiderAsync(store, store.Combine(cache, f.Name), f, titleId, place, found, ct);
            }
            return found;
        }

        private static async Task ConsiderAsync(IContentStore store, string path, StoreItem f, uint titleId, string where, List<GameExtra> found, CancellationToken ct)
        {
            try
            {
                var pkg = StfsPackage.FromHeader(await store.ReadHeadAsync(path, HeadBytes, ct), path);
                // the dashboard keeps files of its own in those folders (a "Title Content Progress cache"): only the game's own
                if (!pkg.Valid || pkg.TitleId != titleId || (pkg.ContentType != XeniaContent.UpdateType && pkg.ContentType != XeniaContent.DlcType)) return;
                found.Add(new GameExtra
                {
                    Store = store, Path = path, FileName = f.Name, DisplayName = pkg.DisplayName.Trim(), Where = where,
                    TitleId = titleId, ContentType = pkg.ContentType, Version = pkg.Version, Size = f.Size
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { }                     // unreadable: not one of the game's
        }

        /// <summary>What Xenia should get from everything found: each DLC once (by file name), and the newest title update.</summary>
        public static List<GameExtra> Pick(IEnumerable<GameExtra> all)
        {
            var list = all.ToList();
            var picked = list.Where(x => !x.IsUpdate).GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            var newest = list.Where(x => x.IsUpdate).OrderByDescending(x => x.Version).ThenBy(x => x.FileName.StartsWith("TU_", StringComparison.Ordinal) ? 1 : 0).FirstOrDefault();
            if (newest != null) picked.Insert(0, newest);
            return picked;
        }
    }
}
