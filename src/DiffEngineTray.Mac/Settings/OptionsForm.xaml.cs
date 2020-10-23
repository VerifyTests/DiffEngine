using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DiffEngineTray.Common;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Setting = Settings;

namespace DiffEngineTray.Mac.Settings
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class OptionsForm : ContentPage
    {
        public OptionsForm()
        {
            InitializeComponent();
            //Icon = Images.Active;
            //versionLabel.Text = $"Version: {VersionReader.VersionString}";
        }

        public OptionsForm(Setting settings, Func<Setting, Task<IReadOnlyList<string>>> trySave) :
            this()
        {
            this.trySave = trySave;
            //acceptAllHotKey.HotKey = settings.AcceptAllHotKey;
            //acceptOpenHotKey.HotKey = settings.AcceptOpenHotKey;
            //startupCheckBox.Checked = settings.RunAtStartup;
        }

        Func<Setting, Task<IReadOnlyList<string>>> trySave = null!;

        //IUpdater updater = new WindowsAppUpdater();

        async void save_Click(object sender, EventArgs e)
        {
            var newSettings = new Setting
            {
                //RunAtStartup = startupCheckBox.Checked,
                // AcceptAllHotKey = acceptAllHotKey.HotKey,
                // AcceptOpenHotKey = acceptOpenHotKey.HotKey
            };

            var errors = (await trySave(newSettings)).ToList();
            if (!errors.Any())
            {
                //DialogResult = DialogResult.OK;
                return;
            }

            var builder = new StringBuilder();
            foreach (var error in errors)
            {
                builder.AppendLine($" * {error}");
            }

            MacMessageBox.ShowMessage(builder.ToString(), "Errors", MessageBoxIcon.Error, MessageBoxButtons.OK);
        }

        // void diffEngineLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        // {
        //     LinkLauncher.LaunchUrl("https://github.com/VerifyTests/DiffEngine");
        // }
        //
        // void trayDocsLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        // {
        //     LinkLauncher.LaunchUrl("https://github.com/VerifyTests/DiffEngine/blob/master/docs/tray.md");
        // }
        //
        // void updateButton_Click(object sender, EventArgs e)
        // {
        //     updater.Run();
        // }
    }
}