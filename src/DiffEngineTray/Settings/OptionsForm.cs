using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

public partial class OptionsForm :
    Form
{
    Settings settings= null!;
    Func<Task<IEnumerable<string>>> trySave = null!;

    public OptionsForm()
    {
        InitializeComponent();
        keyCombo.Items.AddRange(GetAlphabet().ToArray());

        Icon = Images.Active;
    }

    public OptionsForm(Settings settings, Func<Task<IEnumerable<string>>> trySave):
        this()
    {
        this.settings = settings;
        this.trySave = trySave;
        var key = settings.AcceptAllHotKey;
        if (key != null)
        {
            hotKeyEnabled.Checked = true;
            keysSelectionPanel.Enabled = true;
            shift.Checked = key.Shift;
            alt.Checked = key.Alt;
            control.Checked = key.Control;
            keyCombo.SelectedItem = key.Key;
        }
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

    void save_Click(object sender, EventArgs e)
    {
        if (hotKeyEnabled.Checked)
        {
            settings.AcceptAllHotKey = new HotKey
            {
                Key = (string) keyCombo.SelectedItem,
                Shift = shift.Checked,
                Alt = alt.Checked,
                Control = control.Checked
            };
        }
        else
        {
            settings.AcceptAllHotKey = null;
        }

        if (!settings.IsValidate(out var errors))
        {
            var builder = new StringBuilder();
            foreach (var error in errors)
            {
                builder.AppendLine($" * {error}");
            }

            MessageBox.Show(builder.ToString(), "Errors", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        DialogResult = DialogResult.OK;
    }
}