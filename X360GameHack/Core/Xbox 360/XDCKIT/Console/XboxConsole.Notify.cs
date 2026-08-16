// =============================================================================
// XDCKIT.XboxConsole.Notify.cs - XNotify toasts (console.Notify(...))
// =============================================================================
// Pops a notification on the console using the xbdm "consolefeatures type=12"
// fast path, falling back to a direct ordinal call into
// xam.xex!XamShowNotificationUI (ordinal 0x282) if the consolefeatures plugin
// doesn't answer.  Both paths report success/failure via the return value.
//
// Usage:
//     console.Notify("Hello");
//     console.Notify("Achievement unlocked", XNotiyLogo.ACHIEVEMENT_UNLOCKED);
// =============================================================================
using System;

    public partial class XboxConsole
    {
        /// <summary>
        /// Show a notification with the default flashing-xbox icon.  Returns
        /// true on success (either the consolefeatures fast path or the
        /// direct xam.xex ordinal call succeeded).
        /// </summary>
        public bool Notify(string message)
            => Notify(message, XNotiyLogo.FLASHING_XBOX_LOGO);

        /// <summary>
        /// Show a notification with a specific icon.  Tries the
        /// <c>consolefeatures type=12</c> fast path first, then falls back to
        /// resolving <c>xam.xex!XamShowNotificationUI</c> (ordinal 0x282) and
        /// invoking it directly.  Returns true if either path reported
        /// success; false otherwise.
        /// </summary>
        public bool Notify(string message, XNotiyLogo logo)
        {
            if (string.IsNullOrEmpty(message)) message = " ";
            if (!Connected) throw new InvalidOperationException("Not connected.");

            try
            {
                var resp = SendTextCommand(ConsoleFeaturesWire.Type12Notify(message, logo));
                if (resp.IsSuccess) return true;
            }
            catch { /* fall through to direct ordinal call */ }

            try
            {
                uint addr = Rpc.ResolveFunction("xam.xex", 0x282);
                if (addr == 0) return false;
                Rpc.CallVoid(addr, (uint)logo, message, 4u);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
