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
    /// <summary>What "Play in Xenia" is going to copy from the console, and what it will start.</summary>
    public sealed class XeniaPlan
    {
        public string Name = "";
        public string LocalRoot = "";
        public string LaunchPath = "";
        public int Files;
        public long TotalBytes, MissingBytes;
        /// <summary>True when the file to start is a package or disc image (checked after the copy), not a .xex.</summary>
        public bool CheckIsGame;
        public readonly List<XeniaFile> Missing = new List<XeniaFile>();
        public readonly List<string> Folders = new List<string>();
    }

    public sealed class XeniaFile
    {
        public string RemoteDir, LocalDir;
        public RemoteItem Item;
    }

    /// <summary>
    /// Copies a game from the console (over XBDM or FTP) into a folder on this PC and works out which file Xenia
    /// has to open. Files already on the PC with the same size are not copied again, so an interrupted copy
    /// carries on where it stopped and a second play starts at once. Nothing on the console is changed.
    /// </summary>
    public static class XeniaPlay
    {
        /// <summary>Room kept free on the PC drive on top of the game itself.</summary>
        public const long SpareBytes = 256L * 1024 * 1024;

        public static string GamesDir
        {
            get
            {
                string set = AppSettings.Current.XeniaGamesDir;
                if (!string.IsNullOrWhiteSpace(set)) return set.Trim();
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "X360GameHack", "Xenia Games");
            }
        }

        private static bool IsXex(string name) => name.EndsWith(".xex", StringComparison.OrdinalIgnoreCase);
        private static bool IsXbe(string name) => name.EndsWith(".xbe", StringComparison.OrdinalIgnoreCase);

        private static string LastPart(string path)
        {
            string t = (path ?? "").TrimEnd('\\', '/');
            int cut = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
            return (cut >= 0 ? t.Substring(cut + 1) : t).TrimEnd(':');
        }

        /// <summary>A drive list or the top of a drive is not a game: refuse it rather than copy a whole hard drive.</summary>
        private static bool IsTopLevel(IRemoteFs fs, string dir)
        {
            if (string.IsNullOrEmpty(dir) || dir == fs.Root) return true;
            string parent = fs.Parent(dir);
            return string.IsNullOrEmpty(parent) || parent == fs.Root || parent == dir;
        }

        /// <param name="dir">The folder the browser is showing.</param>
        /// <param name="selected">The selected entry in it, or null to mean "this folder".</param>
        /// <param name="originalXbox">An original Xbox game for xemu: started from its default.xbe (or a disc image), not a .xex.</param>
        public static async Task<XeniaPlan> PlanAsync(IRemoteFs fs, string dir, RemoteItem selected, string gamesDir, CancellationToken ct, Action<XeniaPlan> onProgress = null,
                                                      bool originalXbox = false)
        {
            var plan = new XeniaPlan();
            Func<string, bool> isStart = originalXbox ? (Func<string, bool>)IsXbe : IsXex;
            string startName = originalXbox ? "default.xbe" : "default.xex", button = originalXbox ? "Play in xemu" : "Play in Xenia";
            if (selected != null && !selected.IsDirectory && !isStart(selected.Name))
            {
                if (originalXbox && !selected.Name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Select the game's folder (the one with default.xbe in it), its .xbe, or its disc image (.iso).");
                // a package (Games on Demand, arcade game) or a disc image: the file itself, plus its ".data" folder
                var here = await fs.ListAsync(dir, ct);
                plan.Name = selected.Name;
                plan.LocalRoot = PathSafety.CombineUnder(gamesDir, PathSafety.SafeName((LastPart(fs.Parent(dir)) + " " + LastPart(dir) + " " + selected.Name).Trim()));
                plan.LaunchPath = Path.Combine(plan.LocalRoot, PathSafety.SafeName(selected.Name));
                plan.CheckIsGame = !originalXbox;
                Add(plan, dir, plan.LocalRoot, selected);
                var data = here.FirstOrDefault(i => i.IsDirectory && i.Name.Equals(selected.Name + ".data", StringComparison.OrdinalIgnoreCase));
                if (data != null) await WalkAsync(fs, fs.Combine(dir, data.Name), Path.Combine(plan.LocalRoot, PathSafety.SafeName(data.Name)), plan, ct, onProgress: onProgress);
            }
            else
            {
                string folder = selected != null && selected.IsDirectory ? fs.Combine(dir, selected.Name) : dir;
                if (IsTopLevel(fs, folder))
                    throw new InvalidOperationException("Open the game's own folder (the one with " + startName + " in it), or select it, and press " + button + " again.");
                var items = await fs.ListAsync(folder, ct);
                RemoteItem start;
                if (selected != null && !selected.IsDirectory) start = selected;
                else
                {
                    var starts = items.Where(i => !i.IsDirectory && isStart(i.Name)).ToList();
                    start = starts.FirstOrDefault(i => i.Name.Equals(startName, StringComparison.OrdinalIgnoreCase)) ?? (starts.Count == 1 ? starts[0] : null);
                    if (start == null)
                    {
                        // an original Xbox game in Xenia (or the other way round): say what it is and what plays it
                        if (!originalXbox && starts.Count == 0 && items.Any(i => !i.IsDirectory && IsXbe(i.Name)))
                            throw new InvalidOperationException(LastPart(folder) + " is an original Xbox game (it has a .xbe, no .xex). Xenia plays Xbox 360 games only - " +
                                                                "use Play in xemu for it (on an original Xbox drive Drive Explorer shows that button).");
                        if (originalXbox && starts.Count == 0 && items.Any(i => !i.IsDirectory && IsXex(i.Name)))
                            throw new InvalidOperationException(LastPart(folder) + " is an Xbox 360 game (it has a .xex). xemu plays original Xbox games only - use Play in Xenia for it.");
                        throw new InvalidOperationException(starts.Count == 0
                            ? "There is no " + startName + " in " + folder + ". Select the game's folder" + (originalXbox ? ", its .xbe, or its disc image." : ", a .xex file, or a game package.")
                            : folder + " has several " + (originalXbox ? ".xbe" : ".xex") + " files and none is called " + startName + ". Select the one to start.");
                    }
                }
                plan.Name = LastPart(folder);
                plan.LocalRoot = PathSafety.CombineUnder(gamesDir, PathSafety.SafeName(plan.Name));
                plan.LaunchPath = Path.Combine(plan.LocalRoot, PathSafety.SafeName(start.Name));
                await WalkAsync(fs, folder, plan.LocalRoot, plan, ct, items, onProgress: onProgress);
            }
            return plan;
        }

        private static void Add(XeniaPlan plan, string remoteDir, string localDir, RemoteItem item)
        {
            plan.Files++;
            plan.TotalBytes += item.Size;
            var local = new FileInfo(Path.Combine(localDir, PathSafety.SafeName(item.Name)));
            if (local.Exists && local.Length == item.Size) return;
            plan.MissingBytes += item.Size;
            plan.Missing.Add(new XeniaFile { RemoteDir = remoteDir, LocalDir = localDir, Item = item });
        }

        private static async Task WalkAsync(IRemoteFs fs, string remoteDir, string localDir, XeniaPlan plan, CancellationToken ct, List<RemoteItem> known = null, int depth = 0, Action<XeniaPlan> onProgress = null)
        {
            ct.ThrowIfCancellationRequested();
            if (depth > 32) throw new InvalidOperationException("The folders under " + remoteDir + " are nested too deeply to be a game.");
            plan.Folders.Add(localDir);
            foreach (var it in known ?? await fs.ListAsync(remoteDir, ct))
            {
                if (it.IsDirectory)
                {
                    if (it.Name == "." || it.Name == "..") continue;
                    await WalkAsync(fs, fs.Combine(remoteDir, it.Name), Path.Combine(localDir, PathSafety.SafeName(it.Name)), plan, ct, null, depth + 1, onProgress);
                }
                else { Add(plan, remoteDir, localDir, it); onProgress?.Invoke(plan); }
            }
        }

        /// <summary>Null when the PC drive has room for what is still to copy, else the message to show.</summary>
        public static string SpaceProblem(XeniaPlan plan)
        {
            if (plan.MissingBytes == 0) return null;
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(plan.LocalRoot)));
                long free = drive.AvailableFreeSpace;
                if (free >= plan.MissingBytes + SpareBytes) return null;
                return "Not enough room on " + drive.Name + " for " + plan.Name + ": " + FileSniffer.FormatSize(plan.MissingBytes) + " still has to be copied and " +
                       FileSniffer.FormatSize(free) + " is free. Free some space, or choose another folder on the Xenia page (Setup tab).";
            }
            catch (ArgumentException) { return null; }      // a network path: no free-space figure to check
            catch (IOException) { return null; }
        }

        private sealed class Shifted : IProgress<TransferProgress>
        {
            public IProgress<TransferProgress> Inner; public long Base, Total;
            public void Report(TransferProgress value) => Inner?.Report(new TransferProgress { Total = Total, Done = Math.Min(Total, Base + value.Done), File = value.File });
        }

        /// <summary>Copies whatever the plan says is still missing.</summary>
        public static async Task FetchAsync(IRemoteFs fs, XeniaPlan plan, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            foreach (string f in plan.Folders) Directory.CreateDirectory(f);
            Directory.CreateDirectory(plan.LocalRoot);
            var shifted = new Shifted { Inner = progress, Total = plan.MissingBytes };
            foreach (var group in plan.Missing.GroupBy(m => m.RemoteDir + "\n" + m.LocalDir))
            {
                ct.ThrowIfCancellationRequested();
                var first = group.First();
                Directory.CreateDirectory(first.LocalDir);
                var items = group.Select(g => g.Item).ToList();
                await fs.DownloadAsync(items, first.RemoteDir, first.LocalDir, shifted, ct);
                shifted.Base += items.Sum(i => i.Size);
            }
            foreach (var m in plan.Missing)
            {
                var local = new FileInfo(Path.Combine(m.LocalDir, PathSafety.SafeName(m.Item.Name)));
                if (!local.Exists || local.Length != m.Item.Size)
                    throw new IOException(m.Item.Name + " did not arrive complete (" + (local.Exists ? local.Length : 0).ToString("N0") + " of " + m.Item.Size.ToString("N0") + " bytes). Press Play in Xenia again to carry on.");
            }
        }

        /// <summary>Null when the copied file is something Xenia can start, else why not.</summary>
        public static string NotAGame(XeniaPlan plan)
        {
            if (!File.Exists(plan.LaunchPath)) return plan.LaunchPath + " is missing after the copy.";
            if (!plan.CheckIsGame) return null;
            var head = new byte[4];
            using (var fs = File.OpenRead(plan.LaunchPath)) if (fs.Read(head, 0, 4) < 4) return plan.Name + " is empty.";
            string magic = System.Text.Encoding.ASCII.GetString(head);
            if (magic == "LIVE" || magic == "PIRS" || magic == "CON " || magic == "XEX2" || plan.LaunchPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) return null;
            return plan.Name + " is not a game package, a .xex or a disc image, so Xenia cannot start it.";
        }
    }
}
