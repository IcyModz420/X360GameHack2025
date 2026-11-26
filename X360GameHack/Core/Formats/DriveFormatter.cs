using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace X360GameHack.Core.Formats
{
    /// <summary>How an Xbox drive is laid out when it is made (formatted).</summary>
    public enum DriveLayout
    {
        /// <summary>The Xbox 360's own layout: SysExt, SysExt2, the compatibility partition and Data (the rest).</summary>
        Xbox360,
        /// <summary>Original Xbox, the five partitions every console has (C: E: X: Y: Z:) - a stock 8 / 10 GB drive.</summary>
        OgStock,
        /// <summary>Original Xbox: F: takes the whole rest of the drive.</summary>
        OgFTakesAll,
        /// <summary>Original Xbox: F: up to the 137 GB line (sector 0x0FFFFFFF), G: the rest - what big-drive BIOSes do without a table.</summary>
        OgStandard,
        /// <summary>Original Xbox: F: up to the 137 GB line only, nothing after it (for BIOSes without big-drive support).</summary>
        OgFOnly137,
        /// <summary>Original Xbox: F: of a chosen size, G: the rest.</summary>
        OgCustom,
    }

    /// <summary>One partition of a <see cref="DrivePlan"/>.</summary>
    public sealed class PlannedPartition
    {
        public string Name;
        /// <summary>The name in the original Xbox partition table (16 characters, as XBpartitioner writes it), or null.</summary>
        public string TableLabel;
        public long Offset, Size;
        public uint SectorsPerCluster;
        public bool Xbox360;
        /// <summary>Table (FAT) entries: 2 or 4 bytes.</summary>
        public int EntrySize;
        public long Clusters, TableEntries, TableBytes;
        public uint VolumeId;
        /// <summary>Xbox 360 compatibility partition: made exactly like the console's own (header of FF, id FFFFFFFF, empty root of zeros).</summary>
        public bool Compatibility;
        /// <summary>Xbox 360 Data partition: its header carries "XLID" at 0x850, as the console's own does.</summary>
        public bool Data360;
        public long ClusterSize => SectorsPerCluster * 512L;
        public long TableOffset => Offset + 0x1000;
        public long DataOffset => TableOffset + TableBytes;
        public long RootOffset => DataOffset;                     // the root folder is cluster 1, the first of the data area
        public override string ToString() => Name + " @0x" + Offset.ToString("X") + ", " + FileSniffer.FormatSize(Size) + ", " + ClusterSize / 1024 + " KB clusters";
    }

    /// <summary>Everything a format writes, worked out before anything is written.</summary>
    public sealed class DrivePlan
    {
        public DriveLayout Layout;
        public long DriveSize;
        public bool Xbox360 => Layout == DriveLayout.Xbox360;
        public readonly List<PlannedPartition> Partitions = new List<PlannedPartition>();
        /// <summary>Original Xbox: sector 0, the partition table (512 bytes) - written so the drive says itself how it is laid out.</summary>
        public byte[] PartitionTable;
        /// <summary>Nothing below this offset is written, except the original Xbox's sector 0 (the table) and sector 3 (refurb info on a blank drive).</summary>
        public long LowestPartitionWrite => Partitions.Min(p => p.Offset);
        public string Describe() => string.Join("; ", Partitions.Select(p => p.ToString()));
    }

    /// <summary>
    /// Makes an Xbox drive: works out the layout (<see cref="Plan"/>) and writes it (<see cref="Apply"/>) into any stream -
    /// an image file, or a physical drive through the Disk Reader (which journals every change, so it can be undone).
    ///
    /// Xbox 360 - as the console makes its own drives (checked against a real one, Hitachi HTS545025B9SA00): SysExt
    /// (0x10C080000) and SysExt2 (0x118EB0000) with 16 KB clusters and 2-byte table entries, the compatibility partition
    /// (0x120EB0000, 256 MB, 4-byte entries, header of FF, id FFFFFFFF), Data (0x130EB0000 to the end, 4-byte entries,
    /// "XLID" at 0x850 of its header). Big-endian "XTAF" headers: random volume id, sectors per cluster, root cluster 1,
    /// then 00 00 and FF. Tables have one entry more than the volume has clusters (the console's count). Nothing in
    /// front of SysExt is ever written: the drive's security sector (0x2000) is the drive's own, and the two cache
    /// partitions are the console's - it sets them up itself.
    ///
    /// Original Xbox - the kernel's fixed partitions X: Y: Z: (750 MB) C: (500 MB) E: (4.88 GB) with 16 KB clusters, F:
    /// from sector 0xEE8AB0 and G: as the layout says, their cluster size chosen as XBpartitioner does (16 KB, doubling
    /// with every 256 GB). Little-endian "FATX" headers (random volume id, sectors per cluster, 1 table copy, then 0 and
    /// FF), one table entry per cluster (2 bytes below 0xFFF0 clusters, else 4), as the kernel counts them. Sector 0 gets
    /// XBpartitioner's partition table ("****PARTINFO****") describing exactly what was made, so a BIOS that reads the
    /// table and one that does not see the same drive. Drives up to 2 TB (the table counts sectors in 32 bits).
    /// </summary>
    public static class DriveFormatter
    {
        public const long Sector = 512;
        public const long OgFStart = 0x1DD156000;                 // sector 0xEE8AB0: right after E:
        public const long Og137Line = 0x0FFFFFFFL * Sector;       // sector 0x0FFFFFFF: where LBA28 ends
        public const long MaxOgSectors = 0xFFFFFFFFL;
        public const long Max360Size = 0xFFFFFFFFL * Sector;      // 2 TB
        public const long MinExtended = 1L << 30;                 // F: / G: / the 360's Data: at least 1 GB

        private static readonly (string Name, string Label, long Offset, long Size)[] OgFixed =
        {
            // in the order of XBpartitioner's table (entries 0-4): E: C: X: Y: Z:
            ("E: Data", "XBOX SHELL", 0xABE80000, 0x1312D6000),
            ("C: System", "XBOX DATA", 0x8CA80000, 0x1F400000),
            ("X: Cache", "XBOX GAME SWAP 1", 0x80000, 0x2EE00000),
            ("Y: Cache", "XBOX GAME SWAP 2", 0x2EE80000, 0x2EE00000),
            ("Z: Cache", "XBOX GAME SWAP 3", 0x5DC80000, 0x2EE00000),
        };

        /// <summary>
        /// The cluster size XBpartitioner gives an original Xbox partition of this size (SelectClusterSize): 16 KB, doubled
        /// for every time the size goes over 256 GB, 512 GB, 1 TB... (in 512-byte sectors).
        /// </summary>
        public static uint OgSectorsPerCluster(long sizeBytes)
        {
            uint kb = 16;
            ulong compare = 0x20000000;                           // sectors: 256 GB
            ulong sectors = (ulong)(sizeBytes / Sector);
            while (sectors > compare) { compare *= 2; kb *= 2; }
            return kb * 2;
        }

        /// <summary>Why a drive of this size cannot be made with this layout, or null when it can.</summary>
        public static string Unsuitable(long driveSize, DriveLayout layout)
        {
            if (driveSize % Sector != 0) return "the size is not a whole number of sectors";
            if (layout == DriveLayout.Xbox360)
            {
                if (driveSize < FatxVolume.DataOffset360 + MinExtended) return "an Xbox 360 drive needs at least " + FileSniffer.FormatSize(FatxVolume.DataOffset360 + MinExtended);
                if (driveSize > Max360Size) return "the Xbox 360 cannot use drives over 2 TB";
                return null;
            }
            if (driveSize / Sector > MaxOgSectors) return "the original Xbox cannot use drives over 2 TB";
            if (driveSize < OgFStart) return "an original Xbox drive needs at least " + FileSniffer.FormatSize(OgFStart) + " (the size of the stock 8 GB drive)";
            if (layout != DriveLayout.OgStock && driveSize < OgFStart + MinExtended) return "there is no room for an F: partition - use the stock layout";
            if (layout == DriveLayout.OgCustom && driveSize < OgFStart + 2 * MinExtended) return "there is no room for both F: and G:";
            return null;
        }

        /// <summary>
        /// Works out every partition of a drive of <paramref name="driveSize"/> bytes. <paramref name="customF"/> is F:'s size
        /// for <see cref="DriveLayout.OgCustom"/> (rounded down to whole 16-sector steps, at least 1 GB, leaving G: at least
        /// 1 GB). Volume ids are new random ones. Throws when the layout does not fit.
        /// </summary>
        public static DrivePlan Plan(long driveSize, DriveLayout layout, long customF = 0)
        {
            string why = Unsuitable(driveSize, layout);
            if (why != null) throw new ArgumentException("This drive cannot be made that way: " + why + ".");
            var plan = new DrivePlan { Layout = layout, DriveSize = driveSize };
            if (layout == DriveLayout.Xbox360)
            {
                plan.Partitions.Add(X360("SysExt (system extended)", 0x10C080000, 0xCE30000));
                plan.Partitions.Add(X360("SysExt2 (system auxiliary)", 0x118EB0000, 0x8000000));
                var compat = X360(FatxVolume.CompatibilityName, FatxVolume.CompatibilityOffset, FatxVolume.CompatibilitySize, entrySize: 4);
                compat.Compatibility = true; compat.VolumeId = 0xFFFFFFFF;
                plan.Partitions.Add(compat);
                var data = X360("Content (Data)", FatxVolume.DataOffset360, driveSize - FatxVolume.DataOffset360);
                data.Data360 = true;
                plan.Partitions.Add(data);
                return plan;
            }

            foreach (var f in OgFixed) plan.Partitions.Add(Og(f.Name, f.Label, f.Offset, f.Size));
            long sectors = driveSize / Sector, fStartSector = OgFStart / Sector;
            long fSectors = 0, gStartSector = 0, gSectors = 0;
            switch (layout)
            {
                case DriveLayout.OgFTakesAll:
                    fSectors = sectors - fStartSector;
                    break;
                case DriveLayout.OgStandard:
                    if (sectors >= 0x10000000) { fSectors = 0x0FFFFFFF - fStartSector; gStartSector = 0x0FFFFFFF; gSectors = sectors - 0x0FFFFFFF; }
                    else fSectors = sectors - fStartSector;
                    break;
                case DriveLayout.OgFOnly137:
                    fSectors = Math.Min(sectors, 0x0FFFFFFF) - fStartSector;
                    break;
                case DriveLayout.OgCustom:
                    fSectors = customF / Sector;
                    fSectors -= fSectors % 16;
                    if (fSectors * Sector < MinExtended) throw new ArgumentException("F: must be at least " + FileSniffer.FormatSize(MinExtended) + ".");
                    // a table that ends exactly on a 4 KB boundary is the one size where tools that count one entry more
                    // than there are clusters would find the data in another place: one cluster less avoids it
                    uint spc = OgSectorsPerCluster(fSectors * Sector);
                    long cl = fSectors * Sector / (spc * Sector);
                    if (cl * (cl < 0xFFF0 ? 2 : 4) % 0x1000 == 0) fSectors -= spc;
                    gStartSector = fStartSector + fSectors;
                    gSectors = sectors - gStartSector;
                    if (gSectors * Sector < MinExtended) throw new ArgumentException("F: is too big: G: would get less than " + FileSniffer.FormatSize(MinExtended) + ".");
                    break;
            }
            if (fSectors > 0) plan.Partitions.Add(Og("F: Extended", "XBOX F", OgFStart, fSectors * Sector, OgSectorsPerCluster(fSectors * Sector)));
            if (gSectors > 0) plan.Partitions.Add(Og("G: Extended", "XBOX G", gStartSector * Sector, gSectors * Sector, OgSectorsPerCluster(gSectors * Sector)));
            plan.PartitionTable = OgTableSector(plan, fSectors > 0, gSectors > 0, fSectors, gStartSector, gSectors);
            return plan;
        }

        private static uint NewVolumeId()
        {
            var b = new byte[4];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            uint id = BitConverter.ToUInt32(b, 0);
            return id == 0 || id == 0xFFFFFFFF ? 0x58474821 : id;
        }

        private static PlannedPartition X360(string name, long offset, long size, int entrySize = 0)
        {
            var p = new PlannedPartition { Name = name, Offset = offset, Size = size, SectorsPerCluster = 0x20, Xbox360 = true, VolumeId = NewVolumeId() };
            p.Clusters = size / p.ClusterSize;
            p.TableEntries = p.Clusters + 1;                         // the console's count: one entry more than clusters
            p.EntrySize = entrySize != 0 ? entrySize : p.TableEntries < 0xFFF0 ? 2 : 4;
            p.TableBytes = (p.TableEntries * p.EntrySize + 0xFFF) & ~0xFFFL;
            return p;
        }

        private static PlannedPartition Og(string name, string label, long offset, long size, uint spc = 32)
        {
            var p = new PlannedPartition { Name = name, TableLabel = label, Offset = offset, Size = size, SectorsPerCluster = spc, VolumeId = NewVolumeId() };
            p.Clusters = size / p.ClusterSize;
            p.TableEntries = p.Clusters;                             // the original Xbox's count: one entry per cluster
            p.EntrySize = p.TableEntries < 0xFFF0 ? 2 : 4;
            p.TableBytes = (p.TableEntries * p.EntrySize + 0xFFF) & ~0xFFFL;
            return p;
        }

        /// <summary>
        /// XBpartitioner's sector 0 (partition.h, part_setup_standard_partitions): "****PARTINFO****", 32 zero bytes, then 14
        /// entries of 32 bytes - name (16, space-padded), flags (0x80000000 = in use), first sector, sector count, 0. Entries
        /// 0-4 are E: C: X: Y: Z:, 5 is F:, 6 is G: (both named even when not in use, starting at F:'s sector), 7-13 are
        /// spaces. The rest of the sector is zero.
        /// </summary>
        private static byte[] OgTableSector(DrivePlan plan, bool f, bool g, long fSectors, long gStartSector, long gSectors)
        {
            var s = new byte[512];
            Encoding.ASCII.GetBytes("****PARTINFO****", 0, 16, s, 0);
            void Entry(int i, string label, bool used, long start, long count)
            {
                int at = 0x30 + i * 0x20;
                Encoding.ASCII.GetBytes((label ?? "").PadRight(16).Substring(0, 16), 0, 16, s, at);
                BE.WLE32(s, at + 16, used ? 0x80000000u : 0);
                BE.WLE32(s, at + 20, (uint)start);
                BE.WLE32(s, at + 24, (uint)count);
            }
            for (int i = 0; i < OgFixed.Length; i++) Entry(i, OgFixed[i].Label, true, OgFixed[i].Offset / Sector, OgFixed[i].Size / Sector);
            long fStart = OgFStart / Sector;
            Entry(5, "XBOX F", f, fStart, f ? fSectors : 0);
            Entry(6, "XBOX G", g, g ? gStartSector : fStart, g ? gSectors : 0);
            for (int i = 7; i < 14; i++) Entry(i, "", false, 0, 0);
            return s;
        }

        /// <summary>
        /// What layout the drive in <paramref name="s"/> has now (to offer the same again), or null when it has none that is
        /// recognised (a blank drive, or something else).
        /// </summary>
        public static DriveLayout? Detect(Stream s, out long fSize)
        {
            fSize = 0;
            long len = s.Length;
            string Magic(long off)
            {
                if (off + 4 > len) return null;
                var m = new byte[4];
                lock (s) { s.Position = off; if (ReadFull(s, m, 4) != 4) return null; }
                return Encoding.ASCII.GetString(m);
            }
            if (Magic(FatxVolume.DataOffset360) == "XTAF") return DriveLayout.Xbox360;
            var table = FatxVolume.OgTable(s, len);
            if (table.Count > 0)
            {
                var fe = table.FirstOrDefault(p => p.Off == OgFStart);
                var ge = table.FirstOrDefault(p => p.Name.StartsWith("G:"));
                if (fe.Size == 0) return DriveLayout.OgStock;
                fSize = fe.Size;
                if (ge.Size > 0) return ge.Off == Og137Line && fe.Off + fe.Size == Og137Line ? DriveLayout.OgStandard : DriveLayout.OgCustom;
                return fe.Off + fe.Size >= len - Sector ? DriveLayout.OgFTakesAll : fe.Off + fe.Size == Og137Line ? DriveLayout.OgFOnly137 : DriveLayout.OgFTakesAll;
            }
            if (Magic(0xABE80000) != "FATX") return null;
            if (Magic(OgFStart) != "FATX") return DriveLayout.OgStock;
            return len > Og137Line + 0x2000 && Magic(Og137Line) == "FATX" ? DriveLayout.OgStandard : DriveLayout.OgFTakesAll;
        }

        // ------------------------------------------------------------------ a new image file

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, IntPtr inBuf, int inSize, IntPtr outBuf, int outSize, out int returned, IntPtr overlapped);

        /// <summary>
        /// A new, empty image file of <paramref name="size"/> bytes: sparse where the file system allows it (NTFS - only what
        /// is written takes room, a 1 TB drive image starts at a few MB), otherwise only when the PC has room for all of it.
        /// </summary>
        public static void CreateImageFile(string path, long size)
        {
            using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                bool sparse = DeviceIoControl(fs.SafeFileHandle, 0x000900C4 /* FSCTL_SET_SPARSE */, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                if (!sparse)
                {
                    long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace;
                    if (free < size + (64L << 20))
                        throw new IOException("That drive cannot hold the image as a sparse file (only NTFS can), and it has " + FileSniffer.FormatSize(free) +
                                              " free - the image needs " + FileSniffer.FormatSize(size) + ". Save it on an NTFS drive.");
                }
                fs.SetLength(size);
            }
        }

        // ------------------------------------------------------------------ writing

        /// <summary>
        /// Writes the plan into <paramref name="s"/> (whose length is the drive's): for each partition its header, its whole
        /// table (cleared, with the media marker and the root folder's end mark) and its root folder; then, for the original
        /// Xbox, the partition table at sector 0 and - on a blank drive only - the refurb sector ("BRFR", sector 3). Only what
        /// differs is written (a blank drive gets little more than its headers). Reads the drive back afterwards and throws
        /// when it does not open exactly as planned. <paramref name="progress"/> gets (done, total) bytes.
        /// </summary>
        public static void Apply(Stream s, DrivePlan plan, Action<long, long> progress = null, CancellationToken ct = default(CancellationToken))
        {
            if (s.Length < plan.DriveSize - plan.DriveSize % 0x1000) throw new ArgumentException("The drive is smaller than planned.");
            // whole sectors (G: of the standard layout starts at sector 0x0FFFFFFF - not on a 4 KB boundary, as on every such drive)
            if (plan.Partitions.Any(p => p.Offset < 0x80000 || p.Offset % Sector != 0 || p.Offset + p.Size > plan.DriveSize ||
                                         p.DataOffset + p.ClusterSize > p.Offset + p.Size))
                throw new InvalidOperationException("The plan does not fit the drive.");
            long total = plan.Partitions.Sum(p => 0x1000 + p.TableBytes + p.ClusterSize), done = 0;
            var buf = new byte[1 << 20];
            // where the drive's old original Xbox table put partitions, read before anything is written
            var oldTable = FatxVolume.OgTable(s, s.Length).Select(p => p.Off).ToList();
            foreach (var p in plan.Partitions)
            {
                ct.ThrowIfCancellationRequested();
                WriteIfDifferent(s, p.Offset, Header(p));
                done += 0x1000; progress?.Invoke(done, total);
                // the table: the first 4 KB carries the media marker and the root's end mark, the rest is cleared
                var first = new byte[0x1000];
                if (p.Xbox360)
                {
                    if (p.EntrySize == 2) { first[0] = 0xFF; first[1] = 0xF8; first[2] = 0xFF; first[3] = 0xFF; }
                    else { first[0] = 0xFF; first[1] = 0xFF; first[2] = 0xFF; first[3] = 0xF8; first[4] = 0xFF; first[5] = 0xFF; first[6] = 0xFF; first[7] = 0xFF; }
                }
                else
                {
                    if (p.EntrySize == 2) { first[0] = 0xF8; first[1] = 0xFF; first[2] = 0xFF; first[3] = 0xFF; }
                    else { first[0] = 0xF8; first[1] = 0xFF; first[2] = 0xFF; first[3] = 0xFF; first[4] = 0xFF; first[5] = 0xFF; first[6] = 0xFF; first[7] = 0xFF; }
                }
                WriteIfDifferent(s, p.TableOffset, first);
                done += 0x1000; progress?.Invoke(done, total);
                for (long at = p.TableOffset + 0x1000, end = p.TableOffset + p.TableBytes; at < end;)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = (int)Math.Min(buf.Length, end - at);
                    ClearIfNot(s, at, n, buf);
                    at += n; done += n; progress?.Invoke(done, total);
                }
                // the root folder: FF (no entries) as the consoles leave a new folder - the compatibility partition's own is zeros
                var root = new byte[p.ClusterSize];
                if (!p.Compatibility) for (int i = 0; i < root.Length; i++) root[i] = 0xFF;
                WriteIfDifferent(s, p.RootOffset, root);
                done += p.ClusterSize; progress?.Invoke(done, total);
            }
            ClearLeftovers(s, plan, oldTable);
            if (plan.PartitionTable != null)
            {
                WriteIfDifferent(s, 0, plan.PartitionTable);
                // a blank drive gets the refurb sector the console keeps its first power-on time in ("BRFR", nothing counted yet)
                var refurb = new byte[512];
                lock (s) { s.Position = 3 * Sector; ReadFull(s, refurb, 512); }
                if (refurb.All(b => b == 0))
                {
                    Encoding.ASCII.GetBytes("BRFR", 0, 4, refurb, 0);
                    WriteIfDifferent(s, 3 * Sector, refurb);
                }
            }
            lock (s) s.Flush();
            Verify(s, plan);
        }

        /// <summary>Where either console keeps a partition header (both fixed layouts, and the 137 GB line where G: starts).</summary>
        private static readonly long[] KnownStarts =
        {
            0x80000, 0x80080000, 0x10C030000, 0x10C080000, 0x118EB0000, 0x120EB0000, 0x130EB0000,
            0x2EE80000, 0x5DC80000, 0x8CA80000, 0xABE80000, OgFStart, Og137Line
        };

        /// <summary>
        /// What the drive's old layout leaves behind that would make it read as the old one: a partition header ("FATX" /
        /// "XTAF") where the new layout has none - it now lies in free space - is cleared (4 KB), and an Xbox 360 drive made
        /// from an original Xbox one loses its old partition table (sector 0 cleared - with zeros only; the security sector,
        /// sector 16, is not in that block). Only places the format may write: an Xbox 360 drive nothing in front of SysExt
        /// but that zeroed first block.
        /// </summary>
        private static void ClearLeftovers(Stream s, DrivePlan plan, List<long> oldTable)
        {
            long lowest = plan.Xbox360 ? 0x10C080000 : 0x80000;
            var zero = new byte[0x1000];
            foreach (long at in KnownStarts.Concat(oldTable).Distinct().OrderBy(x => x))
            {
                if (at < lowest || at + 0x1000 > s.Length || plan.Partitions.Any(p => p.Offset == at)) continue;
                // never inside what this format just wrote (a header, a table or a root folder of the new layout)
                if (plan.Partitions.Any(p => at < p.RootOffset + p.ClusterSize && p.Offset < at + 0x1000)) continue;
                var m = new byte[4];
                lock (s) { s.Position = at; ReadFull(s, m, 4); }
                string magic = Encoding.ASCII.GetString(m);
                if (magic == "FATX" || magic == "XTAF") WriteIfDifferent(s, at, zero);
            }
            if (plan.Xbox360)
            {
                var first = new byte[16];
                lock (s) { s.Position = 0; ReadFull(s, first, 16); }
                if (Encoding.ASCII.GetString(first) == "****PARTINFO****") WriteIfDifferent(s, 0, zero);
            }
        }

        /// <summary>The partition's 4 KB header.</summary>
        internal static byte[] Header(PlannedPartition p)
        {
            var h = new byte[0x1000];
            for (int i = 0; i < h.Length; i++) h[i] = 0xFF;
            Encoding.ASCII.GetBytes(p.Xbox360 ? "XTAF" : "FATX", 0, 4, h, 0);
            if (p.Xbox360)
            {
                BE.W32(h, 4, p.VolumeId); BE.W32(h, 8, p.SectorsPerCluster); BE.W32(h, 12, 1);
                if (!p.Compatibility) { h[16] = 0; h[17] = 0; }
                if (p.Data360) Encoding.ASCII.GetBytes("XLID", 0, 4, h, 0x850);
            }
            else
            {
                BE.WLE32(h, 4, p.VolumeId); BE.WLE32(h, 8, p.SectorsPerCluster);
                h[12] = 1; h[13] = 0;                               // one table copy
                h[14] = 0; h[15] = 0; h[16] = 0; h[17] = 0;          // reserved, 0
            }
            return h;
        }

        private static void WriteIfDifferent(Stream s, long at, byte[] want)
        {
            var have = new byte[want.Length];
            lock (s)
            {
                s.Position = at;
                int n = ReadFull(s, have, have.Length);
                if (n == have.Length && have.SequenceEqual(want)) return;
                s.Position = at;
                s.Write(want, 0, want.Length);
            }
        }

        /// <summary>Makes <paramref name="n"/> bytes at <paramref name="at"/> zero, writing only where they are not.</summary>
        private static void ClearIfNot(Stream s, long at, int n, byte[] buf)
        {
            lock (s)
            {
                s.Position = at;
                int got = ReadFull(s, buf, n);
                bool zero = got == n;
                for (int i = 0; zero && i < n; i++) if (buf[i] != 0) zero = false;
                if (zero) return;
                Array.Clear(buf, 0, n);
                s.Position = at;
                s.Write(buf, 0, n);
            }
        }

        private static int ReadFull(Stream s, byte[] b, int count)
        {
            int n = 0;
            while (n < count) { int r = s.Read(b, n, count - n); if (r <= 0) break; n += r; }
            return n;
        }

        /// <summary>
        /// Opens the drive with the program's own reader and checks that every planned partition is found where it was
        /// put, at its size, with its cluster size and an empty root folder - and nothing else. Throws when not.
        /// </summary>
        public static void Verify(Stream s, DrivePlan plan)
        {
            var vols = FatxVolume.OpenStream(s, "formatted drive", false);
            try
            {
                var problems = new List<string>();
                foreach (var p in plan.Partitions)
                {
                    var v = vols.FirstOrDefault(x => x.Offset == p.Offset);
                    if (v == null) { problems.Add(p.Name + " is not found"); continue; }
                    if (v.ClusterSize != p.ClusterSize) problems.Add(p.Name + " has " + v.ClusterSize / 1024 + " KB clusters, not " + p.ClusterSize / 1024);
                    if (v.ClusterCount != p.Clusters) problems.Add(p.Name + " reads as " + v.ClusterCount + " clusters, not " + p.Clusters);
                    if (v.ListRoot().Count != 0) problems.Add(p.Name + "'s root folder is not empty");
                }
                foreach (var v in vols.Where(x => !plan.Partitions.Any(p => p.Offset == x.Offset))) problems.Add("an extra partition reads at 0x" + v.Offset.ToString("X"));
                if (problems.Count > 0) throw new InvalidDataException("The drive does not read back as made: " + string.Join("; ", problems) + ".");
            }
            finally { foreach (var v in vols) v.Dispose(); }
        }
    }
}
