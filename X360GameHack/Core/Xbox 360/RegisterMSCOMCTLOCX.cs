using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack
{
    class RegisterMSCOMCTLOCX
    {
		[DllImport("regsvr32.dll")]		
        private static extern int RegSvr32(string fileName, int flags);
        private static readonly string destinationPath = new StringXORing(@"C:\Windows\System32\MSCOMCTL.OCX");
        private static readonly string arguments = new StringXORing("/C copy \"" + Path.Combine(Application.StartupPath, "MSCOMCTL.OCX") + "\" \"" + @"C:\Windows\System32\MSCOMCTL.OCX" + "\" & regsvr32 \"" + @"C:\Windows\System32\MSCOMCTL.OCX" + "\"");
		private static AntiDebug AntiNoob = new AntiDebug();
		[Confuser.Obfuscate]
		public static void OpenXIB(bool xib)
		{
			try
			{            
				Type type = Type.GetTypeFromProgID("MSCOMCTL.OCX");
			    if (Type.GetTypeFromProgID("MSCOMCTL.OCX") != null || File.Exists(destinationPath))
				{
					if (xib)
					{
                        AntiNoob.DoAntiDebugFunc(); 
                        if (Sha3_256.ComputeSha3_256(Path.Combine(Application.StartupPath, "XIB.exe")) != new StringXORing("e3c5910e7178d869da623a64976ae2bdcc2f8bcfad87fba9eda349d239bcc697"))
                        {
                            throw new SecurityException("Hash verification failed!, Possible tampering or corrupted file detected!\n If you believe you are seeing this in error please redownload!");
                        }
                        Process.Start(Application.StartupPath + "\\XIB.exe");
						return;
					}
					else
					{
                        AntiNoob.DoAntiDebugFunc();
                        if (Sha3_256.ComputeSha3_256(Path.Combine(Application.StartupPath, "XBBC.exe")) != new StringXORing("d2ee67caf130821164c291b25de7bc8c91b59453cace1a48444a16faff1fc3c4"))
                        {
                            throw new SecurityException("Hash verification failed!, Possible tampering or corrupted file detected!\n If you believe you are seeing this in error please redownload!");
                        }
                        Process.Start(Application.StartupPath + "\\XBBC.exe");
						return;
					}
				}
				DialogResult result = MessageBox.Show("X360GameHack will now create a new cmd (command prompt) process to request admin privilages then SHA3 check and register MSCOMCTL.OCX with regsvr32. X360GameHack its self will not elevate to administrator. Do you want to proceed?", "MSCOMCTL.OCX Windows Registration required!", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

				if (result == DialogResult.Yes)
				{
					// Create new cmd process with elevated privileges to copy and register
					ProcessStartInfo startInfo = new ProcessStartInfo();
					startInfo.FileName = "cmd.exe"; 
                    startInfo.Arguments = arguments;
					startInfo.Verb = "runas"; // Requires user interaction for elevation
					Process process = new Process();
                    process.StartInfo = startInfo;
					// compute secure hash of file before installation
					AntiNoob.DoAntiDebugFunc();
                    if (Sha3_256.ComputeSha3_256(Path.Combine(Application.StartupPath,new StringXORing("MSCOMCTL.OCX"))) != new StringXORing("ccd0cf579b788a55b34c4a9b2ea0ac6a451cecfa25e7dc76803c4841a2acccd9"))
					{
                        throw new SecurityException("Hash verification failed!, Possible tampering or corrupted file detected!\n If you believe you are seeing this in error please redownload!");
                    }
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
					//nothing
				}				
			}
			catch (Exception ex)
			{
				MessageBox.Show("An error occurred: " + ex.Message);
			}
		}
	}
}
