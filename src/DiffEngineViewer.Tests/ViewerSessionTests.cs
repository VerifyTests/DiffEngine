public class ViewerSessionTests
{
    [Test]
    public async Task EnqueueAppendsDistinctKeys()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch("A.cs", 1, "\"a\"", "x"),
            Fixtures.Patch("B.cs", 1, "\"b\"", "y"));

        await Assert.That(state.Queue.Count).IsEqualTo(2);
        await Assert.That(state.Selected).IsEqualTo(0);
    }

    [Test]
    public async Task EnqueueReplacesSameKey()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch("A.cs", 1, "\"a\"", "first"),
            Fixtures.Patch("A.cs", 1, "\"a\"", "second"));

        await Assert.That(state.Queue.Count).IsEqualTo(1);
        await Assert.That(state.Queue[0].LeftText).IsEqualTo("second");
    }

    [Test]
    public async Task EnqueueKeyIgnoresPathCase()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch("A.cs", 1, "\"a\"", "first"),
            Fixtures.Patch("a.CS", 1, "\"a\"", "second"));

        await Assert.That(state.Queue.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SettleRemovesMatchingItem()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch("A.cs", 1, "\"a\"", "x"),
            Fixtures.Patch("B.cs", 1, "\"b\"", "y"));

        var settled = ViewerSession.Settle(state, QueueEntry.KeyForInline("A.cs", 1));

        await Assert.That(settled.Queue.Count).IsEqualTo(1);
        await Assert.That(settled.Queue[0].Name).IsEqualTo("B.cs:1");
        await Assert.That(settled.Exit).IsFalse();
    }

    [Test]
    public async Task SettleUnknownKeyChangesNothing()
    {
        var state = Fixtures.Inline(Fixtures.Patch("A.cs", 1, "\"a\"", "x"));

        var settled = ViewerSession.Settle(state, QueueEntry.KeyForInline("Nope.cs", 9));

        await Assert.That(settled).IsEqualTo(state);
    }

    [Test]
    public async Task SettlingTheLastItemExits()
    {
        var state = Fixtures.Inline(Fixtures.Patch("A.cs", 1, "\"a\"", "x"));

        var settled = ViewerSession.Settle(state, QueueEntry.KeyForInline("A.cs", 1));

        await Assert.That(settled.Queue).IsEmpty();
        await Assert.That(settled.Exit).IsTrue();
        await Assert.That(settled.Selected).IsEqualTo(-1);
    }

    [Test]
    public async Task AcceptingTheLastItemExits()
    {
        var state = Fixtures.Inline(Fixtures.Patch());

        var accepted = ViewerSession.Apply(state, CommandKind.Accept, Fixtures.Applied);

        await Assert.That(accepted.Queue).IsEmpty();
        await Assert.That(accepted.Exit).IsTrue();
    }

    [Test]
    public async Task AcceptFailureKeepsItemPending()
    {
        var state = Fixtures.Inline(Fixtures.Patch());
        var actions = Fixtures.Applying(InlineApplyResult.Failed("locked"));

        var accepted = ViewerSession.Apply(state, CommandKind.Accept, actions);

        await Assert.That(accepted.Queue.Count).IsEqualTo(1);
        await Assert.That(accepted.Queue[0].Status).IsEqualTo("locked");
        await Assert.That(accepted.Exit).IsFalse();
    }

    [Test]
    public async Task StalePatchIsDroppedNotRetried()
    {
        // A NotFound patch can never succeed, so it is removed rather than left pending.
        var state = Fixtures.Inline(Fixtures.Patch());
        var actions = Fixtures.Applying(InlineApplyResult.NotFound("source changed"));

        var accepted = ViewerSession.Apply(state, CommandKind.Accept, actions);

        await Assert.That(accepted.Queue).IsEmpty();
    }

    /// <summary>
    /// File mode is one comparison, so accepting copies left over right and there is nothing left
    /// to show.
    /// </summary>
    [Test]
    public async Task AcceptingAFileCopiesLeftOverRight()
    {
        var copies = new List<(string Left, string Right)>();
        var state = Fixtures.File();

        var accepted = ViewerSession.Apply(state, CommandKind.Accept, Fixtures.Copying((left, right) => copies.Add((left, right))));

        await Assert.That(copies).IsEquivalentTo([("Sample.received.txt", "Sample.verified.txt")]);
        await Assert.That(accepted.Queue).IsEmpty();
        await Assert.That(accepted.Exit).IsTrue();
        await Assert.That(accepted.Message).IsEqualTo("Accepted Sample.received.txt <> Sample.verified.txt");
    }

    /// <summary>
    /// A locked target is retryable, so the comparison stays on screen carrying what went wrong.
    /// </summary>
    [Test]
    public async Task AFailedCopyKeepsTheComparison()
    {
        var state = Fixtures.File();
        var actions = Fixtures.Copying((_, _) => throw new IOException("the target is locked"));

        var accepted = ViewerSession.Apply(state, CommandKind.Accept, actions);

        await Assert.That(accepted.Queue).HasSingleItem();
        await Assert.That(accepted.Queue[0].Status).IsEqualTo("the target is locked");
        await Assert.That(accepted.Message).IsEqualTo("the target is locked");
        await Assert.That(accepted.Exit).IsFalse();
    }

    /// <summary>
    /// Shift+A reaches accept all even though the button is disabled for a single item. With one
    /// comparison it is the same act, and must not copy twice or report a count.
    /// </summary>
    [Test]
    public async Task AcceptAllInFileModeIsAccept()
    {
        var copies = new List<(string Left, string Right)>();
        var state = Fixtures.File();

        var accepted = ViewerSession.Apply(state, CommandKind.AcceptAll, Fixtures.Copying((left, right) => copies.Add((left, right))));

        await Assert.That(copies).HasSingleItem();
        await Assert.That(accepted.Queue).IsEmpty();
        await Assert.That(accepted.Message).IsEqualTo("Accepted Sample.received.txt <> Sample.verified.txt");
    }

    /// <summary>
    /// Both, because with one comparison discard all is discard. Looped rather than parameterised
    /// because CommandKind is internal and a test method taking one cannot be public.
    /// </summary>
    [Test]
    public async Task DiscardingAFileLeavesNothingToShow()
    {
        foreach (var command in (CommandKind[]) [CommandKind.Discard, CommandKind.DiscardAll])
        {
            var discarded = ViewerSession.Apply(Fixtures.File(), command);

            await Assert.That(discarded.Queue).IsEmpty();
            await Assert.That(discarded.Exit).IsTrue();
            await Assert.That(discarded.Message).IsEqualTo("Discarded Sample.received.txt <> Sample.verified.txt");
        }
    }

    /// <summary>
    /// Settles arrive over the socket and file mode runs without one, so this cannot happen. The
    /// guard is what stops it being an NRE if it ever does.
    /// </summary>
    [Test]
    public async Task SettlingAFileComparisonIsIgnored()
    {
        var state = Fixtures.File();

        var settled = ViewerSession.Settle(state, state.Queue[0].Key);

        await Assert.That(settled).IsSameReferenceAs(state);
    }

    [Test]
    public async Task ScrollIsClampedToTheLastPage()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));

        var end = ViewerSession.Apply(state, CommandKind.ScrollEnd);

        var body = ScreenBuilder.BodyRows(state);
        await Assert.That(end.ScrollTop).IsEqualTo(state.Queue[0].TotalRows - body);
    }

    [Test]
    public async Task ScrollDoesNotGoNegative()
    {
        var state = Fixtures.File();

        var up = ViewerSession.Apply(state, CommandKind.PageUp);

        await Assert.That(up.ScrollTop).IsEqualTo(0);
    }

    /// <summary>
    /// What a scrollbar thumb reports: an absolute row rather than a delta, clamped exactly like
    /// every other scroll so the bar can never offer a position the session then refuses.
    /// </summary>
    [Test]
    public async Task ScrollToClampsToTheLastPage()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));

        var scrolled = ViewerSession.Apply(state, Command.Scroll(int.MaxValue));

        var body = ScreenBuilder.BodyRows(state);
        await Assert.That(scrolled.ScrollTop).IsEqualTo(state.Queue[0].TotalRows - body);
    }

    [Test]
    public async Task ScrollToDoesNotGoNegative()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));

        var scrolled = ViewerSession.Apply(state, Command.Scroll(-5));

        await Assert.That(scrolled.ScrollTop).IsEqualTo(0);
    }

    [Test]
    public async Task ScrollToTakesAnAbsoluteRow()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));

        var scrolled = ViewerSession.Apply(state, Command.Scroll(7));

        await Assert.That(scrolled.ScrollTop).IsEqualTo(7);
    }

    [Test]
    public async Task ShortContentDoesNotScroll()
    {
        var state = Fixtures.File();

        var down = ViewerSession.Apply(state, CommandKind.ScrollDown);

        await Assert.That(down.ScrollTop).IsEqualTo(0);
    }

    [Test]
    public async Task GrowingTheWindowPullsScrollBack()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));
        var end = ViewerSession.Apply(state, CommandKind.ScrollEnd);

        var resized = ViewerSession.Resize(end, Fixtures.Columns, 200);

        await Assert.That(resized.ScrollTop).IsEqualTo(0);
    }

    [Test]
    public async Task NextChangeWalksBlocksThenStops()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));

        var first = ViewerSession.Apply(state, CommandKind.NextChange);
        var second = ViewerSession.Apply(first, CommandKind.NextChange);
        var third = ViewerSession.Apply(second, CommandKind.NextChange);
        var past = ViewerSession.Apply(third, CommandKind.NextChange);

        // Changes sit on lines 3, 17 and 33, so rows 2, 16 and 32 zero based. Row 32 is past the
        // last full page of 40 rows in a 16 row viewport, so it clamps to 24 and stays there.
        await Assert.That(first.ScrollTop).IsEqualTo(2);
        await Assert.That(second.ScrollTop).IsEqualTo(16);
        await Assert.That(third.ScrollTop).IsEqualTo(24);
        await Assert.That(past.ScrollTop).IsEqualTo(24);
    }

    [Test]
    public async Task SelectingResetsScroll()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch("A.cs", 1, null, Fixtures.Long(true)),
            Fixtures.Patch("B.cs", 1, "\"b\"", "y"));
        var scrolled = ViewerSession.Apply(state, CommandKind.PageDown);
        await Assert.That(scrolled.ScrollTop).IsGreaterThan(0);

        var selected = ViewerSession.Apply(scrolled, CommandKind.NextItem);

        await Assert.That(selected.Selected).IsEqualTo(1);
        await Assert.That(selected.ScrollTop).IsEqualTo(0);
    }

    [Test]
    public async Task SelectingPastTheEndIsIgnored()
    {
        var state = Fixtures.Inline(Fixtures.Patch());

        var selected = ViewerSession.Apply(state, CommandKind.NextItem);

        await Assert.That(selected.Selected).IsEqualTo(0);
    }

    /// <summary>
    /// Something accepted elsewhere shifts the list under the reader, so selection follows the key
    /// rather than the index and what is on screen does not silently change.
    /// </summary>
    [Test]
    public async Task SyncKeepsTheReaderOnTheSameItem()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch("A.cs", 1, null, "a"),
            Fixtures.Patch("B.cs", 2, null, "b"));
        var onB = ViewerSession.Apply(state, CommandKind.NextItem);

        var synced = ViewerSession.Sync(onB, Queue(Fixtures.Patch("B.cs", 2, null, "b")), [], null);

        await Assert.That(synced.Selected).IsEqualTo(0);
        await Assert.That(synced.Current!.Name).IsEqualTo("B.cs:2");
    }

    /// <summary>
    /// Every refresh parses fresh patch instances off the wire, so an entry has to be recognised
    /// by value. Otherwise the whole queue is re-diffed five times a second and the reader is
    /// thrown back to the top of the pane each time.
    /// </summary>
    [Test]
    public async Task SyncKeepsTheScrollWhenNothingChanged()
    {
        var state = Fixtures.Inline(Fixtures.Patch("A.cs", 1, null, Fixtures.Long(true)));
        var scrolled = ViewerSession.Apply(state, CommandKind.PageDown);
        await Assert.That(scrolled.ScrollTop).IsGreaterThan(0);

        var synced = ViewerSession.Sync(scrolled, Queue(Fixtures.Patch("A.cs", 1, null, Fixtures.Long(true))), [], null);

        await Assert.That(synced.ScrollTop).IsEqualTo(scrolled.ScrollTop);
        await Assert.That(synced.Queue[0]).IsSameReferenceAs(scrolled.Queue[0]);
    }

    [Test]
    public async Task SyncClosesOnAnEmptyQueue()
    {
        var state = Fixtures.Inline(Fixtures.Patch());

        var synced = ViewerSession.Sync(state, InlineQueue.Empty, [], "Accepted 1");

        await Assert.That(synced.Queue).IsEmpty();
        await Assert.That(synced.Exit).IsTrue();
        await Assert.That(synced.Message).IsEqualTo("Accepted 1");
    }

    static InlineQueue Queue(params InlinePatch[] patches) =>
        patches.Aggregate(InlineQueue.Empty, (queue, patch) => queue.Enqueue(patch));

    [Test]
    public async Task QuitExitsWithoutTouchingTheQueue()
    {
        var state = Fixtures.Inline(Fixtures.Patch());

        var quit = ViewerSession.Apply(state, CommandKind.Quit);

        await Assert.That(quit.Exit).IsTrue();
        await Assert.That(quit.Queue.Count).IsEqualTo(1);
    }

    /// <summary>
    /// Grouping keeps a solution's entries contiguous, so an arrival for the first solution slots
    /// beside its siblings rather than appending at the end.
    /// </summary>
    [Test]
    public async Task EnqueueKeepsSolutionsContiguous()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 1),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 2),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "OtherTests.cs"), 3));

        await Assert.That(state.Queue.Select(_ => _.Name))
            .IsEquivalentTo(["ATests.cs:1", "OtherTests.cs:3", "BTests.cs:2"]);
    }

    /// <summary>
    /// The reorder must not move the reader: selection follows its key through the shuffle.
    /// </summary>
    [Test]
    public async Task SelectionFollowsItsKeyWhenGroupingReorders()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 1),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 2));
        var onB = ViewerSession.Apply(state, CommandKind.NextItem);
        await Assert.That(onB.Current!.Name).IsEqualTo("BTests.cs:2");

        var arrived = ViewerSession.EnqueueInline(
            onB,
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "OtherTests.cs"), 3));

        await Assert.That(arrived.Current!.Name).IsEqualTo("BTests.cs:2");
    }

    /// <summary>
    /// A test's several changes coalesce at its first member's position, which is what the test
    /// sub-header renders over.
    /// </summary>
    [Test]
    public async Task ATestsChangesCoalesce()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch("SampleTests.cs", 10, testName: "Compare handles nulls"),
            Fixtures.Patch("OtherTests.cs", 20),
            Fixtures.Patch("SampleTests.cs", 30, testName: "Compare handles nulls"));

        await Assert.That(state.Queue.Select(_ => _.Name))
            .IsEquivalentTo(["SampleTests.cs:10", "SampleTests.cs:30", "OtherTests.cs:20"]);
    }

    [Test]
    public async Task HeaderRowsMapToNoEntry()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 1),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 2));

        var rows = QueueProjection.Rows(state);

        await Assert.That(rows.Where(_ => _.Kind == QueueRowKind.Header).Select(_ => _.EntryIndex))
            .IsEquivalentTo([-1, -1]);
        await Assert.That(rows.Where(_ => _.Kind == QueueRowKind.Entry).Select(_ => _.EntryIndex))
            .IsEquivalentTo([0, 1]);
    }

    static SessionState Conflicted() =>
        Fixtures.Inline(
            Fixtures.Patch(content: "eight", framework: "net8.0"),
            Fixtures.Patch(content: "nine", framework: "net9.0"));

    [Test]
    public async Task NextVariantCyclesAndWraps()
    {
        var state = Conflicted();
        await Assert.That(state.Current!.LeftHeader).IsEqualTo("received (net8.0)");

        var second = ViewerSession.Apply(state, CommandKind.NextVariant);
        await Assert.That(second.Current!.LeftHeader).IsEqualTo("received (net9.0)");
        await Assert.That(second.Current.LeftText).IsEqualTo("nine");

        var wrapped = ViewerSession.Apply(second, CommandKind.NextVariant);
        await Assert.That(wrapped.Current!.LeftHeader).IsEqualTo("received (net8.0)");
    }

    [Test]
    public async Task NextVariantWithoutAConflictIsIgnored()
    {
        var state = Fixtures.Inline(Fixtures.Patch());

        await Assert.That(ViewerSession.Apply(state, CommandKind.NextVariant)).IsSameReferenceAs(state);
    }

    [Test]
    public async Task AcceptAppliesTheVariantOnScreen()
    {
        var applied = new List<InlinePatch>();
        var second = ViewerSession.Apply(Conflicted(), CommandKind.NextVariant);

        var accepted = ViewerSession.Apply(
            second,
            CommandKind.Accept,
            new(patch =>
            {
                applied.Add(patch);
                return InlineApplyResult.Applied;
            }, static (_, _) =>
            {
            }, static _ =>
            {
            }));

        await Assert.That(applied.Single().NewContent).IsEqualTo("nine");
        await Assert.That(accepted.Queue).IsEmpty();
    }

    [Test]
    public async Task AcceptAllSkipsConflictedEntries()
    {
        var state = ViewerSession.EnqueueInline(Conflicted(), Fixtures.Patch("OtherTests.cs", 7));

        var accepted = ViewerSession.Apply(state, CommandKind.AcceptAll, Fixtures.Applied);

        await Assert.That(accepted.Queue.Select(_ => _.Name)).IsEquivalentTo(["SampleTests.cs:42"]);
        await Assert.That(accepted.Message).IsEqualTo("Accepted 1, 1 conflict needs review");
    }

    /// <summary>
    /// What the reader cycled to survives a refresh that changed nothing, exactly as the scroll
    /// does.
    /// </summary>
    [Test]
    public async Task SyncPreservesTheSelectedVariant()
    {
        var second = ViewerSession.Apply(Conflicted(), CommandKind.NextVariant);
        var pending = Fixtures.Pending(
            Fixtures.Patch(content: "eight", framework: "net8.0"),
            Fixtures.Patch(content: "nine", framework: "net9.0"));

        var synced = ViewerSession.Sync(second, pending, [], null);

        await Assert.That(synced.Current!.SelectedVariant).IsEqualTo(1);
        await Assert.That(synced.Current.LeftHeader).IsEqualTo("received (net9.0)");
    }

    [Test]
    public async Task SyncClampsTheSelectedVariantWhenTheListShrinks()
    {
        var second = ViewerSession.Apply(Conflicted(), CommandKind.NextVariant);
        var pending = Fixtures.Pending(Fixtures.Patch(content: "eight", framework: "net8.0"));

        var synced = ViewerSession.Sync(second, pending, [], null);

        await Assert.That(synced.Current!.SelectedVariant).IsEqualTo(0);
        await Assert.That(synced.Current.Conflicted).IsFalse();
    }

    [Test]
    public async Task SyncMaterializesMovesAndDeletes()
    {
        var state = Fixtures.Attached(
            Fixtures.Pending(Fixtures.Patch()),
            Fixtures.Move(),
            Fixtures.Delete());

        await Assert.That(state.Queue.Select(_ => _.Kind))
            .IsEquivalentTo([QueueEntryKind.Inline, QueueEntryKind.Move, QueueEntryKind.Delete]);
    }

    /// <summary>
    /// A delete is the file against nothing, so every content row reads as removed.
    /// </summary>
    [Test]
    public async Task ADeleteShowsRemovalRows()
    {
        var state = Fixtures.Attached(InlineQueue.Empty, Fixtures.Delete());

        var entry = state.Queue.Single();
        await Assert.That(entry.LeftHeader).IsEqualTo("(deleted)");
        await Assert.That(entry.LeftText).IsEmpty();
        // Every content line reads as going away: nothing against the file.
        await Assert.That(entry.RightRows
                .Where(_ => _.LineNumber is not null)
                .All(_ => _.Kind != RowKind.Unchanged))
            .IsTrue();
    }

    /// <summary>
    /// An attached viewer now has a reason to stay open with no snapshots: the tray's moves and
    /// deletes are still reviewable.
    /// </summary>
    [Test]
    public async Task SyncWithOnlyChangesStaysOpen()
    {
        var state = Fixtures.Attached(InlineQueue.Empty, Fixtures.Delete());

        await Assert.That(state.Exit).IsFalse();
        await Assert.That(state.Queue).HasSingleItem();
    }

    [Test]
    public async Task RightClickOnAnEntrySelectsAndOpensItsMenu()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch(),
            Fixtures.Patch("OtherTests.cs", 7));

        var open = ViewerSession.OpenMenu(state, 1);

        await Assert.That(open.Current!.Name).IsEqualTo("OtherTests.cs:7");
        await Assert.That(open.Menu!.Items.Select(_ => _.Label))
            .IsEquivalentTo(["Accept", "Discard", "Open source file"]);
    }

    [Test]
    public async Task AConflictedEntryMenuOffersTheVariant()
    {
        var open = ViewerSession.OpenMenu(Conflicted(), 0);

        await Assert.That(open.Menu!.Items.Select(_ => _.Label))
            .IsEquivalentTo(["Accept", "Show next variant", "Discard", "Open source file"]);
    }

    [Test]
    public async Task MoveAndDeleteMenusNameTheActs()
    {
        var state = Fixtures.Attached(InlineQueue.Empty, Fixtures.Move(), Fixtures.Delete());

        var move = ViewerSession.OpenMenu(state, 0);
        await Assert.That(move.Menu!.Items.Select(_ => _.Label))
            .IsEquivalentTo(["Accept move", "Discard", "Open target directory"]);

        var delete = ViewerSession.OpenMenu(state, 1);
        await Assert.That(delete.Menu!.Items.Select(_ => _.Label))
            .IsEquivalentTo(["Accept delete", "Discard", "Open directory"]);
    }

    static SessionState TwoSolutions() =>
        Fixtures.Inline(
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 1),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "OtherTests.cs"), 2, "\"a\"", "b"),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 3, "\"x\"", "y"));

    /// <summary>
    /// A solution header's menu sweeps that solution and nothing else.
    /// </summary>
    [Test]
    public async Task ASolutionHeaderMenuSweepsItsGroup()
    {
        var open = ViewerSession.OpenMenu(TwoSolutions(), 0);
        await Assert.That(open.Menu!.Items.Select(_ => _.Label))
            .IsEquivalentTo(["Collapse", "Accept all in SolutionA", "Discard all in SolutionA"]);

        var accepted = ViewerSession.Apply(open, CommandKind.AcceptGroup, Fixtures.Applied);

        await Assert.That(accepted.Queue.Select(_ => _.Name)).IsEquivalentTo(["BTests.cs:3"]);
        await Assert.That(accepted.Message).IsEqualTo("Accepted 2");
        await Assert.That(accepted.Menu).IsNull();
    }

    [Test]
    public async Task ATestHeaderMenuSweepsItsTest()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch("SampleTests.cs", 10, testName: "Compare handles nulls"),
            Fixtures.Patch("SampleTests.cs", 30, "\"a\"", "b", testName: "Compare handles nulls"),
            Fixtures.Patch("OtherTests.cs", 7));

        var open = ViewerSession.OpenMenu(state, 0);
        await Assert.That(open.Menu!.Items.Select(_ => _.Label))
            .IsEquivalentTo(["Collapse", "Accept all for Compare handles nulls", "Discard all for Compare handles nulls"]);

        var discarded = ViewerSession.Apply(open, CommandKind.DiscardGroup, Fixtures.Applied);

        await Assert.That(discarded.Queue.Select(_ => _.Name)).IsEquivalentTo(["OtherTests.cs:7"]);
        await Assert.That(discarded.Message).IsEqualTo("Discarded 2");
    }

    [Test]
    public async Task AcceptGroupSkipsConflictedMembers()
    {
        var state = ViewerSession.EnqueueInline(
            ViewerSession.EnqueueInline(Conflicted(), Fixtures.Patch("OtherTests.cs", 7)),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 1));
        // Row 0 is the headerless trailing group's first row only when a named solution exists;
        // open on the ungrouped entries via their rows.
        var rows = QueueProjection.Rows(state);
        var conflictedRow = rows.ToList().FindIndex(_ => _.Label.Contains('*'));
        var header = ViewerSession.OpenMenu(state, 0);
        // Second, because folding leads a header's menu.
        await Assert.That(header.Menu!.Items[1].Label).IsEqualTo("Accept all in SolutionA");
        await Assert.That(conflictedRow).IsGreaterThanOrEqualTo(0);

        // Sweep the ungrouped section instead: build a menu over all entries via accept-all is
        // covered elsewhere; here the conflicted member must survive a group sweep.
        var all = header with
        {
            Menu = new(0, ContextMenu.ForSolution("everything", false), [.. Enumerable.Range(0, state.Queue.Count)])
        };
        var accepted = ViewerSession.Apply(all, CommandKind.AcceptGroup, Fixtures.Applied);

        await Assert.That(accepted.Message).IsEqualTo("Accepted 2, 1 conflict needs review");
        await Assert.That(accepted.Queue.Single().Conflicted).IsTrue();
    }

    [Test]
    public async Task RevealActsOnTheCurrentEntry()
    {
        var revealed = new List<string>();
        var actions = new ViewerActions(
            _ => InlineApplyResult.Applied,
            static (_, _) =>
            {
            },
            revealed.Add);
        var state = Fixtures.Attached(Fixtures.Pending(Fixtures.Patch()), Fixtures.Move());

        ViewerSession.Apply(state, CommandKind.RevealSource, actions);
        var onMove = ViewerSession.Apply(state, CommandKind.NextItem);
        ViewerSession.Apply(onMove, CommandKind.RevealSource, actions);

        await Assert.That(revealed).IsEquivalentTo(["SampleTests.cs", "code/sample.verified.txt"]);
    }

    [Test]
    public async Task AnyOtherCommandClosesTheMenu()
    {
        var open = ViewerSession.OpenMenu(Fixtures.Inline(Fixtures.Patch()), 0);
        await Assert.That(open.Menu).IsNotNull();

        var scrolled = ViewerSession.Apply(open, CommandKind.ScrollDown);

        await Assert.That(scrolled.Menu).IsNull();
    }

    /// <summary>
    /// Including dragging the scrollbar, which is a command like any other.
    /// </summary>
    [Test]
    public async Task ScrollToClosesTheMenu()
    {
        var open = ViewerSession.OpenMenu(Fixtures.Inline(Fixtures.Patch()), 0);

        var scrolled = ViewerSession.Apply(open, Command.Scroll(3));

        await Assert.That(scrolled.Menu).IsNull();
    }

    [Test]
    public async Task AQueueChangeClosesTheMenu()
    {
        var open = ViewerSession.OpenMenu(Fixtures.Inline(Fixtures.Patch()), 0);

        var synced = ViewerSession.Sync(
            open,
            Fixtures.Pending(Fixtures.Patch(), Fixtures.Patch("OtherTests.cs", 7)),
            [],
            null);

        await Assert.That(synced.Menu).IsNull();
    }

    /// <summary>
    /// Owner-mode operations rebuild the inline queue from the display list, and tracked entries
    /// must never leak into it.
    /// </summary>
    [Test]
    public async Task AnInlineAcceptIgnoresChangeEntries()
    {
        var state = Fixtures.Attached(Fixtures.Pending(Fixtures.Patch()), Fixtures.Move());

        var accepted = ViewerSession.Apply(state, CommandKind.Accept, Fixtures.Applied);

        // The move entry is display state from the owner; accepting the inline entry must not
        // count it as pending.
        await Assert.That(accepted.Message).IsEqualTo("Applied SampleTests.cs:42");
    }
}
