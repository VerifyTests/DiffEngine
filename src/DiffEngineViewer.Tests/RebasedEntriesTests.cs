/// <summary>
/// Accepting a snapshot moves every call site under it in its file, and the entries still pending
/// for that file go with them (<see cref="InlineQueue.Rebased"/>). The list is edited where it
/// stands: an entry at another line is the same texts under another key.
/// </summary>
public class RebasedEntriesTests
{
    /// <summary>
    /// The literal accepted at line 42 is five lines longer, so everything under it is five
    /// lines down.
    /// </summary>
    static InlineApplyResult Grew =>
        InlineApplyResult.AppliedMoving(43, 5);

    static SessionState ThreeInOneFile() =>
        Fixtures.Inline(
            Fixtures.Patch(),
            Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two"),
            Fixtures.Patch("OtherTests.cs", 88, "\"one\"", "two"),
            Fixtures.Patch("SampleTests.cs", 90, "\"three\"", "four"));

    [Test]
    public async Task AnAcceptTakesTheFilesOtherEntriesToWhereTheirCallSitesAre()
    {
        var state = ThreeInOneFile();
        var before = state.Queue;

        var after = ViewerSession.Apply(state, CommandKind.Accept, Fixtures.Applying(Grew));

        await Assert.That(after.Queue.Select(_ => _.Name))
            .IsEquivalentTo(["SampleTests.cs:93", "OtherTests.cs:88", "SampleTests.cs:95"]);
        var moved = after.Queue.Single(_ => _.Name == "SampleTests.cs:93");
        await Assert.That(moved.Key).IsEqualTo(InlineKey.For("SampleTests.cs", 93));
        await Assert.That(moved.Patch!.LineHint).IsEqualTo(93);
        await Assert.That(moved.Variants.Single().Patch).IsSameReferenceAs(moved.Patch);
        // The same diff, not one made again
        await Assert.That(moved.LeftRows)
            .IsSameReferenceAs(before.Single(_ => _.Name == "SampleTests.cs:88").LeftRows);
        // And another file's entry is the entry it was
        await Assert.That(after.Queue.Single(_ => _.Name == "OtherTests.cs:88"))
            .IsSameReferenceAs(before.Single(_ => _.Name == "OtherTests.cs:88"));
    }

    /// <summary>
    /// The owner is asked to accept by key, from the tray or an attached window, while the
    /// reader is on another entry of the same file. That entry is still the one being read.
    /// </summary>
    [Test]
    public async Task TheEntryBeingReadIsStillSelectedAfterItMoves()
    {
        var actions = Fixtures.Applied with
        {
            ApplyInlineTogether = _ => _.Select(patch => patch.LineHint == 42 ? Grew : InlineApplyResult.Failed("held")).ToList()
        };
        var state = ViewerSession.SelectKey(ThreeInOneFile(), InlineKey.For("SampleTests.cs", 90));

        var after = ViewerSession.Apply(state, CommandKind.AcceptAll, actions);

        await Assert.That(after.Current!.Name).IsEqualTo("SampleTests.cs:95");
        await Assert.That(after.Current.Status).IsEqualTo("held");
    }

    /// <summary>
    /// A batch finds what it claimed by its variants, so the entries are taken along once every
    /// outcome of the file is in. What is left of the file is a snapshot that was not written and
    /// a conflict the batch passed over, and both are where their call sites now are.
    /// </summary>
    [Test]
    public async Task ABatchTakesWhatItLeftOfAFileAlong()
    {
        var state = ViewerSession.EnqueueInline(
            Fixtures.Inline(
                Fixtures.Patch(framework: "net8.0"),
                Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two", framework: "net8.0"),
                Fixtures.Patch("SampleTests.cs", 90, "\"three\"", "four", framework: "net8.0"),
                Fixtures.Patch("SampleTests.cs", 99, "\"five\"", "six", framework: "net8.0")),
            Fixtures.Patch("SampleTests.cs", 90, "\"three\"", "nine", framework: "net9.0"));
        var actions = Fixtures.Applied with
        {
            ApplyInlineTogether = _ => _
                .Select(patch => patch.LineHint switch
                {
                    42 => Grew,
                    88 => InlineApplyResult.NotFound("no Verify call"),
                    _ => InlineApplyResult.Applied
                })
                .ToList()
        };

        var after = ViewerSession.Apply(state, CommandKind.AcceptAll, actions);

        await Assert.That(after.Queue.Select(_ => _.Name)).IsEquivalentTo(["SampleTests.cs:93", "SampleTests.cs:95"]);
        await Assert.That(after.Queue.Single(_ => _.Name == "SampleTests.cs:93").Status!).Contains("no Verify call");
        var conflict = after.Queue.Single(_ => _.Name == "SampleTests.cs:95");
        await Assert.That(conflict.Conflicted).IsTrue();
        await Assert.That(conflict.Variants.Select(_ => _.Patch.LineHint)).IsEquivalentTo([95, 95]);
        await Assert.That(after.Message!).StartsWith("Accepted 2, 1 not written, 1 conflict needs review");
    }

    /// <summary>
    /// A group's batch counts the conflicts among its own members when it ends, and knows its
    /// members by key. One that moved is still a member.
    /// </summary>
    [Test]
    public async Task AGroupStillCountsAConflictThatMoved()
    {
        var state = ViewerSession.EnqueueInline(
            Fixtures.Inline(
                Fixtures.Patch(testName: "First", framework: "net8.0"),
                Fixtures.Patch("SampleTests.cs", 90, "\"three\"", "four", testName: "First", framework: "net8.0"),
                Fixtures.Patch("OtherTests.cs", 12, "\"five\"", "six", testName: "Second", framework: "net8.0")),
            Fixtures.Patch("SampleTests.cs", 90, "\"three\"", "nine", testName: "First", framework: "net9.0"));
        var rows = QueueProjection.Visible(state, ScreenBuilder.BodyRows(state), out _).ToList();
        var open = ViewerSession.OpenMenu(state, rows.FindIndex(_ => _.GroupName == "First"));

        var after = ViewerSession.BeginAcceptGroup(open);
        while ((after = ViewerSession.ClaimNext(after)).Batch?.Current is not null)
        {
            after = ViewerSession.ApplyClaimed(after, Fixtures.Applying(Grew))(after);
        }

        await Assert.That(after.Queue.Select(_ => _.Name)).IsEquivalentTo(["SampleTests.cs:95", "OtherTests.cs:12"]);
        await Assert.That(after.Message).IsEqualTo("Accepted 1, 1 conflict needs review");
    }

    /// <summary>
    /// An edit that moved no line changes nothing about the entries beside it, which stay the
    /// instances they were.
    /// </summary>
    [Test]
    public async Task AnAcceptThatMovedNothingLeavesTheOtherEntriesAlone()
    {
        var state = ThreeInOneFile();

        var after = ViewerSession.Apply(state, CommandKind.Accept, Fixtures.Applied);

        await Assert.That(after.Queue.Count).IsEqualTo(3);
        foreach (var entry in after.Queue)
        {
            await Assert.That(state.Queue.Any(_ => ReferenceEquals(_, entry))).IsTrue();
        }
    }
}
