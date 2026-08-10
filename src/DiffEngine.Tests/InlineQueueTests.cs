/// <summary>
/// The queue half of what ViewerSession used to do, now that both the tray and the viewer host it.
/// These are the behaviours that must not differ between the two, which is the whole reason the
/// queue was extracted rather than reimplemented.
/// </summary>
public class InlineQueueTests
{
    static InlinePatch Patch(string source = "Sample.cs", int line = 42, string content = "new") =>
        new(source, line, "\"old\"", content);

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
    /// The same path can reach here with different casing, and it is still one call site.
    /// </summary>
    [Test]
    public async Task EnqueueMatchesRegardlessOfPathCase()
    {
        var queue = InlineQueue.Empty
            .Enqueue(Patch("Sample.cs"))
            .Enqueue(Patch("SAMPLE.CS"));

        await Assert.That(queue.Count).IsEqualTo(1);
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
}
