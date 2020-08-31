using System.Threading.Tasks;
using System.Windows.Forms;

static class OptionsFormLauncher
{
    public static async Task Launch()
    {
        var settings = await SettingsHelper.Read();
        var form = new OptionsForm
        {
            Settings = settings
        };
        var result = form.ShowDialog();
        if (result == DialogResult.OK)
        {
            await SettingsHelper.Write(settings);
        }
    }
}