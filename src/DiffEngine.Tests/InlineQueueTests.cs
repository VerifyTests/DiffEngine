/// <summary>
/// The queue half of what ViewerSession used to do, now that both the tray and the viewer host it.
/// These are the behaviours that must not differ between the two, which is the whole reason the
/// queue was extracted rather than reimplemented.
/// </summary>
public class InlineQueueTests
{
    static InlinePatch Patch(
        string source = "Sample.cs",
        int line = 42,
        string content = "new",
        string? framework = null,
        string? testName = null,
        string? member = null,
        string expression = "\"old\"") =>
        new(source, line, expression, content)
        {
            Framework = framework,
            TestName = testName,
            MemberName = member
        };

    static InlineApplyResult Fails(InlinePatch patch) =>
        InlineApplyResult.Failed("locked");

    [Test]
    public async Task EnqueueAppends()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1))
            .Enqueue(Patch("B.cs", 2));

        await Assert.That(queue.Items.Select(_ => _.Name)).IsEquivalentTo(["A.cs:1", "B.cs:2"]);
    }

    /// <summary>
    /// A re-run of the same failing test must update its entry, not stack up duplicates.
    /// </summary>
    [Test]
    public async Task EnqueueReplacesTheSameCallSite()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "first"))
            .Enqueue(Patch(content: "second"));

        await Assert.That(queue.Count).IsEqualTo(1);
        await Assert.That(queue.Items[0].Patch.NewContent).IsEqualTo("second");
    }

    /// <summary>
    /// The same path can reach here with different casing, and where the file system says those
    /// are one file it is still one call site.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task EnqueueMatchesRegardlessOfPathCase()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch())
            .Enqueue(Patch("SAMPLE.CS"));

        await Assert.That(queue.Count).IsEqualTo(1);
    }

    /// <summary>
    /// And where it says they are two files, two call sites. Matching them everywhere gave both
    /// one entry: the second patch replaced the first, and settling either settled both.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Linux)]
    public async Task EnqueueKeepsPathCaseApartWhereTheFilesDo()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch())
            .Enqueue(Patch("SAMPLE.CS"));

        await Assert.That(queue.Count).IsEqualTo(2);
    }

    /// <summary>
    /// Three tests in one file with a snapshot pending each. Accepting the first writes its
    /// literal into the source, and the re-run reports the other two from five lines further down.
    /// Their keys name nothing by then, so each was queued a second time beside the entry it
    /// should have updated: four entries for two snapshots, the stale one of each first in line
    /// for a bulk accept.
    /// </summary>
    [Test]
    public async Task ARerunFromWhereItsCallSiteMovedToUpdatesItsEntry()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 10, content: "a", member: "TestA"))
            .Enqueue(Patch(line: 20, content: "b", member: "TestB"))
            .Enqueue(Patch(line: 30, content: "c", member: "TestC"))
            .Accept(InlineKey.For("Sample.cs", 10), _ => InlineApplyResult.Applied, out _)
            .Enqueue(Patch(line: 25, content: "b, as it is now", member: "TestB"))
            .Enqueue(Patch(line: 35, content: "c", member: "TestC"));

        await Assert.That(queue.Items.Select(_ => $"{_.Name} {_.Patch.NewContent}")).IsEquivalentTo(
        [
            "Sample.cs:25 b, as it is now",
            "Sample.cs:35 c"
        ]);
    }

    /// <summary>
    /// Every variant goes to the new line, not only the one the re-run came from. Left where they
    /// were, the same content from two frameworks no longer matched - a patch is compared line and
    /// all - and an entry that had agreed with itself turned into a conflict.
    /// </summary>
    [Test]
    public async Task AMovedEntryTakesEveryFrameworksVariantWithIt()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 20, content: "eight", framework: "net8.0", member: "TestB"))
            .Enqueue(Patch(line: 20, content: "nine", framework: "net9.0", member: "TestB"))
            .Enqueue(Patch(line: 25, content: "nine", framework: "net8.0", member: "TestB"));

        var entry = queue.Items.Single();
        await Assert.That(entry.Key).IsEqualTo(InlineKey.For("Sample.cs", 25));
        await Assert.That(entry.Conflicted).IsFalse();
        await Assert.That(entry.Variants[0].Origins).IsEquivalentTo(["net9.0", "net8.0"]);
        await Assert.That(entry.Variants[0].Patch.LineHint).IsEqualTo(25);
    }

    /// <summary>
    /// The patches a queue holds are shared with whatever is displaying them, so an entry that
    /// moves is given copies and the ones it held are left saying what they said.
    /// </summary>
    [Test]
    public async Task AMovedEntryLeavesThePatchItHeldAlone()
    {
        var first = Patch(line: 20, content: "b", member: "TestB");

        var queue = InlineQueue.Empty
            .Enqueue(first)
            .Enqueue(Patch(line: 25, content: "b", member: "TestB"));

        await Assert.That(queue.Items.Single().Patch.LineHint).IsEqualTo(25);
        await Assert.That(first.LineHint).IsEqualTo(20);
    }

    /// <summary>
    /// Two entries that read alike from here: one member, the same literal in the source at both.
    /// Nothing says which of them a patch from a third line is, and folding into the wrong one
    /// replaces a snapshot that is still pending, so it is queued beside them, as it always was.
    /// </summary>
    [Test]
    public async Task ARerunFromAnotherLineIsQueuedBesideEntriesItCannotTellApart()
    {
        var queue = InlineQueue
            .From(
            [
                new(Patch(line: 20, content: "first", member: "MyTest")),
                new(Patch(line: 30, content: "second", member: "MyTest"))
            ])
            .Enqueue(Patch(line: 25, content: "first", member: "MyTest"));

        await Assert.That(queue.Count).IsEqualTo(3);
    }

    /// <summary>
    /// One framework stopped at the first call of a test, and another passed that one and stopped
    /// at the second, which holds the same literal. Two call sites, and from here they differ only
    /// by line and by who reported them: taking the second for the first one moved would put the
    /// first framework's snapshot on the second call.
    /// </summary>
    [Test]
    public async Task ARerunDoesNotMoveAnEntryAnotherFrameworkQueued()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 20, content: "eight", framework: "net8.0", member: "MyTest"))
            .Enqueue(Patch(line: 25, content: "nine", framework: "net9.0", member: "MyTest"));

        await Assert.That(queue.Items.Select(_ => $"{_.Name} {_.OriginsLabel}")).IsEquivalentTo(
        [
            "Sample.cs:20 net8.0",
            "Sample.cs:25 net9.0"
        ]);
    }

    /// <summary>
    /// A member is a name, and one file can declare it twice: a class per scenario, each with its
    /// own Works. The test name is what says the second patch is not the first one moved.
    /// </summary>
    [Test]
    public async Task ARerunDoesNotTakeTheEntryOfTheSameMemberInAnotherClass()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 10, content: "a", testName: "First.Works", member: "Works"))
            .Enqueue(Patch(line: 40, content: "b", testName: "Second.Works", member: "Works"));

        await Assert.That(queue.Items.Select(_ => _.Name)).IsEquivalentTo(["Sample.cs:10", "Sample.cs:40"]);
    }

    // Nor of the same member in another file, which a base class and its partial make ordinary
    [Test]
    public async Task ARerunDoesNotTakeTheEntryOfTheSameMemberInAnotherFile()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 10, content: "a", member: "Works"))
            .Enqueue(Patch("B.cs", 40, content: "b", member: "Works"));

        await Assert.That(queue.Items.Select(_ => _.Name)).IsEquivalentTo(["A.cs:10", "B.cs:40"]);
    }

    /// <summary>
    /// The move that put one call site on the line another was queued under. Both entries are
    /// stale by the same accept, and the first re-run to arrive lands on the other one's key.
    /// Folded into it, the other test's snapshot was replaced by this one's, and this one's own
    /// entry was left behind stale. It updates its own instead, and keeps the key it had until the
    /// other entry has moved off the line.
    /// </summary>
    [Test]
    public async Task ARerunOntoALineAnotherMembersEntryIsUnderLeavesThatEntryAlone()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 20, content: "b", member: "TestB"))
            .Enqueue(Patch(line: 30, content: "c", member: "TestC"))
            .Enqueue(Patch(line: 30, content: "b, as it is now", member: "TestB"));

        await Assert.That(queue.Items.Select(_ => $"{_.Name} {_.Patch.MemberName} {_.Patch.NewContent}")).IsEquivalentTo(
        [
            "Sample.cs:20 TestB b, as it is now",
            "Sample.cs:30 TestC c"
        ]);

        // The other test's re-run moves its entry off the line, and the next run of this one
        // finds the line free
        queue = queue
            .Enqueue(Patch(line: 40, content: "c", member: "TestC"))
            .Enqueue(Patch(line: 30, content: "b, as it is now", member: "TestB"));

        await Assert.That(queue.Items.Select(_ => $"{_.Name} {_.Patch.MemberName}")).IsEquivalentTo(
        [
            "Sample.cs:30 TestB",
            "Sample.cs:40 TestC"
        ]);
    }

    /// <summary>
    /// A patch with no entry of its own, arriving at a line another member's entry is under. The
    /// two are not one call site, and folding them said they were: a second framework's patch for
    /// one test became a conflicting variant of another test's snapshot.
    /// </summary>
    [Test]
    public async Task APatchFromAnotherMemberTakesTheLineRatherThanJoiningTheEntry()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 30, content: "c", framework: "net8.0", member: "TestC"))
            .Enqueue(Patch(line: 30, content: "x", framework: "net9.0", member: "TestX"));

        var entry = queue.Items.Single();
        await Assert.That(entry.Conflicted).IsFalse();
        await Assert.That(entry.Patch.MemberName).IsEqualTo("TestX");
    }

    [Test]
    public async Task SettleRemoves()
    {
        var patch = Patch();
        var queue = InlineQueue.Empty.Enqueue(patch).Settle(InlineKey.For("Sample.cs", 42));

        await Assert.That(queue.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Returning the same instance is how a host tells that nothing changed, and so avoids
    /// treating a settle for something it never had as an emptied queue.
    /// </summary>
    [Test]
    public async Task SettleForAnUnknownKeyReturnsTheSameQueue()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch());

        await Assert.That(queue.Settle("nothing")).IsSameReferenceAs(queue);
    }

    /// <summary>
    /// Accepting a snapshot inserts several lines of source, so every later call site in that file
    /// moves and the entries queued against them can never be named by their line again. The
    /// member is what still points at them.
    /// </summary>
    [Test]
    public async Task SettleFindsAnEntryWhoseLineHasMovedByMember()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 42, member: "MyTest"))
            .Settle(InlineKey.For("Sample.cs", 807), null, "MyTest");

        await Assert.That(queue.Count).IsEqualTo(0);
    }

    /// <summary>
    /// One member, two Snapshot calls: line 20 fails and is queued, line 10 passes. The passing
    /// call's settle names no entry by its line, and the member names exactly one, the failing
    /// sibling's. The value it carries is what tells them apart: the passing call holds neither
    /// the failing entry's anchor nor its new content.
    /// </summary>
    [Test]
    public async Task SettleFromAPassingSiblingKeepsTheFailingSiblingsEntry()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch(line: 20, framework: "net10.0", member: "MyTest"));

        var settled = queue.Settle(InlineKey.For("Sample.cs", 10), "net10.0", "MyTest", "what the sibling holds");

        await Assert.That(settled).IsSameReferenceAs(queue);
    }

    /// <summary>
    /// After an accept higher in the file, a passing call sits on the line a later test's entry
    /// was queued under. Its settle names that entry by key and nothing checked whose it was, so a
    /// snapshot that was still failing left the queue.
    /// </summary>
    [Test]
    public async Task SettleFromAnotherMemberAtAnEntrysLineLeavesTheEntry()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch(line: 20, framework: "net10.0", member: "TestB"));

        var settled = queue.Settle(InlineKey.For("Sample.cs", 20), "net10.0", "TestA", "what the passing call holds");

        await Assert.That(settled).IsSameReferenceAs(queue);
    }

    /// <summary>
    /// And the entry the settle was for is still found, by its member, under the line it was
    /// queued at before the move.
    /// </summary>
    [Test]
    public async Task SettleFromAnotherMemberAtAnEntrysLineStillSettlesItsOwn()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 15, content: "a", member: "TestA", expression: "\"was a\""))
            .Enqueue(Patch(line: 20, content: "b", member: "TestB"));

        var settled = queue.Settle(InlineKey.For("Sample.cs", 20), null, "TestA", "was a");

        await Assert.That(settled.Items.Single().Patch.MemberName).IsEqualTo("TestB");
    }

    /// <summary>
    /// A test renamed while its snapshot was pending: the member no longer matches, the line does,
    /// and the call passes holding what the entry was waiting to become.
    /// </summary>
    [Test]
    public async Task SettleFromARenamedMemberTakesTheEntryItsValueSettles()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 20, content: "new", member: "OldName"))
            .Settle(InlineKey.For("Sample.cs", 20), null, "NewName", "new");

        await Assert.That(queue.Count).IsEqualTo(0);
    }

    /// <summary>
    /// The moved line again, with the value the settle now carries. Passing with the value it was
    /// anchored to is the code under test producing that again.
    /// </summary>
    [Test]
    public async Task SettleByMemberTakesAnEntryAnchoredToTheValue()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 42, member: "MyTest"))
            .Settle(InlineKey.For("Sample.cs", 807), null, "MyTest", "old");

        await Assert.That(queue.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Passing with the value the entry was waiting to be accepted as: written in by hand, or by
    /// another surface.
    /// </summary>
    [Test]
    public async Task SettleByMemberTakesAnEntryWaitingForTheValue()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 42, member: "MyTest"))
            .Settle(InlineKey.For("Sample.cs", 807), null, "MyTest", "new");

        await Assert.That(queue.Count).IsEqualTo(0);
    }

    /// <summary>
    /// A value also picks out one entry of several in a member, which the member alone cannot.
    /// </summary>
    [Test]
    public async Task SettleByMemberWithAValueChoosesAmongSeveral()
    {
        // Two literals, since two calls in one member anchored to the same one are a single call
        // site to the queue: see ARerunFromWhereItsCallSiteMovedToUpdatesItsEntry
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 42, content: "first", member: "MyTest", expression: "\"one\""))
            .Enqueue(Patch(line: 48, content: "second", member: "MyTest", expression: "\"two\""))
            .Settle(InlineKey.For("Sample.cs", 807), null, "MyTest", "second");

        await Assert.That(queue.Items.Single().Patch.NewContent).IsEqualTo("first");
    }

    /// <summary>
    /// The value crosses the wire, and an owner that predates it reads past it.
    /// </summary>
    [Test]
    public async Task ASettleValueRoundTrips()
    {
        var message = new ViewerMessage(ViewerVerb.Settle, InlineKey.For("Sample.cs", 1), "net10.0", "MyTest", "line one\nline \"two\"");

        await Assert.That(ViewerMessage.TryParse(message.Build(), out var parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(message);
    }

    /// <summary>
    /// A member holding several inline snapshots cannot say which of them the settle was for, and
    /// dropping the wrong one loses a pending snapshot outright.
    /// </summary>
    [Test]
    public async Task SettleLeavesAnAmbiguousMemberAlone()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 42, member: "MyTest", expression: "\"one\""))
            .Enqueue(Patch(line: 48, member: "MyTest", expression: "\"two\""));

        await Assert.That(queue.Settle(InlineKey.For("Sample.cs", 807), null, "MyTest"))
            .IsSameReferenceAs(queue);
    }

    [Test]
    public async Task SettleDoesNotMatchTheSameMemberInAnotherFile()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch(source: "Sample.cs", member: "MyTest"));

        await Assert.That(queue.Settle(InlineKey.For("Other.cs", 807), null, "MyTest"))
            .IsSameReferenceAs(queue);
    }

    [Test]
    public async Task SettleWithoutAMemberStillOnlyMatchesTheKey()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch(line: 42, member: "MyTest"));

        await Assert.That(queue.Settle(InlineKey.For("Sample.cs", 807))).IsSameReferenceAs(queue);
    }

    /// <summary>
    /// A call site that is no longer inline at all carries no framework, because the statement is
    /// not "this framework now passes" but "there is no snapshot here for any of them". So it
    /// takes the whole entry, not one variant of it.
    /// </summary>
    [Test]
    public async Task SettleWithoutAnOriginTakesEveryVariant()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "eight", framework: "net8.0", member: "MyTest"))
            .Enqueue(Patch(content: "nine", framework: "net9.0", member: "MyTest"))
            .Settle(InlineKey.For("Sample.cs", 807), null, "MyTest");

        await Assert.That(queue.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AcceptAppliesAndRemoves()
    {
        var applied = new List<InlinePatch>();
        var queue = InlineQueue.Empty
            .Enqueue(Patch())
            .Accept(InlineKey.For("Sample.cs", 42), _ =>
            {
                applied.Add(_);
                return InlineApplyResult.Applied;
            }, out var message);

        await Assert.That(queue.Count).IsEqualTo(0);
        await Assert.That(applied).HasSingleItem();
        await Assert.That(message).IsEqualTo("Applied Sample.cs:42");
    }

    /// <summary>
    /// The source moved on, so the patch can never succeed. Dropped rather than left as an item
    /// that will fail forever.
    /// </summary>
    [Test]
    public async Task AcceptDropsAStalePatch()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch())
            .Accept(InlineKey.For("Sample.cs", 42), _ => InlineApplyResult.NotFound("gone"), out var message);

        await Assert.That(queue.Count).IsEqualTo(0);
        await Assert.That(message).IsEqualTo("Sample.cs:42 not written. gone");
    }

    /// <summary>
    /// A failure is retryable, so the entry stays and carries what went wrong.
    /// </summary>
    [Test]
    public async Task AcceptKeepsAFailureWithItsStatus()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch())
            .Accept(InlineKey.For("Sample.cs", 42), Fails, out var message);

        await Assert.That(queue.Count).IsEqualTo(1);
        await Assert.That(queue.Items[0].Status).IsEqualTo("locked");
        await Assert.That(message).IsEqualTo("locked");
    }

    [Test]
    public async Task AcceptForAnUnknownKeyDoesNothing()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch());
        var after = queue.Accept("nothing", _ => throw new("must not be applied"), out var message);

        await Assert.That(after.Count).IsEqualTo(1);
        await Assert.That(message).IsNull();
    }

    [Test]
    public async Task AcceptAllReportsTheCount()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1))
            .Enqueue(Patch("B.cs", 2))
            .AcceptAll(_ => InlineApplyResult.Applied, out var message);

        await Assert.That(queue.Count).IsEqualTo(0);
        await Assert.That(message).IsEqualTo("Accepted 2");
    }

    [Test]
    public async Task AcceptAllKeepsWhatFailedAndSaysWhy()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1))
            .Enqueue(Patch("B.cs", 2))
            .AcceptAll(
                patch => patch.SourceFile == "A.cs" ? InlineApplyResult.Applied : Fails(patch),
                out var message);

        await Assert.That(queue.Count).IsEqualTo(1);
        await Assert.That(queue.Items[0].Name).IsEqualTo("B.cs:2");
        await Assert.That(message).IsEqualTo("Accepted 1, 1 failed. locked");
    }

    /// <summary>
    /// A batch parts company with a single accept here. Alone, a stale entry is dropped and the
    /// reader is told; out of a batch of thirty, dropping it silently counted a snapshot written
    /// nowhere among the accepts and left nothing behind to say otherwise. It stays, carrying what
    /// the applier said, and a re-run clears the status when the patch arrives again.
    /// </summary>
    [Test]
    public async Task AcceptAllKeepsAStalePatchApartFromTheAccepts()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1))
            .Enqueue(Patch("B.cs", 2))
            .AcceptAll(
                patch => patch.SourceFile == "A.cs"
                    ? InlineApplyResult.Applied
                    : InlineApplyResult.NotFound("no Verify or Throws call"),
                out var message);

        await Assert.That(queue.Count).IsEqualTo(1);
        await Assert.That(queue.Items[0].Status).IsEqualTo("B.cs:2 not written. no Verify or Throws call");
        await Assert.That(message).IsEqualTo("Accepted 1, 1 not written. B.cs:2 not written. no Verify or Throws call");
    }

    /// <summary>
    /// One reason speaks for a batch of one and for no batch larger. It is whichever entry went
    /// wrong last, so naming a single file among thirteen reads as the extent of the damage, and it
    /// arrives at the length of a paragraph in surfaces one line high - a status bar, a balloon, a
    /// menu label. The entries keep their own reasons, which is where a reader with thirteen of
    /// them has to look regardless.
    /// </summary>
    [Test]
    public async Task AcceptAllCountsABatchRatherThanNamingOneOfIt()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1))
            .Enqueue(Patch("B.cs", 2))
            .Enqueue(Patch("C.cs", 3))
            .AcceptAll(_ => InlineApplyResult.NotFound("no Verify or Throws call"), out var message);

        await Assert.That(message).IsEqualTo("Accepted 0, 3 not written");
        await Assert.That(queue.Items.Select(_ => _.Status!)).IsEquivalentTo(
        [
            "A.cs:1 not written. no Verify or Throws call",
            "B.cs:2 not written. no Verify or Throws call",
            "C.cs:3 not written. no Verify or Throws call"
        ]);
    }

    /// <summary>
    /// The same bulk accept with the patches handed over in one call, which is what lets an
    /// applier write a file once for all the snapshots in it. Nothing else about it differs: the
    /// conflict is left out, the patches go over in queue order, and each outcome lands on its own
    /// entry.
    /// </summary>
    [Test]
    public async Task AcceptAllCanHandThePatchesOverTogether()
    {
        IReadOnlyList<InlinePatch> handed = [];
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1))
            .Enqueue(Patch("B.cs", 2, content: "eight", framework: "net8.0"))
            .Enqueue(Patch("B.cs", 2, content: "nine", framework: "net9.0"))
            .Enqueue(Patch("C.cs", 3))
            .AcceptAll(
                _ =>
                {
                    handed = _;
                    return [InlineApplyResult.Applied, InlineApplyResult.NotFound("no Verify or Throws call")];
                },
                out var message);

        await Assert.That(string.Join(", ", handed.Select(_ => $"{_.SourceFile}:{_.LineHint}"))).IsEqualTo("A.cs:1, C.cs:3");
        await Assert.That(string.Join(", ", queue.Items.Select(_ => _.Name))).IsEqualTo("B.cs:2, C.cs:3");
        await Assert.That(queue.Items[1].Status).IsEqualTo("C.cs:3 not written. no Verify or Throws call");
        await Assert.That(message).IsEqualTo("Accepted 1, 1 not written, 1 conflict needs review. C.cs:3 not written. no Verify or Throws call");
    }

    /// <summary>
    /// An applier that answers for fewer patches than it was given has no outcome for some entry,
    /// and guessing which would mark a snapshot accepted that nothing wrote.
    /// </summary>
    [Test]
    public void AcceptAllRefusesResultsThatDoNotMatchThePatches()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1))
            .Enqueue(Patch("B.cs", 2));

        Assert.Throws<ArgumentException>(
            () => queue.AcceptAll(_ => [InlineApplyResult.Applied], out _));
    }

    /// <summary>
    /// The two phase form: find, apply outside the host's lock, complete. A re-run that replaced
    /// the patch while it was applying keeps its new entry, because the outcome describes the old
    /// one.
    /// </summary>
    [Test]
    public async Task CompletingAfterAReplaceLeavesTheNewEntry()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch(content: "first"));
        var entry = queue.Find(InlineKey.For("Sample.cs", 42))!;
        queue = queue.Enqueue(Patch(content: "second"));

        var after = queue.Accept(entry, InlineApplyResult.Applied, out var message);

        await Assert.That(after).IsSameReferenceAs(queue);
        await Assert.That(message).IsNull();
        await Assert.That(after.Items[0].Patch.NewContent).IsEqualTo("second");
    }

    /// <summary>
    /// The other side of <see cref="CompletingAfterAReplaceLeavesTheNewEntry"/>. Applying takes up
    /// to ten seconds on the cross process mutex, and a test that is still failing re-runs and
    /// re-sends the identical patch inside that window. That is not a replace: the entry says what
    /// it said before, so the accept it is in the middle of still completes.
    /// <para>
    /// Rebuilding the entry regardless made every one of those look like a change, so the patch
    /// reached the file and the entry stayed pending with nothing said about it.
    /// </para>
    /// </summary>
    [Test]
    public async Task CompletingAfterAnIdenticalReRunStillApplies()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch(content: "same"));
        var entry = queue.Find(InlineKey.For("Sample.cs", 42))!;
        queue = queue.Enqueue(Patch(content: "same"));

        var after = queue.Accept(entry, InlineApplyResult.Applied, out var message);

        await Assert.That(message).IsEqualTo("Applied Sample.cs:42");
        await Assert.That(after.Items).IsEmpty();
    }

    /// <inheritdoc cref="CompletingAfterAnIdenticalReRunStillApplies"/>
    [Test]
    public async Task CompletingAfterAnIdenticalReRunFromTheSameFrameworkStillApplies()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch(content: "same", framework: "net8.0"));
        var entry = queue.Find(InlineKey.For("Sample.cs", 42))!;
        queue = queue.Enqueue(Patch(content: "same", framework: "net8.0"));

        var after = queue.Accept(entry, InlineApplyResult.Applied, out var message);

        await Assert.That(message).IsEqualTo("Applied Sample.cs:42");
        await Assert.That(after.Items).IsEmpty();
    }

    /// <summary>
    /// A re-run still drops what the last attempt failed with, which is what rebuilding the entry
    /// did: the content has arrived again and nothing has retried it.
    /// </summary>
    [Test]
    public async Task AnIdenticalReRunClearsTheFailureStatus()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch(content: "same"));
        var entry = queue.Find(InlineKey.For("Sample.cs", 42))!;
        queue = queue.Accept(entry, InlineApplyResult.Failed("the file is locked"), out _);
        await Assert.That(queue.Items.Single().Status).IsEqualTo("the file is locked");

        queue = queue.Enqueue(Patch(content: "same"));

        await Assert.That(queue.Items.Single().Status).IsNull();
    }

    [Test]
    public async Task CompletingAfterASettleDoesNothing()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch());
        var entry = queue.Find(InlineKey.For("Sample.cs", 42))!;
        queue = queue.Settle(entry.Key);

        var after = queue.Accept(entry, InlineApplyResult.Applied, out var message);

        await Assert.That(after).IsSameReferenceAs(queue);
        await Assert.That(message).IsNull();
    }

    /// <summary>
    /// An entry that arrived while the batch was applying was not part of the accept, so it is
    /// kept untouched rather than counted as a failure.
    /// </summary>
    [Test]
    public async Task ABatchCompletionSkipsANewcomer()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1))
            .Enqueue(Patch("B.cs", 2));
        var outcomes = queue.Items
            .Select(_ => (_, InlineApplyResult.Applied))
            .ToList();
        queue = queue.Enqueue(Patch("C.cs", 3));

        var after = queue.AcceptAll(outcomes, out var message);

        await Assert.That(after.Items.Select(_ => _.Name)).IsEquivalentTo(["C.cs:3"]);
        await Assert.That(message).IsEqualTo("Accepted 2");
    }

    /// <summary>
    /// A batch completed an entry at a time, the way an owner applying a long queue completes it
    /// so the queue can be watched shrinking. Each step leaves the queue a listing would show at
    /// that point, and the steps together say what the whole batch would have.
    /// </summary>
    [Test]
    public async Task ABatchCompletedAnEntryAtATimeShrinksAsItGoes()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1))
            .Enqueue(Patch("B.cs", 2))
            .Enqueue(Patch("C.cs", 3));
        var pending = queue.Items;
        var tally = new AcceptAllTally();

        queue = queue.AcceptInBatch(pending[0], InlineApplyResult.Applied, ref tally);
        await Assert.That(queue.Items.Select(_ => _.Name)).IsEquivalentTo(["B.cs:2", "C.cs:3"]);

        queue = queue.AcceptInBatch(pending[1], InlineApplyResult.NotFound("no Verify or Throws call"), ref tally);
        queue = queue.AcceptInBatch(pending[2], InlineApplyResult.Applied, ref tally);

        // The stale one stays, as it does out of a whole batch
        await Assert.That(queue.Items.Single().Status).IsEqualTo("B.cs:2 not written. no Verify or Throws call");
        await Assert.That(tally.Refused).IsTrue();
        await Assert.That(tally.Message(queue.Conflicts))
            .IsEqualTo("Accepted 2, 1 not written. B.cs:2 not written. no Verify or Throws call");
    }

    /// <summary>
    /// A re-run that replaced the entry while its patch applied keeps its new content, and the
    /// batch does not count an outcome that describes content no longer pending.
    /// </summary>
    [Test]
    public async Task ABatchStepSkipsAnEntryReplacedWhileItApplied()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch(content: "first"));
        var entry = queue.Items.Single();
        queue = queue.Enqueue(Patch(content: "second"));
        var tally = new AcceptAllTally();

        var after = queue.AcceptInBatch(entry, InlineApplyResult.Applied, ref tally);

        await Assert.That(after).IsSameReferenceAs(queue);
        await Assert.That(tally).IsEqualTo(new());
    }

    /// <summary>
    /// An accept moves the call sites under it, and the entries pending for that file go with
    /// them: the ones under the edit, by what it added, with their variants and status, and no
    /// entry above it or in another file.
    /// </summary>
    [Test]
    public async Task EntriesUnderAnAcceptedSnapshotGoToTheLinesTheirCallSitesAreOn()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 10, member: "First"))
            .Enqueue(Patch(line: 20, member: "Second"))
            .Enqueue(Patch(line: 30, member: "Third", framework: "net9.0", content: "nine"))
            .Enqueue(Patch(line: 30, member: "Third", framework: "net10.0", content: "ten"))
            .Enqueue(Patch("Other.cs", 30));
        var tally = new AcceptAllTally();
        queue = queue.AcceptInBatch(queue.Items[2], InlineApplyResult.Failed("locked"), ref tally);
        var held = queue.Items;

        // The snapshot at line 20 is accepted and its literal is four lines longer
        var accepted = InlineApplyResult.AppliedMoving(21, 4);
        queue = queue
            .Accept(held[1], accepted, out _)
            .Rebased("Sample.cs", [accepted]);

        await Assert.That(queue.Items.Select(_ => _.Name)).IsEquivalentTo(["Sample.cs:10", "Sample.cs:34", "Other.cs:30"]);
        await Assert.That(queue.Items[0]).IsSameReferenceAs(held[0]);
        await Assert.That(queue.Items[2]).IsSameReferenceAs(held[3]);
        var moved = queue.Items[1];
        await Assert.That(moved.Key).IsEqualTo(InlineKey.For("Sample.cs", 34));
        await Assert.That(moved.Status).IsEqualTo("locked");
        await Assert.That(moved.Variants.Select(_ => _.Patch.LineHint)).IsEquivalentTo([34, 34]);
        await Assert.That(moved.Variants.Select(_ => _.Patch.NewContent)).IsEquivalentTo(["nine", "ten"]);
        // A copy, since what was queued is shared with whoever is showing or applying it
        await Assert.That(held[2].Patch.LineHint).IsEqualTo(30);
    }

    /// <summary>
    /// With the lines kept right, the re-run that follows an accept finds its entry by its key,
    /// which is what every other way of finding it was a guess at: here a framework that has no
    /// content in the entry yet, which was queued a second time beside it, and a settle from a
    /// sibling call in the same member that now stands on the old line, which took the entry.
    /// </summary>
    [Test]
    public async Task AfterAnAcceptARerunAndASettleFindAnEntryByItsKey()
    {
        var accepted = InlineApplyResult.AppliedMoving(11, 5);
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 10, member: "First"))
            .Enqueue(Patch(line: 20, member: "Second", framework: "net9.0"));
        queue = queue
            .Accept(queue.Items[0], accepted, out _)
            .Rebased("Sample.cs", [accepted]);

        // Another framework reports the call site where it is now
        var rerun = queue.Enqueue(Patch(line: 25, member: "Second", framework: "net10.0"));
        await Assert.That(rerun.Items.Single().OriginsLabel).IsEqualTo("net9.0 / net10.0");

        // A passing call of the same member, on the line the entry used to have and holding
        // something else, settles nothing
        var settled = queue.Settle(InlineKey.For("Sample.cs", 20), "net9.0", "Second", "unrelated");
        await Assert.That(settled.Items.Single().Key).IsEqualTo(InlineKey.For("Sample.cs", 25));
    }

    /// <summary>
    /// A snapshot that got shorter brings the lines under it up, and an entry whose hint was
    /// already wrong can be standing where another is taken to. A queue holds one entry to a
    /// key, so that one stays where it was.
    /// </summary>
    [Test]
    public async Task AnEntryIsNotTakenToALineAnotherHolds()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 18, member: "Stale"))
            .Enqueue(Patch(line: 20, member: "Second"))
            .Enqueue(Patch(line: 40, member: "Third"));

        var after = queue.Rebased("Sample.cs", [InlineApplyResult.AppliedMoving(19, -2)]);

        await Assert.That(after.Items.Select(_ => _.Name)).IsEquivalentTo(["Sample.cs:18", "Sample.cs:20", "Sample.cs:38"]);
        await Assert.That(after.Items.Select(_ => _.Key).Distinct().Count()).IsEqualTo(3);
    }

    /// <summary>
    /// Nothing moved is the same queue, so a host can tell, and so can whatever reads a queue's
    /// identity as its generation.
    /// </summary>
    [Test]
    public async Task AnEditThatMovedNothingLeavesTheQueueAsItIs()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(line: 10))
            .Enqueue(Patch(line: 20));

        await Assert.That(queue.Rebased("Sample.cs", [InlineApplyResult.Applied, InlineApplyResult.NotFound("gone")])).IsSameReferenceAs(queue);
        await Assert.That(queue.Rebased("Sample.cs", [InlineApplyResult.AppliedMoving(21, 3)])).IsSameReferenceAs(queue);
        await Assert.That(queue.Rebased("Other.cs", [InlineApplyResult.AppliedMoving(1, 3)])).IsSameReferenceAs(queue);
    }

    [Test]
    public async Task DiscardRemovesWithoutApplying()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch())
            .Discard(InlineKey.For("Sample.cs", 42), out var message);

        await Assert.That(queue.Count).IsEqualTo(0);
        await Assert.That(message).IsEqualTo("Discarded Sample.cs:42");
    }

    [Test]
    public async Task DiscardAllEmpties()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1))
            .Enqueue(Patch("B.cs", 2))
            .DiscardAll(out var message);

        await Assert.That(queue.Count).IsEqualTo(0);
        await Assert.That(message).IsEqualTo("Discarded 2");
    }

    /// <summary>
    /// A multi-targeted run disagreeing with itself is one call site with two contents, not two
    /// entries and not a silent overwrite.
    /// </summary>
    [Test]
    public async Task ADifferentFrameworkWithDifferentContentAddsAVariant()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "eight", framework: "net8.0"))
            .Enqueue(Patch(content: "nine", framework: "net9.0"));

        await Assert.That(queue.Count).IsEqualTo(1);
        var entry = queue.Items[0];
        await Assert.That(entry.Conflicted).IsTrue();
        await Assert.That(entry.Variants.Count).IsEqualTo(2);
        // The primary stays the first arrival, so the display does not jump under a reader.
        await Assert.That(entry.Patch.NewContent).IsEqualTo("eight");
        await Assert.That(entry.OriginsLabel).IsEqualTo("net8.0 / net9.0");
    }

    [Test]
    public async Task ADifferentFrameworkWithSameContentMergesOrigins()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(framework: "net8.0"))
            .Enqueue(Patch(framework: "net9.0"));

        var entry = queue.Items[0];
        await Assert.That(entry.Conflicted).IsFalse();
        await Assert.That(entry.Variants).HasSingleItem();
        await Assert.That(entry.Variants[0].Origins).IsEquivalentTo(["net8.0", "net9.0"]);
    }

    [Test]
    public async Task ASameFrameworkRerunReplacesItsVariant()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "first", framework: "net9.0"))
            .Enqueue(Patch(content: "second", framework: "net9.0"));

        var entry = queue.Items[0];
        await Assert.That(entry.Conflicted).IsFalse();
        await Assert.That(entry.Patch.NewContent).IsEqualTo("second");
    }

    /// <summary>
    /// The conflict-clearing path: a re-run whose content now agrees moves its label across, and
    /// the variant it abandons disappears with its last label.
    /// </summary>
    [Test]
    public async Task ARerunThatConvergesClearsTheConflict()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "eight", framework: "net8.0"))
            .Enqueue(Patch(content: "nine", framework: "net9.0"))
            .Enqueue(Patch(content: "nine", framework: "net8.0"));

        var entry = queue.Items[0];
        await Assert.That(entry.Conflicted).IsFalse();
        await Assert.That(entry.Variants).HasSingleItem();
        await Assert.That(entry.Patch.NewContent).IsEqualTo("nine");
        await Assert.That(entry.Variants[0].Origins).IsEquivalentTo(["net9.0", "net8.0"]);
    }

    [Test]
    public async Task AMergedVariantSplitsWhenAFrameworkDiverges()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "same", framework: "net8.0"))
            .Enqueue(Patch(content: "same", framework: "net9.0"))
            .Enqueue(Patch(content: "different", framework: "net8.0"));

        var entry = queue.Items[0];
        await Assert.That(entry.Conflicted).IsTrue();
        await Assert.That(entry.Variants.Count).IsEqualTo(2);
        await Assert.That(entry.Variants[0].Origins).IsEquivalentTo(["net9.0"]);
        await Assert.That(entry.Variants[1].Origins).IsEquivalentTo(["net8.0"]);
        await Assert.That(entry.Variants[1].Patch.NewContent).IsEqualTo("different");
    }

    /// <summary>
    /// An unlabeled arrival cannot be told apart from a re-run, so it falls back to the
    /// pre-variant semantics: the newest content wins outright.
    /// </summary>
    [Test]
    public async Task AnUnlabeledArrivalReplacesEverything()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "eight", framework: "net8.0"))
            .Enqueue(Patch(content: "nine", framework: "net9.0"))
            .Enqueue(Patch(content: "plain"));

        var entry = queue.Items[0];
        await Assert.That(entry.Conflicted).IsFalse();
        await Assert.That(entry.Variants).HasSingleItem();
        await Assert.That(entry.Patch.NewContent).IsEqualTo("plain");
        await Assert.That(entry.Variants[0].Origins).IsEmpty();
    }

    /// <summary>
    /// The mirror case: a labeled arrival into an unlabeled entry cannot be presented as an
    /// honest conflict, so it also collapses to a replace.
    /// </summary>
    [Test]
    public async Task ALabeledArrivalReplacesAnUnlabeledEntry()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "plain"))
            .Enqueue(Patch(content: "nine", framework: "net9.0"));

        var entry = queue.Items[0];
        await Assert.That(entry.Variants).HasSingleItem();
        await Assert.That(entry.Patch.NewContent).IsEqualTo("nine");
        await Assert.That(entry.Variants[0].Origins).IsEquivalentTo(["net9.0"]);
    }

    [Test]
    public async Task AcceptRefusesAConflictedEntry()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "eight", framework: "net8.0"))
            .Enqueue(Patch(content: "nine", framework: "net9.0"));

        var after = queue.Accept(InlineKey.For("Sample.cs", 42), _ => throw new("must not be applied"), out var message);

        await Assert.That(after).IsSameReferenceAs(queue);
        await Assert.That(message).IsEqualTo("Conflicting snapshots (net8.0 / net9.0), resolve in the viewer");
    }

    [Test]
    public async Task AcceptByOriginAppliesThatVariantAndRemovesTheEntry()
    {
        var applied = new List<InlinePatch>();
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "eight", framework: "net8.0"))
            .Enqueue(Patch(content: "nine", framework: "net9.0"))
            .Accept(InlineKey.For("Sample.cs", 42), "net9.0", _ =>
            {
                applied.Add(_);
                return InlineApplyResult.Applied;
            }, out var message);

        await Assert.That(queue.Count).IsEqualTo(0);
        await Assert.That(applied.Single().NewContent).IsEqualTo("nine");
        await Assert.That(message).IsEqualTo("Applied Sample.cs:42");
    }

    [Test]
    public async Task AcceptByOriginFailureKeepsTheWholeEntry()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "eight", framework: "net8.0"))
            .Enqueue(Patch(content: "nine", framework: "net9.0"))
            .Accept(InlineKey.For("Sample.cs", 42), "net9.0", Fails, out var message);

        await Assert.That(queue.Count).IsEqualTo(1);
        await Assert.That(queue.Items[0].Conflicted).IsTrue();
        await Assert.That(queue.Items[0].Status).IsEqualTo("locked");
        await Assert.That(message).IsEqualTo("locked");
    }

    [Test]
    public async Task AcceptByAnUnknownOriginDoesNothing()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "eight", framework: "net8.0"))
            .Enqueue(Patch(content: "nine", framework: "net9.0"));

        var after = queue.Accept(InlineKey.For("Sample.cs", 42), "net6.0", _ => throw new("must not be applied"), out var message);

        await Assert.That(after).IsSameReferenceAs(queue);
        await Assert.That(message).IsEqualTo("No net6.0 variant for Sample.cs:42");
    }

    /// <summary>
    /// A bulk accept never picks sides: the conflicted entry survives untouched and the message
    /// says what still needs a human.
    /// </summary>
    [Test]
    public async Task AcceptAllSkipsConflictsAndCountsThem()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1))
            .Enqueue(Patch("B.cs", 2, content: "eight", framework: "net8.0"))
            .Enqueue(Patch("B.cs", 2, content: "nine", framework: "net9.0"))
            .AcceptAll(_ => InlineApplyResult.Applied, out var message);

        await Assert.That(queue.Items.Select(_ => _.Name)).IsEquivalentTo(["B.cs:2"]);
        await Assert.That(message).IsEqualTo("Accepted 1, 1 conflict needs review");
    }

    [Test]
    public async Task AcceptAllPluralizesConflicts()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1, content: "eight", framework: "net8.0"))
            .Enqueue(Patch("A.cs", 1, content: "nine", framework: "net9.0"))
            .Enqueue(Patch("B.cs", 2, content: "eight", framework: "net8.0"))
            .Enqueue(Patch("B.cs", 2, content: "nine", framework: "net9.0"))
            .AcceptAll(_ => InlineApplyResult.Applied, out var message);

        await Assert.That(queue.Count).IsEqualTo(2);
        await Assert.That(message).IsEqualTo("Accepted 0, 2 conflicts need review");
    }

    [Test]
    public async Task AcceptAllComposesFailuresAndConflicts()
    {
        InlineQueue.Empty
            .Enqueue(Patch("A.cs", 1))
            .Enqueue(Patch("B.cs", 2, content: "eight", framework: "net8.0"))
            .Enqueue(Patch("B.cs", 2, content: "nine", framework: "net9.0"))
            .AcceptAll(Fails, out var message);

        await Assert.That(message).IsEqualTo("Accepted 0, 1 failed, 1 conflict needs review. locked");
    }

    /// <summary>
    /// The mid-apply guard, generalized: an entry that grew a variant while its patch was applying
    /// keeps its new self, so the other framework's differing content is never silently dropped.
    /// A later targeted accept resolves it, with the applied side completing as already applied.
    /// </summary>
    [Test]
    public async Task CompletingAfterAVariantArrivedLeavesTheEntry()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch(content: "eight", framework: "net8.0"));
        var entry = queue.Find(InlineKey.For("Sample.cs", 42))!;
        queue = queue.Enqueue(Patch(content: "nine", framework: "net9.0"));

        var after = queue.Accept(entry, InlineApplyResult.Applied, out var message);

        await Assert.That(after).IsSameReferenceAs(queue);
        await Assert.That(message).IsNull();
        await Assert.That(after.Items[0].Conflicted).IsTrue();
    }

    [Test]
    public async Task SettleWithOriginRemovesThatVariant()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "eight", framework: "net8.0"))
            .Enqueue(Patch(content: "nine", framework: "net9.0"))
            .Settle(InlineKey.For("Sample.cs", 42), "net8.0");

        var entry = queue.Items[0];
        await Assert.That(entry.Conflicted).IsFalse();
        await Assert.That(entry.Patch.NewContent).IsEqualTo("nine");
    }

    /// <summary>
    /// The content is still pending for the other framework, so only the label goes.
    /// </summary>
    [Test]
    public async Task SettleWithOriginRemovesJustTheLabelOfAMergedVariant()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(framework: "net8.0"))
            .Enqueue(Patch(framework: "net9.0"))
            .Settle(InlineKey.For("Sample.cs", 42), "net8.0");

        var entry = queue.Items[0];
        await Assert.That(entry.Variants).HasSingleItem();
        await Assert.That(entry.Variants[0].Origins).IsEquivalentTo(["net9.0"]);
    }

    [Test]
    public async Task SettleWithOriginRemovesTheEntryWithTheLastVariant()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(framework: "net9.0"))
            .Settle(InlineKey.For("Sample.cs", 42), "net9.0");

        await Assert.That(queue.Count).IsEqualTo(0);
    }

    /// <summary>
    /// An unlabeled entry cannot be scoped, and leaving it would strand a stale entry, so a
    /// labeled settle takes the whole thing.
    /// </summary>
    [Test]
    public async Task SettleWithOriginRemovesAnUnlabeledEntry()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch())
            .Settle(InlineKey.For("Sample.cs", 42), "net9.0");

        await Assert.That(queue.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SettleWithoutOriginRemovesTheWholeEntry()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "eight", framework: "net8.0"))
            .Enqueue(Patch(content: "nine", framework: "net9.0"))
            .Settle(InlineKey.For("Sample.cs", 42), null);

        await Assert.That(queue.Count).IsEqualTo(0);
    }

    /// <summary>
    /// A settle from a framework with nothing pending changed nothing, and the same-instance
    /// contract lets the host see that.
    /// </summary>
    [Test]
    public async Task SettleForAnAbsentOriginReturnsTheSameQueue()
    {
        var queue = InlineQueue.Empty.Enqueue(Patch(framework: "net9.0"));

        await Assert.That(queue.Settle(InlineKey.For("Sample.cs", 42), "net8.0")).IsSameReferenceAs(queue);
    }

    [Test]
    public async Task DiscardRemovesAllVariants()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch(content: "eight", framework: "net8.0"))
            .Enqueue(Patch(content: "nine", framework: "net9.0"))
            .Discard(InlineKey.For("Sample.cs", 42), out var message);

        await Assert.That(queue.Count).IsEqualTo(0);
        await Assert.That(message).IsEqualTo("Discarded Sample.cs:42");
    }
}
