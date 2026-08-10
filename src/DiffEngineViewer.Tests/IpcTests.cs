// Serialised: each test stands up a real listener and drives it through a blocking client, so
// running them concurrently starves the thread pool on a small machine and every exchange then
// times out. Mirrors the assembly wide limiter DiffEngineTray.Tests uses for its socket tests.
[NotInParallel]
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

    /// <summary>
    /// What a viewer showing someone else's queue asks for. Checked by parsing the patches back
    /// rather than snapshotting, because the payload is base64 and a baseline of it pins nothing
    /// a reader could judge.
    /// </summary>
    [Test]
    public async Task ListFullCarriesThePatches()
    {
        using var fixture = new ServerFixture();
        fixture.Send(Inline(Fixtures.Patch()));
        fixture.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));

        var response = fixture.Send(new(ViewerVerb.ListFull));

        await Assert.That(response.Items.Select(_ => _.Name))
            .IsEquivalentTo(["SampleTests.cs:42", "OtherTests.cs:7"]);
        foreach (var item in response.Items)
        {
            await Assert.That(InlinePatchFile.TryParse(item.Patch!, out var patch)).IsTrue();
            await Assert.That(QueueEntry.ForInline(patch!).Name).IsEqualTo(item.Name);
        }
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

    /// <summary>
    /// Removing a literal is a configuration change with nothing to review, so it must never reach
    /// the queue and sit there waiting for an accept that means nothing.
    /// </summary>
    [Test]
    public async Task ARemovePatchIsRejected()
    {
        using var fixture = new ServerFixture();

        var response = fixture.Send(Inline(new("Sample.cs", 1, "\"old\"", "", InlinePatchMode.Remove)));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(fixture.Host.State.Queue).IsEmpty();
    }

    static ViewerMessage Inline(InlinePatch patch) =>
        new(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch));
}
