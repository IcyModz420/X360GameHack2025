// =============================================================================
// XDCKIT.XboxAutomation.cs - Controller automation helper
// =============================================================================
// `console.Automation.BindController(0, 16)`         - register virtual pad
// `console.Automation.SetGamepadState(0, ref pad)`   - update buttons/sticks
//
// Wraps the xbdm `autoinput` command family.  The DVD-tray helper lives in
// its own file (Helpers/Tray.cs) so this class stays focused on virtual
// controllers.
// =============================================================================

    /// <summary>
    /// Controller automation - binds a virtual pad to a user slot and lets you
    /// queue/feed gamepad packets at the xbdm <c>autoinput</c> level.
    /// </summary>
    public sealed class XboxAutomation
    {
        private readonly XboxConsole _console;

        internal XboxAutomation(XboxConsole console) => _console = console;

        public void GetInputProcess(UserIndex index, out bool systemProcess)
        {
            systemProcess = false;
            _console.SendTextCommand($"autoinput user={(int)index} process");
        }

        public void BindController(UserIndex index, uint queueLength)
            => _console.SendTextCommand($"autoinput user={(int)index} bind queuelen={queueLength}");

        public void UnbindController(UserIndex index)
            => _console.SendTextCommand($"autoinput user={(int)index} unbind");

        public void ConnectController(UserIndex index)
            => _console.SendTextCommand($"autoinput user={(int)index} connect");

        public void DisconnectController(UserIndex index)
            => _console.SendTextCommand($"autoinput user={(int)index} disconnect");

        public void SetGamepadState(UserIndex index, ref XBOX_AUTOMATION_GAMEPAD pad)
        {
            string cmd =
                $"autoinput user={(int)index} setpacket " +
                $"buttons=0x{(uint)pad.Buttons:X4} " +
                $"lt=0x{pad.LeftTrigger:X2} rt=0x{pad.RightTrigger:X2} " +
                $"lthumbx={pad.LeftThumbX} lthumby={pad.LeftThumbY} " +
                $"rthumbx={pad.RightThumbX} rthumby={pad.RightThumbY}";
            _console.SendTextCommand(cmd);
        }

        public bool QueueGamepadState(UserIndex index, ref XBOX_AUTOMATION_GAMEPAD pad,
                                      uint timedDuration, uint countDuration)
        {
            SetGamepadState(index, ref pad);
            _console.SendTextCommand($"autoinput user={(int)index} queuepackets count={countDuration} duration={timedDuration}");
            return true;
        }

        public void ClearGamepadQueue(UserIndex index)
            => _console.SendTextCommand($"autoinput user={(int)index} clearqueue");

        public void QueryGamepadQueue(UserIndex index,
                                      out uint queueLength, out uint itemsInQueue,
                                      out uint timedDurationRemaining, out uint countDurationRemaining)
        {
            queueLength = itemsInQueue = timedDurationRemaining = countDurationRemaining = 0;
            var resp = _console.SendTextCommand($"autoinput user={(int)index} queryqueue");
            if (!resp.IsSuccess) return;
            string flat = resp.StatusMessage ?? string.Empty;
            queueLength = XboxConsole.ParseUIntKvHex(flat, "queuelen");
            itemsInQueue = XboxConsole.ParseUIntKvHex(flat, "items");
            timedDurationRemaining = XboxConsole.ParseUIntKvHex(flat, "timedremaining");
            countDurationRemaining = XboxConsole.ParseUIntKvHex(flat, "countremaining");
        }

        public void GetUserDefaultProfile(out long xuid)
        {
            xuid = 0;
            var resp = _console.SendTextCommand("autoprof");
            if (!resp.IsSuccess) return;
            string s = XboxConsole.ParseKvLine(resp.StatusMessage, "xuid");
            if (!string.IsNullOrEmpty(s)) long.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out xuid);
        }

        public void SetUserDefaultProfile(long xuid)
            => _console.SendTextCommand($"autoprof xuid=0x{xuid:X}");
    }
