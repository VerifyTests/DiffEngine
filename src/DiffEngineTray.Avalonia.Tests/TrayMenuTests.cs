// Snapshots the tray NativeMenu structure produced by TrayMenuBuilder (mirrors the Windows MenuBuilderTest).
public class TrayMenuTests
{
    static readonly TrayCommands commands = new()
    {
        Exit = () => { },
        Options = () => { },
        OpenLogs = () => { },
        Purge = () => { },
        RaiseIssue = () => { },
        Refresh = () => { }
    };

    [Test]
    public Task Empty() =>
        VerifyMenu((_, _) => { });

    [Test]
    public Task OnlyDelete() =>
        VerifyMenu((tracker, directory) =>
            tracker.AddDelete(Path.Combine(directory, "file1.txt")));

    [Test]
    public Task OnlyMove() =>
        VerifyMenu((tracker, directory) =>
            tracker.AddMove(
                Path.Combine(directory, "file2.txt"),
                Path.Combine(directory, "file2.txt"),
                "theExe",
                "theArguments",
                true,
                null));

    [Test]
    public Task DiffTempTarget() =>
        VerifyMenu((tracker, directory) =>
            tracker.AddMove(
                Path.Combine(directory, "file3.txt"),
                Path.Combine(directory, "file4.txt"),
                "theExe",
                "theArguments",
                true,
                null));

    [Test]
    public Task Full() =>
        VerifyMenu((tracker, directory) =>
        {
            tracker.AddDelete(Path.Combine(directory, "file1.txt"));
            tracker.AddDelete(Path.Combine(directory, "file2.txt"));
            tracker.AddMove(Path.Combine(directory, "file3.txt"), Path.Combine(directory, "file3.txt"), "theExe", "theArguments", true, null);
            tracker.AddMove(Path.Combine(directory, "file4.txt"), Path.Combine(directory, "file4.txt"), "theExe", "theArguments", true, null);
        });

    static async Task VerifyMenu(Action<Tracker, string> populate)
    {
        var directory = Path.Combine(Path.GetTempPath(), "DiffEngineTrayTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            await using var tracker = new Tracker(() => { }, () => { });
            populate(tracker, directory);

            // NativeMenuItem construction must happen on the Avalonia UI thread; project to a plain
            // model there and verify the model on the test thread.
            var model = await AvaloniaSession.Instance.Dispatch(
                () => Task.FromResult(MenuDump.ToModel(TrayMenuBuilder.Build(commands, tracker))),
                CancellationToken.None);

            await Verify(model);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
