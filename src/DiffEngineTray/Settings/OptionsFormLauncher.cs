static class OptionsFormLauncher
{
    static OptionsForm? instance;

    static EventHandler formOnClosed = (_, _) =>
    {
        instance!.Closed -= formOnClosed;
        instance = null;
    };

    public static async Task Launch(KeyRegister keyRegister, Tracker tracker)
    {
        if (instance != null)
        {
            instance.BringToFront();
            return;
        }

        var settings = await SettingsHelper.Read();
        using var form = new OptionsForm(settings, newSettings => Save(keyRegister, tracker, newSettings));
        instance = form;
        form.Closed += formOnClosed;
        form.ShowDialog();
    }

    static async Task<IReadOnlyCollection<string>> Save(KeyRegister keyRegister, Tracker tracker, Settings settings)
    {
        if (!settings.IsValidate(out var errors))
        {
            return errors;
        }

        var saveErrors = new List<string>();

        AddHotKey(keyRegister, settings.AcceptAllHotKey, KeyBindingIds.AcceptAll, tracker.AcceptAll, saveErrors);
        AddHotKey(keyRegister, settings.DiscardAllHotKey, KeyBindingIds.DiscardAll, tracker.Clear, saveErrors);
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
        return Array.Empty<string>();
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