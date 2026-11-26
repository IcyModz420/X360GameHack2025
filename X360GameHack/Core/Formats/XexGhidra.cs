using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace X360GameHack.Core.Formats
{
    public sealed class XexSection
    {
        public string Name = "";
        public uint Address, Size;
        public bool Read = true, Write, Execute;
        public string Access => (Read ? "r" : "-") + (Write ? "w" : "-") + (Execute ? "x" : "-");
    }

    public sealed class XexImport
    {
        public string Library = "";
        public int LibraryIndex, Ordinal;
        /// <summary>True: a 16-byte call stub the kernel fills in when the module loads. False: a pointer slot.</summary>
        public bool IsFunction;
        public uint Address;
        /// <summary>The export's real name when it is known, else null.</summary>
        public string Name;
        public string Label => Name ?? (System.IO.Path.GetFileNameWithoutExtension(Library) + "_" + Ordinal.ToString("X", CultureInfo.InvariantCulture));
    }

    public sealed class XexExport
    {
        public int Ordinal;
        public uint Address;
        public string Name;
        public string Label => Name ?? ("export_" + Ordinal.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// What a disassembler needs to know about a decoded XEX beyond its bytes: the PE sections of the base file, the
    /// entry point, every function start the image's own function table (.pdata) lists, the imports (a XEX imports
    /// by ordinal: each record is the address of a slot whose top byte says "pointer" (0) or "call stub" (1) and
    /// whose low 16 bits are the ordinal) and the exports of system modules.
    /// </summary>
    public sealed class XexProgram
    {
        public uint ImageBase, ImageSize, EntryPoint;
        public string ModuleName = "";
        public List<XexSection> Sections { get; } = new List<XexSection>();
        public List<uint> Functions { get; } = new List<uint>();
        public List<XexImport> Imports { get; } = new List<XexImport>();
        public List<XexExport> Exports { get; } = new List<XexExport>();
        public List<string> Libraries { get; } = new List<string>();
        /// <summary>False when the PE headers did not describe the image and it is treated as one block.</summary>
        public bool SectionsKnown { get; private set; } = true;

        public static XexProgram Read(string xexPath)
        {
            byte[] file = BE.ReadAllShared(xexPath);
            return Read(file, XexImage.Load(file));
        }

        public static XexProgram Read(byte[] file, XexImage image)
        {
            var p = new XexProgram { ImageBase = image.ImageBase, ImageSize = (uint)image.BaseFile.Length, EntryPoint = image.EntryPoint };
            byte[] b = image.BaseFile;
            uint dataOffset = BE.U32(file, 0x08), sec = BE.U32(file, 0x10), count = BE.U32(file, 0x14);
            uint importsAt = 0, nameAt = 0;
            for (uint i = 0; i < count; i++)
            {
                int e = (int)(0x18 + i * 8);
                uint key = BE.U32(file, e), value = BE.U32(file, e + 4);
                if (key == 0x000103FF) importsAt = value;
                else if (key == 0x000183FF) nameAt = value;
            }
            if (nameAt != 0 && (long)nameAt + 4 <= dataOffset)
            {
                uint size = BE.U32(file, (int)nameAt);
                if (size > 4 && size <= 0x200 && (long)nameAt + size <= dataOffset)
                    p.ModuleName = Encoding.ASCII.GetString(file, (int)nameAt + 4, (int)size - 4).Split('\0')[0];
            }

            p.ReadSections(b);
            // Some homebrew strips or scrambles its PE section table. When the headers do not describe where the
            // code is, the whole image is offered as one section and nothing is claimed about its inside.
            bool entryInCode = p.EntryPoint == 0 || p.Sections.Any(s => s.Execute && p.EntryPoint >= s.Address && p.EntryPoint - s.Address < s.Size);
            if (p.Sections.Count == 0 || !p.Sections.Any(s => s.Execute) || !entryInCode)
            {
                p.Sections.Clear();
                p.Sections.Add(new XexSection { Name = "image", Address = p.ImageBase, Size = p.ImageSize, Read = true, Write = true, Execute = true });
                p.SectionsKnown = false;
                p._exceptionRva = p._exceptionSize = 0;
            }
            p.ReadFunctionTable(b);
            if (importsAt != 0) p.ReadImports(file, importsAt, dataOffset, b);
            p.ReadExports(b, BE.U32(file, (int)sec + 0x160));
            return p;
        }

        /// <summary>
        /// The function that contains <paramref name="address"/>: [start, end). Taken from the image's function table;
        /// where the table has no entry (leaf functions), from the address up to and including the next return.
        /// Trailing padding is left out. Null when the address is not inside the image.
        /// </summary>
        public Tuple<uint, uint> FunctionAt(uint address, byte[] image)
        {
            address &= ~3u;
            if (!Inside(address, 4)) return null;
            var section = Sections.FirstOrDefault(s => address >= s.Address && address - s.Address < s.Size);
            uint limit = section != null ? section.Address + section.Size : ImageBase + ImageSize;
            int lo = 0, hi = Functions.Count - 1, found = -1;
            while (lo <= hi) { int mid = (lo + hi) / 2; if (Functions[mid] <= address) { found = mid; lo = mid + 1; } else hi = mid - 1; }
            uint next = found + 1 < Functions.Count ? Math.Min(limit, Functions[found + 1]) : limit;
            uint start, end;
            Func<uint, uint> word = a => BE.U32(image, (int)(a - ImageBase));
            if (found >= 0 && (section == null || Functions[found] >= section.Address))
            {
                start = Functions[found]; end = next;
            }
            else { start = address; end = next; }
            if (start == address && (found < 0 || Functions[found] != address))
            {
                // not a listed start: read up to the first return
                for (uint a = start; a + 4 <= end && a - start < 0x8000; a += 4)
                    if (word(a) == 0x4E800020) { end = a + 4; break; }
            }
            if (end - start > 0x40000) end = start + 0x40000;
            while (end - 4 > start && end - 4 >= ImageBase && word(end - 4) == 0) end -= 4;      // padding
            return end > start ? Tuple.Create(start, end) : null;
        }

        /// <summary>
        /// The compiler's register save / restore helpers (__savegprlr_14..31, __restgprlr_14..31): one run of
        /// "std rN, -x(r1)" (or "ld") instructions that functions enter at the register they need.
        /// </summary>
        public Dictionary<uint, string> HelperNames(byte[] image)
        {
            var names = new Dictionary<uint, string>();
            foreach (var s in Sections.Where(s => s.Execute))
                for (uint a = s.Address; a + 80 <= s.Address + s.Size; a += 4)
                {
                    uint w = BE.U32(image, (int)(a - ImageBase));
                    if (w != 0xF9C1FF68 && w != 0xE9C1FF68) continue;
                    bool save = w == 0xF9C1FF68, run = true;
                    for (int n = 0; n < 18 && run; n++)
                        run = BE.U32(image, (int)(a - ImageBase) + n * 4) == ((save ? 0xF8010000u : 0xE8010000u) | (uint)(14 + n) << 21 | (uint)((-0x98 + n * 8) & 0xFFFF));
                    if (!run) continue;
                    for (int n = 0; n < 18; n++) names[a + (uint)n * 4] = (save ? "__savegprlr_" : "__restgprlr_") + (14 + n);
                }
            return names;
        }

        private bool Inside(long address, long length = 1) => address >= ImageBase && address + length <= (long)ImageBase + ImageSize;
        private static uint LE32(byte[] b, long o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        private static int LE16(byte[] b, long o) => b[o] | b[o + 1] << 8;

        private long _exceptionRva, _exceptionSize;

        // The base file starts with ordinary PE headers (little-endian, as PE headers always are).
        private void ReadSections(byte[] b)
        {
            if (b.Length < 0x40 || b[0] != 'M' || b[1] != 'Z') return;
            long pe = LE32(b, 0x3C);
            if (pe < 0x40 || pe + 0x18 > b.Length || LE32(b, pe) != 0x00004550) return;
            int sections = LE16(b, pe + 6), optionalSize = LE16(b, pe + 0x14);
            long optional = pe + 0x18, table = optional + optionalSize;
            if (sections > 96 || table + sections * 0x28L > b.Length) return;
            if (optionalSize >= 0x60 + 4 * 8 && LE16(b, optional) == 0x10B)
            {
                _exceptionRva = LE32(b, optional + 0x60 + 3 * 8);
                _exceptionSize = LE32(b, optional + 0x60 + 3 * 8 + 4);
            }
            for (int i = 0; i < sections; i++)
            {
                long s = table + i * 0x28L;
                string name = Encoding.ASCII.GetString(b, (int)s, 8).Split('\0')[0];
                uint virtualSize = LE32(b, s + 8), rva = LE32(b, s + 12), rawSize = LE32(b, s + 16), flags = LE32(b, s + 0x24);
                uint size = virtualSize != 0 ? virtualSize : rawSize;
                if (size == 0 || rva >= ImageSize) continue;
                if ((long)rva + size > ImageSize) size = ImageSize - rva;
                Sections.Add(new XexSection
                {
                    Name = name.Length > 0 ? name : "section" + i, Address = ImageBase + rva, Size = size,
                    Read = (flags & 0x40000000) != 0 || (flags & 0xE0000000) == 0, Write = (flags & 0x80000000) != 0, Execute = (flags & 0x20000000) != 0
                });
            }
            Sections.Sort((x, y) => x.Address.CompareTo(y.Address));
        }

        // .pdata: one 8-byte record per function { start address, packed lengths and flags }, big-endian, sorted.
        private void ReadFunctionTable(byte[] b)
        {
            long at = _exceptionRva, size = _exceptionSize;
            if (at == 0 || size < 8)
            {
                var pdata = Sections.FirstOrDefault(s => s.Name == ".pdata");
                if (pdata == null) return;
                at = pdata.Address - ImageBase; size = pdata.Size;
            }
            if (at + size > b.Length) size = b.Length - at;
            var code = Sections.Where(s => s.Execute).ToList();
            var seen = new HashSet<uint>();
            for (long o = at; o + 8 <= at + size; o += 8)
            {
                uint start = BE.U32(b, (int)o);
                if (start == 0) continue;
                if ((start & 3) != 0 || !Inside(start, 4)) continue;
                if (code.Count > 0 && !code.Any(s => start >= s.Address && start - s.Address < s.Size)) continue;
                if (seen.Add(start)) Functions.Add(start);
            }
            Functions.Sort();
        }

        private void ReadImports(byte[] file, uint at, uint limit, byte[] b)
        {
            if ((long)at + 12 > limit) return;
            uint size = BE.U32(file, (int)at), stringsSize = BE.U32(file, (int)at + 4), libraries = BE.U32(file, (int)at + 8);
            if (size < 12 || (long)at + size > limit || stringsSize > size - 12 || libraries > 256) return;
            var names = new List<string>();
            for (uint pos = 0; pos < stringsSize;)
            {
                uint end = pos;
                while (end < stringsSize && file[at + 12 + end] != 0) end++;
                names.Add(Encoding.ASCII.GetString(file, (int)(at + 12 + pos), (int)(end - pos)));
                pos = (end + 1 + 3) & ~3u;
            }
            uint p = at + 12 + stringsSize, stop = at + size;
            for (uint i = 0; i < libraries && p + 0x28 <= stop; i++)
            {
                uint librarySize = BE.U32(file, (int)p);
                int nameIndex = BE.U16(file, (int)p + 0x24) & 0xFF, records = BE.U16(file, (int)p + 0x26);
                if (librarySize < 0x28 || p + librarySize > stop || 0x28 + records * 4L > librarySize) break;
                string library = nameIndex < names.Count ? names[nameIndex] : "library" + i;
                int index = Libraries.Count;
                Libraries.Add(library);
                for (int r = 0; r < records; r++)
                {
                    uint address = BE.U32(file, (int)p + 0x28 + r * 4);
                    if (!Inside(address, 4)) continue;
                    uint value = BE.U32(b, (int)(address - ImageBase));
                    uint type = value >> 24;
                    if (type > 1) continue;
                    if (type == 1 && !Inside(address, 16)) continue;
                    int ordinal = (int)(value & 0xFFFF);
                    Imports.Add(new XexImport { Library = library, LibraryIndex = index, Ordinal = ordinal, IsFunction = type == 1, Address = address, Name = XexImportNames.Find(library, ordinal) });
                }
                p += librarySize;
            }
        }

        // System modules list their exports in a table the security header points at:
        // { 3 magic words, 2 module numbers, 3 version words, image base >> 16, count, first ordinal, offsets... }.
        private void ReadExports(byte[] b, uint tableAddress)
        {
            if (tableAddress == 0 || !Inside(tableAddress, 0x2C)) return;
            long t = tableAddress - ImageBase;
            if (BE.U32(b, (int)t) != 0x48000000 || BE.U32(b, (int)t + 4) != 0x00485645 || BE.U32(b, (int)t + 8) != 0x48000000) return;
            uint high = BE.U32(b, (int)t + 0x20), exports = BE.U32(b, (int)t + 0x24), first = BE.U32(b, (int)t + 0x28);
            if (exports > 0x4000 || first > 0xFFFF || !Inside(tableAddress, 0x2C + exports * 4L)) return;
            // debug builds of the system modules carry names like "xamd.dll" / "xboxkrnld.exe"
            string module = ModuleName.ToLowerInvariant();
            module = module.StartsWith("xam") ? "xam" : module.StartsWith("xboxkrnl") ? "xboxkrnl" : module.StartsWith("xbdm") ? "xbdm" : module;
            for (uint i = 0; i < exports; i++)
            {
                uint offset = BE.U32(b, (int)(t + 0x2C + i * 4));
                if (offset == 0) continue;
                uint address = (high << 16) + offset;
                if (!Inside(address, 4)) continue;
                int ordinal = (int)(first + i);
                Exports.Add(new XexExport { Ordinal = ordinal, Address = address, Name = XexImportNames.Find(module, ordinal) });
            }
        }
    }

    public sealed class XexGhidraExport
    {
        public string Folder, ImageFile, MapFile, ScriptFile, BatchFile, ReadmeFile;
        public XexProgram Program;
    }

    /// <summary>
    /// Writes a decoded XEX in a form Ghidra opens directly: the base file (byte for byte), a map of everything in
    /// <see cref="XexProgram"/>, a Ghidra script that applies the map (sections, entry point, function starts, named
    /// imports and exports) and a batch file that builds the Ghidra project in one step.
    /// </summary>
    public static class XexGhidra
    {
        public const string Language = "PowerPC:BE:64:A2ALT-32addr", ScriptName = "X360GameHackXex.java", MapHeader = "x360gamehack-ghidra-map 1";
        /// <summary>The batch file runs the script once more after Ghidra's analysis with this argument; it writes <see cref="FinishedFile"/> into the project folder.</summary>
        public const string FinishedArg = "--finished", FinishedFile = "finished.txt";

        /// <summary>Letters, digits, '_', '-' and '.' only - the name ends up in file names and in a batch file.</summary>
        public static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "") sb.Append((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' ? c : c == '.' && sb.Length > 0 ? '.' : '_');
            string s = sb.ToString().Trim('.', '_');
            if (s.Length > 60) s = s.Substring(0, 60).Trim('.', '_');
            return s.Length == 0 ? "xex" : s;
        }

        private static string SafeLabel(string label)
        {
            var sb = new StringBuilder();
            foreach (char c in label ?? "") sb.Append((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '$' || c == '@' || c == '?' ? c : '_');
            return sb.Length == 0 ? "unnamed" : sb.ToString();
        }

        public static string BuildMap(XexProgram p)
        {
            var sb = new StringBuilder();
            Action<string> line = s => sb.Append(s).Append('\n');
            line(MapHeader);
            line("base " + p.ImageBase.ToString("X8"));
            line("size " + p.ImageSize.ToString("X8"));
            foreach (var s in p.Sections) line("section " + SafeLabel(s.Name) + " " + s.Address.ToString("X8") + " " + s.Size.ToString("X8") + " " + s.Access);
            for (int i = 0; i < p.Libraries.Count; i++) line("library " + i + " " + SafeLabel(p.Libraries[i]));
            var used = new Dictionary<string, int>(StringComparer.Ordinal);
            Func<string, string> unique = label =>
            {
                label = SafeLabel(label);
                if (!used.TryGetValue(label, out int n)) { used[label] = 1; return label; }
                used[label] = n + 1;
                return label + "_" + (n + 1);
            };
            foreach (var i in p.Imports.Where(i => i.IsFunction))
                line("import f " + i.Address.ToString("X8") + " " + i.LibraryIndex + " " + i.Ordinal.ToString("X") + " " + unique(i.Label));
            foreach (var i in p.Imports.Where(i => !i.IsFunction))
                line("import v " + i.Address.ToString("X8") + " " + i.LibraryIndex + " " + i.Ordinal.ToString("X") + " __imp__" + SafeLabel(i.Label));
            foreach (var e in p.Exports) line("export " + e.Address.ToString("X8") + " " + unique(e.Label));
            foreach (uint f in p.Functions) line("func " + f.ToString("X8"));
            if (p.EntryPoint != 0) line("entry " + p.EntryPoint.ToString("X8"));
            return sb.ToString();
        }

        /// <summary>
        /// Creates "<paramref name="parentFolder"/>\name-ghidra" (which must not exist yet) with everything in it.
        /// The XEX itself is only read.
        /// </summary>
        public static XexGhidraExport Export(string xexPath, string parentFolder)
        {
            byte[] file = BE.ReadAllShared(xexPath);
            var image = XexImage.Load(file);
            if (image.BaseFile.Length < 2 || image.BaseFile[0] != 'M' || image.BaseFile[1] != 'Z')
                throw new InvalidDataException("This XEX holds no program (it carries resources only), so there is nothing to disassemble.");
            var program = XexProgram.Read(file, image);
            string name = SafeName(Path.GetFileNameWithoutExtension(xexPath));
            string folder = Path.Combine(parentFolder, name + "-ghidra");
            if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("\"" + folder + "\" already exists - choose another place or remove it first.");
            Directory.CreateDirectory(folder);
            var r = new XexGhidraExport
            {
                Folder = folder, Program = program,
                ImageFile = Path.Combine(folder, name + ".bin"), MapFile = Path.Combine(folder, name + ".ghidra-map"),
                ScriptFile = Path.Combine(folder, ScriptName), BatchFile = Path.Combine(folder, "Create Ghidra project.bat"), ReadmeFile = Path.Combine(folder, "How to open in Ghidra.txt")
            };
            var ascii = new ASCIIEncoding();
            File.WriteAllBytes(r.ImageFile, image.BaseFile);
            File.WriteAllText(r.MapFile, BuildMap(program), ascii);
            File.WriteAllText(r.ScriptFile, Script, ascii);
            File.WriteAllText(r.BatchFile, Batch(name, program.ImageBase), ascii);
            File.WriteAllText(r.ReadmeFile, Readme(name, program), ascii);
            return r;
        }

        private static string Batch(string name, uint imageBase)
        {
            // name is SafeName output (no spaces, quotes, % or shell characters); every path is taken from the batch file's own location
            return string.Join("\r\n", new[]
            {
                "@echo off",
                "setlocal",
                "rem Builds a Ghidra project from the files in this folder. Written by X360GameHack.",
                "set \"GHIDRA=%GHIDRA_INSTALL_DIR%\"",
                "if not \"%~1\"==\"\" set \"GHIDRA=%~1\"",
                "if not exist \"%GHIDRA%\\support\\analyzeHeadless.bat\" (",
                "  echo Ghidra was not found.",
                "  echo Drag your Ghidra folder ^(the one that holds ghidraRun.bat^) onto this file,",
                "  echo or set the GHIDRA_INSTALL_DIR environment variable to it.",
                "  pause",
                "  exit /b 1",
                ")",
                "if exist \"%~dp0ghidra-project\\" + FinishedFile + "\" (",
                "  echo The Ghidra project is already built. In Ghidra: File - Open Project - \"%~dp0ghidra-project\\" + name + ".gpr\"",
                "  echo To build it again, delete the ghidra-project folder first.",
                "  pause",
                "  exit /b 0",
                ")",
                "if not exist \"%~dp0ghidra-project\" mkdir \"%~dp0ghidra-project\"",
                "rem The same headless analyzer analyzeHeadless.bat starts, but with up to half of this PC's memory:",
                "rem analyzeHeadless.bat stops Java at 2 GB, which is too little to analyze a whole game.",
                "call \"%GHIDRA%\\support\\launch.bat\" fg jdk Ghidra-Headless \"\" \"-XX:ParallelGCThreads=2 -XX:CICompilerCount=2 -XX:MaxRAMPercentage=50\" ghidra.app.util.headless.AnalyzeHeadless" +
                    " \"%~dp0ghidra-project\" " + name + " -import \"%~dp0" + name + ".bin\" -loader BinaryLoader -loader-baseAddr 0x" + imageBase.ToString("X8") +
                    " -processor \"" + Language + "\" -cspec default -scriptPath \"%~dp0.\" -preScript " + ScriptName + " \"%~dp0" + name + ".ghidra-map\"" +
                    " -postScript " + ScriptName + " " + FinishedArg + " \"%~dp0ghidra-project\\" + FinishedFile + "\"",
                "echo.",
                "if exist \"%~dp0ghidra-project\\" + FinishedFile + "\" (",
                "  echo Done. In Ghidra: File - Open Project - \"%~dp0ghidra-project\\" + name + ".gpr\"",
                ") else (",
                "  echo Ghidra did not finish - the messages above say why. Nothing outside this folder was changed.",
                "  echo Delete the ghidra-project folder before you try again.",
                ")",
                "pause",
                ""
            });
        }

        private static string Readme(string name, XexProgram p)
        {
            return string.Join("\r\n", new[]
            {
                name + " - exported for Ghidra by X360GameHack",
                "",
                name + ".bin            the decrypted, decompressed program exactly as it sits in memory at 0x" + p.ImageBase.ToString("X8"),
                name + ".ghidra-map     sections (" + p.Sections.Count + "), function starts (" + p.Functions.Count + "), imports (" + p.Imports.Count + "), exports (" + p.Exports.Count + "), entry point",
                ScriptName + "   the Ghidra script that applies the map",
                "",
                "THE QUICK WAY",
                "  Drag your Ghidra folder onto \"Create Ghidra project.bat\" (or just double-click it when the",
                "  GHIDRA_INSTALL_DIR environment variable is set). It imports the program, applies the map, runs",
                "  Ghidra's analysis and leaves a project in the \"ghidra-project\" folder. Open that project in Ghidra.",
                "  A whole game takes a few minutes and several GB of memory; it says Done when the project is ready.",
                "",
                "BY HAND",
                "  1. In Ghidra: File - Import File - " + name + ".bin",
                "       Format:    Raw Binary",
                "       Language:  " + Language + "   (PowerPC, 64 bit, big endian, 32-bit addresses)",
                "       Options:   Base Address  " + p.ImageBase.ToString("X8"),
                "  2. Open it and answer No when Ghidra offers to analyze.",
                "  3. Window - Script Manager - Manage Script Directories - add this folder, then run " + ScriptName,
                "     and choose " + name + ".ghidra-map when it asks.",
                "  4. Analysis - Auto Analyze.",
                "",
                "WHAT THE SCRIPT DOES",
                "  - splits the image into its sections with the right read / write / execute flags",
                "  - names every import. A XEX imports by ordinal; the names of kernel, XAM and XBDM exports are filled in.",
                "    The 16-byte import stubs hold placeholder data on disk (the console writes the real jump when the",
                "    game loads); inside the Ghidra project only, their first two words become \"li r3,library; li r4,ordinal\"",
                "    so they disassemble as small named functions. The .bin file is never changed.",
                "  - creates a function at every start the game's own function table lists, and at the entry point",
                "",
                "Addresses in Ghidra are the same as in the XEX Disassembler's memory-address view and on the console.",
                ""
            });
        }

        /// <summary>The Ghidra script (Java, so it runs in every Ghidra without extra setup).</summary>
        public const string Script =
@"// Applies a map written by X360GameHack to an Xbox 360 program imported as a raw binary.
//@category Xbox360
//@menupath Tools.X360GameHack.Apply XEX map

import java.io.BufferedReader;
import java.io.File;
import java.io.FileInputStream;
import java.io.InputStreamReader;
import java.util.ArrayList;
import java.util.List;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.data.PointerDataType;
import ghidra.program.model.listing.Function;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.SourceType;

public class X360GameHackXex extends GhidraScript {

    private Memory memory;
    private int functions, imports, exports, sections;

    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length == 2 && args[0].equals(""" + FinishedArg + @""")) {
            // run by the batch file after Ghidra's analysis: the import got this far
            java.io.PrintWriter done = new java.io.PrintWriter(new File(args[1]), ""US-ASCII"");
            try { done.println(""Imported and analyzed by Ghidra: "" + currentProgram.getFunctionManager().getFunctionCount() + "" functions.""); }
            finally { done.close(); }
            return;
        }
        File map = args.length > 0 ? new File(args[0]) : askFile(""Choose the .ghidra-map file X360GameHack wrote"", ""Apply"");
        if (!""PowerPC"".equalsIgnoreCase(currentProgram.getLanguage().getProcessor().toString())) {
            printerr(""This program is not PowerPC. Import the .bin file as Raw Binary with language " + Language + @"."");
            return;
        }
        List<String[]> lines = new ArrayList<String[]>();
        BufferedReader reader = new BufferedReader(new InputStreamReader(new FileInputStream(map), ""US-ASCII""));
        try {
            String first = reader.readLine();
            if (first == null || !first.trim().equals(""" + MapHeader + @""")) {
                printerr(""That file is not a map written by X360GameHack."");
                return;
            }
            for (String line = reader.readLine(); line != null; line = reader.readLine()) {
                line = line.trim();
                if (line.length() > 0) lines.add(line.split("" ""));
            }
        }
        finally {
            reader.close();
        }

        memory = currentProgram.getMemory();
        long base = 0, size = 0;
        for (String[] l : lines) {
            if (l[0].equals(""base"")) base = Long.parseLong(l[1], 16);
            if (l[0].equals(""size"")) size = Long.parseLong(l[1], 16);
        }
        if (!memory.contains(toAddr(base))) {
            if (memory.getBlocks().length == 1 && memory.getBlocks()[0].getSize() == size) {
                memory.moveBlock(memory.getBlocks()[0], toAddr(base), monitor);
            }
            else {
                printerr(""The program is not loaded at "" + Long.toHexString(base) + "". Import the .bin file again with that base address."");
                return;
            }
        }

        // sections first, so that code is only created where code is
        for (MemoryBlock block : memory.getBlocks()) {
            if (block.getStart().getOffset() >= base && block.getStart().getOffset() < base + size) {
                block.setPermissions(true, false, false);
            }
        }
        for (String[] l : lines) {
            if (!l[0].equals(""section"")) continue;
            long start = Long.parseLong(l[2], 16), length = Long.parseLong(l[3], 16);
            MemoryBlock block = piece(start, length);
            if (block == null) continue;
            try { block.setName(l[1]); } catch (Exception e) { block.setName(l[1] + ""_"" + Long.toHexString(start)); }
            block.setPermissions(l[4].charAt(0) == 'r', l[4].charAt(1) == 'w', l[4].charAt(2) == 'x');
            sections++;
        }
        // what lies between the sections (the PE headers at the start, alignment gaps)
        java.util.Set<Long> named = new java.util.HashSet<Long>();
        for (String[] l : lines) {
            if (l[0].equals(""section"")) named.add(Long.parseLong(l[2], 16));
        }
        for (MemoryBlock block : memory.getBlocks()) {
            long start = block.getStart().getOffset();
            if (start < base || start >= base + size || named.contains(start)) continue;
            try { block.setName(start == base ? ""headers"" : ""gap_"" + Long.toHexString(start)); } catch (Exception e) { }
        }

        for (String[] l : lines) {
            monitor.checkCancelled();
            if (l[0].equals(""import"")) {
                Address at = toAddr(Long.parseLong(l[2], 16));
                int library = Integer.parseInt(l[3]), ordinal = Integer.parseInt(l[4], 16);
                if (l[1].equals(""f"")) {
                    // on disk the stub's first two words are the import record; make them instructions (in this project only)
                    memory.setInt(at, 0x38600000 | (library & 0xFFFF));
                    memory.setInt(at.add(4), 0x38800000 | (ordinal & 0xFFFF));
                    function(at, l[5]);
                    setPlateComment(at, ""Import stub: "" + l[5] + "" (library "" + library + "", ordinal 0x"" + Integer.toHexString(ordinal) + "")"");
                }
                else {
                    createLabel(at, l[5], true, SourceType.IMPORTED);
                    try { createData(at, new PointerDataType()); } catch (Exception e) { }
                }
                imports++;
            }
            else if (l[0].equals(""export"")) {
                Address at = toAddr(Long.parseLong(l[1], 16));
                MemoryBlock block = memory.getBlock(at);
                if (block != null && block.isExecute()) function(at, l[2]);
                else createLabel(at, l[2], true, SourceType.IMPORTED);
                exports++;
            }
            else if (l[0].equals(""func"")) {
                function(toAddr(Long.parseLong(l[1], 16)), null);
            }
            else if (l[0].equals(""entry"")) {
                Address at = toAddr(Long.parseLong(l[1], 16));
                addEntryPoint(at);
                function(at, null);
                Function f = getFunctionAt(at);
                if (f != null && f.getSymbol().getSource() == SourceType.DEFAULT) f.setName(""entry"", SourceType.IMPORTED);
            }
        }
        println(""X360GameHack map applied: sections "" + sections + "", functions "" + functions + "", imports "" + imports + "", exports "" + exports);
    }

    // the block that covers exactly [start, start + length), splitting where needed
    private MemoryBlock piece(long start, long length) throws Exception {
        Address from = toAddr(start);
        MemoryBlock block = memory.getBlock(from);
        if (block == null) return null;
        if (!block.getStart().equals(from)) {
            memory.split(block, from);
            block = memory.getBlock(from);
        }
        long end = start + length;
        if (block.getEnd().getOffset() >= end) {
            memory.split(block, toAddr(end));
            block = memory.getBlock(from);
        }
        return block;
    }

    private void function(Address at, String name) throws Exception {
        MemoryBlock block = memory.getBlock(at);
        if (block == null) return;
        if (getInstructionAt(at) == null) disassemble(at);
        Function f = getFunctionAt(at);
        if (f == null && getInstructionAt(at) != null) {
            f = createFunction(at, name);
            if (f != null) functions++;
        }
        if (name != null) {
            if (f != null) f.setName(name, SourceType.IMPORTED);
            else createLabel(at, name, true, SourceType.IMPORTED);
        }
    }
}
";
    }
}
