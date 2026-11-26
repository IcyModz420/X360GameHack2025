using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>
    /// One click: saves and profiles from a console, an Xbox drive or a PC folder into Xenia, where they go exactly as
    /// on a console (see <see cref="XeniaContent"/>). A save brings its profile along when Xenia does not have it yet,
    /// and Xenia signs in to a copied profile when it starts, unless it already signs in to another. Whatever is
    /// replaced in Xenia is backed up to the Saves folder first. Nothing on the source changes.
    /// </summary>
    public static class XeniaTransfer
    {
        /// <summary>True when Xenia can be copied into now; otherwise says why not (before any file is picked).</summary>
        public static bool CheckReady(string what) => Target(what) != null;

        /// <summary>The Xenia to copy into, or null after saying why not now.</summary>
        private static string Target(string what)
        {
            string xenia = Tools.Xenia;
            if (string.IsNullOrWhiteSpace(xenia) || !File.Exists(xenia)) { Dlg.Info(what, "Xenia was not found. Set its path on the Xenia page (Setup tab)."); return null; }
            if (XeniaProcess.Running().Count > 0)
            {
                Dlg.Info(what, "Xenia is running. Close it first: it reads its profiles as it starts, and keeps open the saves a game is using.");
                return null;
            }
            return xenia;
        }

        /// <summary>
        /// One click for a game on the Xenia page: its newest title update and its DLC, from the Xbox drive open in Drive
        /// Explorer and from the connected console, go into Xenia - each one Xenia does not have yet (<see cref="GameExtras"/>).
        /// The drive and the console are only read; nothing is copied when Xenia has everything already.
        /// </summary>
        public static async Task<bool> AddExtrasFromXboxAsync(XeniaGame game)
        {
            const string what = "DLC and updates from my Xbox";
            if (game == null || game.TitleId == 0) { Dlg.Info(what, "Select a game first (one whose title ID is known)."); return false; }
            string xenia = Target(what);
            if (xenia == null) return false;
            var places = new List<(IContentStore Store, string Where)>();
            var de = UI.Pages.DriveExplorerPage.Instance;
            if (de != null && !de.IsDisposed && de.OpenDriveName.Length > 0)
                foreach (var v in de.OpenVolumes) places.Add((new FatxContentStore(v), de.OpenDriveName));
            if (ConsoleService.IsConnected) places.Add((new XbdmContentStore(), "Console"));
            if (places.Count == 0)
            {
                Dlg.Info(what, "Open your Xbox drive in Drive Explorer (Open physical drive... or Open drive image...), or connect your console, then press this again.\n\n" +
                               "It looks there for " + game.Name + "'s newest title update and its DLC.");
                return false;
            }
            var found = new List<GameExtra>();
            var cts = new CancellationTokenSource();
            List<GameExtra> picked, wanted;
            var have = new List<string>();
            using (Status.Begin("Looking for " + game.Name + "'s DLC and title updates", cts))
            {
                try
                {
                    foreach (var (store, where) in places) found.AddRange(await GameExtras.FindAsync(store, game.TitleId, where, cts.Token));
                }
                catch (OperationCanceledException) { Log.Warn("Looking for DLC and updates cancelled."); cts.Dispose(); return false; }
                catch (Exception ex) { Log.Error("Could not look for DLC and updates: " + ex.Message); Dlg.Error(what, ex.Message); cts.Dispose(); return false; }
                picked = GameExtras.Pick(found);
                wanted = new List<GameExtra>();
                foreach (var x in picked)
                {
                    string target = XeniaContent.PathFor(xenia, XeniaContent.Everyone, game.TitleIdHex, x.ContentType, x.FileName);
                    if (Directory.Exists(target) && File.Exists(XeniaContent.HeaderOf(target))) have.Add(x.Label);
                    else wanted.Add(x);
                }
            }
            string looked = string.Join(" and ", places.Select(p => p.Where).Distinct());
            if (picked.Count == 0)
            {
                cts.Dispose();
                Dlg.Info(what, "There is no title update or DLC for " + game.Name + " (" + game.TitleIdHex + ") on " + looked + ".");
                return false;
            }
            if (wanted.Count == 0)
            {
                cts.Dispose();
                Dlg.Info(what, "Xenia has them already: " + string.Join(", ", have) + ".");
                return false;
            }
            long need = wanted.Sum(x => x.Size);
            string temp = Path.Combine(Path.GetTempPath(), "X360GameHack", "to-xenia-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(temp))).AvailableFreeSpace;
            if (free < need * 2 + (512L << 20))
            {
                cts.Dispose();
                Dlg.Warn(what, "They are " + FileSniffer.FormatSize(need) + "; that needs about " + FileSniffer.FormatSize(need * 2) + " free on this PC for a moment, and " + FileSniffer.FormatSize(free) + " is free.");
                return false;
            }
            bool read = false;
            try
            {
                using (Status.Begin("Copying " + wanted.Count + " item(s) for " + game.Name + " off " + looked, cts))
                {
                    var progress = Status.NewProgress();
                    Directory.CreateDirectory(temp);
                    foreach (var x in wanted)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        await x.Store.DownloadAsync(x.Path, Path.Combine(temp, PathSafety.SafeName(x.FileName)), progress, cts.Token);
                    }
                    read = true;
                }
                if (read)
                {
                    Log.Info("Found for " + game.Name + " on " + looked + ": " + string.Join(", ", wanted.Select(x => x.Label)) +
                             (have.Count > 0 ? " (Xenia has " + string.Join(", ", have) + " already)" : "") + ".");
                    return await AddAsync(new[] { temp }, p => p.TitleId == game.TitleId && (p.ContentType == XeniaContent.DlcType || p.ContentType == XeniaContent.UpdateType), what,
                                          "No title update or DLC for " + game.Name + " was in what was copied.");
                }
                return false;
            }
            catch (OperationCanceledException) { Log.Warn(what + " cancelled - nothing went into Xenia."); return false; }
            catch (Exception ex) { Log.Error(what + " failed: " + ex.Message); Dlg.Error(what, ex.Message); return false; }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                cts.Dispose();
            }
        }

        /// <summary>
        /// For each save among <paramref name="found"/>, the profile it belongs to when that sits where a console keeps it, next
        /// to the save: {profile ID}\FFFE07D1\00010000\{profile ID} beside {profile ID}\{title}\00000001\{save} - each profile
        /// once, and only ones not found already.
        /// </summary>
        private static List<StfsPackage> ProfilesBeside(List<StfsPackage> found)
        {
            var more = new List<StfsPackage>();
            foreach (var s in found.Where(p => p.ContentType == XeniaContent.SaveType && ContentWalk.IsHex(p.ProfileId, 16) && p.ProfileId.Trim('0').Length > 0))
            {
                try
                {
                    string typeDir = Path.GetDirectoryName(Path.GetFullPath(s.Path)), titleDir = Path.GetDirectoryName(typeDir), profileDir = Path.GetDirectoryName(titleDir);
                    if (!string.Equals(Path.GetFileName(profileDir), s.ProfileId, StringComparison.OrdinalIgnoreCase)) continue;
                    string candidate = Path.Combine(profileDir, "FFFE07D1", "00010000", Path.GetFileName(profileDir));
                    if (!File.Exists(candidate) || found.Concat(more).Any(p => p.ContentType == XeniaContent.ProfileType && string.Equals(p.ProfileId, s.ProfileId, StringComparison.OrdinalIgnoreCase))) continue;
                    var pkg = StfsPackage.Read(candidate);
                    if (pkg.Valid && pkg.ContentType == XeniaContent.ProfileType && string.Equals(pkg.ProfileId, s.ProfileId, StringComparison.OrdinalIgnoreCase)) more.Add(pkg);
                }
                catch (Exception) { }                     // not laid out that way, or unreadable: no profile beside it
            }
            return more;
        }

        private static string TempFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "X360GameHack", "to-xenia");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");
        }

        private static void Forget(string file) { try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        private static string Was(string backup) => backup == "" ? " (it was already there)" : backup != null ? " (the one there before is kept in " + backup + ")" : "";

        private static string GamertagOf(string profileFile)
        {
            try { return (Xbox360Profile.Open(profileFile).Account?.Gamertag ?? "").Trim(); } catch (Exception) { return ""; }
        }

        /// <summary>
        /// The note for files that went to Xenia with a block that does not match its checksum (null when there are none).
        /// Xenia never checks those, so the copy goes ahead rather than refusing everything over one block.
        /// </summary>
        private static string DamagedNote(string what, List<string> damaged)
        {
            if (damaged.Count == 0) return null;
            return what + ": " + string.Join(", ", damaged) + (damaged.Count == 1 ? " holds" : " hold") + " a block that does not match its checksum. " +
                   "It was copied as it is - Xenia does not check them. Most likely the console was writing to it while it was copied " +
                   "(a signed-in profile, or a game saving); for a clean copy, sign out of that profile or quit the game, then copy it again.";
        }

        /// <summary>Copies a profile package into Xenia and lets Xenia sign in to it. Returns the line for the log, and a warning or null.</summary>
        private static (string Line, string Warning) PutProfile(string xenia, string local, string profileId)
        {
            var pkg = StfsPackage.Read(local);
            if (!pkg.Valid || pkg.ContentType != XeniaContent.ProfileType) throw new InvalidOperationException(profileId + " is not a profile package.");
            string tag = GamertagOf(local);
            string label = tag.Length > 0 ? tag + " (" + profileId + ")" : profileId;
            var damaged = new List<string>();
            string backup = XeniaContent.Put(local, XeniaContent.PathFor(xenia, profileId, XeniaContent.Dashboard, XeniaContent.ProfileType, profileId), damaged);
            bool signIn = XeniaContent.SignInIfNone(xenia, profileId);
            return ("Profile " + label + " copied to Xenia" + Was(backup) + "." + (signIn ? " Xenia signs in to it when it starts." : ""), DamagedNote("Profile " + label, damaged));
        }

        /// <summary>Logs a copy, and its warning (also kept for the closing message) when there is one.</summary>
        private static void Report((string Line, string Warning) r, List<string> notes)
        {
            Log.Success(r.Line);
            if (r.Warning == null) return;
            Log.Warn(r.Warning);
            notes.Add(r.Warning);
        }

        /// <summary>Where the profile is on the same console, drive or folder (any of its drives), or null.</summary>
        private static async Task<string> FindProfileAsync(IContentStore store, string profileId, CancellationToken ct)
        {
            foreach (string root in await store.ContentRootsAsync(ct))
            {
                string path = store.Combine(store.Combine(store.Combine(store.Combine(root, profileId), XeniaContent.Dashboard), "00010000"), profileId);
                try
                {
                    var pkg = StfsPackage.FromHeader(await store.ReadHeadAsync(path, 0x1712, ct), path);
                    if (pkg.Valid && pkg.ContentType == XeniaContent.ProfileType) return path;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { }            // not on this drive
            }
            return null;
        }

        /// <summary>Copies saves into Xenia, each with its profile when Xenia does not have that profile yet.</summary>
        public static async Task<bool> CopySavesAsync(IList<SaveInfo> saves)
        {
            if (saves.Count == 0) { Dlg.Info("Copy to Xenia", "Select one or more saves."); return false; }
            string xenia = Target("Copy to Xenia");
            if (xenia == null) return false;
            var notes = new List<string>();
            int copied = 0;
            var cts = new CancellationTokenSource();
            using (Status.Begin("Copying " + saves.Count + " save(s) to Xenia", cts))
            {
                try
                {
                    var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var s in saves)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        string name = s.GameLabel + " - " + (s.SaveName.Length > 0 ? s.SaveName : s.FileName);
                        if (s.XeniaFolder) { notes.Add(name + " is already kept the way Xenia keeps saves."); continue; }
                        string target = XeniaContent.PathFor(xenia, s.ProfileId, s.TitleId, XeniaContent.SaveType, s.FileName);
                        string tmp = TempFile();
                        try
                        {
                            await s.Store.DownloadAsync(s.Path, tmp, Status.NewProgress(), cts.Token);
                            var damaged = new List<string>();
                            string backup = await Task.Run(() => XeniaContent.Put(tmp, target, damaged));
                            Report(("Save " + name + " copied to Xenia" + Was(backup) + ": " + target, DamagedNote("Save " + name, damaged)), notes);
                            copied++;
                        }
                        finally { Forget(tmp); }

                        // its profile too: the game only sees the saves of the profile Xenia signs in to
                        if (s.ProfileId.Trim('0').Length == 0 || !profiles.Add(s.ProfileId) || XeniaContent.HasProfile(xenia, s.ProfileId)) continue;
                        string remote = await FindProfileAsync(s.Store, s.ProfileId, cts.Token);
                        if (remote == null)
                        {
                            notes.Add("The profile " + s.ProfileLabel + " is not on " + s.Store.Title + ". Copy it to Xenia as well (Profiles > Copy to Xenia): " +
                                      "each save belongs to a profile, and a game in Xenia only sees the saves of the profile Xenia is signed in to.");
                            continue;
                        }
                        string ptmp = TempFile();
                        try
                        {
                            await s.Store.DownloadAsync(remote, ptmp, Status.NewProgress(), cts.Token);
                            Report(await Task.Run(() => PutProfile(xenia, ptmp, s.ProfileId)), notes);
                        }
                        finally { Forget(ptmp); }
                    }
                }
                catch (OperationCanceledException) { Log.Warn("Copy to Xenia cancelled."); }
                catch (Exception ex) { Log.Error("Copy to Xenia failed: " + ex.Message); Dlg.Error("Copy to Xenia", ex.Message); return copied > 0; }
                finally { cts.Dispose(); }
            }
            if (notes.Count > 0) Dlg.Info("Copy to Xenia", (copied > 0 ? copied + " save(s) copied to Xenia.\n\n" : "") + string.Join("\n\n", notes));
            return copied > 0;
        }

        /// <summary>Copies profiles into Xenia; Xenia signs in to the first one when it starts, unless it already signs in to a profile.</summary>
        public static async Task<bool> CopyProfilesAsync(IList<ProfileInfo> profiles)
        {
            if (profiles.Count == 0) { Dlg.Info("Copy to Xenia", "Select one or more profiles."); return false; }
            string xenia = Target("Copy to Xenia");
            if (xenia == null) return false;
            var notes = new List<string>();
            int copied = 0;
            var cts = new CancellationTokenSource();
            using (Status.Begin("Copying " + profiles.Count + " profile(s) to Xenia", cts))
            {
                try
                {
                    foreach (var p in profiles.Where(x => !x.XeniaFolder))
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        string tmp = TempFile();
                        try
                        {
                            await p.Store.DownloadAsync(p.Path, tmp, Status.NewProgress(), cts.Token);
                            Report(await Task.Run(() => PutProfile(xenia, tmp, p.ProfileId)), notes);
                            copied++;
                        }
                        finally { Forget(tmp); }
                    }
                }
                catch (OperationCanceledException) { Log.Warn("Copy to Xenia cancelled."); }
                catch (Exception ex) { Log.Error("Copy to Xenia failed: " + ex.Message); Dlg.Error("Copy to Xenia", ex.Message); return copied > 0; }
                finally { cts.Dispose(); }
            }
            if (notes.Count > 0) Dlg.Info("Copy to Xenia", copied + " profile(s) copied to Xenia.\n\n" + string.Join("\n\n", notes));
            return copied > 0;
        }

        /// <summary>
        /// Package files on this PC - or every package in a folder or a .zip / .rar / .7z - into Xenia, each where a console
        /// keeps it. <paramref name="only"/> keeps just the packages it accepts (null = all); <paramref name="none"/> is what
        /// to say when nothing fits.
        /// </summary>
        public static async Task<bool> AddAsync(IEnumerable<string> paths, Func<StfsPackage, bool> only = null, string what = "Add to Xenia",
                                                string none = "No Xbox 360 saves, profiles, downloadable content or title updates were found there.")
        {
            string xenia = Target(what);
            if (xenia == null) return false;
            var notes = new List<string>();
            int copied = 0;
            var list = paths.ToList();
            var temps = new List<string>();
            var cts = new CancellationTokenSource();
            using (Status.Begin("Copying to Xenia", cts))
            {
                try
                {
                    var found = await Task.Run(() => ContentInstaller.Collect(Unpack(list, temps, notes, cts.Token)), cts.Token);
                    var packages = only == null ? found : found.Where(only).ToList();
                    if (packages.Count == 0) notes.Insert(0, none);
                    else if (packages.Count < found.Count) notes.Add((found.Count - packages.Count) + " other package(s) there were left out.");
                    foreach (var pkg in packages)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        string target;
                        try { target = XeniaContent.PathFor(xenia, pkg); }
                        catch (InvalidOperationException ex) { notes.Add(ex.Message); continue; }
                        if (pkg.ContentType == XeniaContent.ProfileType) Report(await Task.Run(() => PutProfile(xenia, pkg.Path, pkg.ProfileId)), notes);
                        else
                        {
                            var damaged = new List<string>();
                            string backup = await Task.Run(() => XeniaContent.Put(pkg.Path, target, damaged));
                            Report((ContentInstaller.Label(pkg) + " copied to Xenia" + Was(backup) + ": " + target, DamagedNote(ContentInstaller.Label(pkg), damaged)), notes);
                            if (pkg.ContentType == XeniaContent.UpdateType)
                            {
                                // Xenia uses one title update per game - the first it finds - so this one is now the only one
                                var aside = await Task.Run(() => XeniaContent.SetAsideOtherUpdates(target));
                                if (aside.Count > 0)
                                    notes.Add("Xenia uses one title update per game, so the other one" + (aside.Count == 1 ? "" : "s") + " for this game (" + string.Join(", ", aside) +
                                              ") went to your Saves folder's Backups.");
                            }
                        }
                        copied++;
                    }
                }
                catch (OperationCanceledException) { Log.Warn("Copy to Xenia cancelled."); }
                catch (Exception ex) { Log.Error("Copy to Xenia failed: " + ex.Message); Dlg.Error(what, ex.Message); return copied > 0; }
                finally { RemoveTemps(temps); cts.Dispose(); }
            }
            if (notes.Count > 0)
                Dlg.Info(what, (copied > 0 ? copied + " item(s) copied to Xenia.\n\n" : "") + string.Join("\n\n", notes.Take(8)) + (notes.Count > 8 ? "\n\n(and " + (notes.Count - 8) + " more)" : ""));
            return copied > 0;
        }

        // ------------------------------------------------------------ saves given to one of Xenia's profiles

        /// <summary>"Gamertag (ID)" for one of Xenia's profiles.</summary>
        public static string LabelOf(string xenia, string profileId)
        {
            try { return XeniaContent.Profiles(xenia).FirstOrDefault(p => p.Id == profileId)?.Label ?? profileId; }
            catch (Exception) { return profileId; }
        }

        /// <summary>
        /// The Xenia profile new saves go to: the one Xenia signs in to; else its only profile; else the one picked, or a new
        /// one made the way Xenia's own Create Profile makes it. Xenia then signs in to it when a game starts, so the game
        /// sees the save. Null = cancelled.
        /// </summary>
        public static string PickProfile(string xenia, string what)
        {
            var profiles = XeniaContent.Profiles(xenia);
            string current = XeniaContent.SignedIn(xenia);
            if (current.Length > 0 && profiles.Any(p => p.Id == current)) return current;
            string id;
            if (profiles.Count == 1) id = profiles[0].Id;
            else
            {
                int pick = profiles.Count;                       // = make a new profile
                if (profiles.Count > 1)
                {
                    var items = profiles.Select(p => p.Label).ToList();
                    items.Add("Make a new profile...");
                    pick = Dlg.Choose(what, "Which of Xenia's profiles gets it? Xenia signs in to that profile when a game starts, so the game finds it.", items, "Use this profile");
                    if (pick < 0) return null;
                }
                if (pick < profiles.Count) id = profiles[pick].Id;
                else
                {
                    string tag = Dlg.Input(what, (profiles.Count == 0 ? "Xenia has no profile yet. " : "") +
                                                 "Gamertag for the new profile (a letter first, then letters and digits, at most 15):");
                    if (string.IsNullOrWhiteSpace(tag)) return null;
                    tag = tag.Trim();
                    string problem = XeniaContent.GamertagProblem(tag);
                    if (problem != null) { Dlg.Info(what, problem); return null; }
                    id = XeniaContent.CreateProfile(xenia, tag);
                    Log.Success("Made the profile " + tag + " (" + id + ") in Xenia, the way Xenia's own Create Profile makes it.");
                }
            }
            XeniaContent.SignIn(xenia, id);
            Log.Info("Xenia signs in to " + LabelOf(xenia, id) + " when a game starts.");
            return id;
        }

        /// <summary>
        /// Saves on this PC - console save files, folders of them, or a .zip (a .rar / .7z with 7-Zip installed) holding
        /// them - into Xenia for one of its profiles, whichever profile made them (<see cref="XeniaContent.PutSaveFor"/>):
        /// downloaded and modded saves included, with no resigning. <paramref name="titleId"/> keeps only that game's saves
        /// (0 = every game's).
        /// </summary>
        public static async Task<bool> InstallSavesAsync(IEnumerable<string> paths, uint titleId = 0, string gameName = null)
        {
            const string what = "Install a save";
            string xenia = Target(what);
            if (xenia == null) return false;
            string profile = null, owner = null, ownFor = null;
            var notes = new List<string>();
            int copied = 0;
            var list = paths.ToList();
            var temps = new List<string>();
            var cts = new CancellationTokenSource();
            using (Status.Begin("Installing saves into Xenia", cts))
            {
                try
                {
                    var found = await Task.Run(() => ContentInstaller.Collect(Unpack(list, temps, notes, cts.Token)), cts.Token);
                    // a save picked from a download laid out as on a console's drive ({profile}\{title}\00000001\{save}) may have its
                    // profile beside it ({profile}\FFFE07D1\00010000\{profile}): taken along, so it can stay with that profile
                    found.AddRange(await Task.Run(() => ProfilesBeside(found), cts.Token));
                    var saves = found.Where(p => p.ContentType == XeniaContent.SaveType).ToList();
                    if (saves.Count == 0)
                        notes.Insert(0, "No Xbox 360 saves were found there. An Xbox 360 save is a package file (it starts with CON); a PlayStation or PC save does not work in Xenia.");
                    // saves downloaded together with the profile they belong to (a mod menu's save and its profile, say): they can
                    // stay with that profile - it goes into Xenia too and Xenia signs in to it - or go to one of the user's profiles
                    var bundled = found.Where(p => p.ContentType == XeniaContent.ProfileType && saves.Any(s => string.Equals(s.ProfileId, p.ProfileId, StringComparison.OrdinalIgnoreCase)))
                                       .GroupBy(p => p.ProfileId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                    bool keepOwn = false;
                    if (saves.Count > 0 && bundled.Count > 0)
                    {
                        string names = string.Join(", ", bundled.Select(b => { string g = GamertagOf(b.Path); return (g.Length > 0 ? g + " " : "") + "(" + b.ProfileId + ")"; }));
                        int pick = Dlg.Choose(what, "These saves came with the profile they belong to: " + names + ". Some saves - a mod menu's, for one - only work with their own profile.",
                                              new[] { "Keep them with that profile - add it to Xenia and sign in to it", "Give them to one of my Xenia profiles" }, "Install");
                        if (pick < 0) return false;
                        keepOwn = pick == 0;
                    }
                    if (keepOwn)
                        foreach (var b in bundled)
                        {
                            cts.Token.ThrowIfCancellationRequested();
                            Report(await Task.Run(() => PutProfile(xenia, b.Path, b.ProfileId)), notes);
                        }
                    int others = found.Count - saves.Count - (keepOwn ? bundled.Count : 0);
                    if (others > 0)
                        notes.Add(others + " other package(s) there (profiles, DLC or updates) were left out - Profiles > Add a profile... and Games > Add DLC / updates... take those.");
                    foreach (var s in saves)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        string label = SaveLabel(s);
                        if (titleId != 0 && s.TitleId != titleId)
                        {
                            notes.Add(label + " is a save for " + (s.TitleName.Length > 0 ? s.TitleName : "another game") + " (" + s.TitleIdHex + "), not " + (gameName ?? titleId.ToString("X8")) + " - left out.");
                            continue;
                        }
                        // its own profile when it came with it (and that was chosen), else one of the user's - asked once, when needed
                        string to = keepOwn && bundled.Any(b => string.Equals(b.ProfileId, s.ProfileId, StringComparison.OrdinalIgnoreCase)) ? s.ProfileId.ToUpperInvariant() : null;
                        if (to == null)
                        {
                            if (profile == null)
                            {
                                profile = PickProfile(xenia, what);
                                if (profile == null) break;
                                owner = LabelOf(xenia, profile);
                            }
                            to = profile;
                        }
                        else ownFor = ownFor ?? to;
                        var damaged = new List<string>();
                        (string Target, string Backup) r;
                        string target = to;
                        try { r = await Task.Run(() => XeniaContent.PutSaveFor(xenia, s.Path, target, damaged)); }
                        catch (InvalidOperationException ex) { notes.Add(ex.Message); continue; }
                        owner = LabelOf(xenia, to);
                        Report(("Save " + label + " installed in Xenia for " + owner + Was(r.Backup) + ": " + r.Target, DamagedNote("Save " + label, damaged)), notes);
                        copied++;
                    }
                }
                catch (OperationCanceledException) { Log.Warn("Install a save cancelled."); }
                catch (Exception ex) { Log.Error("Install a save failed: " + ex.Message); Dlg.Error(what, ex.Message); return copied > 0; }
                finally { RemoveTemps(temps); cts.Dispose(); }
            }
            if (copied > 0 && ownFor != null)
            {
                // the profile the saves came with is the one Xenia signs in to now, so the game finds them with it
                try
                {
                    XeniaContent.SignIn(xenia, ownFor);
                    notes.Insert(0, "Xenia now signs in to " + LabelOf(xenia, ownFor) + " when a game starts (Xenia > Profiles > Sign in to selected switches back).");
                }
                catch (Exception ex) { notes.Add("Could not make Xenia sign in to " + ownFor + ": " + ex.Message); }
            }
            if (copied > 0)
                Dlg.Info(what, copied + " save(s) installed in Xenia for " + owner + ". " +
                               (gameName != null ? "Start " + gameName + " in Xenia and load it." : "Start the game in Xenia and load it.") +
                               (notes.Count > 0 ? "\n\n" + string.Join("\n\n", notes.Take(6)) : ""));
            else if (notes.Count > 0) Dlg.Info(what, string.Join("\n\n", notes.Take(8)));
            return copied > 0;
        }

        private static string SaveLabel(StfsPackage s) =>
            (s.TitleName.Length > 0 ? s.TitleName : s.TitleIdHex) + " - " + (s.DisplayName.Length > 0 ? s.DisplayName : Path.GetFileName(s.Path));

        /// <summary>
        /// Where to look for packages: folders and files as they are; a .zip unpacked by the program itself, and a .rar or
        /// .7z by the installed 7-Zip, each into a new temporary folder (added to <paramref name="temps"/> for clean-up).
        /// </summary>
        private static List<string> Unpack(List<string> paths, List<string> temps, List<string> notes, CancellationToken ct)
        {
            var result = new List<string>();
            foreach (string p in paths)
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(p) || !Archives.IsArchive(p)) { result.Add(p); continue; }
                string dir = Path.Combine(Path.GetTempPath(), "X360GameHack", "unpack-" + Guid.NewGuid().ToString("N").Substring(0, 12));
                Directory.CreateDirectory(dir);
                temps.Add(dir);
                string ext = Path.GetExtension(p).ToLowerInvariant();
                try
                {
                    if (ext == ".zip") Archives.UnzipInto(p, dir, ct);
                    else if (!Archives.SevenZipInto(p, dir, ct))
                    {
                        notes.Add(Path.GetFileName(p) + " is a " + ext.Substring(1).ToUpperInvariant() + " archive. Install 7-Zip (7-zip.org) and try again, or unpack it yourself and pick the folder.");
                        continue;
                    }
                    result.Add(dir);
                }
                catch (InvalidDataException ex) { notes.Add(Path.GetFileName(p) + ": " + ex.Message); }
            }
            return result;
        }

        private static void RemoveTemps(List<string> temps)
        {
            foreach (string t in temps)
            {
                try { Directory.Delete(t, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
