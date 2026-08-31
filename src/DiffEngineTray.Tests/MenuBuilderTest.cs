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
        // people never see. The name goes, having just been said by the item this hangs under,
        // and what is left is one line so the drop down does not give every entry its height
        var label = item.DropDownItems
            .OfType<System.Windows.Forms.ToolStripMenuItem>()
            .Single(_ => !_.Enabled);
        await Assert.That(label.ToolTipText).IsEqualTo(status);
        await Assert.That(label.Text).DoesNotContain("Sample.cs:12");
        await Assert.That(label.Text).StartsWith("No Verify or Throws call at line 12.");
        await Assert.That(label.Text!.Length).IsLessThanOrEqualTo(60);
        await Assert.That(label.Text).DoesNotContain("\n");
        menu.Close();
    }

    /// <summary>
    /// The same item with its drop down open, which is where the reason lives. The menu images
    /// elsewhere in this file stop at the top level and show only the marker, so nothing here saw
    /// how wide the reason made the menu - the thing that decides whether putting it on the item
    /// was an improvement or a menu running off the side of the screen.
    /// </summary>
    [Test]
    public async Task SnapshotThatWasNotWrittenDropDown()
    {
        await using var tracker = new RecordingTracker(
            inline: new StubInlineHost(
                new PendingSnapshot(
                    "c:\\repo\\sample.cs|12",
                    "Sample.cs:12",
                    "Sample.cs:12 not written. No Verify or Throws call at line 12. One reached through a receiver of its own does not count.")));
        var menu = MenuBuilder.Build(
            emptyAction,
            emptyAction,
            tracker);
        menu.Show(0, 0);

        var item = menu.Items
            .OfType<System.Windows.Forms.ToolStripDropDownButton>()
            .Single(_ => _.Text!.StartsWith("Sample.cs:12"));
        item.ShowDropDown();

        await Verify(Draw(item.DropDown), "png", settings);
        menu.Close();
    }

    /// <summary>
    /// Drawn rather than handed to Verify.WinForms, which renders a control by parenting it to a
    /// form of its own - something a drop down, being a top level window already, refuses.
    /// </summary>
    static MemoryStream Draw(System.Windows.Forms.Control control)
    {
        using var bitmap = new System.Drawing.Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new(0, 0, bitmap.Width, bitmap.Height));
        var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        return stream;
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

    /// <summary>
    /// The tracked items were told from the fixed ones by their text, so a solution named after
    /// one of them kept its group header through the close that removed everything under it, and
    /// grew another on the next open.
    /// </summary>
    [Test]
    public async Task A_group_named_after_a_fixed_item_is_removed_with_the_rest()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"MenuBuilderTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Options.sln"), "");
            var file = Path.Combine(directory, "file.txt");
            await File.WriteAllTextAsync(file, "");
            await using var tracker = new RecordingTracker();
            tracker.AddDelete(file);
            var menu = MenuBuilder.Build(
                emptyAction,
                emptyAction,
                tracker);
            var fixedCount = menu.Items.Count;

            menu.Show(0, 0);
            var opened = menu.Items
                .Cast<System.Windows.Forms.ToolStripItem>()
                .Skip(fixedCount)
                .Select(_ => _.Text)
                .ToList();
            menu.Close();

            // That the group is there at all, and under the name the rest of this is about
            await Assert.That(opened).Contains("Options");
            await Assert.That(menu.Items.Count).IsEqualTo(fixedCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// A close removed the tracked items and the next open disposed whatever was still in the
    /// collection, which by then was only the fixed ones. So every open leaked a menu's worth of
    /// controls to the finaliser.
    /// </summary>
    [Test]
    public async Task Tracked_items_are_disposed_by_the_next_open()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        var menu = MenuBuilder.Build(
            emptyAction,
            emptyAction,
            tracker);
        var fixedCount = menu.Items.Count;

        menu.Show(0, 0);
        var tracked = menu.Items
            .Cast<System.Windows.Forms.ToolStripItem>()
            .Skip(fixedCount)
            .ToList();
        // ToolStripItem.IsDisposed stays false through a Dispose, so the event is what says it
        // happened
        var disposed = 0;
        foreach (var item in tracked)
        {
            item.Disposed += (_, _) => disposed++;
        }

        menu.Close();
        menu.Show(0, 0);
        menu.Close();

        await Assert.That(tracked).IsNotEmpty();
        await Assert.That(disposed).IsEqualTo(tracked.Count);
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
