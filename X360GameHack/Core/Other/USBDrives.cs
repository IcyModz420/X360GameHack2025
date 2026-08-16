using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Windows.Forms;
using X360GameHack;

public class USBDrives
{
    public void DetectFat32USB(ComboBox comboBox)
    {
        comboBox.Items.Clear();

        try
        {
            // Step 1: Get all removable drives (USB sticks, USB HDDs, etc.)
            var driveQuery = new ManagementObjectSearcher(
                "SELECT DeviceID FROM Win32_DiskDrive WHERE InterfaceType='USB'");

            var usbPhysicalDrives = new System.Collections.Generic.HashSet<string>();
            foreach (ManagementObject disk in driveQuery.Get())
            {
                string deviceId = disk["DeviceID"]?.ToString()?.Replace(@"\\.\", "");
                if (!string.IsNullOrEmpty(deviceId))
                    usbPhysicalDrives.Add(deviceId);
            }

            if (usbPhysicalDrives.Count == 0)
            {
                comboBox.Items.Add("No USB drives detected");
                return;
            }

            // Step 2: Check each logical drive (E:\, F:\, etc.)
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Removable && drive.DriveType != DriveType.Fixed)
                    continue; // Skip network, CD, etc.

                if (!drive.IsReady) continue;

                string root = drive.Name; // "E:\"
                string letter = root.Substring(0, 2); // "E:"

                // Check if this logical drive belongs to a USB physical disk
                bool isUsb = false;
                var partitionQuery = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}'}} WHERE AssocClass = Win32_LogicalDiskToPartition");

                foreach (ManagementObject partition in partitionQuery.Get())
                {
                    var diskQuery = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{{partition.Path}}} WHERE AssocClass = Win32_DiskDriveToDiskPartition");
                    foreach (ManagementObject disk in diskQuery.Get())
                    {
                        string diskId = disk["DeviceID"]?.ToString()?.Replace(@"\\.\", "");
                        if (usbPhysicalDrives.Contains(diskId))
                        {
                            isUsb = true;
                            break;
                        }
                    }
                    if (isUsb) break;
                }

                if (!isUsb) continue; // Not a real USB drive

                string volumeName = drive.VolumeLabel;
                if (string.IsNullOrEmpty(volumeName)) volumeName = "USB Drive";

                string fs = drive.DriveFormat;

                if (string.Equals(fs, "FAT32", StringComparison.OrdinalIgnoreCase))
                {
                    // GOOD TO GO
                    comboBox.Items.Add($"{letter}" + "\\");
                }
                else
                {
                    // USB but wrong format → show warning
                    comboBox.Items.Add($"{letter} ({volumeName}) → Wrong format: {fs} – Must be FAT32!");
                }
            }

            // Final message if nothing valid
            if (comboBox.Items.Count == 0)
                comboBox.Items.Add("No FAT32 USB drive found – Insert one formatted as FAT32");
            else
                comboBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            comboBox.Items.Clear();
            comboBox.Items.Add($"Error: {ex.Message}");
        }
    }
}