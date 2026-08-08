public class IpcTests
{
    [Test]
    public async Task InlineQueuesAPatch()
    {
        using var fixture = new ServerFixture();

        var response = fixture.Send(Inline(Fixtures.Patch()));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(fixture.Host.State.Queue.Count).IsEqualTo(1);
    }

    [Test]
    public async Task InlineReplacesTheSameKey()
    {
        using var fixture = new ServerFixture();

        fixture.Send(Inline(Fixtures.Patch(content: "first")));
        fixture.Send(Inline(Fixtures.Patch(content: "second")));

        await Assert.That(fixture.Host.State.Queue.Count).IsEqualTo(1);
        await Assert.That(fixture.Host.State.Queue[0].LeftText).IsEqualTo("second");
    }

    [Test]
    public async Task InlineRejectsAnUnreadableBody()
    {
        using var fixture = new ServerFixture();

        var response = fixture.Send(new(ViewerVerb.Inline, Body: "not a patch"));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(fixture.Host.State.Queue).IsEmpty();
    }

    [Test]
    public async Task InlineWithoutABodyIsRejected()
    {
        using var fixture = new ServerFixture();

        var response = fixture.Send(new(ViewerVerb.Inline));

        await Assert.That(response.Ok).IsFalse();
    }

    [Test]
    public async Task SettleDropsTheEntry()
    {
        using var fixture = new ServerFixture();
        fixture.Send(Inline(Fixtures.Patch()));

        var response = fixture.Send(new(ViewerVerb.Settle, QueueEntry.KeyForInline("SampleTests.cs", 42)));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(fixture.Host.State.Queue).IsEmpty();
    }

    [Test]
    public async Task SettleForAnUnknownKeyIsHarmless()
    {
        using var fixture = new ServerFixture();
        fixture.Send(Inline(Fixtures.Patch()));

        var response = fixture.Send(new(ViewerVerb.Settle, "nope|1"));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(fixture.Host.State.Queue.Count).IsEqualTo(1);
    }

    [Test]
    public Task List()
    {
        using var fixture = new ServerFixture();
        fixture.Send(Inline(Fixtures.Patch()));
        fixture.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));

        return Verify(fixture.Send(new(ViewerVerb.List)));
    }

    [Test]
    public Task ListWhenEmpty()
    {
        using var fixture = new ServerFixture();

        return Verify(fixture.Send(new(ViewerVerb.List)));
    }

    [Test]
    public async Task AcceptAppliesAndRemoves()
    {
        using var fixture = new ServerFixture();
        fixture.Send(Inline(Fixtures.Patch()));
        fixture.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));

        var response = fixture.Send(new(ViewerVerb.Accept, QueueEntry.KeyForInline("SampleTests.cs", 42)));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(fixture.Applied.Count).IsEqualTo(1);
        await Assert.That(fixture.Host.State.Queue.Count).IsEqualTo(1);
        await Assert.That(fixture.Host.State.Queue[0].Name).IsEqualTo("OtherTests.cs:7");
    }

    /// <summary>
    /// The tray can act on an item that is not the selected one, so accept must not silently
    /// operate on whatever happens to be in view.
    /// </summary>
    [Test]
    public async Task AcceptTargetsTheKeyNotTheSelection()
    {
        using var fixture = new ServerFixture();
        fixture.Send(Inline(Fixtures.Patch()));
        fixture.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));
        await Assert.That(fixture.Host.State.Selected).IsEqualTo(0);

        fixture.Send(new(ViewerVerb.Accept, QueueEntry.KeyForInline("OtherTests.cs", 7)));

        await Assert.That(fixture.Host.State.Queue.Count).IsEqualTo(1);
        await Assert.That(fixture.Host.State.Queue[0].Name).IsEqualTo("SampleTests.cs:42");
    }

    [Test]
    public Task AcceptAnUnknownKey()
    {
        using var fixture = new ServerFixture();

        return Verify(fixture.Send(new(ViewerVerb.Accept, "missing|1")));
    }

    [Test]
    public async Task AcceptAll()
    {
        using var fixture = new ServerFixture();
        fixture.Send(Inline(Fixtures.Patch()));
        fixture.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));

        var response = fixture.Send(new(ViewerVerb.AcceptAll));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(fixture.Applied.Count).IsEqualTo(2);
        await Assert.That(fixture.Host.State.Queue).IsEmpty();
    }

    [Test]
    public async Task DiscardRemovesWithoutApplying()
    {
        using var fixture = new ServerFixture();
        fixture.Send(Inline(Fixtures.Patch()));

        fixture.Send(new(ViewerVerb.Discard, QueueEntry.KeyForInline("SampleTests.cs", 42)));

        await Assert.That(fixture.Applied).IsEmpty();
        await Assert.That(fixture.Host.State.Queue).IsEmpty();
    }

    [Test]
    public async Task DiscardAll()
    {
        using var fixture = new ServerFixture();
        fixture.Send(Inline(Fixtures.Patch()));
        fixture.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));

        fixture.Send(new(ViewerVerb.DiscardAll));

        await Assert.That(fixture.Applied).IsEmpty();
        await Assert.That(fixture.Host.State.Queue).IsEmpty();
    }

    [Test]
    public async Task FocusSelectsAndRaises()
    {
        using var fixture = new ServerFixture();
        fixture.Send(Inline(Fixtures.Patch()));
        fixture.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));

        var response = fixture.Send(new(ViewerVerb.Focus, QueueEntry.KeyForInline("OtherTests.cs", 7)));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(fixture.Host.State.Selected).IsEqualTo(1);
        await Assert.That(fixture.Windows).IsEquivalentTo([WindowCommand.Focus]);
    }

    [Test]
    public async Task ShowAndHide()
    {
        using var fixture = new ServerFixture();

        fixture.Send(new(ViewerVerb.Hide));
        fixture.Send(new(ViewerVerb.Show));

        await Assert.That(fixture.Windows).IsEquivalentTo([WindowCommand.Hide, WindowCommand.Show]);
    }

    [Test]
    public async Task Quit()
    {
        using var fixture = new ServerFixture();
        fixture.Send(Inline(Fixtures.Patch()));

        fixture.Send(new(ViewerVerb.Quit));

        await Assert.That(fixture.Host.State.Exit).IsTrue();
    }

    /// <summary>
    /// The single instance rule. A second process must fail to bind so it forwards and exits
    /// rather than opening a rival window.
    /// </summary>
    [Test]
    public async Task ASecondBindOnTheSamePortFails()
    {
        using var fixture = new ServerFixture();

        var second = ViewerServer.TryBind(fixture.Server.Port, out var rival);
        rival?.Dispose();

        await Assert.That(second).IsFalse();
    }

    [Test]
    public async Task BindSucceedsWhenNothingOwnsThePort()
    {
        var bound = ViewerServer.TryBind(0, out var server);
        using (server)
        {
            await Assert.That(bound).IsTrue();
            await Assert.That(server!.Port).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task SendingToANoOneReportsFailureRatherThanThrowing()
    {
        // Bind then immediately release, so the port is almost certainly free.
        ViewerServer.TryBind(0, out var server);
        var port = server!.Port;
        server.Dispose();

        await Assert.That(ViewerClient.TrySend(new(ViewerVerb.List), out _, port)).IsFalse();
    }

    [Test]
    public async Task GarbageIsRejectedWithoutKillingTheServer()
    {
        using var fixture = new ServerFixture();

        await Assert.That(fixture.SendRaw("total nonsense").Ok).IsFalse();
        // Still serving.
        await Assert.That(fixture.Send(new(ViewerVerb.List)).Ok).IsTrue();
    }

    static ViewerMessage Inline(InlinePatch patch) =>
        new(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch));
}
