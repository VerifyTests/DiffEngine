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
            this.acceptAllHotKey = new HotKeyControl();
            this.discardAllHotKey = new HotKeyControl();
            this.startupCheckBox = new System.Windows.Forms.CheckBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.versionLabel = new System.Windows.Forms.Label();
            this.trayDocsLink = new System.Windows.Forms.LinkLabel();
            this.diffEngineLink = new System.Windows.Forms.LinkLabel();
            this.acceptOpenHotKey = new HotKeyControl();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.updateButton = new System.Windows.Forms.Button();
            this.targetOnLeftCheckBox = new System.Windows.Forms.CheckBox();
            this.maxInstancesGroupBox = new System.Windows.Forms.GroupBox();
            this.maxInstancesNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.bottomPanel.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.flowLayoutPanel1.SuspendLayout();
            this.maxInstancesGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.maxInstancesNumericUpDown)).BeginInit();
            this.SuspendLayout();
            // 
            // bottomPanel
            // 
            this.bottomPanel.AutoSize = true;
            this.bottomPanel.Controls.Add(this.cancel);
            this.bottomPanel.Controls.Add(this.save);
            this.bottomPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.bottomPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.bottomPanel.Location = new System.Drawing.Point(5, 639);
            this.bottomPanel.Margin = new System.Windows.Forms.Padding(2);
            this.bottomPanel.Name = "bottomPanel";
            this.bottomPanel.Size = new System.Drawing.Size(520, 29);
            this.bottomPanel.TabIndex = 1;
            // 
            // cancel
            // 
            this.cancel.AutoSize = true;
            this.cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancel.Location = new System.Drawing.Point(438, 2);
            this.cancel.Margin = new System.Windows.Forms.Padding(2);
            this.cancel.Name = "cancel";
            this.cancel.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.cancel.Size = new System.Drawing.Size(80, 25);
            this.cancel.TabIndex = 0;
            this.cancel.Text = "Cancel";
            this.cancel.UseVisualStyleBackColor = true;
            // 
            // save
            // 
            this.save.AutoSize = true;
            this.save.Location = new System.Drawing.Point(354, 2);
            this.save.Margin = new System.Windows.Forms.Padding(2);
            this.save.Name = "save";
            this.save.Size = new System.Drawing.Size(80, 25);
            this.save.TabIndex = 1;
            this.save.Text = "Apply";
            this.save.UseVisualStyleBackColor = true;
            this.save.Click += new System.EventHandler(this.save_Click);
            // 
            // acceptAllHotKey
            // 
            this.acceptAllHotKey.AutoSize = true;
            this.acceptAllHotKey.Dock = System.Windows.Forms.DockStyle.Top;
            this.acceptAllHotKey.Help = "Accept pending deletes and pending moves";
            this.acceptAllHotKey.HotKey = null;
            this.acceptAllHotKey.Label = "Accept all HotKey";
            this.acceptAllHotKey.Location = new System.Drawing.Point(5, 254);
            this.acceptAllHotKey.Margin = new System.Windows.Forms.Padding(2);
            this.acceptAllHotKey.Name = "acceptAllHotKey";
            this.acceptAllHotKey.Padding = new System.Windows.Forms.Padding(3);
            this.acceptAllHotKey.Size = new System.Drawing.Size(520, 114);
            this.acceptAllHotKey.TabIndex = 2;
            this.acceptAllHotKey.TabStop = false;
            // 
            // discardAllHotKey
            // 
            this.discardAllHotKey.AutoSize = true;
            this.discardAllHotKey.Dock = System.Windows.Forms.DockStyle.Top;
            this.discardAllHotKey.Help = "Discard all deletes and pending moves";
            this.discardAllHotKey.HotKey = null;
            this.discardAllHotKey.Label = "Discard all HotKey";
            this.discardAllHotKey.Location = new System.Drawing.Point(5, 140);
            this.discardAllHotKey.Margin = new System.Windows.Forms.Padding(2);
            this.discardAllHotKey.Name = "discardAllHotKey";
            this.discardAllHotKey.Padding = new System.Windows.Forms.Padding(3);
            this.discardAllHotKey.Size = new System.Drawing.Size(520, 114);
            this.discardAllHotKey.TabIndex = 2;
            this.discardAllHotKey.TabStop = false;
            // 
            // startupCheckBox
            // 
            this.startupCheckBox.AutoSize = true;
            this.startupCheckBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.startupCheckBox.Location = new System.Drawing.Point(5, 4);
            this.startupCheckBox.Margin = new System.Windows.Forms.Padding(2);
            this.startupCheckBox.Name = "startupCheckBox";
            this.startupCheckBox.Padding = new System.Windows.Forms.Padding(5, 4, 5, 4);
            this.startupCheckBox.Size = new System.Drawing.Size(520, 27);
            this.startupCheckBox.TabIndex = 3;
            this.startupCheckBox.Text = "Run at startup";
            this.startupCheckBox.UseVisualStyleBackColor = true;
            // 
            // groupBox1
            // 
            this.groupBox1.AutoSize = true;
            this.groupBox1.Controls.Add(this.versionLabel);
            this.groupBox1.Controls.Add(this.trayDocsLink);
            this.groupBox1.Controls.Add(this.diffEngineLink);
            this.groupBox1.Dock = System.Windows.Forms.DockStyle.Top;
            this.groupBox1.Location = new System.Drawing.Point(5, 497);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Padding = new System.Windows.Forms.Padding(6, 5, 6, 5);
            this.groupBox1.Size = new System.Drawing.Size(520, 89);
            this.groupBox1.TabIndex = 5;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "More information:";
            // 
            // versionLabel
            // 
            this.versionLabel.AutoSize = true;
            this.versionLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.versionLabel.Location = new System.Drawing.Point(6, 63);
            this.versionLabel.Name = "versionLabel";
            this.versionLabel.Padding = new System.Windows.Forms.Padding(3);
            this.versionLabel.Size = new System.Drawing.Size(51, 21);
            this.versionLabel.TabIndex = 2;
            this.versionLabel.Text = "Version";
            // 
            // trayDocsLink
            // 
            this.trayDocsLink.AutoSize = true;
            this.trayDocsLink.Dock = System.Windows.Forms.DockStyle.Top;
            this.trayDocsLink.Location = new System.Drawing.Point(6, 42);
            this.trayDocsLink.Name = "trayDocsLink";
            this.trayDocsLink.Padding = new System.Windows.Forms.Padding(3);
            this.trayDocsLink.Size = new System.Drawing.Size(175, 21);
            this.trayDocsLink.TabIndex = 1;
            this.trayDocsLink.TabStop = true;
            this.trayDocsLink.Text = "DiffEngineTray Documentation";
            this.trayDocsLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.trayDocsLink_LinkClicked);
            // 
            // diffEngineLink
            // 
            this.diffEngineLink.AutoSize = true;
            this.diffEngineLink.Dock = System.Windows.Forms.DockStyle.Top;
            this.diffEngineLink.Location = new System.Drawing.Point(6, 21);
            this.diffEngineLink.Name = "diffEngineLink";
            this.diffEngineLink.Padding = new System.Windows.Forms.Padding(3);
            this.diffEngineLink.Size = new System.Drawing.Size(111, 21);
            this.diffEngineLink.TabIndex = 0;
            this.diffEngineLink.TabStop = true;
            this.diffEngineLink.Text = "GitHub/DiffEngine";
            this.diffEngineLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.diffEngineLink_LinkClicked);
            // 
            // acceptOpenHotKey
            // 
            this.acceptOpenHotKey.AutoSize = true;
            this.acceptOpenHotKey.Dock = System.Windows.Forms.DockStyle.Top;
            this.acceptOpenHotKey.Help = "Accept pending deletes and moves with an open diff tool. This works for diff tool" +
    "s that are not MDI, eg it does not work for Visual Studio or Rider.";
            this.acceptOpenHotKey.HotKey = null;
            this.acceptOpenHotKey.Label = "Accept all open HotKey";
            this.acceptOpenHotKey.Location = new System.Drawing.Point(5, 368);
            this.acceptOpenHotKey.Margin = new System.Windows.Forms.Padding(2);
            this.acceptOpenHotKey.Name = "acceptOpenHotKey";
            this.acceptOpenHotKey.Padding = new System.Windows.Forms.Padding(3);
            this.acceptOpenHotKey.Size = new System.Drawing.Size(520, 129);
            this.acceptOpenHotKey.TabIndex = 6;
            this.acceptOpenHotKey.TabStop = false;
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.AutoSize = true;
            this.flowLayoutPanel1.Controls.Add(this.updateButton);
            this.flowLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Top;
            this.flowLayoutPanel1.Location = new System.Drawing.Point(5, 586);
            this.flowLayoutPanel1.Margin = new System.Windows.Forms.Padding(2);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Padding = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.flowLayoutPanel1.Size = new System.Drawing.Size(520, 33);
            this.flowLayoutPanel1.TabIndex = 7;
            // 
            // updateButton
            // 
            this.updateButton.AutoSize = true;
            this.updateButton.Location = new System.Drawing.Point(5, 4);
            this.updateButton.Margin = new System.Windows.Forms.Padding(2);
            this.updateButton.Name = "updateButton";
            this.updateButton.Size = new System.Drawing.Size(76, 25);
            this.updateButton.TabIndex = 0;
            this.updateButton.Text = "Update";
            this.updateButton.UseVisualStyleBackColor = true;
            this.updateButton.Click += new System.EventHandler(this.updateButton_Click);
            // 
            // targetOnLeftCheckBox
            // 
            this.targetOnLeftCheckBox.AutoSize = true;
            this.targetOnLeftCheckBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.targetOnLeftCheckBox.Location = new System.Drawing.Point(5, 31);
            this.targetOnLeftCheckBox.Margin = new System.Windows.Forms.Padding(2);
            this.targetOnLeftCheckBox.Name = "targetOnLeftCheckBox";
            this.targetOnLeftCheckBox.Padding = new System.Windows.Forms.Padding(5, 4, 5, 4);
            this.targetOnLeftCheckBox.Size = new System.Drawing.Size(520, 27);
            this.targetOnLeftCheckBox.TabIndex = 8;
            this.targetOnLeftCheckBox.Text = "Open target on left. The default is temp on left and target on right";
            this.targetOnLeftCheckBox.UseVisualStyleBackColor = true;
            // 
            // maxInstancesGroupBox
            // 
            this.maxInstancesGroupBox.Controls.Add(this.maxInstancesNumericUpDown);
            this.maxInstancesGroupBox.Controls.Add(this.label1);
            this.maxInstancesGroupBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.maxInstancesGroupBox.Location = new System.Drawing.Point(5, 58);
            this.maxInstancesGroupBox.Name = "maxInstancesGroupBox";
            this.maxInstancesGroupBox.Padding = new System.Windows.Forms.Padding(5);
            this.maxInstancesGroupBox.Size = new System.Drawing.Size(520, 82);
            this.maxInstancesGroupBox.TabIndex = 9;
            this.maxInstancesGroupBox.TabStop = false;
            this.maxInstancesGroupBox.Text = "Max instances to launch";
            // 
            // maxInstancesNumericUpDown
            // 
            this.maxInstancesNumericUpDown.Dock = System.Windows.Forms.DockStyle.Left;
            this.maxInstancesNumericUpDown.Location = new System.Drawing.Point(5, 44);
            this.maxInstancesNumericUpDown.Margin = new System.Windows.Forms.Padding(5);
            this.maxInstancesNumericUpDown.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.maxInstancesNumericUpDown.Name = "maxInstancesNumericUpDown";
            this.maxInstancesNumericUpDown.Size = new System.Drawing.Size(62, 23);
            this.maxInstancesNumericUpDown.TabIndex = 0;
            this.maxInstancesNumericUpDown.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Dock = System.Windows.Forms.DockStyle.Top;
            this.label1.Location = new System.Drawing.Point(5, 21);
            this.label1.Name = "label1";
            this.label1.Padding = new System.Windows.Forms.Padding(3, 3, 3, 5);
            this.label1.Size = new System.Drawing.Size(473, 23);
            this.label1.TabIndex = 1;
            this.label1.Text = "To minimize the impact on system resources, the maximum diffs to launch is restri" +
    "cted.";
            // 
            // OptionsForm
            // 
            this.AcceptButton = this.save;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.CancelButton = this.cancel;
            this.ClientSize = new System.Drawing.Size(530, 672);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.bottomPanel);
            this.Controls.Add(this.acceptOpenHotKey);
            this.Controls.Add(this.acceptAllHotKey);
            this.Controls.Add(this.discardAllHotKey);
            this.Controls.Add(this.maxInstancesGroupBox);
            this.Controls.Add(this.targetOnLeftCheckBox);
            this.Controls.Add(this.startupCheckBox);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Margin = new System.Windows.Forms.Padding(2);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OptionsForm";
            this.Padding = new System.Windows.Forms.Padding(5, 4, 5, 4);
            this.ShowInTaskbar = false;
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Text = "Options";
            this.bottomPanel.ResumeLayout(false);
            this.bottomPanel.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.maxInstancesGroupBox.ResumeLayout(false);
            this.maxInstancesGroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.maxInstancesNumericUpDown)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

    }

    private System.Windows.Forms.FlowLayoutPanel bottomPanel;
    private System.Windows.Forms.Button cancel;
    private System.Windows.Forms.Button save;
    private HotKeyControl acceptAllHotKey; 
    private HotKeyControl discardAllHotKey;
    private System.Windows.Forms.CheckBox startupCheckBox;
    private System.Windows.Forms.GroupBox groupBox1;
    private System.Windows.Forms.LinkLabel diffEngineLink;
    private System.Windows.Forms.LinkLabel trayDocsLink;
    private System.Windows.Forms.Label versionLabel;
    private HotKeyControl acceptOpenHotKey;
    private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
    private System.Windows.Forms.Button updateButton;
    private System.Windows.Forms.CheckBox targetOnLeftCheckBox;
    private System.Windows.Forms.GroupBox maxInstancesGroupBox;
    private System.Windows.Forms.NumericUpDown maxInstancesNumericUpDown;
    private System.Windows.Forms.Label label1;
}