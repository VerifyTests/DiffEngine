/// <summary>
/// A tray standing in for a panel: it keeps every model it is handed, and reports the items a test
/// chooses, one a poll, as a panel's clicks arrive.
/// </summary>
sealed class StandInTray : ITray<TrayIcon>
{
    readonly ConcurrentQueue<string> chosen = new();
    readonly TaskCompletionSource<TrayModel<TrayIcon>> first = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConcurrentQueue<TrayModel<TrayIcon>> Applied { get; } = new();

    public Task<TrayModel<TrayIcon>> FirstApplied =>
        first.Task;

    public void Choose(string id) =>
        chosen.Enqueue(id);

    public void Apply(TrayModel<TrayIcon> model)
    {
        Applied.Enqueue(model);
        first.TrySetResult(model);
    }

    public void Notify(TrayNotification notification)
    {
    }

    public TrayInput Poll() =>
        chosen.TryDequeue(out var id) ? new(ClickedItem: id) : new();

    public void Dispose()
    {
    }
}
