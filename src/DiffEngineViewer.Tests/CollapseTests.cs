/// <summary>
/// Folding group headers.
/// <para>
/// A fold is a view and never a filter: what it hides is still queued, still swept by accept-all,
/// and still counted by the header hiding it. Most of what follows exists to hold that line.
/// </para>
/// </summary>
public class CollapseTests
{
    const string solutionA = "solution|SolutionA";

    [Test]
    public async Task FoldingHidesTheMembersAndFlipsTheMarker()
    {
        var open = Fixtures.GroupedConflicted();
        await Assert.That(Labels(open)).Contains("- SolutionA (2)");

        var folded = ViewerSession.ToggleGroup(open, solutionA);

        await Assert.That(Labels(folded)).Contains("+ SolutionA (2)");
        // The header stays and keeps its count; only its members go.
        await Assert.That(Labels(folded).Any(_ => _.Contains("ATests.cs"))).IsFalse();
        await Assert.That(Labels(folded).Any(_ => _.Contains("BTests.cs"))).IsTrue();
    }

    [Test]
    public async Task FoldingTwiceUnfolds()
    {
        var open = Fixtures.GroupedConflicted();

        var round = ViewerSession.ToggleGroup(ViewerSession.ToggleGroup(open, solutionA), solutionA);

        await Assert.That(Labels(round)).IsEquivalentTo(Labels(open));
    }

    /// <summary>
    /// The column follows the selection, so a selection under a fold would leave the whole list
    /// with nothing highlighted.
    /// </summary>
    [Test]
    public async Task FoldingTheSelectedGroupMovesTheSelection()
    {
        // Select the first entry, which lives in SolutionA.
        var open = ViewerSession.Apply(Fixtures.GroupedConflicted(), Command.Select(0));
        await Assert.That(open.Selected).IsEqualTo(0);

        var folded = ViewerSession.ToggleGroup(open, solutionA);

        await Assert.That(QueueProjection.VisibleEntries(folded)).Contains(folded.Selected);
        await Assert.That(folded.Queue[folded.Selected].Solution).IsEqualTo("SolutionB");
    }

    /// <summary>
    /// Nothing left to move to: the selection stays rather than being cleared, because the panes
    /// are still showing it.
    /// </summary>
    [Test]
    public async Task FoldingEverythingLeavesTheSelectionAlone()
    {
        var open = ViewerSession.Apply(Fixtures.GroupedConflicted(), Command.Select(0));
        // Folding the first group already moved the selection into the second, so it is that one
        // the last fold has nowhere to move away from.
        var half = ViewerSession.ToggleGroup(open, solutionA);

        var folded = ViewerSession.ToggleGroup(half, "solution|SolutionB");

        await Assert.That(QueueProjection.VisibleEntries(folded)).IsEmpty();
        await Assert.That(folded.Selected).IsEqualTo(half.Selected);
    }

    [Test]
    public async Task TabSkipsFoldedEntries()
    {
        var open = ViewerSession.Apply(Fixtures.GroupedConflicted(), Command.Select(0));
        // Fold the test group holding entries 0 and 1, leaving the rest of SolutionA visible.
        var testKey = Rows(open).Single(_ => _.GroupName == "Compare handles nulls").GroupKey!;
        var folded = ViewerSession.ToggleGroup(open, testKey);

        var stepped = ViewerSession.Apply(folded, CommandKind.NextItem);

        // 1 is the other member of the folded test, so it is stepped over rather than into.
        await Assert.That(stepped.Selected).IsNotEqualTo(1);
        await Assert.That(QueueProjection.VisibleEntries(stepped)).Contains(stepped.Selected);
    }

    /// <summary>
    /// The tray, or a second process, asking for an entry that happens to be folded away.
    /// </summary>
    [Test]
    public async Task SelectingAFoldedEntryUnfoldsIt()
    {
        var folded = ViewerSession.ToggleGroup(Fixtures.GroupedConflicted(), solutionA);
        var key = folded.Queue[0].Key;

        var selected = ViewerSession.SelectKey(folded, key);

        await Assert.That(selected.Selected).IsEqualTo(0);
        await Assert.That(QueueProjection.VisibleEntries(selected)).Contains(0);
    }

    /// <summary>
    /// And unfolds only what was in the way. A fold on an unrelated group is left alone.
    /// </summary>
    [Test]
    public async Task RevealingLeavesOtherFoldsAlone()
    {
        var folded = ViewerSession.ToggleGroup(
            ViewerSession.ToggleGroup(Fixtures.GroupedConflicted(), solutionA),
            "solution|SolutionB");

        var selected = ViewerSession.SelectKey(folded, folded.Queue[0].Key);

        await Assert.That(selected.Collapsed).Contains("solution|SolutionB");
        await Assert.That(selected.Collapsed).DoesNotContain(solutionA);
    }

    /// <summary>
    /// The one that would be silent and destructive if it were wrong: a fold is a view, so accept
    /// all still takes what it hides.
    /// </summary>
    [Test]
    public async Task AcceptAllSweepsFoldedEntries()
    {
        var folded = ViewerSession.ToggleGroup(Fixtures.GroupedConflicted(), solutionA);
        var before = folded.Queue.Count;

        var accepted = ViewerSession.Apply(folded, CommandKind.AcceptAll, Fixtures.Applied);

        // Everything but the conflicted entry, which accept-all always leaves for review, and none
        // of it spared by being folded.
        await Assert.That(before).IsEqualTo(3);
        await Assert.That(accepted.Queue.Count).IsEqualTo(1);
        await Assert.That(accepted.Queue.Single().Conflicted).IsTrue();
    }

    /// <summary>
    /// Keyed by name, so a fold outlives the entries it was folded over.
    /// </summary>
    [Test]
    public async Task AFoldSurvivesItsMembersBeingAccepted()
    {
        var folded = ViewerSession.ToggleGroup(
            ViewerSession.Apply(Fixtures.GroupedConflicted(), Command.Select(2)),
            solutionA);

        var accepted = ViewerSession.Apply(folded, CommandKind.Accept, Fixtures.Applied);

        await Assert.That(accepted.Collapsed).Contains(solutionA);
    }

    [Test]
    public async Task TheHeaderMenuOffersTheOppositeOfItsState()
    {
        var open = Fixtures.GroupedConflicted();

        var expanded = ViewerSession.OpenMenu(open, 0);
        await Assert.That(expanded.Menu!.Items[0].Label).IsEqualTo("Collapse");

        var collapsed = ViewerSession.OpenMenu(ViewerSession.ToggleGroup(open, solutionA), 0);
        await Assert.That(collapsed.Menu!.Items[0].Label).IsEqualTo("Expand");
    }

    [Test]
    public async Task TheMenuItemFolds()
    {
        var opened = ViewerSession.OpenMenu(Fixtures.GroupedConflicted(), 0);

        var folded = ViewerSession.Apply(opened, CommandKind.ToggleGroup);

        await Assert.That(folded.Collapsed).Contains(solutionA);
        await Assert.That(folded.Menu).IsNull();
    }

    static IReadOnlyList<QueueItem> Rows(SessionState state) =>
        QueueProjection.Rows(state);

    static List<string> Labels(SessionState state) =>
        QueueProjection.Rows(state)
            .Select(_ => _.Label)
            .ToList();

    /// <summary>
    /// The entry being read is accepted, and the queue closes up: the index it leaves behind names
    /// the first entry of the folded group after it. Nothing in the list would be highlighted while
    /// the panes and Accept went on acting on an entry nobody could see.
    /// </summary>
    [Test]
    public async Task Accepting_the_entry_being_read_does_not_select_one_under_a_fold()
    {
        var state = ViewerSession.ToggleGroup(Fixtures.Inline(solutionA1, solutionA2, solutionB1, solutionB2), solutionB);
        state = ViewerSession.SelectKey(state, KeyOf(solutionA2));
        await Assert.That(QueueProjection.VisibleEntries(state)).Contains(state.Selected);

        var accepted = ViewerSession.Apply(state, CommandKind.Accept, Fixtures.Applied);

        await Assert.That(QueueProjection.VisibleEntries(accepted)).Contains(accepted.Selected);
    }

    /// <summary>
    /// The same for a window attached to someone else's queue, whose listing no longer has the
    /// entry being read - accepted from the tray menu, say.
    /// </summary>
    [Test]
    public async Task A_listing_without_the_entry_being_read_does_not_select_one_under_a_fold()
    {
        var state = ViewerSession.ToggleGroup(Fixtures.Attached(Fixtures.Pending(solutionA1, solutionA2, solutionB1, solutionB2)), solutionB);
        state = ViewerSession.SelectKey(state, KeyOf(solutionA2));
        await Assert.That(QueueProjection.VisibleEntries(state)).Contains(state.Selected);

        var synced = ViewerSession.Sync(state, Fixtures.Pending(solutionA1, solutionB1, solutionB2), [], null);

        await Assert.That(QueueProjection.VisibleEntries(synced)).Contains(synced.Selected);
    }

    const string solutionB = "solution|SolutionB";
    static readonly InlinePatch solutionA1 = Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 10);
    static readonly InlinePatch solutionA2 = Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 30);
    static readonly InlinePatch solutionB1 = Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 10);
    static readonly InlinePatch solutionB2 = Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 30);

    static string KeyOf(InlinePatch patch) =>
        QueueEntry.KeyForInline(patch.SourceFile, patch.LineHint);
}
