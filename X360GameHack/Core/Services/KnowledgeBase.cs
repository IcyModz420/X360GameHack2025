using System.Collections.Generic;

namespace X360GameHack.Core.Services
{
    public sealed class QaEntry
    {
        public string Category;
        public string Question;
        public string Answer;
        public QaEntry(string c, string q, string a) { Category = c; Question = q; Answer = a; }
    }

    /// <summary>
    /// Built-in, offline answers, written for someone who has never modded a console or used
    /// anything on a PC beyond a web browser. Every answer is complete on its own.
    /// </summary>
    public static class KnowledgeBase
    {
        public static readonly List<QaEntry> Entries = Build();

        private static List<QaEntry> Build()
        {
            var list = new List<QaEntry>();
            Start(list);
            Words(list);
            Usb(list);
            HardDrives(list);
            Network(list);
            InstallGames(list);
            Updates(list);
            Xex(list);
            Packages(list);
            Saves(list);
            Profiles(list);
            Discs(list);
            Modding(list);
            Disassembling(list);
            Xenia(list);
            ConsoleMods(list);
            OriginalXbox(list);
            Trouble(list);
            return list;
        }

        /// <summary>One answer; each string is a paragraph.</summary>
        private static void E(List<QaEntry> list, string category, string question, params string[] paragraphs)
        {
            list.Add(new QaEntry(category, question, string.Join("\n\n", paragraphs)));
        }

        // ------------------------------------------------------------------ Start here
        private static void Start(List<QaEntry> l)
        {
            const string c = "Start here";
            E(l, c, "What does the orange BETA label mean?",
                "It marks a part of the program that has not been proven to work end to end yet. It may well work, but that is not proven. " +
                "Everything without the label was tested end to end: the button was pressed in this program and what it is for was checked on the real thing - " +
                "console features on a real modded console, files against the identical output of a trusted tool (xextool, extract-xiso) or the real data. " +
                "A changed profile or save counts as proven only once a console has used it, so editors that change files stay BETA until then.",
                "Where the label sits tells you how much is unproven: on a side-menu entry, nothing on that page is proven yet; on a tab or a card, nothing inside it; on a button, just that button. " +
                "Hover an unproven button and its tip says so too. On a switch or a drop-down, only the unproven choices carry the label, and the tip speaks of BETA only while one of them is selected. " +
                "Keep a backup of anything a BETA feature changes. The full list, with how each part was tested, is in TESTING.md next to the program.");
            E(l, c, "What does X360GameHack do?",
                "It is one program that holds the tools people normally collect one by one to work with Xbox 360 and original Xbox games: " +
                "installing games, title updates and DLC, patching game programs, editing saves and profiles, opening disc images, " +
                "looking inside a console's drives, changing a running game's memory, and reading a game's code.",
                "You do not need to know which tool a file needs. Drag any file onto the window and the program looks at what is inside the file " +
                "(not at its name) and opens the right page for it.");
            E(l, c, "I am completely new. What do I need before anything works?",
                "1. A console that can run unsigned programs. A normal, untouched Xbox 360 only runs things Microsoft signed. " +
                "To run copied games, mods or homebrew you need an RGH or JTAG console (a small chip soldered inside), or a normal console running the Bad Update or Bad Avatar exploit from a USB drive. " +
                "See \"Console mods\" for what each one is.\n" +
                "2. A way to move files: a USB drive formatted as FAT32, or a network cable between the console and your router or PC.\n" +
                "3. The game files themselves, from discs and downloads you own.",
                "Things that work with NO modded console: opening and inspecting any file, extracting disc images, editing profiles and saves on a USB drive " +
                "(a stock console also needs the save signed with a keyvault, see \"Saves\"), running games in the Xenia emulator on your PC, and reading game code.");
            E(l, c, "How do I open a file in the program?",
                "Any of these do the same thing:\n" +
                "- Drag the file from a Windows folder and drop it anywhere on the program window.\n" +
                "- Press the open button at the top and pick the file.\n" +
                "- Pick it from Recent files on the Home page.",
                "The program then switches to the page for that kind of file. Many Xbox 360 files have no ending like .exe or .zip at all " +
                "(their names are long strings of letters and numbers). That is normal; just drop them in.");
            E(l, c, "Which page do I use for what?",
                "XEX Tool - patch a game's main program (default.xex): remove limits, decrypt, change IDs.\n" +
                "XEX Disassembler - look at and change the bytes and the code of a game program.\n" +
                "Title Updates - explains game updates and installs or merges them.\n" +
                "X360Packages - Games on Demand, arcade games, DLC and other packages: look inside, unlock, edit.\n" +
                "Xbox ISOs - disc images: look inside, extract, build, convert, swap files.\n" +
                "Original Xbox (OG) - default.xbe files of the first Xbox.\n" +
                "Xenia - your Xbox 360 games on the PC: play them, and give them saves (downloaded or modded too), profiles, DLC and patches.\n" +
                "Saves / Profiles - game saves, achievements, gamertag, gamer picture.\n" +
                "Drive Explorer - read an Xbox hard drive plugged into the PC.\n" +
                "Neighborhood / Console / Peek Poker / Debugger - talk to a console over the network.\n" +
                "FTP - copy files to and from a console (fastest network method).\n" +
                "Console Setup - install things onto a USB drive or the console; its INI Editor tab edits launch.ini and the plugins' ini files.\n" +
                "Flash Tools - helpers for the people who install the RGH chip.");
            E(l, c, "Can this break my console or my files?",
                "The program never changes your original file without saying so: actions that rewrite a file make a backup copy first or ask where to save the result. " +
                "Reading, opening, browsing and extracting never change anything.",
                "The things that can cause real trouble are on the console side and are always your choice: writing wrong values into a running game's memory (the game may crash - restarting the game fixes it), " +
                "editing launch.ini badly (the console still starts; fix the file from a USB drive), and going on Xbox LIVE with a modded console or modded profile (ban risk, see \"Console mods\").",
                "Nothing in this program writes to the console's NAND (its internal system memory), so it cannot \"brick\" a console.");
            E(l, c, "Why does the program refuse to run as administrator?",
                "Administrator rights let a program change anything on your PC. This program handles files downloaded from the internet, so it deliberately runs with normal rights; " +
                "if it is started as administrator it closes itself.",
                "The single job that truly needs more rights - a real Xbox hard drive plugged into the PC - is done by the small separate Disk Reader that only comes with the installed version. " +
                "It only opens disks Windows does not use (never the Windows disk, never a disk with a Windows volume), reads unless you allowed changes, and then saves what every sector " +
                "held before changing it, so Undo changes can put it back. Windows' permission prompt names it \"X360GameHack Disk Reader\": that is a tiny program that starts the reader " +
                "with a clean environment, so nothing else on the PC can slip into it while it has those rights.");
            E(l, c, "Windows Defender or my antivirus says the program is dangerous. Is it?",
                "New programs that are not signed with a paid company certificate, and that do things like edit other programs' files or memory, are often flagged by antivirus \"machine learning\" guesses. " +
                "Names ending in \"!ml\" are exactly that: a guess from a model, not a known virus.",
                "The full source code is public, so anyone can read and build it. Only download it from the official releases page linked on Settings > About. " +
                "Do not turn your antivirus off for it; if a release is flagged, report it on the bug page so it can be submitted to Microsoft as a false positive.");
            E(l, c, "Where must the helper tools be?",
                "xextool.exe, X360PkgTool.exe, XbeTool.exe, ISO2God.exe, xenia_canary.exe and the other bundled programs must stay in the same folder as X360GameHack.exe. Do not move or rename them.",
                "Each one is checked against a known fingerprint (SHA-256) before it is started. If a tool was replaced or damaged it is refused, and Settings > Bundled tools > Check now tells you which one.");
            E(l, c, "What does the title bar text mean (latest, preview, not the latest)?",
                "When it starts, the program reads the signed update notice from its own address (on Cloudflare), which holds the current release number.\n" +
                "- Latest Version: the released number is the same as yours, or nothing newer has been released.\n" +
                "- Yours is lower: a newer version is out - the program asks whether to install it (see the next question).\n" +
                "- Yours is higher: you are running a preview version that has not been released yet.\n" +
                "- Offline: Windows says this PC has no internet connection.\n" +
                "- Update Server Unreachable: you are online, but the update server did not answer. It is tried again at the next start.\n" +
                "- Update Notice Failed Its Check: something answered with a notice that is not signed by X360GameHack (or not signed at all) - it is ignored.",
                "The \"Opened ... times!\" number is a public counter at the same address: each start adds one. Nothing about you or your PC is sent or stored - not even an address or an id, only the count goes up. Settings > Check for updates at startup switches both off.");
            E(l, c, "How does the program update itself? Is that safe?",
                "When a newer version is out, a window asks \"Update to ... now?\" - Yes or No. It shows what is new and the SHA-256 the signed update notice gives for the download; nothing is downloaded until you answer Yes. " +
                "Yes downloads and checks it, then shows the SHA-256 of the file that arrived next to the expected one, and every file the update adds or replaces with its own SHA-256, and asks \"Install ... now?\" - Yes or No again. Nothing is installed until that second Yes. " +
                "No leaves everything as it is; you can also Open the GitHub page and update by hand, or Skip this version. A copy installed with the installer is updated by the new installer (Windows asks for permission); a portable copy has its files replaced. " +
                "Settings > Ask me to install new versions switches the question off; Settings > Check for updates now asks the server at once.",
                "The notice is signed with a private key that only the publisher has. The program checks that signature, then downloads only from the program's own GitHub releases, and keeps a file only when its exact size and SHA-256 match the signed notice. " +
                "Right before installing, the file is checked once more while it is locked against changes - so nobody else (not Cloudflare, GitHub, your network or an attacker) can make it install anything. It never runs as administrator itself.");
            E(l, c, "Can an AI assistant use this program for me?",
                "Yes. The same program can be run without its window: \"X360GameHack.exe cli help\" lists every command, and each command prints one block of JSON text and nothing else. " +
                "\"X360GameHack.exe mcp\" starts it as a tool server that AI apps can connect to.",
                "These commands only read and write the files you name. They never start other programs and never run text as a command. The file AI.md next to the program explains it for the assistant.");
        }

        // ------------------------------------------------------------------ Words explained
        private static void Words(List<QaEntry> l)
        {
            const string c = "Words explained";
            E(l, c, "Retail, RGH, JTAG, devkit - what are these consoles?",
                "Retail (also \"stock\"): a normal console as sold in shops. It only runs programs signed by Microsoft.",
                "JTAG: the first hardware hack. It only works on consoles that were never updated past mid-2009, so they are rare.",
                "RGH (Reset Glitch Hack): a small chip soldered to the mainboard that makes the console accept a modified system. It works on almost every Xbox 360. RGH3 is a newer way that needs no chip on many boards. " +
                "RGH and JTAG consoles behave the same once they run.",
                "Devkit: a console Microsoft made for game developers. It runs unsigned programs out of the box but uses different encryption keys, so files must be made \"for devkit\".");
            E(l, c, "What is a dashboard? What are Aurora, FSD and XeXMenu?",
                "The dashboard is the menu you see when the console starts. Microsoft's own one is called the \"stock\" or \"NXE / Metro\" dashboard.",
                "Aurora and Freestyle Dash (FSD) are replacement menus for hacked consoles. They list your games with covers, copy files, run an FTP server for file transfers, and download title updates and covers. Aurora is the newer one and is what most people use.",
                "XeXMenu is a tiny file manager: it shows the drives and lets you start any .xex file or copy and delete files. It is usually the first thing run on a freshly hacked console.");
            E(l, c, "What is DashLaunch?",
                "A small program installed once on a hacked console. It decides what the console starts into (for example Aurora instead of Microsoft's dashboard), blocks Microsoft's system updates and Xbox LIVE if you ask it to, " +
                "and loads \"plugins\" - extra modules such as xbdm.xex that run in the background.",
                "Its settings live in a text file called launch.ini. You can edit that file in Console Setup.");
            E(l, c, "What do XEX, STFS, GOD, XBLA, DLC, TU and ISO mean?",
                "XEX - an Xbox 360 program. A game's main program is the file default.xex.",
                "STFS package - one file that holds other files inside it, like a zip file that the console understands. It usually has no file ending. CON, LIVE and PIRS are the three kinds (signed by a console, or by Microsoft).",
                "GOD (Games on Demand) - a full disc game stored as a package: one small file plus a folder of big data files.",
                "XBLA - Xbox LIVE Arcade, the smaller downloadable games. One package each.",
                "DLC - downloadable content: extra maps, songs, cars and so on for a game.",
                "TU (title update) - a patch for one game.",
                "ISO / XISO - an exact copy of a whole game disc in one big file.");
            E(l, c, "What are the Title ID and the Media ID?",
                "Title ID: an 8-character code that names a game, for example 4D5307E6. Every copy of that game in every country has the same one. Saves, DLC and updates are filed under it.",
                "Media ID: an 8-character code for one particular pressing of the game (the first disc release, the Platinum Hits re-release, the downloadable version...). " +
                "A title update only loads if it was made for the Media ID of the game you have.",
                "You never have to work them out: open the game's default.xex and the XEX Tool and Title Updates pages show both.");
            E(l, c, "What are the Profile ID, Console ID and Device ID?",
                "Profile ID: the 16-character code of one player profile; it starts with E0000. A save belongs to one profile ID.",
                "Console ID: the code of the console that wrote the file.",
                "Device ID: the code of the storage device (hard drive, memory unit, USB drive) the file was written on.",
                "Some games refuse a save if these do not match the profile, console or device now in use. The Saves page can set them.");
            E(l, c, "What is a keyvault (KV) and a CPU key?",
                "The keyvault is a small block of secret data inside each console that holds its identity: serial number, console certificate and private keys. Xbox LIVE recognises a console by it, and bans are bans of the keyvault.",
                "The CPU key is a 32-character code unique to each console's processor. It is the password that unlocks that console's keyvault and system memory (NAND). People who install RGH read it once and write it down.",
                "Never share either one, and never use somebody else's keyvault from the internet. This program does not contain anyone's keyvault and keeps yours only in memory while it runs.");
            E(l, c, "What is NAND? What is \"flashing\"?",
                "The NAND is the small memory chip on the console's mainboard that holds its system software - a different thing from the hard drive that holds your games. " +
                "\"Flashing\" means writing a new system image onto it, which is how an RGH console gets its modified system.",
                "This is the risky part of console modding and is done with J-Runner by whoever installs the chip. X360GameHack does not flash anything.");
            E(l, c, "What do hex, byte, offset and address mean?",
                "A byte is the smallest piece of a file: a number from 0 to 255. Files are just long rows of bytes.",
                "Hex (hexadecimal) is a way of writing numbers with 16 digits: 0-9 and then A-F. One byte is always two hex digits, from 00 to FF. \"0x\" in front of a number just says \"this is hex\": 0x10 is sixteen.",
                "An offset is a position counted from the start of a file: offset 0 is the first byte.",
                "An address is a position in the console's memory while a game runs. Xbox 360 game code normally starts at address 0x82000000.");
            E(l, c, "What does big-endian mean?",
                "It is the order in which the bytes of a number are stored. The Xbox 360 stores the biggest part first: the number 1 as four bytes is 00 00 00 01. " +
                "A Windows PC stores it the other way round (01 00 00 00).",
                "This program always shows and accepts Xbox values the Xbox way, so you can type the number as you read it.");
            E(l, c, "What do signed, hashed, rehash and resign mean?",
                "A hash is a short fingerprint calculated from data. If one byte of the data changes, the fingerprint no longer matches. Packages store fingerprints of everything inside them, so the console notices any change and calls the file corrupt.",
                "Rehash: calculate all the fingerprints again after you changed something, so they match.",
                "A signature is a fingerprint locked with a secret key, proving who made the file. Resign: sign the file again after rehashing. " +
                "Hacked consoles do not check the signature; normal consoles do.");
            E(l, c, "What are FTP and XBDM?",
                "Both are ways for the PC to talk to the console over your home network.",
                "FTP is a general way of copying files. Aurora, FSD and XeXMenu each contain an FTP server. It is the fastest way to send big things like games.",
                "XBDM is the debug service developers used. On a hacked console it is added as a DashLaunch plugin (xbdm.xex). It can copy files too, but more slowly; what it adds is control: " +
                "screenshots, starting games, reading and changing a running game's memory, and debugging.");
            E(l, c, "What is homebrew?",
                "Programs written by hobbyists rather than game companies: dashboards like Aurora, emulators, media players, tools like XeXMenu. They only run on hacked consoles.");
        }

        // ------------------------------------------------------------------ USB
        private static void Usb(List<QaEntry> l)
        {
            const string c = "USB drives";
            E(l, c, "What kind of USB drive do I need?",
                "Any USB flash drive or USB hard drive works. The console's ports are USB 2.0, so a USB 3 drive works but is no faster than USB 2 speed.",
                "The drive must be formatted as FAT32 (see the next questions). A current dashboard accepts drives up to 2 TB.",
                "A USB hard drive without its own power plug sometimes does not get enough power from one console port. If it clicks or disappears, use a powered USB hub or a Y-cable.");
            E(l, c, "What do \"format\" and \"file system\" mean?",
                "A file system is the way a drive keeps its list of files - like the index of a book. FAT32, exFAT and NTFS are three different indexes. A device can only read a drive whose index it understands.",
                "Formatting writes a new, empty index onto the drive. EVERYTHING on the drive is lost when you format it, so copy off anything you want to keep first.");
            E(l, c, "Why must the USB drive be FAT32?",
                "FAT32 is the only USB file system the Xbox 360 reads. Drives formatted as exFAT or NTFS (what Windows picks for most big drives) simply do not show up on the console, in Microsoft's dashboard or in Aurora.");
            E(l, c, "How do I see what format my USB drive has now?",
                "Open File Explorer (the folder icon on the Windows taskbar), click This PC, right-click the USB drive and choose Properties. The line \"File system\" says FAT32, exFAT or NTFS.",
                "Console Setup in this program also shows it and warns you if the chosen drive is not FAT32.");
            E(l, c, "How do I format a USB drive of 32 GB or smaller as FAT32?",
                "1. Copy anything you want to keep off the drive.\n" +
                "2. Open File Explorer > This PC.\n" +
                "3. Right-click the USB drive > Format...\n" +
                "4. Under File system choose FAT32. Leave Allocation unit size on Default.\n" +
                "5. Leave Quick Format ticked and press Start, then OK.",
                "Check twice that you picked the USB drive and not another drive.");
            E(l, c, "Windows does not offer FAT32 for my drive. Why, and what do I do?",
                "Windows' own Format window only offers FAT32 for drives up to 32 GB. Bigger drives can still be FAT32 - Windows just refuses to make it.",
                "Easiest fix: let the console do it. Plug the drive into the Xbox 360 and go to Settings > System > Storage, select the USB device and choose to format / configure it. " +
                "A current dashboard formats it as plain FAT32.",
                "Or on the PC use a small free FAT32 formatting tool (commonly called \"guiformat\" or \"FAT32 Format\"): pick the drive letter, leave everything else alone, press Start.");
            E(l, c, "What is the 4 GB limit?",
                "On FAT32 a single file cannot be bigger than 4 GB (exactly 4 GB minus one byte). The drive can be far bigger; only each single file has the limit.",
                "A disc image (ISO) is 7 to 8 GB in one file, so Windows says \"The file is too large for the destination file system\". " +
                "The answer is never to copy the ISO itself. Either extract it to a folder (the game becomes many smaller files) or convert it to Games on Demand (it is cut into pieces of about 170 MB). Both are on the Xbox ISOs page.",
                "A few games have one file over 4 GB even after extracting. Those need Games on Demand format on USB, or the console's internal hard drive, which has no such limit.");
            E(l, c, "My drive shows less space than it says on the box, or Windows cannot format it. What now?",
                "Makers count 1 GB as 1,000,000,000 bytes and Windows counts it as 1,073,741,824, so a \"64 GB\" drive shows about 59 GB. That is normal.",
                "If the drive shows only a tiny size, or has several odd partitions, it was probably set up by another device. In Windows open Disk Management (right-click the Start button > Disk Management), " +
                "find the USB drive by its size, delete the volumes on it and create one New Simple Volume. Be very careful to pick the USB drive and not your PC's disk.",
                "The drive's partition style should be MBR, not GPT. Drives of 2 TB and under formatted by the console or by the FAT32 tools are MBR already.");
            E(l, c, "Where do things go on the USB drive?",
                "Packages (Games on Demand, arcade games, DLC, title updates, themes, gamer pictures, saves, profiles) go in a fixed folder tree that the console looks in:\n" +
                "Content\\<profile ID or 0000000000000000>\\<Title ID>\\<type code>\\<the file>",
                "Extracted games (a folder with default.xex in it) can go anywhere. The usual place is a folder called Games in the top level of the drive, one folder per game.",
                "You do not need to build any of this by hand: Console Setup reads each package and puts it in the right place.");
            E(l, c, "What are the type code folders (00007000, 000D0000 ...)?",
                "Each kind of package has an 8-character type code, and the folder is named after it:\n" +
                "00000001 - saved game\n" +
                "00000002 - DLC (marketplace content)\n" +
                "00004000 - a disc game installed to the hard drive by the dashboard\n" +
                "00005000 - original Xbox game\n" +
                "00007000 - Games on Demand\n" +
                "00009000 - avatar item\n" +
                "00010000 - profile\n" +
                "00020000 - gamer picture\n" +
                "00030000 - theme\n" +
                "00080000 - demo\n" +
                "000B0000 - title update\n" +
                "000D0000 - Xbox LIVE Arcade game",
                "Open any package on the X360Packages page and the Overview shows its type.");
            E(l, c, "What is the 0000000000000000 folder?",
                "The folder directly under Content says who owns what is inside. Sixteen zeros means \"everyone on this console\" - games, DLC and updates go there. " +
                "A folder named after a profile ID (E0000...) holds that one player's saves and the profile itself.");
            E(l, c, "The console does not see my USB drive at all.",
                "Go through these in order:\n" +
                "1. Is it FAT32? (Most common cause.)\n" +
                "2. Try a different USB port, preferably one on the back of the console.\n" +
                "3. USB hard drive: does it get enough power? Try a powered hub.\n" +
                "4. Is it MBR with one partition? See the question about odd sizes.\n" +
                "5. Try another drive. A few flash drives never work with the 360.",
                "In Aurora the first drive is called Usb0, the second Usb1. In Microsoft's dashboard it appears under Settings > System > Storage.");
            E(l, c, "Is running games from USB slower than from the hard drive?",
                "A little. Loading from a decent USB drive is about the same as from the disc drive or better; the internal hard drive is the fastest. Cheap flash drives can cause stutter in games that stream a lot of data. " +
                "A USB hard drive or SSD in an enclosure works well.");
        }

        // ------------------------------------------------------------------ Hard drives
        private static void HardDrives(List<QaEntry> l)
        {
            const string c = "Hard drives";
            E(l, c, "What format is the Xbox 360's own hard drive?",
                "It uses Microsoft's Xbox file system, called FATX (the 360 version is also called XTAF). Windows does not understand it at all.",
                "It has no 4 GB file limit, but it has two limits of its own: a file or folder name can be at most 42 characters long, and these characters are not allowed in names: " +
                "< > : \" / \\ | ? * and a few others such as + , ; = [ ]. Keep game folder names short and plain.");
            E(l, c, "I plugged the Xbox drive into my PC and Windows asks to initialize or format it. Should I?",
                "NO. Press Cancel. Windows asks because it cannot read the Xbox file system and thinks the drive is empty. If you let it initialize or format the drive, everything on it is gone.",
                "Open the drive with Drive Explorer in this program instead.");
            E(l, c, "How do I connect an Xbox 360 hard drive to my PC?",
                "Inside the plastic case of every Xbox 360 hard drive is an ordinary 2.5-inch SATA laptop drive. On Slim and E consoles the drive slides out of the console in a small caddy; " +
                "on the older \"fat\" consoles the grey box on top opens with small screws.",
                "Connect the bare drive with a SATA-to-USB adapter cable (the cheapest option), a USB drive enclosure, or a free SATA port inside a desktop PC.",
                "Then install X360GameHack with its installer (the portable version cannot read real disks), open Drive Explorer and choose the drive. Windows asks once for permission for the small disk reader.");
            E(l, c, "What can Drive Explorer do with the drive?",
                "It shows every partition and folder on Xbox 360 and original Xbox drives - real drives and image files of a drive - and copies files and folders from it to your PC. " +
                "Its Saves view lists every save on the drive by game, Check drive tests the whole file system for damage, and Back up whole drive... copies every sector into one image file with its SHA-256.",
                "A real drive - original Xbox or Xbox 360 - opens ready for changes (Windows asks for permission once): add, rename and delete there too; before any sector changes, what it held is saved in an undo journal, and every write is read back. " +
                "Undo changes... puts a session back - only while nothing else has changed the drive since. Once the drive has been in a console, Undo refuses (the console may have added files); delete what you added instead. " +
                "File times are shown in this PC's time; an Xbox 360 drive keeps them in UTC, as the console does. " +
                "To put files on the console's drive without taking it out, use FTP or the Neighborhood page over the network, or Console Setup.");
            E(l, c, "The folders on the drive have numbers for names. How do I find a game, its DLC or its title update?",
                "Drive Explorer says it for you in the \"What it is\" column: a game's folder (its Title ID, such as 4156081C) shows the game's name when this PC knows it, " +
                "the folders inside show what they hold (Title Updates, Marketplace Content (DLC), Saved Games...), and every package says its own name and kind, " +
                "read from the package itself - nothing is looked up online.",
                "Find (next to the partition list) finds every file and folder whose name holds what you type - a Title ID, default.xex, part of a folder name - " +
                "with the folder each is in; double-click one to open its folder with it selected. Properties shows everything about the selected item: its folder, " +
                "size, date and clusters, and for a package its name, game, Title ID and profile. The path line shows the partition's free space.");
            E(l, c, "How do I see every partition on my Xbox drive?",
                "Drive Explorer > open the drive (a drive image, or Open physical drive...) > the Partitions tab. A map of the whole drive comes first - each partition's share of " +
                "it - then a row for each partition in the order it sits on the drive: its letter or name, its label, size, how much is free, its file system and cluster size, and " +
                "where it starts. Click one (or press Enter on it) to open it in Files; the partition list next to Open does the same.",
                "Original Xbox: X: Y: Z: (the game caches), C: (the dashboard), E: (saves and settings) and, on a drive bigger than the stock 8 GB, F: and G: - read from the " +
                "drive's own partition table when it has one (XBpartitioner writes one), else from where the Xbox itself looks. A partition in the table that cannot be read says " +
                "why (not formatted, or damaged), and space no partition uses shows too, so nothing on the drive is hidden.\n" +
                "Xbox 360: the two caches (the console's own working data - never opened as files), SysExt, SysExt2, the compatibility partition (HddX) and Data.");
            E(l, c, "Can I drag files off an Xbox drive straight into Windows?",
                "Yes. In Drive Explorer's Files list, select files or folders and drag them into a folder in Windows Explorer, onto the desktop or into any program that takes " +
                "dropped files. Windows copies them straight off the drive with its own progress window - folders come with everything in them - and X360GameHack keeps working " +
                "meanwhile. The drive is only read.",
                "While Windows copies, the drive stays open here (the status bar says so): opening another drive asks first. Names Windows cannot take are made safe on the way " +
                "(a name can never put a file outside the folder you dropped on). To copy onto the drive, drag files from Windows into the list after Allow changes.");
            E(l, c, "How do I make (format) an Xbox drive - a new drive, or a drive image?",
                "Drive Explorer > Drive tools > Make Xbox drive... Pick what becomes an Xbox drive - a new drive image file, the image or drive that is open, or a drive connected to " +
                "this PC (a new, blank one too) - then the console (original Xbox or Xbox 360) and, for an original Xbox drive, the layout. Every partition it gets is shown before " +
                "anything is written.",
                "Xbox 360: laid out exactly as the console lays out its own drives (checked against a real one). Nothing in front of SysExt is written: the drive's security sector and " +
                "the console's caches stay as they are. A console that is not modded only accepts drives sold for it (each has its own security sector) - this does not change that; " +
                "on a modded console you can also format the drive in the console itself.\n" +
                "Original Xbox: C: E: X: Y: Z: as every console has them, F: and G: the way big-drive BIOSes and XBpartitioner lay them out, with XBpartitioner's partition table at " +
                "sector 0. Everything starts empty: put a dashboard on C: before the drive goes into the Xbox. A drive for an Xbox that is not modded must also be locked to it, which " +
                "X360GameHack does not do.",
                "A real drive is written through the Disk Reader: a drive Windows uses is refused, and every block is journaled first, so Undo changes can put the drive back. " +
                "A new image file is sparse - only what is written takes room. Making drives is BETA: what it makes reads back exactly, but no drive made here has been in a console yet.");
            E(l, c, "How do I back up or move my original Xbox saves?",
                "Drive Explorer > open the original Xbox drive > Saves. Every save in E:\\UDATA is listed by its game and its own name, as the dashboard shows them. Select saves > " +
                "Copy to PC... - each game gets a folder (\"4541000d - Black\") with the saves and the game's TitleMeta.xbx and pictures, so they can go back onto any " +
                "original Xbox drive.",
                "To put them on a drive: open it, press Allow changes, then Saves > Put a save back... and pick that game folder. A save already on the drive is never replaced - " +
                "delete it there first if you mean to. Some games tie a save to the console that made it - such a save may not load on another Xbox.");
            E(l, c, "Which layout should I pick for a big original Xbox drive?",
                "F: up to 137 GB, G: the rest (the recommended one) - what BIOSes with big-drive support (LBA48) use when they find no partition table, and what most do. " +
                "F: takes the whole rest - for a BIOS set to \"F: takes all\". F: of a size you choose - for a BIOS or dashboard that reads XBpartitioner's partition table (Cerbios and " +
                "others). F: up to 137 GB only - for an old BIOS without big-drive support.",
                "Whatever you pick, the partition table at sector 0 describes exactly what was made, so a BIOS that reads it and one that does not see the same drive - as long as the " +
                "BIOS's own setting matches the layout. The original Xbox can use up to 2 TB. Partitions over 256 GB get bigger clusters (32 KB, 64 KB over 512 GB, 128 KB over 1 TB), " +
                "as XBpartitioner gives them.");
            E(l, c, "Can I get deleted files back from an Xbox drive?",
                "Often, yes. Deleting on an Xbox drive only marks the file's entry as deleted and frees its space; the name, size and data stay until something else is written there. " +
                "Drive Explorer > pick the partition > Recover deleted... lists everything deleted it still finds - files, deleted folders with what they held, and lost data (space marked used that no file owns any more) - " +
                "each marked Recoverable, Partly overwritten or Overwritten, with what its first bytes say it is. Tick what you want; it is copied to a folder on this PC, into the folders it was in.",
                "The drive is only read, never changed. Works on Xbox 360 and original Xbox drives and on drive images. The sooner after the delete, the better: every new file written to the drive can take the space. " +
                "Recoverable means the space is free now; a file the console wrote in pieces, or whose space another file used for a while, can come back mixed - check Xbox packages with Verify integrity.");
            E(l, c, "How do I unplug the Xbox drive from my PC safely?",
                "Drive Explorer > Safely remove drive (Windows asks for permission). It closes the drive, flushes its cache, spins it down and has Windows let go of it - also on an internal SATA port, " +
                "where Windows' own Safely Remove Hardware does not offer the drive. When it says the drive can be unplugged, unplug it.",
                "It only ever removes a drive with an Xbox file system, never the Windows disk, and it writes nothing to the drive. " +
                "Changed your mind and the drive is still connected? Drive Explorer > Bring back drive starts it again (Windows asks for permission) - a hardware scan alone does not. " +
                "Unplugged and plugged in again, it usually shows up by itself; if not, Bring back drive looks for it too.");
            E(l, c, "What is a drive image, and can I change one?",
                "A drive image is one big file that holds an exact copy of a whole hard drive or of one partition - what backup tools and emulators (xemu for the original Xbox) work with.",
                "Drive Explorer can change image files. Open the image, press Allow changes and confirm; from then on you can:\n" +
                "- Add files... / Add folder... or drag files from Windows onto the list - they go into the folder shown.\n" +
                "- Install packages... - Games on Demand, arcade games, DLC, title updates and saves are each put into the right Content folder, exactly as Console Setup does for a USB drive.\n" +
                "- New folder, Rename, Delete.\n" +
                "- Check drive - confirms the image is still consistent.",
                "Names follow the Xbox rules (42 characters at most, no characters such as + , ; = * ? : and the like); a copy with a bad name is stopped before anything is written, and the message lists the names to fix. " +
                "Changes are written straight into the image file and cannot be undone, so keep a spare copy of the image.");
            E(l, c, "Can I write to the real hard drive from the PC? Is it safe?",
                "Yes - Drive Explorer > Allow changes (Windows asks for permission). It is made to be undone: before any sector changes, what it held is saved in an undo journal on this PC, " +
                "every write is read back, and Drive tools > Undo changes... puts a whole session back. Writing is still marked BETA: it is proven on drive images and on the user's own " +
                "drive image, not yet on many real drives - keep a backup (Drive tools > Back up whole drive...).",
                "Undo refuses once something else changed the drive (it was in a console since) - then delete what you added instead. " +
                "Without taking the drive out, FTP or the Neighborhood page send files over the network (about 9 MB per second with a network cable).");
            E(l, c, "What are Hdd1, HddX, Usb0, Flash and the other drive names?",
                "They are the names the console gives its storage, and what you see in Aurora, FTP and Neighborhood:\n" +
                "Hdd1 (called Hdd over XBDM) - the main part of the internal hard drive. Your games and Content folder are here.\n" +
                "HddX - a small system part of the hard drive.\n" +
                "Usb0, Usb1 ... - USB drives, in the order they were found.\n" +
                "Flash - the console's internal system memory. Leave it alone.\n" +
                "IntMu / Mu0 - memory units, including the 4 GB built into some Slim consoles.\n" +
                "Dvd - the disc in the drive.\n" +
                "Game - a shortcut to the folder of the game that is running.");
            E(l, c, "Can I put any hard drive in my Xbox 360?",
                "A normal (retail) console only accepts drives that carry Microsoft's security data, which is why official drives cost more. " +
                "A hacked (RGH / JTAG) console accepts any 2.5-inch SATA drive: put it in, then format it from the console (Settings > System > Storage, or Aurora's file manager).",
                "Any console, hacked or not, accepts a FAT32 USB drive up to 2 TB, which is the simple way to add space.");
            E(l, c, "What is the fastest way to copy a game to the console's hard drive?",
                "From fastest to slowest:\n" +
                "1. Copy to a USB drive on the PC, plug it into the console and copy from Usb0 to Hdd1 with Aurora's file manager (or just play from USB).\n" +
                "2. FTP with a network cable. The console's network port is 100 Mbit, so expect roughly 8 to 11 MB per second - a 7 GB game takes around 12 to 15 minutes.\n" +
                "3. FTP over Wi-Fi - works, but several times slower.\n" +
                "4. XBDM (Neighborhood page) - handy because it is already connected; usually somewhat slower than FTP for whole games, and one file can be at most 4 GB.",
                "The program tells you this once, the first time you start a big copy over XBDM.");
        }

        // ------------------------------------------------------------------ Network
        private static void Network(List<QaEntry> l)
        {
            const string c = "Connecting to the console";
            E(l, c, "How do I change launch.ini, xbdm.ini or JRPC.ini on my console?",
                "Open Console Setup > INI Editor, connect, pick the file's tab and press Load from console. The file is found on the console for you. Settings are listed with what each one does; double-click a switch to flip it, or any other setting to type a value. Then press Send to console.",
                "The file is kept exactly as it was apart from the lines you change, the console's current copy is saved to your Saves folder (Backups) first, and what was sent is read back and compared. Values that cannot be right - a plugin path without a drive, a switch that is not true or false - are refused before anything is sent.",
                "The console reads these files when it starts, so a change takes effect after the next restart.");
            E(l, c, "How do I add a plugin such as xbdm.xex, JRPC2.xex or XRPC.xex?",
                "Console Setup > INI Editor > launch.ini > Load from console, then press Add plugin: XBDM, JRPC2 or XRPC. It goes into the first free one of the five plugin slots. The plugin file itself must be in the root of that drive on the console - the editor checks and tells you if it is missing. Send to console, then restart the console.",
                "Keep xbdm.xex in the list: without it this program cannot reach the console at all.");
            E(l, c, "How do I use a game mod?",
                "Open Mods. On the Game mods tab pick the game, then a group on the left, then an option. If the option needs a value there is a box for it. Press Apply (or Turn on / Turn off for a switch). The find box searches every group at once.",
                "The console must be connected and running that game - the program checks the running game first and refuses if it is another one, because a mod's addresses only mean something inside its own game. Values are written over XBDM; an option that runs a game command also needs the JRPC2 or XRPC plugin.",
                "A mod is made for one title update of its game - normally the latest - and says which under the game's name. The program reads the update the console is running and, if it is a different one, tells you and asks before anything is sent, because on another update the addresses point at the wrong places. Title Updates > Check & install puts the right update on the console.");            E(l, c, "What are mods (.x360mod files) and are they free?",
                "X360GameHack itself, and every console tool in it, is free and open source. Mods are optional add-ons: one .x360mod file each, for a game (changes values in the running game) or for a save game (named values and one-click actions on the Saves page).",
                "A mod can be free (open: it simply loads) or paid (sealed: you unlock it with a licence token from whoever sells it). Put the files in the mods folder - Mods > Open mods folder - and press Refresh.");
            E(l, c, "How do I unlock a paid mod?",
                "Put the .x360mod files in the mods folder (Mods > My mod files > Open mods folder), paste your licence token under Licence and press Unlock my mods. One token unlocks all of your paid mods at once. With Remember this token ticked, they unlock by themselves each time the program starts.",
                "A token is tied to the first PC that uses it (the PC id shown on the page). On another PC it is refused; ask the seller to move it. Unlocking checks the token online, so it needs an internet connection, and a paid mod's contents are never written to disk.");
            E(l, c, "How do I lock a mod again?",
                "Closing the program locks every paid mod: its key and its data only ever exist in memory and are wiped. Mods > My mod files > Lock selected takes one out of the tools straight away; it stays locked until you unlock it again or restart the program.",
                "With Remember this token ticked, your mods unlock by themselves each time the program starts. Forget token locks them and removes the remembered token from this PC, so they stay locked until the token is typed in again. The licence is not affected: the same token still works on this PC. Free mods are always loaded and cannot be locked.");
            E(l, c, "How do I make my own mod file?",
                "Open Mods > Mod Maker. Fill in every field of the mod - id, name, game, Title ID, what it needs and a one-line description; buyers see them all, and the Title ID must be the one in the payload. Press Insert game template or Insert save template: the template is a worked example full of notes (the lines that start with //) explaining every part. Change it to your mod, pick the protection and press Build.",
                "The payload box colors what you write (notes, entry names, text, numbers and true / false / null each have their own color; a word in red needs \"double quotes\") and checks it as you type: the line under the box says what is wrong and on which line, in plain words (a missing comma, a misspelt name, a number in quotes...), and that line is tinted red. Check syntax also selects that line and marks any empty field in red. Inserting a template replaces the box straight away - Ctrl+Z puts back what was there. Notes are removed before the mod is built.",
                "Open (free): readable, loads for everyone. Protected (paid): sealed with a new key; a key card is saved beside the file for your licence server and the program keeps no copy. Server-only: no data in the file at all; your server delivers it at unlock.",
                "Game mod values are written over XBDM; calls into game functions go through JRPC2, then XRPC. Scripts can do the same with the mod-validate and mod-build commands.");
            E(l, c, "Does the program use XBDM, JRPC2 or XRPC?",
                "Always XBDM when XBDM can do the job - and that is nearly everything: files, saves, memory, screenshots, launching, the debugger, the ini files. Only for the few things XBDM has no command for does it use a plugin: JRPC2 first, and XRPC when JRPC2 is not loaded or answers with an error.",
                "Today that means: pop-up notifications go JRPC2, then XRPC. Temperatures and the ring of light need JRPC2 (XRPC cannot do them). The log line says which route was used.");            E(l, c, "What is JRPC2, and does this program need it?",
                "JRPC2 is a plugin for modified consoles (JRPC2.xex, loaded from launch.ini) that lets a PC ask the console to do things XBDM has no command for. This program does not need it: files, saves, memory, screenshots, launching games and the debugger all work over plain XBDM.",
                "It is used by the Notify & Sensors tab of the Console page, for the three things XBDM cannot do: pop-up notifications with any of the console's 67 pictures, reading the temperature sensors, and setting the ring-of-light LEDs. If JRPC2.xex is not loaded, that tab says so and sends nothing. Game mods that run a game command use it as well.",
                "The commands are the same ones the published JRPC2 client sends, so they behave as they do in any other JRPC2 tool.",
                "One known clash: with InfinityOps.xex loaded as well, the console hangs on the first JRPC2 command and has to be switched off and on. This program checks for it and sends nothing while both are loaded - take InfinityOps out of launch.ini (INI Editor) and restart the console.");
            E(l, c, "How do I show a message on my console's screen?",
                "Open Console > Notify & Sensors, connect, type the text, pick a picture in the list and press Send. The pop-up appears on the console like an achievement or a friend message would. Show every picture runs through all 67 so you can see which is which.",
                "The text takes plain letters, digits and punctuation, up to 200 characters. This needs the JRPC2 plugin on the console.");            E(l, c, "How do I connect my console and PC?",
                "Simplest: plug the console into the same router as your PC with a network cable. Both then get an address from the router and can see each other.",
                "Wi-Fi also works, with limits: the Xbox 360 only joins 2.4 GHz networks and only with WPA2 or older security (not WPA3, not 5 GHz-only networks).",
                "No router nearby: run a network cable straight from the console to the PC and switch on internet sharing on the PC (see the next question but one).");
            E(l, c, "What is an IP address and how do I find the console's?",
                "An IP address is the console's number on your home network, four numbers with dots such as 192.168.1.50. The PC needs it to know where to send things.",
                "In Aurora it is shown on the main screen (bottom corner) and under Settings. In Microsoft's dashboard: Settings > System > Network Settings > pick your network > Configure Network; the IP Address line shows it.",
                "The Neighborhood page also has a Discover button that searches your network for consoles running XBDM.",
                "The address can change when the console or router restarts. If connecting suddenly fails, read the address again.");
            E(l, c, "How do I connect the console directly to my PC with a cable?",
                "1. Plug a network cable from the console into the PC's network port. The PC keeps using Wi-Fi for internet.\n" +
                "2. On the PC press the Windows key, type \"view network connections\" and press Enter.\n" +
                "3. Right-click the Wi-Fi adapter > Properties > Sharing tab.\n" +
                "4. Tick \"Allow other network users to connect through this computer's Internet connection\", choose the cable (Ethernet) adapter in the list below it, press OK. Windows asks for administrator permission.\n" +
                "5. On the console run the network test (Settings > System > Network Settings > Test Xbox LIVE Connection). It now gets an address that starts with 192.168.137.",
                "Important: Windows often leaves sharing switched off after the PC restarts. If the console connected yesterday and not today, open the Sharing tab, untick the box, OK, tick it again, OK, and run the console's network test again.");
            E(l, c, "How do I copy files with FTP?",
                "1. On the console start Aurora (its FTP server is always on), FSD, or XeXMenu.\n" +
                "2. In this program open the FTP page and make a profile: the console's IP address, port 21, user name xbox, password xbox.\n" +
                "3. Press Connect. The left side is your PC, the right side the console. You see folders such as Hdd1 and Usb0.\n" +
                "4. Drag files or folders across, or onto the list, to copy them.",
                "Game folders usually go in Hdd1/Games/. Packages are easier to install with Console Setup, which builds the Content folders for you over the same FTP profile.");
            E(l, c, "What is XBDM and how do I switch it on?",
                "XBDM (Xbox Debug Monitor) is a background module that lets the PC control the console on network port 730. The Neighborhood, Console, Peek Poker and Debugger pages all need it.",
                "To switch it on:\n" +
                "1. Copy the file xbdm.xex to the console's hard drive, for example into Hdd1.\n" +
                "2. In Console Setup open the launch.ini editor, set plugin1 to the path of that file (for example Hdd:\\xbdm.xex) and save it to the console.\n" +
                "3. Restart the console once. From then on XBDM starts by itself.",
                "Then open Neighborhood, type the console's IP address and press Connect.");
            E(l, c, "It connected before and now it will not connect. What changed?",
                "The program now tells you what it found when a connection fails. The usual causes, most common first:\n" +
                "1. The console's IP address changed after a restart. Read it again on the console.\n" +
                "2. Console cabled directly to the PC: Windows internet sharing switched itself off when the PC restarted. Turn it off and on again (see the direct cable question).\n" +
                "3. The console is off or showing an error screen.\n" +
                "4. XBDM is not loaded: the plugin line is missing from launch.ini, or xbdm.xex is not where the line says.\n" +
                "5. The Windows firewall is blocking the program: allow it on private networks.\n" +
                "6. Too many connections: the console's debug service holds only a few at a time. Programs that connect again for every command, or close without saying goodbye, use them up, and then it times out or refuses new ones. " +
                "This program connects once and keeps that one connection for everything, JRPC2 commands included. Close other tools that talk to the console, wait a minute, then press Connect once.",
                "You do not need to restart the console for the first two. For the last one, restart it only if it still refuses after a minute.");
            E(l, c, "What can the Neighborhood page do?",
                "It is a file browser for the console over XBDM: look through every drive, copy files both ways, rename, delete, make folders, and start a .xex. " +
                "Its Saves, Profiles and Title updates tabs find every save, gamer profile and title update on the console's drives - with the game, gamertag, gamerscore or update number - to open, download, replace, install or delete, each one backed up first.",
                "The power buttons (reboot, shut down) do exactly that straight away, so save your game first.");
            E(l, c, "Does this program use JRPC, XRPC or other RPC plugins?",
                "Only for the few things plain XBDM cannot do. Files, saves, memory, screenshots, launching games, the debugger and the ini files all use plain XBDM. " +
                "Pop-up notifications go through JRPC2 (or XRPC when JRPC2 is not loaded); the temperature sensors and the ring of light need JRPC2. If neither plugin is loaded, those few buttons say so and send nothing.");
            E(l, c, "Is it safe to leave FTP and XBDM on?",
                "Neither has real password protection, so anyone on the same network can reach the console's files. On your own home network that is fine. " +
                "Do not use them on public or shared networks, and never forward ports 21 or 730 from the internet to your console.");
        }

        // ------------------------------------------------------------------ Installing games
        private static void InstallGames(List<QaEntry> l)
        {
            const string c = "Installing games";
            E(l, c, "What forms can an Xbox 360 game come in?",
                "1. A disc image: one big .iso file, an exact copy of the disc.\n" +
                "2. An extracted game: a folder full of files, one of which is default.xex. Also called a \"JTAG rip\" or \"XEX folder\".\n" +
                "3. A Games on Demand (GOD) package: one small file with a long hex name plus a folder of the same name ending in .data.\n" +
                "4. An Xbox LIVE Arcade (XBLA) package: one file with a long hex name.",
                "Drop whatever you have onto the program and it says which it is.");
            E(l, c, "I have an ISO. How do I get it onto my hacked console, step by step?",
                "Way 1 - extracted folder (best for most people):\n" +
                "1. Drop the .iso onto the program. The Xbox ISOs page opens.\n" +
                "2. Press Extract... and choose an empty folder on your PC, named after the game.\n" +
                "3. Copy that whole folder to the console: to a FAT32 USB drive into a folder called Games (Console Setup > Copy game folder to USB...), or over FTP into Hdd1/Games/.\n" +
                "4. On the console, in Aurora: Settings > Content > Scan Paths, add the Games folder (scan depth 2), then let it scan. The game appears in the list with its cover.\n" +
                "5. Start it.",
                "Way 2 - Games on Demand: press Convert to Games on Demand (or use the Convert tab for several games at once), then install the result with Console Setup - " +
                "or let the Convert tab put it on the USB drive or the console by itself (When a package is done). The game then also shows up in Microsoft's own dashboard under Games.");
            E(l, c, "How does the Convert tab work, and which options should I pick?",
                "Xbox ISOs > Convert is ISO2God and god2iso built into the program, and more: any form of a game into any other. Add disc images (Add images), Games on Demand packages " +
                "(Add packages - the file next to its .data folder) or games in their XEX version - an extracted folder with default.xex, or default.xbe for an original Xbox game " +
                "(Add game folders) - mixed as you like, or drop them on the list. Choose what they become under Convert to: Games on Demand, Disc image (ISO) or XEX version (folder). " +
                "The name, IDs, disc number and picture are read from the game itself; double-click an entry to change any of them. Then press Convert all.\n" +
                "Padding removal: None keeps the whole image; Partial cuts it after the last file (quick); Full packs every file tightly together - the smallest package, " +
                "and here it costs no extra time or disk space. Full is ISO2God's own default and the one to use.\n" +
                "Made for: RGH / JTAG, or Devkit. Unlock the game's XEX removes its limits with xextool (the same patch as Bulk extract); for a devkit the XEX is always converted to devkit form, " +
                "because a devkit runs nothing else. Unlock the package runs X360PkgTool's unlock on the finished package (-z -b 1 -m 1 for RGH, -m 0 for a devkit). " +
                "Your image or folder is never changed - the XEX is patched on a copy.\n" +
                "Add \"Disc N\" names the discs of a multi-disc game apart. Package file names like ISO2God mean converting the same disc again replaces the old package instead of adding a second one.",
                "Every block of the package is hashed and the finished package is checked from top to bottom before it says Done. The packages it makes are byte for byte what ISO2God makes " +
                "(Microsoft's own packages in the header fields), and an image made back from a package holds the game's files byte for byte. Made for RGH / JTAG or Devkit works " +
                "for every output: an image or folder made for a devkit holds the devkit-form XEX too. As it is changes nothing - the result is what ISO2God, god2iso or extract-xiso would make.");
            E(l, c, "Will a disc image made here work on my console?",
                "On an RGH / JTAG console, yes - as Games on Demand (Convert) or extracted (Extract...), and in emulators. Burned to a disc for a console with a flashed DVD drive, no: an image " +
                "made here holds only the game partition, packed anew, without the stealth sectors (SS, DMI, PFI, video) such a drive needs.",
                "The Convert tab's \"Stealth-patch each new image with abgx360\" asks abgx360 to add those from its verified database. abgx360 can only do that to a complete, unaltered game " +
                "partition with the game's disc XEX (for example a package made without padding removal, converted back with Every sector in place). A repacked image is shorter than the disc " +
                "and lacks its random padding, a Games on Demand edition has an XEX no disc has, and a patched XEX is not in the database - the status line says which, and the image is left as it is.");
            E(l, c, "Can original Xbox games be made into Games on Demand packages?",
                "Yes - an original Xbox disc image or game folder (default.xbe) becomes a package of type 00005000 (Content\\0000000000000000\\<title ID>\\00005000), with the name and picture " +
                "taken from the XBE, exactly as ISO2God makes it. Full original Xbox disc images (with the video partition, \"redump\" layout) work too.",
                "The console plays them through its backward compatibility emulator, so the game must be one the emulator supports, and the emulator files must be on the hard drive " +
                "(Drive Explorer > Install emulator files...).");
            E(l, c, "How do I start a Games on Demand or arcade game from the PC?",
                "Neighborhood > Files: open Hdd:\\Content\\0000000000000000\\<title ID>\\00007000 (000D0000 for arcade, 00005000 for original Xbox), select the package - the file without an extension - " +
                "and press Launch package. Launch XEX only starts .xex files.",
                "Plain XBDM can start an .xex but cannot open a package, so starting one goes through JRPC2 on an RGH / JTAG console or the XDRPC every development kit has: it asks the console " +
                "to mount the package and start the game in it (xam.xex's own launcher). Whatever is running is closed first.");
            E(l, c, "Extracted folder or Games on Demand - which should I choose?",
                "Extracted folder: fastest to load, easy to mod (every file is right there), needs Aurora, FSD or XeXMenu to start it. Choose this on RGH / JTAG unless you have a reason not to.",
                "Games on Demand: shows up in Microsoft's own dashboard, keeps a two-disc game together, and always fits on FAT32 because it is cut into small pieces. Harder to mod.");
            E(l, c, "Should I skip the system update when extracting?",
                "Yes. The folder $SystemUpdate on a disc only exists so the disc can update a console. A hacked console must never install it, and leaving it out saves space. The extract options skip it by default.");
            E(l, c, "How do I make Aurora show my games?",
                "Aurora only lists games in folders you tell it to watch.\n" +
                "1. In Aurora press Start (or open Settings) > Content > Scan Paths.\n" +
                "2. Press Y to add a path, browse to the folder that holds your game folders (for example Usb0:\\Games or Hdd1:\\Games), set the scan depth to 2 and save.\n" +
                "3. Leave the menu; Aurora scans and adds the games. Covers download by themselves if the console has internet.",
                "Games on Demand and arcade games are found if you also add the Content\\0000000000000000 folder as a path, with a deeper scan depth (4 or more).");
            E(l, c, "How do I install a Games on Demand or arcade (XBLA) package?",
                "1. Open Console Setup.\n" +
                "2. Choose where it should go: a USB drive, the console over XBDM, or the console over FTP.\n" +
                "3. Drop the package files - or the whole folder they are in - onto the Install content list. For Games on Demand include the .data folder that sits next to the file; dropping the parent folder gets both.\n" +
                "4. Press Install all.",
                "The program reads the Title ID and type out of each package and builds Content\\0000000000000000\\<Title ID>\\<type>\\ for you. " +
                "By hand it would be: 00007000 for Games on Demand, 000D0000 for arcade games.");
            E(l, c, "My arcade game or DLC shows as a trial or is locked. Why?",
                "Packages carry a licence that says which console or profile bought them. On another console they fall back to trial mode.",
                "On RGH / JTAG: in launch.ini switch on contpatch (Console Setup has a tick box for it) and restart the console. It makes the console treat arcade games and DLC as licensed.",
                "Or change the package itself: open it on the X360Packages page and press Unlock DLC for RGH / exploited (or Make bought). The original file is kept as a backup.");
            E(l, c, "How do I install DLC?",
                "DLC is installed exactly like an arcade package: drop it on Console Setup > Install content and press Install all. It goes to Content\\0000000000000000\\<Title ID>\\00000002\\.",
                "Many DLC packs also need the game's latest title update, and need unlocking - see the question above.");
            E(l, c, "How do I install a two-disc game?",
                "Two kinds of two-disc game exist:\n" +
                "- Disc 2 continues the story (you swap discs while playing). Extract each disc to its own folder, or convert both to Games on Demand, which handles the swap for you.\n" +
                "- Disc 2 is an \"install disc\" or content disc (extra data the game copies to the hard drive). Its content packages must be installed into the Content folder.",
                "On the Xbox ISOs page add both images to the Bulk extract list and tick \"Install content packages found on the discs to the USB drive\" so the content is put in the right place automatically.");
            E(l, c, "Do I have to patch the game's XEX before it runs?",
                "On RGH / JTAG, usually not: the hacked system ignores most of the limits in a retail game, so an extracted game just runs.",
                "You do need to patch when: the console runs Bad Update or Bad Avatar (its system is not modified, so limits still apply - use the Bad Update / Bad Avatar card on the XEX Tool page); " +
                "the game is for a devkit; the game is locked to another region; or the game shows an error about the disc or media. " +
                "The one-click patches on the XEX Tool page handle all of these.");
            E(l, c, "The game says it needs an update, or asks for a newer dashboard or Kinect / avatar data.",
                "Three different things get called \"update\":\n" +
                "1. A title update for that one game - see the Title Updates section. Safe and normal.\n" +
                "2. Avatar and Kinect data. Some games will not start without it. It is installed from Microsoft's system update files placed on a USB drive in a folder renamed to $$ystemUpdate, " +
                "and must be the same version as the console's system. Whoever set up your RGH can tell you the version.\n" +
                "3. A newer system (dashboard) version. On a hacked console this is NEVER done by accepting the update on screen. It is done with J-Runner by rebuilding the system image. " +
                "If a game offers to update the console, say no.");
            E(l, c, "Can I copy a game from the disc with the console itself?",
                "Yes, on a hacked console: put the disc in and use Aurora's or FSD's disc copy function to copy it to the hard drive as an extracted folder. No PC needed.",
                "A normal console can \"Install\" a disc to its hard drive from Microsoft's dashboard, but still asks for the disc to be in the drive when you play.");
            E(l, c, "Do games from other countries work?",
                "Many discs are region-free. Region-locked ones refuse to start on a console from another region. " +
                "On the XEX Tool page the one-click patch removes the region limit from default.xex; on RGH you can also set a region in DashLaunch.");
        }

        // ------------------------------------------------------------------ Title updates
        private static void Updates(List<QaEntry> l)
        {
            const string c = "Title updates";
            E(l, c, "What is a title update?",
                "A patch for one game, released after the game came out: bug fixes, balance changes, support for new DLC. On Xbox LIVE it is the \"an update is available\" message.",
                "It is a small package. Inside is default.xexp - a list of changes to the game's default.xex - and often more: other .xexp files for the game's other programs, and new or replaced game data files.",
                "The game itself is not changed on disc; the console loads the game and lays the update over it each time it starts.");
            E(l, c, "How do I check that a title update is the right one for my game?",
                "Open Title Updates. Under \"1. Your game\" choose the game's default.xex, its disc image (.iso) or its package; under \"2. The title update\" choose the update you downloaded. The page compares them the way the console does: same Title ID, same Media ID, and the update must start from your game's exact version.",
                "It then says in plain words whether it fits, and if not, which part is wrong - another game, another pressing of the disc, or a game file that already has an update merged into it. Nothing in either file is changed.",
                "Scripts can do the same with: X360GameHack.exe cli tu-check <update> <game>.");
            E(l, c, "Where does a title update go on the console?",
                "The file name decides: a name that starts with lower-case \"tu\" goes to Content\\0000000000000000\\<Title ID>\\000B0000\\ and one that starts with upper-case \"TU_\" goes to the Cache folder, both on the console's hard drive or a USB drive it uses for storage. Never rename the file.",
                "Title Updates > Check & install picks the folder, creates it and copies the file for you. When the game next starts, the console finds the update, patches the game in memory and adds the update's files - the game's own files are not rewritten. Delete the file to go back.");
            E(l, c, "Why should I never use xextool -r a?",
                "Because \"a\" means every removal at once, and that includes \"z\", which zeroes the game's Media ID. Title updates are matched by the Media ID, so after that no update will load.",
                "A modern RGH or JTAG console running freeBOOT starts ordinary retail games as they are and does not need that patch. If one limit really has to go, remove only that one and leave z out. The one-click patches on this program's XEX Tool page keep the Media ID.");            E(l, c, "How do I find the right title update for my game?",
                "A title update only works with the Media ID it was made for. Open Title Updates and choose your game - its default.xex, its disc image (.iso) or its package. \"1. Your game\" shows the Title ID, Media ID and version. " +
                "Open XboxUnity takes you to the public database where updates are listed per Media ID; choose the highest version listed for yours.",
                "On the console, Aurora does the same search for you: highlight the game, open its details and choose the title update option. This is the easiest way if the console has internet.");
            E(l, c, "How do I install a title update the normal way?",
                "Open Title Updates, choose your game and the update, read the check, then press Install on the console or Install on a USB drive. It is put in the folder the console reads updates from.",
                "It ends up in one of two places, by the file's name:\n" +
                "- A name that starts with lower-case \"tu\" goes to Content\\0000000000000000\\<Title ID>\\000B0000\\.\n" +
                "- A name that starts with upper-case \"TU_\" goes to the Cache folder in the top level of the drive.\n" +
                "The program picks the right one. Start the game and the update is used; nothing else to do.");
            E(l, c, "What does merging an update into the game's XEX mean, and when do I want it?",
                "Instead of the console laying the update over the game at every start, you apply default.xexp to default.xex once, on the PC, and get an already-updated default.xex.",
                "You want this when you are going to modify the game program (so your changes are made to the updated code), or when the console's way of loading updates is not available.",
                "Steps: open the game's default.xex, go to Title Updates > Merge into the XEX, press Extract a title update... and pick the update package, then Apply .xexp to the open XEX.");
            E(l, c, "I extracted a title update. What do I do with all the files?",
                "Everything in the update belongs in the game's folder, next to default.xex, in the same sub-folders it had inside the update:\n" +
                "- default.xexp patches default.xex.\n" +
                "- Any other .xexp patches the program of the same name (for example default_mp.xexp for default_mp.xex).\n" +
                "- Every other file is a new or updated game file and replaces the old one of that name.",
                "If you only apply default.xexp and leave the other files out, the updated program looks for data that is not there and the game can crash or show missing content. " +
                "Extract a title update... puts all of them in place; a game file it would overwrite is kept with .bak added to its name.");
            E(l, c, "The game ignores my title update, or crashes after it.",
                "- Wrong Media ID: the update is for a different pressing of the game. Title Updates > Check & install compares the update with your game and says which part does not match.\n" +
                "- The game's Media ID was wiped: a XEX patched with \"remove all limits\" in some tools has its Media ID set to zero, and then no update matches. This program's one-click patches keep the Media ID.\n" +
                "- Installed twice: do not both install the package and merge the same update into the XEX. Pick one way.\n" +
                "- Merged without the other files: see the question above.\n" +
                "- Wrong folder or wrong upper/lower case in the name: let Console Setup place it.");
        }

        // ------------------------------------------------------------------ XEX
        private static void Xex(List<QaEntry> l)
        {
            const string c = "XEX files";
            E(l, c, "What is a XEX file?",
                "The Xbox 360's program format - what a .exe is on Windows. A game's main program is default.xex in the top level of the game folder; some games have more (default_mp.xex for multiplayer, for example).",
                "It has a header with facts about the game (Title ID, Media ID, version, which regions and media it may run from) followed by the program itself, which is normally compressed and encrypted.");
            E(l, c, "What do the one-click patches on the XEX Tool page do?",
                "A retail game program carries limits: run from disc only, certain regions only, certain dates, a minimum system version and so on. " +
                "The one-click patch removes those limits and writes the result as a retail-format file (for RGH, JTAG, Bad Update, Bad Avatar) or a devkit-format file.",
                "A backup of your original is made first. The Media ID is kept, so title updates still work.");
            E(l, c, "What do decrypt and uncompress mean, and when do I need them?",
                "The program inside a XEX is scrambled (encrypted) and squeezed (compressed). In that state the bytes on disk look like noise and editing them is pointless.",
                "Decrypt + uncompress turns it into the plain program. You need that before changing bytes by hand, before applying patches that are given as file offsets, and for some other tools. " +
                "The console runs a decrypted, uncompressed XEX just as well, so there is no need to reverse it afterwards.",
                "For looking at code you do not need to do it at all: the XEX Disassembler's Memory addresses view decodes the program for you without touching the file.");
            E(l, c, "What do the xextool letters (-r mrbdiyvklc and so on) mean?",
                "xextool is the long-trusted command line tool that does the patching; the program runs it for you. Its options:\n" +
                "-r followed by letters removes limits: m media, r region, b bounding path, d device ID, i console ID, y dates, v keyvault privileges, k signed keyvault only, l minimum library versions, c required revocation check. " +
                "The letter z would zero the Media ID, and \"a\" means all including z - the program avoids both because they break title updates.\n" +
                "-m r / -m d  write a retail / devkit file.\n" +
                "-c u / -c c  uncompress / compress.\n" +
                "-e u / -e e  decrypt / encrypt.\n" +
                "-u  apply an update so the separate .xexp is no longer needed.",
                "The Custom patch builder makes these for you with tick boxes and refuses combinations that contradict each other.");
            E(l, c, "Why would I change the Title ID or Media ID?",
                "Saves, DLC and updates are tied to these IDs. Changing them makes a prototype, a modded copy or another region's release use the content of a different release.",
                "Only the ID fields in the header are changed. The file's signature no longer matches afterwards, which hacked consoles do not mind.");
            E(l, c, "What is the Bad Update / Bad Avatar card for?",
                "Consoles running those software exploits still have Microsoft's unmodified system, so every limit in a game program still applies. " +
                "The card patches the open XEX (or many discs at once) so the game runs from a USB drive or hard drive on such a console, and can unlock DLC the same way.");
            E(l, c, "What are Export resources, Save base file and Save IDA .idc?",
                "Export resources saves the extra data stored inside the XEX (often the game's icon, text tables and achievement list).",
                "Save base file saves the decoded program as a plain Windows-style PE file - the form disassemblers such as IDA and Ghidra want.",
                "Save IDA .idc writes a script for the IDA disassembler that names the imported system functions.");
        }

        // ------------------------------------------------------------------ Packages
        private static void Packages(List<QaEntry> l)
        {
            const string c = "Packages";
            E(l, c, "What is a package and how do I look inside one?",
                "A package (STFS) is one file that holds other files plus a header with a name, pictures, the Title ID and the owner. Games on Demand, arcade games, DLC, title updates, themes, gamer pictures, saves and profiles are all packages.",
                "Drop one on the program. X360Packages shows the Overview, and Contents lists the files inside; from there you can extract one file, everything, or replace a file.");
            E(l, c, "What is the difference between CON, LIVE and PIRS?",
                "It is who signed the package. CON packages are signed by a console - saves and profiles. LIVE and PIRS packages are signed by Microsoft - things downloaded from the marketplace or delivered on disc.",
                "After you change anything inside a Microsoft-signed package the signature can no longer be valid; hacked consoles accept it anyway.");
            E(l, c, "What do Make bought, Make trial and Remove purchase traces do?",
                "Make bought / Make trial set the licence inside the package to full or trial.",
                "Remove purchase traces blanks the IDs of the console and profile that originally downloaded it.",
                "Make retail / RGH and Make devkit switch which kind of console the package is prepared for. Unlock DLC does the usual combination in one click. A backup is kept each time.");
            E(l, c, "Can I change a package's name and picture?",
                "Yes: Edit > Metadata changes the display name, title name and description; Set picture and Set game picture change the two small images. " +
                "Any picture file works - it is resized to the 64 x 64 PNG the console wants.");
            E(l, c, "What is the Custom X360PkgTool command box?",
                "X360PkgTool is the trusted command line tool behind the convert buttons. The box lets experienced users run it with their own options on the open package. If you do not know the options, you do not need this box.");
        }

        // ------------------------------------------------------------------ Saves
        private static void Saves(List<QaEntry> l)
        {
            const string c = "Saves";
            E(l, c, "Will unlocking achievements get my account banned?",
                "It can, if the edited profile signs in to Xbox LIVE - nothing can promise otherwise, and the only certain way is to keep that profile offline. The program does what can be done to lower the risk, and it is on by default.",
                "Achievements are always stored as earned offline, exactly the way a console stores them when it has no connection. With Natural unlock dates ticked (Profile > Games, on by default) each achievement and avatar award also gets its own date and time, spread backwards like real play sessions - minutes apart in a sitting, hours or days between sittings, one game after another - instead of hundreds sharing the same second, which is the plainest sign of an edited profile.",
                "You can type the date the spread should end at, or untick the box to give them all exactly that date. Unlocking a few games at a time looks more natural than every game at once.");
            E(l, c, "Can I change values inside a game save, like scores or unlocks?",
                "Yes, with a save mod for that game. Save editors are add-on mods (.x360mod files, see Mods): unlock or load the mod for your game, open the save, go to Saves > Game editor, and the values appear by name. Double-click one to change it, or pick a one-step action, then press Write into the save. A .bak copy is made first and the save is rehashed and checked automatically.",
                "The program itself and every console tool are free; game-specific save editors are separate mods, free or paid depending on who made them. Without a mod for the game, the Game editor tab says so.",
                "After writing: on a modified (RGH / JTAG) console put the save back as it is. For a stock console press Rehash & resign first. Keep the .bak until you have seen the game load the edited save.",
                "A save with no mod still opens on the Contents tab, where its files can be extracted and replaced.");            E(l, c, "Where are my saves?",
                "On the console's hard drive or USB drive at:\n" +
                "Content\\<your profile ID>\\<Title ID of the game>\\00000001\\<save file>",
                "You do not need to find them by hand. Saves > Browse saves lists every save by game name from a connected console (My console), a folder on the PC, or an Xbox hard drive plugged into the PC.");
            E(l, c, "How do I copy a save to my PC and back?",
                "With a USB drive: on the console go to Settings > System > Storage, open the device, Games, the game, select the save and choose Copy to the USB drive. On the PC it is under Content on that drive.",
                "Over the network: Saves > Browse saves > My console and copy it out; Install save... sends a save back to the right folder.");
            E(l, c, "How do I mod a save, start to finish?",
                "1. Get the save onto the PC and make a spare copy of it.\n" +
                "2. Drop it on the program. The Saves page opens.\n" +
                "3. Under Contents, extract the file inside (often called savegame, save.dat or similar).\n" +
                "4. Change that file with a save editor made for that game, or with a hex editor if you know what to change.\n" +
                "5. Back under Contents, replace the file with your changed one.\n" +
                "6. Press Rehash & resign.\n" +
                "7. Put the save back on the console (Install save...).",
                "Step 6 matters: without it the console sees that the contents changed and reports the save as corrupted.");
            E(l, c, "How do I use a save someone else made?",
                "A save remembers the profile that made it. To load it on your profile, open it on the Saves page and go to the Rehash & resign tab. Under Pick the IDs, double-click your profile - " +
                "its profile ID, device ID and console ID go into the boxes above - then press Rehash & resign and install the save. " +
                "(From a save or profile... next to the profile ID reads them from one of your own files instead.)");
            E(l, c, "Where do I get my profile ID, device ID and console ID for resigning?",
                "On the Rehash & resign tab (Saves, Profiles or X360Packages), under Pick the IDs. It lists your profiles by itself - the ones in Xenia, in your Saves folder, " +
                "on the Xbox drive open in Drive Explorer and on the connected console (its hard drive and USB sticks) - with all three IDs. Double-click one and they go into Rehash & resign; " +
                "nothing is written until you press Rehash & resign. A USB stick or folder... adds a folder that holds a console's Content folder.",
                "The device ID belongs to the drive: use the one of the drive the save will be on. Pick a profile on that drive, or the drive itself (the rows named Drive: give only its device ID - " +
                "the one most saves on that drive carry, which is the drive's own serial number on a hard drive). A USB stick formatted by the Xbox is read through the console: " +
                "plug it into the console and connect.",
                "With a keyvault loaded, Rehash & resign makes the save valid on any console, stock ones included. Without one it loads on RGH / JTAG consoles.");
            E(l, c, "Do I need a keyvault to rehash and resign?",
                "For a hacked console (RGH / JTAG): no. It skips the signature check, so the program only has to rebuild the hashes. One click, nothing to load.",
                "For a normal console: yes. It checks the signature, and a valid signature can only be made with a real console's keyvault. " +
                "Load your own keyvault file once on the Rehash & resign tab; the program remembers where the file is (not its contents) and uses it from then on.",
                "The program ships with nobody's keyvault. Do not use one downloaded from the internet.");
            E(l, c, "The game says my save is corrupted or does not show it.",
                "- Not rehashed and resigned after editing.\n" +
                "- Wrong profile ID: the save is filed under, or marked for, another profile.\n" +
                "- Wrong folder: it must be in Content\\<profile ID>\\<Title ID>\\00000001\\.\n" +
                "- Wrong game version: the save's Title ID must be the one of the game you run.\n" +
                "- The game has its own check inside the save data (a checksum). Then a general tool is not enough and you need an editor made for that game.\n" +
                "- Normal console and no valid keyvault signature.");
            E(l, c, "What are Make save RGH / retail and Make save devkit for?",
                "A save from a devkit console and a save from a retail or RGH console are packed differently. These buttons convert a save from one kind to the other so it loads on your kind of console.");
            E(l, c, "What do Create a save and the GPD viewer do?",
                "Create a save builds a new save package from files you choose: you say which game and profile it belongs to, add the files, and it is written, hashed and ready to install.",
                "The GPD viewer opens .gpd files - the per-game records inside a profile that hold achievements, settings and pictures - and can export the pictures.");
        }

        // ------------------------------------------------------------------ Profiles
        private static void Profiles(List<QaEntry> l)
        {
            const string c = "Profiles";
            E(l, c, "Where is my profile and how do I open it?",
                "A profile is one package file named after its profile ID (E0000...), stored at Content\\<profile ID>\\FFFE07D1\\00010000\\. " +
                "Copy it to a USB drive from the console's Storage screen, or fetch it over the network, and drop it on the program.",
                "Work on a copy and keep the original somewhere safe.");
            E(l, c, "Can I unlock achievements and avatar awards?",
                "Yes. Open the profile, pick a game and unlock single achievements, all of one game, or all of every game; the same for avatar awards. Lock does the reverse.",
                "Be honest with yourself about the risk: a profile with achievements unlocked this way that later connects to Xbox LIVE can have its gamerscore reset and be marked as a cheater, and the console can be banned. " +
                "For an offline profile on a hacked console there is no such risk.");
            E(l, c, "The game I want is not in my profile's list.",
                "A game only appears in a profile after it has been started once with that profile. Either start the game once on the console, or add it under Add games: from another profile that has it, from its .gpd file, or from the game's XEX.");
            E(l, c, "Can I change my gamertag and gamer picture?",
                "Yes, for offline profiles. Edit gamertag changes the name; Gamer picture accepts any picture file and makes the two sizes the console needs. " +
                "A profile tied to an Xbox LIVE account takes its name from the account, so the change shows offline only.");
            E(l, c, "What do Remove passcode, Convert to retail and Turn Xbox LIVE off do?",
                "Remove passcode turns off this profile's passcode lock - the four-button pass code that guards signing in to the profile and its parental / content controls. It clears the flag and the four button presses in the Account file, so the profile opens without a code.",
                "Convert to retail changes a profile made on a devkit so a normal or RGH console accepts it.",
                "Turn Xbox LIVE off makes the profile a local, offline profile so the console does not try to sign it in.");
            E(l, c, "I forgot the parental-controls / Family Settings passcode - can I turn it off?",
                "There are two different pass codes. A single profile's own pass code is turned off here: Profiles > Remove passcode (it guards that profile and its content controls).",
                "The console-wide Family Settings pass code is a separate thing, kept by the console itself, not inside a profile or the flash. Each console has its own reset code for it, and Flash Dumps reads that code: open the console's NAND backup, and the SMC config card on the Overview shows \"Family Settings reset code\" as four buttons. " +
                "Enter those at the console's pass-code screen to clear the lock. It resets the console's settings but keeps games and saved data. If you did not set up the console, only clear a lock on a console that is yours.");
            E(l, c, "The console says the profile is corrupt after I edited it.",
                "Press Rehash & resign on the profile before copying it back, and make sure it goes back to exactly the folder it came from with exactly the same file name. " +
                "On a normal console it also needs a valid keyvault signature, as with saves.");
        }

        // ------------------------------------------------------------------ Disc images
        private static void Discs(List<QaEntry> l)
        {
            const string c = "Disc images";
            E(l, c, "What is an ISO / XISO and why can't Windows open it?",
                "It is a complete copy of a game disc in one file. Xbox discs use their own layout (XDVDFS) that Windows does not read, so double-clicking it shows nothing useful or an error.",
                "Drop it on the program: Browse contents shows every file and folder inside, and you can extract one file or everything.");
            E(l, c, "What do the buttons on the Xbox ISOs page do?",
                "Extract... - copy every file out into a normal folder.\n" +
                "Optimize / rewrite... - write a new image without the empty filler, much smaller (an image that is already optimized is left alone, as extract-xiso does).\n" +
                "Convert to Games on Demand - make an installable package (with the options of the Convert tab).\n" +
                "Play in Xenia - run the image on the PC.\n" +
                "Browse contents - every file and folder with its sector, byte offset and attributes, Find, Properties, Extract (Xbox Image Browser built in; a package's disc too).\n" +
                "Create XISO from folder - the reverse of extract: build an image from a game folder.\n" +
                "Convert tab - disc images, Games on Demand packages and XEX version folders into any of the three, many at once (ISO2God and god2iso built in).\n" +
                "abgx360 tab - check an image against the verified database and fix its stealth files (abgx360 built in, with Hadzz's server).\n" +
                "Swap files - make a copy of the image with some files replaced (for testing mods).");
            E(l, c, "From a package to an image: compact or every sector in place?",
                "Microsoft's own Games on Demand packages leave out the empty stretch between the disc's header and its first file. Compact - what god2iso writes - closes that gap " +
                "(the folder tables get new sector numbers) and writes a fresh 64 KB lead-in, so the image is a little smaller. Every sector in place puts each block back exactly where it was on the disc. " +
                "Both hold the same files, byte for byte; a package made by ISO2God or this program comes out the same either way.",
                "Every block is checked against the package's hashes while it is copied, and the finished image is opened as an Xbox disc before it gets its name. " +
                "Packages ISO2God made with one data file have an empty root hash (an ISO2God bug); they are accepted and checked against their own hash tables.");
            E(l, c, "Do I need abgx360, Xbox Backup Creator or ISO2GOD?",
                "For hacked consoles, no. abgx360 checks and repairs images that will be burned to blank discs for consoles with a flashed DVD drive; it does nothing useful for extracting or converting. " +
                "ISO2God and god2iso are built in (the Convert tab) and make the same packages and images they do. abgx360 is built in too (the abgx360 tab): every option of the abgx360 GUI, " +
                "the report shown in the program in abgx360's own colours, its questions asked in a dialog.",
                "abgx360 itself is Hadzz's 1.0.7 build, which comes with this program - \"Set up abgx360\" installs it when it is missing (Windows asks, because it installs into the Windows folder) " +
                "and puts Hadzz's server fix file (abgx360.ini, next to abgx360.dat) in place, since the original abgx360.net is gone. ISO2God, Xbox Backup Creator and the original abgx360 GUI are still under Third-party tools.");
            E(l, c, "Why is my ISO 7 or 8 GB when the game is much smaller?",
                "A disc image is always the size of the disc, and the unused part is filled with padding. Extracting it, or Optimize / rewrite, leaves only the real game data.");
            E(l, c, "How do I test a mod without a console?",
                "Use the swap card on the Xbox ISOs page: select a file inside the image and press Swap the selected file..., or give it a folder of changed files. " +
                "A new image is written with those files replaced - your original image is untouched - and Play the new image in Xenia starts it on the PC.");
        }

        // ------------------------------------------------------------------ Modding
        private static void Modding(List<QaEntry> l)
        {
            const string c = "Modding games";
            E(l, c, "What kinds of game mods are there?",
                "1. File mods: you replace or edit the game's data files (textures, maps, scripts, text). Extract the game, change files, run it from the folder.\n" +
                "2. Program patches: you change bytes in default.xex so the game behaves differently, permanently.\n" +
                "3. Memory mods (RTM, \"real-time modding\"): you change values in the console's memory while the game runs - health, ammo, money. Gone when the game restarts.\n" +
                "4. Save mods: you edit the saved game.\n" +
                "5. Patch files for the Xenia emulator, which can also be applied to a real console.");
            E(l, c, "How do I change game files?",
                "Extract the game to a folder (Xbox ISOs > Extract..., or X360Packages > Contents > extract for a package). Replace or edit the files you want, keeping the same names and folders, and run the game from that folder on the console. " +
                "Keep an untouched copy of every file you change.",
                "Many games pack their data in big archive files of their own design; those need a tool made for that game or that game engine.");
            E(l, c, "What is Peek Poker?",
                "A live memory editor. \"Peek\" reads the bytes at a memory address of the running game; \"poke\" writes new bytes there. It works on a console over XBDM, and on a game running in Xenia on this PC (switch at the top of the page).");
            E(l, c, "How do I find the address of something like money or ammo?",
                "Use Memory search on the Peek Poker page:\n" +
                "1. Look at the value in the game, say 500 coins.\n" +
                "2. Type 500, choose the size (try 4 bytes first) and press First scan. You get many addresses that hold 500 right now.\n" +
                "3. Go back to the game and change the value - spend some coins so you have 450.\n" +
                "4. Type 450 and press Next scan. Only addresses that changed from 500 to 450 remain.\n" +
                "5. Repeat until one or a few are left. Write a new value to it with Write and look at the game.",
                "If nothing is found, try another size (2 bytes, or float for values with decimals such as health bars), or the game may store the value scrambled.");
            E(l, c, "What does freeze do, and what is a trainer?",
                "Freeze writes the same value to an address again and again, several times a second, so the game cannot change it - endless ammo, for example.",
                "A trainer is a saved list of addresses with names and values. Save trainer... keeps yours in a file; Load trainer... brings it back later. " +
                "Addresses usually stay the same for the same game with the same title update, and change when the game is updated.");
            E(l, c, "Can poking memory damage anything?",
                "It cannot damage the console or the game files: memory is wiped when the game closes. A wrong value in the wrong place can crash or freeze the running game; restart the game and it is as before. " +
                "Save your progress before experimenting.");
            E(l, c, "How do I make a permanent patch to the game program?",
                "1. Open default.xex and press Open in XEX Disassembler.\n" +
                "2. On the XEX Tool page first use Decrypt + uncompress (a backup is made), so the file's bytes are the real program.\n" +
                "3. Find the place: Jump to an address, or Find a byte pattern.\n" +
                "4. Type over the bytes. A patch must keep the same length - you replace bytes, you never insert or delete any.\n" +
                "5. Save copy... to keep the original, and run the copy.",
                "Two classic patches: replacing one instruction with \"do nothing\" is the four bytes 60 00 00 00; making a function immediately answer \"1\" is 38 60 00 01 4E 80 00 20 at its start.");
            E(l, c, "What is the difference between a file offset and a memory address in a patch?",
                "People share patches in two forms. \"Address 0x82123456\" is where the code sits in memory while the game runs. \"Offset 0x123456\" is a position in the decrypted, uncompressed file.",
                "The XEX Disassembler has both views. Memory addresses shows the program the way it sits in memory, so addresses from guides and from IDA or Ghidra match directly. Raw file (editable) shows file offsets.");
            E(l, c, "What does the Debugger page do?",
                "It stops the running game on the console and lets you look inside: the instruction being run, the values in the processor's registers, the list of threads and loaded modules. " +
                "A breakpoint makes the game stop when it reaches a certain instruction, or when a certain address is read or written - which is how you find the code that changes your health.",
                "While the game is stopped the picture on the TV is frozen. Press Continue to let it run again.\n\n" +
                "To step through the game's code: put a breakpoint on a line of the game's code, press Continue, and step from where the game stops there. " +
                "Break on its own holds every thread inside the console's system code, and a step or a Run to... that would stop in system code can lock the console up until it is switched off and on - so the page asks first.");
            E(l, c, "Can the program act as a controller?",
                "Not yet. XBDM has a way to feed button presses to the console, and a virtual controller page is planned, but it is not in this version.");
        }

        // ------------------------------------------------------------------ Disassembling
        private static void Disassembling(List<QaEntry> l)
        {
            const string c = "Reading game code";
            E(l, c, "What are disassembling and decompiling?",
                "A game program is a long list of processor instructions stored as numbers. Disassembling turns each number back into the instruction's short name, such as \"add\" or \"branch\" - readable, but very low-level.",
                "Decompiling goes a step further and rewrites groups of instructions as C-style code with ifs, loops and function calls, which is much easier to follow. " +
                "It is a reconstruction: names, comments and the original layout are gone for good, so variables have names like r3 and functions like sub_82123456.");
            E(l, c, "What is PowerPC?",
                "The kind of processor in the Xbox 360. Things worth knowing:\n" +
                "- Every instruction is exactly 4 bytes.\n" +
                "- It has 32 general registers (small fast storage slots) named r0 to r31. r1 points at the stack. r3 to r10 carry a function's inputs, and r3 carries its answer back.\n" +
                "- f0 to f31 are the registers for numbers with decimals.\n" +
                "- \"lr\" remembers where to go back to after a function; \"blr\" means \"return\".\n" +
                "- \"bl\" calls a function; \"b\" jumps; \"beq\", \"bne\" and friends jump only if a comparison came out a certain way.");
            E(l, c, "How do I use the XEX Disassembler?",
                "Open a game's default.xex and press Open in XEX Disassembler (or go to the page with the file open).\n" +
                "- The big area is a hex view: positions on the left, bytes in the middle, the same bytes as text on the right.\n" +
                "- Switch between Raw file (editable) and Memory addresses at the top.\n" +
                "- Jump goes to an address. Find next / Find all search for bytes or text. Replace all replaces a byte pattern everywhere.\n" +
                "- Disassemble lists the instructions of what you selected with the mouse; with nothing selected, of the page on screen.\n" +
                "- Decompile shows C-style code: of your selection, or with nothing selected, of every whole function on the page.\n" +
                "- Double-click a line in the result to jump to that instruction.");
            E(l, c, "How good is the built-in decompiler?",
                "It follows the program's real logic: every instruction form it handles was compared against Ghidra's processor model, and its output was tested on thousands of real functions. " +
                "Calls to the console's system functions are shown by their real names.",
                "It does not guess variable types or structure layouts the way a big tool like Ghidra or IDA does. Vector (VMX) instructions are shown as they are instead of as C. " +
                "For a long session on one game, use To Ghidra...");
            E(l, c, "What does To Ghidra... do?",
                "Ghidra is a free, professional reverse-engineering program. It cannot open XEX files on its own. " +
                "To Ghidra... writes a folder with the decoded program, a map of its sections, functions and named system calls, and a small script and batch file that create a ready Ghidra project from them. " +
                "The readme in that folder gives the three steps.");
            E(l, c, "What are imports, exports and functions?",
                "A function is one named job in the program - a block of instructions that is called, does something and returns.",
                "Imports are functions the game uses but does not contain: they live in the console's system (the kernel xboxkrnl.exe and xam.xex). Seeing which system functions a piece of code calls is the best clue to what it does - " +
                "code that calls the file-reading functions is loading something.",
                "Exports are functions a module offers to others; games rarely have any.");
            E(l, c, "How do I find the code that does a particular thing?",
                "Three common ways:\n" +
                "1. Text: search for a message the game shows (Find, as text). The code that uses that text is near what you want.\n" +
                "2. From memory: find the value's address with Peek Poker, then put a \"written\" breakpoint on it in the Debugger. The game stops at the exact instruction that changes it.\n" +
                "3. Imports: look at which functions call a system function related to what you want.",
                "Then Decompile that function and read it.");
            E(l, c, "Is it legal to look at a game's code?",
                "Laws differ between countries. In general, studying software you own for your own learning and to make things work together is treated very differently from sharing copyrighted game files or helping people play games they did not buy. " +
                "Share your patches as small change lists, not as modified copies of the game.");
        }

        // ------------------------------------------------------------------ Xenia
        private static void Xenia(List<QaEntry> l)
        {
            const string c = "Xenia (PC emulator)";
            E(l, c, "What is Xenia and what do I need?",
                "Xenia is a free program that runs Xbox 360 games on a Windows PC. The Canary version comes with this program, so there is nothing to download or set up. " +
                "It needs a reasonably modern PC with a dedicated graphics card; not every game runs well or at all.");
            E(l, c, "How do I start a game in Xenia?",
                "On the Xenia page, Games tab: select the game and press Play (or double-click it). Every game Play in Xenia copied from your console or an Xbox drive is listed there, and Add a game... adds one from anywhere on this PC (a default.xex, a disc image or a Games on Demand / arcade package). " +
                "A disc image open in the program also starts with Play in Xenia on the Xbox ISOs page.",
                "The first time, the program asks which profile Xenia plays as: pick one, or type a gamertag to make one (made exactly the way Xenia's own Create Profile makes it). After that the game starts straight away. " +
                "Use an unpatched copy: Xenia runs the original retail files as they are. A controller plugged into the PC works straight away.");
            E(l, c, "Can I use a PlayStation 4 or PlayStation 5 controller in Xenia?",
                "Yes, over USB or Bluetooth, with nothing extra installed - no DS4Windows. Xenia reads PlayStation, Switch Pro and about 2,000 other controllers itself (through SDL), and the program keeps that switched on every time it starts Xenia " +
                "(Xenia page > Setup > Controllers > Every controller works in Xenia). The buttons are where an Xbox controller has them: Cross = A, Circle = B, Square = X, Triangle = Y, Options = Start, Share / Create = Back, the PS button = Guide.",
                "For Bluetooth, pair the controller in Windows Settings > Bluetooth first: PS4 - hold Share and the PS button until the light flashes; PS5 - hold Create and the PS button. " +
                "Press Check controllers on the Xenia page (Setup tab) to see that Windows has it. DS4Windows works too: Xenia then uses the Xbox 360 controller DS4Windows makes. The first controller is player 1. " +
                "If the buttons work but Start does nothing at the game's title screen, the controller is fine - Xenia is not signed in to a profile, and the game is waiting for one (see \"A game in Xenia does not react to Start\").");
            E(l, c, "A game in Xenia does not react to Start.",
                "Most likely Xenia is not signed in to a profile. Many games wait at their title screen for a signed-in player (and a place for their saves), so pressing Start seems to do nothing. " +
                "The program asks which profile to use before it starts a game in Xenia: pick one, or type a gamertag to make one. To use your console profile, copy it in first: Neighborhood > Profiles > Copy to Xenia.",
                "Seen with Crackdown 2 and a PS4 controller through DS4Windows: Xenia had the controller as player 1, but no profile at all. Once Xenia signs in to a profile, the program starts the game straight away, without asking.");
            E(l, c, "Xenia closes as soon as it starts.",
                "Xenia keeps its settings and saves in its own folder, and it closes at once when it cannot write there - for example when the program is installed in Program Files. " +
                "The program handles that by itself: when Xenia's folder cannot be written it gives Xenia %LocalAppData%\\X360GameHack\\Xenia instead (your Xenia saves and settings are then kept there).",
                "If it still closes, start it from the Xenia page and look at the activity log; also check your graphics driver is up to date - Xenia needs a recent Vulkan or Direct3D 12 driver.");
            E(l, c, "Can I play a game that is on my console's hard drive in Xenia?",
                "Yes, but not straight off the console: Xenia can only open files that are on this PC, so the game is copied over first. On the Neighborhood page (or the FTP page) select the game's folder and press Play in Xenia. " +
                "The game is copied into Documents\\X360GameHack\\Xenia Games and then started. The next time it starts at once, because files that are already on the PC are not copied again.",
                "A game is several gigabytes, so the first copy takes a while: about 15 minutes for 7 GB over a network cable. If you stop it part-way, press Play in Xenia again and it carries on where it stopped. " +
                "Nothing on the console is changed. The game is then listed on Xenia > Games, where Play starts it from then on. You can pick a different folder for the copies on the Xenia page (Setup tab).",
                "It works for a game folder (the one with default.xex in it), a single .xex, and a Games on Demand or arcade package (the package's .data folder is copied with it), on the console's hard drive or on a USB drive plugged into the console. Not every game runs in Xenia.");
            E(l, c, "Can I play a game from an Xbox-formatted USB drive or hard drive in Xenia?",
                "Yes. Plugged into the console: Neighborhood page, open the drive (Usb0, Hdd...), select the game and press Play in Xenia. " +
                "Plugged into this PC: Drive Explorer > Open physical drive... (or open a drive image), select the game and press Play in Xenia.",
                "Xenia cannot read an Xbox-formatted drive itself, so the game is copied to this PC once (Documents\\X360GameHack\\Xenia Games) and started from there; the drive is only read. " +
                "Select the game's folder (the one with default.xex), its .xex, or its Games on Demand / arcade package file. A game that is already on the PC starts straight away.");
            E(l, c, "How do I use my console saves and profile in Xenia?",
                "Xenia keeps saves and profiles the way a console's hard drive does, so console ones work in it as they are. " +
                "From the console: Neighborhood > Saves (or Profiles) > select them > Copy to Xenia. From an Xbox drive on this PC: Drive Explorer > Saves > Copy to Xenia. " +
                "From files on this PC: Xenia > Profiles > Add a profile... for profiles, and Xenia > Saves > Install a save... for saves (see \"How do I install a downloaded or modded save in Xenia?\").",
                "A save brings its profile along when Xenia does not have it yet, and Xenia signs in to a copied profile when it starts (unless it already signs in to another - change that in Xenia's Profile menu). " +
                "A game only sees the saves of the profile Xenia is signed in to. Anything replaced is backed up to your Saves folder first, and nothing on the console or drive is changed. Close Xenia before copying.",
                "Xenia keeps saves and profiles as folders of files, so a console save or profile is unpacked into that form as it goes in - the way Xenia's own installer does it; the original is not changed. " +
                "Xenia > Saves and Xenia > Profiles list what Xenia has, including the saves and profiles Xenia made itself. In Xenia's form they can be copied out or deleted, but not opened in the editors or used on a console.");
            E(l, c, "What are Xenia patches?",
                "Small text files (ending in .patch.toml) that change a game while it runs in Xenia: 60 frames per second, higher resolution, cheats, bug fixes. The community keeps a public collection, linked by Get game-patches.",
                "On Xenia > Xenia Patches: Load .patch.toml..., tick the patches you want with Enable / disable, then Install into Xenia and start the game.");
            E(l, c, "Can I use those patches on a real console?",
                "Yes. With the game running on a console connected over XBDM, press Apply enabled to console. The same changes are written into the game's memory. They last until the game is closed.");
            E(l, c, "Can I change memory in a game running in Xenia?",
                "Yes. On the Peek Poker page switch the target from Console (XBDM) to Xenia (this PC). Reading, writing, searching and freezing then work on the emulated game, using the same addresses as on a console.",
                "One difference: Xenia keeps the game's code and read-only data read-only while it runs, so those cannot be written there. Values and other data change at once; " +
                "code changes go in a Xenia patch file (Xenia > Xenia Patches), which Xenia applies as the game starts. Write into the running game writes the patches it can and names the ones it left out.");
            E(l, c, "Can Drive Explorer read my big original Xbox hard drive (F: and G:)?",
                "Yes - read only. Drive Explorer > Open physical drive... (or Open drive image...) finds the original Xbox partitions on a drive of any size: C:, E:, the X: Y: Z: caches, " +
                "and on a big drive F: and G: - from the partition table XBpartitioner writes (the one Cerbios and other LBA48 BIOSes read), or without one the standard layout " +
                "(F: up to 137 GB, G: the rest), or one big F:. Big partitions with 32 KB or 64 KB clusters read the same way.",
                "When a drive opens, the log lists every partition with its size and cluster size, and where an original Xbox drive keeps them - handy if a drive looks wrong. " +
                "Nothing is written to the drive. If Windows offers to format or initialize it, say no: Windows does not know the Xbox's file system.");
            E(l, c, "What about the original Xbox? Can I play its games on the PC?",
                "Yes - in xemu, a separate free emulator for the original Xbox (Xenia plays Xbox 360 games only). Get xemu (Original Xbox (OG) page) opens its download page; set xemu.exe's " +
                "place there once. xemu also needs its own files - a boot ROM, a BIOS and a hard drive image - set up in xemu itself.",
                "From an original Xbox drive: Drive Explorer > open the drive > select the game's folder > Play in xemu. The game is copied to Documents\\X360GameHack\\Original Xbox Games once " +
                "(the drive is only read), its disc image is made beside it - xemu plays XISO images, not folders - and xemu starts it. Pressed again it starts straight away; the image is " +
                "only made again when the game's folder changed. A game already on the PC: open its default.xbe and press Original Xbox (OG) > Play this game.");
            E(l, c, "How do I install a downloaded or modded save in Xenia?",
                "Xenia > Saves > Install a save... (or Xenia > Games > select the game > Install a save...). Pick the save file itself, or the .zip or .rar it came in - a .rar needs 7-Zip installed. " +
                "The save goes to the profile Xenia signs in to, whichever profile made it, so a save from a website, a friend or a save editor works without resigning. Then start the game in Xenia and load it.",
                "Why it works: Xenia opens a save under the profile ID written inside it, so the program writes your profile's ID in as the save goes in (on a console that would also need the save resigned; Xenia checks no signature). " +
                "If the save came with the profile it belongs to (a download laid out like a USB stick, a mod menu's save), the program asks whether to keep it with that profile - then that profile goes into Xenia too and Xenia signs in to it - or to give it to one of your profiles. A save already there under the same name is backed up to your Saves folder first. It has to be an Xbox 360 save (a package file starting with CON); a PlayStation or PC save does not work. " +
                "Some games turn off achievements for a save made with cheats.",
                "If the game does not show the save, it is usually the game's version: many games (Call of Duty among them) only load a save made on the same version, and a save " +
                "is often made after the latest title update. Put that title update into Xenia too - the easiest way: open your Xbox drive in Drive Explorer (or connect your console), then " +
                "Xenia > Games > select the game > DLC / updates from my Xbox; from a file: Xenia > Games > Add DLC / updates... Xenia applies it when the game starts.");
            E(l, c, "My game in Xenia does not see a save, DLC or title update I put in.",
                "Close Xenia, then start the game again from the Xenia page. Before a game starts, X360GameHack puts what older versions of it put into Xenia in the form this Xenia reads: " +
                "Xenia only looks at folders (a DLC or title update left as a package file is not seen) and reads a short header file for each (an older version wrote a longer one that " +
                "this Xenia misread, so a game could not find the save). Anything changed is backed up to your Saves folder first.",
                "Then check: the save is for the profile Xenia signs in to (Xenia > Profiles); the game's title update matches the save's version (see the question about installing a save); " +
                "and for DLC, that it is for this game (its title ID).");
            E(l, c, "How do I upscale games or change Xenia's graphics?",
                "Xenia > Settings (or Xenia > Games > Graphics settings...). Quick setup sets it in one click: 4K (3x), Sharper (2x), or Xenia's defaults to undo it all. Resolution scale renders above the console's resolution - 2x is 1440p and 3x is 4K from a 720p game - " +
                "and Upscaling filter picks AMD FSR (best upscaling) or CAS (sharpening). There are also anti-aliasing (FXAA), texture filtering, a frame rate limit, VSync, fullscreen, " +
                "accuracy options for games that show something wrong, sound, controllers, the licence (unlock full Arcade games), the console language and the TV system.",
                "Every Xenia setting at the bottom lists all of Xenia's settings with Xenia's own description: search, select, change the value, press Set.",
                "Each change goes straight into Xenia's own settings file and is used the next time a game starts (close Xenia first if it is running). " +
                "The file is copied before the first change, so Put back my old settings... undoes everything. A higher resolution scale needs a stronger graphics card; " +
                "set it back to 1x for a game that looks wrong with it.");
            E(l, c, "Where are my games for Xenia?",
                "On the Xenia page, Games tab: every game Play in Xenia copied from your console or an Xbox drive (kept in Documents\\X360GameHack\\Xenia Games) and every game you added with Add a game.... " +
                "Play starts one; Install a save... and Add DLC / updates... give it a save, map packs or a title update; Remove... moves a copy to the Recycle Bin (its saves stay).");
        }

        // ------------------------------------------------------------------ Console mods
        private static void ConsoleMods(List<QaEntry> l)
        {
            const string c = "Console mods";
            E(l, c, "How do RGH and JTAG work, in simple words?",
                "When the console starts, it checks that its system software is Microsoft's. RGH adds a tiny chip that disturbs the processor for a fraction of a second at exactly the moment of that check, so the check wrongly passes and a modified system starts. " +
                "JTAG used a software mistake in very old system versions to the same end.",
                "Installing it needs soldering and reading the console's system memory, so most people have it done by someone experienced. Once done, the console starts hacked every time.");
            E(l, c, "What are Bad Update and Bad Avatar?",
                "Exploits that need no opening of the console and no soldering. They use mistakes in Microsoft's own software to run unsigned programs on a fully updated, otherwise normal console, started from a USB drive.",
                "They are not permanent: the console is normal again after it is switched off, and the exploit is run again next time. " +
                "Games for them must be patched (XEX Tool > Bad Update / Bad Avatar card). The project page linked on that card has the current instructions.");
            E(l, c, "Will I get banned from Xbox LIVE?",
                "A hacked console that connects to Xbox LIVE is detected and its keyvault is banned - permanently, for that console. Modded profiles and saves used online can get the account penalised.",
                "Keep a hacked console offline from LIVE: in launch.ini leave liveblock switched on (it blocks the LIVE servers while still allowing your home network for FTP and covers). Use a separate, normal console for online play.");
            E(l, c, "What is launch.ini and how do I edit it?",
                "The settings file of DashLaunch, a plain text file in the top level of the hard drive or a USB drive. The lines that matter most:\n" +
                "- Default = the program the console starts into, for example the path to Aurora's default.xex.\n" +
                "- plugin1 ... plugin5 = modules to load in the background, such as xbdm.xex.\n" +
                "- liveblock = block Xbox LIVE.\n" +
                "- contpatch = let arcade games and DLC run without their licence.\n" +
                "- noupdater = refuse system updates.",
                "Console Setup has an editor with tick boxes and a description for each setting, and can load the file from and send it to a USB drive or the console. The previous file is kept as launch.ini.bak. Restart the console for changes to apply.");
            E(l, c, "How do I add a game to a profile, or find a game's name from its Title ID?",
                "Profiles > Games > Add games: from another profile that has the game, from its .gpd file, from the game's own default.xex (achievements with their pictures), or with Add a game by Title ID (just the 8-digit ID, for example 415608FC). " +
                "The game is added the way a console adds it the first time it is played: listed, with every achievement locked.\n" +
                "Profiles > Games > Title ID lookup gives a game's name from its Title ID, or the Title IDs of the games whose name you type; double-click one to add it.",
                "Add a game by Title ID and the lookup ask the public title database at dbox.tools; only the Title ID or the name you typed is sent. Its lists include downloadable-content achievements but no achievement pictures - add a game from its XEX when you want those. Names found are kept on this PC.");
            E(l, c, "What are the Flash Tools for?",
                "They are for the people who install and maintain RGH: shortcuts to J-Runner (reads and writes the console's NAND and builds the modified system with its own copy of xeBuild), Jungle Flasher for DVD drives, and SimpleNandFlasher. " +
                "FTP2XeLL reads the CPU key and other values from XeLL, the small diagnostic system a hacked console can start into, by typing in the IP address XeLL shows on screen.",
                "If your console is already hacked and working, you do not need this page.");
            E(l, c, "How do I update a hacked console's system?",
                "Not from the screen and not from a game disc - DashLaunch blocks that on purpose, and forcing it removes the hack or worse. " +
                "A new system version is built with J-Runner from the console's own NAND backup and CPU key and written with a flasher program from a USB drive. " +
                "If you did not install the RGH yourself, ask the person who did.");
            E(l, c, "What can I see in a NAND backup (nanddump.bin, updflash.bin)?",
                "Open it (or drop it on the window) and Flash Dumps shows what is inside - for every board: Xenon, Zephyr, Falcon / Opus, Jasper small block (16 MB) and big block (256 / 512 MB), Trinity, Corona 16 MB and 4 GB. " +
                "Overview: the board, the boot chain and both patch slots (the dashboard version each boots, up to 17559; each stage decrypted, with its lockdown value and pairing data), the SMC (which board it is for, and whether it is a glitch image's patched SMC) and the SMC config - " +
                "fan speeds, the temperatures the console holds its chips at, the region. Keyvault: with the console's CPU key (typed, or J-Runner's cpukey.txt next to the dump) the serial, console ID, region, DVD key and drive; " +
                "fcrt.bin is decrypted with the same key. Files: the flash file system (dash.xex, xam.xex, crl.bin ...), each file extractable. Checks: every page's error check and the bad blocks.",
                "The backup is only ever read. A hack image can mix boards - RGH3 puts Trinity's boot loader on a Corona, RGH 1.2 puts Jasper's SMC on a Falcon - so the board is worked out from the boot loader, the SMC and the flash together, as J-Runner does, and the Overview says what is unusual. " +
                "The boot loader stages are decrypted with the published 1BL key, as J-Runner does - with the console's CPU key too on boards that pair the CB to the console - and the Overview shows each one's lockdown value (LDV) and pairing data. " +
                "A decrypted keyvault is written only when you press its Save button.");
            E(l, c, "Can I change the boot animation (the blades / NXE / current start-up animation)?",
                "Yes, on an RGH / JTAG console. The animation is a file called bootanim.xex inside the flash. On the Flash Dumps page, Files tab, use Swap boot animation: pick a bootanim.xex, or another flash dump to take its animation from " +
                "(an older-dashboard backup carries the older animation), and the program writes a new flash dump with it swapped in. Save this dump's boot animation extracts the current one so you can build a small set to switch between.",
                "The new animation must be a real bootanim.xex and fit the space the current one uses - the same size or smaller; a larger one is refused for now. The new dump is read back and checked before it is saved, and the file you started from is never changed, so keep it as a backup. " +
                "Flash the new dump the way you normally flash a NAND (J-Runner and a programmer, or an on-console flasher). This has been proven on the PC against J-Runner's own reader but not yet on a real console, so it carries a BETA label.");
            E(l, c, "Can I move a keyvault into a NAND (swap the keyvault)?",
                "Yes, on an RGH / JTAG console. On the Flash Dumps page, Keyvault tab, Swap keyvault writes a new flash dump with a different keyvault in it. Give a keyvault saved \"as stored\" (another dump's KV_en.bin, or use Save keyvault (as stored) on the dump you want it from), or point it at another flash dump to take its keyvault. " +
                "The console the keyvault belongs to boots it with that console's own CPU key - a keyvault and the CPU key go together.",
                "A keyvault holds a console's private keys, so only use one that is yours to use. The new dump is read back and checked before it is saved (and, when you give the matching CPU key on the command line, it is checked that the key unlocks the new keyvault); the dump you started from is never changed. " +
                "Proven on the PC but not yet on a real console - it carries a BETA label, and flashing a NAND can still brick a console, so keep the original as a backup.");
        }

        // ------------------------------------------------------------------ Original Xbox
        private static void OriginalXbox(List<QaEntry> l)
        {
            const string c = "Original Xbox";
            E(l, c, "What do I need to run copied games on an original Xbox?",
                "A softmod (a software hack installed through a game save) or a modchip / flashed TSOP (hardware). Either gives you a replacement dashboard such as UnleashX, EvolutionX or XBMC, which can start games from the hard drive and run an FTP server.");
            E(l, c, "What is an XBE file?",
                "The original Xbox's program format; a game's main program is default.xbe. Its header holds a certificate with the Title ID, the game's name, the regions and media it may run from, and start-up flags. " +
                "Drop one on the program to see and edit these.");
            E(l, c, "How do I put a game on an original Xbox hard drive?",
                "1. Drop the game's ISO on the program and extract it to a folder (the folder must contain default.xbe).\n" +
                "2. Start the FTP server in the Xbox's dashboard and connect from the FTP page (user and password are usually xbox / xbox).\n" +
                "3. Copy the folder into the Games folder on E: or F: (for example F:\\Games\\GameName).\n" +
                "4. The dashboard lists it.",
                "The original Xbox's file system limits names to 42 characters and has a 4 GB file limit, and a folder path should stay short.");
            E(l, c, "What do Optimize and Create XISO do for original Xbox images?",
                "A full copy of an original Xbox disc is about 7 GB even if the game is 1 GB, because the rest is filler. Optimize / rewrite writes an image holding just the game, which is what disc-image loaders on the Xbox and the xemu emulator want. " +
                "Create XISO builds an image from an extracted folder.");
            E(l, c, "What are the RAM patch and the CPU clock patch?",
                "Both are for consoles with upgraded hardware. The RAM patch clears the flag in the XBE header that limits the game to 64 MB, for consoles upgraded to 128 MB. " +
                "The CPU clock patch replaces the stock 733 MHz timing value inside the game with your upgraded processor's speed so the game does not run too fast.",
                "On a standard console leave both alone.");
            E(l, c, "Can I read an original Xbox hard drive on my PC?",
                "Original Xbox drives are locked with a password by the console, and a locked drive does not respond to a PC at all. It has to be unlocked first (the replacement dashboards have an unlock option; lock it again afterwards or the Xbox will refuse it). " +
                "An unlocked drive, or an image file of one, opens in Drive Explorer.");
            E(l, c, "Can I play original Xbox games on an Xbox 360?",
                "Only the games on Microsoft's compatibility list, and the console needs the compatibility files on an official hard drive. Convert the image to a package on the Xbox ISOs page and install it with Console Setup like a Games on Demand title.");
        }

        // ------------------------------------------------------------------ Troubleshooting
        private static void Trouble(List<QaEntry> l)
        {
            const string c = "When things go wrong";
            E(l, c, "The game will not start: \"The game couldn't start. Try downloading the game again.\"",
                "This general message has a handful of usual causes:\n" +
                "- Files are missing or damaged: copy the game again and compare the number of files and total size with the folder on the PC.\n" +
                "- Games on Demand: the .data folder is not next to the file, or is in the wrong Title ID folder.\n" +
                "- The game needs a title update, or avatar / Kinect data, that is not installed.\n" +
                "- Bad Update / Bad Avatar console: the XEX is not patched.\n" +
                "- A file or folder name is longer than 42 characters or has a forbidden character, so it did not copy.");
            E(l, c, "The copy to my USB drive stops with \"file is too large\".",
                "One file is over 4 GB and the drive is FAT32. Do not copy an ISO as it is: extract it or convert it to Games on Demand. See \"What is the 4 GB limit?\".");
            E(l, c, "A copy to the console fails on some files.",
                "On the console's own hard drive a name may be at most 42 characters and may not contain characters such as + , ; = [ ] or any of < > : \" / \\ | ? *. " +
                "Shorten or rename the file or folder. Also check that the drive is not full, and for network copies prefer a cable over Wi-Fi.");
            E(l, c, "A tool is missing or was \"refused\".",
                "The log at the bottom of the window names the tool. Either it is not in the program's folder (antivirus programs sometimes remove files; restore it or unpack the release again) " +
                "or its fingerprint does not match, meaning the file was changed. Settings > Bundled tools > Check now lists the state of each one.");
            E(l, c, "Where is the log, and what should I send with a bug report?",
                "The strip at the bottom of the window shows what the program did, with errors in red. Settings > Your data > Open log file opens the full log. " +
                "For a bug report (About > Report a bug) say what you clicked, what you expected, what happened instead, and paste the last lines of the log. " +
                "Remove anything personal first; never post a keyvault, CPU key or console serial number.");
            E(l, c, "How do I undo something the program changed?",
                "Actions that rewrite a file keep the previous version next to it with .bak added to the name (for example default.xex.bak). Delete the changed file and remove \".bak\" from the backup's name to go back.",
                "Settings > Reset all settings... puts the program itself back to how it was when first started; it does not touch your game files.");
            E(l, c, "The window text is too small or too big, or I want other colours.",
                "The program follows Windows' display scaling. Settings > Appearance has ready-made themes and an editor to make your own; themes can be exported and shared as a file.");
        }
    }
}
