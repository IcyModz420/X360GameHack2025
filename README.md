X360GameHack 2025-2026

Version 4.0.0 is out - the biggest update yet. Stay tuned here, more coming soon:

https://www.youtube.com/@IcyModz420

Summary:
----------------------------------------------------
- X360GameHack is a free, open-source, all-in-one app for the Xbox 360 and the original Xbox: RGH/JTAG consoles, Bad Update / Bad Avatar ("exploited consoles"), devkits - and now your Xbox hard drives, Xenia and xemu too.
- It makes the everyday jobs simple: install your own backed-up games from ISO or STFS, patch XEX files, manage saves, profiles, title updates and DLC, open and change Xbox hard drives on your PC, and play in Xenia or xemu.
- Its own engines read and write the Xbox formats (XEX, STFS, ISO / XISO, Games on Demand, FATX / XTAF drives), checked byte for byte against the trusted tools and real console files.
- It never runs as administrator. The only part that does is the small Disk Reader for physical drives, and only from the installer's protected folder.
- Perfect for RGH sellers to give to their customers with their new RGH.
- X360GameHack is still in production in 2026-2027 as a side hobby. Sorry, I'm a professional painter and body man too - I get tired, and I have other projects.

Security:
----------------------------------------------------
- Never runs as administrator - started elevated, it closes at once, so the tools it starts never get those rights either.
- The only part that runs as administrator is the small Disk Reader for physical Xbox drives. It only runs from the installer's protected folder, starts with a clean environment, loads system DLLs only from Windows' own folder (DLL search order hardening), refuses the Windows disk, and only the real X360GameHack can talk to it.
- Updates are signed: the program checks the signature of the update notice and the size and SHA-256 of every download before anything runs.
- Open Source: Hosted on GitHub, allowing community contributions and vulnerability reporting. Users can submit bug reports or patches via the GitHub Issues page.

About the BETA and BRICKS POSSIBLE labels:
----------------------------------------------------
- Anything with a small orange BETA label has NOT been tested end to end yet. It could have problems - or it could work perfectly fine. If you are not an advanced user, please hold off on BETA features until the next big release.
- Everything without a label has been tested end to end: pressed in the real program and checked against the real thing (a real console, real drives, or the trusted tool's own output). Please report bugs for those first.
- The red BRICKS POSSIBLE label sits on Flash Dumps. Writing a NAND can brick a console. I have plenty of consoles to test on, but with everything else going on I haven't had the time to test the flash tabs yet. Everything there works on a copy and never changes your original dump - keep it as your backup, and only flash a console that is yours.
- The full list of what is tested is in TESTING.md, which comes with the program.

Home: (Production Complete)
----------------------------------------------------
![Home](docs/screenshots/01-home.png)

- Drop any file - XEX, package, save, ISO, XBE or a whole Xbox drive image - and it opens in the right tab.
- Quick tiles for everything you use most, and your recent files.

XEX Tool: (Production Complete)
----------------------------------------------------
![XEX Tool](docs/screenshots/02-xex-tool.png)

- Title ID, Media ID, version, the key it is encrypted with and the game's icon - read by X360GameHack itself, no command line.
- Encrypt, decrypt, compress and decompress XEX, EXE and DLL files.
- Patch devkit builds of games to work on RGH consoles, and change Title / Media IDs.

XEX Disassembler: (Production Complete)
----------------------------------------------------
![XEX Disassembler](docs/screenshots/03-xex-disassembler.png)

- A full Xbox 360 PowerPC disassembler (VMX128 included) and a decompiler that shows the C right in the tab.
- Jump to addresses, find, and export the whole game to Ghidra.

Title Updates:
----------------------------------------------------
![Title Updates](docs/screenshots/04-title-updates.png)

- Finds the title updates for your game and puts them in the right folder for your console, USB stick or Xenia.

X360Packages:
----------------------------------------------------
![X360Packages](docs/screenshots/05-x360packages.png)

- Open, verify and extract any package: CON, LIVE and PIRS - saves, DLC, title updates, arcade games, themes.
- Replace, add and delete files inside a package, change its names and pictures, then rehash it in one click.

Xbox ISOs:
----------------------------------------------------
![Xbox ISOs](docs/screenshots/06-xbox-isos.png)

- Extract Xbox and Xbox 360 ISOs (XGD1/2/3) - byte for byte the same as extract-xiso - one at a time or in bulk.
- Convert an ISO to Games on Demand and back.
- Browse what is inside an ISO before you extract it.

Original Xbox (OG):
----------------------------------------------------
![Original Xbox](docs/screenshots/07-original-xbox.png)

- Everything in an XBE header: title, Title ID, region, media types and more - and patch them.

Xenia:
----------------------------------------------------
![Xenia](docs/screenshots/08-xenia.png)

- Your games list, one-click Play, and every Xenia setting with its description (4K, FSR, frame rate and more).
- Install saves, title updates and DLC the way Xenia keeps them - straight from your console, your Xbox drive or a download.

Saves:
----------------------------------------------------
![Saves](docs/screenshots/09-saves.png)

- Browse every save with its game and profile, rehash and resign in one click, create a save package and view GPD files.

Profiles:
----------------------------------------------------
![Profiles](docs/screenshots/10-profiles.png)

- See any profile: gamertag, games, gamerscore and achievements.
- Change the gamertag, and add games by Title ID.

Drive Explorer: (Production Complete)
----------------------------------------------------
![Drive Explorer](docs/screenshots/11-drive-explorer.png)
![Drive Explorer partitions](docs/screenshots/11b-drive-explorer-partitions.png)

- Open Xbox 360 and original Xbox hard drives right on your PC - a drive connected by SATA or USB, or a drive image.
- Every partition of the drive on one tab, with how much is free. Original Xbox drives open on F:.
- Read and write both kinds: drag files out to Windows, drop files in, rename, delete and make folders. Every change to a real drive is saved in an undo journal first, so Undo changes can put the drive back exactly.
- Saves on original Xbox and Xbox 360 drives, Find, Properties, recover deleted files, back up the whole drive, and Safely remove.
- Play in Xenia or xemu straight from the drive.
- Tested on 2 live drives: one formatted in an Xbox 360 and used on an RGH, and a 1 TB drive formatted with FATXplorer from a TSOP'd original Xbox.

Neighborhood:
----------------------------------------------------
![Neighborhood](docs/screenshots/12-neighborhood.png)

- Connect to your console over XBDM, browse its drives, upload and download with drag and drop, and launch games.

Console:
----------------------------------------------------
![Console](docs/screenshots/13-console.png)

- Everything about the connected console in one place.

Peek Poker: (Production Complete)
----------------------------------------------------
![Peek Poker](docs/screenshots/14-peek-poker.png)

- Read, write and freeze live console memory.

Debugger:
----------------------------------------------------
![Debugger](docs/screenshots/15-debugger.png)

- Breakpoints, stepping, Run to and trace in a game running on your console - over XBDM only.

FTP:
----------------------------------------------------
![FTP](docs/screenshots/16-ftp.png)

- File transfers with Aurora, FSD and XeXMenu.

Console Setup:
----------------------------------------------------
![Console Setup](docs/screenshots/17-console-setup.png)

- Drop in Games on Demand, arcade games, DLC, title updates and saves - each goes to the right Content folder on your USB stick or console.

Flash Tools / Flash Dumps: (BRICKS POSSIBLE)
----------------------------------------------------
![Flash Tools](docs/screenshots/18-flash-tools.png)
![Flash Dumps](docs/screenshots/19-flash-dumps.png)

- Open your NAND dumps: board, boot chain, keyvault (with your CPU key) and files - always on a copy.
- Not yet tested on a console: see "About the BETA and BRICKS POSSIBLE labels" above.

Mods:
----------------------------------------------------
![Mods](docs/screenshots/20-mods.png)

- The mods of X360DeRankTool - 12 games - are now built into X360GameHack, and new mods are on the way.
- Paid mods are unlocked with a licence that is tied to your PC. The mod files only come from the licence server, sealed, and only for a licensed PC.

Q & A:
----------------------------------------------------
![Q and A](docs/screenshots/21-q-and-a.png)

- Plain answers for patching, installing, drives, Xenia, xemu and console setup.

Settings: (Production Complete)
----------------------------------------------------
![Settings](docs/screenshots/22-settings.png)

- Themes (make your own), folders and tools, update checks, and a check that every bundled tool is the real one.
