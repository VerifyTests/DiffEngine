// Renders the options window through Verify.Avalonia (headless Skia) to a PNG + text snapshot.
public class OptionsWindowTests
{
    // Returns a value so the lambda binds to Dispatch<TResult>(Func<Task<TResult>>): the non-generic
    // Dispatch(Action) overload would treat an async lambda as `async void` and swallow Verify's failure.
    [Test]
    public Task Render() =>
        AvaloniaSession.Instance.Dispatch(
            async () =>
            {
                var settings = new Settings
                {
                    AcceptAllHotKey = new() { Control = true, Shift = true, Key = "A" },
                    DiscardAllHotKey = new() { Alt = true, Key = "D" },
                    RunAtStartup = true,
                    TargetOnLeft = true,
                    MaxInstancesToLaunch = 5
                };

                var window = new OptionsWindow(
                    settings,
                    _ => Task.FromResult<IReadOnlyCollection<string>>([]),
                    version: "1.2.3");
                window.Show();
                Dispatcher.UIThread.RunJobs();

                // Per-OS baselines: the Windows PNG is committed here; the macOS PNG is captured from the
                // first macOS CI run's *.received.* artifact. Avoids cross-OS pixel comparison.
                await Verify(window).UniqueForOSPlatform();
                return true;
            },
            Cancel.None);
}
