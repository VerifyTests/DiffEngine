using System.Linq;
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
        acceptAllHotKey.HotKey = settings.AcceptAllHotKey;
        acceptOpenHotKey.HotKey = settings.AcceptOpenHotKey;
        startupCheckBox.Checked = settings.RunAtStartup;
        targetOnLeftCheckBox.Checked = settings.TargetOnLeft;
        maxInstancesNumericUpDown.Value = settings.MaxInstancesToLaunch;
    }

    async void save_Click(object sender, EventArgs e)
    {
        var newSettings = new Settings
        {
            TargetOnLeft = targetOnLeftCheckBox.Checked,
            RunAtStartup = startupCheckBox.Checked,
            AcceptAllHotKey = acceptAllHotKey.HotKey,
            AcceptOpenHotKey = acceptOpenHotKey.HotKey,
            MaxInstancesToLaunch = (int)maxInstancesNumericUpDown.Value
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

    void updateButton_Click(object sender, EventArgs e)
    {
        Updater.Run();
    }
}