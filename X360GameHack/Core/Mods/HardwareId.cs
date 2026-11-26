using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace X360GameHack.Core.Mods
{
    /// <summary>
    /// A stable fingerprint of this PC, used to bind a mod licence to one machine. It mixes several values that
    /// stay the same across restarts and reinstalls - the Windows machine GUID, the system drive's volume serial,
    /// and (when they can be read) the motherboard serial and the processor id - and returns their SHA-256.
    ///
    /// Honest note kept with the code: no PC id is truly unspoofable. A determined person can fake these values.
    /// What this gives is a binding the LICENCE SERVER enforces: a token is tied to the first machine id it is
    /// activated on, so the same token will not unlock a mod on a second machine. The strength is on the server
    /// (one token - one machine, revocable), not in this file.
    /// </summary>
    public static class HardwareId
    {
        private static string _cached;

        public static string Current()
        {
            if (_cached != null) return _cached;
            var parts = new System.Collections.Generic.List<string>();
            Add(parts, "mg", MachineGuid());
            Add(parts, "vs", SystemVolumeSerial());
            Add(parts, "bb", Wmi("Win32_BaseBoard", "SerialNumber"));
            Add(parts, "cpu", Wmi("Win32_Processor", "ProcessorId"));
            if (parts.Count < 2) throw new InvalidOperationException("This PC's hardware id could not be read.");
            using (var sha = SHA256.Create())
                _cached = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", parts)))).Replace("-", "").ToLowerInvariant();
            return _cached;
        }

        /// <summary>The first 12 characters, for showing the buyer which machine a licence is bound to.</summary>
        public static string Short() => Current().Substring(0, 12);

        private static void Add(System.Collections.Generic.List<string> parts, string tag, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) parts.Add(tag + ":" + value.Trim());
        }

        private static string MachineGuid()
        {
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    return k?.GetValue("MachineGuid") as string;
            }
            catch { return null; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetVolumeInformation(string rootPathName, StringBuilder volumeNameBuffer, int volumeNameSize,
            out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags, StringBuilder fileSystemNameBuffer, int fileSystemNameSize);

        private static string SystemVolumeSerial()
        {
            try
            {
                string root = System.IO.Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
                uint serial, comp, flags;
                if (GetVolumeInformation(root, null, 0, out serial, out comp, out flags, null, 0)) return serial.ToString("X8");
            }
            catch { }
            return null;
        }

        private static string Wmi(string cls, string prop)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT " + prop + " FROM " + cls))
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        string v = mo[prop]?.ToString();
                        if (!string.IsNullOrWhiteSpace(v) && v.Trim() != "0" && !v.Equals("None", StringComparison.OrdinalIgnoreCase) && !v.Equals("Default string", StringComparison.OrdinalIgnoreCase))
                            return v;
                    }
            }
            catch { }
            return null;
        }
    }
}
