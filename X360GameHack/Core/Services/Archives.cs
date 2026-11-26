using System;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// Unpacks the archives saves are shared in (a downloaded save is usually a .zip or .rar holding the console's folder
    /// layout) into a folder of their own, so the packages inside can be read. Downloads are not trusted: a name that
    /// would leave the folder (..\, a drive, a device) is skipped, a file bigger than the archive says is refused, and the
    /// number and total size of files are capped. Nothing inside is ever started.
    /// </summary>
    public static class Archives
    {
        public const long MaxFileBytes = 4L << 30, MaxTotalBytes = 16L << 30;
        public const int MaxFiles = 50000;

        public static bool IsArchive(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return ext == ".zip" || ext == ".rar" || ext == ".7z";
        }

        /// <summary>Unpacks a .zip into <paramref name="dir"/> with the program's own reader. Returns the number of files written.</summary>
        public static int UnzipInto(string zip, string dir, CancellationToken ct)
        {
            string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            long total = 0;
            int count = 0, written = 0;
            using (var fs = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (var e in z.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (++count > MaxFiles) throw new InvalidDataException("it holds more than " + MaxFiles + " files.");
                    string name = (e.FullName ?? "").Replace('/', '\\');
                    if (name.Length == 0 || name.EndsWith("\\", StringComparison.Ordinal)) continue;          // a folder
                    string to;
                    try { to = Path.GetFullPath(Path.Combine(root, name)); }
                    catch (Exception) { continue; }                                                           // a name Windows cannot use
                    if (!to.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;                   // would leave the folder
                    if (e.Length > MaxFileBytes || (total += e.Length) > MaxTotalBytes) throw new InvalidDataException("it is too big to unpack here.");
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    using (var src = e.Open())
                    using (var dst = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buf = new byte[81920];
                        long n = 0;
                        int k;
                        while ((k = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            if ((n += k) > e.Length) throw new InvalidDataException("a file in it is bigger than the archive says.");
                            dst.Write(buf, 0, k);
                        }
                    }
                    written++;
                }
            }
            return written;
        }

        /// <summary>The 7-Zip installed in Program Files (never one found anywhere else), or null.</summary>
        public static string SevenZip()
        {
            foreach (string pf in new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                          Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                string exe = Path.Combine(pf, "7-Zip", "7z.exe");
                if (File.Exists(exe)) return exe;
            }
            return null;
        }

        /// <summary>
        /// Unpacks a .rar / .7z (or .zip) into <paramref name="dir"/> with the installed 7-Zip. False when 7-Zip is not
        /// installed. A password is never asked for (a made-up one is passed, so a locked archive fails instead of waiting).
        /// </summary>
        public static bool SevenZipInto(string archive, string dir, CancellationToken ct)
        {
            string exe = SevenZip();
            if (exe == null) return false;
            Directory.CreateDirectory(dir);                                          // also its working folder
            var r = ToolRunner.RunAsync(exe, "x -y -bd -bso0 -bsp0 -pX360GameHack-none -o" + ToolRunner.Q(dir) + " -- " + ToolRunner.Q(archive),
                                        dir, quiet: true, ct: ct).GetAwaiter().GetResult();
            ct.ThrowIfCancellationRequested();
            if (r.ExitCode == 0 || r.ExitCode == 1) return true;                     // 1 = finished with warnings
            string why = (r.Output ?? "").IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ? "it is locked with a password" : "7-Zip stopped with code " + r.ExitCode;
            throw new InvalidDataException("it could not be unpacked (" + why + ").");
        }
    }
}
