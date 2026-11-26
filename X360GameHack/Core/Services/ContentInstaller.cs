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
    public enum InstallTarget { UsbDrive, ConsoleXbdm, ConsoleFtp }

    /// <summary>
    /// Installs STFS content (GOD, XBLA, DLC, title updates, saves, themes, ...) into the Xbox 360
    /// Content\{profile}\{titleid}\{type}\ layout. Games-on-Demand ".data" folders are copied too.
    /// </summary>
    public static class ContentInstaller
    {
        /// <summary>Finds STFS packages in files and (recursively) in folders.</summary>
        public static List<StfsPackage> Collect(IEnumerable<string> paths)
        {
            var list = new List<StfsPackage>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                    {
                        if (f.IndexOf(".data" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        TryAdd(list, f);
                    }
                }
                else TryAdd(list, p);
            }
            return list;
        }

        private static void TryAdd(List<StfsPackage> list, string f)
        {
            try
            {
                var k = FileSniffer.Detect(f);
                if (k != WorkingFileKind.Package && k != WorkingFileKind.Save && k != WorkingFileKind.Profile) return;
                var pkg = StfsPackage.Read(f);
                if (pkg.Valid && !list.Any(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase))) list.Add(pkg);
            }
            catch { }
        }

        public static long PackageBytes(StfsPackage p)
        {
            long total = new FileInfo(p.Path).Length;
            if (p.HasDataFolder) total += FtpService.MeasureLocal(p.Path + ".data");
            return total;
        }

        public static async Task InstallAsync(IList<StfsPackage> packages, InstallTarget target, string usbRoot, FtpService ftp,
            string ftpDriveRoot, string xbdmDrive, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            var state = new TransferProgress { Total = packages.Sum(PackageBytes) };
            foreach (var pkg in packages)
            {
                ct.ThrowIfCancellationRequested();
                string fileName = pkg.InstallName;
                string rel = pkg.InstallDir;
                switch (target)
                {
                    case InstallTarget.UsbDrive:
                        await Task.Run(() => CopyToUsb(pkg, usbRoot, progress, state, ct), ct);
                        Log.Success($"Installed {Label(pkg)} -> {Path.Combine(usbRoot, rel)}");
                        break;

                    case InstallTarget.ConsoleXbdm:
                    {
                        string root = xbdmDrive.TrimEnd('\\', ':') + ":\\";
                        string dir = root + rel;
                        await ConsoleService.UploadToPathAsync(pkg.Path, ConsoleService.Combine(dir, fileName), progress, ct);
                        state.Done += new FileInfo(pkg.Path).Length;
                        if (pkg.HasDataFolder)
                            await ConsoleService.UploadToPathAsync(pkg.Path + ".data", ConsoleService.Combine(dir, fileName + ".data"), progress, ct);
                        Log.Success($"Installed {Label(pkg)} -> {dir}");
                        break;
                    }

                    case InstallTarget.ConsoleFtp:
                    {
                        string dir = "/" + ftpDriveRoot.Trim('/') + "/" + rel.Replace('\\', '/');
                        await ftp.EnsurePathAsync(dir);
                        await ftp.UploadFileAsync(pkg.Path, FtpService.Combine(dir, fileName), progress, state, ct);
                        if (pkg.HasDataFolder)
                            await ftp.UploadDirectoryAsync(pkg.Path + ".data", FtpService.Combine(dir, fileName + ".data"), progress, state, ct);
                        Log.Success($"Installed {Label(pkg)} -> ftp:{dir}");
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Installs packages onto an Xbox drive image that is open for changes: each goes to
        /// Content\{profile}\{title ID}\{type}\ exactly as on a USB drive, Games on Demand data folders included.
        /// </summary>
        public static void InstallToVolume(IList<StfsPackage> packages, FatxVolume volume, IProgress<TransferProgress> progress, CancellationToken ct)
        {
            if (!volume.Writable) throw new InvalidOperationException("This drive is open read-only.");
            long total = packages.Sum(PackageBytes);
            if (total > volume.FreeBytes)
                throw new IOException("Not enough free space on the drive: " + FileSniffer.FormatSize(total) + " to install, " + FileSniffer.FormatSize(volume.FreeBytes) + " free.");
            var state = new TransferProgress { Total = total };
            Action<long> step = n => { state.Done += n; progress?.Report(state); };
            foreach (var pkg in packages)
            {
                ct.ThrowIfCancellationRequested();
                state.File = pkg.InstallName;
                uint dir = volume.ResolveDirectory(pkg.InstallDir, true);
                volume.WriteFile(dir, pkg.Path, pkg.InstallName, step, ct);
                if (pkg.HasDataFolder) volume.WriteDirectory(dir, pkg.Path + ".data", pkg.InstallName + ".data", step, ct);
                Log.Success($"Installed {Label(pkg)} -> {volume.Name} \\{pkg.InstallDir}");
            }
        }

        public static string Label(StfsPackage p)
        {
            string name = !string.IsNullOrEmpty(p.DisplayName) ? p.DisplayName : !string.IsNullOrEmpty(p.TitleName) ? p.TitleName : Path.GetFileName(p.Path);
            return $"{name} [{p.ContentTypeName}]";
        }

        private static void CopyToUsb(StfsPackage pkg, string usbRoot, IProgress<TransferProgress> progress, TransferProgress state, CancellationToken ct)
        {
            string dir = Path.Combine(usbRoot, pkg.InstallDir);
            Directory.CreateDirectory(dir);
            string fileName = pkg.InstallName;
            CopyFile(pkg.Path, Path.Combine(dir, fileName), progress, state, ct);
            if (pkg.HasDataFolder) CopyDirectory(pkg.Path + ".data", Path.Combine(dir, fileName + ".data"), progress, state, ct);
        }

        public static void CopyDirectory(string src, string dst, IProgress<TransferProgress> progress, TransferProgress state, CancellationToken ct)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                CopyFile(f, Path.Combine(dst, Path.GetFileName(f)), progress, state, ct);
            foreach (var d in Directory.GetDirectories(src))
                CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)), progress, state, ct);
        }

        public static void CopyFile(string src, string dst, IProgress<TransferProgress> progress, TransferProgress state, CancellationToken ct)
        {
            state.File = Path.GetFileName(src);
            var buf = new byte[1024 * 1024];
            using (var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, buf.Length))
            using (var o = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, buf.Length))
            {
                int n;
                while ((n = i.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    o.Write(buf, 0, n);
                    state.Done += n;
                    progress?.Report(state);
                }
            }
        }
    }
}
