using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiffEngineTray.Common;

static class OptionsFormLauncher
{
    public static async Task Launch(IMessageBox messageBox, KeyRegister keyRegister, Tracker tracker)
    {
        var settings = await SettingsHelper.Read();
        var form = new OptionsForm(
            messageBox,
            settings,
            async newSettings => await Save(keyRegister, tracker, newSettings));
        form.ShowDialog();
    }

    static async Task<IReadOnlyList<string>> Save(KeyRegister keyRegister, Tracker tracker, Settings settings)
    {
        if (!settings.IsValidate(out var errors))
        {
            return errors;
        }

        var saveErrors = new List<string>();

        AddHotKey(keyRegister, settings.AcceptAllHotKey, KeyBindingIds.AcceptAll, tracker.AcceptAll, saveErrors);
        AddHotKey(keyRegister, settings.AcceptOpenHotKey, KeyBindingIds.AcceptOpen, tracker.AcceptOpen, saveErrors);

        if (saveErrors.Any())
        {
            return saveErrors;
        }

        if (settings.RunAtStartup)
        {
            Startup.Add();
        }
        else
        {
            Startup.Remove();
        }

        await SettingsHelper.Write(settings);
        return new List<string>();
    }

    static void AddHotKey(KeyRegister keyRegister, HotKey? hotKey, int id, Action action, List<string> saveErrors)
    {
        keyRegister.ClearBinding(id);
        if (hotKey == null)
        {
            return;
        }

        if (!keyRegister.TryAddBinding(id, hotKey.Shift, hotKey.Control, hotKey.Alt, hotKey.Key, action))
        {
            saveErrors.Add("Binding already registered");
        }
    }
}