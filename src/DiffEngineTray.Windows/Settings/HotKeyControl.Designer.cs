partial class HotKeyControl
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

    #region Component Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
            this.hotKey = new System.Windows.Forms.GroupBox();
            this.hotKeyEnabled = new System.Windows.Forms.CheckBox();
            this.keysSelectionPanel = new System.Windows.Forms.Panel();
            this.keyPanel = new System.Windows.Forms.Panel();
            this.keyCombo = new System.Windows.Forms.ComboBox();
            this.keyLabel = new System.Windows.Forms.Label();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.control = new System.Windows.Forms.CheckBox();
            this.alt = new System.Windows.Forms.CheckBox();
            this.shift = new System.Windows.Forms.CheckBox();
            this.helpLabel = new System.Windows.Forms.Label();
            this.hotKey.SuspendLayout();
            this.keysSelectionPanel.SuspendLayout();
            this.keyPanel.SuspendLayout();
            this.flowLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // hotKey
            // 
            this.hotKey.AutoSize = true;
            this.hotKey.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.hotKey.Controls.Add(this.hotKeyEnabled);
            this.hotKey.Controls.Add(this.keysSelectionPanel);
            this.hotKey.Controls.Add(this.helpLabel);
            this.hotKey.Dock = System.Windows.Forms.DockStyle.Top;
            this.hotKey.Location = new System.Drawing.Point(0, 0);
            this.hotKey.Name = "hotKey";
            this.hotKey.Size = new System.Drawing.Size(367, 108);
            this.hotKey.TabIndex = 3;
            this.hotKey.TabStop = false;
            // 
            // hotKeyEnabled
            // 
            this.hotKeyEnabled.AutoSize = true;
            this.hotKeyEnabled.Location = new System.Drawing.Point(6, -1);
            this.hotKeyEnabled.Name = "hotKeyEnabled";
            this.hotKeyEnabled.Size = new System.Drawing.Size(96, 19);
            this.hotKeyEnabled.TabIndex = 3;
            this.hotKeyEnabled.Text = "HotKey Label";
            this.hotKeyEnabled.UseVisualStyleBackColor = true;
            this.hotKeyEnabled.CheckedChanged += new System.EventHandler(this.hotKeyEnabled_CheckedChanged);
            // 
            // keysSelectionPanel
            // 
            this.keysSelectionPanel.AutoSize = true;
            this.keysSelectionPanel.Controls.Add(this.keyPanel);
            this.keysSelectionPanel.Controls.Add(this.flowLayoutPanel1);
            this.keysSelectionPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.keysSelectionPanel.Enabled = false;
            this.keysSelectionPanel.Location = new System.Drawing.Point(3, 40);
            this.keysSelectionPanel.Margin = new System.Windows.Forms.Padding(2);
            this.keysSelectionPanel.Name = "keysSelectionPanel";
            this.keysSelectionPanel.Padding = new System.Windows.Forms.Padding(5, 4, 5, 4);
            this.keysSelectionPanel.Size = new System.Drawing.Size(361, 65);
            this.keysSelectionPanel.TabIndex = 1;
            // 
            // keyPanel
            // 
            this.keyPanel.Controls.Add(this.keyCombo);
            this.keyPanel.Controls.Add(this.keyLabel);
            this.keyPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.keyPanel.Location = new System.Drawing.Point(5, 33);
            this.keyPanel.Name = "keyPanel";
            this.keyPanel.Padding = new System.Windows.Forms.Padding(3);
            this.keyPanel.Size = new System.Drawing.Size(351, 28);
            this.keyPanel.TabIndex = 6;
            // 
            // keyCombo
            // 
            this.keyCombo.Dock = System.Windows.Forms.DockStyle.Left;
            this.keyCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.keyCombo.FormattingEnabled = true;
            this.keyCombo.Location = new System.Drawing.Point(38, 3);
            this.keyCombo.Name = "keyCombo";
            this.keyCombo.Size = new System.Drawing.Size(44, 23);
            this.keyCombo.TabIndex = 5;
            // 
            // keyLabel
            // 
            this.keyLabel.AutoSize = true;
            this.keyLabel.Dock = System.Windows.Forms.DockStyle.Left;
            this.keyLabel.Location = new System.Drawing.Point(3, 3);
            this.keyLabel.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.keyLabel.Name = "keyLabel";
            this.keyLabel.Padding = new System.Windows.Forms.Padding(3);
            this.keyLabel.Size = new System.Drawing.Size(35, 21);
            this.keyLabel.TabIndex = 0;
            this.keyLabel.Text = "Key:";
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.AutoSize = true;
            this.flowLayoutPanel1.Controls.Add(this.control);
            this.flowLayoutPanel1.Controls.Add(this.alt);
            this.flowLayoutPanel1.Controls.Add(this.shift);
            this.flowLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Top;
            this.flowLayoutPanel1.Location = new System.Drawing.Point(5, 4);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Padding = new System.Windows.Forms.Padding(3);
            this.flowLayoutPanel1.Size = new System.Drawing.Size(351, 29);
            this.flowLayoutPanel1.TabIndex = 4;
            // 
            // control
            // 
            this.control.AutoSize = true;
            this.control.Dock = System.Windows.Forms.DockStyle.Top;
            this.control.Location = new System.Drawing.Point(5, 5);
            this.control.Margin = new System.Windows.Forms.Padding(2);
            this.control.Name = "control";
            this.control.Size = new System.Drawing.Size(66, 19);
            this.control.TabIndex = 7;
            this.control.Text = "Control";
            this.control.UseVisualStyleBackColor = true;
            // 
            // alt
            // 
            this.alt.AutoSize = true;
            this.alt.Dock = System.Windows.Forms.DockStyle.Top;
            this.alt.Location = new System.Drawing.Point(75, 5);
            this.alt.Margin = new System.Windows.Forms.Padding(2);
            this.alt.Name = "alt";
            this.alt.Size = new System.Drawing.Size(41, 19);
            this.alt.TabIndex = 6;
            this.alt.Text = "Alt";
            this.alt.UseVisualStyleBackColor = true;
            // 
            // shift
            // 
            this.shift.AutoSize = true;
            this.shift.Dock = System.Windows.Forms.DockStyle.Top;
            this.shift.Location = new System.Drawing.Point(120, 5);
            this.shift.Margin = new System.Windows.Forms.Padding(2);
            this.shift.Name = "shift";
            this.shift.Size = new System.Drawing.Size(50, 19);
            this.shift.TabIndex = 5;
            this.shift.Text = "Shift";
            this.shift.UseVisualStyleBackColor = true;
            // 
            // helpLabel
            // 
            this.helpLabel.AutoSize = true;
            this.helpLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.helpLabel.Location = new System.Drawing.Point(3, 19);
            this.helpLabel.Name = "helpLabel";
            this.helpLabel.Padding = new System.Windows.Forms.Padding(3);
            this.helpLabel.Size = new System.Drawing.Size(38, 21);
            this.helpLabel.TabIndex = 2;
            this.helpLabel.Text = "Help";
            // 
            // HotKeyControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.hotKey);
            this.Margin = new System.Windows.Forms.Padding(2);
            this.Name = "HotKeyControl";
            this.Size = new System.Drawing.Size(367, 122);
            this.hotKey.ResumeLayout(false);
            this.hotKey.PerformLayout();
            this.keysSelectionPanel.ResumeLayout(false);
            this.keysSelectionPanel.PerformLayout();
            this.keyPanel.ResumeLayout(false);
            this.keyPanel.PerformLayout();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

    }

    #endregion

    private System.Windows.Forms.GroupBox hotKey;
    private System.Windows.Forms.Panel keysSelectionPanel;
    private System.Windows.Forms.Panel keyPanel;
    private System.Windows.Forms.ComboBox keyCombo;
    private System.Windows.Forms.Label keyLabel;
    private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
    private System.Windows.Forms.CheckBox control;
    private System.Windows.Forms.CheckBox alt;
    private System.Windows.Forms.CheckBox shift;
    private System.Windows.Forms.Label helpLabel;
    private System.Windows.Forms.CheckBox hotKeyEnabled;
}