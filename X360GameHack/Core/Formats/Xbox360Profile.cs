using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace X360GameHack.Core.Formats
{
    /// <summary>What an achievement edit changed.</summary>
    public struct ProfileEditResult
    {
        public int Achievements;   // achievements that changed state
        public int Gamerscore;     // their combined gamerscore
        public int Games;          // games that were touched
        public int Awards;         // avatar awards that changed state

        public static ProfileEditResult operator +(ProfileEditResult a, ProfileEditResult b) =>
            new ProfileEditResult { Achievements = a.Achievements + b.Achievements, Gamerscore = a.Gamerscore + b.Gamerscore, Games = a.Games + b.Games, Awards = a.Awards + b.Awards };
    }

    /// <summary>
    /// An Xbox 360 gamer profile: an STFS package (content type 0x10000) holding the encrypted "Account" file, the
    /// dashboard GPD (FFFE07D1.gpd: game list, gamercard settings) and one GPD per game (achievements).
    ///
    /// Editing writes what a console itself writes, as observed in console-made profiles after real offline unlocks:
    ///   game GPD      - achievement flags get "earned" + the Xbox 360 platform bits and the unlock time; its sync
    ///                   record moves to the end of the list with the next sync id; the game's own totals (settings
    ///                   0x10040039 achievements / 0x10040038 gamerscore) follow, created on the first unlock.
    ///   dashboard GPD - the game's record gets the new totals and the "needs sync" flag and its sync record moves
    ///                   to the end of the list; the gamercard totals (0x10040013 achievements / 0x10040006
    ///                   gamerscore) move by the same amount.
    /// The gamertag lives only in the Account file (the package header carries the profile ID, not the name).
    /// </summary>
    public sealed partial class Xbox360Profile
    {
        public const uint DashboardTitleId = 0xFFFE07D1;
        private const uint GamercardCredSetting = 0x10040006, GamercardAchievementsSetting = 0x10040013;   // dashboard GPD
        private const uint TitleCredSetting = 0x10040038, TitleAchievementsSetting = 0x10040039;           // each game's GPD

        public string Path { get; private set; }
        public uint ContentType { get; private set; }
        public bool IsProfile => ContentType == 0x10000;
        public string ProfileId { get; private set; } = "";
        public string ConsoleId { get; private set; } = "";

        public ProfileAccount Account { get; private set; }
        public string AccountError { get; private set; }
        public string AccountEntryPath { get; private set; }

        /// <summary>Every game record in the dashboard GPD.</summary>
        public List<GpdTitle> Titles { get; private set; } = new List<GpdTitle>();
        public Dictionary<uint, List<GpdAchievement>> AchievementsByTitle { get; } = new Dictionary<uint, List<GpdAchievement>>();

        private readonly Dictionary<string, XdbfFile> _gpds = new Dictionary<string, XdbfFile>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _stored = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);   // as they are in the package
        private readonly Dictionary<uint, string> _titleGpd = new Dictionary<uint, string>();
        private XdbfFile _dash;
        private byte[] _storedAccount;

        // ---------------------------------------------------------------- totals

        /// <summary>Gamerscore / achievements earned in the games this profile holds (sum of its game records).</summary>
        public int TotalGamerscore => (int)Math.Min(int.MaxValue, Titles.Sum(t => (long)t.GamerscoreUnlocked));
        public int PossibleGamerscore => (int)Math.Min(int.MaxValue, Titles.Sum(t => (long)t.GamerscoreTotal));
        public int AchievementsUnlocked => (int)Math.Min(int.MaxValue, Titles.Sum(t => (long)t.AchievementsUnlocked));
        public int AchievementsTotal => (int)Math.Min(int.MaxValue, Titles.Sum(t => (long)t.AchievementCount));

        /// <summary>
        /// The totals the gamercard shows (dashboard settings). They can be higher than the sums above: they also count
        /// gamerscore earned outside the games stored in this profile. Null when the profile has never earned anything.
        /// </summary>
        public int? GamercardGamerscore => _dash?.GetInt32Setting(GamercardCredSetting);
        public int? GamercardAchievements => _dash?.GetInt32Setting(GamercardAchievementsSetting);

        /// <summary>True when the account is linked to Xbox LIVE (edits would be uploaded the next time it signs in).</summary>
        public bool IsLiveLinked => Account != null && (Account.LiveEnabled || Account.IsOnlineXuid);

        /// <summary>Real games (excludes the dashboard/system title and empty rows), most recently played first.</summary>
        public List<GpdTitle> Games => Titles
            .Where(t => t.TitleId != 0 && t.TitleId != DashboardTitleId && (t.AchievementCount > 0 || t.GamerscoreTotal > 0 || !string.IsNullOrEmpty(t.Name)))
            .OrderByDescending(t => t.LastPlayed ?? DateTime.MinValue)
            .ToList();

        public List<GpdAchievement> AchievementsFor(uint titleId) =>
            AchievementsByTitle.TryGetValue(titleId, out var list) ? list : new List<GpdAchievement>();

        /// <summary>Null when the game's achievements can be edited, else why not.</summary>
        public string EditBlocker(uint titleId)
        {
            if (_dash == null) return "This profile has no dashboard database (FFFE07D1.gpd).";
            if (_dash.EditBlocker != null) return "The profile's dashboard database cannot be edited safely: " + _dash.EditBlocker + ".";
            if (!_titleGpd.TryGetValue(titleId, out string path) || !_gpds.TryGetValue(path, out var gpd))
                return "The profile does not hold this game's achievement data (only its totals).";
            if (gpd.EditBlocker != null) return "This game's database cannot be edited safely: " + gpd.EditBlocker + ".";
            if (TitleRecord(titleId) == null) return "This game has no record in the profile's game list.";
            return null;
        }

        // ---------------------------------------------------------------- open

        public static Xbox360Profile Open(string path)
        {
            var p = new Xbox360Profile { Path = path };
            using (var v = StfsVolume.Open(path))
            {
                byte[] head = v.ReadHead();
                if (head.Length >= 0x348) p.ContentType = BE.U32(head, 0x344);
                if (head.Length >= 0x379) p.ProfileId = BE.Hex(head, 0x371, 8);
                if (head.Length >= 0x371) p.ConsoleId = BE.Hex(head, 0x36C, 5);

                var files = v.Entries.Where(e => !e.IsDirectory).ToList();

                var account = files.FirstOrDefault(e => string.Equals(e.Name, "Account", StringComparison.OrdinalIgnoreCase));
                if (account != null)
                {
                    p.AccountEntryPath = account.Path;
                    try
                    {
                        p._storedAccount = v.ReadFile(account);
                        p.Account = ProfileAccount.Load(p._storedAccount);
                    }
                    catch (Exception ex) { p.AccountError = ex.Message; }
                }
                else p.AccountError = "No Account file in this package.";

                foreach (var e in files) p._fileNames.Add(e.Path);
                Func<string, byte[]> rootFile = name =>
                {
                    var entry = files.FirstOrDefault(e => string.Equals(e.Path, name, StringComparison.OrdinalIgnoreCase));
                    try { return entry == null ? null : v.ReadFile(entry); } catch (Exception) { return null; }
                };
                p.LoadPictures(head, rootFile(Tile64Name), rootFile(Tile32Name));
                var pec = files.FirstOrDefault(e => string.Equals(e.Name, "PEC", StringComparison.OrdinalIgnoreCase));
                if (pec != null)
                {
                    try { p.LoadPec(pec.Path, v.ReadFile(pec)); }
                    catch (Exception ex) { p.PecError = ex.Message; }
                }

                foreach (var e in files.Where(x => x.Name.EndsWith(".gpd", StringComparison.OrdinalIgnoreCase)))
                {
                    byte[] data;
                    XdbfFile gpd;
                    try
                    {
                        data = v.ReadFile(e);
                        gpd = XdbfFile.Parse(data);
                    }
                    catch (Exception) { continue; }   // unreadable GPD: left alone, never rewritten
                    if (p._gpds.ContainsKey(e.Path)) continue;

                    p._gpds[e.Path] = gpd;
                    p._stored[e.Path] = data;

                    string stem = System.IO.Path.GetFileNameWithoutExtension(e.Name);
                    if (!uint.TryParse(stem, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint titleId)) continue;
                    if (titleId == DashboardTitleId) { if (p._dash == null) p._dash = gpd; }
                    else if (!p._titleGpd.ContainsKey(titleId)) p._titleGpd[titleId] = e.Path;
                }
            }
            p.RebuildView();
            return p;
        }

        /// <summary>Re-reads the game list and achievements from the (possibly edited) databases.</summary>
        private void RebuildView()
        {
            Titles = _dash != null ? _dash.Titles().ToList() : new List<GpdTitle>();
            RebuildAwardView();
            AchievementsByTitle.Clear();
            foreach (var kv in _titleGpd)
            {
                var achievements = _gpds[kv.Value].Achievements().ToList();
                if (achievements.Count > 0) AchievementsByTitle[kv.Key] = achievements;
            }
        }

        // ---------------------------------------------------------------- gamertag

        /// <summary>Changes the gamertag (stored in the Account file only).</summary>
        public void SetGamertag(string gamertag)
        {
            if (Account == null) throw new InvalidOperationException(AccountError ?? "This profile has no readable Account file.");
            string problem = ProfileAccount.GamertagProblem(gamertag);
            if (problem != null) throw new ArgumentException(problem);
            Account.Gamertag = gamertag;
        }

        // ---------------------------------------------------------------- achievements

        public ProfileEditResult UnlockAchievements(uint titleId, ICollection<uint> achievementIds, DateTime? when = null) => Finish(Change(titleId, achievementIds, true, when));
        public ProfileEditResult LockAchievements(uint titleId, ICollection<uint> achievementIds) => Finish(Change(titleId, achievementIds, false, null));
        public ProfileEditResult UnlockAllForTitle(uint titleId, DateTime? when = null) => Finish(Change(titleId, null, true, when));
        public ProfileEditResult LockAllForTitle(uint titleId) => Finish(Change(titleId, null, false, null));

        /// <summary>Unlocks every achievement of every game the profile holds achievement data for.</summary>
        public ProfileEditResult UnlockAll(DateTime? when = null) => Finish(ChangeAll(true, when ?? DateTime.UtcNow));
        public ProfileEditResult LockAll() => Finish(ChangeAll(false, null));

        private ProfileEditResult ChangeAll(bool unlock, DateTime? when)
        {
            var total = new ProfileEditResult();
            var ids = Titles.Select(t => t.TitleId).Distinct().ToList();
            if (unlock && SpreadUnlockTimes) { ids = ids.OrderBy(x => _spreadRng.Next()).ToList(); _spreadCursor = when ?? DateTime.UtcNow; }
            try { foreach (uint titleId in ids) if (EditBlocker(titleId) == null) total += Change(titleId, null, unlock, when); }
            finally { _spreadCursor = null; }
            return total;
        }

        private ProfileEditResult Finish(ProfileEditResult r)
        {
            RebuildView();
            return r;
        }

        private XdbfFile.Entry TitleRecord(uint titleId) =>
            _dash?.Records(XdbfFile.Ns.Title).FirstOrDefault(e => e.Data.Length >= 0x28 && BE.U32(e.Data, 0) == titleId);

        private static void Count(XdbfFile gpd, out long unlocked, out long gamerscore)
        {
            unlocked = gamerscore = 0;
            foreach (var e in gpd.Records(XdbfFile.Ns.Achievement))
            {
                if (e.Data.Length < 0x1C || (BE.U32(e.Data, 0x10) & GpdAchievement.FlagUnlocked) == 0) continue;
                unlocked++;
                gamerscore += BE.U32(e.Data, 0x0C);
            }
        }

        private static long Clamp(long value, long low, long high) => value < low ? low : value > high ? high : value;

        /// <summary>
        /// When on, unlocked achievements and avatar awards do not all get the same instant. Each one gets its own
        /// time, laid out backwards from the chosen date as play sessions (minutes apart inside a session, hours or
        /// days between sessions), the bigger achievements later, and one game after another when several games are
        /// unlocked at once. They are still stored as earned offline, exactly as a console stores them.
        /// </summary>
        public static bool SpreadUnlockTimes;

        private static readonly Random _spreadRng = new Random();
        private DateTime? _spreadCursor;     // set while several games are unlocked in one go, so their sessions do not overlap

        /// <summary>n times, latest first, ending shortly before the cursor; moves the cursor back past them.</summary>
        internal static List<DateTime> SpreadSchedule(int n, ref DateTime cursor, Random rng)
        {
            var floor = new DateTime(2005, 11, 22, 0, 0, 0, DateTimeKind.Utc);       // no Xbox 360 existed before this
            DateTime start = cursor.ToUniversalTime();
            if (start <= floor.AddDays(2)) start = floor.AddDays(2);
            var list = new List<DateTime>(n);
            DateTime t = start.AddMinutes(-rng.Next(20, 360)).AddSeconds(-rng.Next(0, 60));
            for (int i = 0; i < n; i++)
            {
                list.Add(t);
                t = rng.NextDouble() < 0.7 ? t.AddMinutes(-rng.Next(4, 76)).AddSeconds(-rng.Next(1, 60))        // same sitting
                                           : t.AddMinutes(-rng.Next(14 * 60, 4 * 24 * 60)).AddSeconds(-rng.Next(1, 60));   // another day
            }
            if (n > 0 && list[n - 1] < floor.AddDays(1))
            {
                // too many to fit before the console existed: squeeze the same pattern into the time there is
                double factor = (start - floor.AddDays(1)).Ticks * 0.95 / (start - list[n - 1]).Ticks;
                for (int i = 0; i < n; i++) list[i] = start.AddTicks(-(long)((start - list[i]).Ticks * factor));
                t = list[n - 1];
                cursor = start;
            }
            else cursor = t.AddMinutes(-rng.Next(60, 3 * 24 * 60));
            return list;
        }

        private ProfileEditResult Change(uint titleId, ICollection<uint> only, bool unlock, DateTime? when)
        {
            var result = new ProfileEditResult();
            if (titleId == DashboardTitleId) return result;
            string blocker = EditBlocker(titleId);
            if (blocker != null) throw new InvalidOperationException(blocker);
            var gpd = _gpds[_titleGpd[titleId]];
            var title = TitleRecord(titleId);

            Count(gpd, out long unlockedBefore, out long gamerscoreBefore);
            long recordUnlocked = BE.U32(title.Data, 0x08), recordGamerscore = BE.U32(title.Data, 0x10);
            int recordOnline = BE.U16(title.Data, 0x14);
            bool inStep = recordUnlocked == unlockedBefore && recordGamerscore == gamerscoreBefore;

            long changedGamerscore = 0;
            int onlineLocked = 0;
            Dictionary<ulong, DateTime> spread = null;
            if (unlock && SpreadUnlockTimes)
            {
                var todo = gpd.Records(XdbfFile.Ns.Achievement).Where(e => e.Data.Length >= 0x1C && (only == null || only.Contains(BE.U32(e.Data, 0x04))) && (BE.U32(e.Data, 0x10) & GpdAchievement.FlagUnlocked) == 0)
                              .OrderByDescending(e => BE.U32(e.Data, 0x0C) + (uint)_spreadRng.Next(0, 40)).ToList();    // bigger ones later, loosely
                DateTime cursor = _spreadCursor ?? when ?? DateTime.UtcNow;
                var times = SpreadSchedule(todo.Count, ref cursor, _spreadRng);
                if (_spreadCursor != null) _spreadCursor = cursor;
                spread = new Dictionary<ulong, DateTime>();
                for (int i = 0; i < todo.Count; i++) spread[todo[i].Id] = times[i];
            }
            foreach (var e in gpd.Records(XdbfFile.Ns.Achievement).ToList())
            {
                if (e.Data.Length < 0x1C) continue;
                if (only != null && !only.Contains(BE.U32(e.Data, 0x04))) continue;
                uint flags = BE.U32(e.Data, 0x10);
                if (((flags & GpdAchievement.FlagUnlocked) != 0) == unlock) continue;
                if (!unlock && (flags & GpdAchievement.FlagUnlockedOnline) != 0) onlineLocked++;
                GpdAchievement.SetUnlockState(e.Data, unlock, spread != null && spread.TryGetValue(e.Id, out DateTime own) ? own : when);
                gpd.MarkChanged(XdbfFile.Ns.Achievement, e.Id);
                result.Achievements++;
                changedGamerscore += BE.U32(e.Data, 0x0C);
            }
            if (result.Achievements == 0) return result;
            result.Games = 1;
            result.Gamerscore = (int)Clamp(changedGamerscore, 0, int.MaxValue);

            // The game's own running totals. A console creates them on the first unlock (achievements, then gamerscore)
            // and they always equal a recount of the game's achievements.
            Count(gpd, out long unlockedNow, out long gamerscoreNow);
            if (unlockedNow > 0 || gpd.Find(XdbfFile.Ns.Setting, TitleAchievementsSetting) != null)
                gpd.SetInt32Setting(TitleAchievementsSetting, (int)Clamp(unlockedNow, 0, int.MaxValue));
            if (unlockedNow > 0 || gpd.Find(XdbfFile.Ns.Setting, TitleCredSetting) != null)
                gpd.SetInt32Setting(TitleCredSetting, (int)Clamp(gamerscoreNow, 0, int.MaxValue));

            // The game's record in the profile's game list: the recount when record and game agreed before the edit,
            // otherwise the record moves by what changed (it may know of unlocks the local game data does not hold).
            int sign = unlock ? 1 : -1;
            long achievementCount = BE.U32(title.Data, 0x04), gamerscoreTotal = BE.U32(title.Data, 0x0C);
            long newUnlocked = inStep ? unlockedNow : Clamp(recordUnlocked + sign * result.Achievements, 0, Math.Max(achievementCount, unlockedNow));
            long newGamerscore = inStep ? gamerscoreNow : Clamp(recordGamerscore + sign * changedGamerscore, 0, Math.Max(gamerscoreTotal, gamerscoreNow));
            newUnlocked = Clamp(newUnlocked, 0, uint.MaxValue);
            newGamerscore = Clamp(newGamerscore, 0, uint.MaxValue);
            int newOnline = (int)Clamp(recordOnline - onlineLocked, 0, ushort.MaxValue);
            // "needs sync" is what a console sets; the achievement tile image cannot be supplied here, so the
            // "download achievement image" request is set as well when something was unlocked.
            GpdTitle.SetTotals(title.Data, (uint)newUnlocked, (uint)newGamerscore, (ushort)newOnline,
                               GpdTitle.FlagNeedsSync | (unlock ? GpdTitle.FlagDownloadAchievementImage : 0));
            _dash.MarkChanged(XdbfFile.Ns.Title, title.Id);

            // The gamercard totals move by exactly what the game's record moved. They are never recomputed: they also
            // count gamerscore from outside the games held in this profile.
            AdjustGamercard(GamercardAchievementsSetting, newUnlocked - recordUnlocked, t => BE.U32(t, 0x08));
            AdjustGamercard(GamercardCredSetting, newGamerscore - recordGamerscore, t => BE.U32(t, 0x10));
            return result;
        }

        private void AdjustGamercard(uint settingId, long delta, Func<byte[], uint> perTitle)
        {
            if (delta == 0) return;
            int? current = _dash.GetInt32Setting(settingId);
            if (current.HasValue)
                _dash.SetInt32Setting(settingId, (int)Clamp(current.Value + delta, 0, int.MaxValue));
            else if (_dash.Find(XdbfFile.Ns.Setting, settingId) != null)
                throw new InvalidDataException("Gamercard setting 0x" + settingId.ToString("X8") + " has an unexpected format.");
            else if (delta > 0)
            {
                // A profile that never earned anything has no total yet: it starts as the sum over its game records.
                long sum = 0;
                foreach (var e in _dash.Records(XdbfFile.Ns.Title))
                    if (e.Data.Length >= 0x28) sum += perTitle(e.Data);
                _dash.SetInt32Setting(settingId, (int)Clamp(sum, 0, int.MaxValue));
            }
        }

        // ---------------------------------------------------------------- save

        /// <summary>The files inside the package whose contents differ from what is stored (entry path -> new bytes).</summary>
        private Dictionary<string, byte[]> PendingFiles()
        {
            var edits = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _gpds)
            {
                if (!_stored.TryGetValue(kv.Key, out byte[] was)) continue;   // a new file: see PendingAdds
                byte[] now = kv.Value.ToBytes();
                if (!now.SequenceEqual(was)) edits[kv.Key] = now;
            }
            if (Account != null && AccountEntryPath != null && _storedAccount != null)
            {
                byte[] now = Account.ToBytes();
                if (!now.SequenceEqual(_storedAccount)) edits[AccountEntryPath] = now;
            }
            return edits;
        }

        /// <summary>Game databases added by <see cref="AddGame"/> that are not in the package yet.</summary>
        private Dictionary<string, byte[]> PendingAdds()
        {
            var adds = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _gpds)
                if (!_stored.ContainsKey(kv.Key)) adds[kv.Key] = kv.Value.ToBytes();
            return adds;
        }

        public bool HasChanges => PendingFiles().Count > 0 || PendingAdds().Count > 0 || PecChanged || PicturePending;

        /// <summary>
        /// Writes every pending change into the package in one step (all files or none - see
        /// <see cref="StfsEditor.ReplaceFiles"/>) and returns how many files inside it changed. The package's hashes are
        /// valid afterwards; its console signature is not renewed here (Rehash &amp; resign does that).
        /// </summary>
        /// <param name="keyVault">
        /// When avatar awards changed, the PEC container inside the profile gets a new console signature from this
        /// keyvault. Without one its old signature no longer matches (hashes are still renewed).
        /// </param>
        public int SaveChanges(KeyVault keyVault = null)
        {
            var edits = PendingFiles();
            var adds = PendingAdds();
            byte[] pec = BuildPec(keyVault);
            if (pec != null) edits[_pecPath] = pec;
            // the gamer picture: its two files and the copy in the package header change in the same step
            StfsMetadata header = null;
            if (PicturePending)
            {
                (_fileNames.Contains(Tile64Name) ? edits : adds)[Tile64Name] = _newTile64;
                (_fileNames.Contains(Tile32Name) ? edits : adds)[Tile32Name] = _newTile32;
                header = new StfsMetadata { ThumbnailPng = _newTile64 };
            }
            if (edits.Count == 0 && adds.Count == 0) return 0;
            StfsEditor.Update(Path, edits, adds, header);
            foreach (var kv in edits)
            {
                if (IsTile(kv.Key)) continue;
                if (string.Equals(kv.Key, AccountEntryPath, StringComparison.OrdinalIgnoreCase)) _storedAccount = kv.Value;
                else if (pec != null && string.Equals(kv.Key, _pecPath, StringComparison.OrdinalIgnoreCase)) AcceptPec(pec);
                else _stored[kv.Key] = kv.Value;
            }
            foreach (var kv in adds) { if (!IsTile(kv.Key)) _stored[kv.Key] = kv.Value; _fileNames.Add(kv.Key); }
            if (header != null) AcceptPictures();
            return edits.Count + adds.Count;
        }
    }
}
