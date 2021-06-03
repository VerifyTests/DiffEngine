using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

public partial class HotKeyControl :
    UserControl
{
    public HotKeyControl()
    {
        InitializeComponent();
        keyCombo.Items.AddRange(GetAlphabet().ToArray());
    }

    public HotKey? HotKey
    {
        get
        {
            if (!hotKeyEnabled.Checked)
            {
                return null;
            }

            return new()
            {
                Shift = shift.Checked,
                Control = control.Checked,
                Alt = alt.Checked,
                Key = (string) keyCombo.SelectedItem
            };
        }
        set
        {
            if (value == null)
            {
                return;
            }

            hotKeyEnabled.Checked = true;
            keyCombo.SelectedItem = value.Key;
            shift.Checked = value.Shift;
            control.Checked = value.Control;
            alt.Checked = value.Alt;
        }
    }

    public string Label
    {
        get => hotKeyEnabled.Text;
        set => hotKeyEnabled.Text = value;
    }

    public string? Help
    {
        get => (string?) helpLabel.Text;
        set => helpLabel.Text = value;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        var padding = hotKey.Padding;
        helpLabel.MaximumSize = new Size(hotKey.Width - (padding.Left + padding.Right + 10), 0);
    }

    static IEnumerable<string> GetAlphabet()
    {
        for (var c = 'A'; c <= 'Z'; c++)
        {
            yield return c.ToString();
        }
    }

    void hotKeyEnabled_CheckedChanged(object sender, EventArgs e)
    {
        keysSelectionPanel.Enabled = hotKeyEnabled.Checked;
    }
}