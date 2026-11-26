using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>The console's settings files: DashLaunch's launch.ini, and the XBDM and JRPC2 plugins' own ini files.</summary>
    public sealed class IniPage : PageBase
    {
        public override string Title => "INI Editor";
        public override string Subtitle => "Load launch.ini, xbdm.ini or JRPC.ini from the console, change settings, and send the file back.";
        public override string PageGlyph => Glyph.Settings;

        public readonly IniEditorCard Launch = new IniEditorCard(IniProfiles.Launch);
        public readonly IniEditorCard Xbdm = new IniEditorCard(IniProfiles.Xbdm);
        public readonly IniEditorCard Jrpc = new IniEditorCard(IniProfiles.Jrpc);

        public IniPage()
        {
            Body.Add(new ConnectBar());
            Note(Body, "These files are read when the console starts, so a change takes effect after the next restart. The console's current file is copied to your Saves folder (Backups) before it is replaced.", Theme.TextDim);
            AddSection("launch.ini", Glyph.Play).Add(Launch);
            AddSection("xbdm.ini", Glyph.Network).Add(Xbdm);
            AddSection("JRPC.ini", Glyph.Send).Add(Jrpc);
        }

        /// <summary>Opens the launch.ini tab (used by Console Setup).</summary>
        public void ShowLaunch() => ShowSection((VStack)Launch.Parent);
    }
}
