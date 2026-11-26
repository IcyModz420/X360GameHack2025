using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;

namespace X360GameHack.Core.Services
{
    /// <summary>The console a Games on Demand package is made for.</summary>
    public enum GodTarget { Rgh = 0, Devkit = 1 }

    /// <summary>
    /// Makes a package right for its console with the two tools the program relies on for it (the originals are never changed):
    ///   xextool      - the game's XEX files (those in its root folder, the ones Bulk extract patches), on copies: limits removed
    ///                  ("unlocked": -r mrbdiyvklc, the same set Bulk extract uses) and retail (-m r) or devkit (-m d) form. A devkit
    ///                  only runs devkit XEX files, so for a devkit they are always converted.
    ///   X360PkgTool  - the finished package: the Packages page's "Unlock for RGH / exploited" (-z -b 1 -m 1) or "Unlock for devkit"
    ///                  (-z -b 1 -m 0) - purchase traces removed, marked as bought, retail or devkit form (the devkit form is signed
    ///                  with the devkit key X360PkgTool carries). It changes header fields only; that is checked every time.
    /// </summary>
    public static class GodTargets
    {
        public static string Name(GodTarget t) => t == GodTarget.Devkit ? "devkit" : "RGH / JTAG";

        public static string XexArgs(GodTarget t, bool unlock) => (unlock ? XexFile.SafeLimits + " " : "") + (t == GodTarget.Devkit ? "-m d" : "-m r");

        public static string PkgToolArgs(GodTarget t) => t == GodTarget.Devkit ? "-z -b 1 -m 0" : "-z -b 1 -m 1";

        /// <summary>Whether the XEX files go through xextool: always for a devkit, for RGH only when they are to be unlocked.</summary>
        public static bool PatchesXex(GodTarget t, bool unlockXex) => t == GodTarget.Devkit || unlockXex;

        /// <summary>
        /// Patched copies of the XEX files in the root of a disc image or game folder, in <paramref name="workFolder"/>:
        /// path inside the game -> patched copy. Empty for an original Xbox game (it has no XEX).
        /// </summary>
        public static async Task<Dictionary<string, string>> PatchXexAsync(string imageOrFolder, GodTarget target, bool unlock, string workFolder, CancellationToken ct)
        {
            Directory.CreateDirectory(workFolder);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(imageOrFolder))
            {
                foreach (var f in Directory.GetFiles(imageOrFolder, "*.xex"))
                {
                    string copy = Path.Combine(workFolder, Path.GetFileName(f));
                    File.Copy(f, copy, true);
                    map[Path.GetFileName(f)] = copy;
                }
            }
            else
            {
                using (var iso = GameSources.OpenImage(imageOrFolder))
                    foreach (var e in iso.ReadRoot().Where(e => !e.IsDirectory && e.Name.EndsWith(".xex", StringComparison.OrdinalIgnoreCase)))
                    {
                        string copy = PathSafety.CombineUnder(workFolder, e.Name);
                        iso.ExtractFile(e, copy, null, null, ct);
                        map[e.Name] = copy;
                    }
            }
            string args = XexArgs(target, unlock);
            foreach (var kv in map)
            {
                ct.ThrowIfCancellationRequested();
                var before = XexFile.Read(kv.Value);
                var ids = XexFile.ReadMediaIds(kv.Value);
                var r = await ToolRunner.RunAsync(Tools.XexTool, args + " " + ToolRunner.Q(kv.Value), quiet: true, ct: ct);
                if (!r.Success) throw new InvalidOperationException("xextool could not patch " + kv.Key + " (exit code " + r.ExitCode + "): " + LastLine(r.Output));
                string restored = XexFile.RestoreZeroedMediaIds(kv.Value, ids);       // never leave a zeroed media ID behind
                if (restored != null) Log.Warn(kv.Key + ": xextool zeroed the " + restored + " - restored.");
                var after = XexFile.Read(kv.Value);
                if (!after.Valid || after.TitleId != before.TitleId || after.MediaId != before.MediaId)
                    throw new InvalidDataException("xextool's output for " + kv.Key + " is not the same game any more - stopped.");
            }
            return map;
        }

        /// <summary>
        /// Runs the target's unlock on a finished package's header. X360PkgTool may change only header fields (license, flags,
        /// signature, header hash): the package's data descriptor, IDs, names and pictures must stay as they are, or the header
        /// is put back and this throws.
        /// </summary>
        public static async Task UnlockPackageAsync(string header, GodTarget target, CancellationToken ct)
        {
            byte[] before = File.ReadAllBytes(header);
            var r = await ToolRunner.RunAsync(Tools.PkgTool, PkgToolArgs(target) + " " + ToolRunner.Q(header), quiet: true, ct: ct);
            byte[] after = File.Exists(header) ? File.ReadAllBytes(header) : new byte[0];
            string problem = null;
            if (!r.Success) problem = "X360PkgTool could not unlock the package (exit code " + r.ExitCode + "): " + LastLine(r.Output);
            else if (after.Length != before.Length) problem = "X360PkgTool changed the header's size.";
            else if (!Same(before, after, 0x340, 0x3AD - 0x340)) problem = "X360PkgTool changed the package's IDs or data descriptor.";
            else if (!Same(before, after, 0x411, 0x1711 - 0x411) || !Same(before, after, 0x1712, after.Length - 0x1712)) problem = "X360PkgTool changed the package's names or pictures.";
            else
            {
                using (var sha = SHA1.Create())
                    if (!sha.ComputeHash(after, 0x344, after.Length - 0x344).SequenceEqual(after.Skip(0x32C).Take(20))) problem = "X360PkgTool left a wrong header hash.";
            }
            if (problem != null)
            {
                File.WriteAllBytes(header, before);
                throw new InvalidDataException(problem + " The package was left as it was built (not unlocked).");
            }
        }

        private static bool Same(byte[] a, byte[] b, int at, int count)
        {
            for (int i = 0; i < count; i++) if (a[at + i] != b[at + i]) return false;
            return true;
        }

        private static string LastLine(string output)
        {
            var lines = (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? "(no output)" : lines[lines.Length - 1].Trim();
        }
    }

    /// <summary>What one conversion to a package does besides the conversion itself.</summary>
    public sealed class GodJobOptions
    {
        public GodBuildOptions Build = new GodBuildOptions();
        public GodTarget Target = GodTarget.Rgh;
        /// <summary>xextool: remove the XEX files' limits ("unlock" them).</summary>
        public bool UnlockXex = true;
        /// <summary>X360PkgTool: the target's unlock on the finished package.</summary>
        public bool UnlockPackage = true;
    }

    /// <summary>A disc image or a game folder ("XEX version") made into a package for RGH or a devkit, in one go.</summary>
    public static class GodJobs
    {
        public static async Task<GodBuildResult> RunAsync(string imageOrFolder, string outputRoot, GodJobOptions o, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            bool folder = Directory.Exists(imageOrFolder);
            var info = o.Build.Info ?? (folder ? GodTitleInfo.FromFolder(imageOrFolder) : await Task.Run(() => GodTitleInfo.FromImage(imageOrFolder, ct), ct));
            o.Build.Info = info;
            string work = Path.Combine(Path.GetTempPath(), "X360GameHack", "god-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (info.Xbox360 && GodTargets.PatchesXex(o.Target, o.UnlockXex))
                {
                    var map = await GodTargets.PatchXexAsync(imageOrFolder, o.Target, o.UnlockXex, Path.Combine(work, "xex"), ct);
                    if (map.Count > 0) o.Build.Replacements = map;
                }
                GodBuildResult result;
                if (folder)
                {
                    long image = GodBuilder.EstimateFolderImageBytes(imageOrFolder, o.Build.Replacements);
                    string temp = GodBuilder.TempFolderFor(image, Path.GetDirectoryName(Path.GetFullPath(outputRoot).TrimEnd('\\')), Path.GetDirectoryName(Path.GetFullPath(imageOrFolder).TrimEnd('\\')));
                    if (temp == null) throw new IOException("No drive has room for the temporary disc image (" + FileSniffer.FormatSize(image) + ", and it must be able to hold files over 4 GB).");
                    result = await Task.Run(() => GodBuilder.FromFolder(imageOrFolder, outputRoot, temp, o.Build, progress, ct), ct);
                }
                else result = await Task.Run(() => GodBuilder.FromIso(imageOrFolder, outputRoot, o.Build, progress, ct), ct);

                if (o.UnlockPackage)
                {
                    await GodTargets.UnlockPackageAsync(result.HeaderPath, o.Target, ct);
                    GodPackage.Open(result.HeaderPath);      // still opens as a Games on Demand package (data untouched - checked above)
                }
                result.Notes = (result.Notes + " For " + GodTargets.Name(o.Target) + ": " +
                                (info.Xbox360 && GodTargets.PatchesXex(o.Target, o.UnlockXex) ? "XEX " + (o.UnlockXex ? "unlocked and " : "") + (o.Target == GodTarget.Devkit ? "converted to devkit" : "kept retail") + " (xextool), " : "") +
                                (o.UnlockPackage ? "package unlocked (X360PkgTool " + GodTargets.PkgToolArgs(o.Target) + ")." : "package not unlocked.")).Trim();
                return result;
            }
            finally { try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { } }
        }
    }
}
