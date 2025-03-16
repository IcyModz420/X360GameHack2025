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

namespace X360GameHack
{
    public partial class ExtractSeveralGames : Form
    {
        public ExtractSeveralGames()
        {
            InitializeComponent();
        }
        Invoker invoker = new Invoker();
        private void listBox2_DragDrop(object sender, DragEventArgs e)
        {
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
                                invoker.ISOFilePath = sanitizedPath; // Update the file path

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
                listBox2.Items.Add(path);
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
            catch (Exception ex)
            {
                return false; // Return false on error
            }
        }

        private void button67_Click(object sender, EventArgs e)
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
                    invoker.InvokeXexTool(file, "-u -r a -m r -c u -e u", false);
                    invoker.XexFilePath = ""; //reset jic
                    XexNames = XexNames + file + Environment.NewLine;
                }
            }
            this.Text = "X360GameHack 2025 Game Extractor";
        }

        private void button66_Click(object sender, EventArgs e)
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

        private void ExtractSeveralGames_Load(object sender, EventArgs e)
        {

        }
    }
}
