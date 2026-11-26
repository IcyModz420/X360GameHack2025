using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace X360GameHack.Core.Services
{
    /// <summary>Saved FTP connection profile.</summary>
    public sealed class FtpProfile
    {
        public string Name { get; set; } = "Xbox 360";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 21;
        public string User { get; set; } = "xbox";

        /// <summary>Kept in memory only; saved encrypted with Windows DPAPI (bound to this Windows account).</summary>
        [JsonIgnore] public string Password { get; set; } = "xbox";

        [JsonProperty("PasswordProtected")]
        public string PasswordProtected
        {
            get => Dpapi.Protect(Password);
            set => Password = Dpapi.Unprotect(value) ?? "";
        }

        // Older versions stored the password in plain text: read it once, never write it back.
        [JsonProperty("Password", NullValueHandling = NullValueHandling.Ignore)]
        private string LegacyPassword
        {
            get => null;
            set { if (!string.IsNullOrEmpty(value)) Password = value; }
        }
        public bool Passive { get; set; } = true;
        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Host : Name + "  (" + Host + ")";
    }

    /// <summary>Windows DPAPI (CurrentUser) helpers for secrets stored in settings.</summary>
    internal static class Dpapi
    {
        private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("X360GameHack/ftp/v1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var data = System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(plain), Entropy,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }

        public static string Unprotect(string protectedB64)
        {
            if (string.IsNullOrEmpty(protectedB64)) return "";
            try
            {
                var data = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(protectedB64), Entropy,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(data);
            }
            catch { return null; }   // written by another Windows account / machine
        }
    }

    /// <summary>
    /// Application settings persisted as JSON in %AppData%\X360GameHack\settings.json.
    /// Values from the legacy Properties.Settings are migrated on first run.
    /// </summary>
    public sealed class AppSettings
    {
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "X360GameHack");
        private static readonly string FilePath = Path.Combine(Dir, "settings.json");

        public static AppSettings Current { get; private set; } = new AppSettings();

        // --- General ---
        public bool CheckForUpdates { get; set; } = true;
        /// <summary>No longer used (the old one-time "open the releases page" question); kept so older settings files still load.</summary>
        public bool UpdatePromptDismissed { get; set; }
        /// <summary>
        /// When a newer version signed with X360GameHack's update key is released, ask whether to install it (the update
        /// window shows the download's SHA-256 and every file it adds or replaces). Off = only the title bar says so.
        /// Nothing is ever installed without the user's yes.
        /// </summary>
        public bool AutoUpdate { get; set; } = true;
        /// <summary>A version the user chose "Skip this version" for: not offered again at start-up (Check for updates now still shows it).</summary>
        public string SkippedUpdateVersion { get; set; } = "";
        /// <summary>The "opened N times" count as last read from the public page (shown again when the page cannot be reached).</summary>
        public string LastOpens { get; set; } = "";
        /// <summary>The "copying to the console's hard drive over XBDM is slow" tip has been shown (it is shown once, ever).</summary>
        public bool HddCopyTipShown { get; set; }
        public bool ShowToolWindows { get; set; }          // run tools in a visible cmd window instead of capturing output
        public bool KeepLogFile { get; set; } = true;
        public bool LogDrawerOpen { get; set; }
        /// <summary>The log starts closed (more room for the page); this records that the new default was applied once to older settings.</summary>
        public bool LogStartsClosed { get; set; }
        public List<string> RecentFiles { get; set; } = new List<string>();
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        /// <summary>Where the window was last left (int.MinValue = never set, so centre it).</summary>
        public int WindowX { get; set; } = int.MinValue;
        public int WindowY { get; set; } = int.MinValue;
        /// <summary>The DPI the window size and place above are in (0 = saved by a build Windows stretched: 96-DPI units).</summary>
        public int WindowDpi { get; set; }
        public bool WindowMaximized { get; set; }

        // --- XEX ---
        public string XexCustomCommand { get; set; } = "-r mrbdiyvklc -m r";
        public string PkgCustomCommand { get; set; } = "";
        public bool BackupBeforePatch { get; set; } = true;

        // --- XISO ---
        public string ExtractPath { get; set; } = "";
        public bool ExcludeSystemUpdate { get; set; } = true;
        public bool BulkPatchTitleUpdate { get; set; }
        public bool BulkPatchRgh { get; set; } = true;
        public bool BulkPatchXdk { get; set; }
        public bool BulkUseCustom { get; set; }
        public string BulkCustomCommand { get; set; } = "-r mrbdiyvklc -m r";
        public bool BulkInstallContent { get; set; }

        // --- Games on Demand (ISO2God / god2iso) ---
        /// <summary>Where new packages go (a Content\0000000000000000 folder is made inside). Empty = next to each image.</summary>
        public string GodOutputPath { get; set; } = "";
        /// <summary>Padding removal for new queue entries: 0 None, 1 Partial, 2 Full (ISO2God's default).</summary>
        public int GodPadding { get; set; } = 2;
        /// <summary>ISO2God's "Auto-rename multi-disc games".</summary>
        public bool GodAddDiscNumber { get; set; } = true;
        /// <summary>Full padding removal also saves the rebuilt image (in <see cref="GodRebuiltPath"/>, else next to the original).</summary>
        public bool GodKeepRebuilt { get; set; }
        public string GodRebuiltPath { get; set; } = "";
        /// <summary>0 = name packages like ISO2God, 1 = like Microsoft.</summary>
        public int GodNaming { get; set; }
        /// <summary>After converting: 0 nothing, 1 USB drive, 2 console over XBDM, 3 console over FTP.</summary>
        public int GodAfter { get; set; }
        /// <summary>0 = packages for RGH / JTAG, 1 = for a devkit.</summary>
        public int GodTarget { get; set; }
        /// <summary>xextool: remove the game's XEX limits.</summary>
        public bool GodUnlockXex { get; set; } = true;
        /// <summary>X360PkgTool: the target's unlock on each finished package.</summary>
        public bool GodUnlockPackage { get; set; } = true;
        public string GodXbdmDrive { get; set; } = "Hdd";
        public string GodFtpDrive { get; set; } = "Hdd1";
        /// <summary>Where GOD to ISO writes images. Empty = next to each package.</summary>
        public string GodIsoOutputPath { get; set; } = "";
        /// <summary>0 = compact like god2iso, 1 = every sector in its place.</summary>
        public int GodIsoLayout { get; set; }
        public bool GodIsoFixCreateIsoGood { get; set; } = true;
        /// <summary>0 = name images after the game, 1 = after the package file (as god2iso).</summary>
        public int GodIsoNaming { get; set; }
        /// <summary>Convert tab: 0 Games on Demand, 1 disc image, 2 XEX version (folder).</summary>
        public int ConvertTo { get; set; }
        /// <summary>Convert tab: 0 as it is (no XEX or package change), 1 RGH / JTAG, 2 devkit.</summary>
        public int ConvertMadeFor { get; set; } = 1;
        /// <summary>Where XEX versions (folders) are written. Empty = next to each source.</summary>
        public string ConvertFolderOutputPath { get; set; } = "";
        public bool ConvertSkipSystemUpdate { get; set; } = true;
        /// <summary>Convert tab, to a disc image: run abgx360 AutoFix on each new image so it gets its stealth sectors from the database.</summary>
        public bool ConvertStealthPatch { get; set; } = true;

        // --- abgx360 tab ---
        /// <summary>The options on the abgx360 tab (AbgxOptions.Save format). Empty = abgx360's defaults.</summary>
        public string AbgxOptions { get; set; } = "";
        /// <summary>0 = file(s), 1 = folder, 2 = DVD drive (the GUI's Input choice).</summary>
        public int AbgxInput { get; set; }
        public string AbgxFolder { get; set; } = "";
        public bool AbgxSubFolders { get; set; }
        public string AbgxMatch { get; set; } = "";
        /// <summary>0 = report shown here, 1 = abgx360 in its own window (the GUI's CLI window).</summary>
        public int AbgxOutput { get; set; }

        // --- Network ---
        public List<FtpProfile> FtpProfiles { get; set; } = new List<FtpProfile>();
        public int SelectedFtpProfile { get; set; }
        public string XbdmIp { get; set; } = "";
        public bool XbdmAutoConnect { get; set; }
        public string XellIp { get; set; } = "";
        public string UsbDrive { get; set; } = "";

        // --- Xenia and xemu ---
        public string XeniaPath { get; set; } = "";
        public string XemuPath { get; set; } = "";
        /// <summary>Where "Play in Xenia" keeps games copied from the console. Empty = Documents\X360GameHack\Xenia Games.</summary>
        public string XeniaGamesDir { get; set; } = "";
        /// <summary>Where original Xbox games copied off a drive for xemu go (empty = Documents\X360GameHack\Original Xbox Games).</summary>
        public string XemuGamesDir { get; set; } = "";
        /// <summary>Keep Xenia's input setting on every controller (Xbox, PlayStation, Switch Pro, others) whenever the program starts it.</summary>
        public bool XeniaAllControllers { get; set; } = true;
        /// <summary>Games added to the Xenia page by hand (a XEX, game package or disc image anywhere on this PC).</summary>
        public List<string> XeniaAddedGames { get; set; } = new List<string>();
        /// <summary>When each game was last started in Xenia from this program (launch path -> local time).</summary>
        public Dictionary<string, DateTime> XeniaLastPlayed { get; set; } = new Dictionary<string, DateTime>();

        // --- Paid mods (add-on licensed files) ---
        /// <summary>Address of the licence server that unlocks paid mods. The buyer's own, or the author's.</summary>
        public string ModServerUrl { get; set; } = "";
        /// <summary>Unlocked achievements get natural-looking, spread-out offline dates instead of one identical instant. On unless the user turns it off.</summary>
        public bool NaturalUnlockDates { get; set; } = true;
        /// <summary>Remembered licence tokens, by mod id. A token is not a key and is bound to this machine, so it is safe to keep here.</summary>
        public System.Collections.Generic.Dictionary<string, string> ModTokens { get; set; } = new System.Collections.Generic.Dictionary<string, string>();

        // --- Signing ---
        /// <summary>Where the user's KV.bin is, so Rehash &amp; resign can sign with it. Only the location: the key itself is never stored.</summary>
        public string KeyVaultPath { get; set; } = "";

        // --- Appearance ---
        public string ThemeName { get; set; } = ThemePalette.DefaultName;
        public List<ThemePalette> CustomThemes { get; set; } = new List<ThemePalette>();

        // --- Folders ---
        /// <summary>Where saves pulled from a console / drive are kept (empty = Documents\X360GameHack\Saves).</summary>
        public string SavesFolder { get; set; } = "";
        public bool StartOnLastPage { get; set; }
        public string LastPage { get; set; } = "";

        [JsonIgnore]
        public string SavesFolderResolved => !string.IsNullOrWhiteSpace(SavesFolder) ? SavesFolder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "X360GameHack", "Saves");

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(FilePath));
                    if (loaded != null) Current = loaded;
                }
                else
                {
                    MigrateLegacy();
                    Save();
                }
            }
            catch
            {
                Current = new AppSettings();
            }
            Normalize(Current);
        }

        /// <summary>Repairs anything missing or unsafe after loading or importing.</summary>
        private static void Normalize(AppSettings s)
        {
            // Older versions defaulted to "-r a", which also zeroes the Media ID and breaks title updates.
            if (s.XexCustomCommand == "-r a -m r") s.XexCustomCommand = "-r mrbdiyvklc -m r";
            if (s.BulkCustomCommand == "-r a -m r") s.BulkCustomCommand = "-r mrbdiyvklc -m r";
            if (s.FtpProfiles == null) s.FtpProfiles = new List<FtpProfile>();
            if (s.RecentFiles == null) s.RecentFiles = new List<string>();
            if (s.FtpProfiles.Count == 0) s.FtpProfiles.Add(new FtpProfile());
            if (s.FtpProfiles.Count > 50) s.FtpProfiles = s.FtpProfiles.Take(50).ToList();
            if (s.RecentFiles.Count > 12) s.RecentFiles = s.RecentFiles.Take(12).ToList();
            if (s.XeniaAddedGames == null) s.XeniaAddedGames = new List<string>();
            if (s.XeniaAddedGames.Count > 200) s.XeniaAddedGames = s.XeniaAddedGames.Take(200).ToList();
            if (s.XeniaLastPlayed == null) s.XeniaLastPlayed = new Dictionary<string, DateTime>();
            if (s.XeniaLastPlayed.Count > 500) s.XeniaLastPlayed = s.XeniaLastPlayed.OrderByDescending(kv => kv.Value).Take(500).ToDictionary(kv => kv.Key, kv => kv.Value);
            s.XexCustomCommand = s.XexCustomCommand ?? "";
            s.BulkCustomCommand = s.BulkCustomCommand ?? "";
            s.PkgCustomCommand = s.PkgCustomCommand ?? "";
            s.KeyVaultPath = s.KeyVaultPath ?? "";
            s.CustomThemes = SanitizeThemes(s.CustomThemes);
            var previous = Current;
            Current = s;   // theme lookup below reads Current
            if (ThemePalette.Find(s.ThemeName) == null) s.ThemeName = ThemePalette.DefaultName;
            if (!ReferenceEquals(previous, s)) Current = previous;
        }

        /// <summary>Settings as JSON for backup / moving to another PC. Saved FTP passwords are never included.</summary>
        public static string Export()
        {
            var jo = Newtonsoft.Json.Linq.JObject.FromObject(Current);
            if (jo["FtpProfiles"] is Newtonsoft.Json.Linq.JArray profiles)
                foreach (var p in profiles.OfType<Newtonsoft.Json.Linq.JObject>()) p.Remove("PasswordProtected");
            jo.Remove("RecentFiles");
            jo.Remove("KeyVaultPath");
            return jo.ToString(Formatting.Indented);
        }

        /// <summary>Replaces the current settings with an exported file (validated; passwords are not imported).</summary>
        public static void Import(string json)
        {
            if (json == null || json.Length > 1024 * 1024) throw new FormatException("Not a settings file (too large).");
            AppSettings s;
            try
            {
                s = JsonConvert.DeserializeObject<AppSettings>(json, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    MaxDepth = 16
                });
            }
            catch (JsonException) { throw new FormatException("Not a settings file (invalid JSON)."); }
            if (s == null) throw new FormatException("Not a settings file.");
            s.RecentFiles = Current.RecentFiles;
            s.KeyVaultPath = Current.KeyVaultPath;   // an imported file never decides which keyvault is used
            Normalize(s);
            Current = s;
            Save();
        }

        public static void ResetToDefaults()
        {
            var s = new AppSettings { RecentFiles = Current.RecentFiles, FtpProfiles = Current.FtpProfiles, KeyVaultPath = Current.KeyVaultPath };
            Normalize(s);
            Current = s;
            Save();
        }

        /// <summary>Drops broken entries, repairs colors, keeps names unique and never shadows a built-in theme.</summary>
        public static List<ThemePalette> SanitizeThemes(IEnumerable<ThemePalette> themes)
        {
            var clean = new List<ThemePalette>();
            foreach (var t in themes ?? new List<ThemePalette>())
            {
                if (t == null || !t.Sanitize()) continue;
                t.Name = UniqueThemeName(t.Name, clean);
                clean.Add(t);
                if (clean.Count >= ThemePalette.MaxCustomThemes) break;
            }
            return clean;
        }

        public static string UniqueThemeName(string wanted, IEnumerable<ThemePalette> others)
        {
            string baseName = ThemePalette.CleanName(wanted), name = baseName;
            bool Taken(string n) => ThemePalette.BuiltIns.Any(b => b.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) ||
                                    others.Any(o => o.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            for (int i = 2; Taken(name); i++) name = (baseName.Length > 34 ? baseName.Substring(0, 34) : baseName) + " " + i;
            return name;
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(Current, Formatting.Indented));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not save settings: " + ex.Message);
            }
        }

        public void AddRecent(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            RecentFiles.Insert(0, path);
            if (RecentFiles.Count > 12) RecentFiles.RemoveRange(12, RecentFiles.Count - 12);
            Save();
        }

        private static void MigrateLegacy()
        {
            try
            {
                var s = Properties.Settings.Default;
                var c = new AppSettings();
                if (!string.IsNullOrWhiteSpace(s.IP))
                {
                    int port;
                    c.FtpProfiles.Add(new FtpProfile
                    {
                        Name = "Saved console",
                        Host = s.IP,
                        Port = int.TryParse(s.Port, out port) ? port : 21,
                        User = string.IsNullOrEmpty(s.Username) ? "xbox" : s.Username,
                        Password = string.IsNullOrEmpty(s.Password) ? "xbox" : s.Password
                    });
                    c.XellIp = s.IP;
                }
                c.XbdmIp = s.XBDMIP ?? "";
                c.ExtractPath = s.ExtractPath ?? "";
                c.UsbDrive = s.SelectedUSBLetter ?? "";
                if (!string.IsNullOrWhiteSpace(s.XexToolCustomCommand)) c.XexCustomCommand = s.XexToolCustomCommand;
                c.UpdatePromptDismissed = s.HasBeenAskedToUpdate;
                Current = c;
            }
            catch
            {
                Current = new AppSettings();
            }
        }
    }
}
