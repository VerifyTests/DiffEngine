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
        string? testName = null) =>
        new(source, line, "\"old\"", content)
        {
            Framework = framework,
            TestName = testName
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
            .Enqueue(Patch("Sample.cs"))
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
            .Enqueue(Patch("Sample.cs"))
            .Enqueue(Patch("SAMPLE.CS"));

        await Assert.That(queue.Count).IsEqualTo(2);
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
        await Assert.That(message).IsEqualTo("Sample.cs:42 source changed, re-run the test");
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
