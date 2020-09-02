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
            this.hotKey = new System.Windows.Forms.GroupBox();
            this.keysSelectionPanel = new System.Windows.Forms.Panel();
            this.keyPanel = new System.Windows.Forms.Panel();
            this.keyCombo = new System.Windows.Forms.ComboBox();
            this.keyLabel = new System.Windows.Forms.Label();
            this.control = new System.Windows.Forms.CheckBox();
            this.alt = new System.Windows.Forms.CheckBox();
            this.shift = new System.Windows.Forms.CheckBox();
            this.hotKeyEnabled = new System.Windows.Forms.CheckBox();
            this.startupCheckBox = new System.Windows.Forms.CheckBox();
            this.bottomPanel.SuspendLayout();
            this.hotKey.SuspendLayout();
            this.keysSelectionPanel.SuspendLayout();
            this.keyPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // bottomPanel
            // 
            this.bottomPanel.AutoSize = true;
            this.bottomPanel.Controls.Add(this.cancel);
            this.bottomPanel.Controls.Add(this.save);
            this.bottomPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.bottomPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.bottomPanel.Location = new System.Drawing.Point(10, 388);
            this.bottomPanel.Name = "bottomPanel";
            this.bottomPanel.Size = new System.Drawing.Size(1012, 52);
            this.bottomPanel.TabIndex = 1;
            // 
            // cancel
            // 
            this.cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancel.Location = new System.Drawing.Point(859, 3);
            this.cancel.Name = "cancel";
            this.cancel.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.cancel.Size = new System.Drawing.Size(150, 46);
            this.cancel.TabIndex = 0;
            this.cancel.Text = "Cancel";
            this.cancel.UseVisualStyleBackColor = true;
            // 
            // save
            // 
            this.save.Location = new System.Drawing.Point(703, 3);
            this.save.Name = "save";
            this.save.Size = new System.Drawing.Size(150, 46);
            this.save.TabIndex = 1;
            this.save.Text = "Apply";
            this.save.UseVisualStyleBackColor = true;
            this.save.Click += new System.EventHandler(this.save_Click);
            // 
            // hotKey
            // 
            this.hotKey.AutoSize = true;
            this.hotKey.Controls.Add(this.keysSelectionPanel);
            this.hotKey.Controls.Add(this.hotKeyEnabled);
            this.hotKey.Dock = System.Windows.Forms.DockStyle.Top;
            this.hotKey.Location = new System.Drawing.Point(10, 10);
            this.hotKey.Name = "hotKey";
            this.hotKey.Padding = new System.Windows.Forms.Padding(10);
            this.hotKey.Size = new System.Drawing.Size(1012, 277);
            this.hotKey.TabIndex = 2;
            this.hotKey.TabStop = false;
            this.hotKey.Text = "Accept all HotKey";
            // 
            // keysSelectionPanel
            // 
            this.keysSelectionPanel.AutoSize = true;
            this.keysSelectionPanel.Controls.Add(this.keyPanel);
            this.keysSelectionPanel.Controls.Add(this.control);
            this.keysSelectionPanel.Controls.Add(this.alt);
            this.keysSelectionPanel.Controls.Add(this.shift);
            this.keysSelectionPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.keysSelectionPanel.Enabled = false;
            this.keysSelectionPanel.Location = new System.Drawing.Point(10, 78);
            this.keysSelectionPanel.Name = "keysSelectionPanel";
            this.keysSelectionPanel.Padding = new System.Windows.Forms.Padding(10);
            this.keysSelectionPanel.Size = new System.Drawing.Size(992, 189);
            this.keysSelectionPanel.TabIndex = 1;
            // 
            // keyPanel
            // 
            this.keyPanel.Controls.Add(this.keyCombo);
            this.keyPanel.Controls.Add(this.keyLabel);
            this.keyPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.keyPanel.Location = new System.Drawing.Point(10, 118);
            this.keyPanel.Name = "keyPanel";
            this.keyPanel.Padding = new System.Windows.Forms.Padding(10);
            this.keyPanel.Size = new System.Drawing.Size(972, 61);
            this.keyPanel.TabIndex = 6;
            // 
            // keyCombo
            // 
            this.keyCombo.Dock = System.Windows.Forms.DockStyle.Left;
            this.keyCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.keyCombo.FormattingEnabled = true;
            this.keyCombo.Location = new System.Drawing.Point(68, 10);
            this.keyCombo.Name = "keyCombo";
            this.keyCombo.Size = new System.Drawing.Size(79, 40);
            this.keyCombo.TabIndex = 5;
            // 
            // keyLabel
            // 
            this.keyLabel.AutoSize = true;
            this.keyLabel.Dock = System.Windows.Forms.DockStyle.Left;
            this.keyLabel.Location = new System.Drawing.Point(10, 10);
            this.keyLabel.Name = "keyLabel";
            this.keyLabel.Size = new System.Drawing.Size(58, 32);
            this.keyLabel.TabIndex = 0;
            this.keyLabel.Text = "Key:";
            // 
            // control
            // 
            this.control.AutoSize = true;
            this.control.Dock = System.Windows.Forms.DockStyle.Top;
            this.control.Location = new System.Drawing.Point(10, 82);
            this.control.Name = "control";
            this.control.Size = new System.Drawing.Size(972, 36);
            this.control.TabIndex = 4;
            this.control.Text = "Control";
            this.control.UseVisualStyleBackColor = true;
            // 
            // alt
            // 
            this.alt.AutoSize = true;
            this.alt.Dock = System.Windows.Forms.DockStyle.Top;
            this.alt.Location = new System.Drawing.Point(10, 46);
            this.alt.Name = "alt";
            this.alt.Size = new System.Drawing.Size(972, 36);
            this.alt.TabIndex = 1;
            this.alt.Text = "Alt";
            this.alt.UseVisualStyleBackColor = true;
            // 
            // shift
            // 
            this.shift.AutoSize = true;
            this.shift.Dock = System.Windows.Forms.DockStyle.Top;
            this.shift.Location = new System.Drawing.Point(10, 10);
            this.shift.Name = "shift";
            this.shift.Size = new System.Drawing.Size(972, 36);
            this.shift.TabIndex = 0;
            this.shift.Text = "Shift";
            this.shift.UseVisualStyleBackColor = true;
            // 
            // hotKeyEnabled
            // 
            this.hotKeyEnabled.AutoSize = true;
            this.hotKeyEnabled.Dock = System.Windows.Forms.DockStyle.Top;
            this.hotKeyEnabled.Location = new System.Drawing.Point(10, 42);
            this.hotKeyEnabled.Name = "hotKeyEnabled";
            this.hotKeyEnabled.Size = new System.Drawing.Size(992, 36);
            this.hotKeyEnabled.TabIndex = 0;
            this.hotKeyEnabled.Text = "Enabled";
            this.hotKeyEnabled.UseVisualStyleBackColor = true;
            this.hotKeyEnabled.CheckedChanged += new System.EventHandler(this.hotKeyEnabled_CheckedChanged);
            // 
            // startupCheckBox
            // 
            this.startupCheckBox.AutoSize = true;
            this.startupCheckBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.startupCheckBox.Location = new System.Drawing.Point(10, 287);
            this.startupCheckBox.Name = "startupCheckBox";
            this.startupCheckBox.Padding = new System.Windows.Forms.Padding(10);
            this.startupCheckBox.Size = new System.Drawing.Size(1012, 56);
            this.startupCheckBox.TabIndex = 3;
            this.startupCheckBox.Text = "Run at startup";
            this.startupCheckBox.UseVisualStyleBackColor = true;
            // 
            // OptionsForm
            // 
            this.AcceptButton = this.save;
            this.AutoScaleDimensions = new System.Drawing.SizeF(13F, 32F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.cancel;
            this.ClientSize = new System.Drawing.Size(1032, 450);
            this.Controls.Add(this.startupCheckBox);
            this.Controls.Add(this.hotKey);
            this.Controls.Add(this.bottomPanel);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OptionsForm";
            this.Padding = new System.Windows.Forms.Padding(10);
            this.ShowInTaskbar = false;
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Text = "Options";
            this.bottomPanel.ResumeLayout(false);
            this.hotKey.ResumeLayout(false);
            this.hotKey.PerformLayout();
            this.keysSelectionPanel.ResumeLayout(false);
            this.keysSelectionPanel.PerformLayout();
            this.keyPanel.ResumeLayout(false);
            this.keyPanel.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

    }
    private System.Windows.Forms.FlowLayoutPanel bottomPanel;
    private System.Windows.Forms.Button cancel;
    private System.Windows.Forms.Button save;
    private System.Windows.Forms.GroupBox hotKey;
    private System.Windows.Forms.Panel keysSelectionPanel;
    private System.Windows.Forms.ComboBox keyCombo;
    private System.Windows.Forms.CheckBox control;
    private System.Windows.Forms.CheckBox alt;
    private System.Windows.Forms.CheckBox shift;
    private System.Windows.Forms.CheckBox hotKeyEnabled;
    private System.Windows.Forms.Panel keyPanel;
    private System.Windows.Forms.Label keyLabel;
    private System.Windows.Forms.CheckBox startupCheckBox;
}