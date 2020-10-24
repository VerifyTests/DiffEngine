using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DiffEngineTray.Common;
using MessageBoxButtons = DiffEngineTray.Common.MessageBoxButtons;
using MessageBoxIcon = DiffEngineTray.Common.MessageBoxIcon;

public partial class OptionsForm :
    Form
{
    Func<Settings, Task<IReadOnlyList<string>>> trySave = null!;
    IUpdater updater = new WindowsAppUpdater();
    IMessageBox messageBox;

    public OptionsForm(IMessageBox messageBox)
    {
        this.messageBox = messageBox;
        InitializeComponent();
        Icon = Images.Active;
        versionLabel.Text = $"Version: {VersionReader.VersionString}";
    }

    public OptionsForm(IMessageBox messageBox, Settings settings, Func<Settings, Task<IReadOnlyList<string>>> trySave) :
        this(messageBox)
    {
        this.trySave = trySave;
        acceptAllHotKey.HotKey = settings.AcceptAllHotKey;
        acceptOpenHotKey.HotKey = settings.AcceptOpenHotKey;
        startupCheckBox.Checked = settings.RunAtStartup;
    }

    async void save_Click(object sender, EventArgs e)
    {
        var newSettings = new Settings
        {
            RunAtStartup = startupCheckBox.Checked,
            AcceptAllHotKey = acceptAllHotKey.HotKey,
            AcceptOpenHotKey = acceptOpenHotKey.HotKey
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

        messageBox.Show(builder.ToString(), "Errors", MessageBoxIcon.Error, MessageBoxButtons.OK);
    }

    void diffEngineLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        LinkLauncher.LaunchUrl("https://github.com/VerifyTests/DiffEngine");
    }

    void trayDocsLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        LinkLauncher.LaunchUrl("https://github.com/VerifyTests/DiffEngine/blob/master/docs/tray.md");
    }

    void updateButton_Click(object sender, EventArgs e)
    {
        updater.Run();
    }
}