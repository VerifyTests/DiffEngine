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
        using var viewer = new FakeViewer("Sample.cs:12", "Other.cs:40");
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

    /// <summary>
    /// A snapshot the applier would not write. The menu marked it with an exclamation and stopped
    /// there, so a sweep reporting "13 not written" left thirteen items saying only that something
    /// was wrong with them. The reason goes on the item, trimmed to a width a menu can hold,
    /// because a tray menu grows to its widest entry.
    /// </summary>
    [Test]
    public async Task SnapshotThatWasNotWrittenCarriesItsReason()
    {
        const string status = "Sample.cs:12 not written. No Verify or Throws call at line 12. One reached through a receiver of its own does not count.";
        // The host directly rather than through a FakeViewer: that one publishes its port in an
        // environment variable, which the tests running beside it are free to overwrite
        await using var tracker = new RecordingTracker(
            inline: new StubInlineHost(new PendingSnapshot("c:\\repo\\sample.cs|12", "Sample.cs:12", status)));

        var menu = MenuBuilder.Build(
            emptyAction,
            emptyAction,
            tracker);
        // The tracked items are built on Opening and exist only while the menu is up, so a test
        // that reads Items without this one sees the six fixed entries and nothing else
        menu.Show(0, 0);

        var item = menu.Items
            .OfType<System.Windows.Forms.ToolStripDropDownButton>()
            .Single(_ => _.Text!.StartsWith("Sample.cs:12"));
        await Assert.That(item.ToolTipText).IsEqualTo(status);

        // On the item as well as in the tip, since a reason only a hover reveals is one most
        // people never see. Trimmed, because a tray menu grows to its widest entry
        var label = item.DropDownItems.OfType<System.Windows.Forms.ToolStripLabel>().Single();
        await Assert.That(label.ToolTipText).IsEqualTo(status);
        await Assert.That(label.Text!.Length).IsLessThanOrEqualTo(70);
        await Assert.That(label.Text).StartsWith("Sample.cs:12 not written. No Verify or Throws call");
        menu.Close();
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

    [Test]
    public async Task OnlyInline()
    {
        using var viewer = new FakeViewer("Sample.cs:12", "Other.cs:40");
        await using var tracker = new RecordingTracker();
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
