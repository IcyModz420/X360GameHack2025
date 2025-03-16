using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack
{
    class RegisterMSCOMCTLOCX
    {
		[DllImport("regsvr32.dll")]
		static extern int RegSvr32(string fileName, int flags);

		public static void OpenXIB(bool xib)
		{
			string sourcePath = Path.Combine(Application.StartupPath, "MSCOMCTL.OCX");
			string destinationPath = @"C:\Windows\System32\MSCOMCTL.OCX";
			try
			{
				// Check if the OCX is already registered
				Type type = Type.GetTypeFromProgID("MSCOMCTL.OCX");
				    if (Type.GetTypeFromProgID("MSCOMCTL.OCX") != null || File.Exists(destinationPath))
					{
					//	Console.WriteLine("MSCOMCTL.OCX is already registered.");
					if (xib)
					{
						Process.Start(Application.StartupPath + "\\XIB.exe");
						return;
					}
					else
					{
						Process.Start(Application.StartupPath + "\\XBBC.exe");
						return;
					}
				}
				DialogResult result = MessageBox.Show("This application will now request admin privilages to register MSCOMCTL.OCX with regsvr32. Do you want to proceed?", "MSCOMCTL.OCX Windows Registration required!", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

				if (result == DialogResult.Yes)
				{
					// Elevate privileges to copy and register
					ProcessStartInfo startInfo = new ProcessStartInfo();
					startInfo.FileName = "cmd.exe";
					startInfo.Arguments = "/C copy \"" + sourcePath + "\" \"" + destinationPath + "\" & regsvr32 \"" + destinationPath + "\"";
					startInfo.Verb = "runas"; // Requires user interaction for elevation

					Process process = new Process();
					process.StartInfo = startInfo;
					process.Start(); // Start new process do not elevate this one
					process.WaitForExit();

					if (process.ExitCode == 0)
					{
						if (xib)
						{
							MessageBox.Show("Launching Xbox Image Browser!", "MSCOMCTL.OCX copied and registered successfully!");
							Process.Start(Application.StartupPath + "XIB.exe");
						}
						else
						{
							MessageBox.Show("Launching Xbox Backup Creator!", "MSCOMCTL.OCX copied and registered successfully!");
							Process.Start(Application.StartupPath + "XBBC.exe");
						}
					}
					else
					{
						MessageBox.Show("Failed to copy and register MSCOMCTL.OCX. Error code: " + process.ExitCode);
					}
				}
				else
				{
					MessageBox.Show("Failed to validate MSCOMCTL.OCX!", "Error!");
				}				
			}
			catch (Exception ex)
			{
				Console.WriteLine("An error occurred: " + ex.Message);
			}
		}
	}
}
