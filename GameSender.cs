using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack
{
    public partial class GameSender : Form
    {

        FTPClient FTPClient = new FTPClient();
        public GameSender()
        {
            InitializeComponent();
        }

        private void button24_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "All Files (*.*)|*.*"; 

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string filePath = openFileDialog.FileName;
                listBox1.Items.Add(filePath);
            }
        }

        private void GameSender_Load(object sender, EventArgs e)
        {
            textBox7.Text = FTPClient.SendCurDirToGS;

        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (listBox1.SelectedItem !=null)
            {
                listBox1.SelectedItems.Remove(listBox1.SelectedItem);
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            listBox1.Items.Clear();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.Text = "Game Sender (sending games please wait!)";
            foreach (var item in listBox1.Items)
            {
                string filepaths = item.ToString();
                FTPClient.UploadFile(filepaths, textBox7.Text, FTPClient.IP, FTPClient.Port, FTPClient.UserName, FTPClient.Password);
            }
            this.Text = "Game Sender";
        }
    }
}
