using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// Every option of abgx360 1.0.7 that a user would set - what "abgx360 --help" lists plus what the abgx360 GUI (its 1.0.6
    /// source) can set that 1.0.7 still reads (--max, --pause, --dir, --rec, --match) - and the command line they add up to.
    /// Long option names are used so the result reads the same as the help text; options left at abgx360's own default add
    /// nothing. (The GUI's --splitvid, --removesplitvid and --e-videopartition are not here: 1.0.6 and 1.0.7 no longer read
    /// them, so the GUI's SplitVid choice does nothing.)
    /// abgx360 is built with MinGW, whose start-up code expands * and ? in unquoted arguments against the working folder
    /// (proven on 1.0.7), so every file name and pattern is always passed quoted.
    /// </summary>
    public sealed class AbgxOptions
    {
        public enum Verbosity { Normal, High, Low }
        public enum Achievements { Hide, Show, ShowWithoutSecret }
        public enum GameCrc { WhenNeeded, Always, Never }
        public enum AutoFix { Never = 0, IfStealthFails = 1, IfUncertain = 2, Always = 3 }
        public enum Rebuild { Default, LowSpace, Never }
        /// <summary>The GUI's "Xex images" choice: extract the game's pictures with HTML output (its default), always, or never.</summary>
        public enum Images { WithHtml, Always, Never }

        // output
        public Verbosity Verbose;
        public bool TerminalFont, StripColors, Html, ShowFiles, AvatarAwards, EmbedImages;
        public Images XexImages;
        public Achievements ShowAchievements;
        public int Language = 1;                 // 1-12, see the help text

        // checks
        public bool CheckCorruption, CheckPadding, FixL0Padding, NoDvdCheck, RegionCheckOnly, NoVerify;
        public GameCrc Crc;

        // online
        public bool NoUpdate, UpdateCsv, StayOffline, LocalOnly;
        public int? NetTimeout;

        // writing / fixing
        public bool NoWrite;
        public AutoFix Fix = AutoFix.IfUncertain;
        public Rebuild Rebuilding;
        public bool KeepOriginalIso;

        // manual patch / extract
        public string PatchVideo, PatchPfi, PatchDmi, PatchSs, ExtractVideo, ExtractPfi, ExtractDmi, ExtractSs;
        public bool PatchItAnyway, PatchGarbage, Clobber;

        // misc
        public bool SkipLayerbreak, NoTrustSsV2, NoFixDrt, NoFixDeviation, FixAngle359, DevKey;
        public int? Deviation, Retries, DvdTimeout;
        public string RegionCode;                // 8 hex digits, e.g. 00FFFFFF
        public long? TruncateTo;

        /// <summary>Keep the console window open when abgx360 finishes (it waits for a key). Only for a run in its own window.</summary>
        public bool PauseWhenDone;
        /// <summary>Maximize abgx360's console window (the GUI's default for its CLI window). Only for a run in its own window.</summary>
        public bool Maximize;

        /// <summary>Null when the options make sense together, else what to fix.</summary>
        public string Problem()
        {
            if (Language < 1 || Language > 12) return "The language is a number from 1 to 12.";
            if (!string.IsNullOrEmpty(RegionCode) && (RegionCode.Length != 8 || !RegionCode.All(Uri.IsHexDigit))) return "The region code is exactly 8 hex digits (for example 00FFFFFF).";
            if (Deviation.HasValue && (Deviation < 0 || Deviation > 180)) return "The angle deviation is between 0 and 180 degrees.";
            if (Retries.HasValue && Retries < 0) return "The number of retries cannot be negative.";
            if (NetTimeout.HasValue && NetTimeout < 0) return "The network timeout cannot be negative.";
            if (DvdTimeout.HasValue && DvdTimeout < 0) return "The DVD timeout cannot be negative.";
            if (TruncateTo.HasValue && TruncateTo < 0) return "The truncate size cannot be negative.";
            if (FixL0Padding && !CheckPadding) return "Fixing L0 padding only works together with the padding check.";
            if (EmbedImages && !Html) return "Embedding images only applies to HTML output.";
            if (EmbedImages && XexImages == Images.Never) return "Embedding images needs the images to be extracted.";
            if (NoWrite && (PatchVideo ?? PatchPfi ?? PatchDmi ?? PatchSs) != null) return "Patching files needs writing to be allowed - turn off \"don't write\".";
            foreach (string f in new[] { PatchVideo, PatchPfi, PatchDmi, PatchSs, ExtractVideo, ExtractPfi, ExtractDmi, ExtractSs })
                if (!string.IsNullOrWhiteSpace(f) && (f.TrimEnd().EndsWith("\\") || f.TrimEnd().EndsWith("/"))) return "\"" + f.Trim() + "\" is a folder - choose a file name.";
            return null;
        }

        /// <summary>The option arguments, one list item per argument (file names are separate items, unquoted).</summary>
        public List<string> Arguments()
        {
            var a = new List<string>();
            void Flag(bool on, string name) { if (on) a.Add(name); }
            void Value(string name, string value) { if (!string.IsNullOrWhiteSpace(value)) { a.Add(name); a.Add(value.Trim()); } }
            void Number(string name, long? value) { if (value.HasValue) { a.Add(name); a.Add(value.Value.ToString(CultureInfo.InvariantCulture)); } }

            if (Verbose == Verbosity.High) a.Add("--verbose"); else if (Verbose == Verbosity.Low) a.Add("--noverbose");
            Flag(TerminalFont, "--terminal");
            Flag(CheckCorruption, "--corrupt");
            Flag(Html, "--html");
            Flag(ShowFiles, "--showfiles");
            Flag(CheckPadding, "--padding");
            Flag(FixL0Padding, "--pL0");
            Flag(StripColors, "--stripcolors");
            Flag(NoDvdCheck, "--nodvdcheck");
            Flag(RegionCheckOnly, "--regioncheck");
            Flag(NoVerify, "--noverify");
            Flag(NoUpdate, "--noupdate");
            Flag(UpdateCsv, "--csv");
            Flag(StayOffline, "--stayoffline");
            Flag(NoWrite, "--nowrite");
            if (ShowAchievements == Achievements.Show) a.Add("--ach"); else if (ShowAchievements == Achievements.ShowWithoutSecret) a.Add("--achs");
            Flag(AvatarAwards, "--aa");
            Flag(XexImages == Images.Always || XexImages == Images.WithHtml && Html, "--images");
            Flag(EmbedImages, "--embed");

            if (Crc == GameCrc.Always) a.Add("--gamecrc"); else if (Crc == GameCrc.Never) a.Add("--nogamecrc");

            if (Fix == AutoFix.Always) a.Add("--af3");
            else if (Fix == AutoFix.IfStealthFails) a.Add("--af1");
            else if (Fix == AutoFix.Never) a.Add("--af0");

            Flag(LocalOnly, "--localonly");

            if (Rebuilding == Rebuild.LowSpace) a.Add("--rebuildlowspace"); else if (Rebuilding == Rebuild.Never) a.Add("--norebuild");
            Flag(KeepOriginalIso, "--keeporiginaliso");

            Value("--p-video", PatchVideo); Value("--p-pfi", PatchPfi); Value("--p-dmi", PatchDmi); Value("--p-ss", PatchSs);
            Flag(PatchItAnyway, "--patchitanyway");
            Flag(PatchGarbage, "--patchgarbage");
            Value("--e-video", ExtractVideo); Value("--e-pfi", ExtractPfi); Value("--e-dmi", ExtractDmi); Value("--e-ss", ExtractSs);
            Flag(Clobber, "--clobber");

            if (Language != 1) Number("--lang", Language);
            Flag(SkipLayerbreak, "--skiplb");
            Flag(NoTrustSsV2, "--notrust");
            Flag(NoFixDrt, "--nofixdrt");
            Number("--dev", Deviation);
            Flag(NoFixDeviation, "--nofixdev");
            Flag(FixAngle359, "--fixangle359");
            Value("--rgn", string.IsNullOrWhiteSpace(RegionCode) ? null : RegionCode.ToUpperInvariant());
            Number("--truncate", TruncateTo);
            Number("--retries", Retries);
            Number("--nettimeout", NetTimeout);
            Number("--dvdtimeout", DvdTimeout);
            Flag(DevKey, "--devkey");
            Flag(Maximize, "--max");
            Flag(PauseWhenDone, "--pause");
            return a;
        }

        /// <summary>Options that take a value: their value is always quoted (it may be a file name or a pattern).</summary>
        private static readonly HashSet<string> WithValue = new HashSet<string> { "--p-video", "--p-pfi", "--p-dmi", "--p-ss", "--e-video", "--e-pfi", "--e-dmi", "--e-ss", "--rgn", "--match", "--dir" };

        private static string Join(List<string> args)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                bool value = i > 0 && WithValue.Contains(args[i - 1]);
                sb.Append(value ? Quote(args[i]) : ToolRunner.Q(args[i]));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Always quoted, escaped as the Windows argument parser expects (MinGW does not expand quoted arguments): backslashes
        /// before a quote - or before the closing quote, as in a folder ending in \ - are doubled, a quote becomes \".
        /// </summary>
        public static string Quote(string arg)
        {
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg ?? "")
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', backslashes); sb.Append(c); }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            return sb.Append('"').ToString();
        }

        /// <summary>Command line for image / XEX / stealth files. The "--" keeps a file name that starts with a hyphen from being read as an option.</summary>
        public string CommandLine(IEnumerable<string> inputFiles)
        {
            string s = Join(Arguments());
            var files = (inputFiles ?? Enumerable.Empty<string>()).Select(Quote).ToList();
            return (s.Length > 0 ? s + " " : "") + "--" + (files.Count > 0 ? " " + string.Join(" ", files) : "");
        }

        /// <summary>
        /// Command line for every image in a folder, as the GUI's "Directory" input builds it: --match (a pattern list such as
        /// "*.iso,*.000"; the GUI puts a comma in front of a single pattern, which abgx360 skips), --rec for sub-folders, then
        /// --dir and the folder.
        /// </summary>
        public string CommandLineForFolder(string folder, bool subFolders, string match)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Choose a folder.");
            var a = Arguments();
            if (!string.IsNullOrWhiteSpace(match)) { a.Add("--match"); a.Add(match.IndexOf(',') < 0 ? "," + match.Trim() : match.Trim()); }
            if (subFolders) a.Add("--rec");
            a.Add("--dir"); a.Add(folder);
            return Join(a);
        }

        /// <summary>Command line for a disc in a DVD drive (abgx360 never writes to a drive; the GUI adds -w as well).</summary>
        public string CommandLineForDrive(char driveLetter)
        {
            if (!char.IsLetter(driveLetter) || driveLetter > 'z') throw new ArgumentException("A drive letter is A-Z.");
            var a = Arguments();
            if (!a.Contains("--nowrite")) a.Add("--nowrite");
            a.Add("--dvd"); a.Add(char.ToUpperInvariant(driveLetter).ToString());
            return Join(a);
        }

        /// <summary>
        /// The same options for a run inside this program: HTML output (the report is shown as it arrives, coloured), no window
        /// to pause or maximize - like the GUI's "HTML File" output.
        /// </summary>
        public AbgxOptions ForApp()
        {
            var o = Clone();
            o.Html = true;
            o.PauseWhenDone = false;
            o.Maximize = false;
            return o;
        }

        public AbgxOptions Clone() => (AbgxOptions)MemberwiseClone();

        /// <summary>Check a disc image without changing it: full verification, nothing written, nothing rebuilt.</summary>
        public static AbgxOptions VerifyOnly() => new AbgxOptions { NoWrite = true, Fix = AutoFix.Never, Rebuilding = Rebuild.Never, PauseWhenDone = true };

        /// <summary>Check and repair stealth files for burning (AutoFix level 3, the GUI's Quickstart advice for patching with verified files).</summary>
        public static AbgxOptions VerifyAndFix() => new AbgxOptions { Fix = AutoFix.Always, PauseWhenDone = true };

        /// <summary>The abgx360 GUI's own defaults for its CLI window: abgx360's defaults, the window maximized and kept open.</summary>
        public static AbgxOptions GuiDefaults() => new AbgxOptions { PauseWhenDone = true, Maximize = true };

        // ---------------------------------------------------------------- remembered settings

        /// <summary>The options as "Name=value" lines (only what differs from abgx360's defaults).</summary>
        public string Save()
        {
            var d = new AbgxOptions();
            var sb = new StringBuilder();
            foreach (var f in typeof(AbgxOptions).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = f.GetValue(this), dv = f.GetValue(d);
                if (Equals(v, dv)) continue;
                sb.Append(f.Name).Append('=').Append(v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture).Replace("\r", "").Replace("\n", "")).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Options saved by <see cref="Save"/>; unknown or unreadable lines are skipped.</summary>
        public static AbgxOptions Load(string saved)
        {
            var o = new AbgxOptions();
            if (string.IsNullOrEmpty(saved)) return o;
            foreach (string line in saved.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var f = typeof(AbgxOptions).GetField(line.Substring(0, eq), BindingFlags.Public | BindingFlags.Instance);
                if (f == null) continue;
                string s = line.Substring(eq + 1);
                try
                {
                    var t = Nullable.GetUnderlyingType(f.FieldType) ?? f.FieldType;
                    object v = s.Length == 0 && (f.FieldType == typeof(string) || Nullable.GetUnderlyingType(f.FieldType) != null) ? null
                             : t.IsEnum ? Enum.Parse(t, s) : t == typeof(string) ? s : Convert.ChangeType(s, t, CultureInfo.InvariantCulture);
                    if (t.IsEnum && !Enum.IsDefined(t, v)) continue;
                    f.SetValue(o, v);
                }
                catch { }
            }
            return o;
        }
    }
}
