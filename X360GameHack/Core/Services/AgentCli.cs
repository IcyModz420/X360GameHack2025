using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// The program without its window, for scripts and AI agents. Two ways in, one set of commands:
    ///     X360GameHack.exe cli &lt;command&gt; [arguments]     one command, one JSON object on standard output, exit code 0 / 1
    ///     X360GameHack.exe mcp                           a Model Context Protocol server on standard input / output
    ///
    /// Safety rules that hold for every command:
    ///   - Arguments are data. No command builds a command line, starts another program or goes through a shell,
    ///     so there is nothing for an argument to inject into. Numbers are parsed as numbers, paths are used only
    ///     as paths.
    ///   - The original of an edited file is kept as "file.bak" the first time; output files are never overwritten.
    ///   - Never elevated (the same rule as the window), no console connection, no internet.
    /// </summary>
    public static class AgentCli
    {
        private enum Kind { Path, Hex, Number, Text, List }

        private sealed class Param
        {
            public string Name, Does;
            public Kind Kind;
            public bool Optional;
        }

        private sealed class Command
        {
            public string Name, Does;
            public bool Writes;
            public Param[] Params = new Param[0];
            public Func<Args, object> Run;
            public string Usage => Name + string.Concat(Params.Select(p => " " + (p.Optional ? "[" : "<") + p.Name + (p.Kind == Kind.List ? "..." : "") + (p.Optional ? "]" : ">")));
        }

        /// <summary>The arguments of one call, by parameter name.</summary>
        private sealed class Args
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            public bool Has(string name) => Values.ContainsKey(name);
            public string Text(string name) => (string)Values[name];
            public string File(string name)
            {
                string full = System.IO.Path.GetFullPath(Text(name));
                if (!System.IO.File.Exists(full)) throw new FileNotFoundException("File not found: " + full);
                return full;
            }
            public string Path(string name) => System.IO.Path.GetFullPath(Text(name));
            public uint Hex(string name) => (uint)Values[name];
            public int Number(string name, int fallback) => Has(name) ? (int)Values[name] : fallback;
            public List<string> List(string name) => Has(name) ? (List<string>)Values[name] : new List<string>();
        }

        private static Param P(string name, Kind kind, string does, bool optional = false) => new Param { Name = name, Kind = kind, Does = does, Optional = optional };

        private static readonly List<Command> Commands = new List<Command>
        {
            new Command { Name = "help", Does = "Lists every command with its parameters.", Run = a => Help() },

            // ---- any file
            new Command { Name = "info", Does = "What a file is (package, save, profile, XEX, GPD, disc image...) and its main details. Start here with an unknown file.",
                          Params = new[] { P("file", Kind.Path, "The file to look at.") }, Run = a => Info(a.File("file")) },

            // ---- STFS packages: saves, profiles, title updates, DLC, arcade games
            new Command { Name = "list", Does = "The files inside an STFS package (CON / LIVE / PIRS: saves, profiles, title updates, DLC, arcade games).",
                          Params = new[] { P("package", Kind.Path, "The package file.") }, Run = a => List(a.File("package")) },
            new Command { Name = "verify", Does = "Checks every hash of a package and reports its signature state.",
                          Params = new[] { P("package", Kind.Path, "The package file.") }, Run = a => Verify(a.File("package")) },
            new Command { Name = "extract", Does = "Extracts every file of a package into a folder (created if missing). Use this to get default.xexp and the other files out of a title update.", Writes = true,
                          Params = new[] { P("package", Kind.Path, "The package file."), P("folder", Kind.Path, "Where the files go.") }, Run = a => Extract(a.File("package"), a.Path("folder")) },
            new Command { Name = "tu-extract", Does = "Takes a title update package apart into a folder - default.xexp and the updated game files, keeping sub-folders. Extract into the game's folder (the one with default.xex) to merge an update by hand; a file already there that differs is kept as .bak.", Writes = true,
                          Params = new[] { P("package", Kind.Path, "The title update package."), P("folder", Kind.Path, "Usually the game's folder.") }, Run = a => TuExtract(a.File("package"), a.Path("folder")) },
            new Command { Name = "tu-check", Does = "Tells whether a title update was made for a game, the way the console decides it: same Title ID, same Media ID, and the update's patch starts from the game's exact version. The game is its default.xex, its disc image (.iso) or its package. Also says which folder on the console the update belongs in. Nothing is changed.",
                          Params = new[] { P("update", Kind.Path, "The title update package."), P("game", Kind.Path, "The game's default.xex, an Xbox 360 .iso, or an arcade / Games on Demand package.") },
                          Run = a => TuCheck(a.File("update"), a.File("game")) },
            new Command { Name = "rehash", Does = "Rebuilds every hash of a package and, for a CON package when a keyvault file is given, signs it for that console. Without a keyvault the hashes are fixed and the signature is left as it is (RGH / JTAG consoles accept that).", Writes = true,
                          Params = new[] { P("package", Kind.Path, "The package file (edited in place; a .bak is kept)."), P("keyvault", Kind.Path, "The owner's own keyvault file (KV.bin), decrypted. Read into memory only.", true) },
                          Run = a => Rehash(a.File("package"), a.Has("keyvault") ? a.File("keyvault") : null) },
            new Command { Name = "create-save", Does = "Builds a new CON package (a save game by default) from files. Copy the title ID, names and profile / device IDs from an existing save of the same game with 'like'.", Writes = true,
                          Params = new[] { P("output", Kind.Path, "The package to create (must not exist)."), P("like", Kind.Path, "An existing package whose details (title ID, names, IDs, picture) are copied."), P("files", Kind.List, "The files to put inside (each keeps its file name).") },
                          Run = a => CreateSave(a.Path("output"), a.File("like"), a.List("files")) },
            new Command { Name = "gamesave-list", Does = "The named values inside a game save, for games that have a free (open) save mod in the mods folder. Also lists the one-step actions. Paid save mods are used in the program window, not here.",
                          Params = new[] { P("save", Kind.Path, "The save package."), P("filter", Kind.Text, "Only values whose name contains this text (for example kills, angola, intel).", true), P("count", Kind.Number, "How many at most (default 200, at most 5000).", true) },
                          Run = a => GameSaveList(a.File("save"), a.Has("filter") ? a.Text("filter") : "", a.Number("count", 200)) },
            new Command { Name = "gamesave-set", Does = "Changes named values in a game save: each change is name=value, with the name exactly as gamesave-list shows it. All changes are applied or none. The save is rehashed and verified; run 'rehash' with a keyvault afterwards for a stock console.", Writes = true,
                          Params = new[] { P("save", Kind.Path, "The save package (edited in place; a .bak is kept)."), P("changes", Kind.List, "One or more name=value.") },
                          Run = a => GameSaveSet(a.File("save"), a.List("changes"), null) },
            new Command { Name = "gamesave-action", Does = "Runs one of the one-step actions gamesave-list names (for example \"Max money\") on a game save. The save is rehashed and verified.", Writes = true,
                          Params = new[] { P("save", Kind.Path, "The save package (edited in place; a .bak is kept)."), P("action", Kind.Text, "The action's name, as listed.") },
                          Run = a => GameSaveSet(a.File("save"), new List<string>(), a.Text("action")) },

            // ---- mod files (.x360mod): what a file is, and building one. Nothing here can open a paid mod.
            new Command { Name = "mod-info", Does = "What a .x360mod file is: its id, name, game, kind and whether it is free (open), paid (sealed) or server-only. The contents of a paid mod are never shown - they cannot be read without the buyer's licence.",
                          Params = new[] { P("mod", Kind.Path, "The .x360mod file.") }, Run = a => ModInfoCmd(a.File("mod")) },
            new Command { Name = "mod-validate", Does = "Checks a mod payload (the JSON that says what a mod does) the same way the program will read it.",
                          Params = new[] { P("payload", Kind.Path, "The payload JSON file.") },
                          Run = a => { var c = Mods.ModBuilderService.Check(File.ReadAllText(a.File("payload"))); var r = Ok(); r["valid"] = c.Ok; if (!c.Ok) { r["problem"] = c.Problems[0].ToString(); r["problems"] = c.Problems.Select(p => p.ToString()).ToList(); } else r["summary"] = c.Summary; if (c.Notes.Count > 0) r["notes"] = c.Notes.Select(p => p.ToString()).ToList(); return r; } },
            new Command { Name = "mod-build", Does = "For mod makers: builds a .x360mod from a payload JSON. protection is open (free, readable), protected (sealed; a key card for your licence server is written beside it and no copy of the key is kept) or server-only (no data in the file; your server delivers it). Every field is required - buyers see them all - and the Title ID must be the payload's.", Writes = true,
                          Params = new[] { P("output", Kind.Path, "The .x360mod to create (must not exist)."), P("id", Kind.Text, "Short unique id your licence server knows the mod by."), P("name", Kind.Text, "Name shown in the mod list."),
                                           P("protection", Kind.Text, "open, protected or server-only."),
                                           P("game", Kind.Text, "Game name, shown to buyers."), P("titleId", Kind.Text, "The game's Title ID, 8 hex digits; must be the payload's titleId."),
                                           P("description", Kind.Text, "One line describing the mod, shown to buyers."), P("requires", Kind.Text, "What the mod needs, shown to buyers (e.g. Latest title update)."),
                                           P("payload", Kind.Path, "The payload JSON file (not needed for server-only).", true) },
                          Run = a => ModBuildCmd(a.Path("output"), a.Text("id"), a.Text("name"), a.Text("protection"), a.Has("payload") ? a.File("payload") : null,
                                                 a.Text("game"), a.Text("titleId"), a.Text("description"), a.Text("requires")) },
            new Command { Name = "mod-sign", Does = "For the X360GameHack team only: signs an existing .x360mod with the team's key so the program loads it (it loads no unsigned mod). Needs the team's signing key on this PC; the mod's contents and its key are not touched. Keeps <file>.bak.", Writes = true,
                          Params = new[] { P("file", Kind.Path, "The .x360mod to sign.") },
                          Run = a => ModSignCmd(a.File("file")) },
            new Command { Name = "owner-key-restore", Does = "For the X360GameHack team only: puts the team's signing key back on this PC (for this Windows user) from a backup made with Mods > Mod Maker > Back up signing key. The password is read from standard input - typed hidden, or piped - never given as an argument. A key already on this PC is never replaced.", Writes = true,
                          Params = new[] { P("file", Kind.Path, "The .x360key backup file.") },
                          Run = a => OwnerKeyRestoreCmd(a.File("file")) },

            // ---- the JRPC2 plugin: only what plain XBDM cannot do
            new Command { Name = "console-notify", Does = "Shows a pop-up notification on the console. Plain XBDM cannot do this, so it goes through the JRPC2 plugin, or the XRPC plugin when JRPC2 is not loaded. 'console-notify-icons' lists the pictures.", Writes = true,
                          Params = new[] { P("ip", Kind.Text, "The console's IP address."), P("text", Kind.Text, "Plain text, up to 200 characters."), P("icon", Kind.Number, "Picture number (default 0, the Xbox logo).", true) },
                          Run = a => OnConsole(a.Text("ip"), () => { string t = a.Text("text"); int icon = a.Number("icon", 0); ConsoleService.RunAsync(c => ConsoleExtras.Notify(c, t, icon)).GetAwaiter().GetResult(); var r = Ok(); r["text"] = t; r["icon"] = icon; r["route"] = ConsoleExtras.Name(ConsoleExtras.LastRoute); return r; }) },
            new Command { Name = "console-notify-icons", Does = "The notification pictures console-notify can show, with their numbers.",
                          Run = a => { var r = Ok(); r["icons"] = Jrpc.Icons.Select(i => new Dictionary<string, object> { { "icon", i.Id }, { "name", i.Name } }).ToList(); return r; } },
            new Command { Name = "console-temperatures", Does = "Reads the console's temperature sensors in degrees Celsius. Needs the JRPC2 plugin; plain XBDM cannot do this.",
                          Params = new[] { P("ip", Kind.Text, "The console's IP address.") },
                          Run = a => OnConsole(a.Text("ip"), () => { var r = Ok(); foreach (JrpcSensor s in Enum.GetValues(typeof(JrpcSensor))) { var sensor = s; r[s.ToString().ToLowerInvariant()] = ConsoleService.RunAsync(c => ConsoleExtras.Temperature(c, sensor)).GetAwaiter().GetResult(); } r["route"] = ConsoleExtras.Name(ConsoleExtras.LastRoute); return r; }) },

            // ---- profiles
            new Command { Name = "profile", Does = "Gamertag, gamerscore and the games of a gamer profile.",
                          Params = new[] { P("profile", Kind.Path, "The profile file.") }, Run = a => Profile(a.File("profile")) },
            new Command { Name = "achievements", Does = "The achievements of one game in a profile.",
                          Params = new[] { P("profile", Kind.Path, "The profile file."), P("titleId", Kind.Hex, "The game's title ID, hexadecimal.") }, Run = a => Achievements(a.File("profile"), a.Hex("titleId")) },
            new Command { Name = "unlock", Does = "Unlocks every achievement of one game, or of all games. They are stored as earned offline and, unless dates is 'same', each gets its own natural-looking date spread back over time instead of one identical instant. Hashes stay valid; run 'rehash' with a keyvault afterwards for a retail console.", Writes = true,
                          Params = new[] { P("profile", Kind.Path, "The profile file (edited in place; a .bak is kept)."), P("titleId", Kind.Text, "Only this game (hexadecimal title ID), or 'all'.", true), P("dates", Kind.Text, "'natural' (default) or 'same'.", true) },
                          Run = a => { string tid = a.Has("titleId") ? a.Text("titleId").Trim() : "all"; bool all = tid.Length == 0 || tid.Equals("all", StringComparison.OrdinalIgnoreCase);
                                       uint id = 0; if (!all && !uint.TryParse(tid.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? tid.Substring(2) : tid, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id)) throw new ArgumentException("titleId must be a hexadecimal title ID or 'all'.");
                                       string dates = a.Has("dates") ? a.Text("dates").Trim().ToLowerInvariant() : "natural"; if (dates != "natural" && dates != "same") throw new ArgumentException("dates must be natural or same.");
                                       return Unlock(a.File("profile"), all ? (uint?)null : id, dates == "natural"); } },

            // ---- XEX
            new Command { Name = "xex", Does = "Decodes an XEX: title ID, image base, entry point, encryption, compression, sections, counts of functions / imports / exports.",
                          Params = new[] { P("xex", Kind.Path, "The XEX file.") }, Run = a => Xex(a.File("xex")) },
            new Command { Name = "functions", Does = "Function start addresses from the image's own function table.",
                          Params = new[] { P("xex", Kind.Path, "The XEX file."), P("from", Kind.Number, "Index of the first one (default 0).", true), P("count", Kind.Number, "How many (default 200, at most 20000).", true) },
                          Run = a => Functions(a.File("xex"), a.Number("from", 0), a.Number("count", 200)) },
            new Command { Name = "imports", Does = "Imported kernel / XAM / XBDM functions with their names and stub addresses.",
                          Params = new[] { P("xex", Kind.Path, "The XEX file.") }, Run = a => Imports(a.File("xex")) },
            new Command { Name = "disasm", Does = "PowerPC disassembly from a memory address.",
                          Params = new[] { P("xex", Kind.Path, "The XEX file."), P("address", Kind.Hex, "Memory address, hexadecimal."), P("count", Kind.Number, "Instructions (default 64, at most 4096).", true) },
                          Run = a => Disasm(a.File("xex"), a.Hex("address"), a.Number("count", 64)) },
            new Command { Name = "decompile", Does = "The function that contains the address as C-style code.",
                          Params = new[] { P("xex", Kind.Path, "The XEX file."), P("address", Kind.Hex, "Any address inside the function, hexadecimal.") }, Run = a => Decompile(a.File("xex"), a.Hex("address")) },
            new Command { Name = "save-image", Does = "Writes the decrypted, decompressed base file of an XEX.", Writes = true,
                          Params = new[] { P("xex", Kind.Path, "The XEX file."), P("output", Kind.Path, "The file to create (must not exist).") }, Run = a => SaveImage(a.File("xex"), a.Path("output")) },
            new Command { Name = "ghidra", Does = "Writes a Ghidra-ready export (decoded image, map, script) into a new sub-folder.", Writes = true,
                          Params = new[] { P("xex", Kind.Path, "The XEX file."), P("folder", Kind.Path, "The folder the new sub-folder is created in.") }, Run = a => Ghidra(a.File("xex"), a.Path("folder")) },

            // ---- disc images
            new Command { Name = "iso-list", Does = "Every file in an Xbox / Xbox 360 disc image with its path and size.",
                          Params = new[] { P("iso", Kind.Path, "The disc image.") }, Run = a => IsoList(a.File("iso")) },
            new Command { Name = "iso-extract", Does = "Extracts a whole disc image into a folder.", Writes = true,
                          Params = new[] { P("iso", Kind.Path, "The disc image."), P("folder", Kind.Path, "Where the files go (created if missing).") }, Run = a => IsoExtract(a.File("iso"), a.Path("folder")) },
            new Command { Name = "iso-create", Does = "Builds an XISO disc image from a folder.", Writes = true,
                          Params = new[] { P("folder", Kind.Path, "The game folder."), P("output", Kind.Path, "The image to create (must not exist).") }, Run = a => IsoCreate(a.Path("folder"), a.Path("output")) },
            new Command { Name = "iso-swap", Does = "Writes a new disc image in which some files hold other content - for testing a mod. The original image is only read.", Writes = true,
                          Params = new[] { P("iso", Kind.Path, "The original disc image."), P("output", Kind.Path, "The image to create (must not exist)."),
                                           P("swaps", Kind.List, "One or more  path-in-image=file-on-this-PC  pairs, for example  media\\movie.bik=C:\\mods\\movie.bik") },
                          Run = a => IsoSwap(a.File("iso"), a.Path("output"), a.List("swaps")) },

            // ---- Games on Demand (ISO2God / god2iso)
            new Command { Name = "god-info", Does = "What a Games on Demand package made from a disc image would say (name, title / media ID, disc, platform, picture, how much of the image goes in for each padding mode), or the details of an existing package. Nothing is written.",
                          Params = new[] { P("file", Kind.Path, "A disc image (.iso), or a Games on Demand package (the file next to its .data folder).") }, Run = a => GodInfo(a.File("file")) },
            new Command { Name = "iso2god", Does = "Converts a disc image - or a game in its XEX version (a folder with default.xex, or default.xbe for an original Xbox game) - into a Games on Demand package (ISO2God): {folder}\\{title ID}\\{00007000 or 00005000}\\{name} plus its .data folder. Every block is hashed and the finished package is checked hash by hash. With target= the package is made for RGH or a devkit: the game's XEX is unlocked / converted with xextool on a copy and the package unlocked with X360PkgTool.", Writes = true,
                          Params = new[] { P("source", Kind.Path, "The disc image, or the game folder."), P("folder", Kind.Path, "Where the title-ID folder is made, usually ...\\Content\\0000000000000000 (created if missing)."),
                                           P("padding", Kind.Text, "none, partial (cut after the last file) or full (rebuilt with every file packed; the default, as in ISO2God). A game folder is always packed tight.", true),
                                           P("options", Kind.List, "Any of: name=<dashboard name>  naming=iso2god|microsoft  rebuilt=<path for the rebuilt .iso (full only)>  disc-suffix=no  thumbnail=<picture file>|none  target=rgh|devkit  xex-unlock=yes|no  pkg-unlock=yes|no (both yes when a target is given)", true) },
                          Run = a => Iso2God(a.Path("source"), a.Path("folder"), a.Has("padding") ? a.Text("padding") : "full", a.List("options")) },
            new Command { Name = "god2iso", Does = "Converts a Games on Demand package back into a disc image (god2iso). Every block is checked against the package's hashes; the image is read back as an Xbox disc before it gets its name. The output must not exist.", Writes = true,
                          Params = new[] { P("package", Kind.Path, "The package (the file next to its .data folder)."), P("output", Kind.Path, "The disc image to create."),
                                           P("layout", Kind.Text, "compact (like god2iso: Microsoft's empty stretch dropped, the default) or exact (every sector in its place).", true),
                                           P("fix-createisogood", Kind.Text, "yes (default) or no: replace a CreateIsoGood-broken lead-in.", true) },
                          Run = a => God2Iso(a.File("package"), a.Path("output"), a.Has("layout") ? a.Text("layout") : "compact", !a.Has("fix-createisogood") || !a.Text("fix-createisogood").Trim().Equals("no", StringComparison.OrdinalIgnoreCase)) },
            new Command { Name = "nand-info", Does = "What an Xbox 360 flash (NAND) dump holds - every board, small and big block, Corona 4 GB: dump kind, board, every boot stage and both patch slots (the dashboard each boots), SMC version, page EDC and bad-block check, the flash file system's files. With the console's CPU key (or a cpukey.txt beside the dump) it says whether the key unlocks the keyvault; the keyvault's own values (serial, console ID, DVD key ...) only with kv=show. Nothing is written.",
                          Params = new[] { P("dump", Kind.Path, "The flash dump (nanddump.bin, updflash.bin ...)."), P("options", Kind.List, "cpukey=<32 hex digits or a file>  kv=show", true) },
                          Run = a => NandInfo(a.File("dump"), a.List("options")) },
            new Command { Name = "nand-extract", Does = "Writes every file of a flash dump's file system (the newest root) into a new folder, each exactly its size. The dump is only read.", Writes = true,
                          Params = new[] { P("dump", Kind.Path, "The flash dump."), P("folder", Kind.Path, "A new folder (must not exist or be empty).") },
                          Run = a => NandExtract(a.File("dump"), a.Path("folder")) },
            new Command { Name = "nand-stages", Does = "Writes every boot stage and patch-slot stage of a flash dump into a new folder: <stage>.bin as stored and <stage>_dec.bin decrypted as J-Runner decrypts it (published 1BL key; the CPU key from cpukey= or a cpukey.txt beside the dump for stages paired to the console). A stage that does not decrypt right is written as stored only. The dump is only read.", Writes = true,
                          Params = new[] { P("dump", Kind.Path, "The flash dump."), P("folder", Kind.Path, "A new folder (must not exist or be empty)."), P("options", Kind.List, "cpukey=<32 hex digits or a file>", true) },
                          Run = a => NandStages(a.File("dump"), a.Path("folder"), a.List("options")) },
            new Command { Name = "nand-replace-file", Does = "Writes a new flash dump with one flash-file-system file replaced - for swapping a boot animation (bootanim.xex) or another system file on an RGH / JTAG console. The file keeps its own blocks, so a same-size-or-smaller replacement is written in place (a larger one is refused); the result is read back and checked before it is kept. The source dump is only read. Flash it the way you flash a NAND; keep the original as a backup. Untested on a real console yet (BETA).", Writes = true,
                          Params = new[] { P("dump", Kind.Path, "The flash dump to start from."), P("name", Kind.Text, "The file in the flash file system to replace, e.g. bootanim.xex."), P("source", Kind.Path, "The replacement file (for a boot animation, a bootanim.xex)."), P("output", Kind.Path, "The new flash dump to write (must not exist).") },
                          Run = a => NandReplaceFile(a.File("dump"), a.Text("name"), a.File("source"), a.Path("output")) },
            new Command { Name = "nand-replace-kv", Does = "Writes a new flash dump with its keyvault swapped for another one (as stored / encrypted, the form \"Save keyvault (as stored)\" or J-Runner's KV_en.bin writes). For putting a different console's keyvault into a NAND on an RGH / JTAG console. The result is read back and checked before it is kept, and with cpukey=<that console's CPU key> it is also checked that the key unlocks the new keyvault. The source dump is only read. The console the keyvault belongs to boots it with its own CPU key. Untested on a real console yet (BETA).", Writes = true,
                          Params = new[] { P("dump", Kind.Path, "The flash dump to start from."), P("keyvault", Kind.Path, "The keyvault to put in (as stored, e.g. KV_en.bin)."), P("output", Kind.Path, "The new flash dump to write (must not exist)."), P("options", Kind.List, "cpukey=<32 hex digits or a file> to check the new keyvault unlocks", true) },
                          Run = a => NandReplaceKv(a.File("dump"), a.File("keyvault"), a.Path("output"), a.List("options")) },
            new Command { Name = "convert", Does = "Any form of a game to any other: a disc image, a Games on Demand package or its XEX version (a folder with default.xex, or default.xbe for an original Xbox game) into a package (god), a disc image (iso) or a folder (folder). The source is only read. target=rgh|devkit unlocks / converts the game's XEX with xextool on a copy, whatever the output; a package is also unlocked with X360PkgTool. stealth=yes runs abgx360 AutoFix on a new Xbox 360 image (images made here are for RGH / JTAG - see the Convert tab's note).", Writes = true,
                          Params = new[] { P("source", Kind.Path, "The disc image, package (the file next to its .data folder) or game folder."),
                                           P("to", Kind.Text, "god, iso or folder."),
                                           P("output", Kind.Path, "god: the Content\\0000000000000000 folder the title folder is made in; iso: the image to create; folder: the folder to create. Never an existing file or a folder with something in it."),
                                           P("options", Kind.List, "Any of: target=rgh|devkit  xex-unlock=yes|no  pkg-unlock=yes|no  padding=none|partial|full  naming=iso2god|microsoft  name=<dashboard name>  layout=compact|exact  fix-createisogood=yes|no  skip-update=yes|no  stealth=yes|no", true) },
                          Run = a => ConvertGame(a.Path("source"), a.Text("to"), a.Path("output"), a.List("options")) },

            // ---- Xenia
            new Command { Name = "patch-list", Does = "The patches in a Xenia .patch.toml file: number, name, author, on / off, how many writes.",
                          Params = new[] { P("patchFile", Kind.Path, "The .patch.toml file.") }, Run = a => PatchList(a.File("patchFile")) },
            new Command { Name = "patch-install", Does = "Copies a Xenia patch file into Xenia's patches folder with the listed patches switched on and all others off. Xenia applies it the next time the game starts.", Writes = true,
                          Params = new[] { P("patchFile", Kind.Path, "The .patch.toml file (not changed)."), P("xenia", Kind.Path, "xenia_canary.exe (the one next to X360GameHack.exe when left out).", true),
                                           P("on", Kind.List, "Numbers of the patches to switch on, as 'patch-list' shows them (none = all off).", true) },
                          Run = a => PatchInstall(a.File("patchFile"), a.Has("xenia") ? a.File("xenia") : null, a.List("on")) },
            new Command { Name = "xenia-read", Does = "Reads memory of the game running in Xenia on this PC, at console addresses. Returns hexadecimal bytes.",
                          Params = new[] { P("address", Kind.Hex, "Console address, hexadecimal."), P("length", Kind.Number, "Bytes (at most 65536).") }, Run = a => XeniaRead(a.Hex("address"), a.Number("length", 16)) },
            new Command { Name = "xenia-write", Does = "Writes bytes into the game running in Xenia. Changes values and data at once; code that has already run is not affected (use patch-install for code).", Writes = true,
                          Params = new[] { P("address", Kind.Hex, "Console address, hexadecimal."), P("bytes", Kind.Text, "The bytes as hexadecimal digits, for example 3B600001.") }, Run = a => XeniaWrite(a.Hex("address"), a.Text("bytes")) },

            // ---- a PHYSICAL Xbox hard drive connected to this PC (installed copy only, through the Disk Reader helper)
            new Command { Name = "disk-run", Does = "Runs a list of steps on a physical Xbox hard drive connected to this PC, through the X360GameHack Disk Reader (installed copy only; Windows asks for permission once). " +
                          "Steps, a JSON array of objects: {op:partitions} {op:list,partition,folder} {op:tree,partition,to} {op:get,partition,path,to} {op:backup,to,progress} {op:check,partition} {op:sessions} " +
                          "{op:recover-list,partition,lost,to} {op:recover,partition,to,which} (deleted files to a folder on this PC - read only on the drive, see drive-recover); " +
                          "with mode rw also {op:mkdir,partition,path} {op:put,partition,folder,items:[...]} {op:delete,partition,path} {op:undo,session} " +
                          "{op:create-hddx} (the original-Xbox compatibility partition, when a drive has none) {op:install-compat,from} (your Compatibility folder with the emulator files into HddX). " +
                          "partition is the number from the partitions step, or the partition's exact name (\"Content (Data)\"), so a step can never land on another partition. " +
                          "The helper never opens the Windows disk or a disk without an Xbox file system; in rw mode it writes only inside the Xbox partitions and journals every changed sector first, so undo can put it back - " +
                          "undo is for changes nothing else has touched since (once the drive has been in a console, delete what was added instead). Stops at the first step that fails.", Writes = true,
                          Params = new[] { P("disk", Kind.Number, "The disk number Windows gives the Xbox drive."), P("mode", Kind.Text, "ro (read only) or rw (changes allowed, journaled)."), P("steps", Kind.Path, "A JSON file with the steps.") },
                          Run = a => DiskRun(a.Number("disk", -1), a.Text("mode"), a.File("steps")) },

            new Command { Name = "disk-eject", Does = "Safely removes a physical Xbox hard drive so it can be unplugged - also from an internal SATA port: its cache is flushed, it spins down (heads parked) and Windows lets go of it. Nothing is written to the drive; only a non-Windows disk with an Xbox file system is removed. Windows asks for permission.", Writes = false,
                          Params = new[] { P("disk", Kind.Number, "The disk number Windows gives the Xbox drive.") },
                          Run = a => DiskEject(a.Number("disk", -1)) },
            new Command { Name = "disk-remount", Does = "Brings back drives that were safely removed (disk-eject, or Windows' own Safely Remove) but are still connected - Windows keeps such a drive parked and a hardware scan alone does not start it again - and has Windows look for drives connected since. Reads and writes no disk. Windows asks for permission.", Writes = false,
                          Run = a => DiskRemount() },

            // ---- Xbox hard-drive images (FATX / XTAF)
            new Command { Name = "drive-list", Does = "An Xbox 360 or original Xbox hard-drive image: its partitions, or (with a partition number) the files and folders in one of its folders.",
                          Params = new[] { P("image", Kind.Path, "The drive or partition image file."), P("partition", Kind.Number, "Partition number as listed (leave out to list the partitions).", true), P("folder", Kind.Text, "Folder on the drive, for example Content\\0000000000000000 (root when left out).", true) },
                          Run = a => DriveList(a.File("image"), a.Number("partition", 0), a.Has("folder") ? a.Text("folder") : "") },
            new Command { Name = "drive-check", Does = "Checks one partition of a drive image for consistency (every file's cluster chain against the allocation table). Read-only.",
                          Params = new[] { P("image", Kind.Path, "The drive image."), P("partition", Kind.Number, "Partition number from drive-list.") }, Run = a => DriveCheck(a.File("image"), a.Number("partition", 0)) },
            new Command { Name = "drive-recover-list", Does = "Lists what was deleted on one partition of a drive image and can still be got back: deleted files and folders (a deleted folder with what it held) and lost data (clusters marked used that no file owns). Each says recoverable, partly overwritten or overwritten, and what its first bytes are. Read-only.",
                          Params = new[] { P("image", Kind.Path, "The drive image."), P("partition", Kind.Number, "Partition number from drive-list."), P("lost", Kind.Text, "yes (default) to list lost data too, no to leave it out.", true) },
                          Run = a => OnVolume(a.File("image"), a.Number("partition", 0), false, v => RecoverList(v, a.Has("lost") ? a.Text("lost") : null)) },
            new Command { Name = "drive-recover", Does = "Copies deleted files and folders from one partition of a drive image to a folder on this PC, into the folders they were in. The image is only read. which: recoverable (default - everything fully there, deleted folders with what they held), all (also partly overwritten files and lost data that is not empty), or the path of one deleted file or folder.",
                          Params = new[] { P("image", Kind.Path, "The drive image."), P("partition", Kind.Number, "Partition number from drive-list."), P("output", Kind.Path, "Folder on this PC to write into (created; nothing already there is replaced)."), P("which", Kind.Text, "recoverable, all, or the path of a deleted file or folder.", true) },
                          Run = a => OnVolume(a.File("image"), a.Number("partition", 0), false, v => RecoverInto(v, a.Path("output"), a.Has("which") ? a.Text("which") : null)) },
            new Command { Name = "drive-extract", Does = "Copies a file or a whole folder out of a drive image to this PC.",
                          Params = new[] { P("image", Kind.Path, "The drive image."), P("partition", Kind.Number, "Partition number from drive-list."), P("path", Kind.Text, "File or folder on the drive."), P("folder", Kind.Path, "Folder on this PC to copy into (created if missing).") },
                          Run = a => DriveExtract(a.File("image"), a.Number("partition", 0), a.Text("path"), a.Path("folder")) },
            new Command { Name = "drive-add", Does = "Copies files and folders from this PC into a folder of a drive image (the folder is created if missing; files of the same name are replaced). Changes the image file: keep a copy.", Writes = true,
                          Params = new[] { P("image", Kind.Path, "The drive image (changed in place)."), P("partition", Kind.Number, "Partition number from drive-list."), P("folder", Kind.Text, "Destination folder on the drive; use \\ for the root."), P("items", Kind.List, "Files and folders on this PC.") },
                          Run = a => DriveAdd(a.File("image"), a.Number("partition", 0), a.Text("folder"), a.List("items")) },
            new Command { Name = "drive-install", Does = "Installs Xbox 360 packages (Games on Demand, arcade, DLC, title updates, saves, profiles) onto a drive image, each into its proper Content folder. Changes the image file: keep a copy.", Writes = true,
                          Params = new[] { P("image", Kind.Path, "The drive image (changed in place)."), P("partition", Kind.Number, "Partition number from drive-list (the Content / data partition)."), P("packages", Kind.List, "Package files, or folders that contain packages.") },
                          Run = a => DriveInstall(a.File("image"), a.Number("partition", 0), a.List("packages")) },
            new Command { Name = "drive-delete", Does = "Deletes a file, or a folder with everything in it, from a drive image. Cannot be undone.", Writes = true,
                          Params = new[] { P("image", Kind.Path, "The drive image (changed in place)."), P("partition", Kind.Number, "Partition number from drive-list."), P("path", Kind.Text, "File or folder on the drive.") },
                          Run = a => DriveDelete(a.File("image"), a.Number("partition", 0), a.Text("path")) },

            // ---- a console on the network (XBDM, port 730; xbdm.xex must be loaded on the console)
            new Command { Name = "console-find", Does = "Searches the local network for consoles running XBDM and returns their IP addresses." , Run = a => ConsoleFind() },
            new Command { Name = "console-info", Does = "Name, type, kernel and XBDM version, running title, drives with free space and loaded modules of a console.",
                          Params = new[] { P("ip", Kind.Text, "The console's IP address, for example 192.168.1.50.") }, Run = a => OnConsole(a.Text("ip"), ConsoleInfo) },
            new Command { Name = "console-list", Does = "Files and folders in a folder on the console.",
                          Params = new[] { P("ip", Kind.Text, "The console's IP address."), P("folder", Kind.Text, "Folder on the console, for example Hdd:\\ or Hdd:\\Content.") }, Run = a => OnConsole(a.Text("ip"), () => ConsoleList(a.Text("folder"))) },
            new Command { Name = "console-get", Does = "Downloads one file from the console to this PC.",
                          Params = new[] { P("ip", Kind.Text, "The console's IP address."), P("remote", Kind.Text, "File on the console, for example Hdd:\\launch.ini."), P("output", Kind.Path, "The file to create on this PC (must not exist).") },
                          Run = a => OnConsole(a.Text("ip"), () => ConsoleGet(a.Text("remote"), a.Path("output"))) },
            new Command { Name = "console-put", Does = "Uploads one file from this PC to the console (folders in the destination are created; an existing file is replaced).", Writes = true,
                          Params = new[] { P("ip", Kind.Text, "The console's IP address."), P("file", Kind.Path, "The file on this PC."), P("remote", Kind.Text, "Full destination path on the console, for example Hdd:\\Games\\Test\\default.xex.") },
                          Run = a => OnConsole(a.Text("ip"), () => ConsolePut(a.File("file"), a.Text("remote"))) },
            new Command { Name = "console-read", Does = "Reads memory of the title running on the console. Returns hexadecimal bytes.",
                          Params = new[] { P("ip", Kind.Text, "The console's IP address."), P("address", Kind.Hex, "Address, hexadecimal."), P("length", Kind.Number, "Bytes (at most 65536).") },
                          Run = a => OnConsole(a.Text("ip"), () => ConsoleRead(a.Hex("address"), a.Number("length", 16))) },
            new Command { Name = "console-write", Does = "Writes bytes into the memory of the title running on the console and returns what was there before. A wrong address can crash the running title.", Writes = true,
                          Params = new[] { P("ip", Kind.Text, "The console's IP address."), P("address", Kind.Hex, "Address, hexadecimal."), P("bytes", Kind.Text, "The bytes as hexadecimal digits, for example 3B600001.") },
                          Run = a => OnConsole(a.Text("ip"), () => ConsoleWrite(a.Hex("address"), a.Text("bytes"))) },
            new Command { Name = "console-dump-storage", Does = "Copies one of the console's storage devices raw into an image file on this PC - the internal 4 GB memory of a Slim / E (INTUSB) or a Corona (INTMMC) - to open with drive-list / drive-recover-list. READ ONLY: the console's kernel opens the device for reading; nothing is written to any storage. Plain XBDM cannot read a device: a development kit's own XDRPC or the JRPC2 plugin (RGH / JTAG) is used. Slow (about 1 MB/s on RGH).",
                          Params = new[] { P("ip", Kind.Text, "The console's IP address."), P("device", Kind.Text, "The drive name as the console lists it (INTUSB, INTMMC) or a kernel device name (\\Device\\BuiltInMuUsb\\Storage)."), P("output", Kind.Path, "The image file to create (must not exist)."), P("length", Kind.Number, "Only the first this many megabytes (for a quick check); the whole device when left out.", true) },
                          Run = a => OnConsole(a.Text("ip"), () => ConsoleDumpStorage(a.Text("device"), a.Path("output"), a.Number("length", 0))) },
            new Command { Name = "console-screenshot", Does = "Saves what the console is showing as a PNG picture.",
                          Params = new[] { P("ip", Kind.Text, "The console's IP address."), P("output", Kind.Path, "The .png file to create (must not exist).") },
                          Run = a => OnConsole(a.Text("ip"), () => ConsoleShot(a.Path("output"))) },
        };

        // ------------------------------------------------------------------ cli

        public static int Run(string[] args, TextWriter output)
        {
            object result; int code = 0;
            try
            {
                string name = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
                var command = Commands.FirstOrDefault(c => c.Name == name);
                if (command == null) throw new ArgumentException("Unknown command \"" + Shorten(name) + "\". Run \"cli help\" for the list.");
                result = command.Run(Positional(command, args.Skip(1).ToArray()));
            }
            catch (Exception ex) { result = Error(ex); code = 1; }
            output.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
            output.Flush();
            return code;
        }

        private static string Shorten(string text) => text.Length > 60 ? text.Substring(0, 60) + "..." : text;

        private static object Error(Exception ex)
        {
            while ((ex is System.Reflection.TargetInvocationException || ex is AggregateException) && ex.InnerException != null) ex = ex.InnerException;
            return new Dictionary<string, object> { { "ok", false }, { "error", ex.Message } };
        }

        private static object Convert(Param p, string text)
        {
            if (text == null) throw new ArgumentException("\"" + p.Name + "\" is missing.");
            switch (p.Kind)
            {
                case Kind.Hex:
                {
                    string t = text.Trim();
                    if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
                    if (t.Length == 0 || t.Length > 8 || !uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) throw new ArgumentException("\"" + p.Name + "\" must be a hexadecimal number of up to 8 digits.");
                    return v;
                }
                case Kind.Number:
                    if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n)) throw new ArgumentException("\"" + p.Name + "\" must be a whole number.");
                    return n;
                case Kind.Path:
                    if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0 || text.Length > 2000) throw new ArgumentException("\"" + p.Name + "\" is not a usable path.");
                    return text;
                default:
                    if (text.Length > 100000) throw new ArgumentException("\"" + p.Name + "\" is too long.");
                    return text;
            }
        }

        private static Args Positional(Command c, string[] given)
        {
            var a = new Args();
            int i = 0;
            foreach (var p in c.Params)
            {
                if (p.Kind == Kind.List)
                {
                    var rest = given.Skip(i).ToList(); i = given.Length;
                    if (rest.Count == 0 && !p.Optional) throw new ArgumentException("Usage: cli " + c.Usage);
                    if (rest.Count > 0) a.Values[p.Name] = rest;
                    continue;
                }
                if (i >= given.Length)
                {
                    if (!p.Optional) throw new ArgumentException("Usage: cli " + c.Usage);
                    continue;
                }
                a.Values[p.Name] = Convert(p, given[i++]);
            }
            if (i < given.Length) throw new ArgumentException("Too many arguments. Usage: cli " + c.Usage);
            return a;
        }

        // ------------------------------------------------------------------ MCP (Model Context Protocol, JSON-RPC over standard input / output)

        private const string McpInstructions =
            "X360GameHack: Xbox 360 files without the window. Start with 'info' on a file you do not know. Packages (saves, profiles, title updates, DLC): list, verify, extract, tu-extract, rehash, create-save. " +
            "Profiles: profile, achievements, unlock. XEX programs: xex, functions, imports, disasm, decompile, save-image, ghidra. Disc images: iso-list, iso-extract, iso-create, iso-swap. " +
            "Xenia: patch-list, patch-install, xenia-read, xenia-write. Addresses and title IDs are hexadecimal. Tools marked read-only never change anything; the others keep a .bak of an edited file and never overwrite an output file. Work on copies of files that matter.";

        public static int Serve(TextReader input, TextWriter output)
        {
            // the console is connected to once for the whole session and every console tool reuses that one connection
            _keepConsole = true;
            try { return ServeLoop(input, output); }
            finally { _keepConsole = false; if (ConsoleService.IsConnected) ConsoleService.Disconnect(); }
        }

        private static int ServeLoop(TextReader input, TextWriter output)
        {
            string line;
            while ((line = input.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                if (line.Length > 4 * 1024 * 1024) { Write(output, RpcError(null, -32600, "Request too large.")); continue; }
                JObject request;
                try { request = JObject.Parse(line); }
                catch (JsonException) { Write(output, RpcError(null, -32700, "Parse error.")); continue; }
                JToken id = request["id"];
                string method = (string)request["method"] ?? "";
                var p = request["params"] as JObject ?? new JObject();
                if (id == null) continue;                                    // a notification (notifications/initialized, cancelled...): nothing to answer
                try
                {
                    switch (method)
                    {
                        case "initialize":
                            Write(output, RpcResult(id, new JObject
                            {
                                ["protocolVersion"] = (string)p["protocolVersion"] ?? "2024-11-05",
                                ["capabilities"] = new JObject { ["tools"] = new JObject() },
                                ["serverInfo"] = new JObject { ["name"] = "x360gamehack", ["version"] = Pastebin.Version.TrimStart('v') },
                                ["instructions"] = McpInstructions
                            }));
                            break;
                        case "ping": Write(output, RpcResult(id, new JObject())); break;
                        case "tools/list": Write(output, RpcResult(id, new JObject { ["tools"] = new JArray(Commands.Where(c => c.Name != "help").Select(ToolSchema)) })); break;
                        case "tools/call": Write(output, RpcResult(id, Call((string)p["name"], p["arguments"] as JObject))); break;
                        default: Write(output, RpcError(id, -32601, "Method not found: " + Shorten(method))); break;
                    }
                }
                catch (Exception ex) { Write(output, RpcError(id, -32603, ex.Message)); }
            }
            return 0;
        }

        private static void Write(TextWriter output, JObject message)
        {
            output.WriteLine(message.ToString(Formatting.None));
            output.Flush();
        }

        private static JObject RpcResult(JToken id, JObject result) => new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        private static JObject RpcError(JToken id, int code, string message) => new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JObject { ["code"] = code, ["message"] = message } };

        private static JObject ToolSchema(Command c)
        {
            var properties = new JObject();
            foreach (var p in c.Params)
            {
                JObject schema;
                if (p.Kind == Kind.List) schema = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } };
                else if (p.Kind == Kind.Number) schema = new JObject { ["type"] = "integer" };
                else schema = new JObject { ["type"] = "string" };
                schema["description"] = p.Does + (p.Kind == Kind.Hex ? " (hexadecimal, 0x optional)" : "");
                properties[p.Name] = schema;
            }
            return new JObject
            {
                ["name"] = c.Name,
                ["description"] = (TestStatus.CommandTested(c.Name) ? "" : "[BETA - not proven end to end] ") + c.Does,
                ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JArray(c.Params.Where(p => !p.Optional).Select(p => p.Name)), ["additionalProperties"] = false },
                ["annotations"] = new JObject { ["readOnlyHint"] = !c.Writes, ["destructiveHint"] = false, ["openWorldHint"] = false }
            };
        }

        private static JObject Call(string name, JObject arguments)
        {
            object result; bool failed = false;
            try
            {
                var command = Commands.FirstOrDefault(c => c.Name == (name ?? "") && c.Name != "help");
                if (command == null) throw new ArgumentException("Unknown tool \"" + Shorten(name ?? "") + "\".");
                var a = new Args();
                arguments = arguments ?? new JObject();
                foreach (var property in arguments.Properties())
                    if (!command.Params.Any(p => p.Name == property.Name)) throw new ArgumentException("\"" + Shorten(property.Name) + "\" is not a parameter of " + command.Name + ".");
                foreach (var p in command.Params)
                {
                    JToken value = arguments[p.Name];
                    if (value == null || value.Type == JTokenType.Null)
                    {
                        if (!p.Optional) throw new ArgumentException("\"" + p.Name + "\" is required.");
                        continue;
                    }
                    if (p.Kind == Kind.List)
                    {
                        if (!(value is JArray array) || array.Any(x => x.Type != JTokenType.String && x.Type != JTokenType.Integer)) throw new ArgumentException("\"" + p.Name + "\" must be a list of strings.");
                        a.Values[p.Name] = array.Select(x => x.ToString()).ToList();
                    }
                    else if (value.Type == JTokenType.String || value.Type == JTokenType.Integer) a.Values[p.Name] = Convert(p, p.Kind == Kind.Hex && value.Type == JTokenType.Integer ? ((long)value).ToString("X") : value.ToString());
                    else throw new ArgumentException("\"" + p.Name + "\" has the wrong type.");
                }
                result = command.Run(a);
            }
            catch (Exception ex) { result = Error(ex); failed = true; }
            return new JObject
            {
                ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = JsonConvert.SerializeObject(result, Formatting.Indented) }),
                ["isError"] = failed
            };
        }

        // ------------------------------------------------------------------ the commands

        private static Dictionary<string, object> Ok() => new Dictionary<string, object> { { "ok", true } };
        private static string H(uint v) => v.ToString("X8");

        private static object Help()
        {
            var r = Ok();
            r["program"] = "X360GameHack " + Pastebin.Version;
            r["usage"] = "X360GameHack.exe cli <command> [arguments]   - one JSON object on standard output, exit code 0 (ok) or 1 (error).   X360GameHack.exe mcp   - the same commands as a Model Context Protocol server.";
            r["notes"] = new[]
            {
                "Start with: cli info <file>",
                "Addresses and title IDs are hexadecimal (0x optional).",
                "Commands with writes=true create or change files; all others only read.",
                "A command that edits a file keeps the original as <file>.bak the first time. Output files are never overwritten.",
                "Arguments are data only: no command starts another program or a shell.",
                "The program never runs elevated and never contacts the internet in this mode; only the console-* commands talk to a console, at the address you give.",
                "beta=true marks a command that has not been proven to work end to end yet (see TESTING.md); it may work, but keep a copy of anything it changes."
            };
            r["commands"] = Commands.Select(c => new Dictionary<string, object>
            {
                { "name", c.Name }, { "usage", c.Usage }, { "does", c.Does }, { "writes", c.Writes }, { "beta", !TestStatus.CommandTested(c.Name) },
                { "parameters", c.Params.Select(p => new Dictionary<string, object> { { "name", p.Name }, { "kind", p.Kind.ToString().ToLowerInvariant() }, { "optional", p.Optional }, { "does", p.Does } }).ToList() }
            }).ToList();
            return r;
        }

        private static TreyarchStats OpenGameSave(string path, out GameSaveKind kind, out string entry)
        {
            var pkg = StfsPackage.Read(path);
            if (!pkg.Valid) throw new InvalidDataException("That file is not an Xbox 360 package.");
            Mods.ModManager.Scan();   // free (open) save mods in the mods folder; a paid mod is never unlocked from the command line
            string why = "No save mod for this game is loaded (title " + pkg.TitleIdHex + "). Save editors come as .x360mod files: a free one in the mods folder works here; a paid one is used on the Saves page of the program after unlocking it on the Mods page.";
            using (var v = StfsVolume.Open(path))
                foreach (var e in v.Entries.Where(x => !x.IsDirectory))
                {
                    var k = GameSaves.Detect(pkg.TitleId, e.Name);
                    if (k == null) continue;
                    byte[] file = v.ReadFile(e);
                    string problem = TreyarchStats.Problem(file, k.Def);
                    if (problem != null) { why = k.Game + ": " + problem; continue; }
                    kind = k; entry = e.Path;
                    return TreyarchStats.Open(file, k.Def);
                }
            throw new InvalidDataException(why);
        }

        private static object TuCheck(string update, string game)
        {
            var tu = TitleUpdateCheck.ReadPackage(update);
            var g = TitleUpdateCheck.GameFrom(game);
            var v = TitleUpdateCheck.Compare(g, tu);
            var r = Ok();
            r["fits"] = v.Ok; r["result"] = v.Fit.ToString(); r["verdict"] = v.Headline; r["details"] = v.Lines;
            r["game"] = new Dictionary<string, object> { { "readFrom", g.From }, { "titleId", H(g.TitleId) }, { "mediaId", H(g.MediaId) },
                { "version", g.VersionKnown ? TitleUpdateCheck.VersionText(g.Version) : null } };
            var u = new Dictionary<string, object> { { "isTitleUpdate", tu.IsTitleUpdate }, { "titleId", H(tu.TitleId) }, { "mediaId", H(tu.MediaId) }, { "files", tu.Files }, { "patches", tu.Patches } };
            if (tu.HasPatch) { u["fromVersion"] = TitleUpdateCheck.VersionText(tu.SourceVersion); u["toVersion"] = TitleUpdateCheck.VersionText(tu.TargetVersion); u["titleUpdateNumber"] = TitleUpdateCheck.UpdateNumber(tu.TargetVersion); }
            else u["fromVersion"] = TitleUpdateCheck.VersionText(tu.PackageBaseVersion);
            r["update"] = u;
            r["installFolder"] = tu.InstallDir; r["installName"] = tu.InstallName;
            return r;
        }

        private static object ModInfoCmd(string path)
        {
            var pack = Mods.ModPack.Load(path);
            var i = pack.Info;
            var r = Ok();
            r["id"] = i.Id; r["name"] = i.Name; r["game"] = i.Game; r["titleId"] = i.TitleId; r["kind"] = i.Kind; r["version"] = i.Version;
            r["requires"] = i.Requires; r["description"] = i.Description;
            r["protection"] = pack.IsOpen ? "open" : i.ServerOnly ? "server-only" : "protected";
            r["needsLicence"] = !pack.IsOpen;
            r["dataInFile"] = pack.Sealed != null;
            return r;
        }

        private static object ModBuildCmd(string output, string id, string name, string protection, string payloadFile, string game, string titleId, string description, string requires)
        {
            Mods.ModProtection prot;
            switch ((protection ?? "").Trim().ToLowerInvariant())
            {
                case "open": prot = Mods.ModProtection.Open; break;
                case "protected": prot = Mods.ModProtection.Protected; break;
                case "server-only": case "serveronly": prot = Mods.ModProtection.ServerOnly; break;
                default: throw new ArgumentException("protection must be open, protected or server-only.");
            }
            if (File.Exists(output)) throw new ArgumentException("The output file already exists.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(id ?? "", "^[A-Za-z0-9._-]{1,64}$")) throw new ArgumentException("The id may hold letters, digits, dot, dash and underscore only (at most 64).");
            if (payloadFile == null && prot != Mods.ModProtection.ServerOnly) throw new ArgumentException("Give the payload JSON file.");
            string payload = payloadFile == null ? null : File.ReadAllText(payloadFile);
            var info = new Mods.ModInfo { Id = id, Name = name, Game = game, TitleId = titleId, Version = "1", Requires = requires, Description = description,
                                          Kind = payload != null && Mods.ModBuilderService.KindOf(payload) == "game" ? "game" : "save" };
            var built = Mods.ModBuilderService.Build(info, payload, prot);
            if (!built.Ok) throw new ArgumentException(string.Join(" ", built.Problems));
            string card = Mods.ModBuilderService.WriteTo(built, output);
            var r = Ok(); r["file"] = output; r["size"] = new FileInfo(output).Length; r["protection"] = protection.Trim().ToLowerInvariant();
            if (card != null) { r["keyCard"] = card; r["note"] = "The key card is the only copy of the key. Register it on your licence server and keep it secret."; }
            return r;
        }

        private static object ModSignCmd(string file)
        {
            var key = Mods.OwnerKey.Load() ?? throw new ArgumentException("Signing mods is reserved for the X360GameHack team: this PC does not hold the team's signing key.");
            byte[] bytes = File.ReadAllBytes(file);
            var pack = Mods.ModPack.Parse(bytes, file);                  // must be a readable mod file
            var r = Ok(); r["file"] = file; r["id"] = pack.Info.Id;
            if (pack.OwnerSigned) { r["changed"] = false; r["note"] = "It is already signed with the team's key."; return r; }
            byte[] signed = Mods.ModSignature.Sign(bytes, key);
            if (!Mods.ModSignature.ByOwner(signed) || !Mods.ModPack.Parse(signed, file).OwnerSigned) throw new InvalidOperationException("The signed file did not check out - nothing was written.");
            string bak = file + ".bak";
            if (!File.Exists(bak)) File.Copy(file, bak);
            File.WriteAllBytes(file, signed);
            r["changed"] = true; r["backup"] = bak; r["size"] = signed.Length;
            return r;
        }

        private static object OwnerKeyRestoreCmd(string file)
        {
            if (File.Exists(Mods.OwnerKey.FilePath)) throw new ArgumentException("This PC already holds a signing key (" + Mods.OwnerKey.FilePath + ") - it is never replaced.");
            string pw = ReadSecretLine("Backup password: ");
            if (string.IsNullOrEmpty(pw)) throw new ArgumentException("No password was given (type it, or pipe it in on standard input).");
            Mods.OwnerKey.RestoreBackup(file, pw);
            var r = Ok(); r["restored"] = Mods.OwnerKey.FilePath; r["note"] = "The signing key is back for this Windows user. Mod Maker shows the next time the program starts.";
            return r;
        }

        /// <summary>One line from standard input: piped as it is, or typed at the console without showing it.</summary>
        private static string ReadSecretLine(string prompt)
        {
            if (Console.IsInputRedirected) return (Console.In.ReadLine() ?? "").TrimEnd('\r', '\n');
            Console.Error.Write(prompt);
            var sb = new System.Text.StringBuilder();
            try
            {
                for (;;)
                {
                    var k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Enter) break;
                    if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
                    if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
                }
            }
            catch (InvalidOperationException) { return (Console.In.ReadLine() ?? "").TrimEnd('\r', '\n'); }
            Console.Error.WriteLine();
            return sb.ToString();
        }

        private static object GameSaveList(string path, string filter, int count)
        {
            if (count < 1 || count > 5000) throw new ArgumentException("count is between 1 and 5000.");
            var s = OpenGameSave(path, out GameSaveKind kind, out string entry);
            var match = s.Fields.Where(f => filter.Length == 0 || f.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            return new Dictionary<string, object>
            {
                { "ok", true }, { "game", kind.Game }, { "what", kind.What }, { "file", entry }, { "values", s.Fields.Count }, { "matching", match.Count },
                { "actions", kind.Actions.Select(x => new Dictionary<string, object> { { "name", x.Name }, { "does", x.Does } }).ToList() },
                { "items", match.Take(count).Select(f => new Dictionary<string, object> { { "name", f.Path }, { "value", s.GetText(f) }, { "kind", f.IsText ? "text" : "number" }, { "max", f.IsText ? (object)(f.Bits / 8 - 1) : f.Max } }).ToList() }
            };
        }

        private static object GameSaveSet(string path, List<string> changes, string action)
        {
            var s = OpenGameSave(path, out GameSaveKind kind, out string entry);
            byte[] before = (byte[])s.Data.Clone();
            var done = new List<Dictionary<string, object>>();
            int byAction = 0;
            if (action != null)
            {
                var a = kind.Actions.FirstOrDefault(x => x.Name.Equals(action.Trim(), StringComparison.OrdinalIgnoreCase));
                if (a == null) throw new ArgumentException("There is no action called \"" + action + "\". The actions are: " + string.Join(", ", kind.Actions.Select(x => x.Name)) + ".");
                byAction = a.Apply(s);
            }
            else
            {
                if (changes.Count == 0) throw new ArgumentException("Give at least one name=value.");
                foreach (string c in changes)
                {
                    int eq = c.LastIndexOf('=');
                    if (eq < 1) throw new ArgumentException("\"" + c + "\" is not name=value.");
                    var f = s.Find(c.Substring(0, eq));
                    string was = s.GetText(f);
                    s.SetText(f, c.Substring(eq + 1));
                    done.Add(new Dictionary<string, object> { { "name", f.Path }, { "was", was }, { "now", s.GetText(f) } });
                }
            }
            bool changed = !before.SequenceEqual(s.Data);
            if (changed)
            {
                BackupOnce(path);
                StfsEditor.ReplaceFile(path, entry, s.Data);
            }
            var r = new Dictionary<string, object> { { "ok", true }, { "game", kind.Game }, { "file", entry }, { "written", changed } };
            if (action != null) r["valuesChanged"] = byAction; else r["changes"] = done;
            if (changed) r["backup"] = path + ".bak";
            return r;
        }

        private static void BackupOnce(string path)
        {
            string bak = path + ".bak";
            if (!File.Exists(bak)) File.Copy(path, bak, false);
        }

        private static void MustNotExist(string output)
        {
            if (File.Exists(output) || Directory.Exists(output)) throw new IOException("\"" + output + "\" already exists - choose a new name.");
        }

        private static Dictionary<string, object> PackageDetails(string path)
        {
            var p = StfsPackage.Read(path);
            var sig = ConSignature.Inspect(path);
            return new Dictionary<string, object>
            {
                { "contentType", p.ContentType.ToString("X8") }, { "titleId", p.TitleIdHex }, { "titleName", p.TitleName }, { "displayName", p.DisplayName },
                { "profileId", p.ProfileId }, { "deviceId", p.DeviceId }, { "mediaId", p.MediaId.ToString("X8") },
                { "signature", sig.State.ToString() }, { "consoleId", sig.IsCon ? sig.ConsoleId : null }, { "hashesChecked", sig.HashesChecked }, { "hashesOk", sig.HashesOk }
            };
        }

        private static object Info(string path)
        {
            var kind = FileSniffer.Detect(path);
            var r = Ok();
            r["file"] = path; r["size"] = new FileInfo(path).Length; r["kind"] = kind.ToString(); r["description"] = FileSniffer.Describe(kind);
            byte[] head = new byte[4];
            using (var fs = File.OpenRead(path)) fs.Read(head, 0, 4);
            string magic = Encoding.ASCII.GetString(head);
            if (magic == "CON " || magic == "LIVE" || magic == "PIRS")
            {
                r["container"] = magic.Trim(); r["package"] = PackageDetails(path);
                r["next"] = "list / verify / extract" + (kind == WorkingFileKind.Profile ? " / profile" : "");
            }
            else if (magic == "XEX2") { r["xex"] = XexSummary(path); r["next"] = "functions / imports / disasm / decompile"; }
            else if (magic == "XBEH")
            {
                var x = XbeFile.Read(path);
                if (x.Valid)
                    r["xbe"] = new Dictionary<string, object>
                    {
                        { "title", x.TitleName }, { "titleId", x.TitleId.ToString("X8") }, { "titleIdText", x.TitleIdString.Trim() }, { "region", x.RegionString },
                        { "media", x.MediaString }, { "disc", x.DiscNumber }, { "certificateVersion", x.CertVersion }, { "built", x.TimeDate.ToString("s") },
                        { "debugBuild", x.IsDebugBuild }, { "limit64Mb", x.Limit64Mb }, { "sections", x.SectionCount }
                    };
                r["next"] = "iso-create (a disc image for xemu from the game's folder); the Original Xbox (OG) page edits title ID, region, media, the 64 MB limit and the CPU speed";
            }
            else if (kind == WorkingFileKind.DriveImage)
            {
                var vols = FatxVolume.OpenImage(path);
                try
                {
                    r["partitions"] = vols.Select(v => new Dictionary<string, object>
                    {
                        { "name", v.Name }, { "offset", "0x" + v.Offset.ToString("X") }, { "size", v.Length }, { "system", v.BigEndian ? "Xbox 360 (XTAF)" : "original Xbox (FATX)" },
                        { "clusterSize", v.ClusterSize }
                    }).ToList();
                    using (var fs = File.OpenRead(path)) r["layout"] = DriveFormatter.Detect(fs, out _)?.ToString() ?? "not one this program makes";
                }
                finally { foreach (var v in vols) v.Dispose(); }
                r["next"] = "drive-list / drive-extract / drive-check / drive-recover-list";
            }
            else if (XisoImage.Probe(path) != null) { r["discImage"] = XisoImage.Describe(XisoImage.Probe(path).Value); r["next"] = "iso-list / iso-extract / iso-swap"; }
            return r;
        }

        private static object List(string path)
        {
            var r = Ok();
            using (var v = StfsVolume.Open(path))
                r["files"] = v.Entries.Select(e => new Dictionary<string, object> { { "path", e.Path }, { "size", e.Size }, { "folder", e.IsDirectory } }).ToList();
            return r;
        }

        private static object Verify(string path)
        {
            var r = Ok();
            using (var v = StfsVolume.Open(path))
            {
                var blocks = v.VerifyAll();
                bool tree = v.VerifyTree(out string why);
                r["blocksChecked"] = blocks.Item1; r["badBlocks"] = blocks.Item2; r["hashTablesOk"] = tree; r["hashTableProblem"] = tree ? null : why;
            }
            r["package"] = PackageDetails(path);
            return r;
        }

        private static object Extract(string path, string folder)
        {
            Directory.CreateDirectory(folder);
            int n = UI.PackageContentsCard.ExtractAllTo(path, folder);
            var r = Ok(); r["extracted"] = n; r["folder"] = folder;
            r["files"] = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => f.Substring(folder.TrimEnd('\\').Length + 1)).Take(500).ToList();
            return r;
        }

        private static object TuExtract(string package, string folder)
        {
            var result = UI.Pages.TitleUpdateExtract.Run(package, folder);
            var r = Ok();
            r["folder"] = folder; r["files"] = result.Files; r["patches"] = result.Patches; r["keptAsBak"] = result.BackedUp; r["isTitleUpdate"] = result.IsTitleUpdate;
            r["next"] = result.Patches.Count > 0 ? "Apply each .xexp to the game file with the same name (xextool -p <file.xexp> <game file>; the window's Title Updates page does it for default.xexp)." : "This package holds no .xexp patch.";
            return r;
        }

        private static object Rehash(string path, string keyVaultPath)
        {
            KeyVault kv = null;
            if (keyVaultPath != null)
            {
                if (new FileInfo(keyVaultPath).Length > 1024 * 1024) throw new InvalidDataException("That file is too large to be a keyvault.");
                kv = KeyVault.Load(keyVaultPath);
            }
            string detail = "";
            var outcome = ConSignature.ApplyMany(new[] { path }, kv, BackupOnce, (file, result, text) => detail = text)[path];
            if (outcome == ConSignature.BatchOutcome.NotPackage) throw new InvalidDataException("That file is not an STFS package.");
            if (outcome == ConSignature.BatchOutcome.Failed) throw new InvalidOperationException(detail.Length > 0 ? detail : "The package could not be rehashed.");
            var r = Ok();
            r["outcome"] = outcome == ConSignature.BatchOutcome.AlreadyGood ? "already good - nothing changed" : "rehashed" + (kv != null ? " and signed" : "");
            r["detail"] = detail; r["signedWithKeyvault"] = kv != null; r["package"] = PackageDetails(path);
            if (outcome == ConSignature.BatchOutcome.Resigned) r["backup"] = path + ".bak";
            return r;
        }

        private static object CreateSave(string output, string like, List<string> files)
        {
            MustNotExist(output);
            if (files.Count == 0) throw new ArgumentException("Give at least one file to put inside.");
            var content = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                string full = Path.GetFullPath(f);
                if (!File.Exists(full)) throw new FileNotFoundException("File not found: " + full);
                if (new FileInfo(full).Length > 512L * 1024 * 1024) throw new InvalidDataException("Too large for a save: " + full);
                string name = Path.GetFileName(full);
                if (content.ContainsKey(name)) throw new ArgumentException("Two files are named " + name + ".");
                content[name] = File.ReadAllBytes(full);
            }
            var options = StfsCreateOptions.From(like);
            StfsCreator.Create(output, options, content);
            var r = Ok(); r["file"] = output; r["files"] = content.Count; r["package"] = PackageDetails(output);
            r["note"] = "Hashes are valid; it is not signed. Run 'rehash' with the owner's keyvault for a retail console.";
            return r;
        }

        private static object Profile(string path)
        {
            var p = Xbox360Profile.Open(path);
            var r = Ok();
            r["isProfile"] = p.IsProfile; r["gamertag"] = p.Account?.Gamertag; r["accountProblem"] = p.AccountError; r["profileId"] = p.ProfileId;
            r["linkedToXboxLive"] = p.IsLiveLinked; r["gamerscore"] = p.GamercardGamerscore ?? p.TotalGamerscore; r["achievements"] = p.GamercardAchievements ?? p.AchievementsUnlocked;
            r["games"] = p.Games.Select(g => new Dictionary<string, object>
            {
                { "titleId", g.TitleIdHex }, { "name", g.Name }, { "gamerscore", g.GamerscoreUnlocked }, { "gamerscoreTotal", g.GamerscoreTotal },
                { "achievements", g.AchievementsUnlocked }, { "achievementsTotal", g.AchievementCount }, { "editable", p.EditBlocker(g.TitleId) == null }
            }).ToList();
            return r;
        }

        private static object Achievements(string path, uint titleId)
        {
            var p = Xbox360Profile.Open(path);
            var r = Ok();
            r["titleId"] = H(titleId); r["blocker"] = p.EditBlocker(titleId);
            r["achievements"] = p.AchievementsFor(titleId).Select(a => new Dictionary<string, object>
            {
                { "id", a.Id }, { "name", a.Name }, { "gamerscore", a.Gamerscore }, { "unlocked", a.Unlocked }, { "when", a.UnlockTime?.ToString("yyyy-MM-dd HH:mm:ss") }
            }).ToList();
            return r;
        }

        private static object Unlock(string path, uint? titleId, bool naturalDates)
        {
            Xbox360Profile.SpreadUnlockTimes = naturalDates;
            var p = Xbox360Profile.Open(path);
            if (!p.IsProfile) throw new InvalidDataException("That file is not a gamer profile.");
            if (titleId.HasValue && p.EditBlocker(titleId.Value) != null) throw new InvalidOperationException(p.EditBlocker(titleId.Value));
            var change = titleId.HasValue ? p.UnlockAllForTitle(titleId.Value, DateTime.UtcNow) : p.UnlockAll(DateTime.UtcNow);
            var r = Ok();
            r["achievementsUnlocked"] = change.Achievements; r["gamerscoreAdded"] = change.Gamerscore; r["games"] = change.Games;
            if (p.HasChanges)
            {
                BackupOnce(path);
                r["filesChangedInside"] = p.SaveChanges();
                r["backup"] = path + ".bak";
                r["signature"] = ConSignature.Inspect(path).State.ToString();
                r["note"] = "Hashes are valid. The console signature was not renewed: RGH / JTAG consoles accept it as it is; for a retail console run 'rehash' with the owner's keyvault.";
            }
            if (p.IsLiveLinked) r["warning"] = "This profile is linked to Xbox LIVE: edited achievements upload when it signs in and can get an account reset or banned.";
            return r;
        }

        private static Dictionary<string, object> XexSummary(string path)
        {
            byte[] file = File.ReadAllBytes(path);
            var image = XexImage.Load(file);
            var program = XexProgram.Read(file, image);
            var info = XexFile.Read(path);
            return new Dictionary<string, object>
            {
                { "titleId", H(info.TitleId) }, { "mediaId", H(info.MediaId) }, { "module", program.ModuleName }, { "imageBase", H(image.ImageBase) }, { "imageSize", image.BaseFile.Length }, { "entryPoint", H(image.EntryPoint) },
                { "encryption", image.EncryptionName }, { "compression", image.CompressionName },
                { "sections", program.Sections.Select(s => new Dictionary<string, object> { { "name", s.Name }, { "address", H(s.Address) }, { "size", s.Size }, { "access", s.Access } }).ToList() },
                { "functions", program.Functions.Count }, { "imports", program.Imports.Count }, { "exports", program.Exports.Count }, { "libraries", program.Libraries }
            };
        }

        private static object Xex(string path)
        {
            var r = Ok(); r["xex"] = XexSummary(path);
            return r;
        }

        private static void Load(string path, out XexImage image, out XexProgram program)
        {
            byte[] file = File.ReadAllBytes(path);
            image = XexImage.Load(file);
            program = XexProgram.Read(file, image);
        }

        private static object Functions(string path, int from, int count)
        {
            Load(path, out var image, out var program);
            var r = Ok();
            r["total"] = program.Functions.Count; r["from"] = from;
            r["addresses"] = program.Functions.Skip(Math.Max(0, from)).Take(Math.Max(0, Math.Min(count, 20000))).Select(H).ToList();
            return r;
        }

        private static object Imports(string path)
        {
            Load(path, out var image, out var program);
            var r = Ok();
            r["imports"] = program.Imports.Select(i => new Dictionary<string, object> { { "library", i.Library }, { "ordinal", i.Ordinal }, { "name", i.Label }, { "address", H(i.Address) }, { "kind", i.IsFunction ? "function" : "pointer" } }).ToList();
            return r;
        }

        private static object Disasm(string path, uint address, int count)
        {
            Load(path, out var image, out var program);
            long offset = image.OffsetOf(address & ~3u);
            if (offset < 0) throw new ArgumentException("0x" + H(address) + " is not inside the image (0x" + H(image.ImageBase) + " - 0x" + H(image.ImageBase + (uint)image.BaseFile.Length) + ").");
            int bytes = (int)Math.Min(Math.Max(1, Math.Min(count, 4096)) * 4L, image.BaseFile.Length - offset) & ~3;
            var r = Ok();
            r["instructions"] = PowerPc.Disassemble(image.BaseFile, (int)offset, bytes, address & ~3u).Select(i => new Dictionary<string, object> { { "address", H(i.Address) }, { "word", H(i.Word) }, { "text", i.Text } }).ToList();
            return r;
        }

        private static object Decompile(string path, uint address)
        {
            Load(path, out var image, out var program);
            var range = program.FunctionAt(address, image.BaseFile);
            if (range == null) throw new ArgumentException("There is no code at 0x" + H(address) + ".");
            var names = new Dictionary<uint, string>();
            foreach (var e in program.Exports) names[e.Address] = e.Label;
            foreach (var i in program.Imports) if (i.IsFunction) names[i.Address] = i.Label;
            foreach (var kv in program.HelperNames(image.BaseFile)) names[kv.Key] = kv.Value;
            byte[] b = image.BaseFile; uint imageBase = program.ImageBase;
            Func<uint, uint?> read = a => a >= imageBase && a - imageBase + 4 <= (uint)b.Length ? (uint?)BE.U32(b, (int)(a - imageBase)) : null;
            var listing = PpcFunction.Build(read, range.Item1, range.Item2).Print(a => names.TryGetValue(a, out string n) ? n : null);
            var r = Ok();
            r["start"] = H(range.Item1); r["end"] = H(range.Item2); r["code"] = listing.Text;
            return r;
        }

        private static object Ghidra(string path, string folder)
        {
            var export = XexGhidra.Export(path, folder);
            var r = Ok();
            r["folder"] = export.Folder; r["image"] = export.ImageFile; r["map"] = export.MapFile; r["script"] = export.ScriptFile; r["batch"] = export.BatchFile;
            r["functions"] = export.Program.Functions.Count; r["imports"] = export.Program.Imports.Count;
            return r;
        }

        private static object SaveImage(string path, string output)
        {
            MustNotExist(output);
            Load(path, out var image, out var program);
            File.WriteAllBytes(output, image.BaseFile);
            var r = Ok(); r["file"] = output; r["size"] = image.BaseFile.Length; r["imageBase"] = H(image.ImageBase);
            return r;
        }

        // ---- disc images

        private static void Flatten(List<XisoEntry> entries, List<Dictionary<string, object>> into)
        {
            foreach (var e in entries)
            {
                into.Add(new Dictionary<string, object> { { "path", e.RelativePath }, { "size", e.Size }, { "folder", e.IsDirectory } });
                if (e.IsDirectory) Flatten(e.Children, into);
            }
        }

        private static object IsoList(string iso)
        {
            var r = Ok();
            using (var img = XisoImage.Open(iso))
            {
                var list = new List<Dictionary<string, object>>();
                Flatten(img.ReadRoot(), list);
                r["layout"] = XisoImage.Describe(img.Layout); r["count"] = list.Count; r["files"] = list;
            }
            return r;
        }

        private static object IsoExtract(string iso, string folder)
        {
            Directory.CreateDirectory(folder);
            using (var img = XisoImage.Open(iso)) img.ExtractAll(folder, false, null, CancellationToken.None);
            var r = Ok(); r["folder"] = folder; r["files"] = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length;
            return r;
        }

        private static object IsoCreate(string folder, string output)
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Folder not found: " + folder);
            MustNotExist(output);
            XisoBuilder.CreateFromFolder(folder, output, false, null, CancellationToken.None);
            var r = Ok(); r["file"] = output; r["size"] = new FileInfo(output).Length;
            return r;
        }

        private static object IsoSwap(string iso, string output, List<string> swaps)
        {
            MustNotExist(output);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in swaps)
            {
                int eq = s.IndexOf('=');
                if (eq <= 0 || eq == s.Length - 1) throw new ArgumentException("Each swap is written  path-in-image=file-on-this-PC  (got \"" + Shorten(s) + "\").");
                map[s.Substring(0, eq).Trim()] = s.Substring(eq + 1).Trim();
            }
            XisoBuilder.Rewrite(iso, output, false, map, null, CancellationToken.None);
            var r = Ok(); r["file"] = output; r["size"] = new FileInfo(output).Length; r["swapped"] = map.Count;
            return r;
        }

        // ---- Games on Demand

        private static string Sha256(byte[] b) { if (b == null) return null; using (var s = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", ""); }

        private static object GodInfo(string file)
        {
            var r = Ok();
            if (GodPackage.IsGod(file))
            {
                var g = GodPackage.Open(file);
                r["kind"] = "Games on Demand package";
                r["name"] = g.Info.DisplayName; r["titleId"] = g.Info.TitleIdHex; r["mediaId"] = g.Info.MediaId.ToString("X8");
                r["disc"] = g.Info.DiscNumber + "/" + g.Info.DiscCount; r["contentType"] = g.Info.ContentType.ToString("X8");
                r["layout"] = g.EnhancedGdf ? "enhanced (Microsoft)" : "plain"; r["startBlock"] = g.StartBlock; r["dataFiles"] = g.DataFiles.Count; r["dataBlocks"] = g.DataBlocks;
                r["rootHash"] = g.RootHashMissing ? "empty (ISO2God single-file package)" : "present";
                r["hasXsfLeadIn"] = g.HasXsfHeader;
                r["imageBytesExact"] = g.OutputLength(GodIsoLayout.Exact); r["imageBytesCompact"] = g.OutputLength(GodIsoLayout.Compact);
                r["packageName"] = Path.GetFileName(file);
                r["iso2godName"] = GodNames.Iso2God(g.Info.TitleId, g.Info.MediaId, (byte)g.Info.DiscNumber, (byte)g.Info.DiscCount);
                return r;
            }
            var info = GodTitleInfo.FromImage(file, CancellationToken.None);
            r["kind"] = "disc image";
            r["platform"] = info.Xbox360 ? "Xbox 360" : "original Xbox"; r["contentType"] = info.ContentType.ToString("X8");
            r["name"] = info.Name; r["dashboardName"] = info.DashboardName(file, true);
            r["titleId"] = info.TitleId.ToString("X8"); r["mediaId"] = info.MediaId.ToString("X8");
            r["version"] = info.Version.ToString("X8"); r["baseVersion"] = info.BaseVersion.ToString("X8");
            r["disc"] = info.DiscNumber + "/" + info.DiscCount; r["platformByte"] = info.Platform; r["executableType"] = info.ExecutableType;
            r["thumbnailBytes"] = info.Thumbnail?.Length ?? 0; r["thumbnailSha256"] = Sha256(info.Thumbnail);
            r["iso2godName"] = GodNames.Iso2God(info.TitleId, info.MediaId, info.DiscNumber, info.DiscCount);
            r["folder"] = GodBuilder.ContentFolder(info.TitleId, info.ContentType);
            r["packageBytes"] = new Dictionary<string, object>
            {
                { "none", GodBuilder.EstimateBytes(file, GodPadding.None, CancellationToken.None) },
                { "partial", GodBuilder.EstimateBytes(file, GodPadding.Partial, CancellationToken.None) },
                { "full", GodBuilder.EstimateBytes(file, GodPadding.Full, CancellationToken.None) }
            };
            r["rebuiltImageBytes"] = GodBuilder.EstimateRebuiltBytes(file, CancellationToken.None);
            if (info.Notes.Length > 0) r["notes"] = info.Notes;
            return r;
        }

        private static object Iso2God(string iso, string folder, string padding, List<string> options)
        {
            bool isFolder = Directory.Exists(iso);
            if (!isFolder && !File.Exists(iso)) throw new FileNotFoundException("Not found: " + iso);
            var o = new GodBuildOptions();
            GodTarget? target = null;
            bool? xexUnlock = null, pkgUnlock = null;
            switch ((padding ?? "").Trim().ToLowerInvariant())
            {
                case "none": o.Padding = GodPadding.None; break;
                case "partial": o.Padding = GodPadding.Partial; break;
                case "full": o.Padding = GodPadding.Full; break;
                default: throw new ArgumentException("padding is none, partial or full.");
            }
            foreach (string s in options)
            {
                int eq = s.IndexOf('=');
                if (eq <= 0) throw new ArgumentException("Each option is written  name=value  (got \"" + Shorten(s) + "\").");
                string k = s.Substring(0, eq).Trim().ToLowerInvariant(), v = s.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "name": if (v.Length == 0 || v.Length > 127) throw new ArgumentException("name is 1 to 127 characters."); o.DisplayName = v; break;
                    case "naming":
                        if (v.Equals("iso2god", StringComparison.OrdinalIgnoreCase)) o.Naming = GodNaming.Iso2God;
                        else if (v.Equals("microsoft", StringComparison.OrdinalIgnoreCase)) o.Naming = GodNaming.Microsoft;
                        else throw new ArgumentException("naming is iso2god or microsoft.");
                        break;
                    case "rebuilt": o.RebuiltIsoPath = Path.GetFullPath(v); MustNotExist(o.RebuiltIsoPath); break;
                    case "disc-suffix": o.AddDiscNumber = !v.Equals("no", StringComparison.OrdinalIgnoreCase); break;
                    case "thumbnail":
                        if (v.Equals("none", StringComparison.OrdinalIgnoreCase)) o.ThumbnailPng = new byte[0];
                        else
                        {
                            string p = Path.GetFullPath(v);
                            if (!File.Exists(p)) throw new FileNotFoundException("File not found: " + p);
                            o.ThumbnailPng = File.ReadAllBytes(p);
                            if (o.ThumbnailPng.Length > GodThumbnail.MaxBytes || o.ThumbnailPng.Length < 8 || o.ThumbnailPng[0] != 0x89 || o.ThumbnailPng[1] != (byte)'P')
                                throw new ArgumentException("thumbnail must be a PNG of at most " + GodThumbnail.MaxBytes + " bytes (64x64 is what the dashboard shows).");
                        }
                        break;
                    case "target":
                        if (v.Equals("rgh", StringComparison.OrdinalIgnoreCase) || v.Equals("jtag", StringComparison.OrdinalIgnoreCase)) target = GodTarget.Rgh;
                        else if (v.Equals("devkit", StringComparison.OrdinalIgnoreCase) || v.Equals("xdk", StringComparison.OrdinalIgnoreCase)) target = GodTarget.Devkit;
                        else throw new ArgumentException("target is rgh or devkit.");
                        break;
                    case "xex-unlock": xexUnlock = YesNo(v, k); break;
                    case "pkg-unlock": pkgUnlock = YesNo(v, k); break;
                    default: throw new ArgumentException("Unknown option \"" + k + "\" (name, naming, rebuilt, disc-suffix, thumbnail, target, xex-unlock, pkg-unlock).");
                }
            }
            if (o.RebuiltIsoPath != null && (o.Padding != GodPadding.Full || isFolder)) throw new ArgumentException("rebuilt= only goes with padding full and a disc image.");
            // never replace anything: with ISO2God names the package's path is known before writing
            var info = isFolder ? GodTitleInfo.FromFolder(iso) : GodTitleInfo.FromImage(iso, CancellationToken.None);
            if (info.TitleId == 0) throw new ArgumentException("The game's executable has no title ID; this command cannot ask for one - give the game one first.");
            o.Info = info;
            if (o.Naming == GodNaming.Iso2God)
            {
                string pkg = Path.Combine(folder, GodBuilder.ContentFolder(info.TitleId, info.ContentType), GodNames.Iso2God(info.TitleId, info.MediaId, info.DiscNumber, info.DiscCount));
                MustNotExist(pkg);
                MustNotExist(pkg + ".data");
            }
            var job = new GodJobOptions
            {
                Build = o,
                Target = target ?? GodTarget.Rgh,
                UnlockXex = xexUnlock ?? target.HasValue,
                UnlockPackage = pkgUnlock ?? target.HasValue
            };
            if (job.Target == GodTarget.Devkit && info.Xbox360 && xexUnlock == false) job.UnlockXex = false;   // still converted to devkit form
            var res = GodJobs.RunAsync(iso, folder, job, null, CancellationToken.None).GetAwaiter().GetResult();
            var r = Ok();
            r["source"] = isFolder ? "game folder (XEX version)" : "disc image";
            r["target"] = target.HasValue ? (target == GodTarget.Devkit ? "devkit" : "rgh") : "none (as ISO2God)";
            r["xexPatched"] = info.Xbox360 && (target == GodTarget.Devkit || job.UnlockXex) ? (job.UnlockXex ? "unlocked, " : "") + (job.Target == GodTarget.Devkit ? "devkit" : "retail") : "no";
            r["packageUnlocked"] = job.UnlockPackage ? GodTargets.PkgToolArgs(job.Target) : "no";
            r["package"] = res.HeaderPath; r["dataFolder"] = res.DataFolder; r["name"] = res.DisplayName;
            r["titleId"] = res.TitleId.ToString("X8"); r["mediaId"] = res.MediaId.ToString("X8"); r["contentType"] = res.ContentType.ToString("X8");
            r["padding"] = res.Padding.ToString().ToLowerInvariant(); r["storedBytes"] = res.StoredBytes; r["dataFiles"] = res.DataFiles; r["dataBytes"] = res.DataBytes;
            r["headerSha256"] = Sha256(File.ReadAllBytes(res.HeaderPath));
            r["verified"] = true;
            if (res.RebuiltIsoPath != null) { r["rebuiltImage"] = res.RebuiltIsoPath; r["rebuiltImageBytes"] = new FileInfo(res.RebuiltIsoPath).Length; }
            if (res.Notes.Length > 0) r["notes"] = res.Notes;
            return r;
        }

        private static object NandInfo(string dump, List<string> options)
        {
            var n = NandImage.Open(dump);
            var r = Ok();
            r["kind"] = n.Kind.ToString(); r["board"] = n.Board; r["bytes"] = n.FileLength;
            r["boardFromCb"] = n.CbBoard.ToString(); r["boardFromSmc"] = n.SmcBoard.ToString(); r["imageNotes"] = n.ImageNotes;
            r["retail"] = !n.Devkit; r["copyright"] = n.Copyright;
            r["smcPatched"] = n.SmcPatched.HasValue ? (object)n.SmcPatched.Value : "not known (J-Runner's test is for Xenon to Jasper SMCs)";
            r["bootloaders"] = n.Bootloaders.Select(b => new Dictionary<string, object> { { "stage", b.Name }, { "build", b.Build }, { "offset", "0x" + b.Offset.ToString("X") }, { "size", "0x" + b.Size.ToString("X") }, { "slot", b.Slot } }).ToList();
            r["dashboard"] = n.Bootloaders.Where(b => b.Magic == "CF").Select(b => b.Build).ToList();
            r["smcVersion"] = n.SmcVersion;
            r["pagesChecked"] = n.PagesChecked; r["edcErrors"] = n.EccErrors.Count; r["badBlocks"] = n.BadBlocks; r["remapped"] = n.Remapped.Count; r["zeroBlocks"] = n.ZeroBlocks.Count;
            r["fsRoot"] = n.FsRootBlock; r["fsRoots"] = n.FsRoots.Count;
            r["files"] = n.Files.Select(f => new Dictionary<string, object> { { "name", f.Name }, { "size", f.Size }, { "pieces", f.Contiguous ? 1 : 2 } }).ToList();
            string keyText = NandKeyText(dump, options, true, out bool show);
            var c = n.SmcConfig;
            if (c != null)
            {
                var cfg = new Dictionary<string, object>
                {
                    { "checksumOk", c.ChecksumOk }, { "targetTemperaturesC", c.Temperatures.Take(3).ToArray() }, { "criticalTemperaturesC", c.Temperatures.Skip(3).ToArray() },
                    { "cpuFan", NandSmcConfig.FanText(c.CpuFan) }, { "gpuFan", NandSmcConfig.FanText(c.GpuFan) },
                    { "region", c.Region + " - " + NandKeyVault.RegionName(c.Region) }, { "dvdRegion", NandSmcConfig.DvdRegionText(c.DvdRegion) },
                    { "address", "0x" + (c.Address + c.ConfigOffset).ToString("X") }
                };
                if (show) { cfg["mac"] = c.Mac; cfg["resetCode"] = c.ResetCode; }
                r["smcConfig"] = cfg;
            }
            r["fcrtInFlash"] = n.Fcrt != null;
            byte[] chainKey = null;
            if (keyText != null)
            {
                var key = NandImage.ParseCpuKey(keyText.Trim().Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
                if (key == null) throw new ArgumentException("A CPU key is 32 hex digits.");
                var kv = n.DecryptKeyVault(key);
                r["cpuKeyUnlocksKeyvault"] = kv != null && kv.CpuKeyMatches;
                if (kv != null && kv.CpuKeyMatches)
                {
                    r["region"] = kv.Region + " - " + NandKeyVault.RegionName(kv.Region);
                    r["keyvaultType"] = kv.Type; r["needsFcrt"] = kv.NeedsFcrt;
                    if (n.Fcrt != null) r["fcrtDecrypts"] = NandImage.DecryptFcrt(n.Fcrt, key) != null;
                    if (show) { r["serial"] = kv.Serial; r["consoleId"] = kv.ConsoleIdFriendly; r["consoleIdInKeyvault"] = kv.ConsoleId; r["motherboardSerial"] = kv.MotherboardSerial; r["dvdKey"] = kv.DvdKey; r["osig"] = kv.Osig; r["made"] = kv.ManufacturingDate; }
                    chainKey = (byte[])key.Clone();
                }
                Array.Clear(key, 0, key.Length);
            }
            // the boot chain decrypted (the CPU key only when it unlocked this console's keyvault), lockdown values and pairing data
            var chain = n.DecryptBootChain(chainKey);
            if (chainKey != null) Array.Clear(chainKey, 0, chainKey.Length);
            r["stages"] = chain.Select(d => new Dictionary<string, object> { { "stage", d.Stage.Name }, { "build", d.Stage.Build }, { "decryptedWith", d.How }, { "checksOut", d.LooksRight } }).ToList();
            var l = n.ReadLockdown(chain);
            r["lockdown"] = new Dictionary<string, object>
            {
                { "cbLdv", l.CbLdv }, { "cbPairing", l.CbPairing }, { "fromCbB", l.FromCbB }, { "zeroPaired", l.ZeroPaired },
                { "slots", l.Slots.Select(s => new Dictionary<string, object> { { "slot", s.Slot }, { "build", s.Build }, { "ldv", s.Ldv }, { "pairing", s.Pairing } }).ToList() }
            };
            return r;
        }

        /// <summary>The CPU key text for a flash command: cpukey= (digits or a file), else a cpukey.txt beside the dump.</summary>
        private static string NandKeyText(string dump, List<string> options, bool allowShow, out bool show)
        {
            string keyText = null;
            show = false;
            foreach (string o in options)
            {
                int eq = o.IndexOf('=');
                if (eq <= 0) throw new ArgumentException("Each option is written  name=value.");
                string k = o.Substring(0, eq).Trim().ToLowerInvariant(), v = o.Substring(eq + 1).Trim();
                if (k == "cpukey") keyText = File.Exists(v) ? File.ReadAllText(v) : v;
                else if (k == "kv" && allowShow) show = v.Equals("show", StringComparison.OrdinalIgnoreCase);
                else throw new ArgumentException("Unknown option \"" + k + "\" (" + (allowShow ? "cpukey, kv" : "cpukey") + ").");
            }
            if (keyText == null)
            {
                string beside = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dump)), "cpukey.txt");
                if (File.Exists(beside)) keyText = File.ReadAllText(beside);
            }
            return keyText;
        }

        private static object NandStages(string dump, string folder, List<string> options)
        {
            if (File.Exists(folder) || Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException("\"" + folder + "\" already exists - give a new folder.");
            var n = NandImage.Open(dump);
            string keyText = NandKeyText(dump, options, false, out _);
            byte[] key = null;
            bool unlocks = false;
            if (keyText != null)
            {
                key = NandImage.ParseCpuKey(keyText.Trim().Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
                if (key == null) throw new ArgumentException("A CPU key is 32 hex digits.");
                var kv = n.DecryptKeyVault(key);
                unlocks = kv != null && kv.CpuKeyMatches;
                if (!unlocks) { Array.Clear(key, 0, key.Length); key = null; }
            }
            var chain = n.DecryptBootChain(key);
            if (key != null) Array.Clear(key, 0, key.Length);
            if (n.Bootloaders.Count == 0) throw new InvalidDataException("No boot stages were found in this dump.");
            Directory.CreateDirectory(folder);
            var written = new List<object>();
            foreach (var b in n.Bootloaders)
            {
                var stored = n.StageBytes(b);
                File.WriteAllBytes(PathSafety.CombineUnder(folder, b.Name + ".bin"), stored);
                var e = new Dictionary<string, object> { { "stage", b.Name }, { "build", b.Build }, { "bytes", stored.Length }, { "sha256", Sha256(stored) } };
                var d = chain.FirstOrDefault(x => x.Stage == b);
                if (d == null) e["decrypted"] = "no - the stage it depends on is missing";
                else if (d.LooksRight == false) e["decrypted"] = "no - " + d.How + (keyText != null && !unlocks ? " (the CPU key given does not unlock this console's keyvault)" : "");
                else
                {
                    File.WriteAllBytes(PathSafety.CombineUnder(folder, b.Name + "_dec.bin"), d.Data);
                    e["decrypted"] = d.How + (d.LooksRight == true ? " (checks out)" : "");
                    e["decryptedSha256"] = Sha256(d.Data);
                }
                written.Add(e);
            }
            var r = Ok();
            r["folder"] = Path.GetFullPath(folder); r["cpuKeyUsed"] = unlocks; r["stages"] = written;
            return r;
        }

        private static object NandExtract(string dump, string folder)
        {
            if (File.Exists(folder) || Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException("\"" + folder + "\" already exists - give a new folder.");
            var n = NandImage.Open(dump);
            if (n.Files.Count == 0) throw new InvalidDataException(n.HasSpare ? "No flash file system was found in this dump." : "This dump has no spare bytes - a Corona 4 GB keeps its files on the eMMC's own partition, not in the flash file system.");
            Directory.CreateDirectory(folder);
            var written = new List<object>();
            foreach (var f in n.Files)
            {
                string target = PathSafety.CombineUnder(folder, PathSafety.SafeName(f.Name));
                byte[] data = n.ReadFile(f);
                File.WriteAllBytes(target, data);
                written.Add(new Dictionary<string, object> { { "name", f.Name }, { "bytes", data.Length }, { "sha256", Sha256(data) } });
            }
            var r = Ok();
            r["folder"] = Path.GetFullPath(folder); r["root"] = n.FsRootBlock; r["files"] = written;
            return r;
        }

        private static object NandReplaceFile(string dump, string name, string source, string output)
        {
            if (File.Exists(output) || Directory.Exists(output)) throw new IOException("\"" + output + "\" already exists - give a new file name.");
            var n = NandImage.Open(dump);
            var newData = File.ReadAllBytes(source);
            var bytes = n.BuildWithReplacedFile(name, newData, out string warning);
            File.WriteAllBytes(output, bytes);
            // read the written dump back and make sure it is sound before keeping it
            try
            {
                var m = NandImage.Open(output);
                var f = m.Files.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                bool ok = m.EccErrors.Count <= n.EccErrors.Count && f != null && f.Size == newData.Length && m.ReadFile(f).SequenceEqual(newData)
                          && m.Files.Count == n.Files.Count && m.BadBlocks.SequenceEqual(n.BadBlocks)
                          && m.Bootloaders.Select(b => b.Name + b.Build).SequenceEqual(n.Bootloaders.Select(b => b.Name + b.Build))
                          && (m.KeyVaultEncrypted ?? new byte[0]).SequenceEqual(n.KeyVaultEncrypted ?? new byte[0])
                          && m.Files.Where(x => !x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).All(x => { var o = n.Files.First(y => y.Name == x.Name); return x.Size == o.Size && m.ReadFile(x).SequenceEqual(n.ReadFile(o)); });
                if (!ok) throw new InvalidDataException("the written dump did not read back correctly");
            }
            catch (Exception ex)
            {
                try { File.Delete(output); } catch { }
                throw new InvalidDataException("The new dump failed its read-back check and was deleted (" + ex.Message + "). Nothing was changed.");
            }
            var r = Ok();
            r["output"] = Path.GetFullPath(output); r["replaced"] = name; r["newBytes"] = newData.Length; r["bytes"] = bytes.Length;
            r["note"] = "Read back correctly. Flash it as a NAND on an RGH / JTAG console and keep the original as a backup. Untested on a console here (BETA).";
            if (warning != null) r["warning"] = warning;
            return r;
        }

        private static object NandReplaceKv(string dump, string keyvault, string output, List<string> options)
        {
            if (File.Exists(output) || Directory.Exists(output)) throw new IOException("\"" + output + "\" already exists - give a new file name.");
            var n = NandImage.Open(dump);
            var kv = File.ReadAllBytes(keyvault);
            string keyText = NandKeyText(dump, options, false, out _);
            byte[] key = keyText == null ? null : NandImage.ParseCpuKey(keyText.Trim().Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
            if (keyText != null && key == null) throw new ArgumentException("A CPU key is 32 hex digits.");
            var bytes = n.BuildWithReplacedKeyVault(kv, out string warning);
            File.WriteAllBytes(output, bytes);
            bool unlocks = false;
            try
            {
                var m = NandImage.Open(output);
                bool ok = m.EccErrors.Count <= n.EccErrors.Count && (m.KeyVaultEncrypted ?? new byte[0]).SequenceEqual(kv)
                          && m.Files.Count == n.Files.Count && m.BadBlocks.SequenceEqual(n.BadBlocks)
                          && m.Bootloaders.Select(b => b.Name + b.Build).SequenceEqual(n.Bootloaders.Select(b => b.Name + b.Build))
                          && m.Files.All(f => { var o = n.Files.First(y => y.Name == f.Name); return f.Size == o.Size && m.ReadFile(f).SequenceEqual(n.ReadFile(o)); });
                if (!ok) throw new InvalidDataException("the written dump did not read back correctly");
                if (key != null) { var d = m.DecryptKeyVault(key); unlocks = d != null && d.CpuKeyMatches; }
            }
            catch (Exception ex)
            {
                try { File.Delete(output); } catch { }
                throw new InvalidDataException("The new dump failed its read-back check and was deleted (" + ex.Message + "). Nothing was changed.");
            }
            if (key != null) Array.Clear(key, 0, key.Length);
            if (key != null && !unlocks)
            {
                try { File.Delete(output); } catch { }
                throw new InvalidDataException("The CPU key given does not unlock the new keyvault - check they belong together. The new dump was deleted; nothing was changed.");
            }
            var r = Ok();
            r["output"] = Path.GetFullPath(output); r["keyvaultBytes"] = kv.Length; r["bytes"] = bytes.Length;
            if (keyText != null) r["cpuKeyUnlocksNewKeyvault"] = unlocks;
            r["note"] = "Read back correctly" + (keyText != null ? " and the CPU key unlocks the new keyvault" : "") + ". Flash it as a NAND on an RGH / JTAG console with that keyvault's own CPU key; keep the original as a backup. Untested on a console here (BETA).";
            if (warning != null) r["warning"] = warning;
            return r;
        }

        private static object ConvertGame(string source, string to, string output, List<string> options)
        {
            bool isFolder = Directory.Exists(source);
            if (!isFolder && !File.Exists(source)) throw new FileNotFoundException("Not found: " + source);
            var o = new GameConvertOptions();
            switch ((to ?? "").Trim().ToLowerInvariant())
            {
                case "god": case "package": o.To = GameFormat.Package; break;
                case "iso": case "image": o.To = GameFormat.Image; break;
                case "folder": case "xex": o.To = GameFormat.Folder; break;
                default: throw new ArgumentException("to is god, iso or folder.");
            }
            o.God.Build.Padding = GodPadding.Full;
            GodTarget? target = null;
            bool? xexUnlock = null, pkgUnlock = null;
            foreach (string s in options)
            {
                int eq = s.IndexOf('=');
                if (eq <= 0) throw new ArgumentException("Each option is written  name=value  (got \"" + Shorten(s) + "\").");
                string k = s.Substring(0, eq).Trim().ToLowerInvariant(), v = s.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "target":
                        if (v.Equals("rgh", StringComparison.OrdinalIgnoreCase) || v.Equals("jtag", StringComparison.OrdinalIgnoreCase)) target = GodTarget.Rgh;
                        else if (v.Equals("devkit", StringComparison.OrdinalIgnoreCase) || v.Equals("xdk", StringComparison.OrdinalIgnoreCase)) target = GodTarget.Devkit;
                        else throw new ArgumentException("target is rgh or devkit.");
                        break;
                    case "xex-unlock": xexUnlock = YesNo(v, k); break;
                    case "pkg-unlock": pkgUnlock = YesNo(v, k); break;
                    case "padding":
                        switch (v.ToLowerInvariant())
                        {
                            case "none": o.God.Build.Padding = GodPadding.None; break;
                            case "partial": o.God.Build.Padding = GodPadding.Partial; break;
                            case "full": o.God.Build.Padding = GodPadding.Full; break;
                            default: throw new ArgumentException("padding is none, partial or full.");
                        }
                        break;
                    case "naming":
                        if (v.Equals("iso2god", StringComparison.OrdinalIgnoreCase)) o.God.Build.Naming = GodNaming.Iso2God;
                        else if (v.Equals("microsoft", StringComparison.OrdinalIgnoreCase)) o.God.Build.Naming = GodNaming.Microsoft;
                        else throw new ArgumentException("naming is iso2god or microsoft.");
                        break;
                    case "name": if (v.Length == 0 || v.Length > 127) throw new ArgumentException("name is 1 to 127 characters."); o.God.Build.DisplayName = v; break;
                    case "layout":
                        if (v.Equals("compact", StringComparison.OrdinalIgnoreCase)) o.Iso.Layout = GodIsoLayout.Compact;
                        else if (v.Equals("exact", StringComparison.OrdinalIgnoreCase)) o.Iso.Layout = GodIsoLayout.Exact;
                        else throw new ArgumentException("layout is compact or exact.");
                        break;
                    case "fix-createisogood": o.Iso.FixCreateIsoGoodHeader = YesNo(v, k); break;
                    case "skip-update": o.SkipSystemUpdate = YesNo(v, k); break;
                    case "stealth": o.StealthPatch = YesNo(v, k); break;
                    default: throw new ArgumentException("Unknown option \"" + k + "\" (target, xex-unlock, pkg-unlock, padding, naming, name, layout, fix-createisogood, skip-update, stealth).");
                }
            }
            if (o.StealthPatch && o.To != GameFormat.Image) throw new ArgumentException("stealth= only goes with to=iso.");
            var info = isFolder ? GodTitleInfo.FromFolder(source) : GodTitleInfo.FromImage(source, CancellationToken.None);
            if (o.To == GameFormat.Package && info.TitleId == 0) throw new ArgumentException("The game's executable has no title ID; this command cannot ask for one - give the game one first.");
            o.God.Build.Info = info;
            o.God.Target = target ?? GodTarget.Rgh;
            o.God.UnlockXex = xexUnlock ?? target.HasValue;
            o.God.UnlockPackage = o.To == GameFormat.Package && (pkgUnlock ?? target.HasValue);
            if (target == GodTarget.Devkit && info.Xbox360 && xexUnlock == false) o.God.UnlockXex = false;   // still converted to devkit form
            if (target == null && xexUnlock != true) o.God.Target = GodTarget.Rgh;
            if (o.To == GameFormat.Package && o.God.Build.Naming == GodNaming.Iso2God)
            {
                string pkg = Path.Combine(output, GodBuilder.ContentFolder(info.TitleId, info.ContentType), GodNames.Iso2God(info.TitleId, info.MediaId, info.DiscNumber, info.DiscCount));
                MustNotExist(pkg);
                MustNotExist(pkg + ".data");
            }
            else if (o.To == GameFormat.Image) MustNotExist(output);
            else if (o.To == GameFormat.Folder && (File.Exists(output) || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()))
                throw new IOException("\"" + output + "\" already exists - give a new folder.");
            bool patching = info.Xbox360 && (o.God.Target == GodTarget.Devkit && target.HasValue || o.God.UnlockXex);
            var res = GameConvert.RunAsync(source, output, o, null, CancellationToken.None).GetAwaiter().GetResult();
            var r = Ok();
            r["from"] = GameSources.Describe(res.From);
            r["to"] = GameSources.Describe(res.To);
            r["output"] = res.Output;
            r["files"] = res.Files;
            r["target"] = target.HasValue ? (target == GodTarget.Devkit ? "devkit" : "rgh") : "none (as it is)";
            r["xexPatched"] = patching ? (o.God.UnlockXex ? "unlocked, " : "") + (o.God.Target == GodTarget.Devkit ? "devkit" : "retail") : "no";
            if (res.Package != null)
            {
                r["packageUnlocked"] = o.God.UnlockPackage ? GodTargets.PkgToolArgs(o.God.Target) : "no";
                r["name"] = res.Package.DisplayName; r["titleId"] = res.Package.TitleId.ToString("X8"); r["padding"] = res.Package.Padding.ToString().ToLowerInvariant();
                r["dataFiles"] = res.Package.DataFiles; r["dataBytes"] = res.Package.DataBytes;
            }
            if (res.To == GameFormat.Image && File.Exists(res.Output)) r["bytes"] = new FileInfo(res.Output).Length;
            if (res.Stealth != null) r["stealth"] = res.Stealth.Summary();
            if (res.To == GameFormat.Image) r["madeHere"] = GameConvert.MadeHereNote;
            if (res.Notes.Length > 0) r["notes"] = res.Notes;
            return r;
        }

        private static bool YesNo(string v, string name)
        {
            if (v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (v.Equals("no", StringComparison.OrdinalIgnoreCase) || v.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            throw new ArgumentException(name + " is yes or no.");
        }

        private static object God2Iso(string package, string output, string layout, bool fixCreateIsoGood)
        {
            MustNotExist(output);
            var o = new GodIsoOptions { FixCreateIsoGoodHeader = fixCreateIsoGood };
            switch ((layout ?? "").Trim().ToLowerInvariant())
            {
                case "compact": o.Layout = GodIsoLayout.Compact; break;
                case "exact": o.Layout = GodIsoLayout.Exact; break;
                default: throw new ArgumentException("layout is compact or exact.");
            }
            if (!GodPackage.IsGod(package)) throw new ArgumentException("That is not a Games on Demand package (pick the file without an extension that sits next to its .data folder).");
            var g = GodPackage.Open(package);
            var res = g.ConvertToIso(output, o, null, CancellationToken.None);
            var r = Ok();
            r["file"] = res.Path; r["size"] = res.Length; r["files"] = res.Files;
            r["layout"] = o.Layout.ToString().ToLowerInvariant(); r["compacted"] = res.Compacted; r["leadInWritten"] = res.LeadInWritten;
            r["sectorsMoved"] = res.SectorsMoved; r["entriesMoved"] = res.EntriesMoved; r["createIsoGoodFixed"] = res.CreateIsoGoodFixed;
            r["rootHash"] = g.RootHashMissing ? "empty (checked against the package's own hash tables)" : "matched";
            return r;
        }

        // ---- Xenia

        private static object PatchList(string patchFile)
        {
            var f = XeniaPatchFile.Load(patchFile);
            var r = Ok();
            r["titleId"] = f.TitleId; r["titleName"] = f.TitleName; r["hashes"] = f.Hashes;
            r["patches"] = f.Patches.Select((p, i) => new Dictionary<string, object> { { "number", i + 1 }, { "name", p.Name }, { "author", p.Author }, { "on", p.IsEnabled }, { "writes", p.Operations.Count }, { "description", p.Description } }).ToList();
            return r;
        }

        private static object PatchInstall(string patchFile, string xenia, List<string> on)
        {
            xenia = xenia ?? Tools.BundledXenia;
            if (!File.Exists(xenia)) throw new FileNotFoundException("Xenia was not found: " + xenia);
            var f = XeniaPatchFile.Load(patchFile);
            var chosen = new HashSet<int>();
            foreach (string s in on)
            {
                if (!int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1 || n > f.Patches.Count) throw new ArgumentException("\"" + Shorten(s) + "\" is not a patch number between 1 and " + f.Patches.Count + ".");
                chosen.Add(n);
            }
            for (int i = 0; i < f.Patches.Count; i++) f.Patches[i].IsEnabled = chosen.Contains(i + 1);
            string to = XeniaPatchTools.Install(f, xenia);
            var r = Ok(); r["installed"] = to; r["on"] = f.Patches.Where(p => p.IsEnabled).Select(p => p.Name).ToList(); r["patches"] = f.Patches.Count;
            r["note"] = "Xenia applies the file the next time the game starts.";
            return r;
        }

        private static object XeniaRead(uint address, int length)
        {
            if (length < 1 || length > 65536) throw new ArgumentException("Length is 1 to 65536 bytes.");
            using (var x = XeniaProcess.Attach())
            {
                byte[] data = x.GetMemory(address, (uint)length);
                var r = Ok(); r["address"] = H(address); r["length"] = data.Length; r["bytes"] = BitConverter.ToString(data).Replace("-", "");
                return r;
            }
        }

        private static object XeniaWrite(uint address, string hex)
        {
            string digits = new string((hex ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray());
            if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) digits = digits.Substring(2);
            if (digits.Length == 0 || digits.Length % 2 != 0 || digits.Length > 131072 || !digits.All(Uri.IsHexDigit)) throw new ArgumentException("The bytes are written as pairs of hexadecimal digits, for example 3B600001.");
            var data = new byte[digits.Length / 2];
            for (int i = 0; i < data.Length; i++) data[i] = byte.Parse(digits.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            using (var x = XeniaProcess.Attach())
            {
                byte[] before = x.GetMemory(address, (uint)data.Length);
                x.SetMemory(address, data);
                var r = Ok(); r["address"] = H(address); r["written"] = data.Length; r["before"] = BitConverter.ToString(before).Replace("-", "");
                return r;
            }
        }

        private static byte[] HexBytes(string hex)
        {
            string digits = new string((hex ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray());
            if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) digits = digits.Substring(2);
            if (digits.Length == 0 || digits.Length % 2 != 0 || digits.Length > 131072 || !digits.All(Uri.IsHexDigit)) throw new ArgumentException("The bytes are written as pairs of hexadecimal digits, for example 3B600001.");
            var data = new byte[digits.Length / 2];
            for (int i = 0; i < data.Length; i++) data[i] = byte.Parse(digits.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return data;
        }

        // ---- drive images

        private static T OnVolume<T>(string image, int partition, bool writable, Func<FatxVolume, T> work)
        {
            var vols = FatxVolume.OpenImage(image, writable);
            try
            {
                if (vols.Count == 0) throw new InvalidDataException("No Xbox (FATX / XTAF) file system was found in " + System.IO.Path.GetFileName(image) + ".");
                if (partition < 1 || partition > vols.Count) throw new ArgumentException("Partition is a number from 1 to " + vols.Count + " (see drive-list).");
                return work(vols[partition - 1]);
            }
            finally { foreach (var v in vols) v.Dispose(); }
        }

        private static string DrivePath(string text)
        {
            string p = (text ?? "").Replace('/', '\\').Trim().Trim('\\');
            foreach (string part in p.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
                if (part == "." || part == "..") throw new ArgumentException("\"" + part + "\" cannot be part of a path on the drive.");
            return p;
        }

        /// <summary>Every place on the drive in order (Drive Explorer's Partitions tab): read partitions, ones that cannot be read and why, unused space.</summary>
        private static List<Dictionary<string, object>> LayoutOf(List<FatxVolume> vols) =>
            FatxVolume.Layout(vols).Select(s => new Dictionary<string, object> { { "tag", s.Tag }, { "name", s.Name }, { "offset", "0x" + s.Offset.ToString("X") }, { "size", s.Size },
                { "read", s.Volume != null }, { "note", s.Note } }).ToList();

        private static object DriveList(string image, int partition, string folder)
        {
            var r = Ok();
            if (partition == 0)
            {
                var vols = FatxVolume.OpenImage(image);
                try
                {
                    if (vols.Count == 0) throw new InvalidDataException("No Xbox (FATX / XTAF) file system was found in " + System.IO.Path.GetFileName(image) + ".");
                    r["partitions"] = vols.Select((v, i) => new Dictionary<string, object> { { "number", i + 1 }, { "name", v.Name }, { "size", v.Length },
                        { "system", v.BigEndian ? "Xbox 360 (XTAF)" : "original Xbox (FATX)" }, { "clusterSize", v.ClusterSize } }).ToList();
                    r["layout"] = LayoutOf(vols);
                }
                finally { foreach (var v in vols) v.Dispose(); }
                return r;
            }
            string path = DrivePath(folder);
            return OnVolume(image, partition, false, v =>
            {
                var items = v.ListDirectory(v.ResolveDirectory(path, false));
                r["partition"] = v.Name; r["folder"] = "\\" + path; r["count"] = items.Count;
                r["items"] = items.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(e => new Dictionary<string, object> { { "name", e.Name }, { "folder", e.IsDirectory }, { "size", e.Size }, { "modified", e.Modified == DateTime.MinValue ? "" : e.Modified.ToString("s") } }).ToList();
                return (object)r;
            });
        }

        // ---- recovery of deleted files (read only on the drive; files go to this PC)

        private static IEnumerable<FatxDeleted> Flatten(IEnumerable<FatxDeleted> xs)
        {
            foreach (var x in xs) { yield return x; foreach (var y in Flatten(x.Children)) yield return y; }
        }

        private static Dictionary<string, object> Describe(FatxDeleted x) => new Dictionary<string, object>
        {
            { "path", x.LostChain ? "(lost data)\\" + x.Name : x.FullPath }, { "folder", x.IsDirectory }, { "size", x.Size }, { "state", x.State.ToString() },
            { "type", x.Type }, { "clusters", x.Clusters }, { "clustersInUse", x.ClustersInUse }, { "firstCluster", x.FirstCluster }, { "lost", x.LostChain },
            { "readByChain", x.ByChain }, { "pieces", x.Pieces }, { "clustersGoneAround", x.Skipped },
            { "modified", x.Modified == DateTime.MinValue ? "" : x.Modified.ToString("s") }, { "note", x.Note }, { "inside", x.Children.Count }
        };

        private static object RecoverList(FatxVolume v, string lost)
        {
            bool withLost = !string.Equals(lost, "no", StringComparison.OrdinalIgnoreCase);
            var found = v.FindDeleted(withLost);
            var all = Flatten(found).ToList();
            var r = Ok();
            r["partition"] = v.Name;
            r["deleted"] = all.Count(x => !x.LostChain);
            r["recoverable"] = all.Count(x => !x.LostChain && x.State == RecoveryState.Recoverable);
            r["partlyOverwritten"] = all.Count(x => !x.LostChain && x.State == RecoveryState.PartlyOverwritten);
            r["overwritten"] = all.Count(x => !x.LostChain && x.State == RecoveryState.Overwritten);
            r["lostItems"] = all.Count(x => x.LostChain);
            r["lostBytes"] = all.Where(x => x.LostChain).Sum(x => x.Size);
            r["items"] = all.Select(Describe).ToList();
            return r;
        }

        /// <summary>
        /// Recovers into <paramref name="to"/>, each item in the folders it was in (lost data in "(lost data)").
        /// which: null / "recoverable" = every fully recoverable item; "all" = also partly overwritten and non-empty lost data;
        /// anything else = the deleted file or folder at that path.
        /// </summary>
        private static object RecoverInto(FatxVolume v, string to, string which)
        {
            to = System.IO.Path.GetFullPath(to);
            var found = v.FindDeleted(true);
            var pick = new List<FatxDeleted>();
            string w = (which ?? "recoverable").Trim();
            if (w.Equals("recoverable", StringComparison.OrdinalIgnoreCase) || w.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                bool all = w.Equals("all", StringComparison.OrdinalIgnoreCase);
                foreach (var x in found)
                {
                    if (x.LostChain) { if (all && x.Type != FatxVolume.EmptyType) pick.Add(x); continue; }
                    if (x.State == RecoveryState.Recoverable || (all && x.State == RecoveryState.PartlyOverwritten)) pick.Add(x);
                }
            }
            else
            {
                string p = DrivePath(w);
                var hit = Flatten(found).FirstOrDefault(x => string.Equals(x.LostChain ? x.Name : x.FullPath, p, StringComparison.OrdinalIgnoreCase))
                          ?? throw new FileNotFoundException("Nothing deleted at \\" + p + " (drive-recover-list shows what there is).");
                pick.Add(hit);
            }
            long bytes = 0;
            var written = new List<string>();
            int files = v.RecoverInto(pick, to, n => bytes += n, CancellationToken.None, written);
            var r = Ok();
            r["partition"] = v.Name; r["output"] = to; r["items"] = pick.Count; r["files"] = files; r["bytes"] = bytes;
            r["written"] = written.Take(500).ToList();
            return r;
        }

        private static object DriveCheck(string image, int partition) => OnVolume(image, partition, false, v =>
        {
            var c = v.Check();
            var r = Ok();
            r["partition"] = v.Name; r["healthy"] = c.Healthy; r["files"] = c.Files; r["folders"] = c.Folders;
            r["freeBytes"] = c.FreeClusters * v.ClusterSize; r["usedBytes"] = c.UsedClusters * v.ClusterSize; r["unownedBytes"] = c.LostClusters * v.ClusterSize;
            r["problems"] = c.Problems.Take(200).ToList();
            return (object)r;
        });

        private static object DriveExtract(string image, int partition, string path, string folder) => OnVolume(image, partition, false, v =>
        {
            string p = DrivePath(path);
            var e = v.FindPath(p) ?? throw new FileNotFoundException("Not found on the drive: \\" + p);
            Directory.CreateDirectory(folder);
            string target = PathSafety.CombineUnder(folder, PathSafety.SafeName(e.Name));
            if (e.IsDirectory) v.ExtractDirectory(e, target, null, CancellationToken.None);
            else { if (File.Exists(target)) throw new IOException("Already exists: " + target); v.ExtractFile(e, target, null, CancellationToken.None); }
            var r = Ok(); r["saved"] = target; r["folder"] = e.IsDirectory; r["bytes"] = e.IsDirectory ? v.MeasureDirectory(e) : e.Size;
            return (object)r;
        });

        private static object DriveAdd(string image, int partition, string folder, List<string> items)
        {
            if (items.Count == 0) throw new ArgumentException("Give at least one file or folder to add.");
            var full = items.Select(System.IO.Path.GetFullPath).ToList();
            foreach (string p in full) if (!File.Exists(p) && !Directory.Exists(p)) throw new FileNotFoundException("Not found: " + p);
            var problems = full.SelectMany(FatxVolume.NameProblemsIn).ToList();
            if (problems.Count > 0) throw new ArgumentException("Nothing was copied. Names the Xbox does not accept: " + string.Join(" ", problems.Take(8)));
            string path = DrivePath(folder);
            return OnVolume(image, partition, true, v =>
            {
                long total = full.Sum(Net.FtpService.MeasureLocal);
                if (total > v.FreeBytes) throw new IOException("Not enough free space on the drive: " + total + " bytes to copy, " + v.FreeBytes + " free.");
                uint dir = v.ResolveDirectory(path, true);
                foreach (string p in full)
                {
                    if (Directory.Exists(p)) v.WriteDirectory(dir, p);
                    else v.WriteFile(dir, p);
                }
                var r = Ok(); r["partition"] = v.Name; r["folder"] = "\\" + path; r["added"] = full.Count; r["bytes"] = total; r["freeBytes"] = v.FreeBytes;
                return (object)r;
            });
        }

        private static object DriveInstall(string image, int partition, List<string> packages)
        {
            var full = packages.Select(System.IO.Path.GetFullPath).ToList();
            foreach (string p in full) if (!File.Exists(p) && !Directory.Exists(p)) throw new FileNotFoundException("Not found: " + p);
            var pkgs = ContentInstaller.Collect(full);
            if (pkgs.Count == 0) throw new ArgumentException("None of the given files is an Xbox 360 package.");
            return OnVolume(image, partition, true, v =>
            {
                ContentInstaller.InstallToVolume(pkgs, v, null, CancellationToken.None);
                var r = Ok(); r["partition"] = v.Name;
                r["installed"] = pkgs.Select(p => new Dictionary<string, object> { { "name", ContentInstaller.Label(p) }, { "to", "\\" + p.InstallDir + "\\" + p.InstallName } }).ToList();
                r["freeBytes"] = v.FreeBytes;
                return (object)r;
            });
        }

        private static object DriveDelete(string image, int partition, string path) => OnVolume(image, partition, true, v =>
        {
            string p = DrivePath(path);
            if (p.Length == 0) throw new ArgumentException("The root folder cannot be deleted.");
            var e = v.FindPath(p) ?? throw new FileNotFoundException("Not found on the drive: \\" + p);
            v.Delete(e);
            var r = Ok(); r["deleted"] = "\\" + p; r["wasFolder"] = e.IsDirectory; r["freeBytes"] = v.FreeBytes;
            return (object)r;
        });

        // ---- a physical Xbox drive, through the Disk Reader helper

        private static object DiskEject(int disk)
        {
            var info = RawDisk.List().FirstOrDefault(d => d.Index == disk) ?? throw new ArgumentException("There is no disk " + disk + " (it may be unplugged already).");
            if (info.IsSystem) throw new ArgumentException("Disk " + disk + " is the Windows disk - it is never removed.");
            string why = RawDisk.EjectAsync(disk).GetAwaiter().GetResult();
            if (why != null) throw new IOException(why);
            var r = Ok(); r["removed"] = info.ToString(); r["note"] = "It can be unplugged now. To use it again without unplugging it: disk-remount.";
            return r;
        }

        private static object DiskRemount()
        {
            string why = RawDisk.RemountAsync().GetAwaiter().GetResult();
            if (why != null) throw new IOException(why);
            var r = Ok(); r["disks"] = RawDisk.List().Select(d => d.ToString()).ToList();
            return r;
        }

        private static object DiskRun(int disk, string mode, string stepsFile)
        {
            if (mode != "ro" && mode != "rw") throw new ArgumentException("mode is ro (read only) or rw (changes allowed, journaled).");
            bool rw = mode == "rw";
            var steps = JArray.Parse(File.ReadAllText(stepsFile));
            if (steps.Count == 0 || steps.Count > 200) throw new ArgumentException("Give 1 to 200 steps.");
            foreach (var s in steps)
            {
                string op = (string)s["op"] ?? "";
                if (!new[] { "partitions", "list", "tree", "get", "backup", "check", "sessions", "recover-list", "recover", "mkdir", "put", "delete", "undo", "create-hddx", "install-compat" }.Contains(op)) throw new ArgumentException("Unknown step: " + op);
                if (!rw && new[] { "mkdir", "put", "delete", "undo", "create-hddx", "install-compat" }.Contains(op)) throw new ArgumentException("\"" + op + "\" changes the drive - run with mode rw.");
            }
            var info = RawDisk.List().FirstOrDefault(d => d.Index == disk) ?? throw new ArgumentException("There is no disk " + disk + ".");
            if (info.IsSystem) throw new ArgumentException("Disk " + disk + " is the Windows disk - it is never opened.");
            var stream = RawDisk.OpenAsync(disk, CancellationToken.None, rw).GetAwaiter().GetResult();
            var vols = FatxVolume.OpenStream(stream, "Disk " + disk, true);
            var results = new List<object>();
            var r = Ok();
            r["disk"] = info.ToString(); r["mode"] = rw ? "changes allowed (journaled)" : "read only"; r["session"] = stream.Session;
            try
            {
                if (vols.Count == 0) throw new InvalidDataException("No FATX / XTAF partition could be read on that drive.");
                FatxVolume Vol(JToken s)
                {
                    if (s["partition"]?.Type == JTokenType.String)
                    {
                        string want = (string)s["partition"];
                        var named = vols.Where(v => string.Equals(v.Name, want, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (named.Count != 1) throw new ArgumentException("No partition is called \"" + want + "\" - the partitions are: " + string.Join(", ", vols.Select(v => v.Name)) + ".");
                        return named[0];
                    }
                    int n = (int?)s["partition"] ?? 0;
                    if (n < 1 || n > vols.Count) throw new ArgumentException("partition is a number from 1 to " + vols.Count + " (see the partitions step).");
                    return vols[n - 1];
                }
                foreach (var s in steps)
                {
                    string op = (string)s["op"];
                    var one = new Dictionary<string, object> { { "op", op } };
                    results.Add(one);
                    switch (op)
                    {
                        case "partitions":
                            one["partitions"] = vols.Select((v, i) => new Dictionary<string, object> { { "number", i + 1 }, { "name", v.Name }, { "offset", "0x" + v.Offset.ToString("X") }, { "size", v.Length },
                                { "system", v.BigEndian ? "Xbox 360 (XTAF)" : "original Xbox (FATX)" }, { "clusterSize", v.ClusterSize }, { "clusters", v.ClusterCount }, { "rootCluster", v.RootCluster }, { "freeBytes", v.FreeBytes } }).ToList();
                            one["layout"] = LayoutOf(vols);
                            break;
                        case "list":
                        {
                            var v = Vol(s); string path = DrivePath((string)s["folder"] ?? "");
                            var items = v.ListDirectory(v.ResolveDirectory(path, false));
                            one["partition"] = v.Name; one["folder"] = "\\" + path;
                            one["items"] = items.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                                .Select(e => new Dictionary<string, object> { { "name", e.Name }, { "folder", e.IsDirectory }, { "size", e.Size }, { "modified", e.Modified == DateTime.MinValue ? "" : e.Modified.ToString("s") } }).ToList();
                            break;
                        }
                        case "tree":
                        {
                            var v = Vol(s); string to = System.IO.Path.GetFullPath((string)s["to"] ?? throw new ArgumentException("tree needs \"to\" (a .json file on this PC)."));
                            var lines = new List<Dictionary<string, object>>();
                            void Walk(uint cluster, string prefix, int depth)
                            {
                                if (depth > 40) return;
                                foreach (var e in v.ListDirectory(cluster))
                                {
                                    string p = prefix.Length == 0 ? e.Name : prefix + "\\" + e.Name;
                                    lines.Add(new Dictionary<string, object> { { "path", p }, { "folder", e.IsDirectory }, { "size", e.Size }, { "cluster", e.FirstCluster }, { "modified", e.Modified == DateTime.MinValue ? "" : e.Modified.ToString("s") } });
                                    if (e.IsDirectory && e.FirstCluster != 0) Walk(e.FirstCluster, p, depth + 1);
                                }
                            }
                            Walk(v.RootCluster, "", 0);
                            File.WriteAllText(to, JsonConvert.SerializeObject(lines, Formatting.Indented));
                            one["partition"] = v.Name; one["entries"] = lines.Count; one["saved"] = to;
                            break;
                        }
                        case "get":
                        {
                            var v = Vol(s); string p = DrivePath((string)s["path"] ?? "");
                            string folder = System.IO.Path.GetFullPath((string)s["to"] ?? throw new ArgumentException("get needs \"to\" (a folder on this PC)."));
                            var e = v.FindPath(p) ?? throw new FileNotFoundException("Not found on the drive: \\" + p);
                            Directory.CreateDirectory(folder);
                            string target = PathSafety.CombineUnder(folder, PathSafety.SafeName(e.Name));
                            if (e.IsDirectory) v.ExtractDirectory(e, target, null, CancellationToken.None);
                            else { if (File.Exists(target)) throw new IOException("Already exists: " + target); v.ExtractFile(e, target, null, CancellationToken.None); }
                            one["saved"] = target; one["bytes"] = e.IsDirectory ? v.MeasureDirectory(e) : e.Size;
                            break;
                        }
                        case "backup":
                        {
                            string to = System.IO.Path.GetFullPath((string)s["to"] ?? throw new ArgumentException("backup needs \"to\" (an image file on this PC)."));
                            string progress = (string)s["progress"];
                            long size = stream.Length;
                            long free = new DriveInfo(System.IO.Path.GetPathRoot(to)).AvailableFreeSpace;
                            if (free < size + (64L << 20)) throw new IOException("Not enough room on " + System.IO.Path.GetPathRoot(to) + ": the drive is " + size + " bytes, " + free + " free.");
                            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to));
                            using (var o = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 20))
                            using (var h = System.Security.Cryptography.SHA256.Create())
                            {
                                var buf = new byte[4 << 20];
                                long done = 0; var last = DateTime.MinValue; var started = DateTime.Now;
                                while (done < size)
                                {
                                    int want = (int)Math.Min(buf.Length, size - done), got = 0;
                                    stream.Position = done;
                                    while (got < want) { int n = stream.Read(buf, got, want - got); if (n <= 0) break; got += n; }
                                    if (got != want) throw new IOException("The drive stopped answering at byte " + done + ".");
                                    o.Write(buf, 0, got); h.TransformBlock(buf, 0, got, null, 0); done += got;
                                    if (progress != null && (DateTime.Now - last).TotalSeconds >= 5)
                                    {
                                        last = DateTime.Now;
                                        try { File.WriteAllText(progress, done + " of " + size + " bytes (" + (done * 100 / size) + "%) after " + (int)(DateTime.Now - started).TotalSeconds + " s"); } catch { }
                                    }
                                }
                                h.TransformFinalBlock(buf, 0, 0);
                                string sha = BitConverter.ToString(h.Hash).Replace("-", "").ToLowerInvariant();
                                File.WriteAllText(to + ".sha256", sha + " *" + System.IO.Path.GetFileName(to) + Environment.NewLine);
                                if (progress != null) try { File.WriteAllText(progress, "done: " + size + " bytes, SHA-256 " + sha); } catch { }
                                one["saved"] = to; one["bytes"] = size; one["sha256"] = sha;
                            }
                            break;
                        }
                        case "check":
                        {
                            var v = Vol(s); var c = v.Check();
                            one["partition"] = v.Name; one["healthy"] = c.Healthy; one["files"] = c.Files; one["folders"] = c.Folders;
                            one["freeBytes"] = c.FreeClusters * v.ClusterSize; one["usedBytes"] = c.UsedClusters * v.ClusterSize; one["unownedBytes"] = c.LostClusters * v.ClusterSize;
                            one["problems"] = c.Problems.Take(200).ToList();
                            if (!c.Healthy) throw new IOException("The check found problems on " + v.Name + " - the run stopped there, nothing after it was done.");
                            break;
                        }
                        case "recover-list":
                        {
                            var res = (Dictionary<string, object>)RecoverList(Vol(s), (string)s["lost"]);
                            string to = (string)s["to"];
                            if (to != null)
                            {
                                File.WriteAllText(System.IO.Path.GetFullPath(to), JsonConvert.SerializeObject(res, Formatting.Indented));
                                res.Remove("items"); one["saved"] = System.IO.Path.GetFullPath(to);
                            }
                            foreach (var kv in res) if (kv.Key != "ok") one[kv.Key] = kv.Value;
                            break;
                        }
                        case "recover":
                        {
                            string to = (string)s["to"] ?? throw new ArgumentException("recover needs \"to\" (a folder on this PC).");
                            var res = (Dictionary<string, object>)RecoverInto(Vol(s), to, (string)s["which"]);
                            foreach (var kv in res) if (kv.Key != "ok") one[kv.Key] = kv.Value;
                            break;
                        }
                        case "sessions":
                            one["sessions"] = rw ? stream.UndoSessions() : new List<string>();
                            if (!rw) one["note"] = "Undo sessions are listed in mode rw.";
                            break;
                        case "mkdir":
                        {
                            var v = Vol(s); string p = DrivePath((string)s["path"] ?? "");
                            if (p.Length == 0) throw new ArgumentException("mkdir needs a path.");
                            v.ResolveDirectory(p, true);
                            one["created"] = "\\" + p;
                            break;
                        }
                        case "put":
                        {
                            var v = Vol(s); string path = DrivePath((string)s["folder"] ?? "");
                            var items = (s["items"] as JArray)?.Select(x => System.IO.Path.GetFullPath((string)x)).ToList() ?? new List<string>();
                            if (items.Count == 0) throw new ArgumentException("put needs items (files or folders on this PC).");
                            foreach (string p in items) if (!File.Exists(p) && !Directory.Exists(p)) throw new FileNotFoundException("Not found: " + p);
                            var bad = items.SelectMany(FatxVolume.NameProblemsIn).ToList();
                            if (bad.Count > 0) throw new ArgumentException("Nothing was copied. Names the Xbox does not accept: " + string.Join(" ", bad.Take(8)));
                            long total = items.Sum(Net.FtpService.MeasureLocal);
                            if (total > v.FreeBytes) throw new IOException("Not enough free space: " + total + " bytes to copy, " + v.FreeBytes + " free.");
                            uint dir = v.ResolveDirectory(path, true);
                            foreach (string p in items) { if (Directory.Exists(p)) v.WriteDirectory(dir, p); else v.WriteFile(dir, p); }
                            one["folder"] = "\\" + path; one["added"] = items.Count; one["bytes"] = total;
                            break;
                        }
                        case "delete":
                        {
                            var v = Vol(s); string p = DrivePath((string)s["path"] ?? "");
                            if (p.Length == 0) throw new ArgumentException("The root folder cannot be deleted.");
                            var e = v.FindPath(p) ?? throw new FileNotFoundException("Not found on the drive: \\" + p);
                            v.Delete(e);
                            one["deleted"] = "\\" + p; one["wasFolder"] = e.IsDirectory;
                            break;
                        }
                        case "create-hddx":
                        {
                            FatxVolume.CreateCompatibilityPartition(stream);
                            vols = FatxVolume.OpenStream(stream, "Disk " + disk, false);      // read the drive's partitions again (the stream stays open)
                            var hx = vols.FirstOrDefault(v => v.Offset == FatxVolume.CompatibilityOffset);
                            if (hx == null || !hx.Check().Healthy) throw new IOException("The new compatibility partition did not read back as a healthy volume.");
                            one["created"] = FatxVolume.CompatibilityName; one["partitions"] = vols.Count;
                            break;
                        }
                        case "install-compat":
                        {
                            var hx = vols.FirstOrDefault(v => v.Offset == FatxVolume.CompatibilityOffset) ?? throw new InvalidOperationException("This drive has no compatibility partition - add a create-hddx step first.");
                            string from = FatxVolume.CompatibilitySource(System.IO.Path.GetFullPath((string)s["from"] ?? throw new ArgumentException("install-compat needs \"from\" (the Compatibility folder on this PC).")));
                            var bad = FatxVolume.NameProblemsIn(from);
                            if (bad.Count > 0) throw new ArgumentException("Nothing was copied. Names the Xbox does not accept: " + string.Join(" ", bad.Take(8)));
                            long total = Net.FtpService.MeasureLocal(from);
                            if (total > hx.FreeBytes) throw new IOException("Not enough free space on the compatibility partition: " + total + " bytes, " + hx.FreeBytes + " free.");
                            hx.InstallCompatibility(from);
                            one["installed"] = "\\Compatibility"; one["from"] = from; one["bytes"] = total;
                            one["files"] = hx.ListDirectory(hx.ResolveDirectory("Compatibility", false)).Select(e => e.Name).ToList();
                            break;
                        }
                        case "undo":
                        {
                            string name = (string)s["session"] ?? "";
                            if (name == "last") name = stream.UndoSessions().FirstOrDefault(x => x != stream.Session) ?? "";
                            string why = stream.Undo(name, out int restored, out int total);
                            one["session"] = name; one["restored"] = restored; one["total"] = total;
                            // the partitions are read again: the ones opened before hold the allocation table as it was before the undo
                            vols = FatxVolume.OpenStream(stream, "Disk " + disk, false);
                            if (why != null) throw new IOException(why);
                            break;
                        }
                    }
                    one["ok"] = true;
                }
            }
            catch (Exception ex)
            {
                r["ok"] = false; r["error"] = ex.Message;
                if (results.Count > 0 && results[results.Count - 1] is Dictionary<string, object> lastStep && !lastStep.ContainsKey("ok")) lastStep["error"] = ex.Message;
            }
            finally
            {
                r["blocksWritten"] = stream.BlocksWritten;
                foreach (var v in vols) v.Dispose();
                stream.Dispose();
            }
            r["steps"] = results;
            return r;
        }

        // ---- console over XBDM: one connection per command, closed again before the answer is given

        private static object ConsoleFind()
        {
            var found = ConsoleService.DiscoverAsync(true).GetAwaiter().GetResult();
            var r = Ok(); r["consoles"] = found; r["count"] = found.Count;
            return r;
        }

        /// <summary>
        /// True while serving MCP: the console connection is kept between tool calls instead of one connect per command,
        /// because every connect uses up one of the few connections the console's debug service can hold.
        /// </summary>
        private static bool _keepConsole;

        private static object OnConsole(string ip, Func<object> work)
        {
            System.Net.IPAddress parsed;
            if (ip == null || ip.Length > 15 || !System.Net.IPAddress.TryParse(ip.Trim(), out parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || ip.Trim().Split('.').Length != 4)
                throw new ArgumentException("The console's address is four numbers with dots, for example 192.168.1.50.");
            string want = parsed.ToString();
            bool reuse = ConsoleService.IsConnected && ConsoleService.Ip == want;
            if (!reuse && !ConsoleService.ConnectAsync(want).GetAwaiter().GetResult())
                throw new IOException(ConsoleService.LastConnectProblem.Length > 0 ? ConsoleService.LastConnectProblem : "Could not connect to the console.");
            try { return work(); }
            finally { if (!_keepConsole) ConsoleService.Disconnect(); }
        }

        private static string ConsolePath(string text)
        {
            string p = (text ?? "").Trim().Replace('/', '\\');
            int colon = p.IndexOf(':');
            if (colon < 1 || p.Length > 400 || p.Any(ch => ch < 0x20 || ch == '"')) throw new ArgumentException("A console path starts with a drive, for example Hdd:\\Content.");
            foreach (string part in p.Substring(colon + 1).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
                if (part == "." || part == "..") throw new ArgumentException("\"" + part + "\" cannot be part of a console path.");
            return p;
        }

        private static object ConsoleInfo()
        {
            var s = ConsoleService.GetSummaryAsync().GetAwaiter().GetResult();
            var r = Ok();
            r["name"] = s.Name; r["ip"] = s.Ip; r["type"] = s.ConsoleType; r["xbdm"] = s.DmVersion; r["kernel"] = s.Kernel; r["board"] = s.Motherboard;
            r["state"] = s.ExecutionState; r["runningTitle"] = s.RunningTitleId;
            r["drives"] = ConsoleService.GetDrivesAsync().GetAwaiter().GetResult()
                .Select(d => new Dictionary<string, object> { { "name", d.DriveName }, { "totalBytes", d.Space.TotalBytes }, { "freeBytes", d.Space.TotalFreeBytes } }).ToList();
            r["modules"] = ConsoleService.RunAsync(c => c.GetModules()).GetAwaiter().GetResult()
                .Select(m => new Dictionary<string, object> { { "name", m.Name }, { "base", H(m.BaseAddress) }, { "size", H(m.Size) } }).ToList();
            return r;
        }

        private static object ConsoleList(string folder)
        {
            string p = ConsolePath(folder);
            if (!p.EndsWith("\\") && p.EndsWith(":")) p += "\\";
            var items = ConsoleService.ListAsync(p).GetAwaiter().GetResult();
            var r = Ok(); r["folder"] = p; r["count"] = items.Count;
            r["items"] = items.Select(i => new Dictionary<string, object> { { "name", i.Name }, { "folder", i.IsDirectory }, { "size", i.Size }, { "modified", i.Modified == DateTime.MinValue ? "" : i.Modified.ToString("s") } }).ToList();
            return r;
        }

        private static object ConsoleGet(string remote, string output)
        {
            MustNotExist(output);
            ConsoleService.DownloadFileAsync(ConsolePath(remote), output, null, CancellationToken.None).GetAwaiter().GetResult();
            var r = Ok(); r["file"] = output; r["size"] = new FileInfo(output).Length;
            return r;
        }

        private static object ConsoleDumpStorage(string device, string output, int megabytes)
        {
            MustNotExist(output);
            var started = DateTime.Now;
            return ConsoleService.RunAsync(c =>
            {
                var route = StorageDump.RouteFor(c, out string why) ?? throw new InvalidOperationException(why);
                string path = (device ?? "").Trim();
                if (!path.StartsWith("\\Device\\", StringComparison.OrdinalIgnoreCase))
                {
                    var hit = StorageDump.Offer(c).FirstOrDefault(d => d.Drive.Equals(path.TrimEnd(':', '\\'), StringComparison.OrdinalIgnoreCase))
                              ?? throw new ArgumentException("The console lists no storage called \"" + device + "\" that can be copied (INTUSB, INTMMC).");
                    path = hit.Path;
                }
                long bytes = StorageDump.Dump(c, route, path, output, megabytes > 0 ? (long)megabytes << 20 : 0, null, CancellationToken.None, out string sha);
                var r = Ok();
                r["device"] = path; r["route"] = route == StorageDump.Route.Xdrpc ? "XDRPC (the devkit's own)" : "JRPC2 plugin";
                r["image"] = output; r["bytes"] = bytes; r["sha256"] = sha; r["seconds"] = (int)(DateTime.Now - started).TotalSeconds;
                return (object)r;
            }).GetAwaiter().GetResult();
        }

        private static object ConsolePut(string file, string remote)
        {
            string p = ConsolePath(remote);
            ConsoleService.UploadToPathAsync(file, p, null, CancellationToken.None).GetAwaiter().GetResult();
            var r = Ok(); r["remote"] = p; r["size"] = new FileInfo(file).Length;
            return r;
        }

        private static object ConsoleRead(uint address, int length)
        {
            if (length < 1 || length > 65536) throw new ArgumentException("Length is 1 to 65536 bytes.");
            byte[] data = ConsoleService.RunAsync(c => c.GetMemory(address, (uint)length)).GetAwaiter().GetResult();
            var r = Ok(); r["address"] = H(address); r["length"] = data.Length; r["bytes"] = BitConverter.ToString(data).Replace("-", "");
            return r;
        }

        private static object ConsoleWrite(uint address, string hex)
        {
            byte[] data = HexBytes(hex);
            byte[] before = ConsoleService.RunAsync(c => { byte[] old = c.GetMemory(address, (uint)data.Length); c.SetMemory(address, data); return old; }).GetAwaiter().GetResult();
            var r = Ok(); r["address"] = H(address); r["written"] = data.Length; r["before"] = BitConverter.ToString(before).Replace("-", "");
            return r;
        }

        private static object ConsoleShot(string output)
        {
            MustNotExist(output);
            bool ok = ConsoleService.RunAsync(c => c.Screenshot(output)).GetAwaiter().GetResult();
            if (!ok || !File.Exists(output)) throw new IOException("The console did not return a picture.");
            var r = Ok(); r["file"] = output; r["size"] = new FileInfo(output).Length;
            return r;
        }
    }
}
