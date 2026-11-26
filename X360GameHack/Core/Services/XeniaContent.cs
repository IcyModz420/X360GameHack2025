using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using X360GameHack.Core.Formats;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// Saves, profiles, downloadable content and title updates in Xenia. Xenia Canary keeps them in the same places as a
    /// console's hard drive - {storage}\content\{profile ID}\{title ID}\{type}\{name} (content_manager.cc) - and a profile
    /// is found when its folder name is the profile ID in capitals and holds FFFE07D1\00010000\{profile ID}
    /// (profile_manager.cc).
    ///
    /// Everything is kept in Xenia's own form: a folder of the package's files, with a header file for it in
    /// {title}\Headers\{type}\{name}.header. The bundled Xenia Canary (02d2cb5cc) lists ONLY folders there
    /// (ContentManager::ListContent skips files) and reads the header file as XCONTENT_AGGREGATE_DATA - see
    /// <see cref="XeniaHeader"/>. Its installer unpacks every package (saves, profiles, DLC, title updates) that way, and so
    /// does this. Measured with the bundled build: a title update left as the package file was never used; unpacked with
    /// this header, "XEX patch applied successfully" (WaW Title Update #7 on a PXv2 default.xex).
    /// </summary>
    public static class XeniaContent
    {
        public const uint SaveType = 0x00000001, DlcType = 0x00000002, ProfileType = 0x00010000, UpdateType = 0x000B0000;
        public const string Everyone = "0000000000000000", Dashboard = "FFFE07D1";

        /// <summary>The content folder of the Xenia at <paramref name="xeniaExe"/> (it may not exist yet).</summary>
        public static string Root(string xeniaExe) => Path.Combine(XeniaHost.StorageRoot(xeniaExe), "content");

        /// <summary>Xenia's content, for the save and profile lists.</summary>
        public static LocalContentStore Store(string xeniaExe) => new LocalContentStore(XeniaHost.StorageRoot(xeniaExe), "Xenia");

        /// <summary>Saves, DLC, profiles and title updates go into Xenia's content folder; games are started from their own file.</summary>
        public static bool Takes(uint contentType) => contentType == SaveType || contentType == DlcType || contentType == ProfileType || contentType == UpdateType;

        private static bool Owned(string profileId) => ContentWalk.IsHex(profileId, 16) && profileId.Trim('0').Length > 0;

        /// <summary>content\{owner}\{title}\{type}\{name} under Xenia's content folder. Names that could leave the folder are refused.</summary>
        public static string PathFor(string xeniaExe, string ownerId, string titleId, uint contentType, string fileName)
        {
            if (!ContentWalk.IsHex(ownerId, 16) || !ContentWalk.IsHex(titleId, 8)) throw new InvalidOperationException("The profile ID or title ID is not valid.");
            if (string.IsNullOrEmpty(fileName) || fileName != PathSafety.SafeName(fileName)) throw new InvalidOperationException("\"" + fileName + "\" cannot be used as a file name.");
            return PathSafety.CombineUnder(Root(xeniaExe), ownerId.ToUpperInvariant() + "\\" + titleId.ToUpperInvariant() + "\\" + contentType.ToString("X8") + "\\" + fileName);
        }

        /// <summary>Where a package file goes in Xenia, read from its header: a save or a profile under its profile, the rest under 0000000000000000.</summary>
        public static string PathFor(string xeniaExe, StfsPackage pkg)
        {
            string name = Path.GetFileName(pkg.Path ?? "");
            if (!pkg.Valid) throw new InvalidOperationException(name + " is not an Xbox 360 package.");
            if (!Takes(pkg.ContentType))
                throw new InvalidOperationException(name + " is a " + pkg.ContentTypeName + ". Saves, profiles, downloadable content and title updates go into Xenia; a game is started from its own file (Xenia > Games > Add a game...).");
            if (pkg.IsCacheTitleUpdate)
                throw new InvalidOperationException(name + " is a cache-format title update (TU_...), which only a console's Cache folder uses. Use the tu... package of the same update.");
            // the dashboard's own files (title FFFExxxx - its caches and settings) are no use to Xenia; only a profile is
            // (a console can keep such a file anywhere - WaW's DLC folder on one drive held a "Title Content Progress cache")
            if ((pkg.TitleId >> 16) == 0xFFFE && pkg.ContentType != ProfileType)
                throw new InvalidOperationException(name + (pkg.DisplayName.Length > 0 ? " (\"" + pkg.DisplayName.Trim() + "\")" : "") + " is one of the console dashboard's own files, not a game's - Xenia does not use it, so it was left out.");
            bool personal = pkg.ContentType == SaveType || pkg.ContentType == ProfileType;
            string owner = personal && Owned(pkg.ProfileId) ? pkg.ProfileId : Everyone;
            if (pkg.ContentType == ProfileType)
            {
                if (owner == Everyone) throw new InvalidOperationException(name + " is a profile without a profile ID.");
                name = owner;                                        // a profile is named after its profile ID
            }
            return PathFor(xeniaExe, owner, pkg.TitleIdHex, pkg.ContentType, name);
        }

        /// <summary>The header file Xenia keeps for a package it made itself (a folder): {title}\Headers\{type}\{name}.header.</summary>
        public static string HeaderOf(string packagePath)
        {
            string typeDir = Path.GetDirectoryName(packagePath), titleDir = Path.GetDirectoryName(typeDir);
            return Path.Combine(titleDir, "Headers", Path.GetFileName(typeDir), Path.GetFileName(packagePath) + ".header");
        }

        /// <summary>
        /// True for an STFS package (CON, LIVE or PIRS: saves, profiles, DLC, title updates) - Xenia keeps every one of them
        /// unpacked into a folder: the bundled build lists only folders in its content folder (ListContent at 02d2cb5cc
        /// skips files), and its installer unpacks every package (InstallContentPackage). Games on Demand (SVOD) are not.
        /// </summary>
        public static bool Unpacks(string packageFile)
        {
            byte[] h = BE.ReadHead(packageFile, 0x400);
            if (h.Length < 0x3AD) return false;
            string magic = Encoding.ASCII.GetString(h, 0, 4);
            return (magic == "CON " || magic == "LIVE" || magic == "PIRS") && h[0x379] == 0x24 && BE.U32(h, 0x3A9) == 0;
        }

        /// <summary>True for a console-signed package (CON) - the kind a save or a profile is.</summary>
        private static bool IsCon(string packageFile) => Encoding.ASCII.GetString(BE.ReadHead(packageFile, 4)) == "CON ";

        /// <summary>
        /// Size of the header file the bundled Xenia reads (ContentManager::ReadContentHeaderFile at 02d2cb5cc):
        /// XCONTENT_AGGREGATE_DATA, 0x148 bytes, then the package's licence mask (4 bytes) as its installer writes it.
        /// </summary>
        public const int AggregateSize = 0x148, XeniaHeaderSize = 0x14C;

        /// <summary>
        /// The header file Xenia keeps for a package in its content folder ({title}\Headers\{type}\{name}.header), as the
        /// bundled Xenia's installer writes it (ExtractContentHeader at 02d2cb5cc): XCONTENT_AGGREGATE_DATA, big-endian -
        /// device ID (4) = 1, Xenia's hard drive; content type (4); display name (UTF-16, 128 characters); file name (42
        /// bytes); 2 + 4 bytes padding; profile / xuid (8) at 0x138; title ID (4) at 0x140; 4 bytes padding - 0x148 bytes,
        /// then the licence mask (the licence bits of the package's licences together) as 4 little-endian bytes. Xenia gives
        /// a game the file name from here, and a newer Xenia reads this form too (its "old header").
        /// </summary>
        /// <param name="packageHead">The package's header (its first 0x1000 bytes at least).</param>
        /// <param name="xuid">The profile it belongs to: a save's or profile's owner, 0 for DLC and title updates.</param>
        public static byte[] XeniaHeader(byte[] packageHead, string fileName, ulong xuid)
        {
            if (packageHead == null || packageHead.Length < 0x411 + 0x100) throw new InvalidDataException("Not a whole package header.");
            var h = new byte[XeniaHeaderSize];
            BE.W32(h, 0, 1);
            BE.W32(h, 4, BE.U32(packageHead, 0x344));
            Buffer.BlockCopy(packageHead, 0x411, h, 8, 0x100);                       // the first display name, UTF-16BE
            byte[] name = Encoding.UTF8.GetBytes(fileName ?? "");
            Buffer.BlockCopy(name, 0, h, 0x108, Math.Min(name.Length, HeaderFileNameLength));
            BE.W64(h, 0x138, xuid);
            BE.W32(h, 0x140, BE.U32(packageHead, 0x360));
            uint licence = 0;
            for (int i = 0; i < 16; i++) licence |= BE.U32(packageHead, 0x22C + i * 0x10 + 8);
            h[0x148] = (byte)licence; h[0x149] = (byte)(licence >> 8); h[0x14A] = (byte)(licence >> 16); h[0x14B] = (byte)(licence >> 24);
            return h;
        }

        /// <summary>What a header file in Xenia's content folder says (either form), or null when it is neither.</summary>
        public sealed class HeaderInfo
        {
            public uint ContentType, TitleId, LicenseMask;
            public ulong Xuid;
            public string DisplayName = "", FileName = "", TitleName = "";
            /// <summary>True for the XCONTENT_AGGREGATE_DATA form (what the bundled Xenia reads and writes); false for a newer Xenia's package-header form.</summary>
            public bool Aggregate;
        }

        public static HeaderInfo ReadHeader(byte[] b)
        {
            if (b == null || b.Length < AggregateSize) return null;
            string magic = Encoding.ASCII.GetString(b, 0, 4);
            if (magic == "CON " || magic == "LIVE" || magic == "PIRS")
            {
                if (b.Length < 0x1691 + 0x80) return null;
                var p = new HeaderInfo
                {
                    ContentType = BE.U32(b, 0x344), TitleId = BE.U32(b, 0x360), Xuid = BE.U64(b, 0x371),
                    DisplayName = Utf16(b, 0x411, 0x100), TitleName = Utf16(b, 0x1691, 0x80),
                    FileName = b.Length >= HeaderFileNameAt + HeaderFileNameLength ? Ascii(b, HeaderFileNameAt, HeaderFileNameLength) : ""
                };
                for (int i = 0; i < 16; i++) p.LicenseMask |= BE.U32(b, 0x22C + i * 0x10 + 8);
                return p;
            }
            var a = new HeaderInfo
            {
                Aggregate = true, ContentType = BE.U32(b, 4), DisplayName = Utf16(b, 8, 0x100), FileName = Ascii(b, 0x108, HeaderFileNameLength),
                Xuid = BE.U64(b, 0x138), TitleId = BE.U32(b, 0x140)
            };
            if (b.Length >= XeniaHeaderSize) a.LicenseMask = (uint)(b[0x148] | b[0x149] << 8 | b[0x14A] << 16 | b[0x14B] << 24);
            return a;
        }

        private static string Utf16(byte[] b, int at, int bytes)
        {
            string s = Encoding.BigEndianUnicode.GetString(b, at, bytes);
            int z = s.IndexOf('\0');
            return (z >= 0 ? s.Substring(0, z) : s).Trim();
        }

        private static string Ascii(byte[] b, int at, int bytes)
        {
            int n = 0;
            while (n < bytes && b[at + n] != 0) n++;
            return Encoding.UTF8.GetString(b, at, n);
        }

        /// <summary>The profile a path in Xenia's content folder belongs to: content\{xuid}\{title}\{type}\{name} (0 for everyone's).</summary>
        private static ulong OwnerOf(string packagePath)
        {
            string xuidDir = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(packagePath)))));
            return ContentWalk.IsHex(xuidDir, 16) ? Convert.ToUInt64(xuidDir, 16) : 0;
        }

        /// <summary>
        /// Where a package header keeps its owner, and where a newer Xenia's header file (XContentContainerHeader) keeps its
        /// file name: profile_id at 0x371, file_name_raw[42] right after the 0x93D6-byte metadata. Only read now.
        /// </summary>
        public const int HeaderProfileIdAt = 0x371, HeaderFileNameAt = 0x971A, HeaderFileNameLength = 42;

        /// <summary>
        /// Copies a console save into Xenia for one of Xenia's profiles, whichever profile made it: unpacked into
        /// content\{profile}\{title}\00000001\{name} as Xenia's own installer does, its header file naming that profile
        /// (<see cref="XeniaHeader"/>) - Xenia opens a save under the profile in its header. This is for saves made on another
        /// profile - downloaded, modded or a friend's: a console needs those resigned, Xenia checks no signature. Whatever has that name there already is copied to Saves\Backups
        /// first. Returns where it went and that backup ("" = this very save was there already, null = nothing was there).
        /// </summary>
        public static (string Target, string Backup) PutSaveFor(string xeniaExe, string saveFile, string profileId, List<string> damaged)
        {
            string name = Path.GetFileName(saveFile ?? "");
            var pkg = StfsPackage.Read(saveFile);
            if (!pkg.Valid) throw new InvalidOperationException(name + " is not an Xbox 360 save.");
            if (pkg.ContentType != SaveType) throw new InvalidOperationException(name + " is a " + pkg.ContentTypeName + ", not a save.");
            if (!Unpacks(saveFile) || !IsCon(saveFile)) throw new InvalidOperationException(name + " is not a console save package (CON), so it cannot be given to a profile.");
            if (!Owned(profileId)) throw new InvalidOperationException("Pick one of Xenia's profiles for the save.");
            string target = PathFor(xeniaExe, profileId, pkg.TitleIdHex, SaveType, name);
            return (target, PutUnpacked(saveFile, target, damaged, XeniaHeader(BE.ReadHead(saveFile, 0x1000), name, Convert.ToUInt64(profileId, 16))));
        }

        /// <summary>
        /// Copies a package into Xenia at <paramref name="target"/>, as Xenia's own installer does: unpacked into a folder
        /// there, with its header file in {title}\Headers\{type} (anything that is not an STFS package is copied as the file).
        /// Whatever is there already is copied to Saves\Backups first and then replaced.
        /// Returns that backup, "" when the same package was already there, or null when nothing was there.
        /// </summary>
        public static string Put(string localFile, string target) => Put(localFile, target, null);

        /// <param name="damaged">null = a package holding a block that does not match its hash is refused. A list = it is
        /// copied as it is (Xenia never checks those hashes) and the files inside that hold such a block are added there,
        /// so they can be named.</param>
        public static string Put(string localFile, string target, List<string> damaged)
        {
            if (Unpacks(localFile)) return PutUnpacked(localFile, target, damaged);
            if (File.Exists(target) && SameFile(localFile, target)) return "";
            string backup = File.Exists(target) || Directory.Exists(target) ? Backup(target) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (Directory.Exists(target))
            {
                string aside = MoveAside(target);                    // fails, changing nothing, while Xenia has it open
                try { File.Copy(localFile, target, false); }
                catch { Directory.Move(aside, target); throw; }
                RemoveQuietly(aside, HeaderOf(target));
            }
            else File.Copy(localFile, target, true);                 // fails, changing nothing, while Xenia has it open
            return backup;
        }

        private static string PutUnpacked(string localFile, string target, List<string> damaged, byte[] header = null)
        {
            header = header ?? XeniaHeader(BE.ReadHead(localFile, 0x1000), Path.GetFileName(target), OwnerOf(target));
            string headerPath = HeaderOf(target);
            // this very package is there: the same header and the same files - the header alone is not enough (two saves of a
            // game with the same name for the same profile have the very same header in this form)
            if (Directory.Exists(target) && File.Exists(headerPath) && File.ReadAllBytes(headerPath).SequenceEqual(header) && SameFiles(localFile, target)) return "";
            string backup = File.Exists(target) || Directory.Exists(target) ? Backup(target) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            // unpacked beside it first, so a failure part-way never leaves half a save in Xenia
            string fresh = target + ".x360gamehack-new-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                Directory.CreateDirectory(fresh);
                using (var v = StfsVolume.Open(localFile))
                {
                    foreach (var e in v.Entries.Where(x => x.IsDirectory)) Directory.CreateDirectory(PathSafety.CombineUnder(fresh, e.Path));
                    foreach (var e in v.Entries.Where(x => !x.IsDirectory))
                    {
                        string to = PathSafety.CombineUnder(fresh, e.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        var bad = damaged == null ? null : new List<int>();
                        v.ExtractFile(e, to, System.Threading.CancellationToken.None, bad);
                        if (bad != null && bad.Count > 0) damaged.Add(e.Path);
                    }
                }
            }
            catch { RemoveQuietly(fresh, null); throw; }
            string aside = null;
            try
            {
                if (Directory.Exists(target)) aside = MoveAside(target);
                else if (File.Exists(target)) File.Delete(target);           // a package file there before (backed up above)
                Directory.Move(fresh, target);
            }
            catch
            {
                if (aside != null && !Directory.Exists(target)) Directory.Move(aside, target);
                RemoveQuietly(fresh, null);
                throw;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(headerPath));
            File.WriteAllBytes(headerPath, header);
            if (aside != null) RemoveQuietly(aside, null);
            return backup;
        }

        /// <summary>True when a folder holds exactly the files of a package, byte for byte (and nothing else).</summary>
        private static bool SameFiles(string packageFile, string folder)
        {
            try
            {
                using (var v = StfsVolume.Open(packageFile))
                {
                    var files = v.Entries.Where(x => !x.IsDirectory).ToList();
                    if (Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length != files.Count) return false;
                    foreach (var e in files)
                    {
                        string path = PathSafety.CombineUnder(folder, e.Path);
                        if (!File.Exists(path) || new FileInfo(path).Length != e.Size) return false;
                    }
                    // byte for byte, a piece at a time (a DLC file can be hundreds of MB): each file out to a temporary copy first
                    string tmp = Path.Combine(Path.GetTempPath(), "X360GameHack", "same-" + Guid.NewGuid().ToString("N") + ".tmp");
                    Directory.CreateDirectory(Path.GetDirectoryName(tmp));
                    try
                    {
                        foreach (var e in files)
                        {
                            v.ExtractFile(e, tmp, System.Threading.CancellationToken.None);
                            if (!SameStream(tmp, PathSafety.CombineUnder(folder, e.Path))) return false;
                        }
                    }
                    finally { try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                    return true;
                }
            }
            catch (Exception) { return false; }
        }

        private static bool SameStream(string a, string b)
        {
            using (var x = File.OpenRead(a))
            using (var y = File.OpenRead(b))
            {
                if (x.Length != y.Length) return false;
                var bx = new byte[1 << 16];
                var by = new byte[1 << 16];
                while (true)
                {
                    int n = x.Read(bx, 0, bx.Length);
                    if (n == 0) return true;
                    int m = 0;
                    while (m < n) { int r = y.Read(by, m, n - m); if (r == 0) return false; m += r; }
                    for (int i = 0; i < n; i++) if (bx[i] != by[i]) return false;
                }
            }
        }

        /// <summary>
        /// Xenia applies only the first title update it finds for a game (KernelState::FindTitleUpdate takes the first one
        /// ListContent gives, in folder order), so a game must have just one: every other title update next to
        /// <paramref name="updatePath"/> (a folder and its header file, or a package file an older version left) is copied to
        /// Saves\Backups and then removed. Returns their names. Fails, changing nothing more, while Xenia has one open.
        /// </summary>
        public static List<string> SetAsideOtherUpdates(string updatePath)
        {
            var moved = new List<string>();
            string dir = Path.GetDirectoryName(Path.GetFullPath(updatePath));
            if (!Directory.Exists(dir)) return moved;
            foreach (string other in Directory.GetFileSystemEntries(dir))
            {
                if (string.Equals(Path.GetFullPath(other), Path.GetFullPath(updatePath), StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(other).IndexOf(".x360gamehack-", StringComparison.Ordinal) >= 0) continue;
                Backup(other);
                if (Directory.Exists(other)) RemoveQuietly(MoveAside(other), HeaderOf(other));
                else File.Delete(other);
                moved.Add(Path.GetFileName(other));
            }
            return moved;
        }

        /// <summary>Deletes a save or profile from Xenia (a file, or a folder and its header file) after copying it to Saves\Backups. Returns the copy.</summary>
        public static string Delete(string path)
        {
            string backup = Backup(path);
            if (Directory.Exists(path)) RemoveQuietly(MoveAside(path), HeaderOf(path));
            else File.Delete(path);
            return backup;
        }

        /// <summary>Renames a folder out of the way in one step - Windows refuses while a file in it is open, and then nothing has changed.</summary>
        private static string MoveAside(string folder)
        {
            string aside = folder + ".x360gamehack-old-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try { Directory.Move(folder, aside); }
            catch (IOException) { throw new IOException(Path.GetFileName(folder) + " is in use. Close Xenia and try again."); }
            catch (UnauthorizedAccessException) { throw new IOException(Path.GetFileName(folder) + " is in use. Close Xenia and try again."); }
            return aside;
        }

        private static void RemoveQuietly(string folder, string header)
        {
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { if (File.Exists(header)) File.Delete(header); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// Puts what earlier versions of this program left in Xenia's content folder into the form the bundled Xenia reads
        /// (see the class notes): a package left as a file (DLC and title updates used to be copied as files) is unpacked into
        /// a folder of the same name - the file itself is moved to Saves\Backups - and a header file in a newer Xenia's form
        /// (the whole package header) is rewritten as XCONTENT_AGGREGATE_DATA, its old bytes copied to Saves\Backups first.
        /// Run only while Xenia is closed. Returns what was done, one line each (none when everything is in order).
        /// </summary>
        public static List<string> Repair(string xeniaExe)
        {
            var done = new List<string>();
            string root = Root(xeniaExe);
            if (!Directory.Exists(root)) return done;
            foreach (string xuidDir in Directory.GetDirectories(root))
            {
                string xuidName = Path.GetFileName(xuidDir);
                if (!ContentWalk.IsHex(xuidName, 16)) continue;
                ulong xuid = Convert.ToUInt64(xuidName, 16);
                foreach (string titleDir in Directory.GetDirectories(xuidDir))
                {
                    if (!ContentWalk.IsHex(Path.GetFileName(titleDir), 8)) continue;
                    foreach (string typeDir in Directory.GetDirectories(titleDir))
                    {
                        if (!ContentWalk.IsHex(Path.GetFileName(typeDir), 8)) continue;
                        foreach (string file in Directory.GetFiles(typeDir))
                        {
                            if (Path.GetFileName(file).IndexOf(".x360gamehack-", StringComparison.Ordinal) >= 0) continue;
                            bool package;
                            try { package = Unpacks(file); }
                            catch (IOException) { continue; }
                            if (!package) continue;
                            string kept = MoveToBackups(file);
                            try { PutUnpacked(kept, file, null); }
                            catch
                            {
                                if (!File.Exists(file) && !Directory.Exists(file)) File.Move(kept, file);      // as it was
                                throw;
                            }
                            done.Add(Path.GetFileName(file) + " (" + Path.GetFileName(titleDir) + "\\" + Path.GetFileName(typeDir) + ") unpacked the way Xenia keeps it - the package file is in " + Path.GetDirectoryName(kept));
                        }
                    }
                    string headers = Path.Combine(titleDir, "Headers");
                    if (!Directory.Exists(headers)) continue;
                    foreach (string typeDir in Directory.GetDirectories(headers))
                        foreach (string headerFile in Directory.GetFiles(typeDir, "*.header"))
                        {
                            if (new FileInfo(headerFile).Length <= XeniaHeaderSize) continue;
                            byte[] old = File.ReadAllBytes(headerFile);
                            var info = ReadHeader(old);
                            if (info == null || info.Aggregate) continue;
                            string name = Path.GetFileNameWithoutExtension(headerFile);       // savegame.svg.header -> savegame.svg
                            string copy = CopyToBackups(headerFile);
                            File.WriteAllBytes(headerFile, XeniaHeader(old, name, xuid));
                            done.Add("the header of " + name + " (" + Path.GetFileName(titleDir) + ") rewritten in the form this Xenia reads - the old one is in " + Path.GetDirectoryName(copy));
                        }
                }
            }
            return done;
        }

        private static string BackupsFolder(string name)
        {
            string root = Path.Combine(AppSettings.Current.SavesFolderResolved, "Backups"), stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss");
            string dir = Path.Combine(root, stamp);
            for (int i = 2; File.Exists(Path.Combine(dir, name)) || Directory.Exists(Path.Combine(dir, name)); i++) dir = Path.Combine(root, stamp + " (" + i + ")");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, name);
        }

        private static string MoveToBackups(string file)
        {
            string to = BackupsFolder(PathSafety.SafeName(Path.GetFileName(file)));
            File.Move(file, to);
            return to;
        }

        private static string CopyToBackups(string file)
        {
            string to = BackupsFolder(PathSafety.SafeName(Path.GetFileName(file)));
            File.Copy(file, to);
            return to;
        }

        /// <summary>Copies a file, or a folder with Xenia's header file for it, into Saves\Backups\{date time}\. Returns the copy.</summary>
        public static string Backup(string path)
        {
            string root = Path.Combine(AppSettings.Current.SavesFolderResolved, "Backups"), stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss");
            string name = PathSafety.SafeName(Path.GetFileName(path.TrimEnd('\\')));
            string dir = Path.Combine(root, stamp);
            for (int i = 2; File.Exists(Path.Combine(dir, name)) || Directory.Exists(Path.Combine(dir, name)); i++) dir = Path.Combine(root, stamp + " (" + i + ")");
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, name);
            if (Directory.Exists(path))
            {
                CopyFolder(path, copy, 0);
                string header = HeaderOf(path);
                if (File.Exists(header)) File.Copy(header, Path.Combine(dir, Path.GetFileName(header)));
            }
            else File.Copy(path, copy);
            return copy;
        }

        /// <summary>Copies a folder and everything in it (links are not followed).</summary>
        public static void CopyFolder(string from, string to, int depth)
        {
            if (depth > 32) throw new IOException("The folders under " + from + " are nested too deeply.");
            Directory.CreateDirectory(to);
            foreach (var f in new DirectoryInfo(from).EnumerateFileSystemInfos())
            {
                if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                string target = PathSafety.CombineUnder(to, f.Name);
                if (f is DirectoryInfo) CopyFolder(f.FullName, target, depth + 1);
                else File.Copy(f.FullName, target, true);
            }
        }

        private static bool SameFile(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            using (var x = fa.OpenRead())
            using (var y = fb.OpenRead())
            {
                var bx = new byte[1 << 16];
                var by = new byte[1 << 16];
                while (true)
                {
                    int n = x.Read(bx, 0, bx.Length);
                    if (n == 0) return true;
                    int m = 0;
                    while (m < n) { int r = y.Read(by, m, n - m); if (r == 0) return false; m += r; }
                    for (int i = 0; i < n; i++) if (bx[i] != by[i]) return false;
                }
            }
        }

        private static readonly Regex SlotZero = new Regex(@"^(\s*logged_profile_slot_0_xuid\s*=\s*"")([^""]*)("".*)$", RegexOptions.Compiled);

        /// <summary>
        /// Makes Xenia sign in to this profile when it starts, unless it already signs in to one (logged_profile_slot_0_xuid
        /// in its settings file). True when it was set. Xenia's settings file exists once Xenia has been started.
        /// </summary>
        public static bool SignInIfNone(string xeniaExe, string profileId)
        {
            string file = XeniaHost.ConfigFile(xeniaExe);
            if (!Owned(profileId) || !File.Exists(file)) return false;
            string[] lines = File.ReadAllLines(file, Encoding.UTF8);
            bool inProfiles = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("[")) { inProfiles = t.Equals("[Profiles]", StringComparison.OrdinalIgnoreCase); continue; }
                if (!inProfiles) continue;
                var m = SlotZero.Match(lines[i]);
                if (!m.Success) continue;
                if (m.Groups[2].Value.Trim().Length > 0) return false;      // Xenia already signs in to a profile: keep it
                lines[i] = m.Groups[1].Value + profileId.ToUpperInvariant() + m.Groups[3].Value;
                File.WriteAllLines(file, lines, new UTF8Encoding(false));
                return true;
            }
            return false;
        }

        /// <summary>The profile Xenia signs in to when it starts (slot 1), or "" when none.</summary>
        public static string SignedIn(string xeniaExe)
        {
            try
            {
                string file = XeniaHost.ConfigFile(xeniaExe);
                if (!File.Exists(file)) return "";
                bool inProfiles = false;
                foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    string t = line.Trim();
                    if (t.StartsWith("[")) { inProfiles = t.Equals("[Profiles]", StringComparison.OrdinalIgnoreCase); continue; }
                    var m = inProfiles ? SlotZero.Match(line) : Match.Empty;
                    if (m.Success) return m.Groups[2].Value.Trim().ToUpperInvariant();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return "";
        }

        /// <summary>True when Xenia has this profile (a console profile file, or a profile it made itself).</summary>
        public static bool HasProfile(string xeniaExe, string profileId)
        {
            if (!Owned(profileId)) return false;
            string p = PathFor(xeniaExe, profileId, Dashboard, ProfileType, profileId.ToUpperInvariant());
            return File.Exists(p) || Directory.Exists(p);
        }

        public sealed class XeniaProfile
        {
            public string Id, Gamertag;
            public string Label => Gamertag.Length > 0 ? Gamertag + "  (" + Id + ")" : Id;
        }

        /// <summary>
        /// The profiles Xenia can sign in to, with their gamertags: what its ProfileManager::FindProfiles finds (a folder
        /// content\{ID}\FFFE07D1\00010000\{ID}, the ID 16 upper-case hex digits) with an Account file it can open.
        /// </summary>
        public static List<XeniaProfile> Profiles(string xeniaExe)
        {
            var list = new List<XeniaProfile>();
            string root = Root(xeniaExe);
            if (!Directory.Exists(root)) return list;
            foreach (string dir in Directory.GetDirectories(root))
            {
                string id = Path.GetFileName(dir);
                if (!Regex.IsMatch(id, "^[0-9A-F]{16}$") || !Owned(id)) continue;
                string account = Path.Combine(PathFor(xeniaExe, id, Dashboard, ProfileType, id), "Account");
                if (!File.Exists(account)) continue;
                string tag;
                try { tag = (ProfileAccount.Load(File.ReadAllBytes(account)).Gamertag ?? "").Trim(); }
                catch (Exception) { tag = ""; }
                list.Add(new XeniaProfile { Id = id, Gamertag = tag });
            }
            return list;
        }

        /// <summary>
        /// Xenia's own rule for a new gamertag (ProfileManager::IsGamertagValid): 1 to 15 characters, a letter first, then
        /// letters and digits, with single spaces between words. Null when the gamertag is fine.
        /// </summary>
        public static string GamertagProblem(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "Type a gamertag.";
            if (tag.Length > 15) return "A gamertag has at most 15 characters.";
            if (!Regex.IsMatch(tag, @"^[A-Za-z][A-Za-z0-9]*( [A-Za-z0-9]+)*\z")) return "A gamertag starts with a letter and has only letters and digits, with single spaces between words.";
            return null;
        }

        /// <summary>
        /// Makes a new profile exactly as Xenia's own "Create Profile" does (ProfileManager::CreateProfile): an offline
        /// XUID 0xE03 followed by 31 random bits, the folder content\{XUID}\FFFE07D1\00010000\{XUID}, and in it the
        /// Account file holding only the gamertag (<see cref="ProfileAccount.NewLikeXenia"/>). Returns the XUID.
        /// </summary>
        public static string CreateProfile(string xeniaExe, string gamertag)
        {
            string problem = GamertagProblem(gamertag);
            if (problem != null) throw new ArgumentException(problem);
            byte[] account = ProfileAccount.NewLikeXenia(gamertag).ToBytes();
            string root = Root(xeniaExe), id;
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                var b = new byte[4];
                do
                {
                    rng.GetBytes(b);
                    id = ((0xE03UL << 52) | (BitConverter.ToUInt32(b, 0) & 0x7FFFFFFFu)).ToString("X16");
                }
                while (Directory.Exists(Path.Combine(root, id)));
            }
            string folder = PathFor(xeniaExe, id, Dashboard, ProfileType, id);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "Account"), account);
            return id;
        }

        /// <summary>
        /// Makes Xenia sign in to this profile when it starts (slot 1), in place of any other. Before Xenia's first start
        /// there is no settings file yet: then one is written holding just this line, and Xenia adds the rest itself.
        /// </summary>
        public static void SignIn(string xeniaExe, string profileId)
        {
            if (!Owned(profileId)) throw new ArgumentException("Not a profile ID: " + profileId);
            string id = profileId.ToUpperInvariant(), line = "logged_profile_slot_0_xuid = \"" + id + "\"";
            string file = XeniaHost.ConfigFile(xeniaExe);
            var utf8 = new UTF8Encoding(false);
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllLines(file, new[] { "[Profiles]", line }, utf8);
                return;
            }
            var lines = File.ReadAllLines(file, Encoding.UTF8).ToList();
            bool inProfiles = false;
            int section = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("[")) { inProfiles = t.Equals("[Profiles]", StringComparison.OrdinalIgnoreCase); if (inProfiles) section = i; continue; }
                if (!inProfiles) continue;
                var m = SlotZero.Match(lines[i]);
                if (!m.Success) continue;
                lines[i] = m.Groups[1].Value + id + m.Groups[3].Value;
                File.WriteAllLines(file, lines, utf8);
                return;
            }
            if (section >= 0) lines.Insert(section + 1, line);
            else { lines.Add(""); lines.Add("[Profiles]"); lines.Add(line); }
            File.WriteAllLines(file, lines, utf8);
        }
    }
}
