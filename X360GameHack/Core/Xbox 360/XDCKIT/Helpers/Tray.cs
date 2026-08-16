// =============================================================================
// XDCKIT.Tray.cs - DVD tray helper
// =============================================================================
// `console.Tray.Open()` / `console.Tray.Close()` calls the XAM dashboard
// ordinals that operate the optical disc tray.  Lives on its own file so
// the controller-automation logic (XboxAutomation.cs) isn't co-located
// with an unrelated peripheral.
// =============================================================================

    /// <summary>DVD tray helper.  Calls XAM ordinals for open/close.</summary>
    public sealed class Tray
    {
        private const string XAM = "xam.xex";
        private readonly XboxConsole _console;

        internal Tray(XboxConsole console) => _console = console;

        /// <summary>True after the most recent <see cref="Open"/>; false after <see cref="Close"/>.</summary>
        public bool IsTrayOpen { get; private set; }

        /// <summary>Open the DVD tray.</summary>
        public void Open()
        {
            uint addr = _console.Rpc.ResolveFunction(XAM, (uint)(int)XboxShortcuts.Open_Tray);
            if (addr != 0) _console.Rpc.CallVoid(addr, 0u, 0u, 0u, 0u);
            IsTrayOpen = true;
        }

        /// <summary>Close the DVD tray.</summary>
        public void Close()
        {
            uint addr = _console.Rpc.ResolveFunction(XAM, (uint)(int)XboxShortcuts.Close_Tray);
            if (addr != 0) _console.Rpc.CallVoid(addr, 0u, 0u, 0u, 0u);
            IsTrayOpen = false;
        }

        /// <summary>Open or close the tray depending on <paramref name="state"/>.</summary>
        public bool Options(TrayState state)
        {
            switch (state) { case TrayState.Open: Open(); break; case TrayState.Close: Close(); break; }
            return IsTrayOpen;
        }
    }
