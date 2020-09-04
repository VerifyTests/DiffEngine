partial class OptionsForm
{
    System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    void InitializeComponent()
    {
            this.bottomPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.cancel = new System.Windows.Forms.Button();
            this.save = new System.Windows.Forms.Button();
            this.hotKey = new HotKeyControl();
            this.startupCheckBox = new System.Windows.Forms.CheckBox();
            this.bottomPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // bottomPanel
            // 
            this.bottomPanel.AutoSize = true;
            this.bottomPanel.Controls.Add(this.cancel);
            this.bottomPanel.Controls.Add(this.save);
            this.bottomPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.bottomPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.bottomPanel.Location = new System.Drawing.Point(9, 364);
            this.bottomPanel.Name = "bottomPanel";
            this.bottomPanel.Size = new System.Drawing.Size(556, 50);
            this.bottomPanel.TabIndex = 1;
            // 
            // cancel
            // 
            this.cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancel.Location = new System.Drawing.Point(415, 3);
            this.cancel.Name = "cancel";
            this.cancel.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.cancel.Size = new System.Drawing.Size(138, 44);
            this.cancel.TabIndex = 0;
            this.cancel.Text = "Cancel";
            this.cancel.UseVisualStyleBackColor = true;
            // 
            // save
            // 
            this.save.Location = new System.Drawing.Point(271, 3);
            this.save.Name = "save";
            this.save.Size = new System.Drawing.Size(138, 44);
            this.save.TabIndex = 1;
            this.save.Text = "Apply";
            this.save.UseVisualStyleBackColor = true;
            this.save.Click += new System.EventHandler(this.save_Click);
            // 
            // hotKey
            // 
            this.hotKey.AutoSize = true;
            this.hotKey.Dock = System.Windows.Forms.DockStyle.Top;
            this.hotKey.Location = new System.Drawing.Point(9, 9);
            this.hotKey.Name = "hotKey";
            this.hotKey.Padding = new System.Windows.Forms.Padding(9);
            this.hotKey.Size = new System.Drawing.Size(556, 46);
            this.hotKey.TabIndex = 2;
            this.hotKey.TabStop = false;
            this.hotKey.Text = "Accept all HotKey";
            // 
            // startupCheckBox
            // 
            this.startupCheckBox.AutoSize = true;
            this.startupCheckBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.startupCheckBox.Location = new System.Drawing.Point(9, 55);
            this.startupCheckBox.Name = "startupCheckBox";
            this.startupCheckBox.Padding = new System.Windows.Forms.Padding(9);
            this.startupCheckBox.Size = new System.Drawing.Size(556, 52);
            this.startupCheckBox.TabIndex = 3;
            this.startupCheckBox.Text = "Run at startup";
            this.startupCheckBox.UseVisualStyleBackColor = true;
            // 
            // OptionsForm
            // 
            this.AcceptButton = this.save;
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 30F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.cancel;
            this.ClientSize = new System.Drawing.Size(574, 423);
            this.Controls.Add(this.startupCheckBox);
            this.Controls.Add(this.hotKey);
            this.Controls.Add(this.bottomPanel);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OptionsForm";
            this.Padding = new System.Windows.Forms.Padding(9);
            this.ShowInTaskbar = false;
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Text = "Options";
            this.bottomPanel.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

    }
    private System.Windows.Forms.FlowLayoutPanel bottomPanel;
    private System.Windows.Forms.Button cancel;
    private System.Windows.Forms.Button save;
    private HotKeyControl hotKey;
    private System.Windows.Forms.CheckBox startupCheckBox;
}