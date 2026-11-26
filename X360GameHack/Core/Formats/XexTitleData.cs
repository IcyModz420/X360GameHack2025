using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace X360GameHack.Core.Formats
{
    /// <summary>One achievement as a game declares it in its own title data.</summary>
    public sealed class TitleAchievement
    {
        public ushort Id, NameString, DescriptionString, UnachievedString, Gamerscore;
        public uint ImageId, Flags;
    }

    /// <summary>
    /// The title data a game carries inside its XEX (a resource named after the title ID; an XDBF container often
    /// called the SPA): the achievement list ("XACH": 0x24-byte records { id, name / description / unachieved string
    /// ids, picture id, gamerscore, -, flags, 16 unused bytes }), string tables per language ("XSTR": id, byte
    /// length, UTF-8 text), pictures, and the title header ("XTHD", title ID at +0xC). This is where a console takes a
    /// game's achievement database from the first time the game runs.
    /// </summary>
    public sealed class XexTitleData
    {
        private const ulong Xach = 0x58414348, Xthd = 0x58544844, Xstc = 0x58535443;

        public uint TitleId { get; private set; }
        public string Name { get; private set; } = "";
        public List<TitleAchievement> Achievements { get; } = new List<TitleAchievement>();
        public Dictionary<ushort, string> Strings { get; } = new Dictionary<ushort, string>();
        public Dictionary<ulong, byte[]> Images { get; } = new Dictionary<ulong, byte[]>();

        /// <summary>Reads the title data out of a game's XEX. Throws when the file holds none (system files, some homebrew).</summary>
        public static XexTitleData FromXex(string xexPath)
        {
            return FromImage(XexImage.Load(xexPath), XexFile.Read(xexPath).TitleId);
        }

        /// <summary>The title data inside an already decoded XEX.</summary>
        public static XexTitleData FromImage(XexImage image, uint titleId)
        {
            // the resource is named after the title ID; fall back to any resource that is an XDBF container
            var candidates = image.Resources.OrderByDescending(r => string.Equals(r.Name, titleId.ToString("X8"), StringComparison.OrdinalIgnoreCase));
            foreach (var r in candidates)
            {
                byte[] data = image.ReadResource(r);
                if (data == null || data.Length < 0x18 || BE.U32(data, 0) != XdbfFile.Magic) continue;
                return FromSpa(data);
            }
            throw new InvalidDataException("This XEX carries no title data (no achievement list). Only a game's main executable has it.");
        }

        public static XexTitleData FromSpa(byte[] spa)
        {
            var x = XdbfFile.Parse(spa);
            var t = new XexTitleData();

            var header = x.Find((XdbfFile.Ns)1, Xthd);
            if (header == null || header.Data.Length < 0x10 || BE.U32(header.Data, 0) != (uint)Xthd) throw new InvalidDataException("The title data has no title header.");
            t.TitleId = BE.U32(header.Data, 0x0C);

            // strings: English (1) when present, else the title's default language
            uint language = 1;
            var config = x.Find((XdbfFile.Ns)1, Xstc);
            if (x.Find((XdbfFile.Ns)3, 1) == null && config != null && config.Data.Length >= 0x10) language = BE.U32(config.Data, 0x0C);
            var table = x.Find((XdbfFile.Ns)3, language) ?? x.OfNamespace((XdbfFile.Ns)3).FirstOrDefault();
            if (table != null)
            {
                byte[] d = table.Data;
                if (d.Length < 0x0E || BE.U32(d, 0) != 0x58535452) throw new InvalidDataException("The title data's string table is malformed.");
                int count = BE.U16(d, 0x0C), p = 0x0E;
                for (int i = 0; i < count; i++)
                {
                    if (p + 4 > d.Length) throw new InvalidDataException("The title data's string table is cut short.");
                    ushort id = BE.U16(d, p); int length = BE.U16(d, p + 2);
                    p += 4;
                    if (p + length > d.Length) throw new InvalidDataException("The title data's string table is cut short.");
                    t.Strings[id] = Encoding.UTF8.GetString(d, p, length);
                    p += length;
                }
            }
            t.Strings.TryGetValue(0x8000, out string name);
            t.Name = name ?? "";

            var list = x.Find((XdbfFile.Ns)1, Xach);
            if (list != null)
            {
                byte[] d = list.Data;
                if (d.Length < 0x0E || BE.U32(d, 0) != (uint)Xach) throw new InvalidDataException("The title data's achievement list is malformed.");
                int count = BE.U16(d, 0x0C);
                if (0x0E + count * 0x24 > d.Length) throw new InvalidDataException("The title data's achievement list is cut short.");
                for (int i = 0; i < count; i++)
                {
                    int p = 0x0E + i * 0x24;
                    t.Achievements.Add(new TitleAchievement
                    {
                        Id = BE.U16(d, p), NameString = BE.U16(d, p + 2), DescriptionString = BE.U16(d, p + 4), UnachievedString = BE.U16(d, p + 6),
                        ImageId = BE.U32(d, p + 8), Gamerscore = BE.U16(d, p + 12), Flags = BE.U32(d, p + 16)
                    });
                }
            }
            foreach (var e in x.OfNamespace((XdbfFile.Ns)2))
                if (e.Data.Length > 8 && e.Data[0] == 0x89 && e.Data[1] == 0x50) t.Images[e.Id] = e.Data;
            return t;
        }

        private string Text(ushort id) => Strings.TryGetValue(id, out string s) ? s : "";

        /// <summary>
        /// Title data made from the achievement list of the public title database (it has no pictures): the same
        /// achievement records the game's own data gives, built by <see cref="BuildTitleGpd"/>. (Checked on Call of Duty:
        /// Ghosts: every id, gamerscore, flag and name equals the game's own XEX; some descriptions are the newer
        /// wording Microsoft gave them later.)
        /// </summary>
        public static XexTitleData FromOnline(uint titleId, string name, IEnumerable<Services.OnlineAchievement> list)
        {
            var t = new XexTitleData { TitleId = titleId, Name = name ?? "" };
            ushort next = 1;
            foreach (var a in list)
            {
                if (next > 0x7FF0) break;
                ushort n = next++, d = next++, u = next++;
                t.Strings[n] = a.Name ?? ""; t.Strings[d] = a.Description ?? ""; t.Strings[u] = a.LockedDescription ?? "";
                t.Achievements.Add(new TitleAchievement { Id = (ushort)a.Id, NameString = n, DescriptionString = d, UnachievedString = u, Gamerscore = (ushort)a.Gamerscore, Flags = a.Flags });
            }
            return t;
        }

        /// <summary>
        /// The game's achievement database as a console first writes it into a profile: every achievement not earned,
        /// sync ids 1..N, the game's name as string 0x8000 and its pictures. (Checked against a console-written
        /// database of the same game: the achievement records, sync records and name are identical.)
        /// </summary>
        public byte[] BuildTitleGpd()
        {
            var g = XdbfFile.Create();
            foreach (var a in Achievements.OrderBy(a => a.Id))
            {
                byte[] name = Encoding.BigEndianUnicode.GetBytes(Text(a.NameString) + "\0"),
                       description = Encoding.BigEndianUnicode.GetBytes(Text(a.DescriptionString) + "\0"),
                       unachieved = Encoding.BigEndianUnicode.GetBytes(Text(a.UnachievedString) + "\0");
                var d = new byte[0x1C + name.Length + description.Length + unachieved.Length];
                BE.W32(d, 0x00, 0x1C);
                BE.W32(d, 0x04, a.Id);
                BE.W32(d, 0x08, a.ImageId);
                BE.W32(d, 0x0C, a.Gamerscore);
                BE.W32(d, 0x10, a.Flags);
                Buffer.BlockCopy(name, 0, d, 0x1C, name.Length);
                Buffer.BlockCopy(description, 0, d, 0x1C + name.Length, description.Length);
                Buffer.BlockCopy(unachieved, 0, d, 0x1C + name.Length + description.Length, unachieved.Length);
                g.Add(XdbfFile.Ns.Achievement, a.Id, d);
            }
            g.ResetSync(XdbfFile.Ns.Achievement);
            foreach (var image in Images.OrderBy(i => i.Key)) g.Add(XdbfFile.Ns.Image, image.Key, (byte[])image.Value.Clone());
            g.ResetSync(XdbfFile.Ns.Setting);
            g.Add(XdbfFile.Ns.String, 0x8000, Encoding.BigEndianUnicode.GetBytes(Name + "\0"));
            return g.ToBytes();
        }
    }
}
