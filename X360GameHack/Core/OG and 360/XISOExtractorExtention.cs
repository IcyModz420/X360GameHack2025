using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Security;

namespace X360GameHack
{
    class XISOExtractorExtention
    {
        private static string arguments;
        Invoker invoker = new Invoker();
        AntiCommandInjection APT = new AntiCommandInjection();
        public async Task<bool> ExtractISOAsync(string isoPath, bool extractSystemUpdate, bool defaultExtractionLocation, string extractorPath)
        {
            APT.SanitizeInvokerFilePath(isoPath);
            arguments = ""; // reset
            if (!extractSystemUpdate)
            {
                arguments = $"-x -s \"{isoPath}\"";
            }
            if (extractSystemUpdate)
            {
                arguments = $"-x \"{isoPath}\"";
            }
            if (!defaultExtractionLocation)
            {
                // check if extract xiso exists at extractor path if not send it to CurrentExtractorPath to iso extract location
                // so we can extract it at its current directory.
                if (!File.Exists(Path.Combine(extractorPath, "extract-xiso.exe")))
                {
                    try
                    {
                        //We need to know where the iso is that's being extracted first its being sent over the method call.
                        // copy xiso.exe from app dir to location of iso to be extracted so it can be used to extract it.
                        File.Copy(Path.Combine(AppContext.BaseDirectory, "extract-xiso.exe"), Path.Combine(isoPath, "extract-xiso.exe"));
                    }

                    catch (Exception ex)
                    {
                        // fail if we can't do the copy for some reason, this is a critical failure to continue.
                        MessageBox.Show("Failed to copy xiso to your ISO Directory!\n Exception: " + ex, "X360GameHack Error!");
                        return false; // critical
                    }
                }
                //Thread.Sleep(3000); // cant use this delay in production 
                if (!File.Exists(Path.Combine(extractorPath, "extract-xiso.exe"))) // do this to be sure its there before we try to run it after we copy it there. do not remove
                {
                    MessageBox.Show("extract-xiso.exe not found in the destination directory, please check the path and try again.", "X360GameHack Error!");
                    return false;
                }
                invoker.GenerateBatchToShowCommand(arguments, false, false, true, false, true);
                return true;
            }








            else
            {
                //this works for startup path only
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Application.StartupPath, "extract-xiso.exe"),
                    Arguments = arguments,
                    UseShellExecute = false,           // Required to redirect output
                    RedirectStandardOutput = true,     // Capture console output
                    CreateNoWindow = true              // Run without showing a console window
                };

                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();

                    // Read the output asynchronously
                  //  string output = await process.StandardOutput.ReadToEndAsync();

                    // Wait for the process to exit
                     process.WaitForExit();

                    // Check if "files in" is in the output to confirm completion
                    // return output.Contains(" files in ");
                    return true;
                }
            }
        }



        public async Task<bool> InvokeABGX360EE(string isoPath, string Arguments)
        {
            APT.SanitizeInvokerFilePath(isoPath);
            invoker.InvokeABGX(isoPath, Arguments);
            return true;
        }
    }
}
