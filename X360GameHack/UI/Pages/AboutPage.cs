using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    public sealed class AboutPage : PageBase
    {
        public override string Title => "About";
        public override string Subtitle => "X360GameHack 2026 " + Pastebin.Version + " - free and open source.";
        public override string PageGlyph => Glyph.Info;

        public AboutPage()
        {
            var cols = AddColumns(2, 420);
            var about = AddCard("X360GameHack 2026", "An all-in-one toolbox for Xbox 360 RGH / JTAG / devkit / exploited consoles and the original Xbox.", Glyph.Game, cols);
            Note(about, "Made by IcyModz420 - a one-person project built over many late nights.");
            Buttons(about,
                Btn("Report a bug", Glyph.Bug, () => Tools.OpenUrl(Pastebin.IssuesUrl)),
                Btn("Releases", Glyph.Download, () => Tools.OpenUrl(Pastebin.ReleasesUrl)),
                Btn("YouTube", Glyph.Video, () => Tools.OpenUrl(Pastebin.YouTubeUrl)));

            var credits = AddCard("Credits", null, Glyph.Heart, cols);
            Note(credits,
                "xextool and X360PkgTool - xorloser\n" +
                "Built-in XISO engine - C# port of extract-xiso. Portions Copyright (c) 2003 in <in@fishtank.com>. All rights reserved. " +
                "Redistributed under the extract-xiso (BSD-style) license, provided AS IS without warranty.\n" +
                "Built-in PowerPC disassembler - opcode table checked against Xenia's instruction list; names of kernel, XAM and XBDM exports from Xenia's export tables. Copyright (c) 2015, Ben Vanik. All rights reserved. " +
                "Used under Xenia's BSD license, provided AS IS without warranty.\n" +
                "Title-update data - XboxUnity.net\n" +
                "ISO2GOD - original authors (Chilano), updated by r4dius. The built-in Convert tab is written for this program and makes the same packages; it was checked against ISO2God's own conversion.\n" +
                "god2iso - Richard Burton (raburton), github.com/raburton/god2iso, CC BY-SA 4.0. Making images from packages (Convert tab) is written for this program and makes the same images; it was checked against god2iso's published conversion.\n" +
                "Games on Demand launch and XDRPC call layout - xam.xex export names and signatures from the Xenia project (BSD); the XDRPC protocol as published by the Unify360 project.\n" +
                "abgx360 - Seacrest (Hadzz patch)\n" +
                "J-Runner, xeBuild, Jungle Flasher, Xbox Image Browser, Xbox Backup Creator - their respective authors\n" +
                "Be.HexBox - Bernhard Elbl\n" +
                "X360GameHack interface - IcyModz420\n" +
                "XDCKIT (the XBDM console library this program's console features grew from) - XboxChef, github.com/XboxChef/XDCKIT\n" +
                "Xenia Canary (bundled Xbox 360 emulator, BSD licence) - the Xenia project and Xenia Canary contributors\n" +
                "SDL_GameControllerDB (the controller list shipped next to Xenia, zlib licence) - github.com/mdqinc/SDL_GameControllerDB and its contributors", Theme.Text);

            var legal = AddCard("Legal", null, Glyph.Shield);
            Note(legal,
                "This software is provided \"as is\", without warranty of any kind. Use it at your own risk and follow your local laws on console modding.\n\n" +
                "What is free: X360GameHack itself, and every console-modding ability and tool built into it - file transfer, saves, packages, title updates, the XEX tools, the console tools, the ini editors, Xenia and everything else in the open-source program - are free and open source, and always will be.\n\n" +
                "What is paid: custom-made add-on mods (game mods and save-game mods supplied as separate licensed files) are a paid product and are not part of the open-source program. They are the author's own work, licensed per buyer, and are not covered by the open-source licence.\n\n" +
                "X360GameHack is an independent open-source project and is not affiliated with, endorsed by or sponsored by Microsoft Corporation. " +
                "All trademarks, copyrighted material, game files and keys belong to their respective owners. The developer claims no ownership of the bundled third-party tools.\n\n" +
                "It is intended only for lawful use such as backing up and running games you own, modifying your own saves on your own console, and running homebrew. Downloading or playing copies of games you do not own is illegal in many countries.");
        }
    }
}
