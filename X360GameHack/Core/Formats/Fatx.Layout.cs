using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace X360GameHack.Core.Formats
{
    /// <summary>
    /// One place on a whole drive, as Drive Explorer's Partitions tab shows it: a partition that was read, a place the drive's
    /// layout has a partition that cannot be read (and what is there instead), or space no partition uses.
    /// </summary>
    public sealed class PartitionSlot
    {
        /// <summary>The short name on its badge and in the drive map: the drive letter on an original Xbox drive ("E:"), a short code on an Xbox 360 drive ("DATA").</summary>
        public string Tag = "";
        /// <summary>As the partition list names it ("E: Data (XBOX SHELL)"), or what the place is.</summary>
        public string Name = "";
        public long Offset, Size;
        /// <summary>The file system read there; null when there is none that can be read.</summary>
        public FatxVolume Volume;
        /// <summary>With no file system read: what is there instead.</summary>
        public string Note = "";
        public bool IsUnused => Volume == null && Name == UnusedName;
        public bool IsCache => Volume == null && Note.StartsWith("The console's cache");

        public const string UnusedName = "Unused space";
    }

    public sealed partial class FatxVolume
    {
        /// <summary>Space between partitions (or after the last) smaller than this is not worth showing.</summary>
        public const long MinUnused = 16L << 20;

        /// <summary>
        /// Every partition of the drive these volumes were read from, in the order they sit on it: each one read, each place
        /// the drive's layout has one that cannot be read (with what is there instead), and the space no partition uses. The
        /// layout is the drive's own partition table when it has one (an original Xbox drive set up by XBpartitioner, a
        /// development kit drive), else the places the console's kernel uses. A single partition dump is just itself. Reads a
        /// few headers only.
        /// </summary>
        public static List<PartitionSlot> Layout(IList<FatxVolume> found)
        {
            var slots = new List<PartitionSlot>();
            if (found == null || found.Count == 0) return slots;
            var s = found[0]._s;
            long len;
            lock (s) len = s.Length;
            bool x360 = found.Any(v => v.BigEndian);
            if (found.Count == 1 && found[0].Offset == 0)       // a single partition dump
            {
                var v = found[0];
                return new List<PartitionSlot> { new PartitionSlot { Tag = TagOf(v.Name), Name = v.Name, Offset = 0, Size = v.Length, Volume = v } };
            }
            string Magic(long off)
            {
                if (off < 0 || off + 4 > len) return null;
                var m = new byte[4];
                lock (s) { s.Position = off; if (ReadFull(s, m, 0, 4) != 4) return null; }
                return Encoding.ASCII.GetString(m);
            }

            var places = new List<(long Off, long Size, string Name, bool Cache)>();
            if (x360)
            {
                var dev = DevkitTable(s, len);
                if (dev.Count > 0 && found.Any(v => dev.Any(d => d.Off == v.Offset))) places.AddRange(dev.Select(d => (d.Off, d.Size, d.Name, false)));
                else places.AddRange(X360Layout.Where(p => p.Off != 0x10C030000).Select(p => (p.Off, p.Size, p.Name, p.Off < 0x10C030000)));
            }
            else
            {
                var table = OgTable(s, len);
                if (table.Count > 0) places.AddRange(table.Select(t => (t.Off, t.Size, t.Name, false)));
                else
                {
                    places.AddRange(OgLayout.Where(p => p.Off < 0x1DD156000).Select(p => (p.Off, p.Size, p.Name, false)));
                    // past the stock 8 GB: F: (a drive bigger than that has room for it), and G: when one starts at the last
                    // 28-bit sector - where LBA48 BIOSes look for it on a drive without a table
                    bool g = found.Any(v => v.Offset == OgGStart) || (len > OgGStart + 0x2000 && Magic(OgGStart) == "FATX");
                    if (len > 0x1DD156000 + MinUnused) places.Add((0x1DD156000, (g ? OgGStart : len) - 0x1DD156000, "F: Extended", false));
                    if (g) places.Add((OgGStart, len - OgGStart, "G: Extended", false));
                }
            }
            // a volume read somewhere the layout does not name is shown as well
            foreach (var v in found) if (!places.Any(p => p.Off == v.Offset)) places.Add((v.Offset, v.Length, v.Name, false));
            places = places.Where(p => p.Off < len).OrderBy(p => p.Off).ToList();

            for (int i = 0; i < places.Count; i++)
            {
                var p = places[i];
                long room = (i + 1 < places.Count ? places[i + 1].Off : len) - p.Off;
                var v = found.FirstOrDefault(x => x.Offset == p.Off);
                var slot = new PartitionSlot { Name = v?.Name ?? p.Name, Offset = p.Off, Volume = v };
                slot.Tag = TagOf(slot.Name);
                if (v != null) slot.Size = Math.Min(v.Length, room);
                else
                {
                    slot.Size = p.Size > 0 ? Math.Min(p.Size, room) : room;
                    string m = Magic(p.Off);
                    slot.Note = p.Cache ? (m == "XTAF" ? "The console's cache - its own working data, not files (never opened as a file system)" : "The console's cache - empty")
                              : m == "FATX" || m == "XTAF" ? "A file system header is here, but its folders cannot be read - damaged, or laid out differently"
                              : "Not formatted - no file system here";
                }
                slots.Add(slot);
            }

            // what no partition uses: before the first (past the drive's own sectors), between them, and after the last
            var all = new List<PartitionSlot>();
            long at = slots.Count > 0 ? slots[0].Offset : len;
            foreach (var sl in slots)
            {
                if (sl.Offset - at >= MinUnused) all.Add(Unused(at, sl.Offset - at, x360));
                all.Add(sl);
                at = Math.Max(at, sl.Offset + sl.Size);
            }
            if (len - at >= MinUnused) all.Add(Unused(at, len - at, x360));
            return all;
        }

        private static PartitionSlot Unused(long off, long size, bool x360) => new PartitionSlot
        {
            Name = PartitionSlot.UnusedName, Offset = off, Size = size,
            Note = "No partition uses this space" + (x360 ? "" : " - Make Xbox drive... (Drive tools) can lay the drive out to use it; that erases the drive")
        };

        /// <summary>A partition's short name: its drive letter on an original Xbox drive, a short code on an Xbox 360 drive.</summary>
        internal static string TagOf(string name)
        {
            name = name ?? "";
            if (name.Length >= 2 && name[1] == ':' && char.IsLetter(name[0])) return name.Substring(0, 2).ToUpperInvariant();
            if (name.StartsWith("Content")) return "DATA";
            if (name.StartsWith("Compatibility")) return "HDDX";
            if (name.StartsWith("SysExt2")) return "SX2";
            if (name.StartsWith("SysExt")) return "SX";
            if (name.StartsWith("System cache") || name.StartsWith("Cache1")) return "C1";
            if (name.StartsWith("Cache")) return "C0";
            if (name.StartsWith("System")) return "SYS";
            var letters = new string(name.Where(char.IsLetterOrDigit).Take(4).ToArray()).ToUpperInvariant();
            return letters.Length > 0 ? letters : "?";
        }
    }
}
