namespace X360GameHack
{
    partial class ExtractSeveralGames
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ExtractSeveralGames));
            this.groupBox18 = new System.Windows.Forms.GroupBox();
            this.checkBox30 = new System.Windows.Forms.CheckBox();
            this.button66 = new System.Windows.Forms.Button();
            this.button67 = new System.Windows.Forms.Button();
            this.listBox2 = new System.Windows.Forms.ListBox();
            this.button1 = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.button3 = new System.Windows.Forms.Button();
            this.button4 = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.groupBox18.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox18
            // 
            this.groupBox18.Controls.Add(this.button1);
            this.groupBox18.Controls.Add(this.button66);
            this.groupBox18.Controls.Add(this.button67);
            this.groupBox18.Controls.Add(this.checkBox30);
            this.groupBox18.Controls.Add(this.listBox2);
            this.groupBox18.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.groupBox18.Location = new System.Drawing.Point(6, 6);
            this.groupBox18.Margin = new System.Windows.Forms.Padding(2);
            this.groupBox18.Name = "groupBox18";
            this.groupBox18.Padding = new System.Windows.Forms.Padding(2);
            this.groupBox18.Size = new System.Drawing.Size(448, 417);
            this.groupBox18.TabIndex = 9;
            this.groupBox18.TabStop = false;
            this.groupBox18.Text = "Extract Several XISO";
            // 
            // checkBox30
            // 
            this.checkBox30.AutoSize = true;
            this.checkBox30.Checked = true;
            this.checkBox30.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox30.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.checkBox30.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.875F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkBox30.Location = new System.Drawing.Point(28, 381);
            this.checkBox30.Margin = new System.Windows.Forms.Padding(2);
            this.checkBox30.Name = "checkBox30";
            this.checkBox30.Size = new System.Drawing.Size(157, 17);
            this.checkBox30.TabIndex = 10;
            this.checkBox30.Text = "Exclude System Update";
            this.checkBox30.UseVisualStyleBackColor = true;
            // 
            // button66
            // 
            this.button66.BackColor = System.Drawing.Color.White;
            this.button66.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button66.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.875F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button66.Location = new System.Drawing.Point(11, 246);
            this.button66.Margin = new System.Windows.Forms.Padding(2);
            this.button66.Name = "button66";
            this.button66.Size = new System.Drawing.Size(417, 38);
            this.button66.TabIndex = 9;
            this.button66.Text = "Extract Files From XISO + Patch any xex for Devkit if needed";
            this.button66.UseVisualStyleBackColor = false;
            this.button66.Click += new System.EventHandler(this.button66_Click);
            // 
            // button67
            // 
            this.button67.BackColor = System.Drawing.Color.White;
            this.button67.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button67.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.875F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button67.Location = new System.Drawing.Point(11, 204);
            this.button67.Margin = new System.Windows.Forms.Padding(2);
            this.button67.Name = "button67";
            this.button67.Size = new System.Drawing.Size(417, 38);
            this.button67.TabIndex = 8;
            this.button67.Text = "Extract Files From XISO + Patch any xex for RGH if needed";
            this.button67.UseVisualStyleBackColor = false;
            this.button67.Click += new System.EventHandler(this.button67_Click);
            // 
            // listBox2
            // 
            this.listBox2.AllowDrop = true;
            this.listBox2.FormattingEnabled = true;
            this.listBox2.ItemHeight = 16;
            this.listBox2.Location = new System.Drawing.Point(11, 20);
            this.listBox2.Margin = new System.Windows.Forms.Padding(2);
            this.listBox2.Name = "listBox2";
            this.listBox2.Size = new System.Drawing.Size(419, 180);
            this.listBox2.TabIndex = 7;
            this.listBox2.DragDrop += new System.Windows.Forms.DragEventHandler(this.listBox2_DragDrop);
            this.listBox2.DragEnter += new System.Windows.Forms.DragEventHandler(this.listBox2_DragEnter);
            // 
            // button1
            // 
            this.button1.BackColor = System.Drawing.Color.White;
            this.button1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button1.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.875F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button1.Location = new System.Drawing.Point(11, 288);
            this.button1.Margin = new System.Windows.Forms.Padding(2);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(417, 38);
            this.button1.TabIndex = 10;
            this.button1.Text = "Extract Files From XISO + Patch any xex for Bad Update if needed";
            this.button1.UseVisualStyleBackColor = false;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // button2
            // 
            this.button2.BackColor = System.Drawing.Color.White;
            this.button2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button2.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.875F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button2.Location = new System.Drawing.Point(17, 334);
            this.button2.Margin = new System.Windows.Forms.Padding(2);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(204, 38);
            this.button2.TabIndex = 11;
            this.button2.Text = "Remove Selected";
            this.button2.UseVisualStyleBackColor = false;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // button3
            // 
            this.button3.BackColor = System.Drawing.Color.White;
            this.button3.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button3.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.875F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button3.Location = new System.Drawing.Point(230, 334);
            this.button3.Margin = new System.Windows.Forms.Padding(2);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(204, 38);
            this.button3.TabIndex = 12;
            this.button3.Text = "Extract All";
            this.button3.UseVisualStyleBackColor = false;
            this.button3.Click += new System.EventHandler(this.button3_Click);
            // 
            // button4
            // 
            this.button4.BackColor = System.Drawing.Color.White;
            this.button4.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button4.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.875F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button4.Location = new System.Drawing.Point(230, 376);
            this.button4.Margin = new System.Windows.Forms.Padding(2);
            this.button4.Name = "button4";
            this.button4.Size = new System.Drawing.Size(204, 38);
            this.button4.TabIndex = 13;
            this.button4.Text = "Extract Selected";
            this.button4.UseVisualStyleBackColor = false;
            this.button4.Click += new System.EventHandler(this.button4_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(3, 425);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(440, 13);
            this.label1.TabIndex = 14;
            this.label1.Text = "DO NOT EXTRACT TO MANY AT A TIME IT MAY USE ALL YOUR MEMORY";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(3, 438);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(447, 13);
            this.label2.TabIndex = 15;
            this.label2.Text = "FOR HIGH END PCS ITS FASTER BECAUSE IT USES SEPERATE THREADS";
            // 
            // ExtractSeveralGames
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.LightGray;
            this.ClientSize = new System.Drawing.Size(459, 455);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.button4);
            this.Controls.Add(this.button3);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.groupBox18);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(2);
            this.Name = "ExtractSeveralGames";
            this.Text = "X360GameHack: Extract Several Games:";
            this.Load += new System.EventHandler(this.ExtractSeveralGames_Load);
            this.groupBox18.ResumeLayout(false);
            this.groupBox18.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox18;
        private System.Windows.Forms.CheckBox checkBox30;
        private System.Windows.Forms.Button button66;
        private System.Windows.Forms.Button button67;
        private System.Windows.Forms.ListBox listBox2;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.Button button4;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
    }
}