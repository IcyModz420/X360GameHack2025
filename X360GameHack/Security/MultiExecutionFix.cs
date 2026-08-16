using System.Diagnostics;
using System.Windows.Forms;
using System.Linq;
using System;

namespace X360GameHack.Other
{
    internal class MultiExecutionFix
    {
        public void CheckForMultiExecution()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("X360GameHack");
                if (processes.Length >= 2)
                {

                    string message = $"{processes.Length} instance(s) of X360GameHack.exe are already running. Do you want to close the other one and open a new process? Click no to do nothing and continue opening a second process of X360GameHack.";
                    DialogResult result = MessageBox.Show(
                        message,
                        "Confirm Close",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (result == DialogResult.No)
                    {

                    }
                    if (result == DialogResult.Yes)
                    {
                        foreach (Process process in processes)
                        {
                            try
                            {
                                if (process.Id != Process.GetCurrentProcess().Id) // Filter out the current process to avoid killing itself
                                {
                                    process.Kill();
                                    process.WaitForExit(5000);
                                    process.Close();
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show(
                                    $"Failed to close process {process.Id}: {ex.Message}",
                                    "Error",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
