using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.Core.Formats
{
    /// <summary>What identifies a game to a title update: which game, which pressing of the disc, which version.</summary>
    public sealed class TuGame
    {
        public string Path = "";
        /// <summary>Where the numbers were read from, in plain words.</summary>
        public string From = "";
        public uint TitleId, MediaId, Version, BaseVersion;
        /// <summary>False when the file only names the game (a Games on Demand header) and holds no default.xex to read a version from.</summary>
        public bool VersionKnown = true;
    }

    /// <summary>What a title update package says about itself.</summary>
    public sealed class TuPackage
    {
        public string Path = "";
        public bool IsTitleUpdate;
        public uint ContentType, TitleId, MediaId, PackageVersion, PackageBaseVersion;
        /// <summary>True when the package holds a default.xexp; its two versions are the exact "from" and "to".</summary>
        public bool HasPatch;
        public uint SourceVersion, TargetVersion;
        public int Files;
        public List<string> Patches = new List<string>();
        /// <summary>Folder on the console's drive (below its root) and the file name it must have there.</summary>
        public string InstallDir = "", InstallName = "";
        public bool GoesToCache;

        /// <summary>The game version this update patches.</summary>
        public uint FromVersion => HasPatch ? SourceVersion : PackageBaseVersion;
    }

    public enum TuFit { Fits, FitsIdsOnly, AlreadyApplied, WrongGame, WrongDisc, WrongVersion, NotAnUpdate, MediaIdZeroed }

    public sealed class TuVerdict
    {
        public TuFit Fit;
        public string Headline = "";
        public List<string> Lines = new List<string>();
        public bool Ok => Fit == TuFit.Fits || Fit == TuFit.FitsIdsOnly;
    }

    /// <summary>
    /// Tells whether a title update was made for a given game, the way the console decides it: same Title ID, same
    /// Media ID, and the update's patch (default.xexp) starts from exactly the version of the game's default.xex.
    /// The game can be given as its default.xex, as a disc image (the XEX is read out of it), or as a package.
    /// Nothing is changed in any file.
    /// </summary>
    public static class TitleUpdateCheck
    {
        /// <summary>An XEX version number the way xextool and the console print it, e.g. 0.0.18.2.</summary>
        public static string VersionText(uint v) => (v >> 28) + "." + ((v >> 24) & 0xF) + "." + ((v >> 8) & 0xFFFF) + "." + (v & 0xFF);

        /// <summary>The title-update number inside a version (the third part): 0 for the game as sold.</summary>
        public static int UpdateNumber(uint v) => (int)((v >> 8) & 0xFFFF);

        public static TuGame GameFrom(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("That file does not exist.", path);
            if (XisoImage.Probe(path) != null) return GameFromIso(path);
            var pkg = TryPackage(path);
            if (pkg != null) return GameFromPackage(path, pkg);
            return GameFromXex(path, path, System.IO.Path.GetFileName(path));
        }

        private static StfsPackage TryPackage(string path)
        {
            try { var p = StfsPackage.Read(path); return p.Valid ? p : null; } catch { return null; }
        }

        private static TuGame GameFromXex(string xexPath, string shownPath, string from) => FromParsed(XexFile.Read(xexPath), shownPath, from);

        private static TuGame FromParsed(XexFile x, string shownPath, string from)
        {
            if (!x.Valid || x.Magic != "XEX2")
                throw new InvalidDataException("That is not an Xbox 360 game file (default.xex), disc image or package.");
            if ((x.ModuleFlags & 0x70) != 0)
                throw new InvalidDataException("That is a title update's patch file (.xexp), not the game. Choose the game's default.xex or its disc image.");
            if (x.ExecutionInfoOffset < 0)
                throw new InvalidDataException("That XEX does not say which game it belongs to (it has no execution info), so it cannot be matched to a title update.");
            return new TuGame { Path = shownPath, From = from, TitleId = x.TitleId, MediaId = x.MediaId, Version = x.Version, BaseVersion = x.BaseVersion };
        }

        /// <summary>How much of a default.xex is read to find its IDs and version. The header that holds them sits at the very start.</summary>
        private const int HeadBytes = 0x10000;

        private static TuGame GameFromIso(string path)
        {
            using (var img = XisoImage.Open(path))
            {
                var root = img.ReadRoot();
                var e = root.FirstOrDefault(x => !x.IsDirectory && string.Equals(x.Name, "default.xex", StringComparison.OrdinalIgnoreCase));
                if (e == null)
                {
                    if (root.Any(x => string.Equals(x.Name, "default.xbe", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("That is an original Xbox disc image. Title updates here are for Xbox 360 games.");
                    throw new InvalidDataException("That disc image has no default.xex in its top folder, so it is not an Xbox 360 game disc.");
                }
                // only the start of the file is read, straight from the image into memory - nothing is copied out
                return FromParsed(XexFile.ReadHeader(img.ReadHead(e, HeadBytes), path), path, "default.xex inside the disc image");
            }
        }

        private static TuGame GameFromPackage(string path, StfsPackage pkg)
        {
            if (pkg.ContentType == 0x000B0000)
                throw new InvalidDataException("That is a title update, not the game. Choose the game's default.xex, its disc image or its package.");
            // An arcade or installed game package can hold its default.xex: then the version can be read too.
            byte[] inner = null;
            try
            {
                using (var v = StfsVolume.Open(path))
                {
                    var e = v.Entries.FirstOrDefault(x => !x.IsDirectory && string.Equals(x.Path.TrimStart('\\', '/'), "default.xex", StringComparison.OrdinalIgnoreCase));
                    if (e != null) inner = v.ReadFile(e);
                }
            }
            catch { /* a Games on Demand header has no file table of its own */ }
            if (inner != null) return FromParsed(XexFile.ReadHeader(inner, path), path, "default.xex inside the package");
            return new TuGame { Path = path, From = "the package header (it names the game but holds no default.xex)", TitleId = pkg.TitleId, MediaId = pkg.MediaId, VersionKnown = false };
        }

        public static TuPackage ReadPackage(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("That file does not exist.", path);
            var info = StfsPackage.Read(path);
            if (!info.Valid) throw new InvalidDataException("That file is not an Xbox 360 package, so it is not a title update.");
            var tu = new TuPackage
            {
                Path = path, ContentType = info.ContentType, IsTitleUpdate = info.ContentType == 0x000B0000,
                TitleId = info.TitleId, MediaId = info.MediaId, PackageVersion = info.Version, PackageBaseVersion = info.BaseVersion,
                InstallDir = info.InstallDir, InstallName = info.InstallName, GoesToCache = info.IsCacheTitleUpdate,
            };
            using (var v = StfsVolume.Open(path))
            {
                foreach (var e in v.Entries)
                {
                    if (e.IsDirectory) continue;
                    tu.Files++;
                    string rel = e.Path.Replace('/', '\\').TrimStart('\\');
                    if (!rel.EndsWith(".xexp", StringComparison.OrdinalIgnoreCase)) continue;
                    tu.Patches.Add(rel);
                    if (!string.Equals(rel, "default.xexp", StringComparison.OrdinalIgnoreCase)) continue;
                    if (TryReadPatch(v.ReadFile(e), out uint source, out uint target)) { tu.HasPatch = true; tu.SourceVersion = source; tu.TargetVersion = target; }
                }
            }
            return tu;
        }

        /// <summary>
        /// Reads the two versions out of a delta patch (.xexp): optional header 0x5FF holds the version the patch
        /// produces (+4) and the version it must be applied to (+8).
        /// </summary>
        public static bool TryReadPatch(byte[] xexp, out uint sourceVersion, out uint targetVersion)
        {
            sourceVersion = targetVersion = 0;
            if (xexp == null || xexp.Length < 0x20 || xexp[0] != 'X' || xexp[1] != 'E' || xexp[2] != 'X' || xexp[3] != '2') return false;
            uint count = BE.U32(xexp, 0x14);
            if (count > 256) return false;
            for (int i = 0; i < count; i++)
            {
                int at = 0x18 + i * 8;
                if (at + 8 > xexp.Length) return false;
                if (BE.U32(xexp, at) != 0x000005FF) continue;
                long d = BE.U32(xexp, at + 4);
                if (d + 12 > xexp.Length) return false;
                targetVersion = BE.U32(xexp, (int)d + 4);
                sourceVersion = BE.U32(xexp, (int)d + 8);
                return true;
            }
            return false;
        }

        public static TuVerdict Compare(TuGame game, TuPackage tu)
        {
            var v = new TuVerdict();
            string gameId = game.TitleId.ToString("X8"), tuId = tu.TitleId.ToString("X8");
            if (!tu.IsTitleUpdate)
            {
                v.Fit = TuFit.NotAnUpdate;
                v.Headline = "That package is not a title update.";
                v.Lines.Add("Its content type is " + tu.ContentType.ToString("X8") + "; a title update is 000B0000.");
                return v;
            }
            if (tu.TitleId != game.TitleId)
            {
                v.Fit = TuFit.WrongGame;
                v.Headline = "Wrong game - this update will not load.";
                v.Lines.Add("The update is for Title ID " + tuId + "; your game is " + gameId + ".");
                return v;
            }
            if (game.MediaId == 0)
            {
                v.Fit = TuFit.MediaIdZeroed;
                v.Headline = "Your game's Media ID has been zeroed - no title update can match it.";
                v.Lines.Add("xextool -r z (and -r a, which includes it) wipes the Media ID. Use an untouched default.xex of the game.");
                return v;
            }
            if (tu.MediaId != 0 && tu.MediaId != game.MediaId)
            {
                v.Fit = TuFit.WrongDisc;
                v.Headline = "Right game, wrong disc - this update will not load.";
                v.Lines.Add("The update is for Media ID " + tu.MediaId.ToString("X8") + "; your disc is " + game.MediaId.ToString("X8") + ".");
                v.Lines.Add("On XboxUnity take the update listed under " + game.MediaId.ToString("X8") + ".");
                return v;
            }
            if (!game.VersionKnown)
            {
                v.Fit = TuFit.FitsIdsOnly;
                v.Headline = "Right game and right disc.";
                v.Lines.Add("Title ID and Media ID match. The game's version could not be read from this file, so that part is not checked - " +
                            "use the game's default.xex or disc image for the full check.");
                AddStep(v, tu);
                return v;
            }
            if (tu.HasPatch && game.Version == tu.TargetVersion && tu.TargetVersion != tu.SourceVersion)
            {
                v.Fit = TuFit.AlreadyApplied;
                v.Headline = "This game file already has this update merged into it.";
                v.Lines.Add("Your default.xex is version " + VersionText(game.Version) + ", which is what this update produces. Installing the package as well would do nothing: " +
                            "the console only applies it to version " + VersionText(tu.SourceVersion) + ".");
                return v;
            }
            if (game.Version != tu.FromVersion)
            {
                v.Fit = TuFit.WrongVersion;
                v.Headline = "Right game and disc, wrong version - this update will not load on this file.";
                v.Lines.Add("The update patches game version " + VersionText(tu.FromVersion) + "; your default.xex is version " + VersionText(game.Version) + ".");
                if (UpdateNumber(game.Version) > UpdateNumber(tu.FromVersion))
                    v.Lines.Add("Your file has already been updated (title update " + UpdateNumber(game.Version) + " is merged into it). A console applies updates to the game as sold - use the original default.xex.");
                return v;
            }
            v.Fit = TuFit.Fits;
            v.Headline = "This update fits your game.";
            v.Lines.Add("Title ID " + gameId + ", Media ID " + game.MediaId.ToString("X8") + " and game version " + VersionText(game.Version) + " all match.");
            AddStep(v, tu);
            return v;
        }

        private static void AddStep(TuVerdict v, TuPackage tu)
        {
            if (tu.HasPatch)
                v.Lines.Add("It takes the game from version " + VersionText(tu.SourceVersion) + " to " + VersionText(tu.TargetVersion) + " (title update " + UpdateNumber(tu.TargetVersion) + ").");
            if (tu.MediaId == 0) v.Lines.Add("Note: the update does not name a disc pressing (Media ID 00000000).");
        }

        /// <summary>Where the package belongs on a console drive, as a path below the drive's root.</summary>
        public static string Destination(TuPackage tu) => tu.InstallDir + "\\" + tu.InstallName;
    }
}
