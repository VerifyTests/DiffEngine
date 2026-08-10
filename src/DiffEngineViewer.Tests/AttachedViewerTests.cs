/// <summary>
/// A viewer displaying a queue it does not own. The owner here is a <see cref="ServerFixture"/>
/// rather than DiffEngineTray, because ownership is decided by who holds the port and nothing on
/// the displaying side depends on which process that turned out to be.
/// </summary>
public class AttachedViewerTests
{
    static ViewerMessage Inline(InlinePatch patch) =>
        new(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch));

    static (SessionHost host, OwnerLink link) Attach(ServerFixture owner)
    {
        var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));
        return (host, new(host, owner.Server.Port));
    }

    /// <summary>
    /// Only the patch crosses the wire. Every pane, header and row is derived on this side, which
    /// is what keeps DiffPlex out of whatever process owns the queue.
    /// </summary>
    [Test]
    public async Task TheQueueIsRebuiltFromTheOwner()
    {
        using var owner = new ServerFixture();
        owner.Send(Inline(Fixtures.Patch()));
        owner.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));
        var (host, link) = Attach(owner);

        await Assert.That(link.Pump()).IsTrue();

        var queue = host.State.Queue;
        await Assert.That(queue.Select(_ => _.Name))
            .IsEquivalentTo(["SampleTests.cs:42", "OtherTests.cs:7"]);
        await Assert.That(queue[0].LeftText).IsEqualTo(Fixtures.Received);
        await Assert.That(queue[0].RightText).IsEqualTo(Fixtures.Expected);
        await Assert.That(queue[0].TotalRows).IsGreaterThan(0);
    }

    [Test]
    public async Task AFailedEntryKeepsItsStatus()
    {
        using var owner = new ServerFixture();
        owner.Send(Inline(Fixtures.Patch()));
        owner.Host.Mutate(_ => _ with
        {
            Queue = [_.Queue[0] with { Status = "locked" }]
        });
        var (host, link) = Attach(owner);

        link.Pump();

        await Assert.That(host.State.Queue[0].Status).IsEqualTo("locked");
    }

    [Test]
    public async Task AcceptingIsDoneByTheOwner()
    {
        using var owner = new ServerFixture();
        owner.Send(Inline(Fixtures.Patch()));
        var (host, link) = Attach(owner);
        link.Pump();

        link.Post(ViewerVerb.Accept, host.State.Current!.Key);
        link.Pump();

        await Assert.That(owner.Applied).HasSingleItem();
        await Assert.That(host.State.Queue).IsEmpty();
        await Assert.That(host.State.Message).IsEqualTo("Applied SampleTests.cs:42");
        // The queue is the owner's, so an empty one closes this window and nothing more.
        await Assert.That(host.State.Exit).IsTrue();
    }

    [Test]
    public async Task DiscardingIsDoneByTheOwner()
    {
        using var owner = new ServerFixture();
        owner.Send(Inline(Fixtures.Patch()));
        owner.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));
        var (host, link) = Attach(owner);
        link.Pump();

        link.Post(ViewerVerb.Discard, host.State.Current!.Key);
        link.Pump();

        await Assert.That(owner.Applied).IsEmpty();
        await Assert.That(host.State.Queue.Select(_ => _.Name)).IsEquivalentTo(["OtherTests.cs:7"]);
    }

    [Test]
    public async Task AcceptAllNeedsNoKey()
    {
        using var owner = new ServerFixture();
        owner.Send(Inline(Fixtures.Patch()));
        owner.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));
        var (host, link) = Attach(owner);
        link.Pump();

        link.Post(ViewerVerb.AcceptAll, null);
        link.Pump();

        await Assert.That(owner.Applied.Count).IsEqualTo(2);
        await Assert.That(host.State.Message).IsEqualTo("Accepted 2");
    }

    /// <summary>
    /// Reported rather than acted on, because before the window opens this means "do not open one"
    /// and afterwards it means "close it".
    /// </summary>
    [Test]
    public async Task AnAbsentOwnerIsReported()
    {
        var host = new SessionHost(SessionState.Start(ViewerMode.Inline));

        // Bound and released, so the port is one nothing is listening on rather than one that
        // might belong to a real viewer on this machine.
        ViewerServer.TryBind(0, out var server);
        var port = server!.Port;
        server.Dispose();

        await Assert.That(new OwnerLink(host, port).Pump()).IsFalse();
    }

    /// <summary>
    /// A command posted while the owner is going away must not be silently swallowed.
    /// </summary>
    [Test]
    public async Task ACommandToAnAbsentOwnerIsReported()
    {
        var host = new SessionHost(SessionState.Start(ViewerMode.Inline));
        ViewerServer.TryBind(0, out var server);
        var port = server!.Port;
        server.Dispose();

        var link = new OwnerLink(host, port);
        link.Post(ViewerVerb.Accept, "nope|1");

        await Assert.That(link.Pump(out var sent)).IsFalse();
        await Assert.That(sent).IsTrue();
    }
}
