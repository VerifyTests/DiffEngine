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
            this.keysSelectionPanel = new System.Windows.Forms.Panel();
            this.keyPanel = new System.Windows.Forms.Panel();
            this.keyCombo = new System.Windows.Forms.ComboBox();
            this.keyLabel = new System.Windows.Forms.Label();
            this.control = new System.Windows.Forms.CheckBox();
            this.alt = new System.Windows.Forms.CheckBox();
            this.shift = new System.Windows.Forms.CheckBox();
            this.hotKeyEnabled = new System.Windows.Forms.CheckBox();
            this.hotKey.SuspendLayout();
            this.keysSelectionPanel.SuspendLayout();
            this.keyPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // hotKey
            // 
            this.hotKey.AutoSize = true;
            this.hotKey.Controls.Add(this.keysSelectionPanel);
            this.hotKey.Controls.Add(this.hotKeyEnabled);
            this.hotKey.Dock = System.Windows.Forms.DockStyle.Top;
            this.hotKey.Location = new System.Drawing.Point(0, 0);
            this.hotKey.Name = "hotKey";
            this.hotKey.Padding = new System.Windows.Forms.Padding(9);
            this.hotKey.Size = new System.Drawing.Size(1012, 257);
            this.hotKey.TabIndex = 3;
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
            this.keysSelectionPanel.Location = new System.Drawing.Point(9, 71);
            this.keysSelectionPanel.Name = "keysSelectionPanel";
            this.keysSelectionPanel.Padding = new System.Windows.Forms.Padding(9);
            this.keysSelectionPanel.Size = new System.Drawing.Size(994, 177);
            this.keysSelectionPanel.TabIndex = 1;
            // 
            // keyPanel
            // 
            this.keyPanel.Controls.Add(this.keyCombo);
            this.keyPanel.Controls.Add(this.keyLabel);
            this.keyPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.keyPanel.Location = new System.Drawing.Point(9, 111);
            this.keyPanel.Name = "keyPanel";
            this.keyPanel.Padding = new System.Windows.Forms.Padding(9);
            this.keyPanel.Size = new System.Drawing.Size(976, 57);
            this.keyPanel.TabIndex = 6;
            // 
            // keyCombo
            // 
            this.keyCombo.Dock = System.Windows.Forms.DockStyle.Left;
            this.keyCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.keyCombo.FormattingEnabled = true;
            this.keyCombo.Location = new System.Drawing.Point(60, 9);
            this.keyCombo.Name = "keyCombo";
            this.keyCombo.Size = new System.Drawing.Size(73, 38);
            this.keyCombo.TabIndex = 5;
            // 
            // keyLabel
            // 
            this.keyLabel.AutoSize = true;
            this.keyLabel.Dock = System.Windows.Forms.DockStyle.Left;
            this.keyLabel.Location = new System.Drawing.Point(9, 9);
            this.keyLabel.Name = "keyLabel";
            this.keyLabel.Size = new System.Drawing.Size(51, 30);
            this.keyLabel.TabIndex = 0;
            this.keyLabel.Text = "Key:";
            // 
            // control
            // 
            this.control.AutoSize = true;
            this.control.Dock = System.Windows.Forms.DockStyle.Top;
            this.control.Location = new System.Drawing.Point(9, 77);
            this.control.Name = "control";
            this.control.Size = new System.Drawing.Size(976, 34);
            this.control.TabIndex = 4;
            this.control.Text = "Control";
            this.control.UseVisualStyleBackColor = true;
            // 
            // alt
            // 
            this.alt.AutoSize = true;
            this.alt.Dock = System.Windows.Forms.DockStyle.Top;
            this.alt.Location = new System.Drawing.Point(9, 43);
            this.alt.Name = "alt";
            this.alt.Size = new System.Drawing.Size(976, 34);
            this.alt.TabIndex = 1;
            this.alt.Text = "Alt";
            this.alt.UseVisualStyleBackColor = true;
            // 
            // shift
            // 
            this.shift.AutoSize = true;
            this.shift.Dock = System.Windows.Forms.DockStyle.Top;
            this.shift.Location = new System.Drawing.Point(9, 9);
            this.shift.Name = "shift";
            this.shift.Size = new System.Drawing.Size(976, 34);
            this.shift.TabIndex = 0;
            this.shift.Text = "Shift";
            this.shift.UseVisualStyleBackColor = true;
            // 
            // hotKeyEnabled
            // 
            this.hotKeyEnabled.AutoSize = true;
            this.hotKeyEnabled.Dock = System.Windows.Forms.DockStyle.Top;
            this.hotKeyEnabled.Location = new System.Drawing.Point(9, 37);
            this.hotKeyEnabled.Name = "hotKeyEnabled";
            this.hotKeyEnabled.Size = new System.Drawing.Size(994, 34);
            this.hotKeyEnabled.TabIndex = 0;
            this.hotKeyEnabled.Text = "Enabled";
            this.hotKeyEnabled.UseVisualStyleBackColor = true;
            this.hotKeyEnabled.CheckedChanged += new System.EventHandler(this.hotKeyEnabled_CheckedChanged);
            // 
            // HotKeyControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 30F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.hotKey);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "HotKeyControl";
            this.Size = new System.Drawing.Size(1012, 615);
            this.hotKey.ResumeLayout(false);
            this.hotKey.PerformLayout();
            this.keysSelectionPanel.ResumeLayout(false);
            this.keysSelectionPanel.PerformLayout();
            this.keyPanel.ResumeLayout(false);
            this.keyPanel.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

    }

    #endregion

    private System.Windows.Forms.GroupBox hotKey;
    private System.Windows.Forms.Panel keysSelectionPanel;
    private System.Windows.Forms.Panel keyPanel;
    private System.Windows.Forms.ComboBox keyCombo;
    private System.Windows.Forms.Label keyLabel;
    private System.Windows.Forms.CheckBox control;
    private System.Windows.Forms.CheckBox alt;
    private System.Windows.Forms.CheckBox shift;
    private System.Windows.Forms.CheckBox hotKeyEnabled;
}