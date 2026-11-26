using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

namespace X360GameHack.Core.Services
{
    public sealed class UsbDrive
    {
        public string Root;        // "E:\"
        public string Label;
        public string FileSystem;
        public long FreeBytes;
        public long TotalBytes;
        public bool IsFat32 => string.Equals(FileSystem, "FAT32", StringComparison.OrdinalIgnoreCase);

        public override string ToString()
        {
            string fs = IsFat32 ? "FAT32" : FileSystem + " - must be FAT32 for Xbox 360";
            return $"{Root}  {Label}  ({Formats.FileSniffer.FormatSize(FreeBytes)} free, {fs})";
        }
    }

    public static class UsbDrives
    {
        /// <summary>Lists logical drives that sit on a USB disk (sticks and external HDDs).</summary>
        public static List<UsbDrive> Detect()
        {
            var result = new List<UsbDrive>();
            var usbLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var disks = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_DiskDrive WHERE InterfaceType='USB'"))
                    foreach (ManagementObject disk in disks.Get())
                    {
                        string id = disk["DeviceID"]?.ToString().Replace("\\", "\\\\");
                        if (id == null) continue;
                        using (var parts = new ManagementObjectSearcher(
                            "ASSOCIATORS OF {Win32_DiskDrive.DeviceID='" + id + "'} WHERE AssocClass = Win32_DiskDriveToDiskPartition"))
                            foreach (ManagementObject part in parts.Get())
                                using (var logical = new ManagementObjectSearcher(
                                    "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + part["DeviceID"] + "'} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                                    foreach (ManagementObject ld in logical.Get())
                                        usbLetters.Add(ld["DeviceID"]?.ToString());
                    }
            }
            catch (Exception ex)
            {
                Log.Warn("USB detection through WMI failed, falling back to removable drives: " + ex.Message);
            }

            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    string letter = d.Name.Substring(0, 2);
                    bool isUsb = usbLetters.Contains(letter) || (usbLetters.Count == 0 && d.DriveType == DriveType.Removable);
                    if (!isUsb) continue;
                    result.Add(new UsbDrive
                    {
                        Root = d.Name,
                        Label = string.IsNullOrEmpty(d.VolumeLabel) ? "USB Drive" : d.VolumeLabel,
                        FileSystem = d.DriveFormat,
                        FreeBytes = d.AvailableFreeSpace,
                        TotalBytes = d.TotalSize
                    });
                }
                catch { /* drive vanished */ }
            }
            return result;
        }
    }
}
