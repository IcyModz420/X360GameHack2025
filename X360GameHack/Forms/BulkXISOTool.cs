using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack;
using DevComponents.DotNetBar.Metro;
using System.Diagnostics;
using X360GameHack.Security;

namespace X360GameHack
{
    public partial class BulkXISOTool : MetroForm
    {
        public BulkXISOTool()
        {
            InitializeComponent();
        }
        public string ExtractorPathFromForm = "";
        Invoker invoker = new Invoker();
        XISOExtractorExtention XISOEE = new XISOExtractorExtention();
        XBEPatches XBEPatches = new XBEPatches();
        AntiCommandInjection APT = new AntiCommandInjection();

        private void listBox2_DragDrop(object sender, DragEventArgs e)
        {
            if(listBox2.Items.Contains("Drag your XISO in this box:"))
            {
                listBox2.Items.Clear();
            }
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (string path in files)
            {

                    string[] invalidCharacters = { " ", "!", "@", "#", "$", "%", "^", "&", "*", "(", ")", "_", "-", "+", "=", "[", "]", "{", "}", "|", ";", "'", "?", "<", ">", "," };

                    bool hasInvalidCharacter = invalidCharacters.Any(c => path.Contains(c));

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

                        string sanitizedPath = RemoveInvalidCharacters(path, invalidCharacters);

                        // Check if the sanitized path is different from the original path
                        if (sanitizedPath != invoker.ISOFilePath)
                        {
                            try
                            {
                                if (System.IO.File.Exists(path))
                                {
                                    System.IO.File.Move(path, sanitizedPath);
                                }
                                else
                                {
                                    MessageBox.Show(path + "doesn't exist");
                                }
                                APT.SanitizeInvokerFilePath(sanitizedPath);
                                invoker.ISOFilePath = sanitizedPath; // Update the file path
                                listBox2.Items.Add(sanitizedPath); // Add the sanitized path to the list box

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
            }
        }

        private void listBox2_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        // Function to remove invalid characters
        string RemoveInvalidCharacters(string filePath, string[] invalidChars)
        {
            string sanitizedPath = new string(filePath.Where(c => !invalidChars.Contains(c.ToString())).ToArray());
            return sanitizedPath;
        }

        public bool ContainsDefaultXbe(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                return false; // Handle null, empty, or non-existent folders
            }

            try
            {
                string[] files = Directory.GetFiles(folderPath);

                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file).ToLower(); // Extract filename and lowercase

                    if (fileName == "default.xbe")
                    {
                        return true; // Found a matching file
                    }
                }

                return false; // No matching files found
            }
            catch (Exception)
            {
                return false; // Return false on error
            }
        }

        public bool ContainsDefaultXex(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                return false; // Handle null, empty, or non-existent folders
            }

            try
            {
                string[] files = Directory.GetFiles(folderPath);

                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file).ToLower(); // Extract filename and lowercase

                    if (fileName == "default.xex" || fileName == "default_mp.xex")
                    {
                        return true; // Found a matching file
                    }
                }

                return false; // No matching files found
            }
            catch (Exception)
            {
                return false; // Return false on error
            }
        }

        public bool S = false;
        private async void button67_Click(object sender, EventArgs e)
        {
            if (listBox2.Items.Count > 0)
            {
                MessageBox.Show("No items to extract dropped in list box.", "X360GameHack Info!");
                return;
            }
            else
            {
                button67.Enabled = false;
                Text = "X360GameHack Bulk XISO Tool (Finding the XISO(s) to Extract...)";

                if (checkBox30.Checked)
                {
                //    XISOEE.ExtractSystemUpdate = false;
                }
                if (!checkBox30.Checked)
                {
                 //   XISOEE.ExtractSystemUpdate = true;
                }
              //  XISOEE.DefaultExtactionLocation = true; // reset default bool
                if (radioButton2.Checked)
                {
                //    XISOEE.DefaultExtactionLocation = false;
                 //   XISOEE.ExtractorPath = textBox1.Text; // this will not set it for the invoker class it needs to be sent fresh
                    Properties.Settings.Default.ExtractPath = textBox1.Text;// like so
                    Properties.Settings.Default.Save(); // workaround making new instances of classes won't work
                                                        // MessageBox.Show(XISOEE.ExtractorPath + " \n\n Setting Saved!");
                }
                string extractXisoPath = Path.Combine(Application.StartupPath, "extract-xiso.exe");
                if (!File.Exists(extractXisoPath))
                {
                    MessageBox.Show("extract-xiso.exe not found!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    button67.Enabled = true;
                    return;
                }
                Text = "X360GameHack Bulk XISO Tool (Extracting the XISO(s) with Extract-XISO...)";
                foreach (string isoPath in listBox2.Items)
                {
                 //   bool success = await XISOEE.ExtractISOAsync(isoPath);
                 //   if (!success)
                 //   {
                        MessageBox.Show($"Failed to extract {isoPath}", "Extraction Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
              //      }
                }
              //  if (File.Exists(Path.Combine(XISOEE.ExtractorPath, "extract-xiso.exe")) && radioButton2.Checked == true)
              //  {
              //      File.Delete(Path.Combine(XISOEE.ExtractorPath, "extract-xiso.exe"));
              //  }
                //
                //
                // get xex locations
                foreach (string path in listBox2.Items)
                {
                    Text = "X360GameHack 2025 Bulk XISO Tool v2.2 (Checking for XEX Locations...)";

                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
                    string jtagriplocation = Application.StartupPath + @"\" + fileNameWithoutExtension;
                    if (radioButton2.Checked)
                    {
                        jtagriplocation = Properties.Settings.Default.ExtractPath + @"\" + fileNameWithoutExtension;
                    }
                    string[] xexFiles = Directory.GetFiles(jtagriplocation, "*.xex");
                    foreach (string file in xexFiles)
                    {
                        if (ContainsDefaultXex(jtagriplocation))
                        {
                            if (checkBox15.Checked)
                            {
                                Text = "X360GameHack 2025 (Backing Up XEX File(s)...)";
                                try
                                {
                                    File.Copy(file, file + "OrininalUnpatchedBackup");
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Failed to backup xex file exception:" + ex, "X360GameHack Info!");
                                }
                            }
                            Text = "X360GameHack 2025 (Patching XEX File(s)...)";
                            invoker.XexFilePath = file; // send path to generate batch
                            invoker.GenerateBatch = true; // turn on generate batch

                            if (checkBox2.Checked)
                            {
                                invoker.InvokeXexTool(file, "-r a -m r", false);
                            }
                            if (checkBox3.Checked)
                            {
                                invoker.InvokeXexTool(file, "-r a -m d", false);
                            }
                            if (checkBox4.Checked)
                            {
                                invoker.InvokeXexTool(file, "-r a", false);
                            }
                            if (checkBox16.Checked)
                            {
                                invoker.InvokeXexTool(file, textBox4.Text, false);
                            }
                        }
                        //
                        //
                        //
                        // patch xbes
                        foreach (string path1 in listBox2.Items)
                        {
                            Text = "X360GameHack 2025 Bulk XISO Tool v2.2 (Checking for XBE Locations...)";

                            string fileNameWithoutExtension1 = Path.GetFileNameWithoutExtension(path1);
                            string ogriplocation = Application.StartupPath + @"\" + fileNameWithoutExtension1;
                            if (radioButton2.Checked)
                            {
                                ogriplocation = Properties.Settings.Default.ExtractPath + @"\" + fileNameWithoutExtension1;
                            }
                            string[] xbeFiles = Directory.GetFiles(ogriplocation, "*.xbe");
                            foreach (string xbe in xexFiles)
                            {
                                if (ContainsDefaultXbe(ogriplocation))
                                {
                                    if (checkBox11.Checked)
                                    {
                                        Text = "X360GameHack 2025 (Backing Up XBE File(s)...)";
                                        try
                                        {
                                            File.Copy(xbe, xbe + "OriginalUnpatchedBackup");
                                        }
                                        catch (Exception ex)
                                        {
                                            MessageBox.Show("Failed to backup xbe file exception:" + ex, "X360GameHack Info!");
                                        }
                                    }
                                    Text = "X360GameHack 2025 (Patching XBE File(s)...)";
                                    //patch xbes
                                    if (checkBox8.Checked)
                                    {
                                        XBEPatches.PatchXBERam(xbe, "stock");
                                    }
                                    if (checkBox1.Checked)
                                    {
                                        XBEPatches.PatchXBERam(xbe, "128");
                                    }
                                    if (checkBox13.Checked)
                                    {
                                        XBEPatches.PatchXBECPUScale(xbe, 733);
                                    }
                                    if (checkBox5.Checked)
                                    {
                                        XBEPatches.PatchXBECPUScale(xbe, 1000);
                                    }
                                    if (checkBox6.Checked)
                                    {
                                        XBEPatches.PatchXBECPUScale(xbe, 1400);
                                    }
                                    if (checkBox7.Checked)
                                    {
                                        XBEPatches.PatchXBECPUScale(xbe, 1480);
                                    }
                                }
                            }
                        }
                    }
                }
                if (checkBox9.Checked)
                {
                    DialogResult result = MessageBox.Show("You have chosen to delete all XISO in the listbox. This may take some time and may not automatically recycle them. \n Do You Want To Proceed?", "X360GameHack Confirmation!", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        this.Text = "X360GameHack 2025 Bulk XISO Tool v2 (Deleting all XISO in listbox...)";
                        foreach (string item in listBox2.Items)
                        {
                            File.Delete(item);
                        }
                        listBox2.Items.Clear();
                    }
                }
                this.Text = "X360GameHack 2025 Bulk XISO Tool v2.2";
                button67.Enabled = true;
            }
        }

        private void button66_Click(object sender, EventArgs e)
        {
            Text = "X360GameHack 2025 (Waiting on Extract-XISO...)";
            if (checkBox30.Checked)
            {
                foreach (string line in listBox2.Items)
                {
                    invoker.GenerateBatch = true;
                    invoker.ISOFilePath = line;
                    invoker.InvokeXISO(line, "-x -s");
                    // continue;
                }
            }
            else
            {
                foreach (string path in listBox2.Items)
                {
                    invoker.InvokeXISO(path, "-x");
                    //continue;
                }
            }
            foreach (string path in listBox2.Items)
            {
                this.Text = "X360GameHack 2025 (Getting XEX Location...)";
                string filePath = path;
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
                string jtagriplocation = Application.StartupPath + "//" + fileNameWithoutExtension;
                //while path doesn't contain default.xex or default_mp.xex or exist
                MessageBox.Show("jtag rip location /n" + jtagriplocation);
                while (!Directory.Exists(jtagriplocation) || ContainsDefaultXex(jtagriplocation) == false)
                {
                    this.Text = "X360GameHack 2025 (Waiting to patch XEX...)";
                    Thread.Sleep(5000);

                }
                this.Text = "X360GameHack 2025 (Patching XEX...)";
                string[] xexFiles = Directory.GetFiles(jtagriplocation, "*.xex");
                string XexNames = "";
                foreach (string file in xexFiles)
                {

                    invoker.XexFilePath = file; //send path to generate batch
                    invoker.InvokeXexTool(file, "-u -r a -m d -c u -e u", false);
                    invoker.XexFilePath = ""; //reset jic
                    XexNames = XexNames + file + Environment.NewLine;
                }
            }
            this.Text = "X360GameHack 2025 Game Extractor";
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Text = "X360GameHack 2025 (Waiting on XISO...)";
            if (checkBox30.Checked)
            {
                foreach (string line in listBox2.Items)
                {
                    invoker.GenerateBatch = true;
                    invoker.ISOFilePath = line;
                    invoker.InvokeXISO(line, "-x -s");
                    // continue;
                }
            }
            else
            {
                foreach (string path in listBox2.Items)
                {
                    invoker.InvokeXISO(path, "-x");
                    //continue;
                }
            }
            foreach (string path in listBox2.Items)
            {
                this.Text = "X360GameHack 2025 (Getting XEX Location...)";
                string filePath = path;
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
                string jtagriplocation = Application.StartupPath + "//" + fileNameWithoutExtension;
                //while path doesn't contain default.xex or default_mp.xex or exist
                MessageBox.Show("jtag rip location /n" + jtagriplocation);
                while (!Directory.Exists(jtagriplocation) || ContainsDefaultXex(jtagriplocation) == false)
                {
                    this.Text = "X360GameHack 2025 (Waiting to patch XEX...)";
                    Thread.Sleep(5000);

                }
                this.Text = "X360GameHack 2025 (Patching XEX...)";
                string[] xexFiles = Directory.GetFiles(jtagriplocation, "*.xex");
                string XexNames = "";
                foreach (string file in xexFiles)
                {

                    invoker.XexFilePath = file; //send path to generate batch
                    invoker.InvokeXexTool(file, "-r a -m r", false);
                    invoker.XexFilePath = ""; //reset jic
                    XexNames = XexNames + file + Environment.NewLine;
                }
            }
            this.Text = "X360GameHack 2025 Game Extractor";
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (listBox2.Items.Count > 0)
            {
                MessageBox.Show("No items to extract dropped in list box.", "X360GameHack Info!");
                return;
            }
            else
            {


                Text = "X360GameHack 2025 (Waiting on XISO...)";
                if (checkBox30.Checked)
                {
                    foreach (string line in listBox2.Items)
                    {
                        invoker.GenerateBatch = true;
                        invoker.ISOFilePath = line;
                        invoker.InvokeXISO(line, "-x -s");
                        // continue;
                    }
                }
                else
                {
                    foreach (string path in listBox2.Items)
                    {
                        invoker.InvokeXISO(path, "-x");
                        //continue;
                    }
                }
                this.Text = "X360GameHack 2025 Game Extractor";
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (listBox2.SelectedIndex != -1) // Check if an item is selected
            {
                listBox2.Items.RemoveAt(listBox2.SelectedIndex);
            }
            else
            {
                MessageBox.Show("Please select an item to remove.", "X360GameHack");
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (listBox2.SelectedIndex != -1) // Check if an item is selected
            {
                MessageBox.Show("Please select an item in the listbox to remove.", "X360GameHack");
                return;
            }
            else
            {


                Text = "X360GameHack 2025 (Waiting on XISO...)";
                if (checkBox30.Checked)
                {
                    string selectedpathtoiso = listBox2.SelectedItem.ToString();
                    invoker.GenerateBatch = true;
                    invoker.ISOFilePath = selectedpathtoiso;
                    invoker.InvokeXISO(selectedpathtoiso, "-x -s");
                }
                else
                {
                    string selectedpathtoiso = listBox2.SelectedItem.ToString();
                    foreach (string path in listBox2.Items)
                    {
                        invoker.InvokeXISO(selectedpathtoiso, "-x");
                    }
                }
                this.Text = "X360GameHack 2025 Game Extractor";
            }
        }

        private void ExtractSeveralGames_Load(object sender, EventArgs e)
        {

        }

        private void checkBox9_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void checkBox10_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void groupBox18_Enter(object sender, EventArgs e)
        {

        }

        private void button10_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog folderDialog = new FolderBrowserDialog();
            folderDialog.Description = "Select a folder";
            DialogResult result = folderDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                string selectedPath = folderDialog.SelectedPath;
                textBox1.Text = selectedPath;
            }

        }

        private async void button8_Click(object sender, EventArgs e)
        {
            if (listBox2.Items.Count > 0)
            {
                MessageBox.Show("No items to extract dropped in list box.", "X360GameHack Info!");
                return;
            }
            else
            {
                XISOExtractorExtention XISOEE = new XISOExtractorExtention();
                foreach (string isoPath in listBox2.Items)
                {
                    if (isoPath.Contains(".iso"))
                    {
                      //  bool success = await XISOEE.ExtractISOAsync(isoPath);
                        //if (!success)
                        //{
                            MessageBox.Show($"Failed to extract {isoPath}", "Extraction Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                       // }
                    }
                }
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {

        }

        private void BulkXISOTool_FormClosing(object sender, FormClosingEventArgs e)
        {
            Environment.Exit(0);
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            listBox2.Items.Clear();
        }

        private void listBox2_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {

        }
    }
}
