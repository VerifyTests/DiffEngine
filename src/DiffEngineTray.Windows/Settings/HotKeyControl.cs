public partial class HotKeyControl :
    UserControl
{
    public HotKeyControl()
    {
        InitializeComponent();
        keyCombo.Items.AddRange(GetAlphabet().ToArray());
    }

    /// <summary>
    /// A new one each call, from the controls as they stand. A method rather than a property's
    /// getter, which WinForms' WFO2000 refuses to have return a new instance on every read.
    /// </summary>
    public HotKey? ReadHotKey()
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
            Key = (string) keyCombo.SelectedItem!
        };
    }

    public void ShowHotKey(HotKey? hotKey)
    {
        if (hotKey == null)
        {
            return;
        }

        hotKeyEnabled.Checked = true;
        keyCombo.SelectedItem = hotKey.Key;
        shift.Checked = hotKey.Shift;
        control.Checked = hotKey.Control;
        alt.Checked = hotKey.Alt;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Label
    {
        get => hotKeyEnabled.Text;
        set => hotKeyEnabled.Text = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Help
    {
        get => (string?) helpLabel.Text;
        set => helpLabel.Text = value;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        var padding = hotKey.Padding;
        helpLabel.MaximumSize = new(hotKey.Width - (padding.Left + padding.Right + 10), 0);
    }

    static IEnumerable<string> GetAlphabet()
    {
        for (var c = 'A'; c <= 'Z'; c++)
        {
            yield return c.ToString();
        }
    }

    void hotKeyEnabled_CheckedChanged(object sender, EventArgs e) =>
        keysSelectionPanel.Enabled = hotKeyEnabled.Checked;
}