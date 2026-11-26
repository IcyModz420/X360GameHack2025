using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using X360GameHack.Core.Formats;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// One set of IDs Rehash &amp; resign can write: a profile (its profile ID, and the device and console its file was last
    /// signed on) or a drive (only the device ID the console gives what it puts there).
    /// </summary>
    public sealed class IdChoice
    {
        /// <summary>A drive, not a profile: only its device ID.</summary>
        public bool IsDrive;
        public string Gamertag = "";
        /// <summary>16 / 40 / 10 hex digits, or "" when unknown (a profile made in Xenia has no device or console).</summary>
        public string ProfileId = "", DeviceId = "", ConsoleId = "";
        /// <summary>Where it was found: "Xenia", "Saves folder", "Console Hdd", a drive image...</summary>
        public readonly List<string> FoundOn = new List<string>();
        /// <summary>A drive: how many of the files read there carry <see cref="DeviceId"/>, of how many.</summary>
        public int Votes, Of;
        /// <summary>The drive's name (drives only).</summary>
        public string DriveName = "";

        public string Label => IsDrive ? "Drive: " + DriveName : Gamertag.Length > 0 ? Gamertag : ProfileId;

        /// <summary>A copy that can be merged without changing this one (rows are shared between pages).</summary>
        public IdChoice Clone()
        {
            var c = new IdChoice { IsDrive = IsDrive, Gamertag = Gamertag, ProfileId = ProfileId, DeviceId = DeviceId, ConsoleId = ConsoleId, Votes = Votes, Of = Of, DriveName = DriveName };
            c.FoundOn.AddRange(FoundOn);
            return c;
        }
        public string Where => IsDrive ? (Of > 0 ? "device ID of " + Votes + " of " + Of + " file(s) there" : "") : string.Join(", ", FoundOn);
        internal string Key => IsDrive ? "drive|" + DriveName.ToUpperInvariant() + "|" + DeviceId : ProfileId + "|" + DeviceId + "|" + ConsoleId;
    }

    /// <summary>
    /// Finds the IDs a save can be re-signed for - the profiles on a console, an Xbox drive, in Xenia, in a folder or
    /// kept in the Saves folder - and each console drive's own device ID. Only reads headers; nothing is changed.
    /// </summary>
    public static class IdFinder
    {
        /// <summary>Enough of a package's header for its magic, type, IDs and display name.</summary>
        public const int HeadBytes = 0x1000;
        private const int DisplayNameAt = 0x411, DisplayNameBytes = 0x80;
        private const uint ProfileType = 0x00010000;

        /// <summary>What a package header says (null when it is not an Xbox 360 package).</summary>
        public sealed class Head
        {
            public string Magic = "";
            public uint ContentType;
            public string ProfileId = "", DeviceId = "", ConsoleId = "", DisplayName = "";
            public bool IsProfile => ContentType == ProfileType;
        }

        public static Head ReadHead(byte[] h)
        {
            if (h == null || h.Length < DisplayNameAt + DisplayNameBytes) return null;
            string magic = Encoding.ASCII.GetString(h, 0, 4);
            if (magic != "CON " && magic != "LIVE" && magic != "PIRS") return null;
            string name = Encoding.BigEndianUnicode.GetString(h, DisplayNameAt, DisplayNameBytes);
            int z = name.IndexOf('\0');
            if (z >= 0) name = name.Substring(0, z);
            return new Head
            {
                Magic = magic.Trim(),
                ContentType = BE.U32(h, 0x344),
                ProfileId = Hex(h, ConSignature.ProfileIdOffset, ConSignature.ProfileIdLength),
                DeviceId = Hex(h, ConSignature.DeviceIdOffset, ConSignature.DeviceIdLength),
                ConsoleId = Hex(h, ConSignature.ConsoleIdOffset, ConSignature.ConsoleIdLength),
                DisplayName = name.Trim()
            };
        }

        /// <summary>Upper-case hex of the bytes, or "" when they are all zero (not set).</summary>
        private static string Hex(byte[] h, int at, int n)
        {
            bool set = false;
            for (int i = 0; i < n; i++) if (h[at + i] != 0) { set = true; break; }
            return set ? BitConverter.ToString(h, at, n).Replace("-", "") : "";
        }

        private static IdChoice FromProfile(ProfileInfo p, string where)
        {
            string id = ContentWalk.IsHex(p.HeaderProfileId, 16) && p.HeaderProfileId.Trim('0').Length > 0 ? p.HeaderProfileId : p.ProfileId;
            var c = new IdChoice
            {
                Gamertag = p.Gamertag ?? "",
                ProfileId = (id ?? "").ToUpperInvariant(),
                DeviceId = Clean(p.DeviceId, 40),
                ConsoleId = Clean(p.ConsoleId, 10)
            };
            c.FoundOn.Add(where);
            return c;
        }

        /// <summary>The hex digits when they are a real ID of that length, else "" (missing, or all zero).</summary>
        private static string Clean(string hex, int len) => ContentWalk.IsHex(hex, len) && hex.Trim('0').Length > 0 ? hex.ToUpperInvariant() : "";

        /// <summary>
        /// The profiles in the Content folders of a console, an Xbox drive, Xenia or a PC folder; with
        /// <paramref name="drives"/>, also each drive's device ID - the one most of its saves and profiles carry.
        /// </summary>
        /// <param name="where">How the place is named in the list ("Xenia", "Console", a drive image's name).</param>
        public static async Task<List<IdChoice>> FromStoreAsync(IContentStore store, string where, bool drives, CancellationToken ct)
        {
            var result = new List<IdChoice>();
            var profiles = await ProfileScanner.ScanAsync(store, null, ct, true);
            foreach (var p in profiles)
            {
                // a console profile counts once its header was read (the IDs are there even when the copy for the totals failed)
                bool header = p.HeaderProfileId.Length > 0 && !(p.Error ?? "").StartsWith("Not a profile", StringComparison.Ordinal);
                if (!ContentWalk.IsHex(p.ProfileId, 16) || !(p.XeniaFolder || header)) continue;
                string drive = ContentWalk.DriveOf(p.Root);
                result.Add(FromProfile(p, Place(store, where, drive)));
            }
            if (!drives) return result;
            foreach (string root in await store.ContentRootsAsync(ct))
            {
                // the saves first: on a tie their device ID wins (a profile may have come from another drive)
                var seen = await SampleDeviceIdsAsync(store, root, ct);
                seen.AddRange(profiles.Where(p => p.Root == root).Select(p => Clean(p.DeviceId, 40)).Where(d => d.Length > 0));
                if (seen.Count == 0) continue;
                var best = seen.GroupBy(d => d).OrderByDescending(g => g.Count()).First();
                result.Add(new IdChoice { IsDrive = true, DriveName = Place(store, where, ContentWalk.DriveOf(root)), DeviceId = best.Key, Votes = best.Count(), Of = seen.Count });
            }
            return result;
        }

        /// <summary>"Console Hdd", "Console Usb0" - a console has several drives; a drive image or a folder is one place.</summary>
        private static string Place(IContentStore store, string where, string drive) =>
            store is XbdmContentStore && drive.Length > 0 ? where + " " + drive : where;

        /// <summary>
        /// The device IDs of a few saves on a drive (Content\{profile}\{game}\00000001): a console writes its drive's ID into
        /// every save it makes there. Console-signed (CON) packages only - LIVE / PIRS headers are Microsoft's and keep theirs.
        /// </summary>
        private static async Task<List<string>> SampleDeviceIdsAsync(IContentStore store, string root, CancellationToken ct, int want = 8, int maxLists = 60)
        {
            var ids = new List<string>();
            int lists = 0;
            foreach (var pf in (await ContentWalk.SafeList(store, root, ct)).Where(d => d.IsDirectory && ContentWalk.IsHex(d.Name, 16) && d.Name.Trim('0').Length > 0))
            {
                string pdir = store.Combine(root, pf.Name);
                if (++lists > maxLists) return ids;
                foreach (var t in (await ContentWalk.SafeList(store, pdir, ct)).Where(d => d.IsDirectory && ContentWalk.IsHex(d.Name, 8) && !d.Name.Equals("FFFE07D1", StringComparison.OrdinalIgnoreCase)))
                {
                    string sdir = store.Combine(store.Combine(pdir, t.Name), "00000001");
                    if (++lists > maxLists) return ids;
                    var f = (await ContentWalk.SafeList(store, sdir, ct)).FirstOrDefault(x => !x.IsDirectory && x.Size >= HeadBytes);
                    if (f == null) continue;
                    try
                    {
                        var h = ReadHead(await store.ReadHeadAsync(store.Combine(sdir, f.Name), HeadBytes, ct));
                        if (h != null && h.Magic == "CON" && h.DeviceId.Length > 0) ids.Add(h.DeviceId);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { }
                    if (ids.Count >= want) return ids;      // one save of each game is enough: on to the next game
                }
            }
            return ids;
        }

        /// <summary>Profile files on this PC (each read by its header only): the Saves folder's copies and backups, files opened before.</summary>
        public static List<IdChoice> FromProfileFiles(IEnumerable<string> files, string where, CancellationToken ct)
        {
            var result = new List<IdChoice>();
            foreach (string f in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(f);
                    if (!info.Exists || info.Length < HeadBytes || info.Length > (64L << 20)) continue;
                    var h = ReadHead(BE.ReadHead(f, HeadBytes));
                    if (h == null || !h.IsProfile) continue;
                    string id = h.ProfileId.Length > 0 ? h.ProfileId : ContentWalk.IsHex(info.Name, 16) ? info.Name.ToUpperInvariant() : "";
                    if (id.Length == 0) continue;
                    var c = new IdChoice
                    {
                        Gamertag = ContentWalk.IsHex(h.DisplayName, 16) ? "" : h.DisplayName,
                        ProfileId = id, DeviceId = h.DeviceId, ConsoleId = h.ConsoleId
                    };
                    c.FoundOn.Add(where);
                    result.Add(c);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return result;
        }

        /// <summary>
        /// The files under <paramref name="dir"/> named like a profile (its 16-digit profile ID) - how the Saves folder keeps
        /// the profiles copied off a console or a drive, and their backups. Bounded; never follows links.
        /// </summary>
        public static List<string> ProfileFilesUnder(string dir, CancellationToken ct, int maxDepth = 6, int maxEntries = 20000)
        {
            var found = new List<string>();
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return found;
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((dir, 0));
            int seen = 0;
            while (queue.Count > 0 && seen < maxEntries)
            {
                ct.ThrowIfCancellationRequested();
                var (d, depth) = queue.Dequeue();
                FileSystemInfo[] items;
                try { items = new DirectoryInfo(d).GetFileSystemInfos(); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (var i in items)
                {
                    if (++seen > maxEntries) break;
                    if ((i.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (i is DirectoryInfo) { if (depth < maxDepth) queue.Enqueue((i.FullName, depth + 1)); }
                    else if (ContentWalk.IsHex(i.Name, 16) && i.Name.Trim('0').Length > 0) found.Add(i.FullName);
                }
            }
            return found;
        }

        /// <summary>One row per profile (same profile, device and console) and per drive, with every place it was found.</summary>
        public static List<IdChoice> Merge(IEnumerable<IdChoice> all)
        {
            var rows = new List<IdChoice>();
            var byKey = new Dictionary<string, IdChoice>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in all)
            {
                if (c == null || (!c.IsDrive && c.ProfileId.Length == 0) || (c.IsDrive && c.DeviceId.Length == 0)) continue;
                if (!byKey.TryGetValue(c.Key, out var row)) { byKey[c.Key] = c; rows.Add(c); continue; }
                if (row.Gamertag.Length == 0) row.Gamertag = c.Gamertag;
                foreach (var w in c.FoundOn) if (!row.FoundOn.Contains(w, StringComparer.OrdinalIgnoreCase)) row.FoundOn.Add(w);
                if (c.IsDrive && c.Of > row.Of) { row.Votes = c.Votes; row.Of = c.Of; }
            }
            // a profile known only by its ID (Xenia keeps no device or console ID for one) joins a row of the same profile that
            // has them, so it is listed once
            foreach (var bare in rows.Where(r => !r.IsDrive && r.DeviceId.Length == 0 && r.ConsoleId.Length == 0).ToList())
            {
                var full = rows.FirstOrDefault(o => !o.IsDrive && !ReferenceEquals(o, bare) && o.ProfileId == bare.ProfileId && (o.DeviceId.Length > 0 || o.ConsoleId.Length > 0));
                if (full == null) continue;
                foreach (var w in bare.FoundOn) if (!full.FoundOn.Contains(w, StringComparer.OrdinalIgnoreCase)) full.FoundOn.Add(w);
                if (full.Gamertag.Length == 0) full.Gamertag = bare.Gamertag;
                rows.Remove(bare);
            }
            // a gamertag known in one place names the same profile everywhere
            foreach (var r in rows.Where(r => !r.IsDrive && r.Gamertag.Length == 0))
                r.Gamertag = rows.FirstOrDefault(o => !o.IsDrive && o.Gamertag.Length > 0 && o.ProfileId == r.ProfileId)?.Gamertag ?? "";
            return rows.OrderBy(r => r.IsDrive ? 1 : 0).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
