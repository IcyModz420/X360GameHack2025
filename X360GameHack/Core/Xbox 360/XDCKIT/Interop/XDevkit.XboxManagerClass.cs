#nullable disable
using System;

namespace XDevkit
{
    /// <summary>
    /// XDevkit-compatible manager entry point.  Drop-in replacement for
    /// <c>new XboxManagerClass()</c> when you reference XDCKIT instead of the
    /// COM <c>XDevkit.dll</c> interop assembly.
    /// </summary>
    public sealed class XboxManagerClass : IXboxManager, XboxManager
    {
        internal readonly global::XboxManager Inner = new global::XboxManager();

        private XboxConsolesList _consoles;

        public string DefaultConsole
        {
            get => Inner.DefaultConsole;
            set => Inner.DefaultConsole = value;
        }

        public IXboxConsoles Consoles => _consoles ??= new XboxConsolesList(this);

        public void AddConsole(string xbox)
        {
            if (string.IsNullOrEmpty(xbox)) return;
            if (!Inner.Consoles.Contains(xbox))
                Inner.Consoles.Add(xbox);
        }

        public void RemoveConsole(string xbox) => Inner.Consoles.Remove(xbox);

        public XboxConsole OpenConsole(string xboxName)
        {
            var host = Inner.OpenConsole(xboxName);
            return new XboxConsoleClass(this, host);
        }

        public IXboxDebugTarget OpenDumpFile(string filename, string imageSearchPath)
            => throw new NotSupportedException("XDCKIT does not support DmOpenDumpFile (dump-file debug targets).");

        public void SelectConsole(int parentWindow, out string selectedXbox, XboxAccessFlags desiredAccess, XboxSelectConsoleFlags flags)
            => throw new NotSupportedException("XDCKIT has no UI console picker. Set XboxManagerClass.DefaultConsole or call OpenConsole directly.");

        public void RunAddConsoleWizard(int parentWindow, bool modal)
            => throw new NotSupportedException("XDCKIT has no add-console wizard.");

        public void OpenWindowsExplorer(string xboxName, string path)
            => throw new NotSupportedException("XDCKIT does not host Windows Explorer integration.");

        public string TranslateError(int hr) => "0x" + hr.ToString("X8");

        public string SystemSymbolServerPath => global::XboxConsole.GetSystemSymbolServerPathA();

        public void SelectConsoleEx(long parentWindow, out string selectedXbox, XboxAccessFlags desiredAccess, XboxSelectConsoleFlags flags)
            => SelectConsole((int)parentWindow, out selectedXbox, desiredAccess, flags);

        public void RunAddConsoleWizardEx(long parentWindow, bool modal)
            => RunAddConsoleWizard((int)parentWindow, modal);
    }
}
