using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace X360GameHack
{
    class Invoker
    {
        Process process = new Process();
        ProcessStartInfo startInfo = new ProcessStartInfo();
        public bool ShowArguments = false;
        public bool ShowCommandPrompt = false;
        public bool GenerateBatch = false;
        public bool Showxiso = false;
        public string XexFilePath = "";
        public string xexpfilepath = "";
        public string GodFilePath = "";
        public string ISOFilePath = "";
        public string xextoolfilepath = AppDomain.CurrentDomain.BaseDirectory + "xextool.exe";
        public string godtoolfilepath = AppDomain.CurrentDomain.BaseDirectory + "X360PkgTool.exe";
        public string xisofilepath = AppDomain.CurrentDomain.BaseDirectory + "extract-xiso.exe";

        public void InvokeXexTool(string XexFilePath, string Command, bool updatexex)
        {
            if (xextoolfilepath != "" && XexFilePath != "" && (File.Exists(xextoolfilepath)))
            {
                try
                {
                    startInfo.FileName = "cmd.exe";
                    if (updatexex)
                    {
                        if (GenerateBatch)
                        {
                            GenerateBatchToShowCommand("-p " + xexpfilepath, true, false, false);
                            return;
                        }
                        else
                        {
                            startInfo.Arguments = "/C " + "xextool -p " + xexpfilepath + " " + XexFilePath;
                        }
                    }
                    else if (GenerateBatch)
                    {
                        GenerateBatchToShowCommand(Command, true, false, false);
                        return;
                    }
                    else if (ShowCommandPrompt)
                    {
                        startInfo.Arguments = "/C " + "xextool " + Command + " " + XexFilePath;
                    }
                    else if (ShowArguments)
                    {
                        MessageBox.Show(startInfo.Arguments + "\n Press okay to start process!", "Arguments to be sent to xextool:");
                        startInfo.Arguments = "/C " + "xextool " + Command + " " + XexFilePath;
                    }
                    else
                    {
                        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        startInfo.Arguments = "/C " + "xextool " + Command + " " + XexFilePath;
                    }
                    process.StartInfo = startInfo;
                    process.Start();
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
            if (godtoolfilepath != "" && GodFilePath != "" && (File.Exists(GodFilePath)))
            {
                try
                {
                    if (GenerateBatch)
                    {
                        GenerateBatchToShowCommand(Command, false, true, false);
                        return;
                    }
                    else if (ShowCommandPrompt)
                    {
                        startInfo.Arguments = "/C " + "X360PkgTool " + Command + " " + GodFilePath;
                    }
                    else if (ShowArguments)
                    {
                        MessageBox.Show(startInfo.Arguments + "\n Press okay to start process!", "Arguments to be sent to X360PkgTool:");
                        startInfo.Arguments = "/C " + "X360PkgTool " + Command + " " + GodFilePath;
                    }
                    else
                    {
                        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        startInfo.Arguments = "/C " + "X360PkgTool " + Command + " " + GodFilePath;
                    }
                    process.StartInfo = startInfo;
                    process.Start();
                }
                catch
                {
                    MessageBox.Show("Could not pass arguments to X360PkgTool!", "An Error occured!");
                }
            }
            else if (godtoolfilepath == "" || godtoolfilepath == null)
            {
                MessageBox.Show("Path to X360PkgTool = null! \n You need to have xextool in the same folder as this application!", "Error!");
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
            else if (File.Exists(godtoolfilepath) && File.Exists(GodFilePath) && Command != "" && startInfo.Arguments != "")
            {
                DialogResult dialogResult = MessageBox.Show("We see you have X360PkgTool in the correct folder and that you tried to pass " + startInfo.Arguments + " to X360PkgTool located at: " + godtoolfilepath + " to an xex file located at: " + GodFilePath + "\n If this appears correct click yes to retry", "An error occured!", MessageBoxButtons.YesNo);

                if (dialogResult == DialogResult.Yes)
                {
                    InvokeXexTool(GodFilePath, Command, false);
                }
                else if (dialogResult == DialogResult.No)
                {
                    Application.Exit();
                }
            }
        }

        public void InvokeXISO(string ISOFilePath, string Command)
        {
            if (xisofilepath != "" && ISOFilePath != "" && (File.Exists(xisofilepath)))
            {
                try
                {
                    startInfo.FileName = "cmd.exe";
                    if (GenerateBatch)
                    {
                        GenerateBatchToShowCommand(Command, false, false, true);
                        return;
                    }
                    else if (ShowCommandPrompt)
                    {
                            startInfo.Arguments = "/C " + "extract-xiso " + Command + " " + ISOFilePath;                       
                    }
                    else if (ShowArguments)
                    {
                        MessageBox.Show(startInfo.Arguments + "\n Press okay to start process!", "Arguments to be sent to xextool:");
                        startInfo.Arguments = "/C " + "extract-xiso " + Command + " " + ISOFilePath;
                    }
                    else
                    {
                        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        startInfo.Arguments = "/C " + "extract-xiso " + Command + " " + ISOFilePath;
                    }
                    process.StartInfo = startInfo;
                    process.Start();
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

        public void GenerateBatchToShowCommand(string command, bool xextool, bool godtool, bool xiso)
        {
            string batchFilePath = AppDomain.CurrentDomain.BaseDirectory + "launchxextooltemp.bat";
            string batchCommand = "";
            if (xextool)
            {
                batchCommand = "start cmd /K xextool.exe " + command + " " + XexFilePath;
            }
            else if (godtool)
            {
                batchCommand = "start cmd /K X360PkgTool.exe " + command + " " + GodFilePath;
            }
            else if (xiso)
            {
                batchCommand = "start cmd /K extract-xiso.exe " + command + " " + ISOFilePath;
            }
            else if (command == "")
            {
                MessageBox.Show("Command cannot be null!", "Error!");
                return;
            }

            File.WriteAllText(batchFilePath, batchCommand);
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = batchFilePath;
            if (!ShowCommandPrompt)
            {
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            }
            Process process = new Process();
            process.StartInfo = startInfo;
            process.Start();
            process.WaitForExit();
            File.Delete(batchFilePath);
        }
    }
}
