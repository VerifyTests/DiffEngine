using System.Collections.Generic;
using System.Threading.Tasks;

static class OptionsFormLauncher
{
    public static async Task Launch(KeyRegister keyRegister, Tracker tracker)
    {
        var settings = await SettingsHelper.Read();
        var form = new OptionsForm(
            settings,
            async newSettings =>
            {
                if (!newSettings.IsValidate(out var errors))
                {
                    return errors;
                }

                var allHotKey = newSettings.AcceptAllHotKey;
                if (allHotKey == null)
                {
                    keyRegister.ClearBinding(KeyBindingIds.AcceptAll);
                }
                else
                {
                    if (!keyRegister.TryAddBinding(KeyBindingIds.AcceptAll, allHotKey.Shift, allHotKey.Control, allHotKey.Alt, allHotKey.Key, tracker.AcceptAll))
                    {
                        return new List<string>{"Binding already registered"};
                    }
                }

                if (newSettings.RunAtStartup)
                {
                    Startup.Add();
                }
                else
                {
                    Startup.Remove();
                }
                await SettingsHelper.Write(newSettings);
                return new List<string>();
            });
        form.ShowDialog();
    }
}