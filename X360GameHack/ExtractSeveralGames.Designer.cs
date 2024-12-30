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
            this.groupBox18.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox18
            // 
            this.groupBox18.Controls.Add(this.checkBox30);
            this.groupBox18.Controls.Add(this.button66);
            this.groupBox18.Controls.Add(this.button67);
            this.groupBox18.Controls.Add(this.listBox2);
            this.groupBox18.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.groupBox18.Location = new System.Drawing.Point(12, 12);
            this.groupBox18.Name = "groupBox18";
            this.groupBox18.Size = new System.Drawing.Size(729, 631);
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
            this.checkBox30.Location = new System.Drawing.Point(22, 565);
            this.checkBox30.Margin = new System.Windows.Forms.Padding(4);
            this.checkBox30.Name = "checkBox30";
            this.checkBox30.Size = new System.Drawing.Size(289, 29);
            this.checkBox30.TabIndex = 10;
            this.checkBox30.Text = "Exclude System Update";
            this.checkBox30.UseVisualStyleBackColor = true;
            // 
            // button66
            // 
            this.button66.BackColor = System.Drawing.Color.White;
            this.button66.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button66.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.875F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button66.Location = new System.Drawing.Point(22, 477);
            this.button66.Margin = new System.Windows.Forms.Padding(4);
            this.button66.Name = "button66";
            this.button66.Size = new System.Drawing.Size(684, 73);
            this.button66.TabIndex = 9;
            this.button66.Text = "Extract Files From XISO + Patch any xex for Devkit if needed";
            this.button66.UseVisualStyleBackColor = false;
            // 
            // button67
            // 
            this.button67.BackColor = System.Drawing.Color.White;
            this.button67.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button67.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.875F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button67.Location = new System.Drawing.Point(22, 396);
            this.button67.Margin = new System.Windows.Forms.Padding(4);
            this.button67.Name = "button67";
            this.button67.Size = new System.Drawing.Size(684, 73);
            this.button67.TabIndex = 8;
            this.button67.Text = "Extract Files From XISO + Patch any xex for RGH if needed";
            this.button67.UseVisualStyleBackColor = false;
            // 
            // listBox2
            // 
            this.listBox2.FormattingEnabled = true;
            this.listBox2.ItemHeight = 31;
            this.listBox2.Items.AddRange(new object[] {
            " "});
            this.listBox2.Location = new System.Drawing.Point(22, 46);
            this.listBox2.Name = "listBox2";
            this.listBox2.Size = new System.Drawing.Size(684, 345);
            this.listBox2.TabIndex = 7;
            this.listBox2.DragDrop += new System.Windows.Forms.DragEventHandler(this.listBox2_DragDrop);
            this.listBox2.DragEnter += new System.Windows.Forms.DragEventHandler(this.listBox2_DragEnter);
            // 
            // ExtractSeveralGames
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 25F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.LightGray;
            this.ClientSize = new System.Drawing.Size(762, 665);
            this.Controls.Add(this.groupBox18);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "ExtractSeveralGames";
            this.Text = "X360GameHack: Extract Several Games:";
            this.groupBox18.ResumeLayout(false);
            this.groupBox18.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox18;
        private System.Windows.Forms.CheckBox checkBox30;
        private System.Windows.Forms.Button button66;
        private System.Windows.Forms.Button button67;
        private System.Windows.Forms.ListBox listBox2;
    }
}