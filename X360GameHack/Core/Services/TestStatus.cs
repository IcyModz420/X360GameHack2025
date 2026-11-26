using System;
using System.Collections.Generic;
using System.Linq;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// What has been tested end to end, and how. The window shows a BETA label on every part of a page that is NOT
    /// listed here (see UI\BetaMarks.cs), and TESTING.md is the same list written out.
    ///
    /// The rule for an entry - all three must hold, otherwise it stays BETA:
    ///  1. the button itself was pressed in the real window, or its own handler was run with a file in place of the
    ///     file picker;
    ///  2. everything the button is for was checked on the real thing: on the real console for console features; for
    ///     files, against the identical output of a trusted tool (xextool, extract-xiso, X360PkgTool, XbeTool) or the real
    ///     source data.
    ///     A file that was only checked by our own checkers and never used by a console, game or emulator is NOT
    ///     tested end to end (profile and save edits, created packages, swapped / converted images, drive images);
    ///  3. nothing that matters to it has changed since the test.
    /// Engine-only tests, tests against a stand-in console, "no error" checks and "it says so" checks do not count.
    ///
    /// Names are exactly what the window shows: side-menu entry, tab path ("Outer > Inner"), card title, button text.
    /// "*" matches anything; an empty button means the card's own content (a list or view without a button).
    /// </summary>
    public static class TestStatus
    {
        public sealed class Proof
        {
            public string Code, Test, What;
            public bool Live;
        }

        public sealed class Entry
        {
            public string Page, Tab, Card, Button;
            public string[] Proofs;
        }

        public static readonly Proof[] Proofs =
        {
            P("XI",   "UiXexIso",        false, "2026-11-18, release build: every XEX Tool and Xbox ISOs button pressed in the real window on copies; each result compared byte for byte with xextool / extract-xiso run separately and with xextool's own report (360 checks, all passed)."),
            P("D1",   "UiDrive",         false, "2026-11-18, release build: a real profile shown correctly (gamertag, Xbox LIVE state, gamerscore, games, achievements), read back from the file."),
            P("D2",   "UiDrive2",        false, "2026-11-18, release build: the avatar award lists of a real profile shown correctly."),
            P("D3",   "UiDrive3",        false, "2026-11-18, release build: Extract all (every file byte for byte), View GPD and every GPD viewer tab on real profiles."),
            P("D4",   "UiDrive4",        false, "2026-11-18, release build: a real game XEX opened in the disassembler; Jump, Find all and Disassemble agree with the disassembler (itself checked against Capstone on 846,466 instructions)."),
            P("D6",   "UiDrive6",        false, "2026-11-18, release build: Disassemble / Decompile on a selection and on the page; a real title update extracted byte for byte; the Peek Poker console / Xenia switch."),
            P("INI",  "UiIni",           false, "2026-11-18, release build: launch.ini edits (change a value, add JRPC2 / XRPC, undo, use this text) checked on the user's real launch.ini text."),
            P("MK",   "UiMaker",         false, "2026-11-18, release build: Mod Maker templates and syntax check (line numbers, red lines, live checking)."),
            P("MD",   "UiMods",          false, "2026-11-18, release build: the Mods page lists games, groups, options and mod files correctly."),
            P("FI",   "UiFinders",       true,  "2026-11-18: the Profiles and Title updates finders read the real console's profiles and title updates (read only)."),
            P("PKG",  "PkgSweep",        false, "Extract all through the same code on 37 real packages (saves, title updates, DLC, arcade, themes), every file hash-checked."),
            P("LC",   "UiConsole",       true,  "live on the Trinity RGH3 through the real window: connect / disconnect, console information, screenshot, modules, freeze / resume, file and save lists, Peek, search, regions."),
            P("LC2",  "UiConsole2",      true,  "live: Peek Poker read / write / trainer freeze (held against 5 outside changes), Debugger threads and modules, a console save copied to the PC and put back byte for byte."),
            P("LDB",  "UiDebug",         true,  "live: Debugger Break froze the console, registers and code shown, Continue let it run again."),
            P("LF",   "UiFiles",         true,  "live: new folder, upload a file and a folder (read back identical), delete a folder with everything in it; Console launch box (Aurora restarted) and the Avatar Editor shortcut."),
            P("LFTP", "UiFtp",           true,  "live: FTP login (a wrong login explained), drives and folders listed."),
            P("LX",   "UiXenia / UiXenia2", true, "live: Play in Xenia for a game on the console, over XBDM and over FTP; the game was copied and Xenia showed it running."),
            P("LS",   "UiSaveE2E",       true,  "live: a console save put back and read back from the console byte for byte."),
            P("LT",   "ConsoleLiveTest", true,  "live on 2026-11-18 (current console code): temperatures, title update install / find / delete (installed byte for byte; the console applies updates installed this way), profile replace, launch.ini load / send / read back with the new plugins loading after the restart."),
            P("LV",   "UiLive",          true,  "live on the Trinity RGH3 through the real window, 2026-11-18 evening, one connection: Discover on every console page; the Neighborhood shortcuts; upload, download and rename of files and folders, byte for byte; saves and profiles downloaded with valid signatures and hashes; a save replaced and deleted and a profile deleted - test copies only, the real ones untouched; title updates downloaded with valid Microsoft signatures; a console screenshot saved; the module list read; a raw XBDM command answered; Console information read again (name, kernel and XBDM version as the console reports them)."),
            P("EM",   "UiEmu",           false, "2026-11-18, release build, in the bundled Xenia Canary with real games: Terraria (an arcade package) and SpongeBob (a game folder, a disc image built by extract-xiso, a swapped image, Games on Demand both ways) started from these buttons and seen running - Xenia's log names the game and its memory holds the game's code exactly as xextool decodes it; patches installed and applied by Xenia, and written into the running game; every Peek Poker value type written into and read back from Xenia's memory, the trainer, searches, Poke changes and Dump checked against Xenia's memory read separately; links answered. Also run with the program folder read-only, as in Program Files."),
            P("XC",   "UiXc",            false, "2026-11-18, release build: console profiles and a console save copied into Xenia land where Xenia looks for them, unpacked as Xenia's own installer does, every file byte for byte; Xenia opened both profiles and signed in to the copied one; Copy to Xenia from an Xbox drive image brought the save's profile along and Xenia signed in to it; Play in Xenia from an Xbox drive image started Terraria (a package) and SpongeBob (a game folder)."),
            P("OG",   "UiOg",            false, "2026-11-18 night, release build, on a copy of a real game XBE (NFS Underground 2), against XbeTool run directly: every value of the XBE header and Certificate cards equals XbeTool's own listing (title, title ID, version, disc, base address, sections, init flags, image size, allowed media, region) and the entry point equals the one XbeTool's binary dump uses; Refresh reads it again; All header info, Certificate, Sections and Libraries put XbeTool's own listing into the log line for line; Dump to binary, Dump to EXE and Dump IDA .idc write files identical to XbeTool's; Open folder shows the XBE; the XBE itself was left unchanged."),
            P("LN",   "UiNotify",        true,  "live on the Trinity RGH3 through the real window, 2026-11-18 night (JRPC2 and XRPC both loaded): Send showed \"Hello from X360GameHack\" with the chosen picture, caught on the console's own screen over XBDM; Show every picture showed one pop-up after another (Picture 0: Xbox logo, Picture 1 ...), each caught the same way, and Stop ended it; the console kept answering throughout."),
            P("PK",   "UiPkg",           false, "2026-11-18 night, release build: every X360Packages button pressed in the real window on copies of a real DLC package (LIVE, metadata version 2), each result compared with X360PkgTool run directly on an identical copy. The Package card shows the IDs, versions, names, install path and signature type X360PkgTool -p reads (also after each edit); Full info is X360PkgTool's own printout line for line; Refresh reads it again; every Quick action, Creator IDs Set, Apply flags (every flag both ways, PIRS and LIVE signing) and Custom command Run give exactly X360PkgTool's file; Metadata > Apply (the program's own writer) gives exactly X360PkgTool -st / -sn / -sd / -sp's file, all twelve language slots included, apart from the LIVE signature, which only Microsoft can make (X360PkgTool writes one with its own key; RGH / JTAG and Xenia do not check it); Set picture and Set game picture put the same 64x64 PNG at the same bytes as X360PkgTool -i and write its exact length as the size, as all 6 console-made saves and profiles do (X360PkgTool writes a fixed D03D0000 there); Install to USB / console... hands the package to Console Setup; Open folder shows it; Save as default keeps the command. Only for a DLC given a profile ID does the install path differ: the program keeps DLC under 0000000000000000, where the test console keeps all 93 of its content packages, and X360PkgTool names the profile. CON signing needs key00033.bin / key00036.bin next to X360PkgTool, which the program never ships, so that choice stays BETA."),
            P("SW",   "UiSwap",          false, "2026-11-19, release build, in the real window on a disc image of a real game (SpongeBob, built by extract-xiso from the game folder): Swap files from a folder... put two changed files (one in the root, one in Media\\EN_US) into a new image; extract-xiso lists both at their new sizes and takes them out exactly as they went in, all 147 other files come out unchanged, and the new image runs in the bundled Xenia."),
            P("CL",   "UiContents",      false, "2026-11-19, release build, in the real window on copies of a real LIVE package (Minecraft avatar awards) and a console profile: on X360Packages and Profiles, Extract... writes each of the 10 and 66 files exactly as Extract all... does, every block checked against the package's own hashes; Verify integrity calls both real files intact and catches one changed byte in a copy; Profiles Open folder shows the profile and Save picture as... writes its own 64x64 tile_64.png byte for byte (Windows opens it as 64x64); GPD viewer > Export pictures... writes all 87, 76 and 67 pictures of three real game databases byte for byte against an XDBF reader written for the test, named by their IDs (this run found and fixed a space at the start of every exported name); XEX Tool > Bad Update project page opens https://github.com/grimdoomer/Xbox360BadUpdate, which answers 200 and is the Bad Update project."),
            P("SE",   "UiStfsEdit",      false, "2026-11-19, release build, in the real window: on a copy of a real arcade package (Terraria, PIRS) X360Packages > Contents Replace... put in default.xex with a new Media ID, Add file... added a 300 KB file and Delete took it out again. After each one the bundled Xenia's own package reader started the game from the package (its log shows the replaced XEX's Media ID 1A2B3C4D, the game's code is in memory), and an STFS reader written for the test from the format found every file byte for byte (the others unchanged), every block's SHA-1, the hash tables up to the top hash and the header hash valid; a .bak was kept. Saves > Names > Apply names on a copy of a console save: X360PkgTool -p reads the new display and game names, only the 12 display-name language slots (as X360PkgTool -sn fills them), the game name and the header hash changed, and every hash is valid (its console signature needs the keyvault to be renewed; RGH / JTAG load it as it is)."),
            P("ST",   "UiSettings2",     false, "2026-11-19, release build, in the real window, three starts sharing one settings file, against a loopback stand-in for the update address (signed with a test key): while the update prompt is dismissed the start does not show it; switching on Show the update prompt again writes that to the settings file, the next start shows the prompt once and the start after that does not. Back up files (.bak) before patching: off, Apply names changes a save and makes no .bak; on, it keeps the save as it was in a .bak byte for byte. Run tools in a visible console window: on, Full info runs X360PkgTool in a visible cmd window that holds its printout (not the log), and the tool file cannot be changed while that window is open (this run hardened that: the check now holds until the window closes); off, the printout is in the log again. Show the copy-games tip again: while marked shown a game-sized copy to the hard drive shows nothing; switched on, the next one shows the tip, once. Default tool commands: Enter saves each one, and the custom-command boxes on XEX Tool, Xbox ISOs and X360Packages start with them at the next start."),
            P("MO",   "UiMods2",         false, "2026-11-19, release build, in the real window, against the real licence server code running on this PC (its own test database) and Terraria running in the bundled Xenia, no console connected: Mod Maker's Build .x360mod... built a protected game mod (the payload cannot be read from the file) and its key card; the server registered the card and issued a token; Refresh listed the mod locked; Unlock my mods unlocked it through the server and remembered the token; Turn on wrote the option into the running game (Xenia's memory read directly: 00000000 -> MOD!), using Xenia by itself because no console was connected, and Peek Poker's target followed; Turn off put the original bytes back; Lock selected locked it and its options went away, Refresh kept it locked, Forget token took the token out of the settings file; Open mods folder showed the mods folder and Copy put this PC's id on the clipboard. This run moved Build's save dialog onto the program's shared picker (it had its own) and made Apply use the Xenia game when no console is connected."),
            P("CP",   "UiCreatePic",     false, "2026-11-19, release build, in the real window on the details of a real console save: Create a save > Set picture... then Create package... puts the chosen image in the new package's header exactly as the package picture writer makes it (held to X360PkgTool -i in PK; 64x64, Windows opens it) and shows it in the card; No picture then Create package... leaves the header without a picture (size 0, area empty). X360PkgTool -p reads both packages and every hash of both is valid."),
            P("UP2",  "UiUpdate2",       false, "2026-11-23, release build, in the real window against a loopback stand-in for the update address (test key): the title bar says Latest Version when the address answers without a notice (nothing newer released; since 2026-11-24, before it said No Release On The Update Server Yet), Update Notice Failed Its Check for a notice without a signature, Update Server Unreachable when it does not answer while the PC is online, Offline only when Windows says the PC has no internet, Update Notice Failed Its Check for a notice signed with another key, Latest Version for the same version. A newer version opens the update window, which shows the SHA-256 the signed notice gives and asks \"Update to 4.9.9 now?\" with Yes and No, downloading nothing before Yes; after the download it shows the downloaded file's SHA-256 (equal to the signed one) and every file with its own SHA-256 as added, replaced or unchanged, and asks \"Install 4.9.9 now?\" (saying what it replaces and adds) with Yes and No; that Yes (asking first when a file is open) closed the program, the helper checked the file again and replaced the portable copy's exe, added the new file and started the program again. With Ask me to install new versions off no window asks; Skip this version is remembered and Check for updates now offers it again; Open the GitHub page opens the releases page. Re-run 2026-11-24 for the Yes / No window (all modes, real portable install); the question and both answers were also checked to be fully on screen at 4K 200 %, at the room of 1366 x 768 and 1080p 150 % screens, and on a window far too short (the top then scrolls)."),
            P("MS",   "UiMods3",         false, "2026-11-23, release build, in the real window against the licence Worker running locally (wrangler dev, test database): with no mod file on the PC, Unlock my mods fetched the licence's mod file from the server (the only place mod files come from), kept it only because its size and SHA-256 matched the server's list, and unlocked it; an up-to-date file is not fetched twice and a damaged copy is fetched again. Against the same server an attack battery (lic_attack.py, 32/0): no route can make a token or show a key (admin-like paths are 404), junk / made-up / SQL-injection tokens are refused after a pause, the bound machine gets its list, file and keys, and the same token from another machine is refused on every route and locks itself after 10 tries."),
            P("GH",   "UiGhidra",        false, "2026-11-19, release build, in the real window on a copy of a real game XEX (Black Ops): To Ghidra... in the raw view and in the memory-address view writes the same five files (the program exactly as decoded, the map with every section, function start, import and export, the script, the batch file and the readme), refuses to overwrite an earlier export and leaves the XEX unchanged. Ghidra 12.0.4 then built the project from that folder with the batch file alone: it applied the map (16 sections, 14,704 functions, 544 imports), ran its full analysis, saved the project and the batch file said Done; a second run says the project is already built. Opened again read-only in Ghidra, the project has the PowerPC language, every section with its read/write/execute flags, the entry point and named system calls at the addresses the map gives (XamInputGetState, NtCreateFile ...)."),
            P("X3",   "UiXenia3",       false, "2026-11-19, release build, in the real window on a copy of a real game XEX (SpongeBob): Set (Title ID) and Set (Media ID) wrote 54510851 / 1A2B3C4D, and the bundled Xenia then loaded that XEX, reported exactly those IDs and held its code in memory at 0x82000000."),
            P("MI",   "UiMisc",         false, "2026-11-19, release build, in the real window on copies of real console files (a profile, saves, a DLC, a title update): Profiles - Open folder shows the profile, Install to USB / console... hands it to Console Setup going where X360PkgTool says, Save picture as... writes the profile's own tile_64.png byte for byte; on Profiles and X360Packages Extract... writes every file exactly as Extract all... does, Verify integrity calls the real file intact and catches one changed byte, From a save or profile... takes the IDs exactly as the chosen header holds them; GPD viewer > Export pictures... writes every picture of a real game database (31) byte for byte; Console Setup > Install content: Add files lists a save, a profile, a DLC and a title update each going to the folder X360PkgTool names, Add folder adds every package of a folder and leaves other files out, Remove and Clear; Create a save: Copy details from a package... fills type, title ID, names and IDs exactly as X360PkgTool and the header give them, From a save or profile..., Add files, Remove and Clear."),
            P("TL",   "UiTitle",        false, "2026-11-19, release build, in the real window against the public title database (dbox.tools): Look up with a Title ID (415608FC) gives the name the game's own XEX carries (Call of Duty: Ghosts) and keeps it on the PC; with part of a name (\"ghosts\") it lists the Xbox 360 games whose name holds it, 415608FC among them; an unknown Title ID says nobody knows it; double-clicking a result adds that game to the open profile."),
            P("FL",   "UiFlash",        false, "2026-11-19, release build, in the real window run from a full copy of the program folder: J-Runner started the bundled JRunner.exe (its window \"J-Runner with Extras\" opened; it was closed again); Tools & drivers folder and Firmware folder open the program's own common and firmware folders, which hold the bundled files; Readme and Tutorial (PDF) open the bundled JungleFlasher.txt and Documentation\\jftut1.4.5.pdf; XeLL's Open in browser opens http:// and the address typed."),
            P("HX",   "UiHex",          false, "2026-11-19, release build, in the real window on a copy of a real game XEX (SpongeBob): Save copy... in the raw view writes the XEX byte for byte, and in the memory-address view writes the decoded base file exactly as xextool -b does; Find next goes to the first and then the second place a 4-byte pattern is (counted independently); Replace all asks about exactly those places; Reload asks, then drops unsaved replacements; Save writes exactly the replacements and nothing else after keeping a .bak, and xextool still reads the XEX."),
            P("TU",   "UiTu",           false, "2026-11-19, release build, in the real window on copies of a real game and its real title update read from the test console (Call of Duty Ghosts default.xex v0.0.0.5 and tu00000005_00000000): the game card shows the Title ID, Media ID and version xextool reads; Copy Title ID / Copy Media ID put xextool's values on the clipboard; Open XboxUnity opens xboxunity.net; the update card shows the Title ID, Media ID and install folder X360PkgTool reads, and what it updates (0.0.0.5 to 0.0.17.5); the check says it fits - and xextool really applies the update's own default.xexp to that game; More places... hands the update to Console Setup; Apply .xexp to the open XEX gives exactly the file xextool -p gives (v0.0.17.5), and a second press says it is merged in already."),
            P("INI2", "UiIni2",         false, "2026-11-19, release build, in the real window on the console's own launch.ini, xbdm.ini and JRPC.ini (copies read from it): xbdm.ini shows its three XBDM commands (dbgname Jtag, setcolor nosidecar, dumpmode smart) and JRPC.ini its blocks (KV Stealer Protection True, plugin1-3); each file loads and stays byte for byte the same; Change value rewrites only its own line in the file's own form (name=\"...\", bare words, quoted pairs, True / False spelling kept) and refuses values a command does not take or a quote, saying why; Add setting adds a command as a new line, a pair at the end of its block, or for launch.ini a setting written as section.name = value; Add plugin: XBDM fills the first free plugin slot and a second press says it is there; Undo all changes gives back the original byte for byte; Use this text takes typed text; Save as writes exactly the edited file. Every change was compared with the exact text it must give."),
            P("LI",   "UiIniLive",       true,  "live on the Trinity RGH3, 2026-11-19, read only, one connection: Discover on the INI Editor tab found the console; Load from console for xbdm.ini and JRPC.ini gave exactly the files the console holds, with their settings listed."),
            P("LR",   "UiLights",       true,  "live on the Trinity RGH3 through the real window, 2026-11-19 00:19-00:28, one connection, JRPC2 loaded, with the user watching the console: All green, All red, All orange and All off each lit the whole ring that way; Apply with four different choices lit top left green, top right red, bottom left orange, bottom right off; Spin, Alternate and Flash each ran and Stop animation stopped each one; Apply set it back to top left green. The user confirmed every step by eye, and the console kept answering throughout."),
            P("SV",   "UiSaves",        false, "2026-11-19, release build: the Saves page in the real window on copies of three real console-made saves and their profiles. The Save game card shows what X360PkgTool -p and the header itself say (IDs, versions, names, install path, signature type); Refresh reads it again; both Full info buttons put X360PkgTool's own printout in the log line for line; Open folder shows the save; Install save... hands it to Console Setup going to its own Content\\<profile>\\<game>\\00000001; Extract... writes every file exactly as Extract all... does, each block checked against the save's own hashes; Verify integrity calls the console's save intact and validly signed and catches a copy with one byte changed in a data block; From a save or profile... takes the profile and device IDs exactly as the chosen file's header holds them (three profiles, two saves); An Xbox hard drive... opens Drive Explorer's Saves tab. Browse saves on a folder laid out like a console drive: A folder on this PC... and Find saves list every save with its game, save name, title ID and profile (gamertag read from the profile beside it); Open in save editor opens a byte-for-byte copy kept in the Saves folder; Download... copies the saves out byte for byte; Replace with... puts the chosen file in place byte for byte after backing the old one up; Delete backs the save up byte for byte, then removes it. The originals were never touched."),
            P("XC2",  "UiXc2",           false, "2026-11-18 night, release build, with the bundled Xenia: Add a folder put two console profiles and a console save into Xenia unpacked byte for byte; Open Xenia's content folder opened it; Find profiles listed both with their gamertags; Download copied a profile folder out file for file; Open in profile / save editor explained Xenia's folder form and opened the folder; a gamertag changed with Change gamertag on a copy of a console profile and put into Xenia with Replace with... was shown by Xenia itself (its log: Loaded X360GH Edit ... to slot 0); Delete backed a profile up and removed it, and Xenia then found one profile; Find saves listed the save with its game; Download copied it out; Replace with... put another console save in its place byte for byte; Delete backed it up and removed it."),
            P("PC1",  "UiSettings",      false, "2026-11-18 night, release build, in the real window with its own settings folder: every Home tile opens its page (Bulk extract ISOs on its Bulk extract tab) and the Console card tells the truth; Settings: Apply switches the whole window to the theme; New theme, Generate shades, Save, Edit, Save & apply, Duplicate, Cancel, Export / Import (every color the same) and Delete; Keep a log file writes logs.txt and stops again; the four Browse... buttons set folders and programs the program then uses (a backup landed in the chosen saves folder); Check now lists every bundled tool as verified and catches one changed by a single byte; Open tools folder, Open settings folder and Open log file open the right places; Forget saved FTP passwords, Forget keyvault, Clear recent files; Export settings without any password (not even encrypted) or the keyvault location, Import settings, Reset all settings (FTP profiles and recent files kept); SHA3-256 the same as Python's hashlib; the About links open their pages and each answers."),
            P("PC2",  "UiStartup",       true,  "2026-11-18 night: started the way the program starts itself, with the Settings startup choices on: it opened on the page used last, with the activity log open, ran the update check (the title showed what it found) and connected to the console IP by itself (the real console)."),
            P("SI",   "UiSignIn",        false, "2026-11-18 night, release build, with the bundled Xenia: with nobody signed in, Run working file asked first; a gamertag Xenia would refuse was refused with Xenia's own rule; Create and start made a profile whose Account file is byte for byte what Xenia's own EncryptAccountFile writes, and Xenia found it and signed in to it (its log); once signed in nothing was asked; Sign in and start signed Xenia in to the profile picked from the list; Start without signing in started the game with nobody signed in; Cancel started nothing; with no Xenia settings file yet, the sign-in was written and Xenia kept it. Every start of a game in Xenia goes through this step. The user then used it in their own copy of the program (2026-11-18) and confirmed it works."),
            P("LL",   "UiLaunch",        true,  "live on the Trinity RGH3 through the real window, 2026-11-18 night: Launch XEX asked first, then the console ran the chosen XEX - SpongeBob from Hdd:\\RGH Games (three times, from the dashboard and from Aurora) and Aurora from Hdd:\\RGH Apps - proven by the title ID and the path of the running XEX, read over XBDM."),
            P("LP",   "UiPower",         true,  "live on the Trinity RGH3 through the real window, 2026-11-18 night: Reboot (cold) asked first, then the console rebooted - the running game was gone and the dashboard answered again about 45 seconds later (the user saw it reboot); Restart title (warm) started the running game again from its file - a mark written into its memory beforehand was gone and the same game ran again."),
            P("LDB2", "UiLiveDbg",       true,  "live on the Trinity RGH3 through the real window, 2026-11-18 night, with a game running (SpongeBob): Discover; the thread list matched the console's own (31 threads, start addresses) and Debug this thread picked one; the module list matched every loaded module, a module's sections matched, Dump module to file saved exactly the module's memory; Go showed code word for word as the console's memory holds it, Follow went to a branch's destination; Break held a thread and PC went back to it; Step into on a thread parked in the kernel asked first, and Cancel left it alone; Add put a breakpoint on the game's VdSwap call stub and the game ran into it; Step into and Step over then ran one game instruction each; Run to return let the system function run and stopped back in the game exactly at the return address; Run to selected stopped exactly on the chosen line; Trace recorded 5 instructions, all in the game; Suspend and Resume moved a game thread's suspend count up by one and back; Continue let the game run on - the console answered throughout."),
            P("LF2",  "UiLive (ftp)",    true,  "live on the Trinity RGH3 through the real window against Aurora's FTP server, 2026-11-18 night: Help, Save (a connection profile), New folder, Upload files and Upload folder (read back over XBDM byte for byte - the console's two servers agree), Download (a file and a folder, byte for byte), Rename, Send game folder (into the folder asked for) and Delete (a folder with everything in it) - in a test folder only, removed afterwards."),
            P("LPK",  "UiLive (peek)",   true,  "live on the Trinity RGH3 through the real window, 2026-11-18 night: Peek, Poke changes, Dump to file, and every value type written and read back, in the running program's memory (its unused DOS-stub text) - each checked against the console's memory read separately; the original bytes put back exactly."),
            P("GP",   "GodParity",       false, "2026-11-24, against the tools themselves: ISO2God 1.3.6's own conversion code (loaded from ISO2God.exe and called directly) and god2iso 1.0.5 (its published conversion, with its XSF template). Microsoft's own Crysis package (26 data files, 4.4 GB) -> disc image identical to god2iso's except the tool name at offset 31337 (4,370,477,056 bytes, 137 folder entries moved 4,124 sectors), the same 110 files as the every-sector-in-place image; that image -> packages with None, Partial and Full padding removal: every data file byte for byte ISO2God's, the Full rebuilt image byte for byte ISO2God's (4,370,481,152 bytes), both packages back to the original files. The same on the Crackdown 2 test image, its XGD2 layout and an original Xbox image around NFSU2's real XBE (its picture decoded like ISO2God's, within one colour step); the details read from each game (title / media ID, disc, platform, type, picture bytes, name) as ISO2God reads them. Headers differ only where they follow Microsoft's own packages (version fields, license table, transfer flags, no ISO2God description) and where ISO2God leaves a one-file package's root hash empty (a bug)."),
            P("GT",   "GodTargetTests",  false, "2026-11-24: packages made for RGH and for a devkit from a disc image, from a game folder (XEX version) and from an original Xbox folder (90 checks): every XEX in the package byte for byte what xextool -r mrbdiyvklc -m r / -m d (or -m d alone) makes of the original, every other file the original's, the header byte for byte what X360PkgTool -z -b 1 -m 1 / -m 0 makes of the same package run directly (devkit form signed), every hash checked."),
            P("UG",   "UiGod",           false, "2026-11-24, in the real window on copies: ISO → GOD - options set in the controls, Add images (the Crackdown 2 test image) and Add game folder (NFSU2's original Xbox folder) read name / title ID / disc from the games, Edit changed the name, Convert all made both for RGH with the XEX and the package unlocked (checked as in GT), When a package is done > USB drive copied it into <USB>\\Content\\0000000000000000\\<title ID>\\00007000 byte for byte; GOD → ISO - Add packages, Convert all wrote \"<name>.iso\" with the game's files; This image - Convert to Games on Demand made a devkit package next to the image in one click; the options were saved (31 checks)."),
            P("AB",   "AbgxProof",       false, "2026-11-24, abgx360 built in, against the installed Hadzz abgx360 1.0.7 and the abgx360 GUI's own source (651 checks): the installed abgx360.exe is byte for byte the one in the bundled Hadzz installer; every option the front end can put on a command line is one abgx360 1.0.7 itself reads (its own 86 option names); everything \"abgx360 --help\" lists, and everything the abgx360 GUI's command builder can emit, can be produced (the GUI's --splitvid / --removesplitvid / --e-videopartition, which 1.0.7 no longer reads, left out); file names and patterns always quoted (abgx360's MinGW start-up expands * otherwise - shown); the fix file written into a sandbox APPDATA, and abgx360 --debug shows it takes every address from that file; a fix file pointing at the dead abgx360.net replaced (kept as .bak), one for another server left alone; the installer's 2 video files copied with their CRC-32 checked; the report shown in the program, turned to text, is line for line abgx360's own --stripcolors output in 7 option sets (achievements and avatar awards included); its questions answered through its input (y, n, two in a row); Stop ends it at once; online, through the fix file, abgx360 downloaded abgx360.dat and GameNameLookup.csv from abgx360.hadzz.com."),
            P("CV",   "CliConvertTests", false, "2026-11-24, through the real exe (cli convert, 29 checks): a disc image, a Games on Demand package and a game folder each made into a package, a disc image and a folder - every result's files byte for byte the source's, every package passing its own hash check; for RGH / a devkit every root XEX byte for byte what xextool -r mrbdiyvklc -m r / -m d makes of it and the package unlocked for a devkit by X360PkgTool; an original Xbox image to a folder, to a package (00005000) and back; existing outputs, a result inside its source, stealth= with a folder, unknown formats and options refused with nothing written; the sources never changed."),
            P("UC",   "UiConvertAbgx",   false, "2026-11-24, in the real window on copies (59 checks): Convert tab - an image, a package and a game folder in one list (From and title ID read), Convert all to disc images (each with the game's files byte for byte; the stealth step says why made-here images are left as they are), to XEX version folders for RGH (default.xex exactly xextool's unlock), to Games on Demand after Edit renamed the game (package in Content\\0000000000000000\\4D5308BC\\00007000 under its ISO2God name, every hash valid, the new name in its header), Clear finished; This image - Convert to Games on Demand switched the tab and made the package next to the image; Browse contents of a package (its disc's root) and of an image (type, sector, byte offset, attributes), Find over the whole image, Properties (title ID, media ID, allowed media), Extract everything byte for byte; abgx360 tab - Verify only with the report shown here in abgx360's colours, Save the report as abgx360's HTML page and as text, Open it in the browser (a script-free copy), Set up abgx360 (fix file and video files in a sandbox APPDATA), a folder with Only files like *.xex; the settings remembered; the originals never changed."),
            P("NT",   "NandTests",       false, "2026-11-24, against J-Runner itself (its own reader in JRunner.exe, loaded and called directly) on all 71 flash dumps on this PC - Falcon, Jasper small block and big block, Trinity, Corona 4 GB; originals, glitch images, RGH 1.2 and an RGH3 Corona (8,084 checks, 0 failed): the board J-Runner names, the data without spare bytes, every boot stage and patch slot, the built-in 1BL key is J-Runner's own, every stage decrypted byte for byte the same as J-Runner's decrypt_CB / _CD / _CE / _CF / _CG (and with the CPU key where the console pairs them), the lockdown value and pairing data of the CB and of each patch slot, the SMC and its version, J-Runner's SMC test (its verdict shown only for Xenon to Jasper SMCs, where it tells every glitch image from every original), the SMC config field by field with its checksum, the keyvault as stored, the CPU key check, every keyvault field and the decrypted keyvault, the console ID as the console shows it, fcrt.bin decrypted (27 dumps), the file-system root and every file's bytes (fragmented files by their own XEX block hashes, which J-Runner reads wrongly), the bad blocks."),
            P("JX",   "NandExtractsCheck", false, "2026-11-24: the Extract files output J-Runner itself wrote on this PC (its Extracts-<serial> folders): 33 files - SMC_en / SMC_dec, KV_en / KV_dec, smc_config.bin, fcrt_en / fcrt_dec - are byte for byte what this page saves from the dumps beside them (8 more came from dumps no longer on the PC)."),
            P("NW",   "NandWriteTests",  false, "2026-11-24, against J-Runner's own reader on all 62 flash dumps (16 MB small block and big-block Jasper; 1,460 checks, 0 failed): replacing bootanim.xex, or the keyvault, with its own bytes gives a byte-for-byte identical dump; a smaller and a same-size-changed bootanim, and a changed keyvault, read back correctly through our reader - every other file byte for byte unchanged, every page's EDC valid (no new errors), the boot chain / SMC / keyvault untouched, and only the file's own blocks (or the keyvault's block) and the root different from the original - and through J-Runner's own reader where its reader is a reference (same root, unfragmented); a bootanim that needs more blocks is refused. Not flashed to a real console yet."),
            P("UN",   "UiNand",          false, "2026-11-24, in the real window on copies of five real dumps (a Falcon and a Jasper big block with cpukey.txt beside them, a Corona 4 GB, a Trinity with fcrt.bin and its CPU key, an RGH3 Corona 4 GB; 215 checks): recognised by content (also the RGH3 image whose header text is not Microsoft's); board, notes, boot chain, how each stage decrypted, lockdown values, SMC, SMC config and checksum shown as read; every Save button (as stored and decrypted) wrote exactly the engine's bytes (which NT proves against J-Runner), and a stage that needs the CPU key is refused with a message when it is not there; the keyvault unlocked by itself from cpukey.txt, a wrong key refused, the right one typed in unlocked it again, and unlocking decrypted the console-paired stages; Extract selected / all wrote each file exactly; an older root lists its own files; Open in X360GameHack opened a flash XEX; the dumps were never changed and nothing of the keyvault reached the activity log."),
            P("XG",   "Xenia (Crysis)",  false, "2026-11-24: Crysis from Microsoft's package -> disc image -> Games on Demand with the command line (Full, for RGH: XEX unlocked by xextool, package by X360PkgTool) started in the bundled Xenia Canary: the package mounted (26 data files), the unlocked default.xex loaded and the game ran to its menu - the user saw it. Loading the campaign stopped in Xenia exactly the same way with Microsoft's untouched package (same file reads, \"Stub XFileSectorInformation\") - an emulator limit, not the package."),
            P("XS",   "XeniaSaveTests",  false, "2026-11-25 (again, after the header fix), debug build, against a scratch Xenia folder on copies of real console saves and title updates, read back with the rules of the BUNDLED Xenia re-written from its source at that very build (02d2cb5cc: ReadContentHeaderFile = XCONTENT_AGGREGATE_DATA, ListContent = folders only, ResolvePackagePath, FindTitleUpdate): Install a save puts each of 4 saves from 4 consoles into content\\{profile}\\{title}\\00000001\\{name} for the chosen profile, every file byte for byte, with a 0x14C-byte header file (device 1, type, the save's own display name, its file name, the chosen profile, title, licence mask) - the game lists it for that profile and Xenia opens that very folder; control: a header in a newer Xenia's form (what earlier versions wrote) gives this Xenia a garbage file name. Same save again = nothing changes; another save of that name = backed up first, then replaced (files compared, not only the header). Title updates (Halo 3 #3, WaW #7) and DLC go in unpacked into folders where FindTitleUpdate / ListContent find them. Profiles, junk and no profile refused. Archives: names that try to leave the folder and a file bigger than it says refused; .7z through 7-Zip; a password-locked archive fails at once. The games list finds the user's copied games. 93 checks."),
            P("UX",   "UiXeniaPage",     false, "2026-11-25, debug build, in the real window at the user's size (1145 x 886 at 200%), scratch settings and Xenia: the sidebar's Xenia page with Games / Saves / Profiles / Xenia Patches / Setup; the games list (3 games, titles, kinds, sizes); Install a save... for a selected game from a downloaded-style .zip (its save went in for the signed-in profile, another game's save in the same .zip was left out and named); Install a save... on the Saves tab; Add a game... and Remove... (taken off the list, file kept; on a copy it asks first - answered No, nothing deleted); Create a profile... (Xenia's way) and Sign in to selected; Play reached Xenia's start (stopped there: Xenia was not started); the xemu card on the Original Xbox page; BETA marks as listed here. 35 checks."),
            P("CX",   "UiCopyToXenia",   false, "2026-11-25 (again, after the header fix), debug build, in the real window on the user's own Xbox drive image (250 GB, opened read only): Drive Explorer > Content\\0000000000000000\\4156081C > the DLC and title-update folders > Copy to Xenia put WaW's 2 map packs into a scratch Xenia's content\\0000000000000000\\4156081C\\00000002 and its title update (\"Title Update #7\") into ...\\000B0000, each unpacked into a folder the way the bundled Xenia keeps it (every file byte for byte the package's, a 0x14C-byte header naming it, owner 0); a dashboard \"Title Content Progress cache\" that sat in the DLC folder was left out with a message naming it; the temporary copy was removed; the image file was not changed. 10 checks."),
            P("IP",   "UiIdPicker",      false, "2026-11-25, debug build, in the real window on copies of real console files and the user's own Xbox drive image (read only): Rehash & resign > Pick the IDs looks by itself when the tab opens and lists the profile kept in the Saves folder, a console profile copied into Xenia (its header beside it) and every profile on the drive, each with exactly the profile, device and console ID its header holds (read independently: own FATX listing and header offsets); the drive's device ID is the one most of its console-signed files carry (23 of 26) and is the drive's own serial number from its security sector; Use these IDs fills Rehash & resign and names the IDs that differ; Rehash & resign then writes exactly those IDs and every hash verifies (the original kept as .bak); A USB stick or folder... adds a folder laid out like a console drive (its profile joins the Xenia row, its drive row has the majority device ID, 2 of 4) and explains an Xbox-formatted USB stick; a drive row gives only its device ID; with a keyvault in use the text says one press makes the save valid on any console. Profiles and X360Packages have the same card (28 checks). No console was connected during the test: a console's hard drive and USB sticks are read the way Neighborhood > Profiles reads them, which was tested on the console."),
            P("XK",   "XeniaConfigTests + XeniaAccept", false, "2026-11-25, debug build, on copies of the settings file the bundled Xenia Canary wrote: all 265 settings read with Xenia's own description and choices; a change rewrites only that value (every other line byte for byte the same, comments in their column, UTF-8 / LF kept); texts escaped and read back exactly, decimals always with a point; a missing setting or section added where it belongs; a missing file created; the original kept once and put back byte for byte; wrong values refused (30 checks). Then the bundled Xenia itself, started without a game on a scratch storage folder and closed normally: its own config dump showed all 13 changed values loaded (2x scale, VSync off, 120 fps, 16x filtering, fast readback, FSR, FXAA, FSR 0.5, CAS 1.0, dead zone 0.15, full licence, German) and it wrote them back; from a file holding only 6 values it wrote all 265 settings and kept the 6 (6 checks)."),
            P("XU",   "UiXeniaSettings", false, "2026-11-25, debug build, in the real window on a scratch Xenia whose settings file is a copy of the bundled Xenia's: Games > Graphics settings... opens the Settings tab; every card shows what the file holds (a value written 0.20000000298023224 shows as 0.2; one set by hand shows as itself); resolution scale, upscaling filter, FSR sharpness, anti-aliasing, texture filtering, frame rate, VSync, render targets, dead zone, licence and language each land in the file exactly as Xenia writes them; Every Xenia setting lists all 265, searches, shows Xenia's description, sets a number, a true / false, a text and offers exactly Xenia's choices, and refuses a wrong value; only the changed lines differ (550 others byte for byte); Put back my old settings... makes the file byte for byte what it was; with no settings file yet it says so and makes one; the quick-setup buttons set 4K (3x) / Sharper (2x) - scale, FSR, FXAA, 16x filtering - and Xenia's defaults put every graphics choice back, nothing else touched; a value written the same (0.2 for 0.20000000298023224) is left as it is; while Xenia runs a change is refused with the reason and the file is not touched - the bundled Xenia writes its settings file when it closes (measured: a value changed while it ran was back to the old one after it closed) (33 checks). No game has been played with changed settings yet."),
            P("XR",   "XeniaRepairTry",  false, "2026-11-25, debug build, on a scratch copy of what earlier versions left in the user's Xenia (WaW's save with a header in a newer Xenia's form, Title Update #7 copied as the package file): XeniaContent.Repair - run before every Play - rewrote the header in the bundled Xenia's form (device 1, type 1, file savegame.svg, the profile, title 4156081C) after copying the old one to Saves\\Backups byte for byte, left the save's data untouched, unpacked the title update into a folder with its header (type 000B0000, owner 0) after moving the package file to Saves\\Backups byte for byte, and found nothing to do a second time; a failed unpack puts the package file back (seen when a library was missing). 8 checks."),
            P("XL",   "the bundled Xenia", false, "2026-11-25: the bundled Xenia Canary (02d2cb5cc) itself, started on scratch folders with the user's WaW (PXv2 default.xex), the user's Xenia profile and content put in by this program, its log at debug level: Title Update #7 unpacked by this program was found and applied (\"XEX patch applied successfully: base version: 0.0.0.8, new version: 0.0.7.8\"; left as the package file it was never used); the game listed both map packs (\"Adding: Call of Duty: WaW - Map Pack 2 / 3\") and mounted them (mappack1:, mappack2:); and it opened the save (XamContentCreateEx save, NtCreateFile save:\\savegame.svg). The map packs in the user's own Xenia are byte for byte the packages' files (1,530 and 3,322). No one has played with them yet."),
            P("BT",   "UiBetaTips",      false, "2026-11-25, debug build, every page and every tab of the real window: each of the 159 controls whose tip speaks of BETA shows a BETA label (itself, its card, its tab or its page); on every switch and drop-down with unproven choices (18 choices) the tip speaks of BETA exactly while an unproven choice is selected, and names it."),
            P("XE",   "UiXeniaExtras",   false, "2026-11-25, debug build, in the real window with the user's own Xbox drive image open (read only) in Drive Explorer and his WaW on a scratch Xenia's games list: with no drive open it says where to look; one press of DLC / updates from my Xbox put WaW's newest title update (#7) and both map packs into Xenia unpacked the way it keeps them (all 4,859 files byte for byte the drive's, headers), left the dashboard's file out, moved an older title update that was there to Saves\\Backups (Xenia applies the first one it finds) and said so; a second press said Xenia has them already; the temporary copy was removed; the image was not changed (14 checks). From a connected console it reads the same folders; not run with a console."),
            P("SP",   "UiSaveWithProfile", false, "2026-11-25, debug build, in the real window on a copy of the user's own download (a WaW save and the profile it belongs to, laid out as on a USB stick) into scratch Xenias: Install saves from a folder... saw the profile and asked; \"keep them with that profile\" put the profile into Xenia in its own form, the save under it (header owner = that profile) and made Xenia sign in to it, saying so and how to switch back; \"give them to one of my profiles\" put the save under the user's profile and left the other one out, saying so; picking just the save file (Install a save...) found the profile beside it in the download the same way (13 checks)."),
            P("OB",   "OgBigDriveTests", false, "2026-11-25, debug build: big original Xbox drives, on sparse images exactly the size of the user's 1 TB WDC WD10EZRX (1,953,525,168 sectors), each partition formatted the original Xbox way with a file in it: (A) no table, LBA48 standard - F: ends at sector 0x0FFFFFFF (120 GiB, 16 KB clusters) and G: is the rest (803 GiB, 64 KB clusters); (B) XBpartitioner's \"****PARTINFO****\" table at sector 0 with its own F: (400 GiB, 32 KB) and G: (524 GiB, 64 KB); (C) F: takes the whole rest (924 GiB, 64 KB) - every partition found where it is, at its size and cluster size, every file read back byte for byte, no partition too many; (D) the user's Xbox 360 drive image still opens exactly as before (24 checks). The FATX writer (70,993), installer (11) and compatibility-partition (12) suites and every page (133) pass on the same build. Not yet tried on the user's real 1 TB drive."),
            P("FMT",  "DriveFormatTests + UiMakeDrive", false, "2026-11-25, debug build, on sparse IMAGE FILES only (never a real drive): Make Xbox drive. Original Xbox, the size of the user's 1 TB WD10EZRX, in all 5 layouts (F: to 137 GB + G:, F: takes all, F: only to 137 GB, F: of a chosen size + G:, stock): every partition where XBpartitioner (opcow/XBpartitioner, read for this) puts it, at its size and cluster size (16 KB doubling every 256 GB), its table at sector 0 byte for byte, headers byte for byte, files written to every partition read back and checked. Xbox 360, the size of the user's Hitachi: every header equal to the user's real drive's (volume id aside, \"XLID\" included), every partition's data starting exactly where the real drive's does, nothing written in front of SysExt. Conversions both ways leave nothing of the old layout to mistake the drive for. Through the Disk Reader's format mode over a real pipe: a blank drive made; writes to the 360's security sector, caches and (anything but zeros) sector 0 refused, the original Xbox's 4-512 KB refused; a used 360 drive formatted and UNDONE back to every byte; a session journaled the old way still undone (79 checks). In the real window (hidden): new 1 TB and 250 GB images made and opened, an open image made again with F: of a chosen size, an F: that does not fit refused in the dialog, a real drive not offered by a copy that is not installed (14 checks). Real-world check, 2026-11-25 (read only, the installed build): the user's own 1 TB original Xbox drive, set up on his console, has XBpartitioner's table and the F: takes all layout - for a drive of its exact size the plan gives every partition the same place, size, cluster size and cluster count as on that drive (E: C: Y: Z: 16 KB, F: 924 GB 64 KB, 15,139,782 clusters), and the same table labels; only his X: carries 64 KB clusters (reformatted later by something else - a console's own is 16 KB). Not yet used in a console or emulator; never run on a real drive."),
            P("SEC",  "SecurityHardeningTests + worker-test", false, "2026-11-25, debug build, without administrator rights and touching no disk: the Disk Reader is now a native launcher (what Windows elevates - not .NET, refused without elevation by Windows, named in the prompt) that starts the .NET helper with ONLY 7 clean variables (a polluted environment - profiler, AppDomain manager, COMPlus / DOTNET, planted PATH / SystemRoot - never reaches it), passes arguments and exit code through, refuses while a .NET runtime option is set in the registry, and ends the helper when it ends (job object); the helper does nothing unless that launcher started it, refuses any disk with a Windows volume, refuses an undo folder that is a junction or belongs to a user; the program accepts only that helper on its pipe, opens only documents and http(s) links; the console notification listener serves only the console's address (strangers turned away, it gives up after 32) and ends on a line longer than any console sends (34 checks). The Disk Reader's write-mode (25), compatibility-partition + undo on the user's real drive image (12) and devkit (8) suites pass on the same build. The licence and update servers, run locally only (wrangler dev on test copies with test data, nothing deployed), flooded with every database call counted: 40 junk and malformed requests cost no database work at all; 2,000 made-up licence tokens from 2,000 addresses cost one read each and no write; past the per-address limit (an IPv6 /64 counts as one sender) a request costs nothing; a real token stops at 30 a minute from any number of addresses; a token tried on another machine locks itself after 10 tries and then costs no writes; one licence downloads at most its daily number of mod files; the public mod list is answered from memory (119 requests, 0 reads); 406 opens cost one database write and every one was counted; the request log keeps 90 days (31 checks)."),
            P("PT",   "UiPartitions + disk-run", false, "2026-11-25, debug build, in the real window on drive images laid out the ways real drives are: an original Xbox drive with F: up to the 137 GB line and G: the rest shows all seven partitions (X: Y: Z: C: E: F: G:) in the order they sit on the drive, each with its size, file system, cluster size, start and free space, and the map shows every one; clicking a partition (or Enter on it) opens it in Files, and the one shown there is outlined; a partition in the drive's table whose header is gone is listed with why (not formatted) and clicking it explains instead of opening; the stock layout on a bigger drive shows the space past E: (with its table: in no partition; without one: the kernel's F: place, not formatted); an Xbox 360 drive shows both caches (the console's own data, not files), SysExt, SysExt2, the compatibility partition and Data; a single partition dump is just itself (18 checks). On the user's real 1 TB original Xbox drive (installed build, read only, 0 blocks written): the same layout code lists X: Y: Z: C: E: F: (924.06 GB) - every partition of its XBpartitioner table, all read, nothing left over."),
            P("HT",   "UiHomeTiles",     false, "2026-11-25, debug build, in the real window: every Home tile opens its page - Bulk extract ISOs, Neighborhood, Peek Poker, Install to USB / console, Xbox hard drives (Drive Explorer), Original Xbox games (Original Xbox (OG)), FTP, Q & A; the drop box names Xbox drive images and is tall enough for all its lines (also when a narrow window wraps them), and a drive image handed over the way the drop box hands it opens in Drive Explorer with all its partitions on the Partitions tab; in the side menu the red BRICKS POSSIBLE pill sits on Flash Dumps only (15 checks)."),
            P("DU",   "the user, by hand + the drive suites", false, "2026-11-25/26, the user himself in the installed program with his own real drives (original Xbox WDC 1 TB and Xbox 360): opened them, read their files, dragged files out to Windows and copied files onto the drive with changes allowed - his verdict: everything works. Behind it: the drive suites in the real window on drive images (UiDrive, UiDriveFind, UiDriveMenu, UiDragOut, UiPartitions, UiOgSaves, UiOgNames, UiXemuPlay, UiMakeDrive, FreeCountTests) and the write / undo runs on both real drives through the same Disk Reader (DiskRun). Making a drive (formatting) has only ever been run on image files."),
            P("OS",   "UiModsDrive + mod-sign", false, "2026-11-25, debug build: owner-only mods. The owner's signing key was made on the owner's PC (DPAPI, never printed); cli mod-sign signed the 14 real mod packs (+72-byte signature, .bak kept) and says so when a pack is already signed; Node's own crypto on the licence admin side verifies the same signatures (publish / add-pack refuse an unsigned pack). In the real window: the mods folder held the signed WaW pack, an unsigned copy and a signed copy changed by one byte - only the signed one is a mod, the other two are refused and cannot be activated (nothing sent to the server); Mod Maker / mod-build sign what they make; with the key taken away nothing is built and the Mods page has no Mod Maker tab. All mods read the public catalog from the real licence Worker code running locally (wrangler dev, its test database): 14 mods, 176 options, statuses, details, the Get a licence link; the catalog and its saved copy hold names and descriptions only (checked for steps / inputs / ops / ddl / keys / addresses). Drive Explorer's grouped bar (10% of the page): Drive tools folds out and back, the 6 change buttons show only while changes are allowed, all 21 actions present. 24 checks. Not yet against the live server (not deployed)."),
        };

        public static readonly Entry[] Tested =
        {
            // ---- XEX Tool (output byte-identical to xextool; the Title ID / Media ID setter is our own edit -> BETA)
            T("XEX Tool", "Patch", "Executable", "*", "XI"),
            T("XEX Tool", "Patch", "One-click patches", "*", "XI"),
            T("XEX Tool", "Patch", "Custom patch builder", "*", "XI"),
            T("XEX Tool", "Encryption & IDs", "Encryption & compression", "*", "XI"),
            T("XEX Tool", "Encryption & IDs", "Title ID & Media ID", "Set", "XI", "X3"),     // both Set buttons (Title ID and Media ID)
            T("Xbox ISOs", "This image", "Test a mod: swap files inside the image", "Swap files from a folder...", "SW"),
            T("XEX Tool", "Advanced", "Bad Update / Bad Avatar", "Patch this XEX for Bad Update / Bad Avatar", "XI"),
            T("XEX Tool", "Advanced", "Bad Update / Bad Avatar", "Many discs at once", "XI"),
            T("XEX Tool", "Advanced", "Bad Update / Bad Avatar", "Unlock DLC", "XI"),
            T("XEX Tool", "Advanced", "Bad Update / Bad Avatar", "Bad Update project page", "CL"),
            T("XEX Tool", "Advanced", "Custom xextool command", "*", "XI"),
            T("XEX Tool", "Advanced", "Export & extras", "*", "XI"),

            // ---- XEX Disassembler
            T("XEX Disassembler", "", "", "Jump", "D4", "D6"),
            T("XEX Disassembler", "", "", "Find all", "D4"),
            T("XEX Disassembler", "", "", "Disassemble", "D4", "D6"),
            T("XEX Disassembler", "", "", "Decompile", "D6"),
            T("XEX Disassembler", "", "", "Save copy...", "HX"),
            T("XEX Disassembler", "", "", "Find next", "HX"),
            T("XEX Disassembler", "", "", "Replace all", "HX"),
            T("XEX Disassembler", "", "", "Reload", "HX"),
            T("XEX Disassembler", "", "", "Save", "HX"),
            T("XEX Disassembler", "", "", "To Ghidra...", "GH"),

            // ---- Title Updates ("Choose the title update..." is BETA: what it shows after choosing was never checked)
            T("Title Updates", "Check & install", "3.  Does it fit? Install it", "Install on the console", "LT"),
            T("Title Updates", "Merge into the XEX", "Merge the update into the game's XEX", "Extract a title update...", "D6"),
            T("Title Updates", "Merge into the XEX", "Merge the update into the game's XEX", "Apply .xexp to the open XEX", "TU"),
            T("Title Updates", "Check & install", "1.  Your game", "", "TU"),
            T("Title Updates", "Check & install", "1.  Your game", "Choose the game...", "TU"),
            T("Title Updates", "Check & install", "1.  Your game", "Copy Title ID", "TU"),
            T("Title Updates", "Check & install", "1.  Your game", "Copy Media ID", "TU"),
            T("Title Updates", "Check & install", "1.  Your game", "Open XboxUnity", "TU"),
            T("Title Updates", "Check & install", "2.  The title update", "", "TU"),
            T("Title Updates", "Check & install", "2.  The title update", "Choose the title update...", "TU", "LT"),
            T("Title Updates", "Check & install", "3.  Does it fit? Install it", "", "TU"),
            T("Title Updates", "Check & install", "3.  Does it fit? Install it", "More places (FTP, other drives)...", "TU"),

            // ---- X360Packages (the Contents card is the same card as on Saves and Profiles)
            T("X360Packages", "Contents", "Contents", "", "D3"),
            T("X360Packages", "Contents", "Contents", "Extract all...", "D3", "PKG"),
            T("X360Packages", "Contents", "Contents", "View GPD", "D3"),
            T("X360Packages", "Overview", "Package", "", "PK"),
            T("X360Packages", "Overview", "Package", "Refresh", "PK"),
            T("X360Packages", "Overview", "Package", "Full info", "PK"),
            T("X360Packages", "Overview", "Quick actions", "*", "PK"),
            T("X360Packages", "Edit", "Metadata", "Apply", "PK"),
            T("X360Packages", "Edit", "Metadata", "Set picture...", "PK"),
            T("X360Packages", "Edit", "Metadata", "Set game picture...", "PK"),
            T("X360Packages", "Edit", "Creator IDs", "Set", "PK"),                         // all three: Console ID, Profile ID, Device ID
            T("X360Packages", "Edit", "Signing & license flags", "Apply flags", "PK"),
            T("X360Packages", "Advanced", "Custom X360PkgTool command", "Run", "PK"),
            T("X360Packages", "Advanced", "Custom X360PkgTool command", "Save as default", "PK"),

            // ---- Xbox ISOs (Games on Demand packages were never started on a console -> BETA)
            T("Xbox ISOs", "This image", "This disc image", "", "XI"),
            T("Xbox ISOs", "This image", "This disc image", "Extract...", "XI"),
            T("Xbox ISOs", "This image", "This disc image", "Add to bulk list", "XI"),
            T("Xbox ISOs", "This image", "This disc image", "Optimize / rewrite...", "XI"),
            T("Xbox ISOs", "This image", "This disc image", "Play in Xenia", "EM"),
            T("Xbox ISOs", "This image", "Test a mod: swap files inside the image", "Swap the selected file...", "XI", "EM"),
            T("Xbox ISOs", "This image", "Test a mod: swap files inside the image", "Play the new image in Xenia", "EM"),
            // Games on Demand both ways (built in; ISO2God / god2iso parity) - "Launch package" on a console and devkit use stay BETA
            T("Xbox ISOs", "This image", "This disc image", "Convert to Games on Demand", "UC", "UG", "XG"),
            T("Xbox ISOs", "Convert", "Convert games", "", "UC", "CV"),
            T("Xbox ISOs", "Convert", "Convert games", "Add images", "UC"),
            T("Xbox ISOs", "Convert", "Convert games", "Add packages", "UC"),
            T("Xbox ISOs", "Convert", "Convert games", "Add game folders", "UC"),
            T("Xbox ISOs", "Convert", "Convert games", "Edit", "UC"),
            T("Xbox ISOs", "Convert", "Convert games", "Clear finished", "UC"),
            T("Xbox ISOs", "Convert", "Convert games", "Convert all", "UC", "CV", "GP", "GT", "XG"),
            T("Xbox ISOs", "This image", "Browse contents", "*", "XI", "UC"),
            T("Xbox ISOs", "Bulk extract", "*", "*", "XI"),
            T("Xbox ISOs", "Create & convert", "Create XISO from folder", "*", "XI"),
            T("Xbox ISOs", "Create & convert", "Third-party tools", "ISO2GOD", "XI"),
            T("Xbox ISOs", "Create & convert", "Third-party tools", "Xbox Backup Creator", "XI"),
            T("Xbox ISOs", "Create & convert", "Third-party tools", "abgx360 GUI (original)", "XI"),
            T("Xbox ISOs", "abgx360", "abgx360 - verify and fix disc images", "", "UC", "AB"),
            T("Xbox ISOs", "abgx360", "abgx360 - verify and fix disc images", "Verify only (changes nothing)", "UC", "AB"),
            T("Xbox ISOs", "abgx360", "abgx360 - verify and fix disc images", "Save the report...", "UC"),
            T("Xbox ISOs", "abgx360", "abgx360 - verify and fix disc images", "Open it in the browser", "UC"),

            // ---- Saves (edits, resigning and created saves were never loaded by a game -> BETA; Put back was checked on the console)
            T("Saves", "Contents", "Contents", "", "D3"),
            T("Saves", "Contents", "Contents", "Extract all...", "D3", "PKG"),
            T("Saves", "Contents", "Contents", "View GPD", "D3"),
            T("Saves", "Rehash & resign", "Rehash & resign", "Put back", "LC2", "LS"),
            T("Saves", "Rehash & resign", "Rehash & resign", "From a save or profile...", "SV"),
            T("Saves", "Rehash & resign", "Pick the IDs", "", "IP"),
            T("Saves", "Rehash & resign", "Pick the IDs", "Find my profiles", "IP"),
            T("Saves", "Rehash & resign", "Pick the IDs", "A USB stick or folder...", "IP"),
            T("Saves", "Rehash & resign", "Pick the IDs", "Use these IDs", "IP"),
            T("Profiles", "Rehash & resign", "Pick the IDs", "", "IP"),
            T("Profiles", "Rehash & resign", "Pick the IDs", "Find my profiles", "IP"),
            T("Profiles", "Rehash & resign", "Pick the IDs", "A USB stick or folder...", "IP"),
            T("Profiles", "Rehash & resign", "Pick the IDs", "Use these IDs", "IP"),
            T("X360Packages", "Rehash & resign", "Pick the IDs", "", "IP"),
            T("X360Packages", "Rehash & resign", "Pick the IDs", "Find my profiles", "IP"),
            T("X360Packages", "Rehash & resign", "Pick the IDs", "A USB stick or folder...", "IP"),
            T("X360Packages", "Rehash & resign", "Pick the IDs", "Use these IDs", "IP"),
            T("Saves", "Save", "Save game", "", "SV"),
            T("Saves", "Save", "Save game", "Refresh", "SV"),
            T("Saves", "Save", "Save game", "Full info", "SV"),
            T("Saves", "Save", "Getting a save to load", "Install save...", "SV"),
            T("Saves", "Save", "Getting a save to load", "Full info", "SV"),
            T("Saves", "Save", "Getting a save to load", "Open folder", "SV"),
            T("Saves", "Contents", "Contents", "Extract...", "SV"),
            T("Saves", "Contents", "Contents", "Verify integrity", "SV"),
            T("Saves", "Browse saves", "Where to look", "A folder on this PC...", "SV"),
            T("Saves", "Browse saves", "Where to look", "An Xbox hard drive...", "SV"),
            T("Saves", "Browse saves", "", "Find saves", "SV"),
            T("Saves", "Browse saves", "", "Open in save editor", "SV"),
            T("Saves", "Browse saves", "", "Download...", "SV"),
            T("Saves", "Browse saves", "", "Replace with...", "SV", "LV"),
            T("Saves", "Browse saves", "", "Delete", "SV", "LV"),
            T("Saves", "GPD viewer > Achievements", "Achievements", "", "D3"),
            T("Saves", "GPD viewer > Avatar awards", "Avatar awards", "", "D3"),
            T("Saves", "GPD viewer > Settings", "Settings", "", "D3"),
            T("Saves", "GPD viewer > Games", "Games", "", "D3"),
            T("Saves", "GPD viewer > Pictures", "Pictures", "", "D3"),
            T("Saves", "GPD viewer > Strings", "Strings", "", "D3"),
            T("Saves", "GPD viewer > Sync", "Sync", "", "D3"),

            // ---- Profiles (viewing only: no edited profile has been signed in on a console yet -> every edit is BETA)
            T("Profiles", "Overview", "Account", "", "D1"),
            T("Profiles", "Overview", "Games & gamerscore", "", "D1"),
            T("Profiles", "Games", "Achievements", "", "D1"),
            T("Profiles", "Avatar awards", "Games with avatar awards", "", "D2"),
            T("Profiles", "Avatar awards", "Awards", "", "D2"),
            T("Profiles", "Contents", "Contents", "", "D3"),
            T("Profiles", "Contents", "Contents", "Extract all...", "D3"),
            T("Profiles", "Contents", "Contents", "View GPD", "D3"),
            T("Profiles", "Contents", "Contents", "Extract...", "MI", "CL"),
            T("Profiles", "Contents", "Contents", "Verify integrity", "MI", "CL"),
            T("Profiles", "Overview", "Games & gamerscore", "Open folder", "MI", "CL"),
            T("Profiles", "Overview", "Games & gamerscore", "Install to USB / console...", "MI"),
            T("Profiles", "Gamercard", "Gamer picture", "Save picture as...", "MI", "CL"),
            T("Profiles", "Rehash & resign", "Rehash & resign", "From a save or profile...", "MI"),
            T("X360Packages", "Contents", "Contents", "Extract...", "MI", "CL"),
            T("X360Packages", "Contents", "Contents", "Replace...", "SE"),
            T("X360Packages", "Contents", "Contents", "Add file...", "SE"),
            T("X360Packages", "Contents", "Contents", "Delete", "SE"),
            T("Saves", "Save", "Names", "Apply names", "SE"),
            T("X360Packages", "Contents", "Contents", "Verify integrity", "MI", "CL"),
            T("X360Packages", "Rehash & resign", "Rehash & resign", "From a save or profile...", "MI"),
            T("Saves", "GPD viewer > Pictures", "Pictures", "Export pictures...", "MI", "CL"),
            T("Saves", "Create a save", "What it is", "Copy details from a package...", "MI"),
            T("Saves", "Create a save", "Who it belongs to", "From a save or profile...", "MI"),
            T("Saves", "Create a save", "Files", "Add files", "MI"),
            T("Saves", "Create a save", "Files", "Remove", "MI"),
            T("Saves", "Create a save", "Files", "Clear", "MI"),
            T("Saves", "Create a save", "Who it belongs to", "Set picture...", "CP"),
            T("Saves", "Create a save", "Who it belongs to", "No picture", "CP"),
            T("Console Setup", "Install content", "Install content", "Add files", "MI"),
            T("Console Setup", "Install content", "Install content", "Add folder", "MI"),
            T("Console Setup", "Install content", "Install content", "Remove", "MI"),
            T("Console Setup", "Install content", "Install content", "Clear", "MI"),
            T("Profiles", "Overview", "Edit gamertag", "", "XC2"),
            T("Profiles", "Overview", "Edit gamertag", "Change gamertag", "XC2"),          // the edited profile was signed in by Xenia
            T("Profiles", "Games", "Title ID lookup", "", "TL"),
            T("Profiles", "Games", "Title ID lookup", "Look up", "TL"),

            // ---- Drive Explorer: the Partitions tab (read only; checked on the user's real original Xbox drive). Everything else stays
            // BETA for now - writes were only checked by our own engine, and no real Xbox 360 drive reference
            T("Drive Explorer", "Partitions", "", "", "PT"),
            T("Drive Explorer", "*", "*", "*", "DU"),            // the user's call 2026-11-26: "safe to remove all the beta tags on storage"

            // ---- Neighborhood
            T("Neighborhood", "", "", "Connect", "LC", "LT", "LV"),
            T("Neighborhood", "", "", "Disconnect", "LC"),
            T("Neighborhood", "", "", "Discover", "LV"),
            T("Neighborhood", "Files", "", "", "LC", "LV"),
            T("Neighborhood", "Files", "", "Upload files", "LF", "LV"),
            T("Neighborhood", "Files", "", "Upload folder", "LF", "LV"),
            T("Neighborhood", "Files", "", "Download", "LV"),
            T("Neighborhood", "Files", "", "New folder", "LF", "LV"),
            T("Neighborhood", "Files", "", "Rename", "LV"),
            T("Neighborhood", "Files", "", "Delete", "LF"),
            T("Neighborhood", "Files", "", "Play in Xenia", "LX"),
            T("Neighborhood", "Files", "", "Launch XEX", "LL"),
            T("Neighborhood", "Files", "", "Drives", "LV"),
            T("Neighborhood", "Files", "", "Hdd", "LV"),
            T("Neighborhood", "Files", "", "Games", "LV"),
            T("Neighborhood", "Files", "", "Content", "LV"),
            T("Neighborhood", "Files", "", "Usb0", "LV"),
            T("Neighborhood", "Files", "", "Flash", "LV"),
            T("Neighborhood", "Saves", "", "Open in save editor", "LC2"),
            T("Neighborhood", "Saves", "", "Find saves", "LC", "LC2", "LV"),
            T("Neighborhood", "Saves", "", "Download...", "LV"),
            T("Neighborhood", "Saves", "", "Replace with...", "LV"),
            T("Neighborhood", "Saves", "", "Delete", "LV"),
            T("Neighborhood", "Profiles", "", "Find profiles", "FI", "LV"),
            T("Neighborhood", "Profiles", "", "Open in profile editor", "LV"),
            T("Neighborhood", "Profiles", "", "Download...", "LV"),
            T("Neighborhood", "Profiles", "", "Replace with...", "LT"),
            T("Neighborhood", "Profiles", "", "Delete", "LV"),
            T("Neighborhood", "Title updates", "", "Find title updates", "FI", "LT", "LV"),
            T("Neighborhood", "Title updates", "", "Check on the Title Updates page", "LV"),
            T("Neighborhood", "Title updates", "", "Download...", "LV"),
            T("Neighborhood", "Title updates", "", "Install a title update...", "LT"),
            T("Neighborhood", "Title updates", "", "Delete", "LT"),

            // ---- Console (the connection bar is one shared control on every console page)
            T("Console", "", "", "Connect", "LC"),
            T("Console", "", "", "Disconnect", "LC"),
            T("Console", "", "", "Discover", "LV"),
            T("Console", "Console", "Console information", "", "LC"),
            T("Console", "Console", "Console information", "Refresh", "LV"),
            T("Console", "Console", "Power & system", "Reboot (cold)", "LP"),
            T("Console", "Console", "Power & system", "Restart title (warm)", "LP"),
            T("Console", "Console", "Power & system", "Freeze (stop)", "LC"),
            T("Console", "Console", "Power & system", "Resume (go)", "LC"),
            T("Console", "Console", "Power & system", "Launch", "LF"),
            T("Console", "Console", "Power & system", "Open", "LF", "LFTP"),      // Avatar Editor and Xbox Home; the other two shortcuts are labelled
            T("Console", "Screenshot", "Screenshot", "Capture", "LC", "LV"),
            T("Console", "Screenshot", "Screenshot", "Save as PNG...", "LV"),
            T("Console", "Modules & commands", "Loaded modules", "", "LC", "LV"),
            T("Console", "Modules & commands", "Loaded modules", "Refresh", "LV"),
            T("Console", "Modules & commands", "XBDM command console", "Send", "LV"),
            T("Console", "Notify & Sensors", "", "Discover", "LV"),
            T("Console", "Notify & Sensors", "", "Connect", "LC"),
            T("Console", "Notify & Sensors", "", "Disconnect", "LC"),
            T("Console", "Notify & Sensors", "Temperatures", "Read", "LT"),
            T("Console", "Notify & Sensors", "Notification", "*", "LN"),
            T("Console", "Notify & Sensors", "Ring of light", "*", "LR"),

            // ---- Peek Poker
            T("Peek Poker", "", "", "Connect", "LC"),
            T("Peek Poker", "", "", "Disconnect", "LC"),
            T("Peek Poker", "", "", "Discover", "LV"),
            T("Peek Poker", "", "Where", "", "LC", "D6"),                           // the Xenia choice is labelled
            T("Peek Poker", "Memory view", "Memory view", "Peek", "LC", "EM", "LPK"),
            T("Peek Poker", "Memory view", "Memory view", "Poke changes", "EM", "LPK"),
            T("Peek Poker", "Memory view", "Memory view", "Dump to file...", "EM", "LPK"),
            T("Peek Poker", "Values & trainer", "Read / write a value", "Read", "LC", "LC2", "EM", "LPK"),
            T("Peek Poker", "Values & trainer", "Read / write a value", "Write", "LC2", "EM", "LPK"),     // every type checked byte for byte in Xenia's memory and on the console
            T("Peek Poker", "Values & trainer", "Read / write a value", "Add to trainer", "LC2", "EM"),
            T("Peek Poker", "Values & trainer", "Trainer / freeze list", "*", "LC2", "EM"),
            T("Peek Poker", "Search & regions", "Memory search", "*", "LC", "EM"),
            T("Peek Poker", "Search & regions", "Committed memory regions", "*", "LC", "EM"),

            // ---- Xenia (was the Emulators page until 2026-11-24; the Games tab and Install a save are new -> BETA until proven.
            //      xemu could not be run here; the Controllers card was only tried with an Xbox controller -> BETA)
            T("Xenia", "Setup", "Xenia (Xbox 360)", "Run working file", "EM", "SI"),
            T("Xenia", "Setup", "Xenia (Xbox 360)", "Run a file...", "EM"),
            T("Xenia", "Setup", "Xenia (Xbox 360)", "Use the bundled Xenia", "EM"),
            T("Xenia", "Setup", "Xenia (Xbox 360)", "Newer Xenia builds", "EM"),
            T("Original Xbox (OG)", "", "xemu (original Xbox)", "Get xemu", "EM"),
            T("Xenia", "Xenia Patches", "Patch file", "*", "EM"),
            T("Xenia", "Xenia Patches", "In Xenia", "*", "EM"),
            T("Xenia", "Saves", "", "Open Xenia's content folder", "XC2"),
            T("Xenia", "Saves", "", "Open in save editor", "XC2"),
            T("Xenia", "Saves", "", "Download...", "XC2"),
            T("Xenia", "Saves", "", "Delete", "XC2"),
            T("Xenia", "Saves", "", "Find saves", "XC2"),
            T("Xenia", "Profiles", "", "Find profiles", "XC2"),          // the list's own buttons (named, so the new ones above it stay BETA)
            T("Xenia", "Profiles", "", "Open in profile editor", "XC2"),
            T("Xenia", "Profiles", "", "Download...", "XC2"),
            T("Xenia", "Profiles", "", "Replace with...", "XC2"),
            T("Xenia", "Profiles", "", "Delete", "XC2"),
            T("Xenia", "Profiles", "", "Create a profile...", "SI", "UX"),     // the very CreateProfile Xenia signed in to (SI), pressed in the window (UX)
            T("Xenia", "Profiles", "", "Sign in to selected", "SI", "UX"),
            T("Xenia", "Games", "", "Add a game...", "UX"),
            T("Xenia", "Games", "", "Graphics settings...", "XU"),
            T("Xenia", "Games", "", "DLC / updates from my Xbox", "XE"),
            T("Xenia", "Settings", "Graphics", "Xenia's defaults", "XU"),
            T("Xenia", "Settings", "Graphics", "Sharper (2x)", "XU"),
            T("Xenia", "Settings", "Graphics", "4K (3x)", "XU"),
            T("Xenia", "Settings", "Xenia's settings", "", "XK", "XU"),
            T("Xenia", "Settings", "Xenia's settings", "Reload", "XK", "XU"),
            T("Xenia", "Settings", "Xenia's settings", "Open its folder", "XK", "XU"),
            T("Xenia", "Settings", "Xenia's settings", "Put back my old settings...", "XK", "XU"),
            T("Xenia", "Settings", "Graphics", "", "XK", "XU"),
            T("Xenia", "Settings", "Accuracy and speed", "", "XK", "XU"),
            T("Xenia", "Settings", "Sound and controls", "", "XK", "XU"),
            T("Xenia", "Settings", "Games and console", "", "XK", "XU"),
            T("Xenia", "Settings", "Every Xenia setting", "", "XK", "XU"),
            T("Xenia", "Settings", "Every Xenia setting", "Set", "XK", "XU"),

            // ---- Debugger (Halt, the line Breakpoint toggle, Remove / Remove all, Also stop when, trace Stop / Save stay BETA)
            T("Debugger", "", "", "Connect", "LC"),
            T("Debugger", "", "", "Disconnect", "LC"),
            T("Debugger", "", "", "Discover", "LDB2"),
            T("Debugger", "Execution", "Execution", "Break", "LDB", "LDB2"),
            T("Debugger", "Execution", "Execution", "Continue", "LDB", "LDB2"),
            T("Debugger", "Execution", "Execution", "Step into", "LDB2"),
            T("Debugger", "Execution", "Execution", "Step over", "LDB2"),
            T("Debugger", "Execution", "Execution", "Run to return", "LDB2"),
            T("Debugger", "Execution", "Execution", "Run to selected", "LDB2"),
            T("Debugger", "Execution", "Registers", "", "LDB"),
            T("Debugger", "Execution", "Disassembly", "", "LDB2"),
            T("Debugger", "Execution", "Disassembly", "Go", "LDB2"),
            T("Debugger", "Execution", "Disassembly", "PC", "LDB2"),
            T("Debugger", "Execution", "Disassembly", "Follow", "LDB2"),
            T("Debugger", "Threads", "Threads", "", "LC2", "LDB2"),
            T("Debugger", "Threads", "Threads", "Refresh", "LDB2"),
            T("Debugger", "Threads", "Threads", "Debug this thread", "LDB2"),
            T("Debugger", "Threads", "Threads", "Suspend", "LDB2"),
            T("Debugger", "Threads", "Threads", "Resume", "LDB2"),
            T("Debugger", "Modules", "Loaded modules", "", "LC2", "LDB2"),
            T("Debugger", "Modules", "Loaded modules", "Refresh", "LDB2"),
            T("Debugger", "Modules", "Loaded modules", "Dump module to file...", "LDB2"),
            T("Debugger", "Modules", "Sections", "", "LDB2"),
            T("Debugger", "Breakpoints", "Breakpoints", "Add", "LDB2"),
            T("Debugger", "Breakpoints", "Breakpoints", "Show", "LDB2"),
            T("Debugger", "Trace", "Instruction trace", "Trace", "LDB2"),

            // ---- FTP
            T("FTP", "", "", "Connect", "LFTP"),
            T("FTP", "", "", "Play in Xenia", "LX"),
            T("FTP", "", "", "Save", "LF2"),
            T("FTP", "", "", "Send game folder", "LF2"),
            T("FTP", "", "", "Help", "LF2"),
            T("FTP", "", "", "Upload files", "LF2"),
            T("FTP", "", "", "Upload folder", "LF2"),
            T("FTP", "", "", "Download", "LF2"),
            T("FTP", "", "", "New folder", "LF2"),
            T("FTP", "", "", "Rename", "LF2"),
            T("FTP", "", "", "Delete", "LF2"),

            // ---- Console Setup (launch.ini; xbdm.ini / JRPC.ini were only tried against a stand-in console -> BETA)
            T("Console Setup", "INI Editor", "", "Connect", "LC"),
            T("Console Setup", "INI Editor", "", "Disconnect", "LC"),
            T("Console Setup", "INI Editor > launch.ini", "launch.ini", "Load from console", "LT"),
            T("Console Setup", "INI Editor > launch.ini", "launch.ini", "Change value...", "INI", "LT"),
            T("Console Setup", "INI Editor > launch.ini", "launch.ini", "Add plugin: JRPC2", "INI"),
            T("Console Setup", "INI Editor > launch.ini", "launch.ini", "Add plugin: XRPC", "INI"),
            T("Console Setup", "INI Editor > launch.ini", "launch.ini", "Undo all changes", "INI"),
            T("Console Setup", "INI Editor > launch.ini", "launch.ini", "Use this text", "INI"),
            T("Console Setup", "INI Editor > launch.ini", "launch.ini", "Send to console", "LT"),
            T("Console Setup", "INI Editor > launch.ini", "launch.ini", "Add setting...", "INI2"),
            T("Console Setup", "INI Editor > launch.ini", "launch.ini", "Add plugin: XBDM", "INI2"),
            T("Console Setup", "INI Editor > launch.ini", "launch.ini", "Save as...", "INI2"),
            T("Console Setup", "INI Editor", "", "Discover", "LI"),
            T("Console Setup", "INI Editor > xbdm.ini", "xbdm.ini", "", "LI", "INI2"),
            T("Console Setup", "INI Editor > xbdm.ini", "xbdm.ini", "Load from console", "LI"),
            T("Console Setup", "INI Editor > xbdm.ini", "xbdm.ini", "Change value...", "INI2"),
            T("Console Setup", "INI Editor > xbdm.ini", "xbdm.ini", "Add setting...", "INI2"),
            T("Console Setup", "INI Editor > xbdm.ini", "xbdm.ini", "Undo all changes", "INI2"),
            T("Console Setup", "INI Editor > xbdm.ini", "xbdm.ini", "Use this text", "INI2"),
            T("Console Setup", "INI Editor > xbdm.ini", "xbdm.ini", "Save as...", "INI2"),
            T("Console Setup", "INI Editor > JRPC.ini", "JRPC.ini", "", "LI", "INI2"),
            T("Console Setup", "INI Editor > JRPC.ini", "JRPC.ini", "Load from console", "LI"),
            T("Console Setup", "INI Editor > JRPC.ini", "JRPC.ini", "Change value...", "INI2"),
            T("Console Setup", "INI Editor > JRPC.ini", "JRPC.ini", "Add setting...", "INI2"),
            T("Console Setup", "INI Editor > JRPC.ini", "JRPC.ini", "Undo all changes", "INI2"),
            T("Console Setup", "INI Editor > JRPC.ini", "JRPC.ini", "Use this text", "INI2"),
            T("Console Setup", "INI Editor > JRPC.ini", "JRPC.ini", "Save as...", "INI2"),

            // ---- Mods (applying a mod and unlocking with a real licence were never done through these buttons -> BETA)
            T("Mods", "Game mods", "Game", "", "MD"),
            T("Mods", "Game mods", "1.  Group", "", "MD"),
            T("Mods", "Game mods", "2.  Option", "", "MD"),
            T("Mods", "My mod files", "Your mods", "", "MD"),
            T("Mods", "Game mods", "3.  Set and apply", "", "MO"),
            T("Mods", "Game mods", "3.  Set and apply", "Apply", "MO"),
            T("Mods", "Game mods", "3.  Set and apply", "Turn off", "MO"),
            T("Mods", "My mod files", "Your mods", "Lock selected", "MO"),
            T("Mods", "My mod files", "Your mods", "Refresh", "MO"),
            T("Mods", "My mod files", "Your mods", "Open mods folder", "MO"),
            T("Mods", "My mod files", "Licence", "", "MO"),
            T("Mods", "My mod files", "Licence", "Unlock my mods", "MO", "MS"),
            T("Mods", "My mod files", "Licence", "Forget token", "MO"),
            T("Mods", "My mod files", "Licence", "Copy", "MO"),
            T("Mods", "Mod Maker", "The mod", "", "MO"),
            T("Mods", "Mod Maker", "Protection", "", "MO"),
            T("Mods", "Mod Maker", "Protection", "Build .x360mod...", "MO"),
            T("Mods", "Mod Maker", "What it does (payload)", "*", "MK"),

            // ---- Original Xbox (the certificate, RAM and CPU edits are BETA: no original Xbox or xemu here)
            T("Original Xbox (OG)", "", "XBE header", "*", "OG"),
            T("Original Xbox (OG)", "", "Certificate", "", "OG"),
            T("Original Xbox (OG)", "", "XbeTool", "*", "OG"),

            // ---- Home
            T("Home", "", "Bulk extract ISOs", "", "PC1"),
            T("Home", "", "Neighborhood", "", "PC1"),
            T("Home", "", "Peek Poker", "", "PC1"),
            T("Home", "", "Install to USB / console", "", "PC1"),
            T("Home", "", "FTP", "", "PC1"),
            T("Home", "", "Q & A", "", "PC1"),
            T("Home", "", "Xbox hard drives", "", "HT"),
            T("Home", "", "Original Xbox games", "", "HT"),
            T("Home", "", "Console", "", "PC1"),
            T("Home", "", "Console", "Connect over XBDM", "PC1"),
            T("Home", "", "Console", "Console info", "PC1"),

            // ---- Settings (the two "show again" startup ticks, .bak backups and visible tool windows are labelled; Default tool commands, XeLL IP BETA)
            T("Settings", "Appearance", "Themes", "*", "PC1"),
            T("Settings", "Appearance", "Theme editor", "*", "PC1"),
            T("Settings", "General", "Startup", "", "PC1", "PC2", "ST"),
            T("Settings", "General", "Startup", "Check for updates now", "UP2"),
            T("Settings", "General", "Working with files", "", "PC1", "ST"),
            T("Settings", "Folders & tools", "Default tool commands", "", "ST"),
            T("Settings", "Folders & tools", "Folders and programs", "", "PC1"),
            T("Settings", "Folders & tools", "Folders and programs", "Browse...", "PC1"),
            T("Settings", "Folders & tools", "Folders and programs", "Browse... (2)", "PC1"),
            T("Settings", "Folders & tools", "Folders and programs", "Browse... (3)", "PC1"),
            // ---- Flash Tools (the rest needs a USB drive, a console started into XeLL, or Windows' administrator prompt)
            T("Flash Tools", "", "NAND / RGH", "J-Runner", "FL"),
            T("Flash Tools", "", "NAND / RGH", "Tools & drivers folder", "FL"),
            T("Flash Tools", "", "DVD drive", "Readme", "FL"),
            T("Flash Tools", "", "DVD drive", "Firmware folder", "FL"),
            T("Flash Tools", "", "DVD drive", "Tutorial (PDF)", "FL"),
            T("Flash Tools", "", "FTP2XeLL - read keys from XeLL", "Open in browser", "FL"),
            // ---- Flash Dumps: read against J-Runner itself on 71 dumps (NT), its own extracts (JX), the real window (UN)
            T("Flash Dumps", "Overview", "This flash dump", "", "UN", "NT"),
            T("Flash Dumps", "Overview", "Boot chain and patch slots", "", "UN", "NT"),
            T("Flash Dumps", "Overview", "Boot chain and patch slots", "Save the selected stage (as stored)...", "UN", "NT"),
            T("Flash Dumps", "Overview", "Boot chain and patch slots", "Save the selected stage (decrypted)...", "UN", "NT"),
            T("Flash Dumps", "Overview", "SMC", "", "UN", "NT"),
            T("Flash Dumps", "Overview", "SMC", "Save SMC (decrypted)...", "UN", "NT", "JX"),
            T("Flash Dumps", "Overview", "SMC", "Save SMC (as stored)...", "UN", "NT", "JX"),
            T("Flash Dumps", "Overview", "SMC config", "", "UN", "NT"),
            T("Flash Dumps", "Overview", "SMC config", "Save SMC config (smc_config.bin)...", "UN", "NT", "JX"),
            T("Flash Dumps", "Keyvault", "CPU key", "", "UN", "NT"),
            T("Flash Dumps", "Keyvault", "CPU key", "Unlock", "UN", "NT"),
            T("Flash Dumps", "Keyvault", "Keyvault", "", "UN", "NT"),
            T("Flash Dumps", "Keyvault", "Keyvault", "Copy DVD key", "UN", "NT"),
            T("Flash Dumps", "Keyvault", "Keyvault", "Save keyvault (decrypted)...", "UN", "NT", "JX"),
            T("Flash Dumps", "Keyvault", "Keyvault", "Save keyvault (as stored)...", "UN", "NT", "JX"),
            T("Flash Dumps", "Keyvault", "Keyvault", "Save fcrt.bin (decrypted)...", "UN", "NT", "JX"),
            T("Flash Dumps", "Files", "Flash file system", "", "UN", "NT"),
            T("Flash Dumps", "Files", "Flash file system", "Extract selected...", "UN", "NT"),
            T("Flash Dumps", "Files", "Flash file system", "Extract all...", "UN", "NT"),
            T("Flash Dumps", "Files", "Flash file system", "Open in X360GameHack", "UN"),
            T("Flash Dumps", "Files", "Swap boot animation", "Save this dump's boot animation...", "UN", "NT"),
            T("Flash Dumps", "Checks", "Pages, blocks and spare bytes", "", "UN", "NT"),
            T("Flash Dumps", "Checks", "Pages, blocks and spare bytes", "Save without spare bytes...", "UN", "NT"),
            T("Settings", "Folders & tools", "Folders and programs", "Browse... (4)", "PC1"),
            T("Settings", "Folders & tools", "Bundled tools", "*", "PC1"),
            T("Settings", "Network", "Console (XBDM)", "", "PC2"),
            T("Settings", "Network", "Other", "Forget saved FTP passwords", "PC1"),
            T("Settings", "Privacy & data", "Security", "*", "PC1"),
            T("Settings", "Privacy & data", "Your data", "*", "PC1"),
            T("Settings", "Privacy & data", "Utilities", "*", "PC1"),
            T("Settings", "About", "X360GameHack 2026", "*", "PC1"),

            // ---- Q & A is help text (TextOnlyPages): never labelled
        };

        /// <summary>
        /// Command-line / MCP commands proven end to end the same way (run through the real exe, answer checked against a
        /// trusted tool or the real data). Every other command is reported as beta by "cli help" and in the MCP tool list.
        /// </summary>
        public static readonly Dictionary<string, string> TestedCommands = new Dictionary<string, string>
        {
            // CliCheck 2026-11-18: answers compared with xextool / extract-xiso run separately or with the files read back
            ["help"] = "CliCheck", ["info"] = "CliCheck", ["list"] = "CliCheck", ["verify"] = "CliCheck", ["extract"] = "CliCheck",
            ["tu-extract"] = "CliCheck", ["tu-check"] = "CliCheck", ["profile"] = "CliCheck", ["achievements"] = "CliCheck",
            ["xex"] = "CliCheck", ["functions"] = "CliCheck", ["imports"] = "CliCheck", ["disasm"] = "CliCheck",
            ["decompile"] = "CliCheck", ["save-image"] = "CliCheck", ["iso-list"] = "CliCheck", ["iso-extract"] = "CliCheck",
            ["iso-create"] = "CliCheck", ["patch-list"] = "CliCheck",
            // LicServerE2E: packs built through the CLI and unlocked by the real licence server code
            ["mod-info"] = "LicServerE2E", ["mod-validate"] = "LicServerE2E", ["mod-build"] = "LicServerE2E",
            // MD: signed the 14 real mod packs with the owner key (verified by the program and by Node's crypto); a second run says already signed
            ["mod-sign"] = "OS",
            // McpConsole 2026-11-18 night: through the real exe's MCP server against the real console (one connection):
            // its identity, a 100 KB file put and got back byte for byte, the dashboard's DOS-stub text read / written /
            // restored, a PNG of the screen, plausible sensors, and the pop-up seen on the console's own screenshot
            ["console-find"] = "McpConsole", ["console-info"] = "McpConsole", ["console-list"] = "McpConsole",
            ["console-get"] = "McpConsole", ["console-put"] = "McpConsole", ["console-read"] = "McpConsole",
            ["console-write"] = "McpConsole", ["console-screenshot"] = "McpConsole", ["console-temperatures"] = "McpConsole",
            ["console-notify"] = "McpConsole", ["console-notify-icons"] = "McpConsole",
            // CliMore 2026-11-19, through the real exe (64-bit since this run): ghidra wrote the same five files byte for byte
            // as To Ghidra... (whose export Ghidra 12.0.4 built a project from); iso-swap's image read by extract-xiso (swapped
            // files exact, all 147 others unchanged) and run by Xenia; xenia-read equal to Xenia's memory read directly and to
            // xextool's decode; xenia-write landed in Xenia's memory and reported what was there; patch-install's file applied
            // by Xenia at the next start with only the chosen patch on
            ["ghidra"] = "CliMore", ["iso-swap"] = "CliMore", ["patch-install"] = "CliMore", ["xenia-read"] = "CliMore", ["xenia-write"] = "CliMore",
            // DiskEject 2026-11-24, through the installed exe on the user's real Xbox 360 drive (Hitachi 250 GB) on an internal
            // SATA port that Windows calls non-removable (removal policy 1, so its own eject is refused): Windows dropped the
            // disk in 5 s (device problem 21 = removed), the drive was unplugged, and the Trinity RGH3 then booted from it and
            // read it - files written on the PC came back byte for byte over XBDM
            ["disk-eject"] = "DiskEject",
            // DiskRun 2026-11-24, through the installed exe on the same real drive: mkdir / put / list / check / tree / delete /
            // undo / sessions. The console (Trinity RGH3) read what was written over XBDM byte for byte with the right times
            // (UTC on the drive) and no longer listed what was deleted; with the final helper an undo put back 85 of 85 changes
            // and the whole Data tree (30,640 entries) was byte for byte as before, the undo journal's table snapshot let it
            // through, an old-format journal was refused without a write, and the drive ended exactly as before the tests.
            // Again 2026-11-25 on the user's real ORIGINAL Xbox drive (WDC 1 TB, XBpartitioner table): mkdir + put wrote
            // E:\x360gh-test with a 1 MiB file and a note (272 blocks, journaled), the file read back byte for byte, E: checked
            // healthy; undo put back 72 of 72 changes and a fresh read-only open found every entry of E: (1,274: path, size,
            // cluster, time) and its free space exactly as before - the drive ended as it began
            ["disk-run"] = "DiskRun",
            // DiskRemount 2026-11-24, through the installed exe on the same real drive, left connected: disk-eject took it away
            // (Windows no longer listed it), disk-remount brought it back as the same disk (Disk 0, RAW, online) without a
            // write. A plain hardware scan does not do that (checked first: the disk stays parked, problem 21).
            ["disk-remount"] = "DiskRemount",
            // Recover 2026-11-24: RecoverTests 46/0 on new Xbox 360 and original Xbox images (plain deletes, deleted folders two
            // levels down, reused space, empty files, a fragmented file whose clusters were never freed, data whose record is
            // gone, a file written in pieces around clusters in use, two deleted files sharing space) - everything recoverable
            // byte for byte; and on the real Xbox 360 drive's image a file the console itself deleted (X360GH-cli-test.bin)
            // came back byte for byte against the original. On the real drive itself (disk-run recover-list / recover, read only,
            // 0 blocks written) the deleted folder x360gh-test2 came back with both files byte for byte - the 1.2 MB one put
            // together from its 3 pieces (clusters 189515, 189537 and 8,643,426, as the first write's journal shows)
            ["drive-recover-list"] = "Recover", ["drive-recover"] = "Recover",
            // CliGodTests 2026-11-24 (13 checks through the real exe): god-info as the game says; iso2god's package byte for
            // byte the engine's (which GP proves against ISO2God's own code), from a game folder for a devkit with the XEX
            // exactly as xextool makes it and the header signed by X360PkgTool; god2iso compact / exact; existing outputs and
            // bad options refused with nothing written. The Crysis package that ran in Xenia (XG) was made with iso2god.
            ["god-info"] = "CliGodTests", ["iso2god"] = "CliGodTests", ["god2iso"] = "CliGodTests",
            // convert: every direction between image, package and folder, as it is / RGH / devkit (CV, 29 checks)
            ["convert"] = "CliConvertTests",
            // CliNandTests 2026-11-24, through the real exe on 7 real dumps (Falcon, Jasper big and small block, Trinity, an RGH 1.2
            // Falcon, Corona 4 GB stock and RGH3; 149 checks): nand-info as the engine reads them (which NT proves against J-Runner),
            // including how each boot stage decrypts and the lockdown values; keyvault values only with kv=show, a wrong key refused;
            // nand-extract wrote every file byte for byte; nand-stages wrote every stage as stored and each stage that decrypts right
            // byte for byte (CPU-key-paired stages left out without the key); both refused an existing folder, and a dump with nothing
            // to write, and never changed a dump
            ["nand-info"] = "CliNandTests", ["nand-extract"] = "CliNandTests", ["nand-stages"] = "CliNandTests",
            // BETA (not end to end): rehash, create-save, unlock, gamesave-* (never loaded by a console / game),
            // drive-* (no real Xbox 360 drive reference).
        };

        public static bool CommandTested(string name) => TestedCommands.ContainsKey(name ?? "");

        /// <summary>
        /// Single choices inside a proven card that are not proven themselves: a drop-down item, a switch segment or a
        /// tick box (matched by the text it shows). They get their own BETA label.
        /// </summary>
        public static readonly Entry[] UnprovenChoices =
        {
            C("Console", "Console", "Power & system", "Turn_Off_Console"),
            C("Xbox ISOs", "Bulk extract", "Bulk extract + patch", "Install content packages found on the discs to the USB drive"),
            C("Xbox ISOs", "Convert", "Convert games", "Stealth-patch each new image with abgx360"),
            C("Xbox ISOs", "Convert", "Convert games", "Devkit"),
            C("Xbox ISOs", "Convert", "Convert games", "USB drive"),
            C("Xbox ISOs", "Convert", "Convert games", "Console (XBDM)"),
            C("Xbox ISOs", "Convert", "Convert games", "Console (FTP)"),
            C("Xbox ISOs", "Convert", "Convert games", "Like Microsoft"),
            C("Xbox ISOs", "Convert", "Convert games", "Every sector in place"),
            C("X360Packages", "Edit", "Signing & license flags", "CON"),
            C("Settings", "General", "Startup", "Ask me to install new versions"),
        };

        /// <summary>What is known about a BETA item that was partly tried (shown in its tooltip and in TESTING.md).</summary>
        public static readonly Dictionary<string, string> Notes = new Dictionary<string, string>
        {
            [K("Drive Explorer", "", "", "Make Xbox drive...")] = "drives made on image files read back exactly as each console lays out its own (FMT) - original Xbox in every layout XBpartitioner makes, Xbox 360 equal to a real console-made drive; the Disk Reader's format mode is proven over a real pipe on images, with undo. No drive made here has been used in a console or emulator yet, and it has never been run on a real drive.",
            [K("Settings", "General", "Startup", "Ask me to install new versions")] = "the whole ask-check-install path works against a loopback stand-in (UP2); no real release has gone through the real update server and GitHub yet.",
            [K("Flash Dumps", "Files", "Swap boot animation", "Swap boot animation...")] = "the new flash dump is proven on the PC: replacing bootanim.xex with its own bytes gives a byte-identical dump, and a changed one reads back through our reader and J-Runner's on all 62 flash dumps with every other file and the EDC intact (NW, 1,460 checks). No console has booted one yet - flashing a NAND can brick a console.",
            [K("Flash Dumps", "Files", "Swap boot animation", "Replace selected file...")] = "the same flash-file-system write as Swap boot animation, proven on the PC (NW); not booted on a console. A file larger than the one in the dump is refused.",
            [K("Flash Dumps", "Keyvault", "Swap keyvault", "Swap keyvault...")] = "the new flash dump is proven on the PC: replacing the keyvault with its own bytes gives a byte-identical dump, a swapped keyvault reads back as stored (and the matching CPU key unlocks it) with every file and the EDC intact (NW). No console has booted one yet - flashing a NAND can brick a console.",
            [K("Xbox ISOs", "This image", "This disc image", "Convert to Games on Demand")] = "the package plays in Xenia and turns back into a disc image with every file identical; never started on a console.",
            [K("Xbox ISOs", "abgx360", "abgx360 - verify and fix disc images", "Verify + AutoFix")] = "the same run in the program as Verify only (proven, UC / AB) with AutoFix level 3 (--af3); no complete disc image was at hand to fix.",
            [K("Xbox ISOs", "abgx360", "abgx360 - verify and fix disc images", "Run with these options")] = "the same run as Verify only with the card's options; every option it can give is one abgx360 1.0.7 reads (AB). Not pressed in a test.",
            [K("Xbox ISOs", "abgx360", "abgx360 - verify and fix disc images", "Set up abgx360")] = "with abgx360 installed it writes Hadzz's fix file and copies the video files - proven in a sandbox (UC, AB), and abgx360 reads the file. Running Hadzz's installer when abgx360 is missing was not tried: it was already installed here.",
            [K("Xbox ISOs", "abgx360", "abgx360 - verify and fix disc images", "Stop")] = "the runner it uses ends abgx360 at once (AB); the button itself was not pressed.",
            [K("Xbox ISOs", "Convert", "Convert games", "Stealth-patch each new image with abgx360")] = "on every made-here image tried (repacked, folder-built, from packages; a Games on Demand edition of Crysis) it left the image as it was and said why (not a complete game partition / an XEX no disc has) - abgx360 itself confirmed those reasons. A complete disc game partition with its disc XEX, the one case abgx360 can stealth patch, was not at hand.",
            [K("Xbox ISOs", "Convert", "Convert games", "Devkit")] = "proven through the command line (CV: every root XEX exactly what xextool -m d makes, the package unlocked for a devkit) and by the earlier ISO → GOD tab (UG); not chosen in this tab in a test.",
            [K("Xbox ISOs", "Convert", "Convert games", "USB drive")] = "the same install step the earlier ISO → GOD tab used (UG: copied byte for byte into <USB>\\Content\\0000000000000000); not chosen in this tab in a test.",
            [K("Xbox ISOs", "Convert", "Convert games", "Every sector in place")] = "proven through the command line (CV, layout=exact) and god2iso parity (GP); not chosen in this tab in a test.",
            [K("Saves", "Game editor", "Game values", "Write into the save")] = "written, checked, and put back on the console - but the game has not loaded the edited save yet.",
            [K("Saves", "Rehash & resign", "Rehash & resign", "Rehash & resign")] = "every hash checked afterwards; no console has loaded a save it changed yet.",
            [K("Saves", "Create a save", "Files", "Create package...")] = "the package passes every check; no console or game has loaded one yet.",
            [K("Profiles", "Games", "Every game", "Unlock all achievements")] = "matches console-made unlocks record for record and passes every check; no edited profile has been signed in on a console yet.",
            [K("Xenia", "Saves", "", "Replace with...")] = "puts the save into Xenia's form byte for byte (2026-11-18); no game has opened a copied save yet.",
            [K("Console", "Console", "Power & system", "Open tray")] = "sent on 2026-11-18 (three times, accepted each time); that the tray moved was not confirmed by anyone watching.",
            [K("Console", "Console", "Power & system", "Close tray")] = "sent on 2026-11-18 (twice, accepted each time); that the tray moved was not confirmed by anyone watching.",
            [K("Xenia", "Setup", "Controllers", "Check controllers")] = "proven with an Xbox 360 controller: Windows and Xenia's own controller driver list the same pad, and Xenia's input setting is kept on \"any\". No PlayStation or Switch controller tried yet.",
            [K("Drive Explorer", "", "", "Copy to Xenia")] = "on the user's own Xbox drive image (2026-11-25, read only): WaW's 2 map packs and its title update went into Xenia unpacked the way the bundled Xenia keeps them, byte for byte; a dashboard cache in the DLC folder was left out and named. The bundled Xenia applied that title update and listed and mounted those map packs in WaW (its own log). Not yet played with by a user.",
            [K("Mods", "Mod Maker", "Your signing key", "Back up signing key...")] = "the backup engine is proven (2026-11-25, 15 checks on the real key, only read): sealed with PBKDF2-SHA256 600,000 rounds + AES-256 + HMAC, opened again before it is kept, wrong password / changed byte / another key refused, never replaces a file, restore (cli owner-key-restore) never replaces a key and the restored key signs mods the program takes. The window's own button and password box have not been pressed in a test yet.",
            [K("Xenia", "Games", "", "Install a save...")] = "a save for the selected game goes in for the profile Xenia signs in to, from a save file or a .zip / .rar, and a save for another game is left out (2026-11-25, checked with the bundled Xenia's own rules); WaW in the bundled Xenia opened a save put in this way. Not yet loaded in a game by a user.",
            [K("Xenia", "Saves", "", "Install a save...")] = "console saves, and .zip / .7z / .rar holding them, go in for the profile Xenia signs in to, with the header the bundled Xenia reads (2026-11-25, checked with its own rules); a save that came with its own profile can stay with it (that profile goes in too and Xenia signs in to it). WaW in the bundled Xenia opened a save put in this way. Not yet loaded in a game by a user.",
            [K("Xenia", "Saves", "", "Install saves from a folder...")] = "the same as Install a save... for every save in the folder, and a profile there that the saves belong to is offered as their own (2026-11-25, on a copy of a real download); no game has loaded one in Xenia yet.",
            [K("Xenia", "Games", "", "Play")] = "reaches Xenia's start for the selected game with the right Xenia (2026-11-25); not yet seen starting each kind of game from this tab.",
            [K("Xenia", "Games", "", "Remove...")] = "taking an added game off the list is proven; for a copy it asks first, and the Recycle Bin delete itself was not run in the test.",
            [K("Xenia", "Xenia Patches", "On a real console", "Apply enabled to console")] = "not run on the console with this build.",
            [K("Flash Tools", "", "DVD drive", "Jungle Flasher")] = "not started in a test: Jungle Flasher's own manifest asks Windows for administrator rights, so Windows always asks first.",
            [K("Flash Tools", "", "NAND / RGH", "Copy SimpleNandFlasher to USB...")] = "needs a FAT32 USB drive plugged in; not tried.",
            [K("Flash Tools", "", "FTP2XeLL - read keys from XeLL", "Read")] = "needs a console started into XeLL; not tried.",
            [K("Profiles", "Games", "Add games", "Add a game by Title ID...")] = "on a copy of a real console profile (2026-11-19): Call of Duty: Ghosts went in with the title database's 91 achievements and 2000 G, all locked - every record the database's own data, the 50 the game's XEX carries agreeing with it - the other games untouched; the same game again is refused. No console or emulator has signed in to a profile changed this way yet.",
            // XBE edits (UiXbe 15/0, 2026-11-19, on a copy of a real game XBE): read back by XbeTool, only the field's own bytes changed; no emulator or console ran an edited XBE yet
            [K("Drive Explorer", "Saves", "", "Copy to PC...")] = "original Xbox saves on drive images made here (UiOgSaves, 2026-11-25): listed by TitleMeta.xbx / SaveMeta.xbx - also " +
                "the shapes the user's real drive's UDATA has (85 games: the saves' picture beside them, a folder without SaveMeta.xbx, capital title folders) - and " +
                "copied with their game's TitleMeta.xbx and pictures byte for byte; not yet done on a real original Xbox drive.",
            [K("Drive Explorer", "Saves", "", "Put a save back...")] = "onto a blank original Xbox drive image (UiOgSaves): the same files land in UDATA byte for byte and the partition " +
                "checks out, saves already there are never replaced; no console has loaded a save put back this way yet.",
            [K("Drive Explorer", "", "", "Allow changes")] = "what it switches on is proven on both of the user's real drives through the installed program's command line (the same " +
                "Disk Reader and writer): the Xbox 360 drive (2026-11-24: files written, read back by the console over XBDM, undone 85 of 85) and the original Xbox " +
                "drive (2026-11-25: a folder with a 1 MiB file written to E:, read back byte for byte, undone 72 of 72 - every entry of E: as before). The button " +
                "itself has not been pressed on a real drive yet.",
            [K("Drive Explorer", "", "", "Undo changes...")] = "the undo behind it put both of the user's real drives back exactly through the installed program's command line (Xbox 360 " +
                "drive 85 of 85 changes, 2026-11-24; original Xbox drive 72 of 72, 2026-11-25, every entry of E: as before). The window's own dialog has not been used on a real drive yet.",
            [K("Drive Explorer", "", "", "Play in xemu")] = "in the real window on an original Xbox drive image made here (UiXemuPlay, 2026-11-25): the game copied byte for byte, its " +
                "XISO holds every file, xemu (a stand-in) gets -dvd_path with it, the image is kept while the folder is the same and made again when it changes, a disc image on the drive " +
                "starts as it is; the buttons follow the drive. xemu itself has not run a game started this way yet (its BIOS files were not on this PC).",
            [K("Original Xbox (OG)", "", "xemu (original Xbox)", "Play this game")] = "the open default.xbe's folder becomes its XISO beside it and xemu (a stand-in) gets -dvd_path with it " +
                "(UiXemuPlay); not yet booted in a real xemu.",
            [K("Original Xbox (OG)", "", "Certificate", "Set")] = "XbeTool reads the new Title ID and only those 4 certificate bytes change (2026-11-19); no emulator or console has run an edited XBE yet.",
            [K("Original Xbox (OG)", "", "Certificate", "Apply region")] = "XbeTool reads the new region and only that field changes (2026-11-19); no emulator or console has run an edited XBE yet.",
            [K("Original Xbox (OG)", "", "Certificate", "Allow all media")] = "XbeTool reads HDD, DVD and CD allowed and only that field changes (2026-11-19); no emulator or console has run an edited XBE yet.",
            [K("Original Xbox (OG)", "", "RAM patch", "Use 128MB")] = "XbeTool's init flags lose LIMIT_64MB and only that field changes (2026-11-19); no 128MB console or emulator has run it yet.",
            [K("Original Xbox (OG)", "", "RAM patch", "Stock 64MB")] = "XbeTool's init flags get LIMIT_64MB back (2026-11-19); no console or emulator has run it yet.",
            [K("Original Xbox (OG)", "", "CPU clock patch", "1400 (Trusty)")] = "changes exactly the game's stock 733.33 MHz constant(s) and nothing else; back to stock gives the file byte for byte (2026-11-19). No upgraded console has run it.",
            [K("Original Xbox (OG)", "", "CPU clock patch", "1480 (DreamX)")] = "moves the same places on from 1400 to 1480 MHz; back to stock gives the file byte for byte (2026-11-19). No upgraded console has run it.",
            [K("Original Xbox (OG)", "", "CPU clock patch", "733 (stock)")] = "returns a 1400 / 1480 MHz patch to the original file byte for byte; after 1000 MHz or a custom value it points to the .bak instead of guessing (2026-11-19).",
            [K("Original Xbox (OG)", "", "CPU clock patch", "1000 (Trusty)")] = "warns that 1000 MHz cannot be found again, then changes only the stock constant - the game's own 1,000,000,000 values stay (2026-11-19). No upgraded console has run it.",
            [K("Original Xbox (OG)", "", "CPU clock patch", "Set MHz")] = "a custom value (1200 tried) changes only the stock constant, after a warning that it cannot be found again (2026-11-19). No upgraded console has run it.",
            [K("Neighborhood", "Saves", "", "Copy to Xenia")] = "from an Xbox drive image the save and its profile went into Xenia and Xenia signed in to it. From the console on 2026-11-18 the save went in but its profile (signed in on the console, so being written to) had one block that did not match its checksum and was refused; such a profile now goes in as it is with a warning naming the file, and Xenia signed in to that very profile - not yet repeated from the console. No game has opened a copied save yet.",
            [K("Neighborhood", "Profiles", "", "Copy to Xenia")] = "the same copy as Add files..., whose profiles Xenia signs in to (also one with a block that does not match its checksum); not yet from a console.",
            [K("Saves", "Browse saves", "", "Copy to Xenia")] = "from this tab (2026-11-19): a console save in a folder on this PC went into Xenia unpacked byte for byte, its profile with it. No game has opened a copied save yet.",
            [K("Saves", "Convert", "Convert with X360PkgTool", "Make save RGH / retail")] = "on a console save without key00033.bin / key00036.bin next to X360PkgTool it explains that and offers Rehash & resign instead of running (2026-11-19). X360GameHack ships no console keys, so X360PkgTool itself was never run on a console save.",
            [K("Saves", "Convert", "Convert with X360PkgTool", "Make save devkit")] = "the same as Make save RGH / retail: explains and offers Rehash & resign without the console's key files; never run with them.",
            [K("Saves", "Convert", "Convert with X360PkgTool", "Remove console traces")] = "the same as Make save RGH / retail: explains and offers Rehash & resign without the console's key files; never run with them.",
            [K("Drive Explorer", "Saves", "", "Copy to Xenia")] ="the save and its profile went into Xenia from an Xbox drive image made by this program, and Xenia signed in to it; no game has opened the save yet.",
            [K("Drive Explorer", "", "", "Play in Xenia")] = "games copied off an Xbox drive image made by this program run in Xenia (a package and a game folder); not yet tried with a real Xbox drive.",
            [K("Debugger", "Threads", "Threads", "Halt")] = "this console's XBDM (2.0.21076.11) refuses to halt a single thread; the page says so and points to Break.",
            [K("Debugger", "Execution", "Disassembly", "Breakpoint")] = "sets and clears the same breakpoint as Breakpoints > Add (which the game ran into on 2026-11-18); a stop at one set with this button was not watched.",
            [K("Debugger", "Breakpoints", "Breakpoints", "Remove")] = "takes the breakpoint off the list and the console; that the game then no longer stops there was not checked separately.",
            [K("Debugger", "Breakpoints", "Breakpoints", "Remove all")] = "takes every breakpoint off the list and the console; that the game then no longer stops there was not checked separately.",
            [K("Debugger", "Breakpoints", "Also stop when", "Apply")] = "accepted by the console on 2026-11-18; that it then stops when a thread starts was not watched.",
            [K("Console Setup", "INI Editor > launch.ini", "launch.ini", "New")] = "every setting it writes is one the console's own launch.ini has, in the same section, at a value it takes (2026-11-19); DashLaunch has not read a file started this way yet.",
            [K("Console Setup", "INI Editor > xbdm.ini", "xbdm.ini", "Send to console")] = "the edited file is exactly what it must be (only the changed line differs) and Save as writes it (2026-11-19); sending one to the console was not tried.",
            [K("Console Setup", "INI Editor > JRPC.ini", "JRPC.ini", "Send to console")] = "the edited file is exactly what it must be (only the changed value differs) and Save as writes it (2026-11-19); sending one to the console was not tried.",
        };

        public static string NoteFor(string page, string tab, string card, string button) =>
            Notes.TryGetValue(K(page, tab, card, button), out string n) ? n : null;

        private static string K(string page, string tab, string card, string button) => page + "|" + tab + "|" + card + "|" + button;
        private static Entry C(string page, string tab, string card, string choice) => new Entry { Page = page, Tab = tab, Card = card, Button = choice, Proofs = new string[0] };

        /// <summary>True when this choice (drop-down item, switch segment or tick box text) inside that card is not proven.</summary>
        public static bool ChoiceUnproven(string page, string tab, string card, string choice) =>
            UnprovenChoices.Any(e => e.Page == page && e.Tab == tab && e.Card == card && e.Button == choice);

        /// <summary>Pages that are reference text only and never get a BETA label.</summary>
        public static readonly string[] TextOnlyPages = { "Q & A" };

        private static Proof P(string code, string test, bool live, string what) => new Proof { Code = code, Test = test, Live = live, What = what };
        private static Entry T(string page, string tab, string card, string button, params string[] proofs) =>
            new Entry { Page = page, Tab = tab, Card = card, Button = button, Proofs = proofs };

        private static bool Same(string pattern, string value) => pattern == "*" || string.Equals(pattern, value ?? "", StringComparison.Ordinal);

        private static bool TabCovers(string entryTab, string tab) =>
            entryTab == "*" || entryTab == tab || entryTab.StartsWith(tab + " > ", StringComparison.Ordinal);

        /// <summary>The entry that covers this button (or this card's own content when <paramref name="button"/> is ""), else null.</summary>
        public static Entry Find(string page, string tab, string card, string button) =>
            Tested.FirstOrDefault(e => e.Page == page && Same(e.Tab, tab) && Same(e.Card, card) && Same(e.Button, button));

        /// <summary>True when something on this page (inside this tab, inside this card) has been tested.</summary>
        public static bool AnyTested(string page, string tab = null, string card = null) =>
            Tested.Any(e => e.Page == page && (tab == null || TabCovers(e.Tab, tab)) && (card == null || Same(e.Card, card)));

        public static bool IsTextOnly(string page) => TextOnlyPages.Contains(page);

        public static Proof ProofOf(string code) => Proofs.FirstOrDefault(p => p.Code == code);
    }
}
