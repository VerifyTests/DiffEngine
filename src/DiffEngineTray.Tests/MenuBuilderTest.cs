using EmptyFiles;

[TUnit.Core.Executors.STAThreadExecutor]
public class MenuBuilderTest :
    IDisposable
{
    static Action emptyAction = () =>
    {
    };

    [Test]
    public async Task Empty()
    {
        await using var tracker = new RecordingTracker();
        var menu = MenuBuilder.Build(
            emptyAction,
            emptyAction,
            tracker);
        await Verify(menu, settings);
    }

    [Test]
    public async Task OnlyMove()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file2, file2, "theExe", "theArguments", true, null);
        var menu = MenuBuilder.Build(
            emptyAction,
            emptyAction,
            tracker);
        await Verify(menu, settings);
    }

    [Test]
    public async Task OnlyDelete()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        var menu = MenuBuilder.Build(
            emptyAction,
            emptyAction,
            tracker);
        await Verify(menu, settings);
    }

    [Test]
    public async Task Full()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        tracker.AddMove(file3, file3, "theExe", "theArguments", true, null);
        tracker.AddMove(file4, file4, "theExe", "theArguments", true, null);
        var menu = MenuBuilder.Build(
            emptyAction,
            emptyAction,
            tracker);
        await Verify(menu, settings);
    }

    [Test]
    public async Task DiffTempTarget()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        tracker.AddMove(file3, file4, "theExe", "theArguments", true, null);
        var menu = MenuBuilder.Build(
            emptyAction,
            emptyAction,
            tracker);
        await Verify(menu, settings);
    }

    [Test]
    public async Task Many()
    {
        await using var tracker = new RecordingTracker();
        foreach (var file in AllFiles.AllPaths)
        {
            tracker.AddDelete(file);
        }

        var menu = MenuBuilder.Build(
            emptyAction,
            emptyAction,
            tracker);
        await Verify(menu, settings);
    }

    [Test]
    public async Task Grouped()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete("file2.txt");
        tracker.AddMove(file4, "file4.txt", "theExe", "theArguments", true, null);
        var menu = MenuBuilder.Build(
            emptyAction,
            emptyAction,
            tracker);
        await Verify(menu, settings);
    }

    [Test]
    public async Task FullGrouped()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete("file2.txt");
        tracker.AddMove(file3, file3, "theExe", "theArguments", true, null);
        tracker.AddMove(file4, "file4.txt", "theExe", "theArguments", true, null);
        var menu = MenuBuilder.Build(
            emptyAction,
            emptyAction,
            tracker);
        await Verify(menu, settings);
    }

    public MenuBuilderTest()
    {
        settings = new();
        file1 = Path.GetFullPath("file1.txt");
        file2 = Path.GetFullPath("file2.txt");
        file3 = Path.GetFullPath("file3.txt");
        file4 = Path.GetFullPath("file4.txt");
        File.WriteAllText(file1, "");
        File.WriteAllText(file2, "");
        File.WriteAllText(file3, "");
        File.WriteAllText(file4, "");
    }

    public void Dispose()
    {
        File.Delete(file1);
        File.Delete(file2);
        File.Delete(file3);
        File.Delete(file4);
    }

    string file1;
    string file2;
    string file3;
    string file4;
    VerifySettings settings;
}
