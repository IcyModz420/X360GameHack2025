using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack;
using X360GameHack.Properties;
using static System.Net.WebRequestMethods;
using static System.Windows.Forms.AxHost;


namespace X360GameHack
{
    public partial class Form1 : Form
    {
        Invoker invoker = new Invoker();
        TitleIDChanger tidchanger = new TitleIDChanger();
        GOD2ISO ISO2GOD = new GOD2ISO();
        RegisterMSCOMCTLOCX RegisterMSCOMCTLOCX = new RegisterMSCOMCTLOCX();
        FTPClient FTPClient = new FTPClient();
        private string lastPath = null;
        // private TaskbarManager windowsTaskbar = null;
        public Form1()
        {
            InitializeComponent();
            // Allow drag and drop on the GroupBox
            groupBox1.AllowDrop = true;
            // Subscribe to the DragEnter event
            groupBox1.DragEnter += groupBox1_DragEnter;
            // Subscribe to the DragDrop event
            groupBox1.DragDrop += groupBox1_DragDrop;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            var fileContent = string.Empty;
            var filePath = string.Empty;
            string extension = Path.GetExtension(filePath);
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = Application.StartupPath;
                openFileDialog.RestoreDirectory = true;
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    //open file dialog
                    filePath = openFileDialog.FileName;
                    // pass filepath to invoker class
                    if (filePath.Contains(".xex"))
                    {
                        textBox1.Text = invoker.XexFilePath;
                        label1.Text = "XEX";
                        invoker.XexFilePath = filePath;
                        groupBox4.Show();
                    }
                    else if (filePath.Contains(".iso"))
                    {
                        textBox1.Text = invoker.ISOFilePath;
                        label1.Text = "ISO";
                        invoker.ISOFilePath = filePath;
                        groupBox4.Show();
                    }
                    else if (string.IsNullOrEmpty(extension))
                    {
                        textBox1.Text = invoker.GodFilePath;
                        label1.Text = "GOD";
                        invoker.ISOFilePath = filePath;
                        groupBox4.Show();
                    }
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            string xexpath = invoker.XexFilePath;
            if (xexpath != "" || xexpath != " ")
            {
                invoker.InvokeXexTool(xexpath, "-e e", false);
                if (!invoker.GenerateBatch)
                    MessageBox.Show("Encrypted file " + xexpath, "Thanks xorloser!");
            }
            else
                MessageBox.Show("This does not work with ISO you must extract it with image browser first..", "You need to open a XEX file first..");
        }

        private void button4_Click(object sender, EventArgs e)
        {
            string xexpath = invoker.XexFilePath;
            if (xexpath != "" && xexpath != " ")
            {
                invoker.InvokeXexTool(xexpath, "-e u", false);
                if (!invoker.GenerateBatch)
                    MessageBox.Show("Unencrypted file " + xexpath, "Thanks xorloser!");
            }
            else
                MessageBox.Show("This does not work with ISO you must extract it with image browser first..", "You need to open a XEX file first..");
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            IP.Text = Properties.Settings.Default.IP;
            Port.Text = Properties.Settings.Default.Port;
            UserName.Text = Properties.Settings.Default.Username;
            Password.Text = Properties.Settings.Default.Password;
            groupBox4.Hide();
            if (System.IO.File.Exists(AppDomain.CurrentDomain.BaseDirectory + "default.xex"))
            {
                textBox1.Text = AppDomain.CurrentDomain.BaseDirectory + "default.xex";
            }
            if (checkBox25.Checked)
            {
                invoker.GenerateBatch = true;
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            string xexpath = invoker.XexFilePath;
            if (xexpath != "" || xexpath != " ")
            {
                invoker.InvokeXexTool(xexpath, "-c c", false);
                if (!invoker.GenerateBatch)
                    MessageBox.Show("Compressed file " + xexpath, "Thanks xorloser!");
            }
            else
                MessageBox.Show("This does not work with ISO you must extract it with image browser first..", "You need to open a XEX file first..");
        }

        private void button5_Click(object sender, EventArgs e)
        {
            string xexpath = invoker.XexFilePath;
            if (xexpath != "" || xexpath != " ")
            {
                invoker.InvokeXexTool(xexpath, "-e u", false);
                if (!invoker.GenerateBatch)
                    MessageBox.Show("Encrypted file " + xexpath, "Thanks xorloser!");
            }
            else
                MessageBox.Show("This does not work with ISO you must extract it with image browser first..", "You need to open a XEX file first..");
        }

        private void button2_Click(object sender, EventArgs e)
        {
            string xexpath = invoker.XexFilePath;
            if (xexpath != "" || xexpath != " ")
            {
                string Arguments = "";

                if (checkBox1.Checked == true)
                {
                    Arguments = "-u ";
                }
                if (checkBox2.Checked == true)
                {
                    Arguments = Arguments + "-r a ";
                }
                if (checkBox3.Checked == true)
                {
                    Arguments = Arguments + "-m d ";
                }
                if (checkBox6.Checked == true)
                {
                    Arguments = Arguments + "-m r ";
                }
                if (checkBox7.Checked == true)
                {
                    Arguments = Arguments + "-c u ";
                }
                if (checkBox4.Checked == true)
                {
                    Arguments = Arguments + "-c c ";
                }
                if (checkBox9.Checked == true)
                {
                    Arguments = Arguments + "-e e";
                }
                if (checkBox8.Checked == true)
                {
                    Arguments = Arguments + "-e u";
                }
                if (checkBox8.Checked == false && (checkBox8.Checked == false))
                {
                    MessageBox.Show("You must select to encrypt the xex file or not!", "OooopsssSS");
                }
                invoker.InvokeXexTool(xexpath, Arguments, false);
                if (!invoker.GenerateBatch)
                    MessageBox.Show("Patches applied to XEX file!", "Thanks xorloser!");
            }
            else
                MessageBox.Show("This does not work with ISO you must extract it with image browser first..", "You need to open a XEX file first..");
        }

        private void button10_Click(object sender, EventArgs e)
        {
            invoker.ShowCommandPrompt = true;
            invoker.InvokeXexTool(invoker.XexFilePath, "-l", false);
            invoker.ShowCommandPrompt = false;
        }

        private void button11_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "-x a", false);
            if (!invoker.GenerateBatch)
                MessageBox.Show("Everything extracted to " + AppDomain.CurrentDomain.BaseDirectory, "Extract Successful!");
        }

        private void button7_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "-u -r a -m r -c u -e u", false);
            if (!invoker.GenerateBatch)
                MessageBox.Show("Selected XEX file is now RGH/Freeboot CFW Compatable", "Patching Successful!");
        }

        private void button8_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "-u -r a -m d -c u -e u", false);
            if (!invoker.GenerateBatch)
                MessageBox.Show("Selected XEX file is now developers kit Compatable", "Patching Successful!");
        }

        private void button9_Click(object sender, EventArgs e)
        {

        }

        private void groupBox1_DragDrop(object sender, DragEventArgs e)
        {
            // Get the dropped files
            string[] filePaths = (string[])e.Data.GetData(DataFormats.FileDrop);

            // string extension = Path.GetExtension(filePath);

            // Assuming you only want the first file
            string firstFilePath = filePaths[0];

            // Handle the filepath
            // For example, display it in a label           
            textBox1.Text = $"{firstFilePath}";
            string filePath = $"{firstFilePath}";
            string extension = Path.GetExtension($"{firstFilePath}");
            if (filePath.Contains(".xex"))
            {
                textBox1.Text = invoker.XexFilePath;
                label1.Text = "XEX";
                invoker.XexFilePath = filePath;
                groupBox4.Show();
                return;
            }
            else if (filePath.Contains(".iso"))
            {
                textBox1.Text = invoker.ISOFilePath;
                label1.Text = "ISO";
                invoker.ISOFilePath = filePath;
                groupBox4.Show();
                return;
            }
            else if (string.IsNullOrEmpty(extension))
            {
                textBox1.Text = invoker.GodFilePath;
                label1.Text = "PKG";
                invoker.GodFilePath = filePath;
                groupBox4.Show();
                return;
            }
            MessageBox.Show("end");

        }

        private void groupBox1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void groupBox1_Enter(object sender, EventArgs e)
        {

        }

        private void groupBox5_DragDrop(object sender, DragEventArgs e)
        {
            string[] filePaths = (string[])e.Data.GetData(DataFormats.FileDrop);
            string firstFilePath = filePaths[0];
            textBox2.Text = $"{firstFilePath}";
            invoker.xexpfilepath = textBox2.Text;
        }

        private void groupBox5_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void button9_Click_1(object sender, EventArgs e)
        {
            var fileContent = string.Empty;
            var filePath = string.Empty;

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = "c:\\";
                //openFileDialog.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
                //openFileDialog.FilterIndex = 2;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    //open file dialog
                    filePath = openFileDialog.FileName;
                    // pass filepath to invoker class
                    invoker.xexpfilepath = filePath;
                    textBox2.Text = invoker.xexpfilepath;
                }
            }
        }

        private void button12_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "", true);
        }

        private void groupBox5_Enter(object sender, EventArgs e)
        {

        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://xboxunity.net");
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://www.wemod.com/horizon");
        }

        private void button13_Click(object sender, EventArgs e)
        {
            string godpath = invoker.GodFilePath;
            string Arguments = "";
            if (godpath != "" || godpath != " ")
            {
                if (checkBox5.Checked == true)
                {
                    Arguments = Arguments + "-z ";
                }
                if (checkBox10.Checked == true)
                {
                    Arguments = Arguments + "-m 0 ";
                }
                if (checkBox11.Checked == true)
                {
                    Arguments = Arguments + "-m 1 ";
                }
                if (checkBox14.Checked == true)
                {
                    Arguments = Arguments + "-b 0 ";
                }
                if (checkBox13.Checked == true)
                {
                    Arguments = Arguments + "-b 1 ";
                }
                if (checkBox12.Checked == true)
                {
                    Arguments = Arguments + "-t c ";
                }
                if (checkBox17.Checked == true)
                {
                    Arguments = Arguments + "-t l ";
                }
                if (checkBox16.Checked == true)
                {
                    Arguments = Arguments + "-t p ";
                }
                if (checkBox20.Checked == true)
                {
                    Arguments = Arguments + "-fp ";
                }
                if (checkBox19.Checked == true)
                {
                    Arguments = Arguments + "-fd ";
                }
                if (checkBox18.Checked == true)
                {
                    Arguments = Arguments + "-fn ";
                }
                if (checkBox23.Checked == true)
                {
                    Arguments = Arguments + "-fl ";
                }
                if (checkBox22.Checked == true)
                {
                    Arguments = Arguments + "-fk ";
                }
                invoker.InvokeGodTool(godpath, Arguments);
                MessageBox.Show("Patches applied to GOD file!", "Thanks xorloser!");
            }
            else
            {
                MessageBox.Show("This does not work with ISO you must extract it with image browser first..", "You need to open a XEX file first..");
            }
        }


        private void checkBox12_CheckedChanged(object sender, EventArgs e)
        {
            MessageBox.Show("Do not check this unless you know what your doing! \n This will not work on retails do not try!", "Warning!");
        }

        private void checkBox17_CheckedChanged(object sender, EventArgs e)
        {
            MessageBox.Show("Do not check this unless you know what your doing! \n This will not work on retails do not try!", "Warning!");
        }

        private void checkBox16_CheckedChanged(object sender, EventArgs e)
        {
            MessageBox.Show("Do not check this unless you know what your doing! \n This will not work on retails do not try!", "Warning!");
        }

        private void checkBox23_CheckedChanged(object sender, EventArgs e)
        {
            MessageBox.Show("Do not check this unless you know what your doing! \n This will not work on retails do not try!", "Warning!");
        }

        private void button10_Click_1(object sender, EventArgs e)
        {
            var fileContent = string.Empty;
            var filePath = string.Empty;

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = "c:\\";
                //openFileDialog.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
                //openFileDialog.FilterIndex = 2;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    //open file dialog
                    filePath = openFileDialog.FileName;
                    // pass filepath to invoker class
                    invoker.GodFilePath = filePath;
                    textBox1.Text = invoker.GodFilePath;
                    label1.Text = "GOD";
                    groupBox4.Show();
                }
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void groupBox2_Enter(object sender, EventArgs e)
        {

        }

        private void tabPage1_Click(object sender, EventArgs e)
        {

        }

        private void button11_Click_1(object sender, EventArgs e)
        {
            if (textBox1.Text.Contains(".xex"))
            {
                tidchanger.GetTitleIDandMediaID(textBox1.Text);
                textBox3.Text = tidchanger.titleID;
                textBox4.Text = tidchanger.mediaID;
            }
            else
            {
                MessageBox.Show("You need to open a XEX File First!", "Error!");
            }
        }

        private void checkBox15_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox15.Checked == true)
            {
                invoker.ShowArguments = true;
            }
            else if (checkBox15.Checked == false)
            {
                invoker.ShowArguments = false;
            }
        }

        private void checkBox21_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox21.Checked == true)
            {
                invoker.ShowCommandPrompt = true;
            }
            else if (checkBox21.Checked == false)
            {
                invoker.ShowCommandPrompt = false;
            }
        }

        private void button15_Click(object sender, EventArgs e)
        {
            if (invoker.GenerateBatch == false)
            {
                invoker.GenerateBatch = true;
                invoker.InvokeXexTool(invoker.XexFilePath, "-l", false);
                invoker.GenerateBatch = false;
            }
            else if (invoker.GenerateBatch == true)
            {
                invoker.InvokeXexTool(invoker.XexFilePath, "-l", false);
            }
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
        }

        private void checkBox24_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox24.Checked == true)
            {
                invoker.GenerateBatch = true;
            }
            else if (checkBox24.Checked == false)
            {
                invoker.GenerateBatch = false;
            }
        }

        private void button14_Click(object sender, EventArgs e)
        {
            if (textBox1.Text.Contains(".xex"))
            {
                tidchanger.titleID = textBox3.Text;
                tidchanger.mediaID = textBox4.Text;
                tidchanger.ChangeTitleIDandMediaID(textBox1.Text);
            }
            else
            {
                MessageBox.Show("You must open an XEX file first!", "Error!");
            }
        }

        private void buttonAdd_Click(object sender, EventArgs e)
        {
            OpenFileDialog browser = new OpenFileDialog();
            browser.CheckFileExists = true;
            if (lastPath != null && Directory.Exists(lastPath)) browser.InitialDirectory = lastPath;
            if (browser.ShowDialog() == DialogResult.OK)
            {
                string path = Path.Combine(browser.FileName + ".data", "Data0000");
                if (listPackages.Items.Contains(browser.FileName))
                {
                    MessageBox.Show("God package already already in the list.", "God2Iso", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else if (System.IO.File.Exists(path))
                {
                    listPackages.Items.Add(browser.FileName);
                    lastPath = Path.GetDirectoryName(browser.FileName);
                }
                else
                {
                    MessageBox.Show("Could not find associated data file: " + path, "God2Iso", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void buttonRemove_Click(object sender, EventArgs e)
        {
            int selected = listPackages.SelectedIndex;
            if (selected < listPackages.Items.Count - 1)
            {
                listPackages.SelectedIndex = selected + 1;
            }
            else if (listPackages.Items.Count > 1)
            {
                listPackages.SelectedIndex = selected - 1;
            }
            listPackages.Items.RemoveAt(selected);
        }



        private void buttonClear_Click(object sender, EventArgs e)
        {
            listPackages.SelectedIndex = -1;
            listPackages.Items.Clear();
        }

        private void buttonBrowse_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog browser = new FolderBrowserDialog();
            browser.ShowNewFolderButton = true;
            browser.Description = "Select an output folder for the iso...";
            if (Directory.Exists(textOutput.Text)) browser.SelectedPath = textOutput.Text;
            if (browser.ShowDialog() == DialogResult.OK)
            {
                textOutput.Text = browser.SelectedPath;
            }
        }

        private void buttonGo_Click(object sender, EventArgs e)
        {
            try
            {
                if (listPackages.Items.Count < 1)
                {
                    MessageBox.Show("No God packages specified.", "God2Iso", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (textOutput.Text.Length < 1)
                {
                    MessageBox.Show("You must specify an output directory.", "God2Iso", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (!Directory.Exists(textOutput.Text))
                {
                    MessageBox.Show("Output directory does not exist.", "God2Iso", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                progressIso.Value = 0;
                // progressTotal.Value = 0;
                listPackages.SelectedIndex = -1;
                buttonAdd.Enabled = false;
                buttonGo.Enabled = false;
                buttonBrowse.Enabled = false;
                buttonClear.Enabled = false;
                listPackages.Enabled = false;
                textOutput.Enabled = false;
                cbFix.Enabled = false;

                // calculate total number & size of files
                int totalFiles = 0;
                ulong totalSize = 0;
                for (int i = 0; i < listPackages.Items.Count; i++)
                {
                    int count = 0;
                    while (true)
                    {
                        string path = Path.Combine(((string)listPackages.Items[i]) + ".data", "Data" + count.ToString("D4"));
                        if (!System.IO.File.Exists(path)) break;
                        count++;
                        FileInfo info = new FileInfo(path);
                        totalSize += (ulong)info.Length;
                    }
                    totalFiles += count;
                }

                try
                {
                    ManagementObject disk = new ManagementObject("win32_logicaldisk.deviceid=\"" + textOutput.Text.Substring(0, 1) + ":\"");
                    disk.Get();
                    ulong freeSpace = (ulong)disk["FreeSpace"];
                    if (totalSize > freeSpace)
                    {
                        MessageBox.Show("Not enough free space in output directory - need approximately " + ISO2GOD.FormatSize(totalSize) + '.', "God2Iso", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        // reset buttons
                        // return;
                        Application.Exit();
                    }
                }
                catch (Exception) { }

                // progressTotal.Maximum = totalFiles;
                //  if (windowsTaskbar != null) windowsTaskbar.SetProgressState(TaskbarProgressBarState.Normal);
                backgroundWorker1.RunWorkerAsync();
            }
            catch (Exception ex)
            {
                textBox1.Text = ex.Message + Environment.NewLine + ex.StackTrace;
            }
        }
        private void backgroundWorker1_DoWork_1(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;

            for (int i = 0; i < listPackages.Items.Count; i++)
            {

                FileStream iso = null;
                FileStream data = null;

                try
                {

                    string baseName = Path.GetFileName((string)listPackages.Items[i]);
                    string dataPath = (string)listPackages.Items[i] + ".data";

                    // count files
                    int totalFiles;
                    for (totalFiles = 0; true; totalFiles++)
                    {
                        string path = Path.Combine(dataPath, "Data" + totalFiles.ToString("D4"));
                        if (!System.IO.File.Exists(path)) break;
                    }
                    if (totalFiles < 1) return;

                    // check for xsf header
                    bool hasXSF = ISO2GOD.HasXSFHeader(Path.Combine(dataPath, "Data0000"));

                    // open new iso file
                    iso = new FileStream(Path.Combine(textOutput.Text, baseName + ".iso"), FileMode.Create, FileAccess.ReadWrite);

                    // add header, if needed
                    if (!hasXSF) iso.Write(GOD2ISO.XSFHeader, 0, GOD2ISO.XSFHeader.Length); ;

                    // loop through data parts
                    for (int fileNum = 0; fileNum < totalFiles; fileNum++)
                    {
                        string path = Path.Combine(dataPath, "Data" + fileNum.ToString("D4"));
                        data = new FileStream(path, FileMode.Open, FileAccess.Read);
                        data.Position = 0x2000;
                        int len = 0;
                        while (true)
                        {
                            byte[] buff = new byte[0xcc000];
                            len = data.Read(buff, 0, buff.Length);
                            iso.Write(buff, 0, len);
                            if (len < 0xcc000) break;
                            len = data.Read(buff, 0, 0x1000);
                            if (len < 0x1000) break;
                        }
                        data.Close();
                        data = null;
                        worker.ReportProgress(0, new GOD2ISO.MyState(fileNum + 1, totalFiles));
                    }

                    if (!hasXSF)
                    {
                        ISO2GOD.FixXFSHeader(iso);
                        ISO2GOD.FixSectorOffsets(iso, (string)listPackages.Items[i]);
                    }
                    if (cbFix.Checked) ISO2GOD.FixCreateIsoGoodHeader(iso);

                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "God2Iso", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    if (iso != null) try { iso.Close(); } catch (Exception) { }
                    if (data != null) try { data.Close(); } catch (Exception) { }
                }
            }
        }

        private void button19_Click(object sender, EventArgs e)
        {
            RegisterMSCOMCTLOCX.OpenXIB(true);
        }

        private void button20_Click(object sender, EventArgs e)
        {
            RegisterMSCOMCTLOCX.OpenXIB(false);
        }

        private void button10_Click_2(object sender, EventArgs e)
        {
            if (checkBox28.Checked)
            {
                invoker.InvokeXISO(invoker.ISOFilePath, "-x -s");
                this.Text = "X360GameHack 2025 - IcyModz420 (Waiting on XISO...)";
                Thread.Sleep(10000);
                this.Text = "X360GameHack 2025 - IcyModz420";
                MessageBox.Show("Your JTAG rip (extracted game) is located at: n/" + Application.StartupPath, "Extract-xiso Complete!");

            }
            else
            {
                invoker.InvokeXISO(invoker.ISOFilePath, "-x");
                this.Text = "X360GameHack 2025 - IcyModz420 (Waiting on XISO...)";
                Thread.Sleep(10000);
                this.Text = "X360GameHack 2025 - IcyModz420";
                MessageBox.Show("Your JTAG rip (extracted game) is located at: n/" + Application.StartupPath, "Extract-xiso Complete!");
            }
        }

        // Function to remove invalid characters
        string RemoveInvalidCharacters(string filePath, string[] invalidChars)
        {
            string sanitizedPath = new string(filePath.Where(c => !invalidChars.Contains(c.ToString())).ToArray());
            return sanitizedPath;
        }

        private void button17_Click(object sender, EventArgs e)
        {
            string[] invalidCharacters = { " ", "!", "@", "#", "$", "%", "^", "&", "*", "(", ")", "_", "-", "+", "=", "[", "]", "{", "}", "|", ";", "'", "?", "<", ">" };

            bool hasInvalidCharacter = invalidCharacters.Any(c => invoker.ISOFilePath.Contains(c));

            if (hasInvalidCharacter)
            {
                DialogResult result = MessageBox.Show(
                    "Error: The file name contains invalid characters. XISO does not allow special digits or spaces as parameters.\n" +
                    "Do you want to remove them?\n" +
                    "Acceptable Example:\n" +
                    "CallOfDuty.iso\n" +
                    "Fallout.iso\n" +
                    "Halo4.iso",
                    "Invalid Characters Found",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Error // Add error icon for better user experience
                );

                if (result == DialogResult.Yes)
                {
                    string sanitizedPath = RemoveInvalidCharacters(invoker.ISOFilePath, invalidCharacters);

                    // Check if the sanitized path is different from the original path
                    if (sanitizedPath != invoker.ISOFilePath)
                    {
                        try
                        {
                            if (System.IO.File.Exists(invoker.ISOFilePath))
                            {
                                System.IO.File.Move(invoker.ISOFilePath, sanitizedPath);
                            }
                            else
                            {
                                MessageBox.Show(invoker.ISOFilePath + "doesn't exist");
                            }
                            invoker.ISOFilePath = sanitizedPath; // Update the file path
                            textBox1.Text = invoker.ISOFilePath;
                            MessageBox.Show("File renamed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Error renaming file: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                }
            }
            Text = "X360GameHack 2025 (Waiting on XISO...)";
            if (checkBox29.Checked)
            {
                invoker.InvokeXISO(invoker.ISOFilePath, "-x -s");
            }
            else
            {
                invoker.InvokeXISO(invoker.ISOFilePath, "-x");
            }

            string filePath = invoker.ISOFilePath;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string jtagriplocation = Application.StartupPath + "//" + fileNameWithoutExtension;
            while (!Directory.Exists(jtagriplocation))
            {
                Thread.Sleep(2000);
            }
            Thread.Sleep(8500);

            this.Text = "X360GameHack 2025 (Patching XEX...)";
            string[] xexFiles = Directory.GetFiles(jtagriplocation, "*.xex");
            string XexNames = "";
            foreach (string file in xexFiles)
            {
                invoker.XexFilePath = file; //send path to generate batch
                invoker.InvokeXexTool(file, "-u -r a -m r -c u -e u", false);
                invoker.XexFilePath = ""; //reset jic
                XexNames = XexNames + file + Environment.NewLine;
            }
            if (checkBox27.Checked)
            {
                this.Text = "X360GameHack 2025 (Uploading Game...)";
                string GameDir = IP.Text + ":" + Port.Text + "/hdd0:/Games";
                FTPClient.UploadFile(jtagriplocation, GameDir, IP.Text, Port.Text, UserName.Text, Password.Text);
            }
            this.Text = "X360GameHack 2025";
        }

        private void button18_Click(object sender, EventArgs e)
        {
            string[] invalidCharacters = { " ", "!", "@", "#", "$", "%", "^", "&", "*", "(", ")", "_", "-", "+", "=", "[", "]", "{", "}", "|", ";", "'", "?", "<", ">" };

            bool hasInvalidCharacter = invalidCharacters.Any(c => invoker.ISOFilePath.Contains(c));

            if (hasInvalidCharacter)
            {
                DialogResult result = MessageBox.Show(
                    "Error: The file name contains invalid characters. XISO does not allow special digits or spaces as parameters.\n" +
                    "Do you want to remove them?\n" +
                    "Acceptable Example:\n" +
                    "CallOfDuty.iso\n" +
                    "Fallout.iso\n" +
                    "Halo4.iso",
                    "Invalid Characters Found",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Error // Add error icon for better user experience
                );

                if (result == DialogResult.Yes)
                {
                    string sanitizedPath = RemoveInvalidCharacters(invoker.ISOFilePath, invalidCharacters);

                    // Check if the sanitized path is different from the original path
                    if (sanitizedPath != invoker.ISOFilePath)
                    {
                        try
                        {
                            if (System.IO.File.Exists(invoker.ISOFilePath))
                            {
                                System.IO.File.Move(invoker.ISOFilePath, sanitizedPath);
                            }
                            else
                            {
                                MessageBox.Show(invoker.ISOFilePath + "doesn't exist");
                            }
                            invoker.ISOFilePath = sanitizedPath; // Update the file path
                            textBox1.Text = invoker.ISOFilePath;
                            MessageBox.Show("File renamed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Error renaming file: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                }
                if (result == DialogResult.No)
                {
                    MessageBox.Show("You must rename the ISO with no special character to use XISO!", "ISO Not touched!");
                    return;
                }
            }
            Text = "X360GameHack 2025 - IcyModz420 (Waiting on XISO...)";
            if (checkBox29.Checked)
            {
                invoker.InvokeXISO(invoker.ISOFilePath, "-x -s");
            }
            else
            {
                invoker.InvokeXISO(invoker.ISOFilePath, "-x");
            }

            string filePath = invoker.ISOFilePath;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string jtagriplocation = Application.StartupPath + "//" + fileNameWithoutExtension;
            while (!Directory.Exists(jtagriplocation))
            {
                Thread.Sleep(2000);
            }
            Thread.Sleep(8500);

            this.Text = "X360GameHack 2025 - IcyModz420 (Patching XEX...)";
            string[] xexFiles = Directory.GetFiles(jtagriplocation, "*.xex");
            string XexNames = "";
            foreach (string file in xexFiles)
            {
                invoker.XexFilePath = file; //send path to generate batch
                invoker.InvokeXexTool(file, "-u -r a -m d -c u -e u", false);
                invoker.XexFilePath = ""; //reset jic
                XexNames = XexNames + file + Environment.NewLine;
            }
            if (checkBox27.Checked)
            {
                this.Text = "X360GameHack 2025 (Uploading Game...)";
                string GameDir = IP.Text + ":" + Port.Text + "/hdd0:/Games";
                FTPClient.UploadFile(jtagriplocation, GameDir, IP.Text, Port.Text, UserName.Text, Password.Text);
            }
            this.Text = "X360GameHack 2025";
        }

        private void tabPage6_Click(object sender, EventArgs e)
        {

        }

        private void button16_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(textBox1.Text, "-x a", false);
            MessageBox.Show("Files Extracted from XEX!", "X360GameHack Info!");
        }

        private void button22_Click(object sender, EventArgs e)
        {
            //FTPClient.SelectFileToUpload();
            FTPClient.UploadFile(FTPClient.FilePathToFileToUpload, CurrentFTPDirectory.Text, IP.Text, Port.Text, UserName.Text, Password.Text);
        }

        private void button28_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start(Application.StartupPath + "/" + "abgx360_v1.0.7_setup_hadzz.exe");
            }
            catch
            {

            }
        }

        private void button29_Click(object sender, EventArgs e)
        {
            if (System.IO.File.Exists("C:\\Program Files (x86)\\abgx360\\abgx360gui.exe"))
            {
                Process.Start("C:\\Program Files (x86)\\abgx360\\abgx360gui.exe");
            }
            else
            {
                MessageBox.Show("abgx not installed on C drive!", "Error!");
            }
        }

        private void button37_Click(object sender, EventArgs e)
        {
            FTPClient.IP = IP.Text;
            FTPClient.Port = Port.Text;
            FTPClient.UserName = UserName.Text;
            FTPClient.Password = Password.Text;
            FTPClient.SendCurDirToGS = CurrentFTPDirectory.Text;
            GameSender gs = new GameSender();
            gs.Show();
        }

        private void button40_Click(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, "-m 1");
        }

        private void button23_Click(object sender, EventArgs e)
        {
            FTPClient.DownloadFile(listBox1.SelectedItem.ToString(), CurrentFTPDirectory.Text, IP.Text, Port.Text, UserName.Text, Password.Text);
        }

        private void button41_Click(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, "-m 0");
        }

        private void button42_Click(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, "-b 0 ");
        }

        private void button43_Click(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, "-b 1 ");
        }

        private void button44_Click(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, "-z");
        }

        private void button45_Click(object sender, EventArgs e)
        {
            if (!invoker.GenerateBatch)
            {
                invoker.GenerateBatch = true;
                invoker.InvokeGodTool(invoker.GodFilePath, "-p");
                invoker.GenerateBatch = false;
            }
            else
            {
                invoker.InvokeGodTool(invoker.GodFilePath, "-p");
            }

        }

        private void button46_Click(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, "-st " + textBox10.Text);
        }

        private void button47_Click(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, "-sn " + textBox11.Text);
        }

        private void button48_Click(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, "-sd " + textBox12.Text);
        }

        private void button49_Click(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, "-sp " + textBox13.Text);
        }

        private void button24_Click(object sender, EventArgs e)
        {
            listBox1.Items.Clear();
            try
            {
                // Create an FTP request
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create($"ftp://" + IP.Text + ":" + Port.Text);
                request.Method = WebRequestMethods.Ftp.ListDirectory;
                request.Credentials = new NetworkCredential(UserName.Text, Password.Text);


                // Get the response
                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))

                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            // Parse the FTP directory listing line (format may vary)
                            // You might need to adjust this parsing logic based on your FTP server's listing format
                            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            string fileName = parts[parts.Length - 1];

                            // Add the file or directory name to the ListBox

                            listBox1.Items.Add(fileName);
                            CurrentFTPDirectory.Text = IP.Text + ":" + Port.Text;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions, e.g., log errors, display error messages
                MessageBox.Show("Error: " + ex.Message);
            }
            // FTPClient.RefreshFTPListBox(listBox1.Items, IP.Text, Port.Text, UserName.Text, Password.Text);
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void tabControl1_DragDrop(object sender, DragEventArgs e)
        {

        }

        private void listBox1_DragDrop(object sender, DragEventArgs e)
        {
            FTPClient.FilePathToFileToUpload = CurrentFTPDirectory.Text;
            FTPClient.UploadFile(FTPClient.FilePathToFileToUpload, CurrentFTPDirectory.Text, IP.Text, Port.Text, UserName.Text, Password.Text);
        }

        private void button52_Click(object sender, EventArgs e)
        {
            try
            {
                // Create an FTP request
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create($"ftp://" + IP.Text + "/" + CurrentFTPDirectory.Text + ":" + Port.Text);
                request.Method = WebRequestMethods.Ftp.GetFileSize;
                request.Credentials = new NetworkCredential(UserName.Text, Password.Text);


                // Get the response
                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))

                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            // Parse the FTP directory listing line (format may vary)
                            // You might need to adjust this parsing logic based on your FTP server's listing format
                            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            string fileName = parts[parts.Length - 1];

                            // Add the file or directory name to the ListBox

                            MessageBox.Show(fileName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions, e.g., log errors, display error messages
                MessageBox.Show("Error: " + ex.Message);
            }
        }

        private void button53_Click(object sender, EventArgs e)
        {
            FTPClient.CreateFtpDirectory(CurrentFTPDirectory.Text, textBox6.Text, IP.Text, Port.Text, UserName.Text, Password.Text);
        }

        private void button51_Click(object sender, EventArgs e)
        {
            FTPClient.RenameFile(listBox1.SelectedItem.ToString(), textBox5.Text, CurrentFTPDirectory.Text, IP.Text, Port.Text, UserName.Text, Password.Text);
        }

        private void button25_Click(object sender, EventArgs e)
        {
            MessageBox.Show("An xex file or xbox 360 executable file is similar to exe files on a pc a games is made up of assets shaders scripts and etc and the executable code is stored in the xex files. long story short XeX files launch the games and keep the assets going in most cases.", "Xbox 360 Executable Info:");
        }

        private void button26_Click(object sender, EventArgs e)
        {
            MessageBox.Show("If your extracting the files from an iso and intend to use them on an RGH console you would want to remove the need for a title update patch or it may crash without a tu.. you would also want to remove all xex limits to further expand the amount of potinial games that will work. No not all games -need- to be patched but it will definitally help not hurt.. and also removing compression and encryption improves loading times.", "Do you need to patch your xex files:");
        }

        private void listBox1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            
        }

        private void listBox1_DoubleClick(object sender, EventArgs e)
        {
            MessageBox.Show("You must hit forward to select this!", "Error!");
        }

        private void button27_Click(object sender, EventArgs e)
        {
            
        }

        private void button30_Click(object sender, EventArgs e)
        {
       //   
        }

        private void button21_Click(object sender, EventArgs e)
        {
            FTPClient.DeleteFile(listBox1.SelectedItem.ToString(), CurrentFTPDirectory.Text, IP.Text, Port.Text, UserName.Text, Password.Text);
        }

        private void button54_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start(Application.StartupPath + "/" + "ISO2God.exe");
            }
            catch
            {

            }
        }

        private void button36_Click(object sender, EventArgs e)
        {
           // MessageBox.Show("Extr");
        }

        private void button30_Click_1(object sender, EventArgs e)
        {
            Properties.Settings.Default.IP = IP.Text;
            Properties.Settings.Default.Port = Port.Text;
            Properties.Settings.Default.Username = UserName.Text;
            Properties.Settings.Default.Password = Password.Text;
            Properties.Settings.Default.Save();
            MessageBox.Show("Settings Saved!");
        }

        private void checkBox27_CheckedChanged(object sender, EventArgs e)
        {
            MessageBox.Show("Be sure to save your FTP info if you havent already!", "X360GameHack Info!");
        }

        private void button55_Click(object sender, EventArgs e)
        {
            MessageBox.Show("XEX and PKG Patches By: Xorloser \n XISO By: XboxDev Organization \n GOD2ISO By: Raburton \n ISO2GOD By: Others and updated by R4dius \n UI and FTP2Xbox By: IcyModz420", "X360GameHack Credits!");
        }

        private void button35_Click(object sender, EventArgs e)
        {
            if (!System.IO.File.Exists(textBox1.Text + ".bak"))
            {
                System.IO.File.Copy(textBox1.Text, textBox1.Text + ".bak");
            }
            else
            {
                MessageBox.Show("File Already exists!", "X360GameHack Info!");
            }
        }

        private void button31_Click(object sender, EventArgs e)
        {
            MessageBox.Show("An STFS File otherwise known as god live con pirs files are a file format in which the game is stored into a single file that the xbox 360 can read it also contains identifier strings security and etc.", "What is an STFS file?");
        }

        private void button32_Click(object sender, EventArgs e)
        {

        }

        private void button34_Click(object sender, EventArgs e)
        {

        }

        private void button33_Click(object sender, EventArgs e)
        {

        }
    }
}

