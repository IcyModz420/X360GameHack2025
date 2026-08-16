using DevComponents.DotNetBar;
using DevComponents.DotNetBar.Metro;
using Krypton.Toolkit;
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
using System.Net.Http;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack;
using X360GameHack.Core.Other;
using X360GameHack.Core.Xbox_360;
using X360GameHack.Core.Xbox360;
using X360GameHack.Other;
using X360GameHack.Properties;
using X360GameHack.Security;
//using Xbox360Tools;
//using X360GameHack.Core.Xbox360.XDCKIT;
using static System.Net.WebRequestMethods;
using static System.Windows.Forms.AxHost;

namespace X360GameHack
{
    public partial class X360GameHack : MetroForm
    {
        public static X360GameHack CurrentInstance { get; private set; }
        public static XboxConsole xdk;
        Invoker invoker = new Invoker();
        //TitleIDChanger tidchanger = new TitleIDChanger();
        GOD2ISO ISO2GOD = new GOD2ISO();
        RegisterMSCOMCTLOCX RegisterMSCOMCTLOCX = new RegisterMSCOMCTLOCX();
        FTPClient FTPClient = new FTPClient();
        Pastebin Pastebin = new Pastebin();
        Animations Anims = new Animations();
        XBEPatches XBEPatches = new XBEPatches();
        AntiDebug AntiNoob = new AntiDebug();
        MultiExecutionFix MEF = new MultiExecutionFix();
        //XbdmClient XBDMClientInstanceForOptions = new XbdmClient();
        USBDrives USBDrives = new USBDrives();
        AntiAdmin AntiAdmin = new AntiAdmin();
        XISOExtractorExtention XISOEE = new XISOExtractorExtention();
        //XbdmClient XBDMClient = new XbdmClient("");
        private string lastPath = null;
        private readonly HttpClient SpoofChrome;
        public static XboxConsole ConsoleX = new XboxConsole();

        public X360GameHack()
        {
            AntiNoob.DoAntiDebugFunc(); // no debugging in release version
            AntiAdmin.EnforceNonAdmin(); // do this here so no code runs
            AntiNoob.DoAntiDebugFunc();
            InitializeComponent();
            CurrentInstance = this;
            //SpoofChrome = new HttpClient();
            // SpoofChrome.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36");
            // Allow drag and drop on the GroupBox
            groupBox1.AllowDrop = true;
            // Subscribe to the DragEnter event
            groupBox1.DragEnter += groupBox1_DragEnter;
            // Subscribe to the DragDrop event
            groupBox1.DragDrop += groupBox1_DragDrop;
        }

        public void UpdateListboxForOutput(string line)
        {
            listBox2.Items.Add(line);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            pictureBox1.Show();
            string filePath = textBox1.Text;
            string fileName = new System.IO.FileInfo(filePath).Name;
            double fileSizeMb = new System.IO.FileInfo(filePath).Length / 1024.0 / 1024.0;
            label81.Text = fileName;
            label83.Text = fileSizeMb.ToString("F2") + " MBs";
            string extension = Path.GetExtension(filePath);
            AntiCommandInjection APT = new AntiCommandInjection();
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = Application.StartupPath;
                openFileDialog.RestoreDirectory = true;
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    //open file dialog
                    filePath = openFileDialog.FileName;
                    // pass filepath to invoker class
                    if (filePath.Contains(".xex") || filePath.Contains(".exe") || filePath.Contains(".dll"))
                    {
                        label1.Text = "Xbox 360 Executable";
                        APT.SanitizeInvokerFilePath(filePath);
                        invoker.XexFilePath = filePath; //set invoker                        
                        textBox1.Text = filePath; //show path
                        groupBox4.Show();
                        tabControl1.SelectedIndex = 0;
                        XEXParcer XEXParcer = new XEXParcer();
                        XEXParcer.GetXEXInfo(textBox1.Text);
                        textBox15.Text = XEXParcer.XEXName;
                        textBox46.Text = XEXParcer.XEXSystem;
                        textBox44.Text = XEXParcer.Encrypted;
                        textBox43.Text = XEXParcer.Compressed;
                        textBox4.Text = XEXParcer.MediaId;
                        textBox3.Text = XEXParcer.TitleId;
                        textBox41.Text = XEXParcer.XEXVersion;
                        textBox9.Text = XEXParcer.BaseVersion;
                        textBox19.Text = XEXParcer.EntryPoint;
                        textBox20.Text = XEXParcer.LoadAddress;
                        GetIconFrombase64(XEXParcer.GameIconBase64);
                        pictureBox1.Hide();
                        return;
                    }
                    else if (filePath.Contains(".iso"))
                    {
                        label1.Text = "OG/Xbox 360 ISO";
                        APT.SanitizeInvokerFilePath(filePath);
                        invoker.ISOFilePath = filePath; //set invoker
                        textBox1.Text = invoker.ISOFilePath;//show path
                        groupBox4.Show();
                        tabControl1.SelectedIndex = 2;
                        pictureBox1.Hide();
                        return;
                    }
                    else if (filePath.Contains(".xbe"))
                    {
                        label1.Text = "Original Xbox Executable";
                        APT.SanitizeInvokerFilePath(filePath);
                        invoker.XBEFilePath = filePath; //set invoker
                        textBox1.Text = invoker.XBEFilePath;//show path
                        groupBox4.Show();
                        tabControl1.SelectedIndex = 4;
                        pictureBox1.Hide();
                        return;
                    }
                    else if (string.IsNullOrEmpty(extension))
                    {
                        label1.Text = "Xbox 360 PKG";
                        APT.SanitizeInvokerFilePath(filePath);
                        invoker.ISOFilePath = filePath; //set invoker
                        textBox1.Text = invoker.GodFilePath;//show path
                        groupBox4.Show();
                        tabControl1.SelectedIndex = 1;
                        Xbox360PkgParser Xbox360PkgParser = new Xbox360PkgParser();
                        Xbox360PkgParser.GetPkgInfo(textBox1.Text);
                        textBox17.Text = Xbox360PkgParser.PkgType;
                        textBox18.Text = Xbox360PkgParser.TitleName;
                        textBox37.Text = Xbox360PkgParser.PkgVersion;
                        textBox45.Text = Xbox360PkgParser.BaseVersion;
                        textBox40.Text = Xbox360PkgParser.TitleId;
                        textBox39.Text = Xbox360PkgParser.MediaId;
                        textBox11.Text = Xbox360PkgParser.DisplayName;
                        textBox12.Text = Xbox360PkgParser.Description;
                        textBox13.Text = Xbox360PkgParser.Publisher;
                        pictureBox1.Hide();
                        return;
                    }
                    else if (filePath.Contains(".bin"))
                    {
                        label1.Text = "Xbox 360 Save Pack";
                        APT.SanitizeInvokerFilePath(textBox1.Text);
                        invoker.GodFilePath = textBox1.Text; //set invoker
                        groupBox4.Show();
                        tabControl1.SelectedIndex = 3;
                        SaveParser SaveParcer = new SaveParser();
                        SaveParcer.GetPkgInfo(textBox1.Text);
                        textBox55.Text = SaveParcer.PkgType;
                        textBox48.Text = SaveParcer.DisplayName;
                        textBox54.Text = SaveParcer.TitleName;
                        textBox53.Text = SaveParcer.PkgVersion;
                        textBox49.Text = SaveParcer.BaseVersion;
                        textBox51.Text = SaveParcer.TitleId;
                        textBox50.Text = SaveParcer.MediaId;
                        textBox38.Text = SaveParcer.InstallDir;
                        textBox56.Text = SaveParcer.ProfileID;
                        textBox57.Text = SaveParcer.ConsoleID;
                        pictureBox1.Hide();
                        return;
                    }
                    else
                    {
                        label1.Text = "???";
                        groupBox4.Show();
                        MessageBox.Show("You have attempted to open a file with an unknown to X360GameHack extention... \nX360GameHack will not use it nor set the invoker for it...\nSupported files are: \nISO\nXEX\nXBE\nand No Extention for 360 PKGs!", "X360GameHack Info!");
                    }

                }
            }
        }

        private async void button3_Click(object sender, EventArgs e)
        {
            string xexpath = invoker.XexFilePath;
            if (xexpath != "" || xexpath != " ")
            {
                invoker.InvokeXexTool(xexpath, "-e e", false);
            }
            else
            {
                MessageBox.Show("This does not work with ISO you must extract it first..", "You need to open a XEX file first..");
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            string xexpath = invoker.XexFilePath;
            if (xexpath != "" && xexpath != " ")
            {
                invoker.InvokeXexTool(xexpath, "-e u", false);
            }
            else
                MessageBox.Show("This does not work with ISO you must extract it first..", "You need to open a XEX file first..");
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.Size = new Size(978, 834);
            pictureBox1.Hide();
            tabControl5.TabPages.Remove(tabControl5.TabPages[2]); // hide X360Debug Tool tab 
            tabControl4.TabPages.Remove(tabControl4.TabPages[2]); // hide ini editors tab
            groupBox5.AllowDrop = true; // property gone had to do this
            invoker.CaptureOutput = true; // Always capture tool output unless they check it off in settings..
            AntiAdmin.EnforceNonAdmin();
            AntiNoob.DoAntiDebugFunc();
            MEF.CheckForMultiExecution();
            timer1.Start();
            Anims.StartButtonAnimation(button67, 1000);
            Anims.StartBuyRGH(button75, 1000);

            tabControl1.SizeMode = TabSizeMode.Fixed;
            if (Pastebin.IsInternetAvailable() == true && Pastebin.IsLatestVersion() == true)// show latest version if it is
            {
                Pastebin.GetYYY();
                Text = Text + " " + Pastebin.CurrentVersion + " Latest Version!" /*Opened " + Pastebin.GetOpenCount() + " unique times to date!"*/;
            }
            else if (Pastebin.IsInternetAvailable() == true && Pastebin.IsLatestVersion() == false)// show outdated only if it is
            {
                Text = Text + " Outdated Version! (" + Pastebin.CurrentVersion + ") Latest Version is " + Pastebin.GetLatestVersion() + "!";
            }
            else if (Pastebin.IsInternetAvailable() == false) // go offline
            {
                Text = Text + " " + Pastebin.CurrentVersion + " (Offline)";
            }
            IP.Text = Properties.Settings.Default.IP;
            textBox7.Text = Properties.Settings.Default.IP; //ftp2xell ip
            Port.Text = Properties.Settings.Default.Port;
            UserName.Text = Properties.Settings.Default.Username;
            Password.Text = Properties.Settings.Default.Password;
            //groupBox4.Hide();
            label1.Text = "";
            label81.Text = "";
            label83.Text = "";
            if (checkBox30.Checked) // do cmd to list box
            {
                invoker.CaptureOutput = true;
                invoker.GenerateBatch = false;
            }
            else if (checkBox31.Checked) // do batch 
            {
                invoker.CaptureOutput = true;
                invoker.GenerateBatch = false;
            }

        }

        private async void button6_Click(object sender, EventArgs e)
        {
            string xexpath = invoker.XexFilePath;
            if (xexpath != "" || xexpath != " ")
            {
                invoker.InvokeXexTool(xexpath, "-c c", false);
            }
            else
                MessageBox.Show("This does not work with ISO you must extract it with image browser first..", "You need to open a XEX file first..");
        }

        private async void button5_Click(object sender, EventArgs e)
        {
            string xexpath = invoker.XexFilePath;
            if (xexpath != "" || xexpath != " ")
            {
                invoker.InvokeXexTool(xexpath, "-e u", false);
            }
            else
                MessageBox.Show("This does not work with ISO you must extract it with image browser first..", "You need to open a XEX file first..");
        }

        private async void button2_Click(object sender, EventArgs e)
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
                if (checkBox7.Checked == true && (checkBox4.Checked == true) || (checkBox8.Checked == true && (checkBox9.Checked == true)))
                {
                    MessageBox.Show("You cannot select add and remove encryption or compression at the same time!", "OooopsssSS");
                    return;
                }
                invoker.InvokeXexTool(xexpath, Arguments, false);
            }
            else
                MessageBox.Show("This does not work with ISO you must extract it with image browser first..", "You need to open a XEX file first..");
        }

        private void button10_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "-l", false);
        }

        private void button11_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "-x a", false);
        }

        private async void button7_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "-r a -m r", false);
        }

        private async void button8_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "-r a -m d", false);
        }

        private void button9_Click(object sender, EventArgs e)
        {

        }

        private async void groupBox1_DragDrop(object sender, DragEventArgs e)
        {
            pictureBox1.Show();
            string[] filePaths = (string[])e.Data.GetData(DataFormats.FileDrop);
            string firstFilePath = filePaths[0];
            textBox1.Text = $"{firstFilePath}";
            string fileName = new System.IO.FileInfo(textBox1.Text).Name; // get it from textbox1 
            double fileSizeMb = new System.IO.FileInfo(textBox1.Text).Length / 1024.0 / 1024.0;
            label81.Text = fileName;
            label83.Text = fileSizeMb.ToString("F2") + " MBs";
            string filePath = $"{firstFilePath}";
            string extension = Path.GetExtension($"{firstFilePath}");
            AntiCommandInjection APT = new AntiCommandInjection();
            if (filePath.Contains(".xex") || filePath.Contains(".exe") || filePath.Contains(".dll"))
            {
                APT.SanitizeInvokerFilePath(filePath);
                textBox1.Text = invoker.XexFilePath; //Send path to invoker class
                label1.Text = "Xbox 360 Executable";
                invoker.XexFilePath = filePath;
                groupBox4.Show();
                tabControl1.SelectedIndex = 0;
                XEXParcer XEXParcer = new XEXParcer();
                XEXParcer.GetXEXInfo(textBox1.Text);
                textBox15.Text = XEXParcer.XEXName;
                textBox46.Text = XEXParcer.XEXSystem;
                textBox44.Text = XEXParcer.Encrypted;
                textBox43.Text = XEXParcer.Compressed;
                textBox4.Text = XEXParcer.MediaId;
                textBox3.Text = XEXParcer.TitleId;
                textBox41.Text = XEXParcer.XEXVersion;
                textBox9.Text = XEXParcer.BaseVersion;
                textBox19.Text = XEXParcer.EntryPoint;
                textBox20.Text = XEXParcer.LoadAddress;
                GetIconFrombase64(XEXParcer.GameIconBase64);
                pictureBox1.Hide();
                return;
            }
            else if (filePath.Contains(".xbe"))
            {
                APT.SanitizeInvokerFilePath(filePath);
                textBox1.Text = invoker.XBEFilePath;
                label1.Text = "Original Xbox Executable";
                invoker.XBEFilePath = filePath;
                groupBox4.Show();
                tabControl1.SelectedIndex = 4;
                pictureBox1.Hide();
                return;
            }
            else if (filePath.Contains(".iso"))
            {
                APT.SanitizeInvokerFilePath(filePath);
                textBox1.Text = invoker.ISOFilePath;
                label1.Text = "OG/Xbox 360 ISO";
                invoker.ISOFilePath = filePath;
                groupBox4.Show();
                tabControl1.SelectedIndex = 2;
                pictureBox1.Hide();
                return;
            }
            else if (string.IsNullOrEmpty(extension))
            {
                APT.SanitizeInvokerFilePath(filePath);
                textBox1.Text = invoker.GodFilePath;
                label1.Text = "Xbox 360 PKG";
                invoker.GodFilePath = filePath;
                groupBox4.Show();
                tabControl1.SelectedIndex = 1;
                Xbox360PkgParser Xbox360PkgParser = new Xbox360PkgParser();
                Xbox360PkgParser.GetPkgInfo(textBox1.Text);
                textBox17.Text = Xbox360PkgParser.PkgType;
                textBox18.Text = Xbox360PkgParser.TitleName;
                textBox37.Text = Xbox360PkgParser.PkgVersion;
                textBox45.Text = Xbox360PkgParser.BaseVersion;
                textBox40.Text = Xbox360PkgParser.TitleId;
                textBox39.Text = Xbox360PkgParser.MediaId;
                textBox11.Text = Xbox360PkgParser.DisplayName;
                textBox12.Text = Xbox360PkgParser.Description;
                textBox13.Text = Xbox360PkgParser.Publisher;
                pictureBox1.Hide();
                return;
            }
            else if (filePath.Contains(".bin"))
            {
                label1.Text = "Xbox 360 Save Pack";
                APT.SanitizeInvokerFilePath(textBox1.Text);
                invoker.GodFilePath = textBox1.Text; //set invoker
                groupBox4.Show();
                tabControl1.SelectedIndex = 3;
                SaveParser SaveParcer = new SaveParser();
                SaveParcer.GetPkgInfo(textBox1.Text);
                textBox55.Text = SaveParcer.PkgType;
                textBox48.Text = SaveParcer.DisplayName;
                textBox54.Text = SaveParcer.TitleName;
                textBox53.Text = SaveParcer.PkgVersion;
                textBox49.Text = SaveParcer.BaseVersion;
                textBox51.Text = SaveParcer.TitleId;
                textBox50.Text = SaveParcer.MediaId;
                textBox38.Text = SaveParcer.InstallDir;
                textBox56.Text = SaveParcer.ProfileID;
                textBox57.Text = SaveParcer.ConsoleID;
                pictureBox1.Hide();
                return;
            }
            else
            {
                label1.Text = "???";
                groupBox4.Show();
                MessageBox.Show("You have attempted to open a file with an unknown to X360GameHack extention... \nX360GameHack will not use it nor set the invoker for it...\nSupported files are: \nISO\nXEX\nXBE\nand No Extention for 360 PKGs!", "X360GameHack Info!");
                return;
            }

        }

        private async void groupBox1_DragEnter(object sender, DragEventArgs e)
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

        private async void button12_Click(object sender, EventArgs e)
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

        }

        private void checkBox15_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void checkBox21_CheckedChanged(object sender, EventArgs e)
        {

        }

        private async void button15_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "-l", false);
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
        }

        private void checkBox24_CheckedChanged(object sender, EventArgs e)
        {

        }

        private async void button14_Click(object sender, EventArgs e)
        {
            XEXIDChanger XEXIDChanger = new XEXIDChanger();
            XEXParcer XEXParcer = new XEXParcer();
            XEXParcer.GetXEXInfo(textBox1.Text);
            XEXIDChanger.PatchXEXTitleID(textBox1.Text, XEXParcer.TitleId, textBox3.Text);
        }

        private void buttonAdd_Click(object sender, EventArgs e)
        {

        }

        private void buttonRemove_Click(object sender, EventArgs e)
        {

        }



        private void buttonClear_Click(object sender, EventArgs e)
        {

        }

        private void buttonBrowse_Click(object sender, EventArgs e)
        {

        }

        private void buttonGo_Click(object sender, EventArgs e)
        {

        }
        private void backgroundWorker1_DoWork_1(object sender, DoWorkEventArgs e)
        {

        }

        private void button19_Click(object sender, EventArgs e)
        {
            RegisterMSCOMCTLOCX.OpenXIB(true);
        }

        private void button20_Click(object sender, EventArgs e)
        {
            RegisterMSCOMCTLOCX.OpenXIB(false);
        }

        private async void button10_Click_2(object sender, EventArgs e)
        {
            /* if (checkBox29.Checked) // exclude sys update 
             {
                 XISOEE.ExtractSystemUpdate = false;
             }
             else if (!checkBox29.Checked) //dont exclude sys update
             {
                 XISOEE.ExtractSystemUpdate = true;
             }
             XISOEE.DefaultExtactionLocation = false;
             XISOEE.ExtractorPath = textBox42.Text;*/
            try
            {
                // bool success = await XISOEE.ExtractISOAsync(textBox1.Text);
                // if (!success)
                //{
                //     X360GameHack.CurrentInstance.UpdateListboxForOutput("Extraction failed.");
                // }
                // else if (success)
                //  {
                X360GameHack.CurrentInstance.UpdateListboxForOutput("Extraction completed successfully.");
                // }
            }
            catch (Exception ex)
            {
                X360GameHack.CurrentInstance.UpdateListboxForOutput("Extraction failed. Exception:" + ex.Message);
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
            Text = "X360GameHack 2025 " + Pastebin.CurrentVersion + " (Waiting on XISO...)";
            /* if (checkBox29.Checked)
             {
                 invoker.InvokeXISO(invoker.ISOFilePath, "-x -s");
             }
             else
             {
                 invoker.InvokeXISO(invoker.ISOFilePath, "-x");
             }*/

            string filePath = invoker.ISOFilePath;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string jtagriplocation = Application.StartupPath + "//" + fileNameWithoutExtension;
            //while path doesn't contain default.xex or default_mp.xex or exist
            while (!Directory.Exists(jtagriplocation) || ContainsDefaultXex(jtagriplocation) == false)
            {
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
            this.Text = "X360GameHack 2025 " + Pastebin.CurrentVersion;
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
            Text = "X360GameHack 2025  " + Pastebin.CurrentVersion + " (Waiting on XISO...)";
            /*if (checkBox29.Checked)
            {
                invoker.InvokeXISO(invoker.ISOFilePath, "-x -s");
            }
            else
            {
                invoker.InvokeXISO(invoker.ISOFilePath, "-x");
            }*/

            string filePath = invoker.ISOFilePath;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string jtagriplocation = Application.StartupPath + "//" + fileNameWithoutExtension;
            while (!Directory.Exists(jtagriplocation) || ContainsDefaultXex(jtagriplocation) == false)
            {
                Thread.Sleep(5000);
            }
            this.Text = "X360GameHack 2025 " + Pastebin.CurrentVersion + " - IcyModz420 (Patching XEX...)";
            string[] xexFiles = Directory.GetFiles(jtagriplocation, "*.xex");
            string XexNames = "";
            foreach (string file in xexFiles)
            {
                invoker.XexFilePath = file; //send path to generate batch
                invoker.InvokeXexTool(file, "-u -r a -m d -c u -e u", false);
                invoker.XexFilePath = ""; //reset jic
                XexNames = XexNames + file + Environment.NewLine;
            }
            this.Text = "X360GameHack 2025  " + Pastebin.CurrentVersion;
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

        private void tabPage6_Click(object sender, EventArgs e)
        {

        }

        private async void button16_Click(object sender, EventArgs e)
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
            QuickFTPGameSender gs = new QuickFTPGameSender();
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
            invoker.InvokeGodTool(invoker.GodFilePath, "-p");
        }

        private void button46_Click(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, "-st " + textBox18.Text);
            //Thread.Sleep(300);
            Xbox360PkgParser Xbox360PkgParser = new Xbox360PkgParser();
            Xbox360PkgParser.GetPkgInfo(textBox1.Text);
            textBox18.Text = Xbox360PkgParser.TitleName;
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

        private async void button25_Click(object sender, EventArgs e)
        {
            button25.Enabled = false;
            await GeminiClient.AskGemini("what is a xbox 360 xex file?", true);
            button25.Enabled = true;
            MessageBox.Show(GeminiClient.LatestResponce, "Responce Automated by Google's Gemini AI!");
        }

        private async void button26_Click(object sender, EventArgs e)
        {
            button26.Enabled = false;
            await GeminiClient.AskGemini("what do i need to patch my xex files with -u -r a -m r -c e -e u with xextool? Also why do you need to stealth patch xiso before burning them?", true);
            button26.Enabled = true;
            MessageBox.Show(GeminiClient.LatestResponce, "Responce Automated by Google's Gemini AI!");
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
            MessageBox.Show("You can use the option under xex patches title update to patch the .xexp file from the extacted tu into an unpached default.xex that comes with your extracted iso. \n But you will still need to copy and click yes on replacing any files from the extracted tu when manually copying them into your game rip. \nYou will also need horizon to actually extract the tu at this time as its copyrighted and can't be added with this tool. Yet.......... you can get the free version from wemod.com to actually extract the tu. \n\nTo download the correct region TU for your game you will need to open the default.xex from the extracted ISO and list info or grab media id and then search that media id on xboxunity.com this is a place where most all tus are best archived.", "X360GameHack Info!");
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
            MessageBox.Show("XEX and PKG Patches By: Xorloser \n XISO By: XboxDev Organization \n GOD2ISO By: Raburton \n ISO2GOD By: Others and updated by R4dius \n X360GameHack Interface By: IcyModz420", "X360GameHack Credits!");
        }

        private async void button35_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select Location to put XEX file";
                //dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        System.IO.File.Copy(dialog.SelectedPath + "\\" + textBox1.Text, dialog.SelectedPath + "\\" + textBox1.Text + ".bak", true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error: " + ex.Message, "X360GameHack Error!");
                    }
                }
            }
        }

        private async void button31_Click(object sender, EventArgs e)
        {
            button31.Enabled = false;
            await GeminiClient.AskGemini("what is a xbox 360 stfs pkg file like con, pirs, and live signed stfs 360 pkg files?", true);
            button31.Enabled = true;
            MessageBox.Show(GeminiClient.LatestResponce, "Responce Automated by Google's Gemini AI!");
        }

        private void button32_Click(object sender, EventArgs e)
        {
            MessageBox.Show("No cleaning the actual XISO with abgx360 is only needed to stealth patch the XISO file itself before burning it to prevent bans and detection using it on a flashed console it doesn't effect the actual game files in most cases.", "X360GameHack Info!");
        }

        private void button34_Click(object sender, EventArgs e)
        {
            MessageBox.Show("If you have an RGH hacked system then no you do not need the system update files and it saves space on the console by deleing them. Its only included on a iso so they could force a system update if needed before you could play it from offline.");
        }

        private void button33_Click(object sender, EventArgs e)
        {
            MessageBox.Show("MSCOMCTL.OCX is a dependency of xbox image tool and xbox backup creator you must register it with regsvr before first time use to use either of these old no longer officially supported programs.");
        }

        private void button56_Click(object sender, EventArgs e)
        {
            string ip = textBox7.Text;
            webBrowser1.Url = new Uri(ip);
        }

        private void X360GameHack_FormClosing(object sender, FormClosingEventArgs e)
        {
            Application.Exit();
        }

        private void button67_Click(object sender, EventArgs e)
        {
            MessageBox.Show("X360GameHack GUI was made by one person with a great knowledge of the xbox 360 and its inner workings, reverse enginering xbox 360 games as well as dotnet applications, and the pre existing software used to mainsteam undoing its protections on XISO. I have been in the modding communitys on facebook and youtube making mod menus, software, and tools for over 10 years and intend to develop this tool slowly until its complete.\n When the GUI is complete it will have every option in one GUI/User interface needed for an xbox 360 XISO, PKG, and XEX as well as original xbox XISO and XBE.\n\n I was on se7ensins as IcyModz420 but they banned my account years ago for no justifyable reason so now I am only in facebook groups. (The admins are haters? :'O) \n\n Consider buying me a monster energy drink for some of the countless late nights I've spent on it putting this all together in to one easy to use tool with cashapp if possible $Collin3400 your donations are appreciated and contribute to the further development of this tool.", "About x360GameHack GUI Ceator:");
        }

        private void button68_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/IcyModz420/X360GameHack2025/issues");
        }

        private void button69_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "-r a -m r", false);
        }

        private void button70_Click(object sender, EventArgs e)
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
            Text = "X360GameHack 2025 " + Pastebin.CurrentVersion + " (Waiting on XISO...)";
            /* if (checkBox29.Checked)
             {
                 invoker.InvokeXISO(invoker.ISOFilePath, "-x -s");
             }
             else
             {
                 invoker.InvokeXISO(invoker.ISOFilePath, "-x");
             }*/

            string filePath = invoker.ISOFilePath;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string jtagriplocation = Application.StartupPath + "//" + fileNameWithoutExtension;
            while (!Directory.Exists(jtagriplocation) || ContainsDefaultXex(jtagriplocation) == false)
            {
                Thread.Sleep(5000);
            }

            this.Text = "X360GameHack 2025 " + Pastebin.CurrentVersion + " - IcyModz420 (Patching XEX...)";
            string[] xexFiles = Directory.GetFiles(jtagriplocation, "*.xex");
            string XexNames = "";
            foreach (string file in xexFiles)
            {
                invoker.XexFilePath = file; //send path to generate batch
                invoker.InvokeXexTool(file, "-r a -m r", false);
                invoker.XexFilePath = ""; //reset jic
                XexNames = XexNames + file + Environment.NewLine;
            }
            this.Text = "X360GameHack 2025" + Pastebin.CurrentVersion;
        }

        private void button39_Click(object sender, EventArgs e)
        {
            MessageBox.Show("You will need an ftp server running from within a dashboard like evox, avelanche, unleashx etc.. \n\n Then you will see your consoles IP address somewhere near there in the dashboard. \n\n use that ip and port then use username and password xbox if not already set.");
        }

        private void button38_Click(object sender, EventArgs e)
        {
            MessageBox.Show("You will need an FTP server running on your console to connect over ftp then you will nee ip address and correst open port along with username and password. \n To do this just open FSD, Xexmenu, Aroura, on 360 or open EvoX,XBMCForGamers, etc and go to settings to start the server. \n Then use the IP, port, and default user and pass to connect with other settings left default. \n\n When you save your connect info in the FTP2Xbox tab it is saved to the application then it can be loaded at startup and other options such as checkboxes etc can be used. If you update the app it needs to be resaved to the application via the save button.");
        }

        private void button50_Click(object sender, EventArgs e)
        {
            MessageBox.Show("You will need n FTP server running to connect. \n To do this make sure goldhen is running and then go in its settings menu and start ftp and connect using the IP address and port it gives you.");
        }

        private void button57_Click(object sender, EventArgs e)
        {

            invoker.InvokeABGX(invoker.ISOFilePath, "abgx360 -pct --af3 --rgn 00FFFFFF --splitvid --max --pause --");
        }

        private void button66_Click(object sender, EventArgs e)
        {

        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            AntiNoob.DoAntiDebugFunc();
        }

        private void groupBox18_Enter(object sender, EventArgs e)
        {

        }

        private void groupBox13_Enter(object sender, EventArgs e)
        {

        }

        private void textBox14_TextChanged(object sender, EventArgs e)
        {

        }

        private void button82_Click(object sender, EventArgs e)
        {
            if (numericUpDown1.Value > 1480)
            {
                MessageBox.Show("You cannot set a value greater than the DreamX CPU...", "X360GameHack Info!");
                return;
            }
            XBEPatches.PatchXBECPUScale(invoker.XBEFilePath, (int)numericUpDown1.Value);
        }

        private void tabPage10_Click(object sender, EventArgs e)
        {

        }

        private void button76_Click(object sender, EventArgs e)
        {
            XBEPatches.PatchXBERam(invoker.XBEFilePath, "Stock");
        }

        private void button77_Click(object sender, EventArgs e)
        {
            XBEPatches.PatchXBERam(invoker.XBEFilePath, "128");
        }

        private void button79_Click(object sender, EventArgs e)
        {
            XBEPatches.PatchXBECPUScale(invoker.XBEFilePath, 733);
        }

        private void button78_Click(object sender, EventArgs e)
        {
            XBEPatches.PatchXBECPUScale(invoker.XBEFilePath, 1000);
        }

        private void button80_Click(object sender, EventArgs e)
        {
            XBEPatches.PatchXBECPUScale(invoker.XBEFilePath, 1400);
        }

        private void button81_Click(object sender, EventArgs e)
        {
            XBEPatches.PatchXBECPUScale(invoker.XBEFilePath, 1480);
        }

        private void button86_Click(object sender, EventArgs e)
        {

        }

        private void button84_Click(object sender, EventArgs e)
        {
            string folderpath = "";
            using (var folderBrowserDialog = new FolderBrowserDialog())
            {
                folderBrowserDialog.Description = "Select folder you want to make an XISO:";
                DialogResult result = folderBrowserDialog.ShowDialog();
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
                {
                    folderpath = folderBrowserDialog.SelectedPath;
                }

            }
            if (!folderpath.Contains(@"")) // contains exactly nothing
            {
                invoker.InvokeXISO(invoker.ISOFilePath, "-c " + folderpath);
            }
        }

        private void button83_Click(object sender, EventArgs e)
        {
            string filepath = "";
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Select an ISO or XISO File";
                openFileDialog.Filter = "ISO/XISO Files (*.iso;*.xiso)|*.iso;*.xiso|All Files (*.*)|*.*";
                openFileDialog.FilterIndex = 1; // Default to ISO/XISO filter
                openFileDialog.Multiselect = false; // Only allow single file selection
                DialogResult result = openFileDialog.ShowDialog();
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(openFileDialog.FileName))
                {
                    filepath = openFileDialog.FileName;
                }
            }
            if (!filepath.Contains(@"")) // contains exactly nothing
            {
                invoker.InvokeXISO(invoker.ISOFilePath, "-r " + filepath);
            }
        }

        private void button95_Click(object sender, EventArgs e)
        {

        }

        private void button86_Click_1(object sender, EventArgs e)
        {

        }

        private void tabPage13_Click(object sender, EventArgs e)
        {

        }

        private void button65_Click(object sender, EventArgs e)
        {

        }

        private void button64_Click(object sender, EventArgs e)
        {

        }

        private void button87_Click(object sender, EventArgs e)
        {
            string sha = Sha3_256.ComputeSha3_256(Path.Combine(Application.StartupPath, "XBBC.exe"));
            MessageBox.Show(Path.Combine(Application.StartupPath, "XBBC.exe") + "\n\n" + sha);

            Clipboard.SetText(sha);
        }

        private void groupBox25_Enter(object sender, EventArgs e)
        {

        }

        private void button87_Click_1(object sender, EventArgs e)
        {
            Clipboard.SetText(textBox16.Text);
        }

        private void button96_Click(object sender, EventArgs e)
        {
            MessageBox.Show("This software is provided \"as is\" for free without warranty of any kind expressed or implyed. Use it at your own risk and ensure compliance with local laws regarding console modding. The developer does not claim ownership of third-party tools included in the package.\nX360GameHack is a free, independent, open source, software project and is not affiliated with, endorsed by, or sponsored by Microsoft Corporation or any other entity. All trademarks, copyrighted materials, intellectual property, including but not limited to Xbox 360 game files, and encryption keys are the property of their respective owners. This tool is intended for lawful use only, such as extracting files from legally owned game ISOs for personal backup or archival purposes, in compliance with applicable copyright and intellectual property laws. \n By using this software users understand that it is illegal in the US, UK, Canada, and alike to download, extract, backup, play, and/or have a copy of a XiSO you do not physically or digitally (with limitations) own and that the creator of this software cannot be held responsible for the unintended use of this software.", "X360GameHack Legal Disclaimer!");
        }

        private void button58_Click(object sender, EventArgs e)
        {
            invoker.InvokeABGX(invoker.ISOFilePath, "abgx360 -pct --af3 --rgn 00FFFFFF --splitvid --max --pause --");
        }

        private void tabPage8_Click(object sender, EventArgs e)
        {

        }

        private void GetIconFrombase64(string base64Data)
        {
            if (string.IsNullOrWhiteSpace(base64Data))
                return;

            try
            {
                // Remove any leftover newlines/spaces/tabs from the base64 chunk
                string cleaned = Regex.Replace(base64Data, @"\s+", "");
                byte[] imageBytes = Convert.FromBase64String(cleaned);

                using (var ms = new MemoryStream(imageBytes))
                using (var temp = Image.FromStream(ms))
                {
                    // Clone into a new Bitmap so the image doesn't stay tied to the
                    // MemoryStream (GDI+ will throw later if that stream gets disposed/GC'd)
                    var img = new Bitmap(temp);

                    pictureBox2.Image?.Dispose(); // avoid leaking the previous image
                    pictureBox2.Image = img;
                    pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
                }
            }
            catch (FormatException)
            {
                // base64 string was malformed/incomplete
            }
            catch (Exception)
            {
                // not a valid image (corrupt data, wrong format, etc.)
            }
        }

        private void button73_Click(object sender, EventArgs e)
        {
            pictureBox1.Show();
            string filePath = textBox1.Text;
            string fileName = new System.IO.FileInfo(filePath).Name;
            double fileSizeMb = new System.IO.FileInfo(filePath).Length / 1024.0 / 1024.0;
            label81.Text = fileName;
            label83.Text = fileSizeMb.ToString("F2") + " MBs";
            string extension = Path.GetExtension(filePath);
            AntiCommandInjection APT = new AntiCommandInjection();
            if (filePath.Contains(".xex") || filePath.Contains(".exe") || filePath.Contains(".dll"))
            {
                label1.Text = "Xbox 360 Executable";
                APT.SanitizeInvokerFilePath(textBox1.Text);
                invoker.XexFilePath = textBox1.Text; //set invoker
                groupBox4.Show();
                tabControl1.SelectedIndex = 0;
                XEXParcer XEXParcer = new XEXParcer();
                XEXParcer.GetXEXInfo(textBox1.Text);
                textBox15.Text = XEXParcer.XEXName;
                textBox46.Text = XEXParcer.XEXSystem;
                textBox44.Text = XEXParcer.Encrypted;
                textBox43.Text = XEXParcer.Compressed;
                textBox4.Text = XEXParcer.MediaId;
                textBox3.Text = XEXParcer.TitleId;
                textBox41.Text = XEXParcer.XEXVersion;
                textBox9.Text = XEXParcer.BaseVersion;
                textBox19.Text = XEXParcer.EntryPoint;
                textBox20.Text = XEXParcer.LoadAddress;
                GetIconFrombase64(XEXParcer.GameIconBase64);
                pictureBox1.Hide();
                return;
            }
            else if (filePath.Contains(".iso"))
            {
                label1.Text = "OG/Xbox 360 ISO";
                APT.SanitizeInvokerFilePath(textBox1.Text);
                invoker.ISOFilePath = textBox1.Text; //set invoker
                groupBox4.Show();
                tabControl1.SelectedIndex = 2;
                pictureBox1.Hide();
                return;
            }
            else if (filePath.Contains(".xbe"))
            {
                label1.Text = "Original Xbox XBE";
                APT.SanitizeInvokerFilePath(textBox1.Text);
                invoker.XBEFilePath = textBox1.Text; //set invoker
                groupBox4.Show();
                tabControl1.SelectedIndex = 4;
                pictureBox1.Hide();
                return;
            }
            else if (string.IsNullOrEmpty(extension))
            {
                label1.Text = "Xbox 360 STFS Pack";
                APT.SanitizeInvokerFilePath(textBox1.Text);
                invoker.GodFilePath = textBox1.Text; //set invoker
                groupBox4.Show();
                tabControl1.SelectedIndex = 1;
                Xbox360PkgParser Xbox360PkgParser = new Xbox360PkgParser();
                Xbox360PkgParser.GetPkgInfo(textBox1.Text);
                textBox17.Text = Xbox360PkgParser.PkgType;
                textBox18.Text = Xbox360PkgParser.TitleName;
                textBox37.Text = Xbox360PkgParser.PkgVersion;
                textBox45.Text = Xbox360PkgParser.BaseVersion;
                textBox40.Text = Xbox360PkgParser.TitleId;
                textBox39.Text = Xbox360PkgParser.MediaId;
                textBox11.Text = Xbox360PkgParser.DisplayName;
                textBox12.Text = Xbox360PkgParser.Description;
                textBox13.Text = Xbox360PkgParser.Publisher;
                pictureBox1.Hide();
                return;
            }
            else if (filePath.Contains(".bin"))
            {
                label1.Text = "Xbox 360 Save Pack";
                APT.SanitizeInvokerFilePath(textBox1.Text);
                invoker.GodFilePath = textBox1.Text; //set invoker
                groupBox4.Show();
                tabControl1.SelectedIndex = 3;
                SaveParser SaveParcer = new SaveParser();
                SaveParcer.GetPkgInfo(textBox1.Text);
                textBox55.Text = SaveParcer.PkgType;
                textBox48.Text = SaveParcer.DisplayName;
                textBox54.Text = SaveParcer.TitleName;
                textBox53.Text = SaveParcer.PkgVersion;
                textBox49.Text = SaveParcer.BaseVersion;
                textBox51.Text = SaveParcer.TitleId;
                textBox50.Text = SaveParcer.MediaId;
                textBox38.Text = SaveParcer.InstallDir;
                textBox56.Text = SaveParcer.ProfileID;
                textBox57.Text = SaveParcer.ConsoleID;
                pictureBox1.Hide();
                return;
            }
            else
            {
                label1.Text = "???";
                groupBox4.Show();
                MessageBox.Show("You have attempted to set a file with an unknown to X360GameHack extention... \nX360GameHack will not use it nor set the invoker for it...\nSupported files are \nISO\nXEX\nXBE\nand No Extention for 360 PKGs!", "X360GameHack Info!");
            }
        }

        private void checkBox30_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox30.Checked)
            {
                invoker.CaptureOutput = true;
                invoker.GenerateBatch = false;
            }
            else if (!checkBox30.Checked)
            {
                invoker.CaptureOutput = false;
                invoker.GenerateBatch = false;
            }
        }

        private void checkBox31_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox31.Checked)
            {
                invoker.CaptureOutput = false;
                invoker.GenerateBatch = true;
            }
            else if (!checkBox31.Checked)
            {
                invoker.CaptureOutput = false;
                invoker.GenerateBatch = false;
            }
        }

        private async void button74_Click(object sender, EventArgs e)
        {

        }

        private async void button89_Click(object sender, EventArgs e)
        {
            button89.Enabled = false;
            await GeminiClient.AskGemini("what is a xbox 360 xbe file?", true);
            button89.Enabled = true;
            MessageBox.Show(GeminiClient.LatestResponce, "Responce Automated by Google's Gemini AI!");
        }

        private async void button92_Click(object sender, EventArgs e)
        {
            button25.Enabled = false;
            await GeminiClient.AskGemini("explain how the J-TAG/R-JTAG/S-RGH/RGH1/RGH1.2/RGH2/RGH3 Exploits all achieve the same goal of glitching past an instruction in the firmware to achieve unsigned code execution allowing homebrew", true);
            button25.Enabled = true;
            MessageBox.Show(GeminiClient.LatestResponce, "Responce Automated by Google's Gemini AI!");
        }

        private async void button90_Click(object sender, EventArgs e)
        {
            button90.Enabled = false;
            await GeminiClient.AskGemini("what is a xbox 360 XISO file and whats in it?", true);
            button90.Enabled = true;
            MessageBox.Show(GeminiClient.LatestResponce, "Responce Automated by Google's Gemini AI!");
        }

        private void button39_Click_1(object sender, EventArgs e)
        {
            MessageBox.Show("As it appears from the reverse engineered c# code from the original tool to first impliment these patches... \n\n There is a static location in XBE files which stores the ram and cpu speed so if we use this location and the correct machine code for the value we can change it via binary machine code to whatever we want.. and as long as your bios and setup supports it. \nIf it is not supported you may get black screens.\n Do not touch any ram or cpu settings if you don't have an upgraded system or know what you are doing. ", "X360GameHack Info!");
        }

        private void button50_Click_1(object sender, EventArgs e)
        {
            MessageBox.Show("As it appears from the reverse engineered c# code from the original tool to first impliment these patches... \n\n There is a static location in XBE files which stores the ram and cpu speed so if we use this location and the correct machine code for the value we can change it via binary machine code to whatever we want.. and as long as your bios and setup supports it. \nIf it is not supported you may get black screens.\n Do not touch any ram or cpu settings if you don't have an upgraded system or know what you are doing. ", "X360GameHack Info!");
        }

        private void button91_Click(object sender, EventArgs e)
        {
            MessageBox.Show("If you have purchased it on your rgh the god/stfs file still needs to be unlocked because when you change your kv your console id the file was assigned to changes and the console would possibly still run the unsigned file.. so I would say yes if you think it has purchase traces remove them. It can help you not get banned if going online.", "X360GameHack Info!");
        }

        private void button85_Click(object sender, EventArgs e)
        {
            MessageBox.Show("This will make the iso run better and faster on the original xbox console.", "X360GameHack Info!");
        }

        private void button109_Click(object sender, EventArgs e)
        {

        }

        private void button86_Click_2(object sender, EventArgs e)
        {
            ConsoleX.Connect("192.168.137.109"); //ConsoleX.
            ConsoleX.Notify("X360GameHack GUI Connected!");
            Console.WriteLine("hello!");
            // MessageBox.Show(ConsoleX.GetConsoleType);
            //ConsoleX.FanSpeed(1, 100);

            //ConsoleX.Notify("XDCKIT online");

        }


        private void button110_Click(object sender, EventArgs e)
        {

        }

        private void button111_Click(object sender, EventArgs e)
        {
            try
            {
                xdk.Connect(textBox14.Text);
                MessageBox.Show("Connected!");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not connect to console! \nException: " + ex, "X360GameHack Info!");
            }
        }

        private void button112_Click(object sender, EventArgs e)
        {
            MessageBox.Show("This will remove all limits, and patch it to be retail to play on an RGH/JTAG \n(-r a -m r)", "X360GameHack Info!");
        }

        private void button113_Click(object sender, EventArgs e)
        {
            MessageBox.Show("This will remove all limits, remove compression and encryption, and patch it to be an XDK XEX so it can play on XDK. \n(-r a -m d -c u -e u)", "X360GameHack Info!");
        }

        private void button114_Click(object sender, EventArgs e)
        {
            MessageBox.Show("This will remove all xex limits and patch the xex image to be retail.", "X360GameHack Info!");
        }

        private void button118_Click(object sender, EventArgs e)
        {

        }

        private void tabPage3_Click(object sender, EventArgs e)
        {

        }

        private void comboBox5_DropDown(object sender, EventArgs e)
        {
            USBDrives USBDrives = new USBDrives();
            USBDrives.DetectFat32USB(comboBox5);
        }



        public void SetStorageComboBox(string Device)
        {
            comboBox5.Items.Add(Device);
        }

        public void ClearStorageComboBox()
        {
            comboBox5.Items.Clear();
        }

        private void tabPage24_Click(object sender, EventArgs e)
        {

        }

        private void button95_Click_1(object sender, EventArgs e)
        {
            Settings.Default.SelectedUSBLetter = "";
            Settings.Default.Save();
            if (comboBox5.SelectedItem != null || comboBox5.SelectedItem != "" || comboBox5.SelectedItem != " ") // if xbdm selected
            {


                if (radioButton4.Checked == true && comboBox5.SelectedItem != null) // if usb storage selected
                {
                    Settings.Default.SelectedUSBLetter = comboBox5.SelectedItem.ToString();
                    X360GameHack.CurrentInstance.UpdateListboxForOutput("X360GameHack: Current USB storage device letter is set to " + Settings.Default.SelectedUSBLetter);
                }
                // do xbdm here but catch error 
                else // fail
                {

                }
            }
            else
            {
                MessageBox.Show("You didn't select a usb drive from the combobox dropdownso it didn't work. Please select a usb drive and try again.", "X360GameHack Info!");
            }
        }

        private void button100_Click(object sender, EventArgs e)
        {
            USBDrives USBDrives = new USBDrives();
            USBDrives.DetectFat32USB(comboBox5);
        }

        private void button97_Click(object sender, EventArgs e)
        {
            STFSInstaller STFSInstaler = new STFSInstaller();
            foreach (string item in listBox7.Items)
            {
                STFSInstaller.InstallSTFSUSB(item);
            }
        }

        private void button104_Click(object sender, EventArgs e)
        {

        }

        private void listBox6_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void groupBox51_Enter(object sender, EventArgs e)
        {

        }


        private void button107_Click(object sender, EventArgs e)
        {
            uint offset = 0x0000000;
            uint length = 0x1000u;
            // byte[] mem = xdk.GetMemory(offset, 10u).ToString();
            //xdk.PeekXbox

        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {

        }

        private void listBox7_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (string path in files)
            {
                listBox7.Items.Add(path);
            }
        }

        private void listBox7_DragEnter(object sender, DragEventArgs e)
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

        private void button117_Click(object sender, EventArgs e)
        {

        }

        private void button131_Click(object sender, EventArgs e)
        {
            Xbox360PkgParser Xbox360PkgParser = new Xbox360PkgParser();
            Xbox360PkgParser.GetPkgInfo(textBox1.Text);
            textBox17.Text = Xbox360PkgParser.PkgType;
            textBox11.Text = Xbox360PkgParser.DisplayName;
            textBox12.Text = Xbox360PkgParser.Description;
            textBox13.Text = Xbox360PkgParser.Publisher;
            textBox18.Text = Xbox360PkgParser.TitleName;
            textBox37.Text = Xbox360PkgParser.PkgVersion;
            textBox45.Text = Xbox360PkgParser.BaseVersion;
            textBox40.Text = Xbox360PkgParser.TitleId;
            textBox39.Text = Xbox360PkgParser.MediaId;
            textBox10.Text = Xbox360PkgParser.InstallDir;
        }

        private void button115_Click(object sender, EventArgs e)
        {
            MessageBox.Show("This will remove the title update requirement however it will make the xex file unable to ever be updated with a default.xexp even one from a tu file it will crash if you ever use a tu so make sure you keep a backup if you care about updating your tu. \n You can go to xbox 360 default dash to find tu files on your storage device and delete them before you try to launch it with this patch.", "X360GameHack Info!");
        }

        private void button126_Click(object sender, EventArgs e)
        {

        }

        private async void button134_Click(object sender, EventArgs e)
        {
            XEXParcer XEXParcer = new XEXParcer();
            XEXParcer.GetXEXInfo(textBox1.Text);
            textBox15.Text = XEXParcer.XEXName;
            textBox46.Text = XEXParcer.XEXSystem;
            textBox44.Text = XEXParcer.Encrypted;
            textBox43.Text = XEXParcer.Compressed;
            textBox4.Text = XEXParcer.MediaId;
            textBox3.Text = XEXParcer.TitleId;
            textBox41.Text = XEXParcer.XEXVersion;
            textBox9.Text = XEXParcer.BaseVersion;
            textBox19.Text = XEXParcer.EntryPoint;
            textBox20.Text = XEXParcer.LoadAddress;
        }

        private async void button116_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "-u -r a -m r", false);
        }

        private void button127_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "-u -r a -m d", false);
        }

        private void button137_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, textBox52.Text, false);
        }

        private void button88_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Executable files (*.exe)|*.exe|DLL files (*.dll)|*.dll|All files (*.*)|*.*";
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string filePath = openFileDialog.FileName;
                string sha3_256 = Sha3_256.ComputeSha3_256(filePath);
                textBox16.Text = sha3_256;
                Clipboard.SetText(sha3_256);
            }
        }

        private void button62_Click(object sender, EventArgs e)
        {
            XEXIDChanger XEXIDChanger = new XEXIDChanger();
            XEXParcer XEXParcer = new XEXParcer();
            XEXParcer.GetXEXInfo(textBox1.Text);
            XEXIDChanger.PatchXEXTitleID(textBox1.Text, XEXParcer.MediaId, textBox4.Text);
        }

        private void button132_Click(object sender, EventArgs e)
        {
            listBox2.Items.Clear();
        }

        private void button133_Click(object sender, EventArgs e)
        {
            // Define your file name in the current working directory
            string filePath = Path.Combine(Environment.CurrentDirectory, "logs.txt");

            // The 'true' argument tells StreamWriter to append to the file instead of overwriting
            using (StreamWriter sw = new StreamWriter(filePath, true))
            {
                foreach (var item in listBox2.Items)
                {
                    // Convert each item to a string and write it as a new line
                    sw.WriteLine(item.ToString());
                }
            }
        }

        private void button63_Click(object sender, EventArgs e)
        {
            invoker.InvokeXexTool(invoker.XexFilePath, "-u -r a -m r", false);
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void button108_Click(object sender, EventArgs e)
        {
            Process.Start("https://www.youtube.com/@IcyModz420");
        }

        private void button11_Click_2(object sender, EventArgs e)
        {
            SaveParser SaveParcer = new SaveParser();
            SaveParcer.GetPkgInfo(textBox1.Text);
            textBox55.Text = SaveParcer.PkgType;
            textBox48.Text = SaveParcer.DisplayName;
            textBox54.Text = SaveParcer.TitleName;
            textBox53.Text = SaveParcer.PkgVersion;
            textBox49.Text = SaveParcer.BaseVersion;
            textBox51.Text = SaveParcer.TitleId;
            textBox50.Text = SaveParcer.MediaId;
            textBox38.Text = SaveParcer.InstallDir;
            textBox56.Text = SaveParcer.ProfileID;
            textBox57.Text = SaveParcer.ConsoleID;
        }

        private void button136_Click(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, "-m 0");
        }

        private void button135_Click(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, "-m 1");
        }

        private void button17_Click_1(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderBrowser = new FolderBrowserDialog())
            {
                folderBrowser.Description = "Select the target directory";
                // folderBrowser.UseDescriptionForTitle = true; // Shows text as title
                folderBrowser.SelectedPath = @"C:\"; // Initial directory
                DialogResult result = folderBrowser.ShowDialog();
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(folderBrowser.SelectedPath))
                {
                    string folderPath = folderBrowser.SelectedPath;
                    // textBox42.Text = folderPath;
                }
            }
        }

        private void checkBox33_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox33.Checked)
            {
                Properties.Settings.Default.HasBeenAskedToUpdate = false;
                Properties.Settings.Default.Save();
            }
            else if (!checkBox33.Checked)
            {
                Properties.Settings.Default.HasBeenAskedToUpdate = true;
                Properties.Settings.Default.Save();
            }
        }

        private void button93_Click(object sender, EventArgs e)
        {

        }

        private void button72_Click(object sender, EventArgs e)
        {
            MessageBox.Show("You do not have to select any patches to use this button.", "X360GameHack Info!");
        }

        private void button74_Click_1(object sender, EventArgs e)
        {
            string xexpath = invoker.XexFilePath;
            if (xexpath != "" || xexpath != " ")
            {
                invoker.InvokeXexTool(xexpath, "-e e -c c", false);
            }
            else
            {
                MessageBox.Show("This does not work with ISO you must extract it first..", "You need to open a XEX file first..");
            }
        }

        private void button143_Click(object sender, EventArgs e)
        {
            string xexpath = invoker.XexFilePath;
            if (xexpath != "" || xexpath != " ")
            {
                invoker.InvokeXexTool(xexpath, "-e u -c u", false);
            }
            else
            {
                MessageBox.Show("This does not work with ISO you must extract it first..", "You need to open a XEX file first..");
            }
        }

        private void button64_Click_1(object sender, EventArgs e)
        {
            MessageBox.Show("This will make a devkit prototype xex file into a retail xex file so it can be played on rgh consoles, it will also remove title update requirement sense its a prototype (-u -r a -m r)", "X360GameHack Info!");
        }

        private void button140_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.XexToolCustomCommand = textBox52.Text;
            Properties.Settings.Default.Save();

        }

        private void button66_Click_1(object sender, EventArgs e)
        {
            textBox52.Text = Properties.Settings.Default.XexToolCustomCommand;
        }

        private void button114_Click_1(object sender, EventArgs e)
        {
            invoker.InvokeGodTool(invoker.GodFilePath, textBox42.Text);
        }

        private async void button146_Click(object sender, EventArgs e)
        {
            pictureBox1.Show();
            // Extract all ISOs in the listbox10 using the XISOExtractorExtention class
            if (textBox8.Text != "DriveLetter:/Games")
            //begin extraction
            { // check if the user has set a custom extraction path which is required to proceed
                button146.Enabled = false;
                ProcessHelper PH = new ProcessHelper();
                PH.KillAllProcessesByName("extract-xiso"); // kill all extract-xiso processes before we start so we don't have any left over from a previous extraction
                foreach (string item in listBox10.Items)
                {
                    if (System.IO.File.Exists(item))
                    {
                        string ISOName = Path.GetFileNameWithoutExtension(item);
                        Properties.Settings.Default.ExtractPath = textBox8.Text; // Set the extract path for use in generatebatchtoshowcommand
                        bool success = await XISOEE.ExtractISOAsync(item, checkBox39.Checked, false, textBox8.Text);
                        if (success) //if iso was extracted
                        {

                            PH.WaitForProcessByNameAsync("extract-xiso");
                            // sense we do this to wait until its done we have to kill all extract-xiso processes before we do anything so it attaches to the right and only one we open at a time
                            // we shouldn't have any left open sense we use /c now in genbatchtoshowcmd..
                            // this will keep our spinner going
                            string[] xexFiles = Directory.GetFiles(textBox8.Text + "/" + ISOName, "*.xex"); // get all xex files in the extraction path to array
                            foreach (string file in xexFiles) // patch each file in dir
                            {
                                //backupxex
                                if (checkBox36.Checked)
                                {
                                    try
                                    {
                                        System.IO.File.Copy(file, file + "OrininalUnpatchedBackup", true); // overwrite if exists no error
                                        X360GameHack.CurrentInstance.UpdateListboxForOutput("X360GameHack: Backed up XEX file " + file + " to " + file + "OrininalUnpatchedBackup");
                                    }
                                    catch (Exception ex)
                                    {
                                        MessageBox.Show("Failed to backup XEX file " + file + "Exception:" + ex, "X360GameHack Info!");
                                        button146.Enabled = true;
                                        pictureBox1.Hide();
                                    }
                                }

                                string args = ""; // string it to optimize
                                if (checkBox34.Checked) // title update chacked 
                                {
                                    args = "-u ";
                                }
                                if (checkBox38.Checked) // rgh patch checked
                                {
                                    args = args + "-r a -m r ";
                                }
                                if (checkBox37.Checked) // xdk patch checked
                                {
                                    args = args + "-r a -m d ";
                                }
                                if (checkBox35.Checked)// custom xextool command
                                {
                                    args = textBox60.Text;
                                }
                                invoker.InvokeXexTool(file, args, false); // invoke xextool for each xex file with args
                            }
                            //done
                           // pictureBox1.Hide();
                            // return; don't return here it needs to take longer 
                        }
                        else if (!success)
                        {
                            MessageBox.Show("ExtractXISOASync start Crashed. The extraction of " + item + " failed and can not continue. Please check the filepaths and try again.", "X360GameHack Error!");
                            button146.Enabled = true;
                            pictureBox1.Hide();
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show("The file " + item + " does not exist. Please check the path and try again for that file.", "X360GameHack Error!");
                        button146.Enabled = true;
                        pictureBox1.Hide();
                        return;
                    }
                }
            }
            else
            {
                MessageBox.Show("You must set a custom extraction path in the textbox below before you can extract ISOs.", "X360GameHack Error no destination!");
                button146.Enabled = true;
                pictureBox1.Hide();
                return;
            }
            pictureBox1.Hide();
        }

        private void button69_Click_1(object sender, EventArgs e)
        {
            FolderBrowserDialog folderDialog = new FolderBrowserDialog();
            folderDialog.Description = "Select a folder";
            DialogResult result = folderDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                string selectedPath = folderDialog.SelectedPath;
                textBox8.Text = selectedPath;
            }
        }

        private void listBox10_DragDrop(object sender, DragEventArgs e)
        {
            if (listBox10.Items.Contains("Drag your XISO in this box:"))
            {
                listBox10.Items.Clear();
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
                                AntiCommandInjection APT = new AntiCommandInjection();
                                APT.SanitizeInvokerFilePath(sanitizedPath);
                                invoker.ISOFilePath = sanitizedPath; // Update the file path
                                listBox10.Items.Add(sanitizedPath); // Add the sanitized path to the list box

                                MessageBox.Show("File renamed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Error renaming file: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                return;
                            }
                        }
                    }
                    else
                    {
                        Application.Exit(); //close if they won't fix the path
                    }
                }
                else
                {
                    AntiCommandInjection APT = new AntiCommandInjection();
                    APT.SanitizeInvokerFilePath(path);
                    invoker.ISOFilePath = path; // Update the file path
                    listBox10.Items.Add(path); // Add the sanitized path to the list box
                }
            }
        }






        private void listBox10_DragEnter(object sender, DragEventArgs e)
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

        private void button154_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select Location to put file";
                //dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        System.IO.File.Copy(dialog.SelectedPath + "\\" + textBox1.Text, dialog.SelectedPath + "\\" + textBox1.Text + ".bak", true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error: " + ex.Message, "X360GameHack Copy Error!");
                    }
                }
            }
        }
    }
}