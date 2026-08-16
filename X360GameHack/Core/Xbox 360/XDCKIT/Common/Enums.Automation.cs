// =============================================================================
// XDCKIT.Common.Enums.Automation.cs - Controller automation enums & structs
// =============================================================================
// Wraps the xbdm `autoinput` user / button / typing surface so callers can
// drive a virtual controller programmatically.
// =============================================================================

    public enum UserIndex
    {
        Zero,
        One,
        Two,
        Three
    }

    [System.Flags]
    public enum XboxAutomationButtonFlags
    {
        None = 0,
        DPadUp = 0x1,
        DPadDown = 0x2,
        DPadLeft = 0x4,
        DPadRight = 0x8,
        StartButton = 0x10,
        BackButton = 0x20,
        LeftThumbButton = 0x40,
        RightThumbButton = 0x80,
        LeftShoulderButton = 0x100,
        RightShoulderButton = 0x200,
        Xbox360_Button = 0x400,
        Bind_Button = 0x800,
        A_Button = 0x1000,
        B_Button = 0x2000,
        X_Button = 0x4000,
        Y_Button = 0x8000
    }

    public struct XBOX_AUTOMATION_GAMEPAD
    {
        public XboxAutomationButtonFlags Buttons;
        public uint LeftTrigger;
        public uint RightTrigger;
        public int LeftThumbX;
        public int LeftThumbY;
        public int RightThumbX;
        public int RightThumbY;
    }

    /// <summary>HID scan codes used by autoinput keyboard typing.</summary>
    public enum XboxChars
    {
        a = 4,
        b = 5,
        c = 6,
        d = 7,
        e = 8,
        f = 9,
        g = 10,
        h = 11,
        i = 12,
        j = 13,
        k = 14,
        l = 15,
        m = 0x10,
        n = 0x11,
        o = 0x12,
        p = 0x13,
        q = 20,
        r = 0x15,
        s = 0x16,
        t = 0x17,
        u = 0x18,
        v = 0x19,
        w = 0x1a,
        x = 0x1b,
        y = 0x1c,
        z = 0x1d,
        one = 30,
        two = 0x1f,
        three = 0x20,
        four = 0x21,
        five = 0x22,
        six = 0x23,
        seven = 0x24,
        eight = 0x25,
        nine = 0x26,
        zero = 0x27,
        Space = 0x2c,
        Caps = 0x39,
        Delete = 0x4c,
        aa = 0x2d,
        bb = 0x2e,
        cc = 0x2f,
        dd = 0x30,
        ee = 0x31,
        ff = 0x33,
        gg = 0x34,
        hh = 0x35,
        ii = 0x36,
        jj = 0x37,
        kk = 0x38
    }
