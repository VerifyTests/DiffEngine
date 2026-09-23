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

    /// <summary>
    /// Asked with the tag of the listing it gave last, an owner whose queue has not moved answers
    /// that and nothing else, rather than every patch again. Any change makes it answer in full.
    /// </summary>
    [Test]
    public async Task AnUnchangedQueueIsNotSentAgain()
    {
        using var owner = new ServerFixture();
        owner.Send(Inline(Fixtures.Patch()));

        var full = owner.Send(new(ViewerVerb.ListFull));
        await Assert.That(full.Tag).IsNotNull();
        await Assert.That(full.Items).HasSingleItem();

        var same = owner.Send(new(ViewerVerb.ListFull, Body: full.Tag));
        await Assert.That(same.Unchanged).IsTrue();
        await Assert.That(same.Items).IsEmpty();

        owner.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));
        var changed = owner.Send(new(ViewerVerb.ListFull, Body: full.Tag));
        await Assert.That(changed.Unchanged).IsFalse();
        await Assert.That(changed.Items.Count).IsEqualTo(2);
    }

    /// <summary>
    /// What an unchanged answer stands for is the listing held from before it, so the window keeps
    /// showing it, and still follows the owner once something does change.
    /// </summary>
    [Test]
    public async Task AnAttachedViewerFollowsAChangeAfterAnUnchangedListing()
    {
        using var owner = new ServerFixture();
        owner.Send(Inline(Fixtures.Patch()));
        var (host, link) = Attach(owner);
        link.Pump();
        var shown = host.State;

        link.Pump();
        await Assert.That(host.State).IsSameReferenceAs(shown);

        owner.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));
        link.Pump();
        await Assert.That(host.State.Queue.Select(_ => _.Name))
            .IsEquivalentTo(["SampleTests.cs:42", "OtherTests.cs:7"]);
    }

    /// <summary>
    /// A send that changes nothing still says so, on a listing that was answered unchanged.
    /// </summary>
    [Test]
    public async Task ARefusalArrivesOnAnUnchangedListing()
    {
        using var owner = new ServerFixture();
        owner.Send(Inline(Fixtures.Patch()));
        var (host, link) = Attach(owner);
        link.Pump();

        link.Post(ViewerVerb.Discard, "nothing|1");
        link.Pump();

        await Assert.That(host.State.Message).IsEqualTo("No pending snapshot for nothing|1");
        await Assert.That(host.State.Queue).HasSingleItem();
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
    /// A busy owner is not a dead one. An accept can hold the owner for ten seconds on
    /// InlineApplier's cross process mutex, and the old three second wait read that as the owner
    /// having died and closed the window mid apply. Only a refused connection, which arrives in
    /// milliseconds, means gone.
    /// </summary>
    [Test]
    public async Task ABusyOwnerDoesNotReadAsDead()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();
        var patch = InlinePatchFile.Build(Fixtures.Patch());
        var listening = server.Listen(
            _ =>
            {
                // Longer than the old wait, shorter than OwnerLink.Wait.
                Thread.Sleep(TimeSpan.FromSeconds(4));
                return ViewerResponse.Listing([new("key", "SampleTests.cs:42", null, patch)]);
            },
            cancel.Token);

        var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));

        await Assert.That(new OwnerLink(host, server.Port).Pump()).IsTrue();

        await Assert.That(host.State.Queue).HasSingleItem();
        await cancel.CancelAsync();
        _ = listening;
    }

    /// <summary>
    /// An owner that answers is an owner, whatever it answered.
    /// <para>
    /// ViewerServer turns any exception in the listing handler into an error reply, and this side
    /// read one of those as the owner having gone: the window closed with "The queue owner is no
    /// longer running." over a single transient throw, taking the queue it was displaying with it.
    /// </para>
    /// </summary>
    [Test]
    public async Task AnErrorReplyIsNotADeadOwner()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();
        var listening = server.Listen(
            _ => ViewerResponse.Error("The listing could not be built"),
            cancel.Token);
        var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));

        await Assert.That(new OwnerLink(host, server.Port).Pump()).IsTrue();

        await Assert.That(host.State.Message).IsEqualTo("The listing could not be built");
        await Assert.That(host.State.Exit).IsFalse();
        await cancel.CancelAsync();
        _ = listening;
    }

    /// <summary>
    /// The owner has no window of its own, so raising, hiding and closing come back on a listing
    /// rather than being pushed at a port this process does not hold.
    /// </summary>
    [Test]
    public async Task AWindowCommandFromTheOwnerIsRaised()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();
        var patch = InlinePatchFile.Build(Fixtures.Patch());
        // ReSharper disable once UnusedVariable
        var listening = server.Listen(
            _ => ViewerResponse.Listing([new("key", "SampleTests.cs:42", null, patch)], WindowCommand.Focus),
            cancel.Token);

        var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));
        var link = new OwnerLink(host, server.Port);

        await Assert.That(link.Pump()).IsTrue();

        // Queued for the render loop to drain, which is the only thread that may touch a window.
        await Assert.That(link.Windows).IsEquivalentTo([WindowCommand.Focus]);
        await Assert.That(host.State.Queue).HasSingleItem();
        await cancel.CancelAsync();
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

    /// <summary>
    /// A raw owner answering a fixed listing, for driving the tracked-file half of the wire
    /// without a tray.
    /// </summary>
    static (ViewerServer server, CancelSource cancel) Listing(Func<ViewerResponse> respond)
    {
        if (!ViewerServer.TryBind(0, out var bound))
        {
            throw new("Could not bind an ephemeral port.");
        }

        var cancel = new CancelSource();
        _ = bound.Listen(_ => respond(), cancel.Token);
        return (bound, cancel);
    }

    /// <summary>
    /// The owner sends paths; the panes are read from local disk on this side, the same shape as
    /// patches carrying the snapshot text.
    /// </summary>
    [Test]
    public async Task MoveAndDeleteLinesMaterialize()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"deview_{Guid.NewGuid():N}.received.txt");
        var target = Path.Combine(Path.GetTempPath(), $"deview_{Guid.NewGuid():N}.verified.txt");
        await File.WriteAllTextAsync(temp, "incoming");
        await File.WriteAllTextAsync(target, "committed");
        try
        {
            var (server, cancel) = Listing(() => ViewerResponse.Listing(
                [],
                moves: [new(TrackedKeys.ForMove(temp), "Sample.Test (txt)", "MySolution", temp, target)],
                deletes: [new(TrackedKeys.ForDelete(target), "extra.verified.txt", null, target)]));
            using (server)
            using (cancel)
            {
                var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));

                await Assert.That(new OwnerLink(host, server.Port).Pump()).IsTrue();

                var move = host.State.Queue.Single(_ => _.Kind == QueueEntryKind.Move);
                await Assert.That(move.LeftText).IsEqualTo("incoming");
                await Assert.That(move.RightText).IsEqualTo("committed");
                await Assert.That(move.Solution).IsEqualTo("MySolution");
                var delete = host.State.Queue.Single(_ => _.Kind == QueueEntryKind.Delete);
                await Assert.That(delete.RightText).IsEqualTo("committed");
                await cancel.CancelAsync();
            }
        }
        finally
        {
            File.Delete(temp);
            File.Delete(target);
        }
    }

    /// <summary>
    /// The poller must survive a file it cannot read — a throw here closes the window as "owner
    /// gone". An unreadable file degrades to an empty pane carrying the reason.
    /// </summary>
    [Test]
    public async Task AnUnreadableFileDegradesNotCrashes()
    {
        var file = Path.Combine(Path.GetTempPath(), $"deview_{Guid.NewGuid():N}.verified.txt");
        await File.WriteAllTextAsync(file, "locked away");
        try
        {
            // ReSharper disable once UseAwaitUsing
            using var holder = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
            var (server, cancel) = Listing(() => ViewerResponse.Listing(
                [],
                deletes: [new(TrackedKeys.ForDelete(file), "extra.verified.txt", null, file)]));
            using (server)
            using (cancel)
            {
                var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));

                await Assert.That(new OwnerLink(host, server.Port).Pump()).IsTrue();

                var entry = host.State.Queue.Single();
                await Assert.That(entry.RightText).IsEmpty();
                await Assert.That(entry.Warning).Contains("Could not read");
                await cancel.CancelAsync();
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// This runs five times a second: an entry whose files have not changed is the same instance
    /// across pumps, and a rewrite underneath it refreshes the pane.
    /// </summary>
    [Test]
    public async Task UnchangedFilesAreNotReReadAndAChangeRefreshes()
    {
        var file = Path.Combine(Path.GetTempPath(), $"deview_{Guid.NewGuid():N}.verified.txt");
        await File.WriteAllTextAsync(file, "first");
        try
        {
            var (server, cancel) = Listing(() => ViewerResponse.Listing(
                [],
                deletes: [new(TrackedKeys.ForDelete(file), "extra.verified.txt", null, file)]));
            using (server)
            using (cancel)
            {
                var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));
                var link = new OwnerLink(host, server.Port);
                link.Pump();
                var first = host.State.Queue.Single();

                link.Pump();
                await Assert.That(host.State.Queue.Single()).IsSameReferenceAs(first);

                // A stamp needs a distinct write time; length changing makes it deterministic.
                await File.WriteAllTextAsync(file, "second, longer");
                link.Pump();
                await Assert.That(host.State.Queue.Single().RightText).IsEqualTo("second, longer");
                await cancel.CancelAsync();
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// Accepting a conflicted entry names the variant on screen, and the owner applies exactly
    /// that one.
    /// </summary>
    [Test]
    public async Task AForwardedAcceptCarriesTheVariantOrigin()
    {
        using var owner = new ServerFixture();
        owner.Send(Inline(Fixtures.Patch(content: "eight", framework: "net8.0")));
        owner.Send(Inline(Fixtures.Patch(content: "nine", framework: "net9.0")));
        var (host, link) = Attach(owner);
        link.Pump();
        await Assert.That(host.State.Current!.Conflicted).IsTrue();

        link.Post(ViewerVerb.Accept, host.State.Current!.Key, "net9.0");
        link.Pump();

        await Assert.That(owner.Applied.Single().NewContent).IsEqualTo("nine");
        await Assert.That(host.State.Queue).IsEmpty();
    }

    /// <summary>
    /// The refusal a key-only accept of a conflict earns, surfaced in the footer rather than
    /// silently doing nothing.
    /// </summary>
    [Test]
    public async Task AKeyOnlyAcceptOfAConflictIsRefused()
    {
        using var owner = new ServerFixture();
        owner.Send(Inline(Fixtures.Patch(content: "eight", framework: "net8.0")));
        owner.Send(Inline(Fixtures.Patch(content: "nine", framework: "net9.0")));
        var (host, link) = Attach(owner);
        link.Pump();

        link.Post(ViewerVerb.Accept, host.State.Current!.Key);
        link.Pump();

        await Assert.That(owner.Applied).IsEmpty();
        await Assert.That(host.State.Queue).HasSingleItem();
        await Assert.That(host.State.Message)
            .IsEqualTo("Conflicting snapshots (net8.0 / net9.0), resolve in the viewer");
    }

    /// <summary>
    /// A snapshot moving inline is a patch plus a delete of the verified file it replaces. "Accept
    /// all in SolutionA" from an attached window used to post an accept per member, the delete
    /// included, and the owner carried each out as asked: a patch it could not write - the source
    /// moved since the run - and the verified file went anyway, leaving no copy of the snapshot.
    /// </summary>
    [Test]
    public async Task AGroupAcceptHoldsItsDeletesWhenASnapshotWasNotWritten()
    {
        using var owner = new ServerFixture(applier: _ => InlineApplyResult.NotFound("Could not locate the call"));
        var (verified, host, link) = AttachToSolutionA(owner);
        try
        {
            AcceptAllInSolutionA(host, link);

            await Assert.That(owner.Actions).IsEquivalentTo([$"apply {solutionA.SourceFile}"]);
            await Assert.That(host.State.Message).IsEqualTo(OwnerLink.DeletesHeld);
            // Still pending, to be accepted on its own once the reviewer has seen why
            await Assert.That(host.State.Queue.Select(_ => _.Kind)).Contains(QueueEntryKind.Delete);
        }
        finally
        {
            File.Delete(verified);
        }
    }

    /// <summary>
    /// The deletes still go once every snapshot has landed, and only then.
    /// </summary>
    [Test]
    public async Task AGroupAcceptDeletesOnceItsSnapshotsLanded()
    {
        using var owner = new ServerFixture();
        var (verified, host, link) = AttachToSolutionA(owner);
        try
        {
            AcceptAllInSolutionA(host, link);

            // In this order: a delete sent before the patch landed is what this is about
            await Assert.That(string.Join(" | ", owner.Actions))
                .IsEqualTo($"apply {solutionA.SourceFile} | delete {verified}");
            await Assert.That(host.State.Queue.Select(_ => _.Key)).IsEquivalentTo([QueueEntry.KeyForInline(solutionB.SourceFile, solutionB.LineHint)]);
        }
        finally
        {
            File.Delete(verified);
        }
    }

    static readonly InlinePatch solutionA = Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 10);
    static readonly InlinePatch solutionB = Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 10);

    /// <summary>
    /// An owner holding a snapshot and a pending delete in SolutionA, and a snapshot in SolutionB
    /// so the queue groups, with a window attached to it.
    /// </summary>
    static (string verified, SessionHost host, OwnerLink link) AttachToSolutionA(ServerFixture owner)
    {
        var verified = Fixtures.SolutionFile("SolutionA", "Tests", $"Group{Guid.NewGuid():N}.verified.txt");
        File.WriteAllText(verified, "the verified file the snapshot is moving inline from");
        owner.Send(Inline(solutionA));
        owner.Host.Mutate(_ => ViewerSession.EnqueueTracked(_, TrackedEntry.ForDelete(verified)));
        owner.Send(Inline(solutionB));
        var (host, link) = Attach(owner);
        link.Pump();
        return (verified, host, link);
    }

    /// <summary>
    /// Right-click SolutionA's header and choose "Accept all in SolutionA", as the reader would.
    /// </summary>
    static void AcceptAllInSolutionA(SessionHost host, OwnerLink link)
    {
        var visible = QueueProjection.Visible(host.State, ScreenBuilder.BodyRows(host.State), out _).ToList();
        var header = visible.FindIndex(_ => _.GroupName == "SolutionA");
        host.Mutate(_ => ViewerSession.OpenMenu(_, header));
        var item = host.State.Menu!.Items.ToList().FindIndex(_ => _.Label == "Accept all in SolutionA");
        var click = new ViewerInput(CommandKind.None, -1, -1, 0, false, Fixtures.Columns, Fixtures.Rows)
        {
            ClickedMenuItem = item
        };
        host.Mutate(_ => ViewerProgram.Apply(_, click, link, new NoWindow()));
        link.Pump();
    }

    sealed class NoWindow : IViewerWindow
    {
        public bool Present(Screen screen) =>
            true;

        public ViewerInput Poll() =>
            default;

        public void SetHidden(bool hidden)
        {
        }

        public void Focus()
        {
        }

        public void SetClipboard(string text)
        {
        }

        public bool Capture(Screen screen, int width, int height, string pngPath) =>
            false;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// A tracked file the owner lists that this side cannot open. The listing is the same from one
    /// pump to the next, so the entry should be too: rebuilt anyway, it replaced the one on screen
    /// every 200ms and closed the reader's open menu with it.
    /// </summary>
    [Test]
    public async Task AnUnreadableTrackedFileLeavesTheMenuOpen()
    {
        var file = Path.Combine(Path.GetTempPath(), $"AttachedViewerTests_{Guid.NewGuid():N}.verified.txt");
        await File.WriteAllTextAsync(file, "locked away");
        try
        {
            await using var holder = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
            if (!ViewerServer.TryBind(0, out var server))
            {
                throw new("Could not bind an ephemeral port.");
            }

            using (server)
            using (var cancel = new CancelSource())
            {
                _ = server.Listen(
                    _ => ViewerResponse.Listing(
                        [],
                        deletes: [new(TrackedKeys.ForDelete(file), "Extra.verified.txt", null, file)]),
                    cancel.Token);
                var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));
                var link = new OwnerLink(host, server.Port);

                await Assert.That(link.Pump()).IsTrue();
                var first = host.State.Queue.Single();
                await Assert.That(first.Warning).Contains("Could not read");
                var row = QueueProjection.Rows(host.State).ToList().FindIndex(_ => _.Kind == QueueRowKind.Entry);
                host.Mutate(_ => ViewerSession.OpenMenu(_, row));
                await Assert.That(host.State.Menu).IsNotNull();

                link.Pump();

                await Assert.That(host.State.Menu).IsNotNull();
                await Assert.That(host.State.Queue.Single()).IsSameReferenceAs(first);
                await cancel.CancelAsync();
            }
        }
        finally
        {
            File.Delete(file);
        }
    }
}
