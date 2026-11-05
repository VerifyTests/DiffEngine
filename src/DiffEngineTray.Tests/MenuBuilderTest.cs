/// <summary>
/// How WinForms shows the tray's menu: the head's options and turns, through a tray that makes its
/// menu as PlatformHeads' NotifyIconTray does (<see cref="MenuTray" />). What the menu says and
/// does is <c>TrayMenuTests</c>', in the core's tests, as text.
/// </summary>
[TUnit.Core.Executors.STAThreadExecutor]
public class MenuBuilderTest :
    IDisposable
{
    [Test]
    public async Task Empty()
    {
        await using var tracker = new RecordingTracker();
        var menu = Build(tracker);
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
        var menu = Build(tracker);
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
        var menu = Build(tracker);
        await Verify(menu, settings);
    }

    /// <summary>
    /// The model's tips, which it keeps beside its items since not every head can show one, put on
    /// the ToolStrip items they belong to.
    /// </summary>
    [Test]
    public async Task ATipIsOnItsItem()
    {
        await using var tracker = new RecordingTracker(inline: new StubInlineHost(new PendingSnapshot("c:\\repo\\sample.cs|12", "Sample.cs:12", status)));
        var menu = Build(tracker);
        Open(menu);

        var item = Snapshot(menu);
        var label = item.DropDownItems
            .OfType<System.Windows.Forms.ToolStripMenuItem>()
            .Single(_ => !_.Enabled);
        var accept = item.DropDownItems
            .OfType<System.Windows.Forms.ToolStripMenuItem>()
            .Single(_ => _.Text == "Accept snapshot");
        var tips = (item.ToolTipText, label.ToolTipText, accept.ToolTipText);
        menu.Close();

        await Assert.That(tips).IsEqualTo((status, status, null));
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
        await using var tracker = new RecordingTracker(inline: new StubInlineHost(new PendingSnapshot("c:\\repo\\sample.cs|12", "Sample.cs:12", status)));
        var menu = Build(tracker);
        Open(menu);

        var item = Snapshot(menu);
        // Before it is shown, as the menu was: a drop down is a window of its own
        item.DropDown.TopLevel = false;
        item.ShowDropDown();

        await Verify(Draw(item.DropDown), "png", settings);
        menu.Close();
    }

    /// <summary>
    /// A click reports the item's id, and the next turn does it on the menus it was chosen from:
    /// the fixed items reach the head's actions, the tracked ones the tracker.
    /// </summary>
    [Test]
    public async Task AClickDoesWhatItsItemDoes()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        var menu = Build(tracker);
        Open(menu);
        // Both found first, since a click closes the menu and the close takes the items
        var exit = Item(menu.Items, "Exit");
        var accept = Item(menu.Items, "Accept delete");

        exit.PerformClick();
        turns!.Turn();
        accept.PerformClick();
        turns.Turn();
        menu.Close();

        await Assert.That(exits).IsEqualTo(1);
        await Assert.That(tracker.Deletes).IsEmpty();
        await Assert.That(File.Exists(file1)).IsFalse();
    }

    /// <summary>
    /// The menu as the head's tray makes it.
    /// </summary>
    System.Windows.Forms.ContextMenuStrip Build(Tracker tracker)
    {
        var tray = new MenuTray(MenuBuilder.Options(_ => turns!.Tooltip(_)));
        trays.Add(tray);
        turns = new(tray, tracker, actions, () => TrayIcon.Default);
        return tray.Menu;
    }

    static System.Windows.Forms.ToolStripMenuItem Item(System.Windows.Forms.ToolStripItemCollection items, string text) =>
        Flatten(items).First(_ => _.Text == text);

    static IEnumerable<System.Windows.Forms.ToolStripMenuItem> Flatten(System.Windows.Forms.ToolStripItemCollection items) =>
        items
            .OfType<System.Windows.Forms.ToolStripMenuItem>()
            .SelectMany(_ => Flatten(_.DropDownItems).Prepend(_));

    static System.Windows.Forms.ToolStripMenuItem Snapshot(System.Windows.Forms.ContextMenuStrip menu) =>
        menu.Items
            .OfType<System.Windows.Forms.ToolStripMenuItem>()
            .Single(_ => _.Text!.StartsWith("Sample.cs:12"));

    /// <summary>
    /// Opened, for the items an open builds, with nothing put on the desktop. A menu is a top
    /// level window, and shown as one it came up at the top left of the screen of whoever was at
    /// the machine, over what they were doing, for as long as the test took. Not top level it is
    /// a child of no window that is on a screen, and still opens, lays out, paints and closes as
    /// it does under the tray's icon. Verify.WinForms draws a menu the same way, which is why the
    /// tests here that only hand it one never showed anything.
    /// </summary>
    static void Open(System.Windows.Forms.ContextMenuStrip menu)
    {
        menu.TopLevel = false;
        menu.Show();
    }

    /// <summary>
    /// What <see cref="Open" /> is for, asked of Windows rather than of WinForms: a menu that is
    /// open as far as its own events and items go, in a window nobody can see. The drop down is
    /// here because it is a second window, and has to be told separately.
    /// </summary>
    [Test]
    public async Task AMenuOpenedByATestIsNotOnTheDesktop()
    {
        await using var tracker = new RecordingTracker(inline: new StubInlineHost(new PendingSnapshot("c:\\repo\\sample.cs|12", "Sample.cs:12", null)));
        var menu = Build(tracker);
        Open(menu);
        var item = Snapshot(menu);
        item.DropDown.TopLevel = false;
        item.ShowDropDown();

        var menuOpen = menu.Visible;
        var menuSeen = IsWindowVisible(menu.Handle);
        var dropDownOpen = item.DropDown.Visible;
        var dropDownSeen = IsWindowVisible(item.DropDown.Handle);
        menu.Close();

        await Assert.That(menuOpen).IsTrue();
        await Assert.That(menuSeen).IsFalse();
        await Assert.That(dropDownOpen).IsTrue();
        await Assert.That(dropDownSeen).IsFalse();
    }

    // False for a window any of whose parents is hidden, which is what says a window that is
    // shown is still not on a screen
    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr handle);

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

    const string status = "Sample.cs:12 not written. No Verify or Throws call at line 12. One reached through a receiver of its own does not count.";

    public MenuBuilderTest()
    {
        settings = new();
        actions = new(
            Exit: () => exits++,
            Options: () =>
            {
            },
            DebugView: () =>
            {
            },
            Purge: () =>
            {
            },
            OpenDiffTool: _ =>
            {
            });
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
        foreach (var tray in trays)
        {
            tray.Dispose();
        }

        File.Delete(file1);
        File.Delete(file2);
        File.Delete(file3);
        File.Delete(file4);
    }

    int exits;
    TrayActions actions;
    TrayTurns? turns;
    List<MenuTray> trays = [];
    string file1;
    string file2;
    string file3;
    string file4;
    VerifySettings settings;
}
