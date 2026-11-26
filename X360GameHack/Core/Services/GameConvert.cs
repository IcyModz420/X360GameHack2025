using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;

namespace X360GameHack.Core.Services
{
    /// <summary>What a conversion makes and how.</summary>
    public sealed class GameConvertOptions
    {
        public GameFormat To = GameFormat.Package;
        /// <summary>
        /// For a package: padding, naming, name / IDs / picture and the X360PkgTool unlock. For every target: <see cref="GodJobOptions.Target"/>
        /// and <see cref="GodJobOptions.UnlockXex"/> decide whether the game's XEX is unlocked / converted for a devkit (xextool, on a copy).
        /// </summary>
        public GodJobOptions God = new GodJobOptions();
        /// <summary>GOD to disc image: compact (god2iso) or every sector in place; the CreateIsoGood fix.</summary>
        public GodIsoOptions Iso = new GodIsoOptions();
        /// <summary>To a folder: leave out $SystemUpdate (a hacked console must never install it).</summary>
        public bool SkipSystemUpdate = true;
        /// <summary>
        /// To an image (Xbox 360, XEX unchanged): run abgx360 AutoFix on the new image afterwards, which adds the stealth sectors
        /// (SS, DMI, PFI, video) from the verified database - see <see cref="GameConvert.MadeHereNote"/>.
        /// </summary>
        public bool StealthPatch;
        /// <summary>abgx360's questions during the stealth patch (true = yes); null answers "no" to everything.</summary>
        public Func<string, bool> Ask;
    }

    public sealed class GameConvertResult
    {
        public GameFormat From, To;
        /// <summary>The package header, the .iso file or the folder written.</summary>
        public string Output;
        public GodBuildResult Package;
        public int Files;
        public string Notes = "";
        /// <summary>What abgx360 said about the new image when it was stealth patched (null when it was not).</summary>
        public AbgxVerdict Stealth;
        /// <summary>abgx360's report of the stealth patch (its HTML page).</summary>
        public string StealthReport;
    }

    /// <summary>
    /// Any form of a game to any other: a disc image, a Games on Demand package or its XEX version (an extracted folder with
    /// default.xex / default.xbe). The source is only read; the game's XEX is patched (unlocked, devkit form) on a copy when the
    /// options ask for it, whatever the target.
    ///   to a package : ISO2God (GodJobs) - from an image, from a folder, or from another package (re-made, e.g. for a devkit);
    ///   to an image  : from a folder as extract-xiso -c builds it; from a package as god2iso does (compact or every sector in
    ///                  place); from an image (or a package with a patched XEX) rewritten as extract-xiso -r does;
    ///   to a folder  : every file extracted (from an image or the disc inside a package), or a folder copied.
    /// </summary>
    public static class GameConvert
    {
        public static async Task<GameConvertResult> RunAsync(string source, string output, GameConvertOptions o, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            var from = GameSources.FormatOf(source);
            var r = new GameConvertResult { From = from, To = o.To };
            if (o.To == GameFormat.Package)
            {
                r.Package = await GodJobs.RunAsync(source, output, o.God, progress, ct);
                r.Output = r.Package.HeaderPath;
                r.Files = r.Package.DataFiles;
                r.Notes = r.Package.Notes;
                return r;
            }

            string full = Path.GetFullPath(output).TrimEnd('\\');
            string src = Path.GetFullPath(source).TrimEnd('\\');
            if (string.Equals(full, src, StringComparison.OrdinalIgnoreCase) || full.StartsWith(src + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The result cannot go into the game it is made from - choose another place.");
            if (File.Exists(full) || (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()))
                throw new IOException("\"" + full + "\" already exists - choose a new name.");

            var info = o.God.Build.Info ?? (from == GameFormat.Folder ? GodTitleInfo.FromFolder(source) : await Task.Run(() => GodTitleInfo.FromImage(source, ct), ct));
            string work = Path.Combine(Path.GetTempPath(), "X360GameHack", "convert-" + Guid.NewGuid().ToString("N"));
            try
            {
                Dictionary<string, string> swap = null;
                bool patch = info.Xbox360 && GodTargets.PatchesXex(o.God.Target, o.God.UnlockXex);
                if (patch)
                {
                    swap = await GodTargets.PatchXexAsync(source, o.God.Target, o.God.UnlockXex, Path.Combine(work, "xex"), ct);
                    if (swap.Count == 0) swap = null;
                    else r.Notes = "XEX " + (o.God.UnlockXex ? "unlocked and " : "") + (o.God.Target == GodTarget.Devkit ? "converted to devkit" : "kept retail") + " (xextool): " + string.Join(", ", swap.Keys) + ". ";
                }

                if (o.To == GameFormat.Image)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    if (from == GameFormat.Folder)
                    {
                        string exe = File.Exists(Path.Combine(src, "default.xex")) ? Path.Combine(src, "default.xex") : Path.Combine(src, "default.xbe");
                        await Task.Run(() => XisoBuilder.CreateFromFolder(src, full, false, swap, File.GetLastWriteTimeUtc(exe), progress, ct), ct);
                        r.Notes += "Built like extract-xiso -c. ";
                    }
                    else if (from == GameFormat.Package && swap == null)
                    {
                        var g = GodPackage.Open(source);
                        var res = await Task.Run(() => g.ConvertToIso(full, o.Iso, progress, ct), ct);
                        r.Notes += res.Compacted ? "Compact, as god2iso writes it. "
                                 : o.Iso.Layout == GodIsoLayout.Compact ? "Nothing to compact (the package has no empty stretch) - every sector in its place. "
                                 : "Every sector in its place. ";
                    }
                    else
                    {
                        await Task.Run(() => XisoBuilder.Rewrite(source, full, false, swap, progress, ct), ct);
                        r.Notes += "Rewritten like extract-xiso -r (every file packed). ";
                    }
                    using (var x = XisoImage.Open(full)) r.Files = Count(x.ReadRoot());
                    if (o.StealthPatch) await StealthPatchAsync(full, info, swap != null, o, r, progress, ct);
                }
                else
                {
                    Directory.CreateDirectory(full);
                    if (from == GameFormat.Folder)
                        await Task.Run(() => CopyFolder(src, full, o.SkipSystemUpdate, progress, ct), ct);
                    else
                        await Task.Run(() => { using (var x = GameSources.OpenImage(source)) x.ExtractAll(full, o.SkipSystemUpdate, progress, ct); }, ct);
                    if (swap != null)
                        foreach (var kv in swap) File.Copy(kv.Value, PathSafety.CombineUnder(full, kv.Key), true);
                    r.Files = Directory.GetFiles(full, "*", SearchOption.AllDirectories).Length;
                    if (o.SkipSystemUpdate) r.Notes += "$SystemUpdate left out. ";
                }
                r.Output = full;
                r.Notes = r.Notes.Trim();
                return r;
            }
            catch
            {
                // nothing half-made is left behind
                try { if (o.To == GameFormat.Image && File.Exists(full)) File.Delete(full); } catch { }
                try { if (o.To == GameFormat.Folder && Directory.Exists(full)) Directory.Delete(full, true); } catch { }
                throw;
            }
            finally { try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { } }
        }

        private static int Count(List<XisoEntry> l) => l.Sum(e => e.IsDirectory ? Count(e.Children) : 1);

        /// <summary>The size abgx360 requires of a game partition before it rebuilds one (XGD2; an XGD3 one is larger).</summary>
        public const long FullGamePartition = 7307001856L;

        /// <summary>What every image made here is, and why it is meant for RGH / JTAG consoles.</summary>
        public const string MadeHereNote =
            "Images made here are for RGH / JTAG consoles (as Games on Demand or extracted) and emulators. Only the game partition is " +
            "written - no stealth sectors (SS, DMI, PFI, video) - so a console with a flashed DVD drive will not start one from a burned " +
            "disc. abgx360 can add those from its verified database only to a complete, unaltered game partition with the game's disc " +
            "XEX: for example a package made without padding removal, converted back with \"Every sector in place\". A repacked image " +
            "is shorter than the disc and lacks its random padding (abgx360 says it would not boot even with stealth files), a Games on " +
            "Demand edition has an XEX no disc has, and a patched XEX is not in the database - those are reported and left as they are.";

        /// <summary>
        /// Runs abgx360 AutoFix on a new image (in place: abgx360 rebuilds it into a full image with its stealth sectors from the
        /// database). Skipped - with the reason in the notes - for original Xbox games, a patched XEX, missing abgx360 or too
        /// little free space for abgx360's rebuild.
        /// </summary>
        private static async Task StealthPatchAsync(string iso, GodTitleInfo info, bool xexPatched, GameConvertOptions o, GameConvertResult r, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            if (!info.Xbox360) { r.Notes += "Not stealth patched: abgx360 has no verified files for original Xbox games made into images. "; return; }
            if (xexPatched) { r.Notes += "Not stealth patched: the XEX is patched, so abgx360's database has no match for it. "; return; }
            // the game's own XEX, as abgx360 reads it: a Games on Demand / digital edition never came on a disc
            XexFile xex = null;
            try
            {
                using (var img = XisoImage.Open(iso))
                {
                    var e = img.ReadRoot().FirstOrDefault(x => !x.IsDirectory && x.Name.Equals("default.xex", StringComparison.OrdinalIgnoreCase));
                    if (e != null) xex = XexFile.ReadHeader(img.ReadHead(e, 0x10000));
                }
            }
            catch { }
            if (xex != null && xex.Valid && !xex.AllowsDisc)
            {
                r.Notes += "Not stealth patched: this is the game's Games on Demand edition (its XEX only runs as a package, media types 0x" + xex.AllowedMediaTypes.ToString("X8") + "), which no disc has - abgx360's database has nothing to match. ";
                return;
            }
            // abgx360 rebuilds only a complete game partition (it refuses one that is short; 7,307,001,856 bytes for XGD2, more for XGD3)
            long len = new FileInfo(iso).Length;
            if (len < FullGamePartition)
            {
                r.Notes += "Not stealth patched: the image is not a complete game partition (" + FileSniffer.FormatSize(len) + "; a disc's is at least " + FileSniffer.FormatSize(FullGamePartition) +
                           "), so abgx360 cannot rebuild it - images made here are for RGH / JTAG. ";
                return;
            }
            var st = AbgxSetup.Check();
            if (st.Exe == null) { r.Notes += "Not stealth patched: abgx360 is not set up (abgx360 tab > Set up abgx360). "; return; }
            try { AbgxSetup.EnsureFixFile(); AbgxSetup.CopyVideoFiles(); } catch (Exception ex) { Log.Warn("abgx360 fix file: " + ex.Message); }
            // abgx360's default rebuild writes the full image (up to ~8.7 GB) next to this one before it removes this one
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(iso)));
                long need = 9L * 1024 * 1024 * 1024;
                if (drive.IsReady && drive.AvailableFreeSpace < need)
                {
                    r.Notes += "Not stealth patched: abgx360 needs about 9 GB free on " + drive.Name.TrimEnd('\\') + " to rebuild the image. ";
                    return;
                }
            }
            catch (ArgumentException) { }
            // abgx360 finds default.xex only when its entry has Microsoft's attribute (0x80); extract-xiso style images have 0x20
            int attrs = await Task.Run(() => XisoAttributes.UseMicrosoftAttributes(iso), ct);
            if (attrs > 0) r.Notes += "File attributes set as Microsoft's discs have them (files 0x80, folders 0x90) so abgx360 can read the game. ";
            var opt = new AbgxOptions { XexImages = AbgxOptions.Images.Never }.ForApp();
            string args = opt.CommandLine(new[] { iso });
            Log.Info("abgx360 " + args);
            progress?.Report(new TransferProgress { File = "abgx360: stealth patch", Total = 1, Done = 0 });
            var res = await AbgxRunner.RunAsync(st.Exe, args, Path.GetDirectoryName(iso), true, null,
                line => progress?.Report(new TransferProgress { File = "abgx360: " + (line.Length > 80 ? line.Substring(0, 80) : line), Total = 1, Done = 0 }),
                o.Ask, ct);
            ct.ThrowIfCancellationRequested();
            r.Stealth = res.Verdict;
            r.StealthReport = res.Output;
            var v = res.Verdict;
            if (v.AutoFixed && v.GameCrcMatches) r.Notes += "Stealth patched by abgx360 and its game data matches the verified database: the same as the original disc. ";
            else if (v.AutoFixed) r.Notes += "Stealth patched by abgx360 (" + v.Summary() + ") - RGH / JTAG only. ";
            else r.Notes += "abgx360 could not stealth patch it (" + v.Summary() + ") - RGH / JTAG only. ";
        }

        private static void CopyFolder(string from, string to, bool skipUpdate, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            var files = Directory.GetFiles(from, "*", SearchOption.AllDirectories)
                .Where(f => !skipUpdate || f.Substring(from.Length).IndexOf(XisoImage.SystemUpdate, StringComparison.OrdinalIgnoreCase) < 0).ToList();
            var state = new TransferProgress { Total = files.Sum(f => new FileInfo(f).Length) };
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                string target = PathSafety.CombineUnder(to, f.Substring(from.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                ContentInstaller.CopyFile(f, target, progress, state, ct);
            }
        }

        /// <summary>The name a result gets: the game's name (" (Disc N)" for a multi-disc game), or the source's file / folder name.</summary>
        public static string OutputName(string source, GodTitleInfo info, bool byGameName)
        {
            string n = byGameName && info != null && !string.IsNullOrWhiteSpace(info.Name) ? info.Name.Trim()
                     : Directory.Exists(source) ? Path.GetFileName(source.TrimEnd('\\')) : Path.GetFileName(source);
            if (byGameName && info != null && info.DiscCount > 1 && n.IndexOf("disc", StringComparison.OrdinalIgnoreCase) < 0) n += " (Disc " + info.DiscNumber + ")";
            if (!Directory.Exists(source) && n.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            return PathSafety.SafeName(n);
        }
    }
}
