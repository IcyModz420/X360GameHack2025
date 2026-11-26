using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace X360GameHack.Core.Formats
{
    /// <summary>The kind of value a stats field holds (the game's own type numbers).</summary>
    public enum DdlType { Byte = 0, Short = 1, UInt = 2, Int = 3, UInt64 = 4, Float = 5, Fixed = 6, String = 7, Struct = 8, Enum = 9 }

    public sealed class DdlMember
    {
        public string Name;
        public int TotalBits, Offset, StructIndex, ArraySize, EnumIndex;
        public DdlType Type;
        public int ElementBits => TotalBits / Math.Max(1, ArraySize);
    }

    public sealed class DdlStruct { public string Name; public int Bits; public List<DdlMember> Members = new List<DdlMember>(); }
    public sealed class DdlEnum { public string Name; public string[] Members = new string[0]; }

    /// <summary>
    /// A stats definition as Treyarch's Call of Duty games keep it ("DDL"): named structures whose members sit at
    /// fixed bit positions. Structure 0 is the root.
    /// </summary>
    public sealed class DdlDef
    {
        public int Version, Bits;
        public readonly List<DdlStruct> Structs = new List<DdlStruct>();
        public readonly List<DdlEnum> Enums = new List<DdlEnum>();

        public static DdlDef Parse(string text)
        {
            var def = new DdlDef();
            DdlStruct current = null;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0) continue;
                string[] p = raw.Trim().Split(' ');
                if (raw.StartsWith("def "))
                {
                    def.Version = int.Parse(p[1].Substring("version=".Length));
                    def.Bits = int.Parse(p[2].Substring("bits=".Length));
                }
                else if (raw.StartsWith("enum "))
                {
                    int colon = raw.IndexOf(" : ", StringComparison.Ordinal);
                    def.Enums.Add(new DdlEnum { Name = p[2], Members = colon < 0 ? new string[0] : raw.Substring(colon + 3).Split(',') });
                }
                else if (raw.StartsWith("struct "))
                {
                    current = new DdlStruct { Name = p[2], Bits = int.Parse(p[3].Substring("bits=".Length)) };
                    def.Structs.Add(current);
                }
                else if (raw.StartsWith(" ") && current != null)
                {
                    if (p.Length != 7) throw new FormatException("Bad member line: " + raw);
                    current.Members.Add(new DdlMember
                    {
                        Name = p[0], TotalBits = int.Parse(p[1]), Offset = int.Parse(p[2]), Type = (DdlType)int.Parse(p[3]),
                        StructIndex = int.Parse(p[4]), ArraySize = int.Parse(p[5]), EnumIndex = int.Parse(p[6])
                    });
                }
                else throw new FormatException("Bad definition line: " + raw);
            }
            if (def.Structs.Count == 0 || def.Bits <= 0) throw new FormatException("The stats definition is empty.");
            return def;
        }

        /// <summary>Every value in the file, in file order, with a readable path such as playerlevelstats[angola].bestplaythrough.kills.</summary>
        public List<DdlField> Flatten()
        {
            var list = new List<DdlField>();
            Walk(Structs[0], 0, "", list, 0);
            return list;
        }

        private void Walk(DdlStruct s, int baseBit, string prefix, List<DdlField> list, int depth)
        {
            if (depth > 16) throw new FormatException("The stats definition nests too deeply.");
            foreach (var m in s.Members)
            {
                int count = Math.Max(1, m.ArraySize), each = m.ElementBits;
                for (int i = 0; i < count; i++)
                {
                    string index = "";
                    if (count > 1)
                    {
                        string name = m.EnumIndex >= 0 && m.EnumIndex < Enums.Count && i < Enums[m.EnumIndex].Members.Length ? Enums[m.EnumIndex].Members[i] : i.ToString();
                        index = "[" + name + "]";
                    }
                    string path = prefix + m.Name + index;
                    int bit = baseBit + m.Offset + i * each;
                    if (m.Type == DdlType.Struct)
                    {
                        if (m.StructIndex < 0 || m.StructIndex >= Structs.Count) throw new FormatException("Member " + path + " points at a structure that is not there.");
                        Walk(Structs[m.StructIndex], bit, path + ".", list, depth + 1);
                    }
                    else list.Add(new DdlField { Path = path, Bit = bit, Bits = each, Type = m.Type });
                }
            }
        }
    }

    public sealed class DdlField
    {
        public string Path;
        public int Bit, Bits;
        public DdlType Type;
        /// <summary>The top-level group, e.g. "playercareerstats".</summary>
        public string Group { get { int cut = Path.IndexOfAny(new[] { '.', '[' }); return cut < 0 ? Path : Path.Substring(0, cut); } }
        public bool IsText => Type == DdlType.String;
        public ulong Max => Bits >= 64 ? ulong.MaxValue : (1UL << Bits) - 1;
    }

    /// <summary>
    /// A Treyarch stats file (Black Ops II campaign "World State", savegame.foo): a 48-byte header followed by the
    /// bit-packed values. What the game checks when it loads one, read from its code: the word at 0x0A is 2, the
    /// word at 0x0C is 0xBEEF, and the version at 0x04 equals the definition's. The first four bytes are a constant
    /// of the definition (a fresh, empty buffer in the running game carries the same value), not a checksum of
    /// the contents - so values can be changed without anything else needing to be recalculated.
    /// </summary>
    public sealed class TreyarchStats
    {
        public const int HeaderBytes = 0x30;

        public DdlDef Def { get; }
        public byte[] Data { get; }
        public List<DdlField> Fields { get; }
        private readonly Dictionary<string, DdlField> _byPath;

        private TreyarchStats(DdlDef def, byte[] data)
        {
            Def = def; Data = data; Fields = def.Flatten();
            _byPath = Fields.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Null when the bytes are a stats file for this definition, else why not.</summary>
        public static string Problem(byte[] file, DdlDef def)
        {
            if (file == null || file.Length < HeaderBytes) return "The file is too small to be a stats file.";
            if ((file[0x0C] << 8 | file[0x0D]) != 0xBEEF || (file[0x0A] << 8 | file[0x0B]) != 2) return "The file does not have the stats header.";
            int version = file[4] << 24 | file[5] << 16 | file[6] << 8 | file[7];
            if (version != def.Version) return "The file is stats version " + version + "; this editor knows version " + def.Version + " (a different game update).";
            if (file.Length < HeaderBytes + (def.Bits + 7) / 8) return "The file is shorter than its definition says (" + file.Length + " bytes).";
            return null;
        }

        public static TreyarchStats Open(byte[] file, DdlDef def)
        {
            string problem = Problem(file, def);
            if (problem != null) throw new System.IO.InvalidDataException(problem);
            return new TreyarchStats(def, (byte[])file.Clone());
        }

        public DdlField Find(string path)
        {
            DdlField f;
            if (!_byPath.TryGetValue((path ?? "").Trim(), out f)) throw new KeyNotFoundException("There is no value called \"" + path + "\".");
            return f;
        }

        // Bits are numbered from the least significant bit of each byte, and a value's lowest bit comes first.
        private ulong ReadBits(int bit, int count)
        {
            ulong v = 0; long at = (long)HeaderBytes * 8 + bit;
            for (int i = 0; i < count; i++, at++)
                if ((Data[at >> 3] >> (int)(at & 7) & 1) != 0) v |= 1UL << i;
            return v;
        }

        private void WriteBits(int bit, int count, ulong v)
        {
            long at = (long)HeaderBytes * 8 + bit;
            for (int i = 0; i < count; i++, at++)
            {
                byte mask = (byte)(1 << (int)(at & 7));
                if ((v >> i & 1) != 0) Data[at >> 3] |= mask; else Data[at >> 3] &= (byte)~mask;
            }
        }

        public ulong Get(DdlField f)
        {
            if (f.IsText) throw new InvalidOperationException(f.Path + " is text.");
            return ReadBits(f.Bit, Math.Min(64, f.Bits));
        }

        public void Set(DdlField f, ulong value)
        {
            if (f.IsText) throw new InvalidOperationException(f.Path + " is text.");
            if (value > f.Max) throw new ArgumentOutOfRangeException(nameof(value), f.Path + " holds 0 to " + f.Max + ".");
            WriteBits(f.Bit, Math.Min(64, f.Bits), value);
        }

        public string GetText(DdlField f)
        {
            if (!f.IsText) return Get(f).ToString();
            var sb = new StringBuilder();
            for (int i = 0; i < f.Bits / 8; i++) { byte c = (byte)ReadBits(f.Bit + i * 8, 8); if (c == 0) break; sb.Append(c >= 32 && c < 127 ? (char)c : '?'); }
            return sb.ToString();
        }

        /// <summary>Sets a value from what a person typed: a whole number (decimal, or hexadecimal with 0x), or text for a text field.</summary>
        public void SetText(DdlField f, string text)
        {
            text = (text ?? "").Trim();
            if (f.IsText)
            {
                int room = f.Bits / 8;
                if (text.Length >= room) throw new ArgumentException(f.Path + " holds at most " + (room - 1) + " characters.");
                if (text.Any(c => c < 32 || c > 126)) throw new ArgumentException(f.Path + " takes plain letters, digits and punctuation only.");
                for (int i = 0; i < room; i++) WriteBits(f.Bit + i * 8, 8, i < text.Length ? (byte)text[i] : (byte)0);
                return;
            }
            ulong v;
            bool ok = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ulong.TryParse(text.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out v)
                : ulong.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out v);
            if (!ok) throw new ArgumentException("\"" + text + "\" is not a whole number (" + f.Path + " holds 0 to " + f.Max + ").");
            Set(f, v);
        }
    }

    /// <summary>One click that sets a group of values.</summary>
    public sealed class GameSaveAction
    {
        public string Name, Does;
        public Func<TreyarchStats, int> Apply;
    }

    /// <summary>A game save this program can open as named values.</summary>
    public sealed class GameSaveKind
    {
        public string Game, What, InnerFile;
        public uint TitleId;
        public DdlDef Def;
        public List<GameSaveAction> Actions = new List<GameSaveAction>();
    }

    public static class GameSaves
    {
        // Save-game editors are mods: none is built into the program. Each one is a .x360mod file (free/open, or
        // paid and licensed) that registers here when it is loaded. They are kept only in memory.
        private static readonly List<GameSaveKind> _external = new List<GameSaveKind>();

        public static IEnumerable<GameSaveKind> All { get { lock (_external) return _external.ToList(); } }

        /// <summary>Adds a save mod loaded from a licensed file (replacing any earlier one for the same game + file).</summary>
        public static void Register(GameSaveKind kind)
        {
            lock (_external)
            {
                _external.RemoveAll(k => k.TitleId == kind.TitleId && string.Equals(k.InnerFile, kind.InnerFile, StringComparison.OrdinalIgnoreCase));
                _external.Add(kind);
            }
        }

        /// <summary>Removes every loaded save mod (used when mods are locked again or the program closes).</summary>
        public static void ClearExternal() { lock (_external) _external.Clear(); }

        /// <summary>The kind of game save a file inside a package is, or null when it is not one this program can open.</summary>
        public static GameSaveKind Detect(uint titleId, string innerFile)
            => All.FirstOrDefault(k => k.TitleId == titleId && string.Equals(k.InnerFile, innerFile, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Call of Duty: Ghosts keeps recent custom-game settings in "cmr_history_..." files whose first four bytes
        /// are a CRC-32 of everything after them (measured on a real save). Returns true when it had to be corrected.
        /// </summary>
        public static bool FixGhostsChecksum(byte[] file)
        {
            if (file == null || file.Length < 8) throw new System.IO.InvalidDataException("The file is too small.");
            uint crc = Crc32(file, 4, file.Length - 4);
            uint stored = (uint)(file[0] << 24 | file[1] << 16 | file[2] << 8 | file[3]);
            if (stored == crc) return false;
            file[0] = (byte)(crc >> 24); file[1] = (byte)(crc >> 16); file[2] = (byte)(crc >> 8); file[3] = (byte)crc;
            return true;
        }

        public static uint Crc32(byte[] d, int start, int count)
        {
            uint c = 0xFFFFFFFF;
            for (int i = start; i < start + count; i++)
            {
                c ^= d[i];
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            return ~c;
        }
    }
}
