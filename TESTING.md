# X360GameHack 2026 v4.0.0 - what is tested and what is BETA

Every page, tab, card and button of the program, and whether it is proven to work end to end. This list is made from the
program itself: the same table that puts the **BETA** labels in the window (`X360GameHack/Core/Services/TestStatus.cs`) is
written out here, so the list and the labels always agree.

**TESTED** means proven end to end. All three of these hold:

1. the button itself was pressed in the real program window (or its own handler was run with a file in place of the file picker);
2. everything the button is for was checked on the real thing - console features on a real modded console (Trinity RGH3), and
   files against the identical output of a trusted tool (xextool, extract-xiso) or the real source data;
3. nothing that matters to it has changed since the test.

Not enough for TESTED: tests of the code underneath without the button, tests against a stand-in console, "no error" or
"it says so" checks, and files that were checked only by this project's own checkers but never used by a console, game or
emulator. That is why every editor that changes a profile, a save or a drive image is still BETA: the files they write pass
every hash and record check, but no console has used one yet.

**BETA** means not proven yet. It may well work. The program shows a small orange **BETA** label on it (on the side-menu
entry when nothing on a page is proven, else on the tab, the card or the single button), and hovering such a button says so.
Keep a backup of anything a BETA feature changes. Labels come off as each part is proven.

Totals (buttons in the window, Q & A excluded): **430 tested, 128 BETA**.

## How things were tested

| Code | Test | Where | What it did |
|---|---|---|---|
| XI | UiXexIso | this PC | 2026-11-18, release build: every XEX Tool and Xbox ISOs button pressed in the real window on copies; each result compared byte for byte with xextool / extract-xiso run separately and with xextool's own report (360 checks, all passed). |
| D1 | UiDrive | this PC | 2026-11-18, release build: a real profile shown correctly (gamertag, Xbox LIVE state, gamerscore, games, achievements), read back from the file. |
| D2 | UiDrive2 | this PC | 2026-11-18, release build: the avatar award lists of a real profile shown correctly. |
| D3 | UiDrive3 | this PC | 2026-11-18, release build: Extract all (every file byte for byte), View GPD and every GPD viewer tab on real profiles. |
| D4 | UiDrive4 | this PC | 2026-11-18, release build: a real game XEX opened in the disassembler; Jump, Find all and Disassemble agree with the disassembler (itself checked against Capstone on 846,466 instructions). |
| D6 | UiDrive6 | this PC | 2026-11-18, release build: Disassemble / Decompile on a selection and on the page; a real title update extracted byte for byte; the Peek Poker console / Xenia switch. |
| INI | UiIni | this PC | 2026-11-18, release build: launch.ini edits (change a value, add JRPC2 / XRPC, undo, use this text) checked on the user's real launch.ini text. |
| MK | UiMaker | this PC | 2026-11-18, release build: Mod Maker templates and syntax check (line numbers, red lines, live checking). |
| MD | UiMods | this PC | 2026-11-18, release build: the Mods page lists games, groups, options and mod files correctly. |
| FI | UiFinders | real console (Trinity RGH3) | 2026-11-18: the Profiles and Title updates finders read the real console's profiles and title updates (read only). |
| PKG | PkgSweep | this PC | Extract all through the same code on 37 real packages (saves, title updates, DLC, arcade, themes), every file hash-checked. |
| LC | UiConsole | real console (Trinity RGH3) | live on the Trinity RGH3 through the real window: connect / disconnect, console information, screenshot, modules, freeze / resume, file and save lists, Peek, search, regions. |
| LC2 | UiConsole2 | real console (Trinity RGH3) | live: Peek Poker read / write / trainer freeze (held against 5 outside changes), Debugger threads and modules, a console save copied to the PC and put back byte for byte. |
| LDB | UiDebug | real console (Trinity RGH3) | live: Debugger Break froze the console, registers and code shown, Continue let it run again. |
| LF | UiFiles | real console (Trinity RGH3) | live: new folder, upload a file and a folder (read back identical), delete a folder with everything in it; Console launch box (Aurora restarted) and the Avatar Editor shortcut. |
| LFTP | UiFtp | real console (Trinity RGH3) | live: FTP login (a wrong login explained), drives and folders listed. |
| LX | UiXenia / UiXenia2 | real console (Trinity RGH3) | live: Play in Xenia for a game on the console, over XBDM and over FTP; the game was copied and Xenia showed it running. |
| LS | UiSaveE2E | real console (Trinity RGH3) | live: a console save put back and read back from the console byte for byte. |
| LT | ConsoleLiveTest | real console (Trinity RGH3) | live on 2026-11-18 (current console code): temperatures, title update install / find / delete (installed byte for byte; the console applies updates installed this way), profile replace, launch.ini load / send / read back with the new plugins loading after the restart. |
| LV | UiLive | real console (Trinity RGH3) | live on the Trinity RGH3 through the real window, 2026-11-18 evening, one connection: Discover on every console page; the Neighborhood shortcuts; upload, download and rename of files and folders, byte for byte; saves and profiles downloaded with valid signatures and hashes; a save replaced and deleted and a profile deleted - test copies only, the real ones untouched; title updates downloaded with valid Microsoft signatures; a console screenshot saved; the module list read; a raw XBDM command answered; Console information read again (name, kernel and XBDM version as the console reports them). |
| EM | UiEmu | this PC | 2026-11-18, release build, in the bundled Xenia Canary with real games: Terraria (an arcade package) and SpongeBob (a game folder, a disc image built by extract-xiso, a swapped image, Games on Demand both ways) started from these buttons and seen running - Xenia's log names the game and its memory holds the game's code exactly as xextool decodes it; patches installed and applied by Xenia, and written into the running game; every Peek Poker value type written into and read back from Xenia's memory, the trainer, searches, Poke changes and Dump checked against Xenia's memory read separately; links answered. Also run with the program folder read-only, as in Program Files. |
| XC | UiXc | this PC | 2026-11-18, release build: console profiles and a console save copied into Xenia land where Xenia looks for them, unpacked as Xenia's own installer does, every file byte for byte; Xenia opened both profiles and signed in to the copied one; Copy to Xenia from an Xbox drive image brought the save's profile along and Xenia signed in to it; Play in Xenia from an Xbox drive image started Terraria (a package) and SpongeBob (a game folder). |
| OG | UiOg | this PC | 2026-11-18 night, release build, on a copy of a real game XBE (NFS Underground 2), against XbeTool run directly: every value of the XBE header and Certificate cards equals XbeTool's own listing (title, title ID, version, disc, base address, sections, init flags, image size, allowed media, region) and the entry point equals the one XbeTool's binary dump uses; Refresh reads it again; All header info, Certificate, Sections and Libraries put XbeTool's own listing into the log line for line; Dump to binary, Dump to EXE and Dump IDA .idc write files identical to XbeTool's; Open folder shows the XBE; the XBE itself was left unchanged. |
| LN | UiNotify | real console (Trinity RGH3) | live on the Trinity RGH3 through the real window, 2026-11-18 night (JRPC2 and XRPC both loaded): Send showed "Hello from X360GameHack" with the chosen picture, caught on the console's own screen over XBDM; Show every picture showed one pop-up after another (Picture 0: Xbox logo, Picture 1 ...), each caught the same way, and Stop ended it; the console kept answering throughout. |
| PK | UiPkg | this PC | 2026-11-18 night, release build: every X360Packages button pressed in the real window on copies of a real DLC package (LIVE, metadata version 2), each result compared with X360PkgTool run directly on an identical copy. The Package card shows the IDs, versions, names, install path and signature type X360PkgTool -p reads (also after each edit); Full info is X360PkgTool's own printout line for line; Refresh reads it again; every Quick action, Creator IDs Set, Apply flags (every flag both ways, PIRS and LIVE signing) and Custom command Run give exactly X360PkgTool's file; Metadata > Apply (the program's own writer) gives exactly X360PkgTool -st / -sn / -sd / -sp's file, all twelve language slots included, apart from the LIVE signature, which only Microsoft can make (X360PkgTool writes one with its own key; RGH / JTAG and Xenia do not check it); Set picture and Set game picture put the same 64x64 PNG at the same bytes as X360PkgTool -i and write its exact length as the size, as all 6 console-made saves and profiles do (X360PkgTool writes a fixed D03D0000 there); Install to USB / console... hands the package to Console Setup; Open folder shows it; Save as default keeps the command. Only for a DLC given a profile ID does the install path differ: the program keeps DLC under 0000000000000000, where the test console keeps all 93 of its content packages, and X360PkgTool names the profile. CON signing needs key00033.bin / key00036.bin next to X360PkgTool, which the program never ships, so that choice stays BETA. |
| SW | UiSwap | this PC | 2026-11-19, release build, in the real window on a disc image of a real game (SpongeBob, built by extract-xiso from the game folder): Swap files from a folder... put two changed files (one in the root, one in Media\\EN_US) into a new image; extract-xiso lists both at their new sizes and takes them out exactly as they went in, all 147 other files come out unchanged, and the new image runs in the bundled Xenia. |
| CL | UiContents | this PC | 2026-11-19, release build, in the real window on copies of a real LIVE package (Minecraft avatar awards) and a console profile: on X360Packages and Profiles, Extract... writes each of the 10 and 66 files exactly as Extract all... does, every block checked against the package's own hashes; Verify integrity calls both real files intact and catches one changed byte in a copy; Profiles Open folder shows the profile and Save picture as... writes its own 64x64 tile_64.png byte for byte (Windows opens it as 64x64); GPD viewer > Export pictures... writes all 87, 76 and 67 pictures of three real game databases byte for byte against an XDBF reader written for the test, named by their IDs (this run found and fixed a space at the start of every exported name); XEX Tool > Bad Update project page opens https://github.com/grimdoomer/Xbox360BadUpdate, which answers 200 and is the Bad Update project. |
| SE | UiStfsEdit | this PC | 2026-11-19, release build, in the real window: on a copy of a real arcade package (Terraria, PIRS) X360Packages > Contents Replace... put in default.xex with a new Media ID, Add file... added a 300 KB file and Delete took it out again. After each one the bundled Xenia's own package reader started the game from the package (its log shows the replaced XEX's Media ID 1A2B3C4D, the game's code is in memory), and an STFS reader written for the test from the format found every file byte for byte (the others unchanged), every block's SHA-1, the hash tables up to the top hash and the header hash valid; a .bak was kept. Saves > Names > Apply names on a copy of a console save: X360PkgTool -p reads the new display and game names, only the 12 display-name language slots (as X360PkgTool -sn fills them), the game name and the header hash changed, and every hash is valid (its console signature needs the keyvault to be renewed; RGH / JTAG load it as it is). |
| ST | UiSettings2 | this PC | 2026-11-19, release build, in the real window, three starts sharing one settings file, against a loopback stand-in for the update address (signed with a test key): while the update prompt is dismissed the start does not show it; switching on Show the update prompt again writes that to the settings file, the next start shows the prompt once and the start after that does not. Back up files (.bak) before patching: off, Apply names changes a save and makes no .bak; on, it keeps the save as it was in a .bak byte for byte. Run tools in a visible console window: on, Full info runs X360PkgTool in a visible cmd window that holds its printout (not the log), and the tool file cannot be changed while that window is open (this run hardened that: the check now holds until the window closes); off, the printout is in the log again. Show the copy-games tip again: while marked shown a game-sized copy to the hard drive shows nothing; switched on, the next one shows the tip, once. Default tool commands: Enter saves each one, and the custom-command boxes on XEX Tool, Xbox ISOs and X360Packages start with them at the next start. |
| MO | UiMods2 | this PC | 2026-11-19, release build, in the real window, against the real licence server code running on this PC (its own test database) and Terraria running in the bundled Xenia, no console connected: Mod Maker's Build .x360mod... built a protected game mod (the payload cannot be read from the file) and its key card; the server registered the card and issued a token; Refresh listed the mod locked; Unlock my mods unlocked it through the server and remembered the token; Turn on wrote the option into the running game (Xenia's memory read directly: 00000000 -> MOD!), using Xenia by itself because no console was connected, and Peek Poker's target followed; Turn off put the original bytes back; Lock selected locked it and its options went away, Refresh kept it locked, Forget token took the token out of the settings file; Open mods folder showed the mods folder and Copy put this PC's id on the clipboard. This run moved Build's save dialog onto the program's shared picker (it had its own) and made Apply use the Xenia game when no console is connected. |
| CP | UiCreatePic | this PC | 2026-11-19, release build, in the real window on the details of a real console save: Create a save > Set picture... then Create package... puts the chosen image in the new package's header exactly as the package picture writer makes it (held to X360PkgTool -i in PK; 64x64, Windows opens it) and shows it in the card; No picture then Create package... leaves the header without a picture (size 0, area empty). X360PkgTool -p reads both packages and every hash of both is valid. |
| UP2 | UiUpdate2 | this PC | 2026-11-23, release build, in the real window against a loopback stand-in for the update address (test key): the title bar says Latest Version when the address answers without a notice (nothing newer released; since 2026-11-24, before it said No Release On The Update Server Yet), Update Notice Failed Its Check for a notice without a signature, Update Server Unreachable when it does not answer while the PC is online, Offline only when Windows says the PC has no internet, Update Notice Failed Its Check for a notice signed with another key, Latest Version for the same version. A newer version opens the update window, which shows the SHA-256 the signed notice gives and asks "Update to 4.9.9 now?" with Yes and No, downloading nothing before Yes; after the download it shows the downloaded file's SHA-256 (equal to the signed one) and every file with its own SHA-256 as added, replaced or unchanged, and asks "Install 4.9.9 now?" (saying what it replaces and adds) with Yes and No; that Yes (asking first when a file is open) closed the program, the helper checked the file again and replaced the portable copy's exe, added the new file and started the program again. With Ask me to install new versions off no window asks; Skip this version is remembered and Check for updates now offers it again; Open the GitHub page opens the releases page. Re-run 2026-11-24 for the Yes / No window (all modes, real portable install); the question and both answers were also checked to be fully on screen at 4K 200 %, at the room of 1366 x 768 and 1080p 150 % screens, and on a window far too short (the top then scrolls). |
| MS | UiMods3 | this PC | 2026-11-23, release build, in the real window against the licence Worker running locally (wrangler dev, test database): with no mod file on the PC, Unlock my mods fetched the licence's mod file from the server (the only place mod files come from), kept it only because its size and SHA-256 matched the server's list, and unlocked it; an up-to-date file is not fetched twice and a damaged copy is fetched again. Against the same server an attack battery (lic_attack.py, 32/0): no route can make a token or show a key (admin-like paths are 404), junk / made-up / SQL-injection tokens are refused after a pause, the bound machine gets its list, file and keys, and the same token from another machine is refused on every route and locks itself after 10 tries. |
| GH | UiGhidra | this PC | 2026-11-19, release build, in the real window on a copy of a real game XEX (Black Ops): To Ghidra... in the raw view and in the memory-address view writes the same five files (the program exactly as decoded, the map with every section, function start, import and export, the script, the batch file and the readme), refuses to overwrite an earlier export and leaves the XEX unchanged. Ghidra 12.0.4 then built the project from that folder with the batch file alone: it applied the map (16 sections, 14,704 functions, 544 imports), ran its full analysis, saved the project and the batch file said Done; a second run says the project is already built. Opened again read-only in Ghidra, the project has the PowerPC language, every section with its read/write/execute flags, the entry point and named system calls at the addresses the map gives (XamInputGetState, NtCreateFile ...). |
| X3 | UiXenia3 | this PC | 2026-11-19, release build, in the real window on a copy of a real game XEX (SpongeBob): Set (Title ID) and Set (Media ID) wrote 54510851 / 1A2B3C4D, and the bundled Xenia then loaded that XEX, reported exactly those IDs and held its code in memory at 0x82000000. |
| MI | UiMisc | this PC | 2026-11-19, release build, in the real window on copies of real console files (a profile, saves, a DLC, a title update): Profiles - Open folder shows the profile, Install to USB / console... hands it to Console Setup going where X360PkgTool says, Save picture as... writes the profile's own tile_64.png byte for byte; on Profiles and X360Packages Extract... writes every file exactly as Extract all... does, Verify integrity calls the real file intact and catches one changed byte, From a save or profile... takes the IDs exactly as the chosen header holds them; GPD viewer > Export pictures... writes every picture of a real game database (31) byte for byte; Console Setup > Install content: Add files lists a save, a profile, a DLC and a title update each going to the folder X360PkgTool names, Add folder adds every package of a folder and leaves other files out, Remove and Clear; Create a save: Copy details from a package... fills type, title ID, names and IDs exactly as X360PkgTool and the header give them, From a save or profile..., Add files, Remove and Clear. |
| TL | UiTitle | this PC | 2026-11-19, release build, in the real window against the public title database (dbox.tools): Look up with a Title ID (415608FC) gives the name the game's own XEX carries (Call of Duty: Ghosts) and keeps it on the PC; with part of a name ("ghosts") it lists the Xbox 360 games whose name holds it, 415608FC among them; an unknown Title ID says nobody knows it; double-clicking a result adds that game to the open profile. |
| FL | UiFlash | this PC | 2026-11-19, release build, in the real window run from a full copy of the program folder: J-Runner started the bundled JRunner.exe (its window "J-Runner with Extras" opened; it was closed again); Tools & drivers folder and Firmware folder open the program's own common and firmware folders, which hold the bundled files; Readme and Tutorial (PDF) open the bundled JungleFlasher.txt and Documentation\\jftut1.4.5.pdf; XeLL's Open in browser opens http:// and the address typed. |
| HX | UiHex | this PC | 2026-11-19, release build, in the real window on a copy of a real game XEX (SpongeBob): Save copy... in the raw view writes the XEX byte for byte, and in the memory-address view writes the decoded base file exactly as xextool -b does; Find next goes to the first and then the second place a 4-byte pattern is (counted independently); Replace all asks about exactly those places; Reload asks, then drops unsaved replacements; Save writes exactly the replacements and nothing else after keeping a .bak, and xextool still reads the XEX. |
| TU | UiTu | this PC | 2026-11-19, release build, in the real window on copies of a real game and its real title update read from the test console (Call of Duty Ghosts default.xex v0.0.0.5 and tu00000005_00000000): the game card shows the Title ID, Media ID and version xextool reads; Copy Title ID / Copy Media ID put xextool's values on the clipboard; Open XboxUnity opens xboxunity.net; the update card shows the Title ID, Media ID and install folder X360PkgTool reads, and what it updates (0.0.0.5 to 0.0.17.5); the check says it fits - and xextool really applies the update's own default.xexp to that game; More places... hands the update to Console Setup; Apply .xexp to the open XEX gives exactly the file xextool -p gives (v0.0.17.5), and a second press says it is merged in already. |
| INI2 | UiIni2 | this PC | 2026-11-19, release build, in the real window on the console's own launch.ini, xbdm.ini and JRPC.ini (copies read from it): xbdm.ini shows its three XBDM commands (dbgname Jtag, setcolor nosidecar, dumpmode smart) and JRPC.ini its blocks (KV Stealer Protection True, plugin1-3); each file loads and stays byte for byte the same; Change value rewrites only its own line in the file's own form (name="...", bare words, quoted pairs, True / False spelling kept) and refuses values a command does not take or a quote, saying why; Add setting adds a command as a new line, a pair at the end of its block, or for launch.ini a setting written as section.name = value; Add plugin: XBDM fills the first free plugin slot and a second press says it is there; Undo all changes gives back the original byte for byte; Use this text takes typed text; Save as writes exactly the edited file. Every change was compared with the exact text it must give. |
| LI | UiIniLive | real console (Trinity RGH3) | live on the Trinity RGH3, 2026-11-19, read only, one connection: Discover on the INI Editor tab found the console; Load from console for xbdm.ini and JRPC.ini gave exactly the files the console holds, with their settings listed. |
| LR | UiLights | real console (Trinity RGH3) | live on the Trinity RGH3 through the real window, 2026-11-19 00:19-00:28, one connection, JRPC2 loaded, with the user watching the console: All green, All red, All orange and All off each lit the whole ring that way; Apply with four different choices lit top left green, top right red, bottom left orange, bottom right off; Spin, Alternate and Flash each ran and Stop animation stopped each one; Apply set it back to top left green. The user confirmed every step by eye, and the console kept answering throughout. |
| SV | UiSaves | this PC | 2026-11-19, release build: the Saves page in the real window on copies of three real console-made saves and their profiles. The Save game card shows what X360PkgTool -p and the header itself say (IDs, versions, names, install path, signature type); Refresh reads it again; both Full info buttons put X360PkgTool's own printout in the log line for line; Open folder shows the save; Install save... hands it to Console Setup going to its own Content\\<profile>\\<game>\\00000001; Extract... writes every file exactly as Extract all... does, each block checked against the save's own hashes; Verify integrity calls the console's save intact and validly signed and catches a copy with one byte changed in a data block; From a save or profile... takes the profile and device IDs exactly as the chosen file's header holds them (three profiles, two saves); An Xbox hard drive... opens Drive Explorer's Saves tab. Browse saves on a folder laid out like a console drive: A folder on this PC... and Find saves list every save with its game, save name, title ID and profile (gamertag read from the profile beside it); Open in save editor opens a byte-for-byte copy kept in the Saves folder; Download... copies the saves out byte for byte; Replace with... puts the chosen file in place byte for byte after backing the old one up; Delete backs the save up byte for byte, then removes it. The originals were never touched. |
| XC2 | UiXc2 | this PC | 2026-11-18 night, release build, with the bundled Xenia: Add a folder put two console profiles and a console save into Xenia unpacked byte for byte; Open Xenia's content folder opened it; Find profiles listed both with their gamertags; Download copied a profile folder out file for file; Open in profile / save editor explained Xenia's folder form and opened the folder; a gamertag changed with Change gamertag on a copy of a console profile and put into Xenia with Replace with... was shown by Xenia itself (its log: Loaded X360GH Edit ... to slot 0); Delete backed a profile up and removed it, and Xenia then found one profile; Find saves listed the save with its game; Download copied it out; Replace with... put another console save in its place byte for byte; Delete backed it up and removed it. |
| PC1 | UiSettings | this PC | 2026-11-18 night, release build, in the real window with its own settings folder: every Home tile opens its page (Bulk extract ISOs on its Bulk extract tab) and the Console card tells the truth; Settings: Apply switches the whole window to the theme; New theme, Generate shades, Save, Edit, Save & apply, Duplicate, Cancel, Export / Import (every color the same) and Delete; Keep a log file writes logs.txt and stops again; the four Browse... buttons set folders and programs the program then uses (a backup landed in the chosen saves folder); Check now lists every bundled tool as verified and catches one changed by a single byte; Open tools folder, Open settings folder and Open log file open the right places; Forget saved FTP passwords, Forget keyvault, Clear recent files; Export settings without any password (not even encrypted) or the keyvault location, Import settings, Reset all settings (FTP profiles and recent files kept); SHA3-256 the same as Python's hashlib; the About links open their pages and each answers. |
| PC2 | UiStartup | real console (Trinity RGH3) | 2026-11-18 night: started the way the program starts itself, with the Settings startup choices on: it opened on the page used last, with the activity log open, ran the update check (the title showed what it found) and connected to the console IP by itself (the real console). |
| SI | UiSignIn | this PC | 2026-11-18 night, release build, with the bundled Xenia: with nobody signed in, Run working file asked first; a gamertag Xenia would refuse was refused with Xenia's own rule; Create and start made a profile whose Account file is byte for byte what Xenia's own EncryptAccountFile writes, and Xenia found it and signed in to it (its log); once signed in nothing was asked; Sign in and start signed Xenia in to the profile picked from the list; Start without signing in started the game with nobody signed in; Cancel started nothing; with no Xenia settings file yet, the sign-in was written and Xenia kept it. Every start of a game in Xenia goes through this step. The user then used it in their own copy of the program (2026-11-18) and confirmed it works. |
| LL | UiLaunch | real console (Trinity RGH3) | live on the Trinity RGH3 through the real window, 2026-11-18 night: Launch XEX asked first, then the console ran the chosen XEX - SpongeBob from Hdd:\\RGH Games (three times, from the dashboard and from Aurora) and Aurora from Hdd:\\RGH Apps - proven by the title ID and the path of the running XEX, read over XBDM. |
| LP | UiPower | real console (Trinity RGH3) | live on the Trinity RGH3 through the real window, 2026-11-18 night: Reboot (cold) asked first, then the console rebooted - the running game was gone and the dashboard answered again about 45 seconds later (the user saw it reboot); Restart title (warm) started the running game again from its file - a mark written into its memory beforehand was gone and the same game ran again. |
| LDB2 | UiLiveDbg | real console (Trinity RGH3) | live on the Trinity RGH3 through the real window, 2026-11-18 night, with a game running (SpongeBob): Discover; the thread list matched the console's own (31 threads, start addresses) and Debug this thread picked one; the module list matched every loaded module, a module's sections matched, Dump module to file saved exactly the module's memory; Go showed code word for word as the console's memory holds it, Follow went to a branch's destination; Break held a thread and PC went back to it; Step into on a thread parked in the kernel asked first, and Cancel left it alone; Add put a breakpoint on the game's VdSwap call stub and the game ran into it; Step into and Step over then ran one game instruction each; Run to return let the system function run and stopped back in the game exactly at the return address; Run to selected stopped exactly on the chosen line; Trace recorded 5 instructions, all in the game; Suspend and Resume moved a game thread's suspend count up by one and back; Continue let the game run on - the console answered throughout. |
| LF2 | UiLive (ftp) | real console (Trinity RGH3) | live on the Trinity RGH3 through the real window against Aurora's FTP server, 2026-11-18 night: Help, Save (a connection profile), New folder, Upload files and Upload folder (read back over XBDM byte for byte - the console's two servers agree), Download (a file and a folder, byte for byte), Rename, Send game folder (into the folder asked for) and Delete (a folder with everything in it) - in a test folder only, removed afterwards. |
| LPK | UiLive (peek) | real console (Trinity RGH3) | live on the Trinity RGH3 through the real window, 2026-11-18 night: Peek, Poke changes, Dump to file, and every value type written and read back, in the running program's memory (its unused DOS-stub text) - each checked against the console's memory read separately; the original bytes put back exactly. |
| GP | GodParity | this PC | 2026-11-24, against the tools themselves: ISO2God 1.3.6's own conversion code (loaded from ISO2God.exe and called directly) and god2iso 1.0.5 (its published conversion, with its XSF template). Microsoft's own Crysis package (26 data files, 4.4 GB) -> disc image identical to god2iso's except the tool name at offset 31337 (4,370,477,056 bytes, 137 folder entries moved 4,124 sectors), the same 110 files as the every-sector-in-place image; that image -> packages with None, Partial and Full padding removal: every data file byte for byte ISO2God's, the Full rebuilt image byte for byte ISO2God's (4,370,481,152 bytes), both packages back to the original files. The same on the Crackdown 2 test image, its XGD2 layout and an original Xbox image around NFSU2's real XBE (its picture decoded like ISO2God's, within one colour step); the details read from each game (title / media ID, disc, platform, type, picture bytes, name) as ISO2God reads them. Headers differ only where they follow Microsoft's own packages (version fields, license table, transfer flags, no ISO2God description) and where ISO2God leaves a one-file package's root hash empty (a bug). |
| GT | GodTargetTests | this PC | 2026-11-24: packages made for RGH and for a devkit from a disc image, from a game folder (XEX version) and from an original Xbox folder (90 checks): every XEX in the package byte for byte what xextool -r mrbdiyvklc -m r / -m d (or -m d alone) makes of the original, every other file the original's, the header byte for byte what X360PkgTool -z -b 1 -m 1 / -m 0 makes of the same package run directly (devkit form signed), every hash checked. |
| UG | UiGod | this PC | 2026-11-24, in the real window on copies: ISO → GOD - options set in the controls, Add images (the Crackdown 2 test image) and Add game folder (NFSU2's original Xbox folder) read name / title ID / disc from the games, Edit changed the name, Convert all made both for RGH with the XEX and the package unlocked (checked as in GT), When a package is done > USB drive copied it into <USB>\\Content\\0000000000000000\\<title ID>\\00007000 byte for byte; GOD → ISO - Add packages, Convert all wrote "<name>.iso" with the game's files; This image - Convert to Games on Demand made a devkit package next to the image in one click; the options were saved (31 checks). |
| AB | AbgxProof | this PC | 2026-11-24, abgx360 built in, against the installed Hadzz abgx360 1.0.7 and the abgx360 GUI's own source (651 checks): the installed abgx360.exe is byte for byte the one in the bundled Hadzz installer; every option the front end can put on a command line is one abgx360 1.0.7 itself reads (its own 86 option names); everything "abgx360 --help" lists, and everything the abgx360 GUI's command builder can emit, can be produced (the GUI's --splitvid / --removesplitvid / --e-videopartition, which 1.0.7 no longer reads, left out); file names and patterns always quoted (abgx360's MinGW start-up expands * otherwise - shown); the fix file written into a sandbox APPDATA, and abgx360 --debug shows it takes every address from that file; a fix file pointing at the dead abgx360.net replaced (kept as .bak), one for another server left alone; the installer's 2 video files copied with their CRC-32 checked; the report shown in the program, turned to text, is line for line abgx360's own --stripcolors output in 7 option sets (achievements and avatar awards included); its questions answered through its input (y, n, two in a row); Stop ends it at once; online, through the fix file, abgx360 downloaded abgx360.dat and GameNameLookup.csv from abgx360.hadzz.com. |
| CV | CliConvertTests | this PC | 2026-11-24, through the real exe (cli convert, 29 checks): a disc image, a Games on Demand package and a game folder each made into a package, a disc image and a folder - every result's files byte for byte the source's, every package passing its own hash check; for RGH / a devkit every root XEX byte for byte what xextool -r mrbdiyvklc -m r / -m d makes of it and the package unlocked for a devkit by X360PkgTool; an original Xbox image to a folder, to a package (00005000) and back; existing outputs, a result inside its source, stealth= with a folder, unknown formats and options refused with nothing written; the sources never changed. |
| UC | UiConvertAbgx | this PC | 2026-11-24, in the real window on copies (59 checks): Convert tab - an image, a package and a game folder in one list (From and title ID read), Convert all to disc images (each with the game's files byte for byte; the stealth step says why made-here images are left as they are), to XEX version folders for RGH (default.xex exactly xextool's unlock), to Games on Demand after Edit renamed the game (package in Content\\0000000000000000\\4D5308BC\\00007000 under its ISO2God name, every hash valid, the new name in its header), Clear finished; This image - Convert to Games on Demand switched the tab and made the package next to the image; Browse contents of a package (its disc's root) and of an image (type, sector, byte offset, attributes), Find over the whole image, Properties (title ID, media ID, allowed media), Extract everything byte for byte; abgx360 tab - Verify only with the report shown here in abgx360's colours, Save the report as abgx360's HTML page and as text, Open it in the browser (a script-free copy), Set up abgx360 (fix file and video files in a sandbox APPDATA), a folder with Only files like *.xex; the settings remembered; the originals never changed. |
| NT | NandTests | this PC | 2026-11-24, against J-Runner itself (its own reader in JRunner.exe, loaded and called directly) on all 71 flash dumps on this PC - Falcon, Jasper small block and big block, Trinity, Corona 4 GB; originals, glitch images, RGH 1.2 and an RGH3 Corona (8,084 checks, 0 failed): the board J-Runner names, the data without spare bytes, every boot stage and patch slot, the built-in 1BL key is J-Runner's own, every stage decrypted byte for byte the same as J-Runner's decrypt_CB / _CD / _CE / _CF / _CG (and with the CPU key where the console pairs them), the lockdown value and pairing data of the CB and of each patch slot, the SMC and its version, J-Runner's SMC test (its verdict shown only for Xenon to Jasper SMCs, where it tells every glitch image from every original), the SMC config field by field with its checksum, the keyvault as stored, the CPU key check, every keyvault field and the decrypted keyvault, the console ID as the console shows it, fcrt.bin decrypted (27 dumps), the file-system root and every file's bytes (fragmented files by their own XEX block hashes, which J-Runner reads wrongly), the bad blocks. |
| JX | NandExtractsCheck | this PC | 2026-11-24: the Extract files output J-Runner itself wrote on this PC (its Extracts-<serial> folders): 33 files - SMC_en / SMC_dec, KV_en / KV_dec, smc_config.bin, fcrt_en / fcrt_dec - are byte for byte what this page saves from the dumps beside them (8 more came from dumps no longer on the PC). |
| NW | NandWriteTests | this PC | 2026-11-24, against J-Runner's own reader on all 62 flash dumps (16 MB small block and big-block Jasper; 1,460 checks, 0 failed): replacing bootanim.xex, or the keyvault, with its own bytes gives a byte-for-byte identical dump; a smaller and a same-size-changed bootanim, and a changed keyvault, read back correctly through our reader - every other file byte for byte unchanged, every page's EDC valid (no new errors), the boot chain / SMC / keyvault untouched, and only the file's own blocks (or the keyvault's block) and the root different from the original - and through J-Runner's own reader where its reader is a reference (same root, unfragmented); a bootanim that needs more blocks is refused. Not flashed to a real console yet. |
| UN | UiNand | this PC | 2026-11-24, in the real window on copies of five real dumps (a Falcon and a Jasper big block with cpukey.txt beside them, a Corona 4 GB, a Trinity with fcrt.bin and its CPU key, an RGH3 Corona 4 GB; 215 checks): recognised by content (also the RGH3 image whose header text is not Microsoft's); board, notes, boot chain, how each stage decrypted, lockdown values, SMC, SMC config and checksum shown as read; every Save button (as stored and decrypted) wrote exactly the engine's bytes (which NT proves against J-Runner), and a stage that needs the CPU key is refused with a message when it is not there; the keyvault unlocked by itself from cpukey.txt, a wrong key refused, the right one typed in unlocked it again, and unlocking decrypted the console-paired stages; Extract selected / all wrote each file exactly; an older root lists its own files; Open in X360GameHack opened a flash XEX; the dumps were never changed and nothing of the keyvault reached the activity log. |
| XG | Xenia (Crysis) | this PC | 2026-11-24: Crysis from Microsoft's package -> disc image -> Games on Demand with the command line (Full, for RGH: XEX unlocked by xextool, package by X360PkgTool) started in the bundled Xenia Canary: the package mounted (26 data files), the unlocked default.xex loaded and the game ran to its menu - the user saw it. Loading the campaign stopped in Xenia exactly the same way with Microsoft's untouched package (same file reads, "Stub XFileSectorInformation") - an emulator limit, not the package. |
| XS | XeniaSaveTests | this PC | 2026-11-25 (again, after the header fix), debug build, against a scratch Xenia folder on copies of real console saves and title updates, read back with the rules of the BUNDLED Xenia re-written from its source at that very build (02d2cb5cc: ReadContentHeaderFile = XCONTENT_AGGREGATE_DATA, ListContent = folders only, ResolvePackagePath, FindTitleUpdate): Install a save puts each of 4 saves from 4 consoles into content\\{profile}\\{title}\\00000001\\{name} for the chosen profile, every file byte for byte, with a 0x14C-byte header file (device 1, type, the save's own display name, its file name, the chosen profile, title, licence mask) - the game lists it for that profile and Xenia opens that very folder; control: a header in a newer Xenia's form (what earlier versions wrote) gives this Xenia a garbage file name. Same save again = nothing changes; another save of that name = backed up first, then replaced (files compared, not only the header). Title updates (Halo 3 #3, WaW #7) and DLC go in unpacked into folders where FindTitleUpdate / ListContent find them. Profiles, junk and no profile refused. Archives: names that try to leave the folder and a file bigger than it says refused; .7z through 7-Zip; a password-locked archive fails at once. The games list finds the user's copied games. 93 checks. |
| UX | UiXeniaPage | this PC | 2026-11-25, debug build, in the real window at the user's size (1145 x 886 at 200%), scratch settings and Xenia: the sidebar's Xenia page with Games / Saves / Profiles / Xenia Patches / Setup; the games list (3 games, titles, kinds, sizes); Install a save... for a selected game from a downloaded-style .zip (its save went in for the signed-in profile, another game's save in the same .zip was left out and named); Install a save... on the Saves tab; Add a game... and Remove... (taken off the list, file kept; on a copy it asks first - answered No, nothing deleted); Create a profile... (Xenia's way) and Sign in to selected; Play reached Xenia's start (stopped there: Xenia was not started); the xemu card on the Original Xbox page; BETA marks as listed here. 35 checks. |
| CX | UiCopyToXenia | this PC | 2026-11-25 (again, after the header fix), debug build, in the real window on the user's own Xbox drive image (250 GB, opened read only): Drive Explorer > Content\\0000000000000000\\4156081C > the DLC and title-update folders > Copy to Xenia put WaW's 2 map packs into a scratch Xenia's content\\0000000000000000\\4156081C\\00000002 and its title update ("Title Update #7") into ...\\000B0000, each unpacked into a folder the way the bundled Xenia keeps it (every file byte for byte the package's, a 0x14C-byte header naming it, owner 0); a dashboard "Title Content Progress cache" that sat in the DLC folder was left out with a message naming it; the temporary copy was removed; the image file was not changed. 10 checks. |
| IP | UiIdPicker | this PC | 2026-11-25, debug build, in the real window on copies of real console files and the user's own Xbox drive image (read only): Rehash & resign > Pick the IDs looks by itself when the tab opens and lists the profile kept in the Saves folder, a console profile copied into Xenia (its header beside it) and every profile on the drive, each with exactly the profile, device and console ID its header holds (read independently: own FATX listing and header offsets); the drive's device ID is the one most of its console-signed files carry (23 of 26) and is the drive's own serial number from its security sector; Use these IDs fills Rehash & resign and names the IDs that differ; Rehash & resign then writes exactly those IDs and every hash verifies (the original kept as .bak); A USB stick or folder... adds a folder laid out like a console drive (its profile joins the Xenia row, its drive row has the majority device ID, 2 of 4) and explains an Xbox-formatted USB stick; a drive row gives only its device ID; with a keyvault in use the text says one press makes the save valid on any console. Profiles and X360Packages have the same card (28 checks). No console was connected during the test: a console's hard drive and USB sticks are read the way Neighborhood > Profiles reads them, which was tested on the console. |
| XK | XeniaConfigTests + XeniaAccept | this PC | 2026-11-25, debug build, on copies of the settings file the bundled Xenia Canary wrote: all 265 settings read with Xenia's own description and choices; a change rewrites only that value (every other line byte for byte the same, comments in their column, UTF-8 / LF kept); texts escaped and read back exactly, decimals always with a point; a missing setting or section added where it belongs; a missing file created; the original kept once and put back byte for byte; wrong values refused (30 checks). Then the bundled Xenia itself, started without a game on a scratch storage folder and closed normally: its own config dump showed all 13 changed values loaded (2x scale, VSync off, 120 fps, 16x filtering, fast readback, FSR, FXAA, FSR 0.5, CAS 1.0, dead zone 0.15, full licence, German) and it wrote them back; from a file holding only 6 values it wrote all 265 settings and kept the 6 (6 checks). |
| XU | UiXeniaSettings | this PC | 2026-11-25, debug build, in the real window on a scratch Xenia whose settings file is a copy of the bundled Xenia's: Games > Graphics settings... opens the Settings tab; every card shows what the file holds (a value written 0.20000000298023224 shows as 0.2; one set by hand shows as itself); resolution scale, upscaling filter, FSR sharpness, anti-aliasing, texture filtering, frame rate, VSync, render targets, dead zone, licence and language each land in the file exactly as Xenia writes them; Every Xenia setting lists all 265, searches, shows Xenia's description, sets a number, a true / false, a text and offers exactly Xenia's choices, and refuses a wrong value; only the changed lines differ (550 others byte for byte); Put back my old settings... makes the file byte for byte what it was; with no settings file yet it says so and makes one; the quick-setup buttons set 4K (3x) / Sharper (2x) - scale, FSR, FXAA, 16x filtering - and Xenia's defaults put every graphics choice back, nothing else touched; a value written the same (0.2 for 0.20000000298023224) is left as it is; while Xenia runs a change is refused with the reason and the file is not touched - the bundled Xenia writes its settings file when it closes (measured: a value changed while it ran was back to the old one after it closed) (33 checks). No game has been played with changed settings yet. |
| XR | XeniaRepairTry | this PC | 2026-11-25, debug build, on a scratch copy of what earlier versions left in the user's Xenia (WaW's save with a header in a newer Xenia's form, Title Update #7 copied as the package file): XeniaContent.Repair - run before every Play - rewrote the header in the bundled Xenia's form (device 1, type 1, file savegame.svg, the profile, title 4156081C) after copying the old one to Saves\\Backups byte for byte, left the save's data untouched, unpacked the title update into a folder with its header (type 000B0000, owner 0) after moving the package file to Saves\\Backups byte for byte, and found nothing to do a second time; a failed unpack puts the package file back (seen when a library was missing). 8 checks. |
| XL | the bundled Xenia | this PC | 2026-11-25: the bundled Xenia Canary (02d2cb5cc) itself, started on scratch folders with the user's WaW (PXv2 default.xex), the user's Xenia profile and content put in by this program, its log at debug level: Title Update #7 unpacked by this program was found and applied ("XEX patch applied successfully: base version: 0.0.0.8, new version: 0.0.7.8"; left as the package file it was never used); the game listed both map packs ("Adding: Call of Duty: WaW - Map Pack 2 / 3") and mounted them (mappack1:, mappack2:); and it opened the save (XamContentCreateEx save, NtCreateFile save:\\savegame.svg). The map packs in the user's own Xenia are byte for byte the packages' files (1,530 and 3,322). No one has played with them yet. |
| BT | UiBetaTips | this PC | 2026-11-25, debug build, every page and every tab of the real window: each of the 159 controls whose tip speaks of BETA shows a BETA label (itself, its card, its tab or its page); on every switch and drop-down with unproven choices (18 choices) the tip speaks of BETA exactly while an unproven choice is selected, and names it. |
| XE | UiXeniaExtras | this PC | 2026-11-25, debug build, in the real window with the user's own Xbox drive image open (read only) in Drive Explorer and his WaW on a scratch Xenia's games list: with no drive open it says where to look; one press of DLC / updates from my Xbox put WaW's newest title update (#7) and both map packs into Xenia unpacked the way it keeps them (all 4,859 files byte for byte the drive's, headers), left the dashboard's file out, moved an older title update that was there to Saves\\Backups (Xenia applies the first one it finds) and said so; a second press said Xenia has them already; the temporary copy was removed; the image was not changed (14 checks). From a connected console it reads the same folders; not run with a console. |
| SP | UiSaveWithProfile | this PC | 2026-11-25, debug build, in the real window on a copy of the user's own download (a WaW save and the profile it belongs to, laid out as on a USB stick) into scratch Xenias: Install saves from a folder... saw the profile and asked; "keep them with that profile" put the profile into Xenia in its own form, the save under it (header owner = that profile) and made Xenia sign in to it, saying so and how to switch back; "give them to one of my profiles" put the save under the user's profile and left the other one out, saying so; picking just the save file (Install a save...) found the profile beside it in the download the same way (13 checks). |
| OB | OgBigDriveTests | this PC | 2026-11-25, debug build: big original Xbox drives, on sparse images exactly the size of the user's 1 TB WDC WD10EZRX (1,953,525,168 sectors), each partition formatted the original Xbox way with a file in it: (A) no table, LBA48 standard - F: ends at sector 0x0FFFFFFF (120 GiB, 16 KB clusters) and G: is the rest (803 GiB, 64 KB clusters); (B) XBpartitioner's "****PARTINFO****" table at sector 0 with its own F: (400 GiB, 32 KB) and G: (524 GiB, 64 KB); (C) F: takes the whole rest (924 GiB, 64 KB) - every partition found where it is, at its size and cluster size, every file read back byte for byte, no partition too many; (D) the user's Xbox 360 drive image still opens exactly as before (24 checks). The FATX writer (70,993), installer (11) and compatibility-partition (12) suites and every page (133) pass on the same build. Not yet tried on the user's real 1 TB drive. |
| FMT | DriveFormatTests + UiMakeDrive | this PC | 2026-11-25, debug build, on sparse IMAGE FILES only (never a real drive): Make Xbox drive. Original Xbox, the size of the user's 1 TB WD10EZRX, in all 5 layouts (F: to 137 GB + G:, F: takes all, F: only to 137 GB, F: of a chosen size + G:, stock): every partition where XBpartitioner (opcow/XBpartitioner, read for this) puts it, at its size and cluster size (16 KB doubling every 256 GB), its table at sector 0 byte for byte, headers byte for byte, files written to every partition read back and checked. Xbox 360, the size of the user's Hitachi: every header equal to the user's real drive's (volume id aside, "XLID" included), every partition's data starting exactly where the real drive's does, nothing written in front of SysExt. Conversions both ways leave nothing of the old layout to mistake the drive for. Through the Disk Reader's format mode over a real pipe: a blank drive made; writes to the 360's security sector, caches and (anything but zeros) sector 0 refused, the original Xbox's 4-512 KB refused; a used 360 drive formatted and UNDONE back to every byte; a session journaled the old way still undone (79 checks). In the real window (hidden): new 1 TB and 250 GB images made and opened, an open image made again with F: of a chosen size, an F: that does not fit refused in the dialog, a real drive not offered by a copy that is not installed (14 checks). Real-world check, 2026-11-25 (read only, the installed build): the user's own 1 TB original Xbox drive, set up on his console, has XBpartitioner's table and the F: takes all layout - for a drive of its exact size the plan gives every partition the same place, size, cluster size and cluster count as on that drive (E: C: Y: Z: 16 KB, F: 924 GB 64 KB, 15,139,782 clusters), and the same table labels; only his X: carries 64 KB clusters (reformatted later by something else - a console's own is 16 KB). Not yet used in a console or emulator; never run on a real drive. |
| SEC | SecurityHardeningTests + worker-test | this PC | 2026-11-25, debug build, without administrator rights and touching no disk: the Disk Reader is now a native launcher (what Windows elevates - not .NET, refused without elevation by Windows, named in the prompt) that starts the .NET helper with ONLY 7 clean variables (a polluted environment - profiler, AppDomain manager, COMPlus / DOTNET, planted PATH / SystemRoot - never reaches it), passes arguments and exit code through, refuses while a .NET runtime option is set in the registry, and ends the helper when it ends (job object); the helper does nothing unless that launcher started it, refuses any disk with a Windows volume, refuses an undo folder that is a junction or belongs to a user; the program accepts only that helper on its pipe, opens only documents and http(s) links; the console notification listener serves only the console's address (strangers turned away, it gives up after 32) and ends on a line longer than any console sends (34 checks). The Disk Reader's write-mode (25), compatibility-partition + undo on the user's real drive image (12) and devkit (8) suites pass on the same build. The licence and update servers, run locally only (wrangler dev on test copies with test data, nothing deployed), flooded with every database call counted: 40 junk and malformed requests cost no database work at all; 2,000 made-up licence tokens from 2,000 addresses cost one read each and no write; past the per-address limit (an IPv6 /64 counts as one sender) a request costs nothing; a real token stops at 30 a minute from any number of addresses; a token tried on another machine locks itself after 10 tries and then costs no writes; one licence downloads at most its daily number of mod files; the public mod list is answered from memory (119 requests, 0 reads); 406 opens cost one database write and every one was counted; the request log keeps 90 days (31 checks). |
| PT | UiPartitions + disk-run | this PC | 2026-11-25, debug build, in the real window on drive images laid out the ways real drives are: an original Xbox drive with F: up to the 137 GB line and G: the rest shows all seven partitions (X: Y: Z: C: E: F: G:) in the order they sit on the drive, each with its size, file system, cluster size, start and free space, and the map shows every one; clicking a partition (or Enter on it) opens it in Files, and the one shown there is outlined; a partition in the drive's table whose header is gone is listed with why (not formatted) and clicking it explains instead of opening; the stock layout on a bigger drive shows the space past E: (with its table: in no partition; without one: the kernel's F: place, not formatted); an Xbox 360 drive shows both caches (the console's own data, not files), SysExt, SysExt2, the compatibility partition and Data; a single partition dump is just itself (18 checks). On the user's real 1 TB original Xbox drive (installed build, read only, 0 blocks written): the same layout code lists X: Y: Z: C: E: F: (924.06 GB) - every partition of its XBpartitioner table, all read, nothing left over. |
| HT | UiHomeTiles | this PC | 2026-11-25, debug build, in the real window: every Home tile opens its page - Bulk extract ISOs, Neighborhood, Peek Poker, Install to USB / console, Xbox hard drives (Drive Explorer), Original Xbox games (Original Xbox (OG)), FTP, Q & A; the drop box names Xbox drive images and is tall enough for all its lines (also when a narrow window wraps them), and a drive image handed over the way the drop box hands it opens in Drive Explorer with all its partitions on the Partitions tab; in the side menu the red BRICKS POSSIBLE pill sits on Flash Dumps only (15 checks). |
| DU | the user, by hand + the drive suites | this PC | 2026-11-25/26, the user himself in the installed program with his own real drives (original Xbox WDC 1 TB and Xbox 360): opened them, read their files, dragged files out to Windows and copied files onto the drive with changes allowed - his verdict: everything works. Behind it: the drive suites in the real window on drive images (UiDrive, UiDriveFind, UiDriveMenu, UiDragOut, UiPartitions, UiOgSaves, UiOgNames, UiXemuPlay, UiMakeDrive, FreeCountTests) and the write / undo runs on both real drives through the same Disk Reader (DiskRun). Making a drive (formatting) has only ever been run on image files. |
| OS | UiModsDrive + mod-sign | this PC | 2026-11-25, debug build: owner-only mods. The owner's signing key was made on the owner's PC (DPAPI, never printed); cli mod-sign signed the 14 real mod packs (+72-byte signature, .bak kept) and says so when a pack is already signed; Node's own crypto on the licence admin side verifies the same signatures (publish / add-pack refuse an unsigned pack). In the real window: the mods folder held the signed WaW pack, an unsigned copy and a signed copy changed by one byte - only the signed one is a mod, the other two are refused and cannot be activated (nothing sent to the server); Mod Maker / mod-build sign what they make; with the key taken away nothing is built and the Mods page has no Mod Maker tab. All mods read the public catalog from the real licence Worker code running locally (wrangler dev, its test database): 14 mods, 176 options, statuses, details, the Get a licence link; the catalog and its saved copy hold names and descriptions only (checked for steps / inputs / ops / ddl / keys / addresses). Drive Explorer's grouped bar (10% of the page): Drive tools folds out and back, the 6 change buttons show only while changes are allowed, all 21 actions present. 24 checks. Not yet against the live server (not deployed). |

Nothing here was tested on original Xbox hardware, on a retail (unmodified) console, or with a keyvault loaded.

## Summary by page

| Page | Tested buttons | BETA buttons | Label in the side menu |
|---|---|---|---|
| Home | 2 | 0 | none |
| XEX Tool | 30 | 0 | none |
| XEX Disassembler | 10 | 0 | none |
| Title Updates | 9 | 1 | none |
| X360Packages | 31 | 5 | none |
| Xbox ISOs | 32 | 11 | none |
| Original Xbox (OG) | 10 | 12 | none |
| Xenia | 32 | 12 | none |
| Saves | 30 | 19 | none |
| Profiles | 13 | 32 | none |
| Drive Explorer | 27 | 0 | none |
| Neighborhood | 32 | 3 | none |
| Console | 29 | 6 | none |
| Peek Poker | 19 | 0 | none |
| Debugger | 21 | 7 | none |
| FTP | 11 | 2 | none |
| Console Setup | 29 | 6 | none |
| Flash Tools | 6 | 5 | none |
| Flash Dumps | 15 | 3 | none |
| Mods | 12 | 4 | none |
| Q & A | (help text, never labelled) | - | none |
| Settings | 30 | 0 | none |

## Page by page

### Home

- *Bulk extract ISOs* - TESTED (PC1)
- *Neighborhood* - TESTED (PC1)
- *Peek Poker* - TESTED (PC1)
- *Install to USB / console* - TESTED (PC1)
- *Xbox hard drives* - TESTED (HT)
- *Original Xbox games* - TESTED (HT)
- *FTP* - TESTED (PC1)
- *Q & A* - TESTED (PC1)
- *Recent files* - information only
- *Console* - TESTED (PC1)
  - Connect over XBDM - TESTED (PC1)
  - Console info - TESTED (PC1)

### XEX Tool

**Patch** tab

- *Executable* - TESTED (XI)
  - Refresh - TESTED (XI)
  - Full info - TESTED (XI)
- *One-click patches* - TESTED (XI)
  - Retail / RGH / exploited - TESTED (XI)
  - Prototype / devkit build to RGH - TESTED (XI)
  - Devkit - TESTED (XI)
  - Devkit, self-contained - TESTED (XI)
  - XDK debug-ready - TESTED (XI)
- *Custom patch builder* - TESTED (XI)
  - Apply patch - TESTED (XI)

**Encryption & IDs** tab

- *Encryption & compression* - TESTED (XI)
  - Decrypt + uncompress - TESTED (XI)
  - Encrypt + compress - TESTED (XI)
  - Decrypt - TESTED (XI)
  - Encrypt - TESTED (XI)
  - Uncompress - TESTED (XI)
  - Compress - TESTED (XI)
- *Title ID & Media ID* - TESTED
  - Set - TESTED (XI, X3)
  - Set (2) - TESTED (XI, X3)

**Advanced** tab

- *Bad Update / Bad Avatar* - TESTED
  - Patch this XEX for Bad Update / Bad Avatar - TESTED (XI)
  - Many discs at once - TESTED (XI)
  - Unlock DLC - TESTED (XI)
  - Bad Update project page - TESTED (CL)
- *Custom xextool command* - TESTED (XI)
  - Run - TESTED (XI)
  - Save as default - TESTED (XI)
- *Export & extras* - TESTED (XI)
  - Export resources... - TESTED (XI)
  - Save base file... - TESTED (XI)
  - Save IDA .idc... - TESTED (XI)
  - XML info to log - TESTED (XI)
  - List special patches - TESTED (XI)
  - Make backup copy - TESTED (XI)
  - Open folder - TESTED (XI)
  - Open in XEX Disassembler - TESTED (XI)

### XEX Disassembler

- Save copy... - TESTED (HX)
- To Ghidra... - TESTED (GH)
- Jump - TESTED (D4, D6)
- Reload - TESTED (HX)
- Save - TESTED (HX)
- Find next - TESTED (HX)
- Find all - TESTED (D4)
- Replace all - TESTED (HX)
- Disassemble - TESTED (D4, D6)
- Decompile - TESTED (D6)

### Title Updates

**Check & install** tab

- *1.  Your game* - TESTED (TU)
  - Choose the game... - TESTED (TU)
  - Copy Title ID - TESTED (TU)
  - Copy Media ID - TESTED (TU)
  - Open XboxUnity - TESTED (TU)
- *2.  The title update* - TESTED (TU)
  - Choose the title update... - TESTED (TU, LT)
- *3.  Does it fit? Install it* - what it shows is TESTED (TU); its buttons as marked
  - Install on the console - TESTED (LT)
  - Install on a USB drive - BETA
  - More places (FTP, other drives)... - TESTED (TU)

**How updates work** tab

- *What a title update is* - information only
- *Which one fits your game* - information only
- *Where it goes on the console* - information only
- *What the console does with it* - information only
- *Do not break the match* - information only
- *If the update does not load* - information only

**Merge into the XEX** tab

- *Merge the update into the game's XEX* - TESTED
  - Extract a title update... - TESTED (D6)
  - Apply .xexp to the open XEX - TESTED (TU)

### X360Packages

**Overview** tab

- *Package* - TESTED (PK)
  - Refresh - TESTED (PK)
  - Full info - TESTED (PK)
- *Quick actions* - TESTED (PK)
  - Unlock DLC for RGH / exploited - TESTED (PK)
  - Unlock DLC for devkit - TESTED (PK)
  - Make retail / RGH - TESTED (PK)
  - Make devkit - TESTED (PK)
  - Make bought - TESTED (PK)
  - Make trial - TESTED (PK)
  - Remove purchase traces - TESTED (PK)
  - Install to USB / console... - TESTED (PK)
  - Open folder - TESTED (PK)

**Contents** tab

- *Contents* - TESTED (D3)
  - Extract... - TESTED (MI, CL)
  - Extract all... - TESTED (D3, PKG)
  - View GPD - TESTED (D3)
  - Replace... - TESTED (SE)
  - Add file... - TESTED (SE)
  - Delete - TESTED (SE)
  - Verify integrity - TESTED (MI, CL)

**Edit** tab

- *Metadata* - TESTED
  - Apply - TESTED (PK)
  - Set picture... - TESTED (PK)
  - Set game picture... - TESTED (PK)
- *Creator IDs* - TESTED
  - Set - TESTED (PK)
  - Set (2) - TESTED (PK)
  - Set (3) - TESTED (PK)
- *Signing & license flags* - TESTED
  - Apply flags - TESTED (PK)
  - choice "CON" - BETA (this one choice is not proven)

**Rehash & resign** tab

- *Rehash & resign* - partly tested (1 of 6 buttons)
  - From a save or profile... - TESTED (MI)
  - Rehash & resign - BETA
  - Put back - BETA
  - Many files... - BETA
  - Load keyvault... - BETA
  - Forget - BETA
- *Pick the IDs* - TESTED (IP)
  - Find my profiles - TESTED (IP)
  - A USB stick or folder... - TESTED (IP)
  - Use these IDs - TESTED (IP)

**Advanced** tab

- *Custom X360PkgTool command* - TESTED
  - Run - TESTED (PK)
  - Save as default - TESTED (PK)

### Xbox ISOs

**This image** tab

- *This disc image* - TESTED (XI)
  - Extract... - TESTED (XI)
  - Add to bulk list - TESTED (XI)
  - Optimize / rewrite... - TESTED (XI)
  - Convert to Games on Demand - TESTED (UC, UG, XG)
  - Play in Xenia - TESTED (EM)
- *Browse contents* - TESTED (XI, UC)
  - Up - TESTED (XI, UC)
  - Properties - TESTED (XI, UC)
  - Clear - TESTED (XI, UC)
  - Extract selected... - TESTED (XI, UC)
  - Extract everything... - TESTED (XI, UC)
  - Open in X360GameHack - TESTED (XI, UC)
- *Test a mod: swap files inside the image* - TESTED
  - Swap the selected file... - TESTED (XI, EM)
  - Swap files from a folder... - TESTED (SW)
  - Play the new image in Xenia - TESTED (EM)

**Bulk extract** tab

- *Bulk extract + patch* - TESTED (XI), except the choice(s) marked below
  - Add ISOs - TESTED (XI)
  - Remove - TESTED (XI)
  - Clear - TESTED (XI)
  - Extract all - TESTED (XI)
  - Open destination - TESTED (XI)
  - choice "Install content packages found on the discs to the USB drive" - BETA (this one choice is not proven)

**Convert** tab

- *Convert games* - what it shows is TESTED (UC, CV); its buttons as marked
  - Add images - TESTED (UC)
  - Add packages - TESTED (UC)
  - Add game folders - TESTED (UC)
  - Edit - TESTED (UC)
  - Remove - BETA
  - Clear finished - TESTED (UC)
  - Convert all - TESTED (UC, CV, GP, GT, XG)
  - Stop - BETA
  - Open the output folder - BETA
  - choice "Devkit" - BETA (this one choice is not proven)
  - choice "Like Microsoft" - BETA (this one choice is not proven)
  - choice "USB drive" - BETA (this one choice is not proven)
  - choice "Console (XBDM)" - BETA (this one choice is not proven)
  - choice "Console (FTP)" - BETA (this one choice is not proven)
  - choice "Every sector in place" - BETA (this one choice is not proven)
  - choice "Stealth-patch each new image with abgx360" - BETA (this one choice is not proven)

**Create & convert** tab

- *Create XISO from folder* - TESTED (XI)
  - Choose folder... - TESTED (XI)
- *Third-party tools* - TESTED
  - ISO2GOD - TESTED (XI)
  - Xbox Backup Creator - TESTED (XI)
  - abgx360 GUI (original) - TESTED (XI)

**abgx360** tab

- *abgx360 - verify and fix disc images* - what it shows is TESTED (UC, AB); its buttons as marked
  - Set up abgx360 - BETA - so far: with abgx360 installed it writes Hadzz's fix file and copies the video files - proven in a sandbox (UC, AB), and abgx360 reads the file. Running Hadzz's installer when abgx360 is missing was not tried: it was already installed here.
  - StealthFiles - BETA
  - Refresh - BETA
  - Verify only (changes nothing) - TESTED (UC, AB)
  - Verify + AutoFix - BETA - so far: the same run in the program as Verify only (proven, UC / AB) with AutoFix level 3 (--af3); no complete disc image was at hand to fix.
  - Stop - BETA - so far: the runner it uses ends abgx360 at once (AB); the button itself was not pressed.
  - Save the report... - TESTED (UC)
  - Open it in the browser - TESTED (UC)
  - Copy as text - BETA
  - Run with these options - BETA - so far: the same run as Verify only with the card's options; every option it can give is one abgx360 1.0.7 reads (AB). Not pressed in a test.
  - Reset to abgx360's defaults - BETA

### Original Xbox (OG)

- *XBE header* - TESTED (OG)
  - Refresh - TESTED (OG)
- *Certificate* - what it shows is TESTED (OG); its buttons as marked
  - Set - BETA - so far: XbeTool reads the new Title ID and only those 4 certificate bytes change (2026-11-19); no emulator or console has run an edited XBE yet.
  - Apply region - BETA - so far: XbeTool reads the new region and only that field changes (2026-11-19); no emulator or console has run an edited XBE yet.
  - Allow all media - BETA - so far: XbeTool reads HDD, DVD and CD allowed and only that field changes (2026-11-19); no emulator or console has run an edited XBE yet.
- *RAM patch* - BETA
  - Use 128MB - BETA - so far: XbeTool's init flags lose LIMIT_64MB and only that field changes (2026-11-19); no 128MB console or emulator has run it yet.
  - Stock 64MB - BETA - so far: XbeTool's init flags get LIMIT_64MB back (2026-11-19); no console or emulator has run it yet.
- *CPU clock patch* - BETA
  - Set MHz - BETA - so far: a custom value (1200 tried) changes only the stock constant, after a warning that it cannot be found again (2026-11-19). No upgraded console has run it.
  - 733 (stock) - BETA - so far: returns a 1400 / 1480 MHz patch to the original file byte for byte; after 1000 MHz or a custom value it points to the .bak instead of guessing (2026-11-19).
  - 1000 (Trusty) - BETA - so far: warns that 1000 MHz cannot be found again, then changes only the stock constant - the game's own 1,000,000,000 values stay (2026-11-19). No upgraded console has run it.
  - 1400 (Trusty) - BETA - so far: changes exactly the game's stock 733.33 MHz constant(s) and nothing else; back to stock gives the file byte for byte (2026-11-19). No upgraded console has run it.
  - 1480 (DreamX) - BETA - so far: moves the same places on from 1400 to 1480 MHz; back to stock gives the file byte for byte (2026-11-19). No upgraded console has run it.
- *XbeTool* - TESTED (OG)
  - All header info - TESTED (OG)
  - Certificate - TESTED (OG)
  - Sections - TESTED (OG)
  - Libraries - TESTED (OG)
  - Dump to binary - TESTED (OG)
  - Dump to EXE - TESTED (OG)
  - Dump IDA .idc - TESTED (OG)
  - Open folder - TESTED (OG)
- *xemu (original Xbox)* - partly tested (1 of 3 buttons)
  - Play this game - BETA - so far: the open default.xbe's folder becomes its XISO beside it and xemu (a stand-in) gets -dvd_path with it (UiXemuPlay); not yet booted in a real xemu.
  - Run an ISO... - BETA
  - Get xemu - TESTED (EM)

### Xenia

**Games** tab

- Play - BETA - so far: reaches Xenia's start for the selected game with the right Xenia (2026-11-25); not yet seen starting each kind of game from this tab.
- Install a save... - BETA - so far: a save for the selected game goes in for the profile Xenia signs in to, from a save file or a .zip / .rar, and a save for another game is left out (2026-11-25, checked with the bundled Xenia's own rules); WaW in the bundled Xenia opened a save put in this way. Not yet loaded in a game by a user.
- Add DLC / updates... - BETA
- DLC / updates from my Xbox - TESTED (XE)
- Open folder - BETA
- Add a game... - TESTED (UX)
- Remove... - BETA - so far: taking an added game off the list is proven; for a copy it asks first, and the Recycle Bin delete itself was not run in the test.
- Graphics settings... - TESTED (XU)
- Refresh - BETA

**Saves** tab

- Install a save... - BETA - so far: console saves, and .zip / .7z / .rar holding them, go in for the profile Xenia signs in to, with the header the bundled Xenia reads (2026-11-25, checked with its own rules); a save that came with its own profile can stay with it (that profile goes in too and Xenia signs in to it). WaW in the bundled Xenia opened a save put in this way. Not yet loaded in a game by a user.
- Install saves from a folder... - BETA - so far: the same as Install a save... for every save in the folder, and a profile there that the saves belong to is offered as their own (2026-11-25, on a copy of a real download); no game has loaded one in Xenia yet.
- Open Xenia's content folder - TESTED (XC2)
- Open in save editor - TESTED (XC2)
- Download... - TESTED (XC2)
- Replace with... - BETA - so far: puts the save into Xenia's form byte for byte (2026-11-18); no game has opened a copied save yet.
- Delete - TESTED (XC2)
- Find saves - TESTED (XC2)

**Profiles** tab

- Sign in to selected - TESTED (SI, UX)
- Create a profile... - TESTED (SI, UX)
- Add a profile... - BETA
- Open in profile editor - TESTED (XC2)
- Download... - TESTED (XC2)
- Replace with... - TESTED (XC2)
- Delete - TESTED (XC2)
- Find profiles - TESTED (XC2)

**Settings** tab

- *Xenia's settings* - TESTED (XK, XU)
  - Reload - TESTED (XK, XU)
  - Open its folder - TESTED (XK, XU)
  - Put back my old settings... - TESTED (XK, XU)
- *Graphics* - TESTED (XK, XU)
  - Xenia's defaults - TESTED (XU)
  - Sharper (2x) - TESTED (XU)
  - 4K (3x) - TESTED (XU)
- *Accuracy and speed* - TESTED (XK, XU)
- *Sound and controls* - TESTED (XK, XU)
- *Games and console* - TESTED (XK, XU)
- *Every Xenia setting* - TESTED (XK, XU)
  - Set - TESTED (XK, XU)

**Xenia Patches** tab

- *Patch file* - TESTED (EM)
  - Load .patch.toml... - TESTED (EM)
  - Enable / disable - TESTED (EM)
  - Get game-patches - TESTED (EM)
- *In Xenia* - TESTED (EM)
  - Install into Xenia - TESTED (EM)
  - Start the open game in Xenia - TESTED (EM)
  - Write into the running game - TESTED (EM)
- *On a real console* - BETA
  - Apply enabled to console - BETA - so far: not run on the console with this build.

**Setup** tab

- *Xenia (Xbox 360)* - TESTED
  - Run working file - TESTED (EM, SI)
  - Run a file... - TESTED (EM)
  - Use the bundled Xenia - TESTED (EM)
  - Newer Xenia builds - TESTED (EM)
- *Controllers* - BETA
  - Check controllers - BETA - so far: proven with an Xbox 360 controller: Windows and Xenia's own controller driver list the same pad, and Xenia's input setting is kept on "any". No PlayStation or Switch controller tried yet.

### Saves

**Save** tab

- *Save game* - TESTED (SV)
  - Refresh - TESTED (SV)
  - Full info - TESTED (SV)
- *Getting a save to load* - partly tested (3 of 4 buttons)
  - Install save... - TESTED (SV)
  - Rehash & resign - BETA
  - Full info - TESTED (SV)
  - Open folder - TESTED (SV)
- *Names* - TESTED
  - Apply names - TESTED (SE)

**Contents** tab

- *Contents* - what it shows is TESTED (D3); its buttons as marked
  - Extract... - TESTED (SV)
  - Extract all... - TESTED (D3, PKG)
  - View GPD - TESTED (D3)
  - Replace... - BETA
  - Add file... - BETA
  - Delete - BETA
  - Verify integrity - TESTED (SV)

**Game editor** tab  - BETA

- *Game values* - BETA
  - Change value... - BETA
  - Set selected to... - BETA
  - Run - BETA
  - Undo all changes - BETA
  - Write into the save - BETA - so far: written, checked, and put back on the console - but the game has not loaded the edited save yet.

**Rehash & resign** tab

- *Rehash & resign* - partly tested (2 of 6 buttons)
  - From a save or profile... - TESTED (SV)
  - Rehash & resign - BETA - so far: every hash checked afterwards; no console has loaded a save it changed yet.
  - Put back - TESTED (LC2, LS)
  - Many files... - BETA
  - Load keyvault... - BETA
  - Forget - BETA
- *Pick the IDs* - TESTED (IP)
  - Find my profiles - TESTED (IP)
  - A USB stick or folder... - TESTED (IP)
  - Use these IDs - TESTED (IP)

**Browse saves** tab

- *Where to look* - partly tested (2 of 3 buttons)
  - My console (XBDM) - BETA
  - A folder on this PC... - TESTED (SV)
  - An Xbox hard drive... - TESTED (SV)
- Open in save editor - TESTED (SV)
- Download... - TESTED (SV)
- Replace with... - TESTED (SV, LV)
- Delete - TESTED (SV, LV)
- Copy to Xenia - BETA - so far: from this tab (2026-11-19): a console save in a folder on this PC went into Xenia unpacked byte for byte, its profile with it. No game has opened a copied save yet.
- Find saves - TESTED (SV)

**Convert** tab  - BETA

- *Convert with X360PkgTool* - BETA
  - Make save RGH / retail - BETA - so far: on a console save without key00033.bin / key00036.bin next to X360PkgTool it explains that and offers Rehash & resign instead of running (2026-11-19). X360GameHack ships no console keys, so X360PkgTool itself was never run on a console save.
  - Make save devkit - BETA - so far: the same as Make save RGH / retail: explains and offers Rehash & resign without the console's key files; never run with them.
  - Remove console traces - BETA - so far: the same as Make save RGH / retail: explains and offers Rehash & resign without the console's key files; never run with them.

**Create a save** tab

- *What it is* - TESTED
  - Copy details from a package... - TESTED (MI)
- *Who it belongs to* - TESTED
  - From a save or profile... - TESTED (MI)
  - Set picture... - TESTED (CP)
  - No picture - TESTED (CP)
- *Files* - partly tested (3 of 4 buttons)
  - Add files - TESTED (MI)
  - Remove - TESTED (MI)
  - Clear - TESTED (MI)
  - Create package... - BETA - so far: the package passes every check; no console or game has loaded one yet.

**GPD viewer** tab - nothing to press

**GPD viewer > Achievements** tab

- *Achievements* - TESTED (D3)

**GPD viewer > Avatar awards** tab

- *Avatar awards* - TESTED (D3)

**GPD viewer > Settings** tab

- *Settings* - TESTED (D3)

**GPD viewer > Games** tab

- *Games* - TESTED (D3)

**GPD viewer > Pictures** tab

- *Pictures* - TESTED (D3)
  - Export pictures... - TESTED (MI, CL)

**GPD viewer > Strings** tab

- *Strings* - TESTED (D3)

**GPD viewer > Sync** tab

- *Sync* - TESTED (D3)

### Profiles

**Overview** tab

- *Account* - what it shows is TESTED (D1); its buttons as marked
  - Remove passcode - BETA
  - Convert to retail - BETA
  - Turn Xbox LIVE off - BETA
- *Games & gamerscore* - what it shows is TESTED (D1); its buttons as marked
  - Rehash & resign - BETA
  - Install to USB / console... - TESTED (MI)
  - Open folder - TESTED (MI, CL)
- *Edit gamertag* - TESTED (XC2)
  - Change gamertag - TESTED (XC2)

**Gamercard** tab

- *Gamer picture* - partly tested (1 of 2 buttons)
  - Change picture... - BETA
  - Save picture as... - TESTED (MI, CL)
- *Gamercard* - BETA
  - Save gamercard - BETA

**Games** tab

- *Every game* - BETA
  - Unlock all achievements - BETA - so far: matches console-made unlocks record for record and passes every check; no edited profile has been signed in on a console yet.
  - Lock all achievements - BETA
- *Add games* - BETA
  - Add games from another profile... - BETA
  - Add games from .gpd files... - BETA
  - Add a game from its XEX... - BETA
  - Add a game by Title ID... - BETA - so far: on a copy of a real console profile (2026-11-19): Call of Duty: Ghosts went in with the title database's 91 achievements and 2000 G, all locked - every record the database's own data, the 50 the game's XEX carries agreeing with it - the other games untouched; the same game again is refused. No console or emulator has signed in to a profile changed this way yet.
- *Title ID lookup* - TESTED (TL)
  - Look up - TESTED (TL)
- *Games played* - BETA
  - Set last played - BETA
  - Clear last played - BETA
- *Achievements* - what it shows is TESTED (D1); its buttons as marked
  - Unlock all for this game - BETA
  - Lock all for this game - BETA
  - Unlock selected - BETA
  - Lock selected - BETA

**Avatar awards** tab

- *Every game* - BETA
  - Unlock all avatar awards - BETA
  - Lock all avatar awards - BETA
- *Games with avatar awards* - TESTED (D2)
- *Awards* - what it shows is TESTED (D2); its buttons as marked
  - Unlock all for this game - BETA
  - Lock all for this game - BETA
  - Unlock selected - BETA
  - Lock selected - BETA

**Contents** tab

- *Contents* - what it shows is TESTED (D3); its buttons as marked
  - Extract... - TESTED (MI, CL)
  - Extract all... - TESTED (D3)
  - View GPD - TESTED (D3)
  - Replace... - BETA
  - Add file... - BETA
  - Delete - BETA
  - Verify integrity - TESTED (MI, CL)

**Rehash & resign** tab

- *Rehash & resign* - partly tested (1 of 6 buttons)
  - From a save or profile... - TESTED (MI)
  - Rehash & resign - BETA
  - Put back - BETA
  - Many files... - BETA
  - Load keyvault... - BETA
  - Forget - BETA
- *Pick the IDs* - TESTED (IP)
  - Find my profiles - TESTED (IP)
  - A USB stick or folder... - TESTED (IP)
  - Use these IDs - TESTED (IP)

### Drive Explorer

- Open drive image... - TESTED (DU)
- Open physical drive... - TESTED (DU)
- Safely remove - TESTED (DU)
- Find - TESTED (DU)
- Drive tools - TESTED (DU)
- Allow changes - TESTED (DU)
- Add files... - TESTED (DU)
- Add folder... - TESTED (DU)
- Install packages... - TESTED (DU)
- Check drive - TESTED (DU)
- Back up whole drive... - TESTED (DU)
- Recover deleted... - TESTED (DU)
- Undo changes... - TESTED (DU)
- Safely remove drive - TESTED (DU)
- Bring back drive - TESTED (DU)
- Create compatibility partition - TESTED (DU)
- Make Xbox drive... - TESTED (DU)
- Install emulator files... - TESTED (DU)

**Files** tab - nothing to press

**Partitions** tab - TESTED (PT)

**Saves** tab

- Open in save editor - TESTED (DU)
- Download... - TESTED (DU)
- Replace with... - TESTED (DU)
- Delete - TESTED (DU)
- Copy to Xenia - TESTED (DU)
- Find saves - TESTED (DU)
- Copy to PC... - TESTED (DU)
- Put a save back... - TESTED (DU)
- Refresh - TESTED (DU)

### Neighborhood

- Connect - TESTED (LC, LT, LV)
- Disconnect - TESTED (LC)
- Discover - TESTED (LV)

**Files** tab

- Upload files - TESTED (LF, LV)
- Upload folder - TESTED (LF, LV)
- Download - TESTED (LV)
- New folder - TESTED (LF, LV)
- Rename - TESTED (LV)
- Delete - TESTED (LF)
- Launch XEX - TESTED (LL)
- Launch package - BETA
- Play in Xenia - TESTED (LX)
- Drives - TESTED (LV)
- Hdd - TESTED (LV)
- Games - TESTED (LV)
- Content - TESTED (LV)
- Usb0 - TESTED (LV)
- Flash - TESTED (LV)

**Saves** tab

- Open in save editor - TESTED (LC2)
- Download... - TESTED (LV)
- Replace with... - TESTED (LV)
- Delete - TESTED (LV)
- Copy to Xenia - BETA - so far: from an Xbox drive image the save and its profile went into Xenia and Xenia signed in to it. From the console on 2026-11-18 the save went in but its profile (signed in on the console, so being written to) had one block that did not match its checksum and was refused; such a profile now goes in as it is with a warning naming the file, and Xenia signed in to that very profile - not yet repeated from the console. No game has opened a copied save yet.
- Find saves - TESTED (LC, LC2, LV)

**Profiles** tab

- Open in profile editor - TESTED (LV)
- Download... - TESTED (LV)
- Replace with... - TESTED (LT)
- Delete - TESTED (LV)
- Copy to Xenia - BETA - so far: the same copy as Add files..., whose profiles Xenia signs in to (also one with a block that does not match its checksum); not yet from a console.
- Find profiles - TESTED (FI, LV)

**Title updates** tab

- Check on the Title Updates page - TESTED (LV)
- Download... - TESTED (LV)
- Install a title update... - TESTED (LT)
- Delete - TESTED (LT)
- Find title updates - TESTED (FI, LT, LV)

### Console

- Connect - TESTED (LC)
- Disconnect - TESTED (LC)
- Discover - TESTED (LV)

**Console** tab

- *Console information* - TESTED (LC)
  - Refresh - TESTED (LV)
- *Power & system* - partly tested (6 of 9 buttons)
  - Reboot (cold) - TESTED (LP)
  - Restart title (warm) - TESTED (LP)
  - Shut down - BETA
  - Freeze (stop) - TESTED (LC)
  - Resume (go) - TESTED (LC)
  - Open tray - BETA - so far: sent on 2026-11-18 (three times, accepted each time); that the tray moved was not confirmed by anyone watching.
  - Close tray - BETA - so far: sent on 2026-11-18 (twice, accepted each time); that the tray moved was not confirmed by anyone watching.
  - Launch - TESTED (LF)
  - Open - TESTED (LF, LFTP)
  - choice "Turn_Off_Console" - BETA (this one choice is not proven)

**Screenshot** tab

- *Screenshot* - TESTED
  - Capture - TESTED (LC, LV)
  - Save as PNG... - TESTED (LV)

**Modules & commands** tab

- *Loaded modules* - TESTED (LC, LV)
  - Refresh - TESTED (LV)

**Storage backup** tab  - BETA

- *Back up storage (raw)* - BETA
  - Refresh - BETA
  - Back up to image... - BETA
- *XBDM command console* - BETA
  - Send - BETA

**Notify & Sensors** tab

- Connect - TESTED (LC)
- Disconnect - TESTED (LC)
- Discover - TESTED (LV)
- *Notification* - TESTED (LN)
  - Send - TESTED (LN)
  - Show every picture - TESTED (LN)
  - Stop - TESTED (LN)
- *Temperatures* - TESTED
  - Read - TESTED (LT)
- *Ring of light* - TESTED (LR)
  - Apply - TESTED (LR)
  - All green - TESTED (LR)
  - All red - TESTED (LR)
  - All orange - TESTED (LR)
  - All off - TESTED (LR)
  - Spin - TESTED (LR)
  - Alternate - TESTED (LR)
  - Flash - TESTED (LR)
  - Stop animation - TESTED (LR)

### Peek Poker

- *Where* - TESTED (LC, D6)
- Connect - TESTED (LC)
- Disconnect - TESTED (LC)
- Discover - TESTED (LV)

**Memory view** tab

- *Memory view* - TESTED
  - Peek - TESTED (LC, EM, LPK)
  - Poke changes - TESTED (EM, LPK)
  - Dump to file... - TESTED (EM, LPK)

**Values & trainer** tab

- *Read / write a value* - TESTED
  - Read - TESTED (LC, LC2, EM, LPK)
  - Write - TESTED (LC2, EM, LPK)
  - Add to trainer - TESTED (LC2, EM)
- *Trainer / freeze list* - TESTED (LC2, EM)
  - Freeze / unfreeze - TESTED (LC2, EM)
  - Write once - TESTED (LC2, EM)
  - Edit value - TESTED (LC2, EM)
  - Remove - TESTED (LC2, EM)
  - Load trainer... - TESTED (LC2, EM)
  - Save trainer... - TESTED (LC2, EM)

**Search & regions** tab

- *Memory search* - TESTED (LC, EM)
  - First scan - TESTED (LC, EM)
  - Next scan - TESTED (LC, EM)
  - Clear - TESTED (LC, EM)
- *Committed memory regions* - TESTED (LC, EM)
  - Load - TESTED (LC, EM)

### Debugger

- Connect - TESTED (LC)
- Disconnect - TESTED (LC)
- Discover - TESTED (LDB2)

**Execution** tab

- *Execution* - TESTED
  - Break - TESTED (LDB, LDB2)
  - Continue - TESTED (LDB, LDB2)
  - Step into - TESTED (LDB2)
  - Step over - TESTED (LDB2)
  - Run to return - TESTED (LDB2)
  - Run to selected - TESTED (LDB2)
- *Disassembly* - what it shows is TESTED (LDB2); its buttons as marked
  - Go - TESTED (LDB2)
  - PC - TESTED (LDB2)
  - Follow - TESTED (LDB2)
  - Breakpoint - BETA - so far: sets and clears the same breakpoint as Breakpoints > Add (which the game ran into on 2026-11-18); a stop at one set with this button was not watched.
- *Registers* - TESTED (LDB)

**Threads** tab

- *Threads* - what it shows is TESTED (LC2, LDB2); its buttons as marked
  - Refresh - TESTED (LDB2)
  - Debug this thread - TESTED (LDB2)
  - Suspend - TESTED (LDB2)
  - Resume - TESTED (LDB2)
  - Halt - BETA - so far: this console's XBDM (2.0.21076.11) refuses to halt a single thread; the page says so and points to Break.

**Modules** tab

- *Loaded modules* - TESTED (LC2, LDB2)
  - Refresh - TESTED (LDB2)
  - Dump module to file... - TESTED (LDB2)
- *Sections* - TESTED (LDB2)

**Breakpoints** tab

- *Breakpoints* - partly tested (2 of 4 buttons)
  - Add - TESTED (LDB2)
  - Show - TESTED (LDB2)
  - Remove - BETA - so far: takes the breakpoint off the list and the console; that the game then no longer stops there was not checked separately.
  - Remove all - BETA - so far: takes every breakpoint off the list and the console; that the game then no longer stops there was not checked separately.
- *Also stop when* - BETA
  - Apply - BETA - so far: accepted by the console on 2026-11-18; that it then stops when a thread starts was not watched.

**Trace** tab

- *Instruction trace* - partly tested (1 of 3 buttons)
  - Trace - TESTED (LDB2)
  - Stop - BETA
  - Save... - BETA

### FTP

- Connect - TESTED (LFTP)
- Save - TESTED (LF2)
- Send game folder - TESTED (LF2)
- Help - TESTED (LF2)
- Upload files - TESTED (LF2)
- Upload folder - TESTED (LF2)
- Download - TESTED (LF2)
- New folder - TESTED (LF2)
- Rename - TESTED (LF2)
- Delete - TESTED (LF2)
- Launch XEX - BETA
- Launch package - BETA
- Play in Xenia - TESTED (LX)

### Console Setup

**Install content** tab

- *Where to install* - BETA
  - Open - BETA
- *Install content* - partly tested (4 of 6 buttons)
  - Add files - TESTED (MI)
  - Add folder - TESTED (MI)
  - Remove - TESTED (MI)
  - Clear - TESTED (MI)
  - Install all - BETA
  - Copy game folder to USB... - BETA

**USB drive guide** tab

- *Setting up a USB drive for the Xbox 360* - information only

**INI Editor** tab

- Connect - TESTED (LC)
- Disconnect - TESTED (LC)
- Discover - TESTED (LI)

**INI Editor > launch.ini** tab

- *launch.ini* - partly tested (10 of 11 buttons)
  - Load from console - TESTED (LT)
  - New - BETA - so far: every setting it writes is one the console's own launch.ini has, in the same section, at a value it takes (2026-11-19); DashLaunch has not read a file started this way yet.
  - Change value... - TESTED (INI, LT)
  - Add setting... - TESTED (INI2)
  - Add plugin: XBDM - TESTED (INI2)
  - Add plugin: JRPC2 - TESTED (INI)
  - Add plugin: XRPC - TESTED (INI)
  - Undo all changes - TESTED (INI)
  - Use this text - TESTED (INI)
  - Send to console - TESTED (LT)
  - Save as... - TESTED (INI2)

**INI Editor > xbdm.ini** tab

- *xbdm.ini* - what it shows is TESTED (LI, INI2); its buttons as marked
  - Load from console - TESTED (LI)
  - Change value... - TESTED (INI2)
  - Add setting... - TESTED (INI2)
  - Undo all changes - TESTED (INI2)
  - Use this text - TESTED (INI2)
  - Send to console - BETA - so far: the edited file is exactly what it must be (only the changed line differs) and Save as writes it (2026-11-19); sending one to the console was not tried.
  - Save as... - TESTED (INI2)

**INI Editor > JRPC.ini** tab

- *JRPC.ini* - what it shows is TESTED (LI, INI2); its buttons as marked
  - Load from console - TESTED (LI)
  - Change value... - TESTED (INI2)
  - Add setting... - TESTED (INI2)
  - Undo all changes - TESTED (INI2)
  - Use this text - TESTED (INI2)
  - Send to console - BETA - so far: the edited file is exactly what it must be (only the changed value differs) and Save as writes it (2026-11-19); sending one to the console was not tried.
  - Save as... - TESTED (INI2)

### Flash Tools

- *NAND / RGH* - partly tested (2 of 3 buttons)
  - J-Runner - TESTED (FL)
  - Tools & drivers folder - TESTED (FL)
  - Copy SimpleNandFlasher to USB... - BETA - so far: needs a FAT32 USB drive plugged in; not tried.
- *DVD drive* - partly tested (3 of 4 buttons)
  - Jungle Flasher - BETA - so far: not started in a test: Jungle Flasher's own manifest asks Windows for administrator rights, so Windows always asks first.
  - Readme - TESTED (FL)
  - Firmware folder - TESTED (FL)
  - Tutorial (PDF) - TESTED (FL)
- *FTP2XeLL - read keys from XeLL* - partly tested (1 of 4 buttons)
  - Read - BETA - so far: needs a console started into XeLL; not tried.
  - Open in browser - TESTED (FL)
  - Copy CPU key - BETA
  - Save to file... - BETA

### Flash Dumps

**Overview** tab

- *This flash dump* - information only
- *Boot chain and patch slots* - TESTED (UN, NT)
  - Save the selected stage (as stored)... - TESTED (UN, NT)
  - Save the selected stage (decrypted)... - TESTED (UN, NT)
- *SMC* - TESTED (UN, NT)
  - Save SMC (decrypted)... - TESTED (UN, NT, JX)
  - Save SMC (as stored)... - TESTED (UN, NT, JX)
- *SMC config* - TESTED (UN, NT)
  - Save SMC config (smc_config.bin)... - TESTED (UN, NT, JX)

**Keyvault** tab

- *CPU key* - TESTED (UN, NT)
  - Unlock - TESTED (UN, NT)
- *Keyvault* - TESTED (UN, NT)
  - Copy DVD key - TESTED (UN, NT)
  - Save keyvault (decrypted)... - TESTED (UN, NT, JX)
  - Save keyvault (as stored)... - TESTED (UN, NT, JX)
  - Save fcrt.bin (decrypted)... - TESTED (UN, NT, JX)
- *Swap keyvault* - BETA
  - Swap keyvault... - BETA - so far: the new flash dump is proven on the PC: replacing the keyvault with its own bytes gives a byte-identical dump, a swapped keyvault reads back as stored (and the matching CPU key unlocks it) with every file and the EDC intact (NW). No console has booted one yet - flashing a NAND can brick a console.

**Files** tab

- *Flash file system* - TESTED (UN, NT)
  - Extract selected... - TESTED (UN, NT)
  - Extract all... - TESTED (UN, NT)
  - Open in X360GameHack - TESTED (UN)
- *Swap boot animation* - partly tested (1 of 3 buttons)
  - Swap boot animation... - BETA - so far: the new flash dump is proven on the PC: replacing bootanim.xex with its own bytes gives a byte-identical dump, and a changed one reads back through our reader and J-Runner's on all 62 flash dumps with every other file and the EDC intact (NW, 1,460 checks). No console has booted one yet - flashing a NAND can brick a console.
  - Save this dump's boot animation... - TESTED (UN, NT)
  - Replace selected file... - BETA - so far: the same flash-file-system write as Swap boot animation, proven on the PC (NW); not booted on a console. A file larger than the one in the dump is refused.

**Checks** tab

- *Pages, blocks and spare bytes* - TESTED (UN, NT)
  - Save without spare bytes... - TESTED (UN, NT)

### Mods

**All mods** tab  - BETA

- *Every mod a licence unlocks* - BETA
  - Get a licence - BETA
  - Enter my licence... - BETA
  - Refresh list - BETA
- *What it does* - BETA

**Game mods** tab

- *Game* - TESTED (MD)
- *1.  Group* - TESTED (MD)
- *2.  Option* - TESTED (MD)
- *3.  Set and apply* - TESTED (MO)
  - Apply - TESTED (MO)
  - Turn off - TESTED (MO)

**My mod files** tab

- *Your mods* - TESTED (MD)
  - Lock selected - TESTED (MO)
  - Refresh - TESTED (MO)
  - Open mods folder - TESTED (MO)
- *Licence* - TESTED (MO)
  - Unlock my mods - TESTED (MO, MS)
  - Forget token - TESTED (MO)
  - Copy - TESTED (MO)

**Mod Maker** tab

- *The mod* - TESTED (MO)
- *Protection* - TESTED (MO)
  - Build .x360mod... - TESTED (MO)
- *What it does (payload)* - TESTED (MK)
  - Insert game template - TESTED (MK)
  - Insert save template - TESTED (MK)
  - Check syntax - TESTED (MK)
- *Your signing key* - BETA
  - Back up signing key... - BETA - so far: the backup engine is proven (2026-11-25, 15 checks on the real key, only read): sealed with PBKDF2-SHA256 600,000 rounds + AES-256 + HMAC, opened again before it is kept, wrong password / changed byte / another key refused, never replaces a file, restore (cli owner-key-restore) never replaces a key and the restored key signs mods the program takes. The window's own button and password box have not been pressed in a test yet.

### Settings

**Appearance** tab

- *Themes* - TESTED (PC1)
  - Apply - TESTED (PC1)
  - New theme - TESTED (PC1)
  - Duplicate - TESTED (PC1)
  - Edit - TESTED (PC1)
  - Delete - TESTED (PC1)
  - Import... - TESTED (PC1)
  - Export... - TESTED (PC1)
- *Theme editor* - TESTED (PC1)
  - Generate shades from accent + background - TESTED (PC1)
  - Save & apply - TESTED (PC1)
  - Save - TESTED (PC1)
  - Cancel - TESTED (PC1)

**General** tab

- *Startup* - TESTED (PC1, PC2, ST), except the choice(s) marked below
  - Check for updates now - TESTED (UP2)
  - choice "Ask me to install new versions" - BETA (this one choice is not proven)
- *Working with files* - TESTED (PC1, ST)

**Folders & tools** tab

- *Folders and programs* - TESTED (PC1)
  - Browse... - TESTED (PC1)
  - Browse... (2) - TESTED (PC1)
  - Browse... (3) - TESTED (PC1)
  - Browse... (4) - TESTED (PC1)
- *Default tool commands* - TESTED (ST)
- *Bundled tools* - TESTED (PC1)
  - Check now - TESTED (PC1)
  - Open tools folder - TESTED (PC1)

**Network** tab

- *Console (XBDM)* - TESTED (PC2)
- *Other* - TESTED
  - Forget saved FTP passwords - TESTED (PC1)

**Privacy & data** tab

- *Security* - TESTED (PC1)
  - Forget keyvault - TESTED (PC1)
- *Your data* - TESTED (PC1)
  - Open settings folder - TESTED (PC1)
  - Open log file - TESTED (PC1)
  - Clear recent files - TESTED (PC1)
  - Export settings... - TESTED (PC1)
  - Import settings... - TESTED (PC1)
  - Reset all settings... - TESTED (PC1)
- *Utilities* - TESTED (PC1)
  - Compute SHA3-256 of a file... - TESTED (PC1)

**About** tab

- *X360GameHack 2026* - TESTED (PC1)
  - Report a bug - TESTED (PC1)
  - Releases - TESTED (PC1)
  - YouTube - TESTED (PC1)
- *Credits* - information only
- *Legal* - information only

## Command line and MCP (`X360GameHack.exe cli ...` / `mcp`)

Same rule: TESTED means the command was run through the real program and its answer checked. `cli help` says
`"beta": true` for the others, and the MCP tool list starts their descriptions with `[BETA - not tested yet]`.
CliCheck (2026-11-18, 62 checks) compared every file command with xextool / extract-xiso run separately or with the
files read back; CliLiveTests (107 checks) covers drive images and the console commands against a stand-in console that
answers the way the real one was measured to.

| Command | Status | Tested by |
|---|---|---|
| `help` | TESTED | CliCheck |
| `info` | TESTED | CliCheck |
| `list` | TESTED | CliCheck |
| `verify` | TESTED | CliCheck |
| `extract` | TESTED | CliCheck |
| `tu-extract` | TESTED | CliCheck |
| `tu-check` | TESTED | CliCheck |
| `rehash` | BETA | - |
| `create-save` | BETA | - |
| `gamesave-list` | BETA | - |
| `gamesave-set` | BETA | - |
| `gamesave-action` | BETA | - |
| `mod-info` | TESTED | LicServerE2E |
| `mod-validate` | TESTED | LicServerE2E |
| `mod-build` | TESTED | LicServerE2E |
| `mod-sign` | TESTED | OS |
| `owner-key-restore` | BETA | - |
| `console-notify` | TESTED | McpConsole |
| `console-notify-icons` | TESTED | McpConsole |
| `console-temperatures` | TESTED | McpConsole |
| `profile` | TESTED | CliCheck |
| `achievements` | TESTED | CliCheck |
| `unlock` | BETA | - |
| `xex` | TESTED | CliCheck |
| `functions` | TESTED | CliCheck |
| `imports` | TESTED | CliCheck |
| `disasm` | TESTED | CliCheck |
| `decompile` | TESTED | CliCheck |
| `save-image` | TESTED | CliCheck |
| `ghidra` | TESTED | CliMore |
| `iso-list` | TESTED | CliCheck |
| `iso-extract` | TESTED | CliCheck |
| `iso-create` | TESTED | CliCheck |
| `iso-swap` | TESTED | CliMore |
| `god-info` | TESTED | CliGodTests |
| `iso2god` | TESTED | CliGodTests |
| `god2iso` | TESTED | CliGodTests |
| `nand-info` | TESTED | CliNandTests |
| `nand-extract` | TESTED | CliNandTests |
| `nand-stages` | TESTED | CliNandTests |
| `nand-replace-file` | BETA | - |
| `nand-replace-kv` | BETA | - |
| `convert` | TESTED | CliConvertTests |
| `patch-list` | TESTED | CliCheck |
| `patch-install` | TESTED | CliMore |
| `xenia-read` | TESTED | CliMore |
| `xenia-write` | TESTED | CliMore |
| `disk-run` | TESTED | DiskRun |
| `disk-eject` | TESTED | DiskEject |
| `disk-remount` | TESTED | DiskRemount |
| `drive-list` | BETA | - |
| `drive-check` | BETA | - |
| `drive-recover-list` | TESTED | Recover |
| `drive-recover` | TESTED | Recover |
| `drive-extract` | BETA | - |
| `drive-add` | BETA | - |
| `drive-install` | BETA | - |
| `drive-delete` | BETA | - |
| `console-find` | TESTED | McpConsole |
| `console-info` | TESTED | McpConsole |
| `console-list` | TESTED | McpConsole |
| `console-get` | TESTED | McpConsole |
| `console-put` | TESTED | McpConsole |
| `console-read` | TESTED | McpConsole |
| `console-write` | TESTED | McpConsole |
| `console-dump-storage` | BETA | - |
| `console-screenshot` | TESTED | McpConsole |

## Not covered by any test yet (whole areas)

- **Saves** > Game editor tab
- **Saves** > Convert tab
- **Console** > Storage backup tab
- **Mods** > All mods tab
- debugger: Halt, the Breakpoint button on a line, Remove / Remove all, Also stop when, trace Stop / Save (stepping, Run to...,
  breakpoints and trace in the game's own code were proven on the console on 2026-11-18)
- changing a physical Xbox drive with the window's own buttons (Allow changes, Add files..., Undo changes...) - the writer and
  undo behind them are proven on both of the user's real drives (Xbox 360 and original Xbox) through the command line
- anything on original Xbox hardware, retail (unmodified) consoles, or with a keyvault loaded

## What it takes to prove the rest

**At the console, with someone watching the TV:**

- Debugger: Halt (this console's XBDM refuses to halt one thread), the Breakpoint button on a line, Remove / Remove all (watch
  the game stop there, then no longer stop), Also stop when > Apply (watch a new thread stop the game), the trace Stop and Save.
  A step into a jump through CTR cannot be done on this XBDM - it reports CTR as 0 at a breakpoint - so the page says so.
- Profiles: sign in with an edited copy (unlocks, gamertag, gamer picture, gamercard, avatar awards, added games, passcode,
  retail / Xbox LIVE switches) and check the dashboard shows the change.
- Saves: load the edited Black Ops II World State save that is already on the console (put back 2026-11-17, never loaded in
  the game yet); then a save made with Create a save, Install save, and a rehashed save.
- Notifications (Send, Show every picture) and the ring of light: they go through JRPC2, which froze the test console three
  times - only with someone at the console, watching the TV and the LEDs.
- Power: Shut down; Open / Close tray with someone watching the tray; the Turn_Off_Console shortcut.
- Copy to Xenia from the console (Neighborhood Saves / Profiles) with that profile signed out on the console; then a game in
  Xenia loading a copied save. Xenia Patches > Apply enabled to console, with a game whose patch file matches.
- Console Setup: Install all and Copy game folder to USB (then the console must see it); xbdm.ini / JRPC.ini load and send.
- Games on Demand: a converted package started from the console.
- Mods: Apply / Turn off on the running game; Unlock my mods with the real licence.
- FTP page: Launch XEX.

**In Xenia, with a real game:** Run working ISO / Run an ISO... on the Emulators page; Swap files from a folder; a game opening a
save copied in on the Xenia saves & profiles tab (and that tab's other buttons); Check controllers with a PlayStation or Switch pad.

**On this PC only (quick):** the Home tiles; every link button; the Flash Tools and Install abgx360 launchers; the Settings
page; Copy Title ID / Media ID; choosing a game and an update on the Title Updates page, Install on a USB drive, Apply .xexp;
the XEX Disassembler Save / Save copy / Reload / Find next / Replace all / To Ghidra (reopened in Ghidra); X360Packages quick
actions and Saves > Convert (compared with X360PkgTool run directly); the Original Xbox page (compared with XbeTool run directly);
abgx360 verify / AutoFix on a real disc image; the Mod Maker building a mod that then loads.

**Needs hardware or a reference that is not here:** Drive Explorer's changes made in the window on a real drive (by hand),
original Xbox file times and drives made here (an original Xbox reading them), original Xbox patches and Play in xemu (an
original Xbox, or xemu with its BIOS files), a retail (unmodified) console, a keyvault.

_Made from the program on 2026-11-26._
