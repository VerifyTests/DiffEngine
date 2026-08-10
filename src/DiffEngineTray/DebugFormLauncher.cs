/// <summary>
/// One debug window at a time, as with the options window: a second click on the menu item brings
/// the open one forward rather than opening another view of the same data.
/// </summary>
static class DebugFormLauncher
{
    static DebugForm? instance;

    public static async Task Launch(Tracker tracker)
    {
        if (instance != null)
        {
            instance.BringToFront();
            return;
        }

        using var form = new DebugForm(() => DebugReport.Build(tracker, DateTime.Now));
        instance = form;
        await form.ShowDialogAsync();
        instance = null;
    }
}
