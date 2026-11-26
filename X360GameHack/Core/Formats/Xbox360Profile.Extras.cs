using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace X360GameHack.Core.Formats
{
    /// <summary>
    /// The console signature of a PEC (the container inside a profile that holds avatar awards): the console
    /// certificate at 0, an RSA signature at 0x1A8 over SHA-1 of bytes 0x23C..0x1000, and that same hash at 0x228.
    /// (Both verified with the certificates' own public keys on two console-written profiles.)
    /// </summary>
    public static class PecSignature
    {
        public const int SignatureOffset = 0x1A8, HashOffset = 0x228, SignedStart = 0x23C, HeaderSize = 0x1000;

        public enum State { Valid, BadSignature, BadHeaderHash, NotPec }

        private static byte[] Digest(byte[] pec)
        {
            using (var sha = SHA1.Create()) return sha.ComputeHash(pec, SignedStart, HeaderSize - SignedStart);
        }

        private static bool LooksLikePec(byte[] pec) => pec != null && pec.Length >= HeaderSize && pec[0] == 0x01 && pec[1] == 0xA8 && pec[0x244] == 0x24;

        public static State Check(byte[] pec)
        {
            if (!LooksLikePec(pec)) return State.NotPec;
            byte[] digest = Digest(pec);
            if (!digest.SequenceEqual(pec.Skip(HashOffset).Take(20))) return State.BadHeaderHash;
            var cert = pec.Take(KeyVault.CertificateSize).ToArray();
            var signature = pec.Skip(SignatureOffset).Take(0x80).ToArray();
            return ConSignature.VerifyRaw(cert, signature, digest) ? State.Valid : State.BadSignature;
        }

        /// <summary>The console that signed it (hex), or "" when the file is not a PEC.</summary>
        public static string ConsoleId(byte[] pec) => LooksLikePec(pec) ? BE.Hex(pec, 2, 5) : "";

        /// <summary>Installs the keyvault's certificate and signs the header (in place). The header hash must already be current.</summary>
        public static void Sign(byte[] pec, KeyVault kv)
        {
            if (kv == null) throw new ArgumentNullException(nameof(kv));
            if (!LooksLikePec(pec)) throw new InvalidDataException("Not a PEC container.");
            byte[] digest = Digest(pec);
            if (!digest.SequenceEqual(pec.Skip(HashOffset).Take(20))) throw new InvalidDataException("The PEC's header hash is out of date.");
            Buffer.BlockCopy(kv.Certificate, 0, pec, 0, KeyVault.CertificateSize);
            Buffer.BlockCopy(kv.SignDigest(digest), 0, pec, SignatureOffset, 0x80);
            if (Check(pec) != State.Valid) throw new CryptographicException("The new PEC signature did not verify.");
        }
    }

    /// <summary>
    /// A game as it can be added to a profile: its achievement database in the state a console creates it in (nothing
    /// earned, nothing to upload) and, when known, its avatar awards.
    /// </summary>
    public sealed class GameTemplate
    {
        public uint TitleId;
        public string Name = "";
        public int AchievementCount, Gamerscore, AwardCount, MaleAwards, FemaleAwards;
        public byte[] TitleGpd;
        /// <summary>The game's avatar award database (null when the source had none).</summary>
        public byte[] AwardGpd;

        public string TitleIdHex => TitleId.ToString("X8");
        public override string ToString() => Name + "  (" + TitleIdHex + ", " + AchievementCount + " achievements, " + Gamerscore + " G" + (AwardCount > 0 ? ", " + AwardCount + " avatar awards" : "") + ")";

        /// <summary>
        /// A game database as a console first writes it: every achievement not earned, sync ids 1..N (newest first in
        /// the list), no game settings, and the pictures and strings the source holds. Checked against the untouched
        /// game databases of console-written profiles: rebuilding one of those gives back the same records.
        /// </summary>
        public static byte[] FreshTitleGpd(XdbfFile source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var g = XdbfFile.Create(source);
            foreach (var e in source.Records(XdbfFile.Ns.Achievement).OrderBy(e => e.Id))
            {
                if (e.Data.Length < 0x1C) throw new InvalidDataException("The source database holds a malformed achievement.");
                var d = (byte[])e.Data.Clone();
                GpdAchievement.SetUnlockState(d, false);
                g.Add(XdbfFile.Ns.Achievement, e.Id, d);
            }
            g.ResetSync(XdbfFile.Ns.Achievement);
            foreach (var e in source.Records(XdbfFile.Ns.Image)) g.Add(XdbfFile.Ns.Image, e.Id, (byte[])e.Data.Clone());
            g.ResetSync(XdbfFile.Ns.Setting);
            foreach (var e in source.Records(XdbfFile.Ns.String)) g.Add(XdbfFile.Ns.String, e.Id, (byte[])e.Data.Clone());
            return g.ToBytes();
        }

        /// <summary>An avatar award database as a console first writes it: nothing earned, sync ids 1..N.</summary>
        public static byte[] FreshAwardGpd(XdbfFile source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var g = XdbfFile.Create(source);
            foreach (var e in source.Records(XdbfFile.Ns.AvatarAward).OrderBy(e => e.Id))
            {
                if (e.Data.Length < 0x2C) throw new InvalidDataException("The source database holds a malformed avatar award.");
                var d = (byte[])e.Data.Clone();
                GpdAvatarAward.SetUnlockState(d, false);
                g.Add(XdbfFile.Ns.AvatarAward, e.Id, d);
            }
            g.ResetSync(XdbfFile.Ns.AvatarAward);
            foreach (var ns in new[] { XdbfFile.Ns.Image, XdbfFile.Ns.String })
                foreach (var e in source.Records(ns)) g.Add(ns, e.Id, (byte[])e.Data.Clone());
            return g.ToBytes();
        }

        /// <summary>The game's name as its own database gives it (string 0x8000), or "".</summary>
        public static string NameIn(XdbfFile gpd)
        {
            var e = gpd?.Find(XdbfFile.Ns.String, 0x8000);
            return e == null ? "" : XdbfFile.ReadWStr(e.Data, 0, out _);
        }

        internal static GameTemplate Build(uint titleId, string name, XdbfFile titleGpd, XdbfFile awardGpd)
        {
            if (titleGpd.EditBlocker != null) throw new InvalidDataException("The game's database is damaged (" + titleGpd.EditBlocker + ").");
            var t = new GameTemplate { TitleId = titleId, TitleGpd = FreshTitleGpd(titleGpd) };
            t.Name = string.IsNullOrEmpty(name) ? NameIn(titleGpd) : name;
            foreach (var a in titleGpd.Achievements()) { t.AchievementCount++; t.Gamerscore += (int)Math.Min(a.Gamerscore, 100000); }
            if (awardGpd != null && awardGpd.EditBlocker == null)
            {
                var awards = awardGpd.AvatarAwards().ToList();
                if (awards.Count > 0 && awards.Count <= 255)
                {
                    t.AwardGpd = FreshAwardGpd(awardGpd);
                    t.AwardCount = awards.Count;
                    t.MaleAwards = awards.Count(a => a.ForMale);
                    t.FemaleAwards = awards.Count(a => a.ForFemale);
                }
            }
            return t;
        }

        /// <summary>Every game another profile holds a database for.</summary>
        public static List<GameTemplate> FromProfile(string profilePath)
        {
            var source = Xbox360Profile.Open(profilePath);
            return source.Templates();
        }

        /// <summary>
        /// A game straight from its own executable (default.xex): the achievement list, strings and pictures the game
        /// carries are turned into the database a console would write. Avatar awards are not taken from the XEX.
        /// </summary>
        public static GameTemplate FromXex(string xexPath)
        {
            var data = XexTitleData.FromXex(xexPath);
            if (data.TitleId == 0 || data.TitleId >> 16 == 0xFFFE || data.TitleId >> 16 == 0xFFFF) throw new InvalidDataException("That XEX is a system file, not a game.");
            if (data.Achievements.Count == 0) throw new InvalidDataException("That XEX declares no achievements, so there is nothing to add.");
            var t = new GameTemplate { TitleId = data.TitleId, Name = data.Name, TitleGpd = data.BuildTitleGpd(), AchievementCount = data.Achievements.Count };
            t.Gamerscore = data.Achievements.Sum(a => (int)a.Gamerscore);
            return t;
        }

        /// <summary>
        /// A game from the public title database (dbox.tools): its name and Xbox 360 achievement list, turned into the
        /// database a console would write - without the achievement pictures, which the database does not have.
        /// </summary>
        public static GameTemplate FromOnline(uint titleId, string name, List<Services.OnlineAchievement> achievements)
        {
            if (titleId == 0 || titleId >> 16 == 0xFFFE || titleId >> 16 == 0xFFFF) throw new InvalidDataException("That Title ID is not a game.");
            if (achievements == null || achievements.Count == 0) throw new InvalidDataException("The title database lists no Xbox 360 achievements for that game.");
            var data = XexTitleData.FromOnline(titleId, name, achievements);
            return new GameTemplate { TitleId = titleId, Name = name ?? "", TitleGpd = data.BuildTitleGpd(), AchievementCount = data.Achievements.Count, Gamerscore = data.Achievements.Sum(a => (int)a.Gamerscore) };
        }

        /// <summary>A game from a loose database file named after its title ID (e.g. 4D5307E6.gpd).</summary>
        public static GameTemplate FromGpdFile(string gpdPath)
        {
            string stem = Path.GetFileNameWithoutExtension(gpdPath);
            if (!uint.TryParse(stem, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint titleId) || stem.Length != 8)
                throw new ArgumentException("The file must be named after the game's title ID, for example 4D5307E6.gpd.");
            if (new FileInfo(gpdPath).Length > 64L * 1024 * 1024) throw new InvalidDataException("That file is too large to be a game database.");
            var gpd = XdbfFile.Parse(File.ReadAllBytes(gpdPath));
            if (!gpd.Records(XdbfFile.Ns.Achievement).Any() && NameIn(gpd).Length == 0) throw new InvalidDataException("That file holds no achievements and no game name.");
            return Build(titleId, null, gpd, null);
        }
    }

    public sealed partial class Xbox360Profile
    {
        private const uint TitlesPlayedSetting = 0x10040012;   // dashboard GPD: number of games in the list

        private readonly HashSet<string> _fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---------------------------------------------------------------- the PEC (avatar awards)

        private string _pecPath;
        private byte[] _pecStored;
        private readonly Dictionary<string, XdbfFile> _pecGpds = new Dictionary<string, XdbfFile>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _pecGpdStored = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Why the profile's avatar awards could not be read, or null.</summary>
        public string PecError { get; private set; }
        /// <summary>True when the profile holds an avatar award container (a console creates it with the first game that has awards).</summary>
        public bool HasAwardStore => _pecStored != null;
        /// <summary>State of that container's console signature.</summary>
        public PecSignature.State AwardStoreSignature => _pecStored == null ? PecSignature.State.NotPec : PecSignature.Check(_pecStored);

        public Dictionary<uint, List<GpdAvatarAward>> AwardsByTitle { get; } = new Dictionary<uint, List<GpdAvatarAward>>();

        private void LoadPec(string entryPath, byte[] pec)
        {
            using (var v = StfsVolume.Open(pec))
            {
                if (!v.IsPec) throw new InvalidDataException("The profile's PEC file is not an avatar award container.");
                foreach (var e in v.Entries.Where(x => !x.IsDirectory && x.Name.EndsWith(".gpd", StringComparison.OrdinalIgnoreCase)))
                {
                    byte[] data;
                    XdbfFile gpd;
                    try { data = v.ReadFile(e); gpd = XdbfFile.Parse(data); }
                    catch (Exception) { continue; }
                    if (_pecGpds.ContainsKey(e.Path)) continue;
                    _pecGpds[e.Path] = gpd;
                    _pecGpdStored[e.Path] = data;
                }
            }
            _pecPath = entryPath;
            _pecStored = pec;
        }

        private static string GpdName(uint titleId) => titleId.ToString("X8") + ".gpd";

        private void RebuildAwardView()
        {
            AwardsByTitle.Clear();
            foreach (var kv in _pecGpds)
            {
                string stem = System.IO.Path.GetFileNameWithoutExtension(kv.Key);
                if (!uint.TryParse(stem, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint titleId)) continue;
                var awards = kv.Value.AvatarAwards().ToList();
                if (awards.Count > 0) AwardsByTitle[titleId] = awards;
            }
        }

        public List<GpdAvatarAward> AwardsFor(uint titleId) => AwardsByTitle.TryGetValue(titleId, out var list) ? list : new List<GpdAvatarAward>();

        /// <summary>Null when the game's avatar awards can be edited, else why not.</summary>
        public string AwardBlocker(uint titleId)
        {
            if (PecError != null) return "The profile's avatar awards could not be read: " + PecError;
            if (!HasAwardStore) return "This profile has no avatar award data yet (a console creates it when a game with awards is first played).";
            if (!_pecGpds.TryGetValue(GpdName(titleId), out var gpd)) return "The profile holds no avatar awards for this game.";
            if (gpd.EditBlocker != null) return "This game's avatar award database cannot be edited safely: " + gpd.EditBlocker + ".";
            if (_dash == null) return "This profile has no dashboard database (FFFE07D1.gpd).";
            if (_dash.EditBlocker != null) return "The profile's dashboard database cannot be edited safely: " + _dash.EditBlocker + ".";
            if (TitleRecord(titleId) == null) return "This game is not in the profile's game list, so its award counts have nowhere to go. Add the game first.";
            return null;
        }

        public ProfileEditResult UnlockAwards(uint titleId, ICollection<ulong> awardIds, DateTime? when = null) => Finish(ChangeAwards(titleId, awardIds, true, when));
        public ProfileEditResult LockAwards(uint titleId, ICollection<ulong> awardIds) => Finish(ChangeAwards(titleId, awardIds, false, null));

        /// <summary>Earns every avatar award of every game that can be edited.</summary>
        public ProfileEditResult UnlockAllAwards(DateTime? when = null) => Finish(ChangeAllAwards(true, when ?? DateTime.UtcNow));
        public ProfileEditResult LockAllAwards() => Finish(ChangeAllAwards(false, null));

        private ProfileEditResult ChangeAllAwards(bool unlock, DateTime? when)
        {
            var total = new ProfileEditResult();
            var ids = AwardsByTitle.Keys.ToList();
            if (unlock && SpreadUnlockTimes) { ids = ids.OrderBy(x => _spreadRng.Next()).ToList(); _spreadCursor = when ?? DateTime.UtcNow; }
            try { foreach (uint titleId in ids) if (AwardBlocker(titleId) == null) total += ChangeAwards(titleId, null, unlock, when); }
            finally { _spreadCursor = null; }
            return total;
        }

        /// <summary>
        /// What a console writes when a game hands out an avatar award offline (seen in a console-written profile):
        /// the award gets the earned + Xbox 360 platform flags and the time, and its sync record moves to the end of
        /// the list with the next sync id; the game's record in the profile's game list gets the new earned counts
        /// (total, male, female) and the "needs sync" + "avatar award" flags, and its sync record moves to the end.
        /// </summary>
        private ProfileEditResult ChangeAwards(uint titleId, ICollection<ulong> only, bool unlock, DateTime? when)
        {
            var result = new ProfileEditResult();
            string blocker = AwardBlocker(titleId);
            if (blocker != null) throw new InvalidOperationException(blocker);
            var gpd = _pecGpds[GpdName(titleId)];
            var title = TitleRecord(titleId);

            Dictionary<ulong, DateTime> spread = null;
            if (unlock && SpreadUnlockTimes)
            {
                var todo = gpd.Records(XdbfFile.Ns.AvatarAward).Where(e => e.Data.Length >= 0x2C && BE.U32(e.Data, 0) == 0x2C && (only == null || only.Contains(e.Id)) && (BE.U32(e.Data, 0x18) & GpdAchievement.FlagUnlocked) == 0)
                              .OrderBy(e => _spreadRng.Next()).ToList();
                DateTime cursor = _spreadCursor ?? when ?? DateTime.UtcNow;
                var times = SpreadSchedule(todo.Count, ref cursor, _spreadRng);
                if (_spreadCursor != null) _spreadCursor = cursor;
                spread = new Dictionary<ulong, DateTime>();
                for (int i = 0; i < todo.Count; i++) spread[todo[i].Id] = times[i];
            }
            foreach (var e in gpd.Records(XdbfFile.Ns.AvatarAward).ToList())
            {
                if (e.Data.Length < 0x2C || BE.U32(e.Data, 0) != 0x2C) continue;
                if (only != null && !only.Contains(e.Id)) continue;
                if (((BE.U32(e.Data, 0x18) & GpdAchievement.FlagUnlocked) != 0) == unlock) continue;
                GpdAvatarAward.SetUnlockState(e.Data, unlock, spread != null && spread.TryGetValue(e.Id, out DateTime own) ? own : when);
                gpd.MarkChanged(XdbfFile.Ns.AvatarAward, e.Id);
                result.Awards++;
            }
            if (result.Awards == 0) return result;
            result.Games = 1;

            var awards = gpd.AvatarAwards().ToList();
            Func<int, byte> cap = n => (byte)Math.Min(255, n);
            title.Data[0x16] = cap(awards.Count(a => a.Unlocked));
            title.Data[0x17] = cap(awards.Count);
            title.Data[0x18] = cap(awards.Count(a => a.Unlocked && a.ForMale));
            title.Data[0x19] = cap(awards.Count(a => a.ForMale));
            title.Data[0x1A] = cap(awards.Count(a => a.Unlocked && a.ForFemale));
            title.Data[0x1B] = cap(awards.Count(a => a.ForFemale));
            BE.W32(title.Data, 0x1C, BE.U32(title.Data, 0x1C) | GpdTitle.FlagNeedsSync | (unlock ? GpdTitle.FlagAvatarAwardSync : 0));
            _dash.MarkChanged(XdbfFile.Ns.Title, title.Id);
            return result;
        }

        /// <summary>True when avatar award changes are waiting to be saved (a keyvault, if one is loaded, signs them).</summary>
        public bool AwardsPending => _pecStored != null && PecChanged;

        private bool PecChanged
        {
            get
            {
                foreach (var kv in _pecGpds)
                    if (!_pecGpdStored.TryGetValue(kv.Key, out byte[] was) || !kv.Value.ToBytes().SequenceEqual(was)) return true;
                return false;
            }
        }

        /// <summary>
        /// The PEC with every pending award change written into it (hashes renewed, and signed when a keyvault is
        /// given), or null when nothing in it changed. Built on a temporary file beside the profile.
        /// </summary>
        private byte[] BuildPec(KeyVault keyVault)
        {
            if (_pecStored == null || !PecChanged) return null;
            var replace = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var add = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _pecGpds)
            {
                byte[] now = kv.Value.ToBytes();
                if (!_pecGpdStored.TryGetValue(kv.Key, out byte[] was)) add[kv.Key] = now;
                else if (!now.SequenceEqual(was)) replace[kv.Key] = now;
            }
            string tmp = Path + ".pec";
            try
            {
                File.WriteAllBytes(tmp, _pecStored);
                StfsEditor.Update(tmp, replace, add);
                byte[] pec = File.ReadAllBytes(tmp);
                if (keyVault != null) PecSignature.Sign(pec, keyVault);
                if (PecSignature.Check(pec) == PecSignature.State.BadHeaderHash) throw new InvalidDataException("The edited avatar award container did not verify.");
                return pec;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        private void AcceptPec(byte[] pec)
        {
            _pecStored = pec;
            foreach (var kv in _pecGpds) _pecGpdStored[kv.Key] = kv.Value.ToBytes();
        }

        // ---------------------------------------------------------------- gamer picture

        /// <summary>
        /// A profile keeps its gamer picture as two PNG files, 64x64 and 32x32, and the package header's own picture
        /// is a copy of the 64x64 one (byte for byte in every console-written profile examined).
        /// </summary>
        public const string Tile64Name = "tile_64.png", Tile32Name = "tile_32.png";
        public const uint PictureKeySetting = 0x4064000F;
        private byte[] _tile64, _tile32, _headerPicture, _newTile64, _newTile32;

        /// <summary>The 64x64 gamer picture (the header's copy when the profile has no tile_64.png), or null.</summary>
        public byte[] GamerPicture => _newTile64 ?? _tile64 ?? _headerPicture;
        /// <summary>The 32x32 gamer picture, or null.</summary>
        public byte[] GamerPictureSmall => _newTile32 ?? _tile32;
        /// <summary>Which stock picture the console last chose ("title id + picture ids"), or null. Not changed by <see cref="SetGamerPicture"/>.</summary>
        public string GamerPictureKey => _dash?.GetStringSetting(PictureKeySetting);
        public bool PicturePending => _newTile64 != null;

        private void LoadPictures(byte[] head, byte[] tile64, byte[] tile32)
        {
            _tile64 = tile64; _tile32 = tile32;
            if (head != null && head.Length >= 0x571A)
            {
                uint size = BE.U32(head, 0x1712);
                if (size >= 8 && size <= 0x4000) { _headerPicture = new byte[size]; Buffer.BlockCopy(head, 0x171A, _headerPicture, 0, (int)size); }
            }
        }

        private static bool IsTile(string path) =>
            string.Equals(path, Tile64Name, StringComparison.OrdinalIgnoreCase) || string.Equals(path, Tile32Name, StringComparison.OrdinalIgnoreCase);

        private void AcceptPictures()
        {
            _tile64 = _headerPicture = _newTile64; _tile32 = _newTile32;
            _newTile64 = _newTile32 = null;
        }

        /// <summary>The pixel size of a PNG, or null when the bytes are not a PNG.</summary>
        public static (int Width, int Height)? PngSize(byte[] png)
        {
            if (png == null || png.Length < 33 || BE.U32(png, 0) != 0x89504E47 || BE.U32(png, 4) != 0x0D0A1A0A || BE.U32(png, 12) != 0x49484452) return null;
            return ((int)BE.U32(png, 16), (int)BE.U32(png, 20));
        }

        /// <summary>
        /// Gives the profile a new gamer picture: <paramref name="png64"/> (64x64) and <paramref name="png32"/> (32x32),
        /// PNG files of at most <see cref="StfsEditor.MaxPictureBytes"/> each (the 64x64 one also goes into the header). Returns false when the profile already has exactly these pictures.
        /// </summary>
        public bool SetGamerPicture(byte[] png64, byte[] png32)
        {
            foreach (var item in new[] { (png64, 64), (png32, 32) })
            {
                var size = PngSize(item.Item1);
                if (size == null) throw new ArgumentException("Gamer pictures are PNG files.");
                if (size.Value.Width != item.Item2 || size.Value.Height != item.Item2) throw new ArgumentException("The " + item.Item2 + "x" + item.Item2 + " gamer picture is " + size.Value.Width + "x" + size.Value.Height + ".");
                if (item.Item1.Length > StfsEditor.MaxPictureBytes) throw new ArgumentException("A gamer picture can be at most " + StfsEditor.MaxPictureBytes.ToString("N0") + " bytes (" + item.Item1.Length.ToString("N0") + " given).");
            }
            if (_tile64 != null && _tile32 != null && _headerPicture != null && png64.SequenceEqual(_tile64) && png32.SequenceEqual(_tile32) && png64.SequenceEqual(_headerPicture))
            {
                _newTile64 = _newTile32 = null;
                return false;
            }
            _newTile64 = (byte[])png64.Clone(); _newTile32 = (byte[])png32.Clone();
            return true;
        }

        // ---------------------------------------------------------------- gamercard details

        public const uint MottoSetting = 0x402C0011, NameSetting = 0x40520041, LocationSetting = 0x40520040, BioSetting = 0x43E80043,
                          ZoneSetting = 0x10040004, ReputationSetting = 0x5004000B;
        public static readonly string[] ZoneNames = { "None", "Recreation", "Pro", "Family", "Underground" };

        /// <summary>The gamercard texts (null = the profile has no such setting yet).</summary>
        public string Motto => _dash?.GetStringSetting(MottoSetting);
        public string RealName => _dash?.GetStringSetting(NameSetting);
        public string Location => _dash?.GetStringSetting(LocationSetting);
        public string Bio => _dash?.GetStringSetting(BioSetting);
        /// <summary>Gamer zone 0-4 (see <see cref="ZoneNames"/>), or null when not set.</summary>
        public int? Zone => _dash?.GetInt32Setting(ZoneSetting);
        /// <summary>Reputation 0-100 (five stars = 100), or null when the profile has none (offline profiles).</summary>
        public float? Reputation => _dash?.GetFloatSetting(ReputationSetting);

        private XdbfFile EditableDash()
        {
            if (_dash == null) throw new InvalidOperationException("This profile has no dashboard database (FFFE07D1.gpd).");
            if (_dash.EditBlocker != null) throw new InvalidOperationException("The profile's dashboard database cannot be edited safely: " + _dash.EditBlocker + ".");
            return _dash;
        }

        /// <summary>Writes the gamercard texts that differ from what is stored (null = leave alone). Returns how many changed.</summary>
        public int SetGamercardText(string motto, string realName, string location, string bio)
        {
            var dash = EditableDash();
            int changed = 0;
            foreach (var item in new[] { (MottoSetting, motto), (NameSetting, realName), (LocationSetting, location), (BioSetting, bio) })
            {
                if (item.Item2 == null) continue;
                string now = dash.GetStringSetting(item.Item1);
                if (now == item.Item2 || (now == null && item.Item2.Length == 0)) continue;   // nothing to create for an empty text
                dash.SetStringSetting(item.Item1, item.Item2);
                changed++;
            }
            return changed;
        }

        public bool SetZone(int zone)
        {
            if (zone < 0 || zone >= ZoneNames.Length) throw new ArgumentOutOfRangeException(nameof(zone));
            var dash = EditableDash();
            if (dash.GetInt32Setting(ZoneSetting) == zone) return false;
            bool existed = dash.Find(XdbfFile.Ns.Setting, ZoneSetting) != null;
            dash.SetInt32Setting(ZoneSetting, zone);
            dash.MarkChanged(XdbfFile.Ns.Setting, ZoneSetting);
            return true;
        }

        public bool SetReputation(float reputation)
        {
            if (float.IsNaN(reputation) || reputation < 0 || reputation > 100) throw new ArgumentOutOfRangeException(nameof(reputation), "Reputation runs from 0 to 100.");
            var dash = EditableDash();
            if (dash.GetFloatSetting(ReputationSetting) == reputation) return false;
            dash.SetFloatSetting(ReputationSetting, reputation);
            return true;
        }

        /// <summary>Sets (or with null clears) the "last played" time of a game in the game list.</summary>
        public bool SetLastPlayed(uint titleId, DateTime? when)
        {
            EditableDash();
            var title = TitleRecord(titleId) ?? throw new InvalidOperationException("This game is not in the profile's game list.");
            ulong value = when.HasValue ? (ulong)when.Value.ToFileTimeUtc() : 0;
            if (BE.U64(title.Data, 0x20) == value) return false;
            BE.W64(title.Data, 0x20, value);
            RebuildView();
            return true;
        }

        // ---------------------------------------------------------------- game adder

        /// <summary>Every game this profile holds a database for, as a template for another profile.</summary>
        internal List<GameTemplate> Templates()
        {
            var list = new List<GameTemplate>();
            foreach (var kv in _titleGpd.OrderBy(k => k.Key))
            {
                var gpd = _gpds[kv.Value];
                var record = Titles.FirstOrDefault(t => t.TitleId == kv.Key);
                if (kv.Key == DashboardTitleId || kv.Key == 0) continue;
                if (!gpd.Records(XdbfFile.Ns.Achievement).Any() && record == null) continue;   // system databases without a game behind them
                _pecGpds.TryGetValue(GpdName(kv.Key), out var awards);
                try { list.Add(GameTemplate.Build(kv.Key, record?.Name, gpd, awards)); }
                catch (InvalidDataException) { }
            }
            return list.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Null when the game can be added, else why not.</summary>
        public string AddGameBlocker(GameTemplate game)
        {
            if (game == null || game.TitleGpd == null) return "No game given.";
            if (game.TitleId == 0 || game.TitleId == DashboardTitleId) return "That is not a game.";
            if (_dash == null) return "This profile has no dashboard database (FFFE07D1.gpd).";
            if (_dash.EditBlocker != null) return "The profile's dashboard database cannot be edited safely: " + _dash.EditBlocker + ".";
            if (TitleRecord(game.TitleId) != null) return "The profile already has " + (game.Name.Length > 0 ? game.Name : game.TitleIdHex) + ".";
            if (_titleGpd.ContainsKey(game.TitleId) || _fileNames.Contains(GpdName(game.TitleId)))
                return "The profile already holds a database for " + game.TitleIdHex + " (without a game list entry); it was left alone.";
            if (game.Name.Length > 127) return "The game's name is too long.";
            return null;
        }

        /// <summary>
        /// Adds a game the way a console does when it is first played: the game's fresh database goes into the
        /// profile, a record is added to the game list (totals, nothing earned) with its sync record at the end of the
        /// list, and the "games played" count goes up by one. When the profile has an avatar award container and the
        /// game has awards, those are added (not earned) as well.
        /// </summary>
        /// <param name="lastPlayed">Shown as "last played". Null leaves it empty, as on a profile that never went online.</param>
        public void AddGame(GameTemplate game, DateTime? lastPlayed = null)
        {
            string blocker = AddGameBlocker(game);
            if (blocker != null) throw new InvalidOperationException(blocker);

            bool withAwards = game.AwardGpd != null && HasAwardStore && PecError == null && !_pecGpds.ContainsKey(GpdName(game.TitleId));
            byte[] name = Encoding.BigEndianUnicode.GetBytes(game.Name + "\0");
            var record = new byte[0x28 + name.Length];
            BE.W32(record, 0x00, game.TitleId);
            BE.W32(record, 0x04, (uint)game.AchievementCount);
            BE.W32(record, 0x0C, (uint)game.Gamerscore);
            if (withAwards)
            {
                record[0x17] = (byte)game.AwardCount;
                record[0x19] = (byte)game.MaleAwards;
                record[0x1B] = (byte)game.FemaleAwards;
            }
            if (lastPlayed.HasValue) BE.W64(record, 0x20, (ulong)lastPlayed.Value.ToFileTimeUtc());
            Buffer.BlockCopy(name, 0, record, 0x28, name.Length);

            var gpd = XdbfFile.Parse(game.TitleGpd);
            _dash.Add(XdbfFile.Ns.Title, game.TitleId, record);
            _dash.MarkChanged(XdbfFile.Ns.Title, game.TitleId);
            int? played = _dash.GetInt32Setting(TitlesPlayedSetting);
            if (played.HasValue) _dash.SetInt32Setting(TitlesPlayedSetting, played.Value + 1);
            else if (_dash.Find(XdbfFile.Ns.Setting, TitlesPlayedSetting) == null)
                _dash.SetInt32Setting(TitlesPlayedSetting, _dash.Records(XdbfFile.Ns.Title).Count());

            string file = GpdName(game.TitleId);
            _gpds[file] = gpd;
            _titleGpd[game.TitleId] = file;
            if (withAwards) _pecGpds[file] = XdbfFile.Parse(game.AwardGpd);
            RebuildView();
        }
    }
}
