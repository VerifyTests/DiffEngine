// Renders the options window through Verify.Avalonia (headless Skia) to a PNG + text snapshot.
public class OptionsWindowTests
{
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

                await Verify(window);
            },
            Cancel.None);
}
