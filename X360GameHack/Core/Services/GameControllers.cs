using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace X360GameHack.Core.Services
{
    /// <summary>One controller Windows sees right now.</summary>
    public sealed class GameController
    {
        public string Name, WindowsName;
        public ushort VendorId, ProductId;
        public bool PlayStation => VendorId == 0x054C;
        public bool Xbox => VendorId == 0x045E;
        public override string ToString() => Name;
    }

    /// <summary>
    /// The game controllers connected to this PC, read with Windows' own joystick functions (winmm) - no driver, no
    /// administrator. Xbox, PlayStation, Switch Pro and other pads all show up here when Windows has them.
    /// </summary>
    public static class GameControllers
    {
        public static List<GameController> Connected()
        {
            var list = new List<GameController>();
            uint count;
            try { count = joyGetNumDevs(); } catch { return list; }
            for (int id = 0; id < Math.Min(count, 16u); id++)
            {
                var pos = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf(typeof(JOYINFOEX)), dwFlags = JOY_RETURNALL };
                if (joyGetPosEx(id, ref pos) != 0) continue;                     // nothing plugged into this slot
                var caps = new JOYCAPSW();
                if (joyGetDevCapsW((IntPtr)id, ref caps, Marshal.SizeOf(typeof(JOYCAPSW))) != 0) continue;
                list.Add(new GameController
                {
                    VendorId = caps.wMid, ProductId = caps.wPid, WindowsName = (caps.szPname ?? "").Trim(),
                    Name = Describe(caps.wMid, caps.wPid, (caps.szPname ?? "").Trim())
                });
            }
            return list;
        }

        /// <summary>A plain name for a controller from its USB vendor and product IDs.</summary>
        public static string Describe(ushort vid, ushort pid, string windowsName)
        {
            if (vid == 0x054C)
            {
                switch (pid)
                {
                    case 0x0268: return "PlayStation 3 controller";
                    case 0x05C4: case 0x09CC: return "PlayStation 4 controller (DualShock 4)";
                    case 0x0BA0: return "PlayStation 4 controller (Sony USB wireless adapter)";
                    case 0x0CE6: return "PlayStation 5 controller (DualSense)";
                    case 0x0DF2: return "PlayStation 5 controller (DualSense Edge)";
                    default: return "PlayStation controller (" + Name(windowsName) + ")";
                }
            }
            if (vid == 0x045E) return "Xbox controller (" + Name(windowsName) + ")";
            if (vid == 0x057E) return pid == 0x2009 ? "Nintendo Switch Pro Controller" : "Nintendo controller (" + Name(windowsName) + ")";
            return Name(windowsName) + " (" + vid.ToString("X4") + ":" + pid.ToString("X4") + ")";
        }

        private static string Name(string s) => string.IsNullOrWhiteSpace(s) ? "game controller" : s;

        private const uint JOY_RETURNALL = 0xFF;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOYINFOEX
        {
            public uint dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos, dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JOYCAPSW
        {
            public ushort wMid, wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax;
            public uint wRmin, wRmax, wUmin, wUmax, wVmin, wVmax, wCaps, wMaxAxes, wNumAxes, wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
        }

        [DllImport("winmm.dll")] private static extern uint joyGetNumDevs();
        [DllImport("winmm.dll")] private static extern int joyGetPosEx(int uJoyID, ref JOYINFOEX pji);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int joyGetDevCapsW(IntPtr uJoyID, ref JOYCAPSW pjc, int cbjc);
    }
}
