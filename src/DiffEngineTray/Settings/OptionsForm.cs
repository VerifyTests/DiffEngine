using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

public partial class OptionsForm :
    Form
{
    Func<Settings, Task<IReadOnlyList<string>>> trySave = null!;

    public OptionsForm()
    {
        InitializeComponent();
        Icon = Images.Active;
        versionLabel.Text = $"Version: {VersionReader.VersionString}";
    }

    public OptionsForm(Settings settings, Func<Settings, Task<IReadOnlyList<string>>> trySave) :
        this()
    {
        this.trySave = trySave;
        hotKey.HotKey = settings.AcceptAllHotKey;

        startupCheckBox.Checked = settings.RunAtStartup;
    }

    async void save_Click(object sender, EventArgs e)
    {
        var newSettings = new Settings
        {
            RunAtStartup = startupCheckBox.Checked,
            AcceptAllHotKey = hotKey.HotKey
        };

        var errors = (await trySave(newSettings)).ToList();
        if (!errors.Any())
        {
            DialogResult = DialogResult.OK;
            return;
        }

        var builder = new StringBuilder();
        foreach (var error in errors)
        {
            builder.AppendLine($" * {error}");
        }

        MessageBox.Show(builder.ToString(), "Errors", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    void diffEngineLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        LinkLauncher.LaunchUrl("https://github.com/VerifyTests/DiffEngine");
    }

    void trayDocsLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        LinkLauncher.LaunchUrl("https://github.com/VerifyTests/DiffEngine/blob/master/docs/tray.md");
    }
}