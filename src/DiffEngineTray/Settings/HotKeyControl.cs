using System.Collections.Generic;
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

    public char? Key
    {
        get => (char?) keyCombo.SelectedItem;
        set => keyCombo.SelectedItem = value;
    }

    public bool KeyEnabled
    {
        get => hotKeyEnabled.Checked;
        set => hotKeyEnabled.Checked = value;
    }

    public bool IsShift
    {
        get => shift.Checked;
        set => shift.Checked = value;
    }

    public bool IsAlt
    {
        get => alt.Checked;
        set => alt.Checked = value;
    }

    public bool IsControl
    {
        get => control.Checked;
        set => control.Checked = value;
    }

    static IEnumerable<object> GetAlphabet()
    {
        for (var c = 'A'; c <= 'Z'; c++)
        {
            yield return c;
        }
    }

    void hotKeyEnabled_CheckedChanged(object sender, System.EventArgs e)
    {
        keysSelectionPanel.Enabled = hotKeyEnabled.Checked;
    }
}