public class InlineScreenTests
{
    [Test]
    public Task Single() =>
        Verify(Fixtures.Render(Fixtures.Inline(Fixtures.Patch())));

    [Test]
    public Task EmptyQueue() =>
        Verify(Fixtures.Render(Fixtures.Inline()));

    [Test]
    public Task NewSnapshot() =>
        Verify(Fixtures.Render(Fixtures.Inline(Fixtures.Patch(expression: null))));

    [Test]
    public Task UnparsedExpression() =>
        Verify(Fixtures.Render(Fixtures.Inline(Fixtures.Patch(expression: "$\"the {value} fox\""))));

    [Test]
    public Task Queue() =>
        Verify(Fixtures.Render(Pending()));

    [Test]
    public Task SecondItemSelected() =>
        Verify(Fixtures.Render(ViewerSession.Apply(Pending(), CommandKind.NextItem)));

    [Test]
    public Task AfterAccept() =>
        Verify(Fixtures.Render(ViewerSession.Apply(Pending(), CommandKind.Accept, Fixtures.Applied)));

    [Test]
    public Task AfterDiscard() =>
        Verify(Fixtures.Render(ViewerSession.Apply(Pending(), CommandKind.Discard)));

    [Test]
    public Task AfterAcceptAll() =>
        Verify(Fixtures.Render(ViewerSession.Apply(Pending(), CommandKind.AcceptAll, Fixtures.Applied)));

    [Test]
    public Task StalePatch()
    {
        var actions = Fixtures.Applying(InlineApplyResult.NotFound("Could not locate the VerifyInline call"));
        return Verify(Fixtures.Render(ViewerSession.Apply(Pending(), CommandKind.Accept, actions)));
    }

    [Test]
    public Task FailedApply()
    {
        var actions = Fixtures.Applying(InlineApplyResult.Failed("Failed to write: SampleTests.cs"));
        return Verify(Fixtures.Render(ViewerSession.Apply(Pending(), CommandKind.Accept, actions)));
    }

    [Test]
    public Task FailedApplyThenAcceptAll()
    {
        var actions = Fixtures.Applying(InlineApplyResult.Failed("Failed to write: SampleTests.cs"));
        var state = ViewerSession.Apply(Pending(), CommandKind.AcceptAll, actions);
        return Verify(Fixtures.Render(state));
    }

    static SessionState Pending() =>
        Fixtures.Inline(
            Fixtures.Patch(),
            Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two"),
            Fixtures.Patch("OtherTests.cs", 12, null, "brand new"),
            Fixtures.Patch("OtherTests.cs", 30, "\"a\"", "b"),
            Fixtures.Patch("WideNamedTests.cs", 501, "\"x\"", "y"));

    /// <summary>
    /// Two solutions get headers with counts; the entries indent beneath them.
    /// </summary>
    [Test]
    public Task TwoSolutions() =>
        Verify(Fixtures.Render(Fixtures.Inline(
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 10),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "OtherTests.cs"), 20, "\"a\"", "b"),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 30, "\"x\"", "y"))));

    /// <summary>
    /// A solution plus entries with no solution: the ungrouped ones trail headerless, the way the
    /// tray menu renders a null group.
    /// </summary>
    [Test]
    public Task SolutionAndUngrouped() =>
        Verify(Fixtures.Render(Fixtures.Inline(
            Fixtures.Patch("SampleTests.cs", 42),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 10, "\"a\"", "b"))));

    /// <summary>
    /// A test that produced several changes gets a sub-header, its entries falling back to call
    /// sites beneath it.
    /// </summary>
    [Test]
    public Task GroupedByTest() =>
        Verify(Fixtures.Render(Fixtures.Inline(
            Fixtures.Patch("SampleTests.cs", 10, testName: "Compare handles nulls"),
            Fixtures.Patch("SampleTests.cs", 30, "\"a\"", "b", testName: "Compare handles nulls"),
            Fixtures.Patch("OtherTests.cs", 12, null, "brand new", testName: "Order is stable"))));

    /// <summary>
    /// A known test name replaces the call-site label when the test stands alone.
    /// </summary>
    [Test]
    public Task TestNameLabels() =>
        Verify(Fixtures.Render(Fixtures.Inline(
            Fixtures.Patch("SampleTests.cs", 42, testName: "Compare handles nulls"),
            Fixtures.Patch("OtherTests.cs", 12, "\"a\"", "b"))));

    /// <summary>
    /// The same file name and line in two projects grows the shortest distinguishing directory
    /// prefix.
    /// </summary>
    [Test]
    public Task CollidingNames() =>
        Verify(Fixtures.Render(Fixtures.Inline(
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "ProjA", "SampleTests.cs"), 42),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "ProjB", "SampleTests.cs"), 42, "\"a\"", "b"))));

    [Test]
    public Task LongSolutionNames() =>
        Verify(Fixtures.Render(Fixtures.Inline(
            Fixtures.Patch(Fixtures.SolutionFile("AVeryLongWindedSolutionName", "Tests", "ATests.cs"), 10),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 30, "\"x\"", "y"))));

    static SessionState Conflicted() =>
        Fixtures.Inline(
            Fixtures.Patch(content: Fixtures.Received, framework: "net8.0"),
            Fixtures.Patch(content: "the quick\nbrown wolf\njumps over\nthe lazy\ndog", framework: "net9.0"),
            Fixtures.Patch("OtherTests.cs", 12, "\"a\"", "b", framework: "net8.0"));

    /// <summary>
    /// A conflicted entry: marked in the queue, its origin in the pane header, and a variant
    /// button showing which of the disagreeing contents is on screen.
    /// </summary>
    [Test]
    public Task ConflictedEntry() =>
        Verify(Fixtures.Render(Conflicted()));

    [Test]
    public Task ConflictedSecondVariant() =>
        Verify(Fixtures.Render(ViewerSession.Apply(Conflicted(), CommandKind.NextVariant)));

    /// <summary>
    /// Two frameworks producing identical content are one entry with both labels and no conflict.
    /// </summary>
    [Test]
    public Task MergedOriginLabels() =>
        Verify(Fixtures.Render(Fixtures.Inline(
            Fixtures.Patch(framework: "net8.0"),
            Fixtures.Patch(framework: "net9.0"))));

    [Test]
    public Task AfterAcceptAllSkipsConflicts() =>
        Verify(Fixtures.Render(ViewerSession.Apply(Conflicted(), CommandKind.AcceptAll, Fixtures.Applied)));

    /// <summary>
    /// A tray move rendered from its two files, with the accept button naming the act.
    /// </summary>
    [Test]
    public Task MoveEntry() =>
        Verify(Fixtures.Render(Fixtures.Attached(InlineQueue.Empty, Fixtures.Move())));

    [Test]
    public Task DeleteEntry() =>
        Verify(Fixtures.Render(Fixtures.Attached(InlineQueue.Empty, Fixtures.Delete())));

    /// <summary>
    /// The whole ecosystem in one queue: snapshots, a move and a delete, grouped by solution.
    /// </summary>
    [Test]
    public Task MixedKindsGrouped() =>
        Verify(Fixtures.Render(Fixtures.Attached(
            Fixtures.Pending(
                Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 10),
                Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "OtherTests.cs"), 20, "\"a\"", "b")),
            Fixtures.Move(solution: "SolutionB"),
            Fixtures.Delete(solution: "SolutionB"))));

    /// <summary>
    /// The context menu, drawn over the frame the way every head draws it, so the offering per
    /// row kind is pinned as text.
    /// </summary>
    [Test]
    public Task MenuOnEntry() =>
        Verify(Fixtures.Render(ViewerSession.OpenMenu(Pending(), 0)));

    [Test]
    public Task MenuOnConflictedEntry() =>
        Verify(Fixtures.Render(ViewerSession.OpenMenu(Conflicted(), 0)));

    [Test]
    public Task MenuOnSolutionHeader() =>
        Verify(Fixtures.Render(ViewerSession.OpenMenu(Fixtures.Inline(
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 10),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "OtherTests.cs"), 20, "\"a\"", "b"),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 30, "\"x\"", "y")), 0)));

    [Test]
    public Task MenuOnMove() =>
        Verify(Fixtures.Render(ViewerSession.OpenMenu(
            Fixtures.Attached(InlineQueue.Empty, Fixtures.Move(), Fixtures.Delete()), 0)));

    /// <summary>
    /// A folded solution: the header keeps its count and its members go. The selection was inside
    /// it, so it has moved to the first entry still on screen.
    /// </summary>
    [Test]
    public Task CollapsedSolution() =>
        Verify(Fixtures.Render(
            ViewerSession.ToggleGroup(
                ViewerSession.Apply(Fixtures.GroupedConflicted(), Command.Select(0)),
                "solution|SolutionA")));

    /// <summary>
    /// A folded test sub-group inside an expanded solution, which is the case that shows both
    /// markers and both indent levels at once.
    /// </summary>
    [Test]
    public Task CollapsedTestGroup()
    {
        var state = Fixtures.GroupedConflicted();
        var key = QueueProjection.Rows(state)
            .Single(_ => _.GroupName == "Compare handles nulls")
            .GroupKey!;
        return Verify(Fixtures.Render(ViewerSession.ToggleGroup(state, key)));
    }

    /// <summary>
    /// More rows than fit: the column slices to keep the selection visible, second from the
    /// bottom once it walks below the fold.
    /// </summary>
    [Test]
    public Task LongQueueScrollsToSelection()
    {
        var state = Fixtures.Inline(
            Enumerable.Range(1, 20)
                .Select(_ => Fixtures.Patch("SampleTests.cs", _, "\"a\"", "b"))
                .ToArray());
        for (var step = 0; step < 17; step++)
        {
            state = ViewerSession.Apply(state, CommandKind.NextItem);
        }

        return Verify(Fixtures.Render(state));
    }
}
