using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Mods
{
    public enum ModState { Locked, Unlocked, Error }

    /// <summary>One mod file on this PC and whether it is unlocked right now.</summary>
    public sealed class LoadedMod
    {
        public string Path = "";
        public ModPack Pack;
        public ModState State = ModState.Locked;
        public string Message = "";
        public string FileName => System.IO.Path.GetFileName(Path);
        public ModInfo Info => Pack?.Info;
        internal GameSaveKind RegisteredSave;
        internal GameModKind RegisteredGame;
    }

    /// <summary>
    /// Finds the mod files in the "mods" folder next to the program, lists what they are, and makes each one
    /// available. Free/open mods load straight away. Paid mods unlock only when the buyer activates them, and the
    /// key and the decrypted data then exist only in memory, wiped as soon as the mod is built and again when it
    /// is locked. This program never writes a mod's contents to disk or sends them anywhere.
    /// </summary>
    public static class ModManager
    {
        private static readonly List<LoadedMod> _mods = new List<LoadedMod>();

        public static string Folder => Path.Combine(Tools.AppDir, "mods");
        /// <summary>
        /// Where mod files downloaded from the licence server are kept: this user's own folder, so it works for an
        /// installed copy in Program Files too. Mod files are handed out only by the licence server, to a licensed PC.
        /// </summary>
        public static string DownloadFolder => TestDownloadFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "X360GameHack", "mods");
        /// <summary>For automated tests only: stands in for <see cref="DownloadFolder"/>. Null in normal use.</summary>
        internal static string TestDownloadFolder;
        public static IReadOnlyList<LoadedMod> Mods { get { lock (_mods) return _mods.ToList(); } }
        public static event Action Changed;

        /// <summary>Reads the mod files and loads the free ones. Paid ones are listed but stay locked until activated.</summary>
        public static void Scan()
        {
            lock (_mods)
            {
                var keptUnlocked = _mods.Where(m => m.State == ModState.Unlocked && m.Info != null && !m.Info.Open).ToList();
                _mods.Clear();
                // downloaded ones first: a mod file the licence server sent wins over another copy of the same mod
                var files = new List<string>();
                foreach (string dir in new[] { DownloadFolder, Folder })
                    if (Directory.Exists(dir)) files.AddRange(Directory.EnumerateFiles(dir, "*.x360mod").OrderBy(x => x));
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (string f in files)
                    {
                        var m = new LoadedMod { Path = f };
                        try { m.Pack = ModPack.Load(f); }
                        catch (Exception ex) { m.State = ModState.Error; m.Message = "Not a readable mod file: " + ex.Message; _mods.Add(m); continue; }
                        // only the X360GameHack team makes mods for this program: a file it did not sign (or one changed
                        // since) is listed as refused and never loaded, unlocked or sent to the licence server
                        if (!m.Pack.OwnerSigned)
                        {
                            m.State = ModState.Error;
                            m.Message = "Not an X360GameHack mod: it is not signed by the X360GameHack team (or it was changed), so it is not loaded.";
                            _mods.Add(m);
                            continue;
                        }
                        if (!ids.Add(m.Pack.Info.Id)) continue;          // the same mod twice: the downloaded copy is kept
                        var prev = keptUnlocked.FirstOrDefault(k => k.Info != null && k.Info.Id == m.Info.Id);
                        if (prev != null) { m.State = ModState.Unlocked; m.Message = prev.Message; m.RegisteredSave = prev.RegisteredSave; m.RegisteredGame = prev.RegisteredGame; }
                        _mods.Add(m);
                    }
                // free/open mods carry their data in the clear and need no server
                foreach (var m in _mods.Where(m => m.State == ModState.Locked && m.Pack != null && m.Pack.IsOpen && m.Pack.Sealed != null))
                {
                    try { string err = BuildAndRegister(m, m.Pack.Sealed); m.State = err == null ? ModState.Unlocked : ModState.Error; if (err != null) m.Message = err; else m.Message = "Free mod - " + m.Message; }
                    catch (Exception ex) { m.State = ModState.Error; m.Message = ex.Message; }
                }
                Reregister();
            }
            Changed?.Invoke();
        }

        /// <summary>The licence server paid mods are unlocked through. Built in; a settings value only replaces it for testing.</summary>
        public const string DefaultServer = "https://x360gh-licence.x360gh-licence.workers.dev";
        public static string ServerUrl { get { string s = AppSettings.Current.ModServerUrl; return string.IsNullOrWhiteSpace(s) ? DefaultServer : s.Trim(); } }

        /// <summary>Where the one licence token that unlocks every paid mod is remembered (per-mod tokens use the mod's id).</summary>
        public const string LicenceKey = "*";
        public static string SavedToken(string modId) { AppSettings.Current.ModTokens.TryGetValue(modId ?? "", out string t); return t ?? ""; }
        /// <summary>The remembered licence token, or "".</summary>
        public static string LicenceToken => SavedToken(LicenceKey);

        /// <summary>
        /// Activates one paid mod with a token: asks the licence server for the key (or, for a server-only mod, the
        /// data), unlocks it and adds it to the tools. Returns null on success, else why it did not work.
        /// </summary>
        public static string Activate(LoadedMod mod, string token, bool remember)
        {
            if (mod?.Info == null) return "That mod file could not be read.";
            if (!mod.Pack.OwnerSigned) return "Not an X360GameHack mod: it is not signed by the X360GameHack team, so it is not loaded.";
            if (mod.Pack.IsOpen) return "This is a free mod - it is already available.";
            var res = ActivationClient.Activate(ServerUrl, token, mod.Info, HardwareId.Current());
            if (!res.Ok) return res.Error ?? "The licence server refused this token.";
            string err = Open(mod, res);
            if (err == null && remember) { AppSettings.Current.ModTokens[mod.Info.Id] = token.Trim(); AppSettings.Save(); }
            if (err == null) Changed?.Invoke();
            return err;
        }

        /// <summary>What happened when a licence token was used.</summary>
        public sealed class UnlockReport
        {
            /// <summary>Set when the server refused the token itself (or could not be reached).</summary>
            public string Error;
            public readonly List<LoadedMod> Unlocked = new List<LoadedMod>();
            public readonly Dictionary<LoadedMod, string> Problems = new Dictionary<LoadedMod, string>();
            /// <summary>What fetching the mod files from the licence server did first (Unlock my mods), or null.</summary>
            public SyncReport Sync;
        }

        /// <summary>What fetching the licence's mod files from the server did.</summary>
        public sealed class SyncReport
        {
            /// <summary>Set when the server refused the token or could not be reached.</summary>
            public string Error;
            public readonly List<string> Downloaded = new List<string>();
            public readonly List<string> UpToDate = new List<string>();
            public readonly List<string> Problems = new List<string>();
        }

        /// <summary>
        /// Fetches every mod file this licence covers from the licence server into <see cref="DownloadFolder"/> - only the
        /// ones that are missing or different (compared by SHA-256). Each file is kept only when its size, SHA-256 and id
        /// are exactly what the server listed. The files are sealed: only the server's keys open them, at unlock time.
        /// </summary>
        public static SyncReport SyncFromServer(string token)
        {
            var report = new SyncReport();
            if (string.IsNullOrWhiteSpace(token)) { report.Error = "Enter your licence token."; return report; }
            string hw = HardwareId.Current();
            var list = ActivationClient.ListPacks(ServerUrl, token, hw);
            if (!list.Ok) { report.Error = list.Error; return report; }
            foreach (var p in list.Packs)
            {
                string target = Path.Combine(DownloadFolder, p.ModId + ".x360mod");
                if (File.Exists(target) && Sha256(target) == p.Sha256) { report.UpToDate.Add(p.Name); continue; }
                var data = ActivationClient.DownloadPack(ServerUrl, token, hw, p, out string error);
                if (data == null) { report.Problems.Add(p.Name + ": " + error); continue; }
                try
                {
                    Directory.CreateDirectory(DownloadFolder);
                    string part = target + ".part";
                    File.WriteAllBytes(part, data);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(part, target);
                    report.Downloaded.Add(p.Name + " " + p.Version);
                }
                catch (Exception ex) { report.Problems.Add(p.Name + ": could not be saved (" + ex.Message + ")"); }
            }
            return report;
        }

        private static string Sha256(string path)
        {
            try { using (var fs = File.OpenRead(path)) using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "").ToLowerInvariant(); }
            catch { return ""; }
        }

        /// <summary>The last fetch of mod files this session (null when none ran yet): the start-up unlock fetches only once.</summary>
        public static SyncReport LastSync { get; private set; }

        /// <summary>
        /// Unlocks every locked paid mod a licence token covers - all of them for an all-mods licence - in one request
        /// to the licence server. With <paramref name="remember"/>, the token is kept so they unlock by themselves
        /// whenever the program starts.
        /// </summary>
        public static UnlockReport UnlockWith(string token, bool remember, IEnumerable<LoadedMod> only = null)
        {
            var report = new UnlockReport();
            if (string.IsNullOrWhiteSpace(token)) { report.Error = "Enter your licence token."; return report; }
            if (only == null)
            {
                // Unlock my mods: first fetch the licence's mod files from the server (the only place they come from)
                var sync = LastSync = SyncFromServer(token);
                report.Sync = sync;
                if (sync.Error != null && !Mods.Any(m => m.Info != null && !m.Info.Open)) { report.Error = sync.Error; return report; }
                if (sync.Downloaded.Count > 0) Scan();
            }
            var targets = (only ?? Mods).Where(m => m.Info != null && m.Pack != null && m.Pack.OwnerSigned && !m.Info.Open && m.State != ModState.Unlocked).ToList();
            if (targets.Count == 0) return report;
            var res = ActivationClient.ActivateAll(ServerUrl, token, targets.Select(m => m.Info).ToList(), HardwareId.Current());
            if (!res.Ok) { report.Error = res.Error ?? "The licence server refused this token."; return report; }
            foreach (var m in targets)
            {
                if (!res.Mods.TryGetValue(m.Info.Id, out ActivationResult one)) { report.Problems[m] = "The licence server did not answer for this mod."; continue; }
                string err = one.Ok ? Open(m, one) : one.Error;
                if (err == null) report.Unlocked.Add(m); else report.Problems[m] = err;
            }
            if (report.Unlocked.Count > 0)
            {
                if (remember) { AppSettings.Current.ModTokens[LicenceKey] = token.Trim(); AppSettings.Save(); }
                Changed?.Invoke();
            }
            return report;
        }

        /// <summary>Opens a paid mod with what the licence server sent and adds it to the tools. Null on success.</summary>
        private static string Open(LoadedMod mod, ActivationResult res)
        {
            byte[] plaintext = null, kAes = res.KeyAes, kMac = res.KeyMac;
            try
            {
                if (mod.Info.ServerOnly) { if (res.Payload == null) return "The server sent no data for this mod."; plaintext = res.Payload; }
                else
                {
                    if (kAes == null || kMac == null) return "The server did not send the key for this mod.";
                    if (mod.Pack.Sealed == null) return "This mod file has no payload to unlock.";
                    plaintext = CryptoBox.Open(kAes, kMac, mod.Pack.Sealed);
                }
                string err = BuildAndRegister(mod, plaintext);
                if (err != null) { mod.State = ModState.Error; mod.Message = err; return err; }
                mod.State = ModState.Unlocked;
                lock (_autoTried) _keptLocked.Remove(mod.Info.Id);
                if (mod.RegisteredSave != null) GameSaves.Register(mod.RegisteredSave);   // register this mod directly
                if (mod.RegisteredGame != null) GameMods.Register(mod.RegisteredGame);     // (works whether or not it came from a folder scan)
                return null;
            }
            catch (Exception ex) { mod.State = ModState.Error; mod.Message = ex.Message; return ex.Message; }
            finally { CryptoBox.Wipe(plaintext); CryptoBox.Wipe(kAes); CryptoBox.Wipe(kMac); }
        }

        private static readonly HashSet<string> _autoTried = new HashSet<string>();
        /// <summary>Mods the user locked by hand: they are not unlocked again by themselves until the program is restarted or the user unlocks them.</summary>
        private static readonly HashSet<string> _keptLocked = new HashSet<string>();

        /// <summary>
        /// One click: unlocks every locked paid mod that has a remembered token. Each mod is tried once per run of
        /// the program (pass retry to try again), so a refused token does not hammer the server. Returns what
        /// happened, one line per mod tried.
        /// </summary>
        public static List<KeyValuePair<LoadedMod, string>> UnlockSaved(bool retry = false)
        {
            var done = new List<KeyValuePair<LoadedMod, string>>();
            // one request per remembered token: normally just the licence token, for every mod at once
            var groups = new Dictionary<string, List<LoadedMod>>(StringComparer.Ordinal);
            string licence = LicenceToken;
            // with a remembered licence, mod files it covers that are not on this PC yet are fetched once per session
            if (licence.Length > 0 && (LastSync == null || retry))
            {
                var sync = LastSync = SyncFromServer(licence);
                if (sync.Downloaded.Count > 0) Scan();
            }
            foreach (var m in Mods.Where(m => m.State == ModState.Locked && m.Info != null && m.Pack != null && m.Pack.OwnerSigned && !m.Info.Open))
            {
                string token = SavedToken(m.Info.Id);
                if (token.Length == 0) token = licence;
                if (token.Length == 0) continue;
                lock (_autoTried)
                {
                    if (_keptLocked.Contains(m.Info.Id)) continue;          // locked on purpose: leave it
                    if (retry) _autoTried.Remove(m.Info.Id);
                    if (!_autoTried.Add(m.Info.Id)) continue;
                }
                if (!groups.TryGetValue(token, out var list)) groups[token] = list = new List<LoadedMod>();
                list.Add(m);
            }
            foreach (var g in groups)
            {
                var report = UnlockWith(g.Key, false, g.Value);
                foreach (var m in g.Value)
                    done.Add(new KeyValuePair<LoadedMod, string>(m, report.Unlocked.Contains(m) ? null
                        : report.Problems.TryGetValue(m, out string why) ? why : report.Error ?? "It was not unlocked."));
            }
            return done;
        }

        /// <summary>Reads the mod's kind from the decrypted payload and makes it available on the right tool.</summary>
        private static string BuildAndRegister(LoadedMod mod, byte[] plaintext)
        {
            string kind;
            try { kind = (string)JObject.Parse(System.Text.Encoding.UTF8.GetString(plaintext))["kind"] ?? "save"; }
            catch { return "The mod data is not readable."; }
            if (string.Equals(kind, "game", StringComparison.OrdinalIgnoreCase))
            {
                var g = GameModAdapter.Build(plaintext);
                mod.RegisteredGame = g; mod.RegisteredSave = null;
                mod.Message = g.Game + " - " + g.Options.Count + " option(s)";
            }
            else
            {
                var s = SaveModAdapter.Build(plaintext);
                mod.RegisteredSave = s; mod.RegisteredGame = null;
                mod.Message = s.Game + " - " + s.What;
            }
            return null;
        }

        private static void Reregister()
        {
            GameSaves.ClearExternal(); GameMods.ClearExternal();
            foreach (var m in _mods.Where(m => m.State == ModState.Unlocked))
            {
                if (m.RegisteredSave != null) GameSaves.Register(m.RegisteredSave);
                if (m.RegisteredGame != null) GameMods.Register(m.RegisteredGame);
            }
        }

        public static void Lock(LoadedMod mod)
        {
            if (mod == null) return;
            if (mod.Info != null && mod.Info.Open) { Changed?.Invoke(); return; }   // a free mod cannot be "locked"
            mod.State = ModState.Locked; mod.RegisteredSave = null; mod.RegisteredGame = null; mod.Message = "";
            if (mod.Info != null) lock (_autoTried) _keptLocked.Add(mod.Info.Id);
            lock (_mods) Reregister();
            Changed?.Invoke();
        }

        /// <summary>
        /// Forgets the remembered token of a mod, so it no longer unlocks by itself when the program starts.
        /// Returns false when no token was remembered. The licence itself is untouched: the same token still works
        /// on this PC when it is typed in again.
        /// </summary>
        public static bool ForgetToken(string modId, bool save = true)
        {
            bool had = AppSettings.Current.ModTokens.Remove(modId ?? "");
            if (had && save) AppSettings.Save();
            return had;
        }

        /// <summary>
        /// Forgets every remembered token on this PC (the licence token and any per-mod ones) and locks every paid
        /// mod, so nothing unlocks by itself any more. The licences are untouched: typing the token again unlocks them
        /// on this PC. Returns how many tokens were forgotten.
        /// </summary>
        public static int ForgetAllTokens(bool save = true)
        {
            int n = AppSettings.Current.ModTokens.Count;
            AppSettings.Current.ModTokens.Clear();
            if (save) AppSettings.Save();
            foreach (var m in Mods.Where(m => m.Info != null && !m.Info.Open && m.State == ModState.Unlocked)) Lock(m);
            return n;
        }

        /// <summary>Called when the program closes: forget every unlocked paid mod.</summary>
        public static void LockAll()
        {
            lock (_mods) foreach (var m in _mods.Where(m => m.Pack != null && m.Pack.OwnerSigned && !m.Info.Open)) { m.State = ModState.Locked; m.RegisteredSave = null; m.RegisteredGame = null; }
            GameSaves.ClearExternal(); GameMods.ClearExternal();
        }
    }
}
