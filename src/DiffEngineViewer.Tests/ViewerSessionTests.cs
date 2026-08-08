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

    [Test]
    public async Task ScrollIsClampedToTheLastPage()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));

        var end = ViewerSession.Apply(state, CommandKind.ScrollEnd, Fixtures.Applied);

        var body = ScreenBuilder.BodyRows(state);
        await Assert.That(end.ScrollTop).IsEqualTo(state.Queue[0].TotalRows - body);
    }

    [Test]
    public async Task ScrollDoesNotGoNegative()
    {
        var state = Fixtures.File();

        var up = ViewerSession.Apply(state, CommandKind.PageUp, Fixtures.Applied);

        await Assert.That(up.ScrollTop).IsEqualTo(0);
    }

    [Test]
    public async Task ShortContentDoesNotScroll()
    {
        var state = Fixtures.File();

        var down = ViewerSession.Apply(state, CommandKind.ScrollDown, Fixtures.Applied);

        await Assert.That(down.ScrollTop).IsEqualTo(0);
    }

    [Test]
    public async Task GrowingTheWindowPullsScrollBack()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));
        var end = ViewerSession.Apply(state, CommandKind.ScrollEnd, Fixtures.Applied);

        var resized = ViewerSession.Resize(end, Fixtures.Columns, 200);

        await Assert.That(resized.ScrollTop).IsEqualTo(0);
    }

    [Test]
    public async Task NextChangeWalksBlocksThenStops()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));

        var first = ViewerSession.Apply(state, CommandKind.NextChange, Fixtures.Applied);
        var second = ViewerSession.Apply(first, CommandKind.NextChange, Fixtures.Applied);
        var third = ViewerSession.Apply(second, CommandKind.NextChange, Fixtures.Applied);
        var past = ViewerSession.Apply(third, CommandKind.NextChange, Fixtures.Applied);

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
        var scrolled = ViewerSession.Apply(state, CommandKind.PageDown, Fixtures.Applied);
        await Assert.That(scrolled.ScrollTop).IsGreaterThan(0);

        var selected = ViewerSession.Apply(scrolled, CommandKind.NextItem, Fixtures.Applied);

        await Assert.That(selected.Selected).IsEqualTo(1);
        await Assert.That(selected.ScrollTop).IsEqualTo(0);
    }

    [Test]
    public async Task SelectingPastTheEndIsIgnored()
    {
        var state = Fixtures.Inline(Fixtures.Patch());

        var selected = ViewerSession.Apply(state, CommandKind.NextItem, Fixtures.Applied);

        await Assert.That(selected.Selected).IsEqualTo(0);
    }

    [Test]
    public async Task QuitExitsWithoutTouchingTheQueue()
    {
        var state = Fixtures.Inline(Fixtures.Patch());

        var quit = ViewerSession.Apply(state, CommandKind.Quit, Fixtures.Applied);

        await Assert.That(quit.Exit).IsTrue();
        await Assert.That(quit.Queue.Count).IsEqualTo(1);
    }
}
