/// <summary>
/// The tray's menu as text: what each item says, its glyph, whether it can be chosen, its tip, and
/// what it opens. The same on every head, so these run everywhere. How WinForms draws it is
/// MenuBuilderTest's.
/// </summary>
public class TrayMenuTests :
    IDisposable
{
    [Test]
    public async Task Fixed() =>
        await Verify(Render(TrayMenu.Fixed(head.Actions)));

    [Test]
    public async Task NothingTracked()
    {
        await using var tracker = new RecordingTracker();

        await Assert.That(TrayMenu.Tracked(tracker, head.Actions).Items).IsEmpty();
    }

    [Test]
    public async Task OnlyMove()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file2, file2, "theExe", "theArguments", true, null);

        await Verify(Render(TrayMenu.Tracked(tracker, head.Actions)));
    }

    [Test]
    public async Task OnlyDelete()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);

        await Verify(Render(TrayMenu.Tracked(tracker, head.Actions)));
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

        await Verify(Render(TrayMenu.Tracked(tracker, head.Actions)));
    }

    [Test]
    public async Task OnlyInline()
    {
        using var viewer = new FakeViewer("Sample.cs:12", "Other.cs:40");
        await using var tracker = new RecordingTracker();

        await Verify(Render(TrayMenu.Tracked(tracker, head.Actions)));
    }

    [Test]
    public async Task DiffTempTarget()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete(file2);
        tracker.AddMove(file3, file4, "theExe", "theArguments", true, null);

        await Verify(Render(TrayMenu.Tracked(tracker, head.Actions)));
    }

    [Test]
    public async Task Many()
    {
        await using var tracker = new RecordingTracker();
        foreach (var file in AllFiles.AllPaths)
        {
            tracker.AddDelete(file);
        }

        await Verify(Render(TrayMenu.Tracked(tracker, head.Actions)));
    }

    [Test]
    public async Task Grouped()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete("file2.txt");
        tracker.AddMove(file4, "file4.txt", "theExe", "theArguments", true, null);

        await Verify(Render(TrayMenu.Tracked(tracker, head.Actions)));
    }

    [Test]
    public async Task FullGrouped()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete("file2.txt");
        tracker.AddMove(file3, file3, "theExe", "theArguments", true, null);
        tracker.AddMove(file4, "file4.txt", "theExe", "theArguments", true, null);

        await Verify(Render(TrayMenu.Tracked(tracker, head.Actions)));
    }

    /// <summary>
    /// A snapshot the applier would not write. The menu marked it with an exclamation and stopped
    /// there, so a sweep reporting "13 not written" left thirteen items saying only that something
    /// was wrong with them. The reason goes on the item, trimmed to a width a menu can hold,
    /// because a tray menu grows to its widest entry, and whole in the tip.
    /// </summary>
    [Test]
    public async Task SnapshotThatWasNotWrittenCarriesItsReason()
    {
        const string status = "Sample.cs:12 not written. No Verify or Throws call at line 12. One reached through a receiver of its own does not count.";
        // The host directly rather than through a FakeViewer: that one publishes its port in an
        // environment variable, which the tests running beside it are free to overwrite
        await using var tracker = new RecordingTracker(
            inline: new StubInlineHost(new PendingSnapshot(Path.Combine(directory, "sample.cs|12"), "Sample.cs:12", status)));
        var menu = TrayMenu.Tracked(tracker, head.Actions);

        var item = menu.Items.Single(_ => _.Label.StartsWith("Sample.cs:12"));
        await Assert.That(menu.Tooltip(item.Id)).IsEqualTo(status);
        // On the item as well as in the tip, since a reason only a hover reveals is one most
        // people never see. The name goes, having just been said by the item this hangs under,
        // and what is left is one line so the drop down does not give every entry its height
        var label = item.Children!.Single(_ => !_.Enabled);
        await Assert.That(menu.Tooltip(label.Id)).IsEqualTo(status);
        await Assert.That(label.Label).DoesNotContain("Sample.cs:12");
        await Assert.That(label.Label).StartsWith("No Verify or Throws call at line 12.");
        await Assert.That(label.Label.Length).IsLessThanOrEqualTo(60);
        await Assert.That(label.Label).DoesNotContain("\n");
        await Verify(Render(menu));
    }

    /// <summary>
    /// A delete "Accept all" passes over, because a move was accepted onto its file. It was listed
    /// like every other delete, under a button that had just been pressed without removing it, and
    /// only the log said why. Marked and given its reason the way a snapshot that was not written
    /// is.
    /// </summary>
    [Test]
    public async Task ADeleteAcceptAllWouldKeepCarriesItsReason()
    {
        var staged = Path.Combine(directory, "staged");
        Directory.CreateDirectory(staged);
        var received = Path.Combine(staged, "Sample.Test.received.txt");
        var verified = Path.Combine(directory, "Sample.Test.verified.txt");
        var plain = Path.Combine(directory, "Plain.verified.txt");
        await File.WriteAllTextAsync(received, "received");
        await File.WriteAllTextAsync(verified, "verified");
        await File.WriteAllTextAsync(plain, "plain");
        await using var tracker = new RecordingTracker();
        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        tracker.AddDelete(verified);
        tracker.AddDelete(plain);
        await tracker.AcceptAll();
        // The plain delete went with the sweep, and is tracked again for something to compare
        await File.WriteAllTextAsync(plain, "plain");
        tracker.AddDelete(plain);

        var menu = TrayMenu.Tracked(tracker, head.Actions);

        var held = menu.Items.Single(_ => _.Label.StartsWith("Sample.Test.verified.txt"));
        // Its own mark, and not the one a snapshot that failed to apply carries
        await Assert.That(held.Label).IsEqualTo("Sample.Test.verified.txt ~");
        await Assert.That(menu.Tooltip(held.Id)).IsEqualTo(Tracker.WroteItsFile);
        var label = held.Children!.Single(_ => !_.Enabled);
        await Assert.That(menu.Tooltip(label.Id)).IsEqualTo(Tracker.WroteItsFile);
        await Assert.That(label.Label.Length).IsLessThanOrEqualTo(60);
        await Assert.That(label.Label).StartsWith("Kept by 'Accept all': a move was accepted onto this file");

        var other = menu.Items.Single(_ => _.Label.StartsWith("Plain.verified.txt"));
        await Assert.That(other.Label).IsEqualTo("Plain.verified.txt");
        await Assert.That(other.Children!.Where(_ => !_.Enabled)).IsEmpty();
        await Assert.That(menu.Tooltip(other.Id)).IsNull();
    }

    /// <summary>
    /// What a click reports, which on PlatformHeads' trays is the id alone, so two items sharing
    /// one would both do whatever the first did.
    /// </summary>
    [Test]
    public async Task EveryIdIsItsOwn()
    {
        using var viewer = new FakeViewer("Sample.cs:12", "Other.cs:40");
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddDelete("file2.txt");
        tracker.AddMove(file3, file3, "theExe", "theArguments", true, null);
        tracker.AddMove(file4, "file4.txt", "theExe", "theArguments", true, null);

        var ids = TrayMenu.Fixed(head.Actions).Items
            .Concat(TrayMenu.Tracked(tracker, head.Actions).Items)
            .SelectMany(Flatten)
            .Select(_ => _.Id)
            .ToList();

        await Assert.That(ids).HasDistinctItems();
    }

    [Test]
    public async Task TheFixedItemsAreTheHeads()
    {
        var menu = TrayMenu.Fixed(head.Actions);
        foreach (var label in new[] {"Exit", "Options", "Debug view", "Purge verified files"})
        {
            await Assert.That(menu.Invoke(Find(menu, label).Id)).IsTrue();
        }

        await Assert.That(head.Calls).IsEquivalentTo(["Exit", "Options", "DebugView", "Purge"]);
    }

    [Test]
    public async Task AcceptingAMoveFromItsItem()
    {
        var received = Path.Combine(directory, "Sample.Test.received.txt");
        var verified = Path.Combine(directory, "Sample.Test.verified.txt");
        await File.WriteAllTextAsync(received, "received");
        await using var tracker = new RecordingTracker();
        tracker.AddMove(received, verified, "theExe", "theArguments", true, null);
        var menu = TrayMenu.Tracked(tracker, head.Actions);

        await Assert.That(menu.Invoke(Find(menu, "Accept move").Id)).IsTrue();

        await Assert.That(tracker.Moves).IsEmpty();
        await Assert.That(await File.ReadAllTextAsync(verified)).IsEqualTo("received");
    }

    /// <summary>
    /// The move as it is when the item is chosen. A re-run replaces the move while a menu built
    /// before it can still be open, and the one the menu was built from then holds a process the
    /// replacement has disposed.
    /// </summary>
    [Test]
    public async Task OpenDiffToolIsHandedTheMoveAsItIsNow()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(file3, file4, "theExe", "theArguments", true, null);
        var menu = TrayMenu.Tracked(tracker, head.Actions);
        var replacement = tracker.AddMove(file3, file4, "otherExe", "otherArguments", true, null);

        menu.Invoke(Find(menu, "Open diff tool").Id);

        await Assert.That(head.Opened).IsEquivalentTo([replacement]);
    }

    [Test]
    public async Task OpenDiffToolDoesNothingOnceTheMoveHasGone()
    {
        await using var tracker = new RecordingTracker();
        var move = tracker.AddMove(file3, file4, "theExe", "theArguments", true, null);
        var menu = TrayMenu.Tracked(tracker, head.Actions);
        tracker.Discard(move);

        menu.Invoke(Find(menu, "Open diff tool").Id);

        await Assert.That(head.Opened).IsEmpty();
    }

    /// <summary>
    /// Each half answers for its own items only, which is how a head holding both finds the one
    /// a click came from.
    /// </summary>
    [Test]
    public async Task AnIdFromTheOtherHalfIsNotInvoked()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        var tracked = TrayMenu.Tracked(tracker, head.Actions);
        var fixedItems = TrayMenu.Fixed(head.Actions);

        await Assert.That(fixedItems.Invoke(Find(tracked, "Accept delete").Id)).IsFalse();
        await Assert.That(tracked.Invoke(Find(fixedItems, "Exit").Id)).IsFalse();
        await Assert.That(tracker.Deletes).HasSingleItem();
        await Assert.That(head.Calls).IsEmpty();
    }

    /// <summary>
    /// A heading is not something to choose, so it is shown and not handled.
    /// </summary>
    [Test]
    public async Task AHeadingDoesNothing()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        var menu = TrayMenu.Tracked(tracker, head.Actions);
        var heading = menu.Items.First(_ => _.IconName == "solution");

        await Assert.That(heading.Enabled).IsFalse();
        await Assert.That(menu.Invoke(heading.Id)).IsFalse();
    }

    static TrayMenuItem Find(TrayMenu menu, string label) =>
        menu.Items
            .SelectMany(Flatten)
            .First(_ => _.Label == label);

    static IEnumerable<TrayMenuItem> Flatten(TrayMenuItem item) =>
        [item, .. (item.Children ?? []).SelectMany(Flatten)];

    /// <summary>
    /// A line an item, indented by how deep it is in submenus. What a head draws beside the label
    /// is in brackets after it.
    /// </summary>
    static string Render(TrayMenu menu)
    {
        var builder = new StringBuilder();
        Render(builder, menu, menu.Items, 0);
        return builder.ToString();
    }

    static void Render(StringBuilder builder, TrayMenu menu, IReadOnlyList<TrayMenuItem> items, int depth)
    {
        foreach (var item in items)
        {
            builder.Append(' ', depth * 2);
            if (item.Separator)
            {
                builder.AppendLine("----");
                continue;
            }

            builder.Append(item.Label);
            if (item.IconName != null)
            {
                builder.Append($"  [glyph: {item.IconName}]");
            }

            if (item.Checked is { } on)
            {
                builder.Append(on ? "  [checked]" : "  [unchecked]");
            }

            if (!item.Enabled)
            {
                builder.Append("  [disabled]");
            }

            if (menu.Tooltip(item.Id) is { } tooltip)
            {
                builder.Append($"  [tip: {tooltip}]");
            }

            builder.AppendLine();
            if (item.HasChildren)
            {
                Render(builder, menu, item.Children!, depth + 1);
            }
        }
    }

    /// <summary>
    /// A head with no options window and no folder picker, as on Linux: run at login is on the
    /// menu, checked as the desktop says, and there is no purge.
    /// </summary>
    [Test]
    public async Task FixedForAHeadWithNoWindows() =>
        await Verify(Render(TrayMenu.Fixed(head.Windowless)));

    /// <summary>
    /// What the desktop has, read again for each menu, rather than a setting that can say
    /// otherwise.
    /// </summary>
    [Test]
    public async Task RunAtStartupSetsTheDesktopsEntry()
    {
        var menu = TrayMenu.Fixed(head.Windowless);

        await Assert.That(menu.Invoke(Find(menu, "Run at startup").Id)).IsTrue();
        await Assert.That(head.Login.Enabled).IsTrue();
        await Assert.That(Find(TrayMenu.Fixed(head.Windowless), "Run at startup").Checked).IsTrue();

        menu.Invoke(Find(menu, "Run at startup").Id);
        await Assert.That(head.Login.Enabled).IsFalse();
        await Assert.That(Find(TrayMenu.Fixed(head.Windowless), "Run at startup").Checked).IsFalse();
    }

    sealed class Head
    {
        public List<string> Calls { get; } = [];
        public List<TrackedMove> Opened { get; } = [];
        public StandInLogin Login { get; } = new();

        public TrayActions Actions =>
            new(
                Exit: () => Calls.Add("Exit"),
                Options: () => Calls.Add("Options"),
                DebugView: () => Calls.Add("DebugView"),
                Purge: () => Calls.Add("Purge"),
                OpenDiffTool: Opened.Add);

        public TrayActions Windowless =>
            new(
                Exit: () => Calls.Add("Exit"),
                Options: null,
                DebugView: () => Calls.Add("DebugView"),
                Purge: null,
                OpenDiffTool: Opened.Add,
                RunAtLogin: Login);
    }

    sealed class StandInLogin : IRunAtLogin
    {
        public bool Enabled { get; private set; }

        public bool Exists() =>
            Enabled;

        public void Set(bool enabled) =>
            Enabled = enabled;
    }

    Head head = new();

    public TrayMenuTests()
    {
        directory = Path.Combine(Path.GetTempPath(), $"TrayMenuTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
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
        Directory.Delete(directory, true);
    }

    string directory;
    string file1;
    string file2;
    string file3;
    string file4;
}
