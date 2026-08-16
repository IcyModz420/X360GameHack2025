using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using X360GameHack.Security;

namespace X360GameHack
{
    class Invoker
    {
        ProcessStartInfo startInfo = new ProcessStartInfo();
        AntiCommandInjection APT = new AntiCommandInjection();
        public bool CaptureOutput = false;
        public bool GenerateBatch = false;
        public string CMDOutput = "";
        public bool Showxiso = false;
        public string XexFilePath = "";
        public string xexpfilepath = "";
        public string GodFilePath = "";
        public string ISOFilePath = "";
        public string XBEFilePath = "";
        public string ABGX360GamePath = "";
        public string XISOExtractorGamesPath = "";
        public string XISOExtractorExtractionDestination = Application.StartupPath;

        public readonly string xextoolfilepath = new StringXORing(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xextool.exe")));
        public readonly string godtoolfilepath = new StringXORing(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "X360PkgTool.exe")));
        public readonly string xisofilepath = new StringXORing(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extract-xiso.exe")));
        private static readonly string xbetoolfilepath = new StringXORing(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XbeTool.exe")));
        private static readonly string system32Path = new StringXORing(Environment.GetFolderPath(Environment.SpecialFolder.System));
        private static readonly string abgx360filepath = new StringXORing(Path.Combine(system32Path, "abgx360.exe"));
        private static readonly string sysWow64Path = new StringXORing(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"));
        private static readonly string abgx36064filepath = new StringXORing(Path.Combine(sysWow64Path, "abgx360.exe"));

        public void InvokeXexTool(string XexFilePath, string Command, bool updatexex)
        {
            APT.SanitizeInvokerFilePath(XexFilePath);

            if (xextoolfilepath != "" && XexFilePath != "" && (File.Exists(xextoolfilepath)))
            {
                try
                {
                    if (updatexex)
                    {
                        if (GenerateBatch)
                        {
                            GenerateBatchToShowCommand("-p " + xexpfilepath, true, false, false, false, false);
                        }
                        else
                        {
                            CaptureProcessOutput(xextoolfilepath, "-p " + xexpfilepath, XexFilePath);
                        }
                    }
                    else if (GenerateBatch)
                    {
                        // MessageBox.Show("batch");
                        GenerateBatchToShowCommand(Command, true, false, false, false, false);
                        return;
                    }
                    else if (CaptureOutput)
                    {
                        try
                        {
                            CaptureProcessOutput(xextoolfilepath, Command, XexFilePath);
                            return;
                        }
                        catch (Exception ex)
                        {
                            CMDOutput = $"Error running xextool: {ex.Message}";
                        }
                    }
                }
                catch
                {
                    MessageBox.Show("Could not pass arguments to xextool!", "An Error occured!");
                }
            }
            else if (xextoolfilepath == "" || xextoolfilepath == null)
            {
                MessageBox.Show("Path to xextool = null! \n You need to have xextool in the same folder as this application!", "Error!");
                Application.Exit();
            }
            else if (XexFilePath == "" || XexFilePath == null)
            {
                MessageBox.Show("Path to xex file = null! \n You need to open a xex file first!", "Error!");
            }
            else if (!File.Exists(XexFilePath))
            {
                MessageBox.Show("Could not find xex file!");
            }
            else if (!File.Exists(xextoolfilepath))
            {
                MessageBox.Show("Could not find xextool! \n You need to have xextool in the same folder as this application!", "Error!");
                Application.Exit();
            }
            else if (File.Exists(xextoolfilepath) && File.Exists(XexFilePath) && Command != "" && startInfo.Arguments != "")
            {
                DialogResult dialogResult = MessageBox.Show("We see you have xextool in the correct folder and that you tried to pass " + startInfo.Arguments + " to xextool located at: " + xextoolfilepath + " to an xex file located at: " + XexFilePath + "\n If this appears correct click yes to retry", "An error occured!", MessageBoxButtons.YesNo);

                if (dialogResult == DialogResult.Yes)
                {
                    InvokeXexTool(XexFilePath, Command, false);
                }
                else if (dialogResult == DialogResult.No)
                {
                    Application.Exit();
                }
            }
        }

        public void InvokeGodTool(string GodFilePath, string Command)
        {
            APT.SanitizeInvokerFilePath(GodFilePath);
            if (godtoolfilepath != "" && GodFilePath != "" && (File.Exists(GodFilePath)))
            {
                try
                {
                    if (GenerateBatch)
                    {
                        GenerateBatchToShowCommand(Command, false, true, false, false, false);
                        return;
                    }
                    else if (CaptureOutput)
                    {
                        try
                        {
                            CaptureProcessOutput(godtoolfilepath, Command, GodFilePath);
                            return;
                        }
                        catch (Exception ex)
                        {
                            CMDOutput = $"Error running pkgtool: {ex.Message}";
                        }
                    }
                }
                catch
                {
                    MessageBox.Show("Could not pass arguments to X360PkgTool!", "An Error occured!");
                }
            }
            else if (godtoolfilepath == "" || godtoolfilepath == null)
            {
                MessageBox.Show("Path to X360PkgTool = null! \n You need to have X360PKGTool in the same folder as this application!", "Error!");
                Application.Exit();
            }
            else if (GodFilePath == "" || GodFilePath == null)
            {
                MessageBox.Show("Path to xbox 360 pkg = null! \n You need to open a xbox 360 pkg file first!", "Error!");
            }
            else if (!File.Exists(GodFilePath))
            {
                MessageBox.Show("Could not find xbox 360 pkg file!");
            }
            else if (!File.Exists(godtoolfilepath))
            {
                MessageBox.Show("Could not find X360PKGTool! \n You need to have xbox 360 pkg in the same folder as this application!", "Error!");
                Application.Exit();
            }
           /* else if (File.Exists(godtoolfilepath) && File.Exists(GodFilePath) && Command != "" && startInfo.Arguments != "")
            {
                DialogResult dialogResult = MessageBox.Show("We see you have X360PkgTool in the correct folder and that you tried to pass " + startInfo.Arguments + " to X360PkgTool located at: " + godtoolfilepath + " to an xex file located at: " + GodFilePath + "\n If this appears correct click yes to retry", "An error occured!", MessageBoxButtons.YesNo);

                if (dialogResult == DialogResult.Yes)
                {
                    InvokeXexTool(GodFilePath, Command, false); //bug 
                }
                else if (dialogResult == DialogResult.No)
                {
                   // Application.Exit();
                }
            }*/
        }

        string RemoveInvalidCharacters(string filePath, string[] invalidChars)
        {
            string sanitizedPath = new string(filePath.Where(c => !invalidChars.Contains(c.ToString())).ToArray());
            return sanitizedPath;
        }

        public void InvokeXISO(string ISOFilePath, string Command)
        {
            APT.SanitizeInvokerFilePath(ISOFilePath);
            string path = ISOFilePath;
            string[] invalidCharacters = { " ", "!", "@", "#", "$", "%", "^", "&", "*", "(", ")", "_", "-", "+", "=", "[", "]", "{", "}", "|", ";", "'", "?", "<", ">", "," };
            bool hasInvalidCharacter = invalidCharacters.Any(c => path.Contains(c));
            if (hasInvalidCharacter)
            {
                DialogResult result = MessageBox.Show(
                    "Error: The file name contains invalid characters. XISO does not allow special digits or spaces as parameters.\n" +
                    "Do you want to remove them?\n" +
                    "Acceptable Example:\n" +
                    "CallOfDuty.iso\n" +
                    "Fallout3.iso\n" +
                    "Halo4.iso",
                    "Invalid Characters Found",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Error
                );

                if (result == DialogResult.Yes)
                {
                    string sanitizedPath = RemoveInvalidCharacters(path, invalidCharacters);
                    // Check if the sanitized path is different from the original path
                    if (sanitizedPath != ISOFilePath)
                    {
                        try
                        {
                            if (System.IO.File.Exists(path))
                            {
                                System.IO.File.Move(path, sanitizedPath); // Rename the file to the sanitized path
                            }
                            else
                            {
                                MessageBox.Show(path + "doesn't exist anymore. If you see a developer tell them: BUG: XISO Invoker Code 1 happened somehow and provide reproducable steps.");
                            }
                            //APT.SanitizeInvokerFilePath(sanitizedPath);
                            ISOFilePath = sanitizedPath; // Update the file path
                            MessageBox.Show("File renamed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Error renaming file: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show("New path equal to old path during renaming phase! If you see a developer tell them BUG: XISO Invoker Code 2 happened somehow and provide reproducable steps.");
                    }
                }
            }

            if (xisofilepath != "" && ISOFilePath != "" && (File.Exists(xisofilepath)))
            {
                try
                {
                    GenerateBatchToShowCommand(Command, false, false, true, false, false);
                    // CaptureProcessOutput lags like hell use batch by default
                    return;                   
                }
                catch
                {
                    MessageBox.Show("Could not pass arguments to xISO!", "An Error occured!");
                }
            }
            else if (xisofilepath == "" || xisofilepath == null)
            {
                MessageBox.Show("Path to xextool = null! \n You need to have xiso in the same folder as this application!", "Error!");
                Application.Exit();
            }
            else if (ISOFilePath == "" || ISOFilePath == null)
            {
                MessageBox.Show("Path to xex file = null! \n You need to open a ISO file first!", "Error!");
            }
            else if (!File.Exists(ISOFilePath))
            {
                MessageBox.Show("Could not find ISO file!");
            }
            else if (!File.Exists(xisofilepath))
            {
                MessageBox.Show("Could not find xISO! \n You need to have xISO in the same folder as this application!", "Error!");
                Application.Exit();
            }
            else if (File.Exists(xisofilepath) && File.Exists(ISOFilePath) && Command != "" && startInfo.Arguments != "")
            {
                DialogResult dialogResult = MessageBox.Show("We see you have xISO in the correct folder and that you tried to pass " + startInfo.Arguments + " to xISO located at: " + xextoolfilepath + " to an ISO file located at: " + XexFilePath + "\n If this appears correct click yes to retry", "An error occured!", MessageBoxButtons.YesNo);

                if (dialogResult == DialogResult.Yes)
                {
                    InvokeXexTool(ISOFilePath, Command, false);
                }
                else if (dialogResult == DialogResult.No)
                {
                    Application.Exit();
                }
            }
        }

        public void GenerateBatchToShowCommand(string command, bool xextool, bool godtool, bool xiso, bool abgx360, bool ChangeBatchFilePath)
        {
            XISOExtractorExtention XISOEE = new XISOExtractorExtention(); // this clears strings so it needs to be reset from a initilized variable in form1...
            string batchFilePath;
            if (ChangeBatchFilePath)
            {
                batchFilePath = Path.Combine(Properties.Settings.Default.ExtractPath, "X360GameHacktemp.bat");
            }
            else
            {
                batchFilePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "X360GameHack_temp.bat"));
            }
            string batchCommand = "";
            if (xextool)
            {
                // MessageBox.Show("batch command = " +batchCommand);
                batchCommand = "start cmd /K xextool.exe " + command + " " + XexFilePath;
            }
            else if (godtool)
            {
                batchCommand = "start cmd /K X360PkgTool.exe " + command + " " + GodFilePath;
            }
            else if (xiso)
            {
                batchCommand = "start cmd /K extract-xiso.exe " + command + " " + ISOFilePath;
                if (ChangeBatchFilePath)
                {
                    //change directory
                    batchCommand = "start cmd /c " + "\""+ "cd /d " + Properties.Settings.Default.ExtractPath + " && extract-xiso.exe " + command + " " + ISOFilePath + "\"";
                    //batch command has to be in qoutes or it will bug when used in a batch file
                    //MessageBox.Show(batchCommand, "batch command");
                    //Clipboard.SetText(batchCommand);
                    //use /c to close it so we can patch xexs
                }
                // MessageBox.Show(batchCommand);
            }
            else if (abgx360)
            {
                if (File.Exists(abgx36064filepath)) // if its 64 bit
                {
                    batchCommand = abgx36064filepath + " " + command + " " + ISOFilePath;
                }
                else if (File.Exists(abgx360filepath)) // if its 32 bit
                {
                    batchCommand = abgx360filepath + command + " " + ISOFilePath;
                }
                else
                {
                    MessageBox.Show("Could not find abgx360! Please reinstall Hadz Patch if you have it installed!", "X360GameHack Info!");
                    return;
                }
            }
            else if (command == "")
            {
                MessageBox.Show("Command cannot be null!", "X360GameHack Error!");
                return;
            }

            File.WriteAllText(batchFilePath, batchCommand);
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = batchFilePath;
            Process process = new Process();
            process.StartInfo = startInfo;
            process.Start();
            process.WaitForExit();
            File.Delete(batchFilePath);
        }

        public void CaptureProcessOutput(string ToolLocation, string Command, string FilePath)
        {
            AntiCommandInjection APT = new AntiCommandInjection();
            APT.SanitizeInvokerFilePath(FilePath);
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = ToolLocation,
                Arguments = Command + " " + FilePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (Process process = new Process())
            {
                process.StartInfo = processInfo;
                StringBuilder outputBuilder = new StringBuilder();
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        outputBuilder.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        outputBuilder.AppendLine($"ERROR: {e.Data}");
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                CMDOutput = outputBuilder.ToString();
            }
            foreach (string line in GetOutputLines())
            {
                X360GameHack.CurrentInstance.UpdateListboxForOutput(line);
            }
        }
        public List<string> GetOutputLines()
        {
            return CMDOutput.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
        }

        public bool CheckIfABGXInstalled(bool notifysuccess, bool notifyfail)
        {
            if (File.Exists(abgx360filepath) || File.Exists(abgx36064filepath))
            {
                if (notifysuccess)
                {
                    MessageBox.Show("AGBX360 is installed!", "X360GameHack Info!");
                    return true;
                }
                else
                {
                    return true;
                }
            }
            else if (!File.Exists(abgx360filepath) || !File.Exists(abgx36064filepath))
            {
                if (notifyfail)
                {
                    MessageBox.Show("ABGX360 Not Installed!", "X360GameHack Info!");
                    return false;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        public void InvokeABGX(string ABGX360GamePath, string Command)
        {
            APT.SanitizeInvokerFilePath(ABGX360GamePath);
            if (CheckIfABGXInstalled(false, true) == true)
            {
                try
                {
                        GenerateBatchToShowCommand(Command, false, false, false, true, false);
                }
                catch
                {

                }
            }
        }
    }
}
