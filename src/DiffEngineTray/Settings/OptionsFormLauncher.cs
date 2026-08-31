static class OptionsFormLauncher
{
    static OptionsForm? instance;

    public static async Task Launch(KeyRegister keyRegister, Tracker tracker)
    {
        if (instance != null)
        {
            instance.BringToFront();
            return;
        }

        var settings = SettingsHelper.Read();
        using var form = new OptionsForm(
            settings,
            newSettings => Save(keyRegister, tracker, settings, newSettings));
        instance = form;
        await form.ShowDialogAsync();
        instance = null;
    }

    static async Task<IReadOnlyCollection<string>> Save(KeyRegister keyRegister, Tracker tracker, Settings previous, Settings settings)
    {
        if (!settings.IsValidate(out var errors))
        {
            return errors;
        }

        var saveErrors = ReBind(keyRegister, tracker, previous, settings);

        if (saveErrors.Count != 0)
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
        LockedFilesHandler.AlwaysKill = settings.AlwaysKillLockingProcesses;
        return [];
    }

    /// <summary>
    /// The three hot keys, all of them or none.
    /// <para>
    /// Each one used to be bound as it was reached, and a collision on the second returned with
    /// the first already live on its new combination while settings.json still held the old one -
    /// so the dialog said the save had failed, and the keys said otherwise until the next restart.
    /// </para>
    /// </summary>
    internal static List<string> ReBind(KeyRegister keyRegister, Tracker tracker, Settings previous, Settings settings)
    {
        var saveErrors = new List<string>();
        Bind(keyRegister, tracker, settings, saveErrors);

        if (saveErrors.Count == 0)
        {
            return saveErrors;
        }

        // Back to what was live before this attempt. Its own outcome is not reported: these are
        // the bindings the tray already had, and there is nothing the person at the dialog could
        // do about one of them having been taken in the meantime
        Bind(keyRegister, tracker, previous, []);
        return saveErrors;
    }

    static void Bind(KeyRegister keyRegister, Tracker tracker, Settings settings, List<string> saveErrors)
    {
        AddHotKey(keyRegister, settings.AcceptAllHotKey, KeyBindingIds.AcceptAll, () => tracker.AcceptAll(), saveErrors);
        AddHotKey(keyRegister, settings.DiscardAllHotKey, KeyBindingIds.DiscardAll, tracker.Clear, saveErrors);
        AddHotKey(keyRegister, settings.AcceptOpenHotKey, KeyBindingIds.AcceptOpen, () => tracker.AcceptOpen(), saveErrors);
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