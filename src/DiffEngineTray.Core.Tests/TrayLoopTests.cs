/// <summary>
/// The loop a head with no window runs, with <see cref="StandInTray" /> for the panel: what the
/// tray is handed, and what is done when an item on it is chosen.
/// </summary>
public class TrayLoopTests :
    IDisposable
{
    [Test]
    public async Task TheTrayIsHandedTheFixedItemsThenTheTracked()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file);
        using var cancel = new CancelSource();
        var actions = Actions(cancel);
        var tray = new StandInTray();

        var loop = TrayLoop.Run(tray, tracker, actions, () => TrayIcon.Active, cancel.Token);
        var model = await tray.FirstApplied.WaitAsync(wait);
        await cancel.CancelAsync();
        await loop;

        await Assert.That(model.Icon).IsEqualTo(TrayIcon.Active);
        await Assert.That(model.Tooltip).IsEqualTo("DiffEngineTray");
        await Assert.That(model.Items).IsEquivalentTo(
            [.. TrayMenu.Fixed(actions).Items, .. TrayMenu.Tracked(tracker, actions).Items]);
    }

    [Test]
    public async Task AChosenItemIsDone()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file);
        using var cancel = new CancelSource();
        var tray = new StandInTray();
        var loop = TrayLoop.Run(tray, tracker, Actions(cancel), () => TrayIcon.Default, cancel.Token);
        await tray.FirstApplied.WaitAsync(wait);

        tray.Choose($"delete:{file}:accept");
        await Until(() => tracker.Deletes.Count == 0);
        await cancel.CancelAsync();
        await loop;

        await Assert.That(File.Exists(file)).IsFalse();
    }

    [Test]
    public async Task ExitEndsTheLoop()
    {
        await using var tracker = new RecordingTracker();
        using var cancel = new CancelSource();
        var tray = new StandInTray();
        var loop = TrayLoop.Run(tray, tracker, Actions(cancel), () => TrayIcon.Default, cancel.Token);
        await tray.FirstApplied.WaitAsync(wait);

        tray.Choose("exit");

        await loop.WaitAsync(wait);
    }

    /// <summary>
    /// Thrown out of the loop, a failing item would have ended the tray with it.
    /// </summary>
    [Test]
    public async Task AnItemThatThrowsLeavesTheLoopRunning()
    {
        await using var tracker = new RecordingTracker();
        using var cancel = new CancelSource();
        var actions = Actions(cancel) with
        {
            DebugView = () => throw new("The report could not be written.")
        };
        var tray = new StandInTray();
        var loop = TrayLoop.Run(tray, tracker, actions, () => TrayIcon.Default, cancel.Token);
        await tray.FirstApplied.WaitAsync(wait);

        tray.Choose("debug");
        tray.Choose("exit");

        await loop.WaitAsync(wait);
    }

    /// <summary>
    /// Nowhere to show a menu, as on a GNOME without the AppIndicator extension: the tray holds
    /// the queue until the session ends it.
    /// </summary>
    [Test]
    public async Task WithNoTrayTheLoopWaitsForTheEnd()
    {
        await using var tracker = new RecordingTracker();
        using var cancel = new CancelSource();
        var loop = TrayLoop.Run(null, tracker, Actions(cancel), () => TrayIcon.Default, cancel.Token);

        await Assert.That(loop.IsCompleted).IsFalse();
        await cancel.CancelAsync();
        await loop.WaitAsync(wait);
    }

    /// <summary>
    /// The macOS head's loop, which AppKit needs on the thread that opened the tray: the pump, the
    /// model and what was chosen all on the thread that called it.
    /// </summary>
    [Test]
    public async Task APumpedLoopStaysOnItsThread()
    {
        await using var tracker = new RecordingTracker();
        using var cancel = new CancelSource();
        var threads = new ConcurrentBag<int>();
        var actions = Actions(cancel) with
        {
            DebugView = () => threads.Add(Environment.CurrentManagedThreadId)
        };
        var tray = new StandInTray();
        tray.Choose("debug");
        tray.Choose("exit");
        var loopThread = 0;

        var loop = new Thread(() =>
        {
            loopThread = Environment.CurrentManagedThreadId;
            TrayLoop.RunPumped(
                tray,
                () =>
                {
                    threads.Add(Environment.CurrentManagedThreadId);
                    Thread.Sleep(10);
                },
                tracker,
                actions,
                () => TrayIcon.Default,
                cancel.Token);
        });
        loop.Start();

        await Assert.That(loop.Join(wait)).IsTrue();
        await Assert.That(threads.Distinct()).IsEquivalentTo([loopThread]);
        await Assert.That(tray.Applied).IsNotEmpty();
    }

    static TrayActions Actions(CancelSource cancel) =>
        new(
            Exit: cancel.Cancel,
            Options: null,
            DebugView: () => { },
            Purge: null,
            OpenDiffTool: _ => { });

    static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow + wait;
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The chosen item was not done.");
            }

            await Task.Delay(20);
        }
    }

    // Long, since only a loop that never gets there takes it
    static readonly TimeSpan wait = TimeSpan.FromSeconds(30);

    string file = Path.GetTempFileName();

    public void Dispose() =>
        File.Delete(file);
}
