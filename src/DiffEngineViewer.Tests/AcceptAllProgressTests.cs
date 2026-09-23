/// <summary>
/// An accept-all takes as long as the queue is long, so it goes an entry at a time: each one
/// leaves the list as it lands, the status line says how far the batch has got, and the window
/// keeps drawing throughout - whichever process is holding the queue.
/// </summary>
[NotInParallel]
public class AcceptAllProgressTests
{
    [Test]
    public async Task EachEntryLeavesAsItLands()
    {
        var state = ViewerSession.BeginAcceptAll(Pending());
        var seen = new List<string>();
        while ((state = ViewerSession.ClaimNext(state)).Batch?.Current is { } entry)
        {
            seen.Add($"{ScreenBuilder.Build(state).Status}, {state.Queue.Count} pending");
            state = ViewerSession.ApplyClaimed(entry, Fixtures.Applied)(state);
        }

        await Assert.That(string.Join("\n", seen)).IsEqualTo(
            """
            Accepting 1 of 3, 3 pending
            Accepting 2 of 3, 2 pending
            Accepting 3 of 3, 1 pending
            """);
        await Assert.That(state.Progress).IsNull();
        await Assert.That(state.Message).IsEqualTo("Accepted 3");
        await Assert.That(state.Exit).IsTrue();
    }

    /// <summary>
    /// The files are part of the batch as much as the snapshots are, and come after them: whether
    /// a delete is held turns on how the snapshots went.
    /// </summary>
    [Test]
    public async Task TheFilesAreCountedAfterTheSnapshots()
    {
        var done = new List<string>();
        var actions = Fixtures.Applied with
        {
            MoveFile = (temp, _) => done.Add(temp),
            DeleteFile = done.Add
        };
        var state = ViewerSession.EnqueueTracked(Pending(), Fixtures.Move());
        state = ViewerSession.EnqueueTracked(state, Fixtures.Delete());

        state = ViewerSession.BeginAcceptAll(state);
        var kinds = new List<string>();
        while ((state = ViewerSession.ClaimNext(state)).Batch?.Current is { } entry)
        {
            kinds.Add($"{state.Progress!.Describe()}: {entry.Kind}");
            state = ViewerSession.ApplyClaimed(entry, actions)(state);
        }

        await Assert.That(string.Join("\n", kinds)).IsEqualTo(
            """
            Accepting 1 of 5: Inline
            Accepting 2 of 5: Inline
            Accepting 3 of 5: Inline
            Accepting 4 of 5: Move
            Accepting 5 of 5: Delete
            """);
        await Assert.That(state.Message).IsEqualTo("Accepted 3, plus 2 files");
        await Assert.That(done.Count).IsEqualTo(2);
    }

    /// <summary>
    /// A test that starts passing while the batch is under way settles its entry, and the batch
    /// passes over it rather than accepting a snapshot that is no longer pending.
    /// </summary>
    [Test]
    public async Task AnEntrySettledPartWayIsPassedOver()
    {
        var applied = new List<InlinePatch>();
        var actions = Fixtures.Applied with
        {
            ApplyInline = _ =>
            {
                applied.Add(_);
                return InlineApplyResult.Applied;
            }
        };
        var state = ViewerSession.BeginAcceptAll(Pending());
        state = ViewerSession.ClaimNext(state);
        var first = state.Batch!.Current!;
        state = ViewerSession.ApplyClaimed(first, actions)(state);

        var next = state.Batch!.Remaining[0];
        state = ViewerSession.Settle(state, next);
        while ((state = ViewerSession.ClaimNext(state)).Batch?.Current is { } entry)
        {
            state = ViewerSession.ApplyClaimed(entry, actions)(state);
        }

        await Assert.That(applied.Count).IsEqualTo(2);
        await Assert.That(state.Message).IsEqualTo("Accepted 2");
    }

    /// <summary>
    /// A still failing test that re-runs while its snapshot is being written sends different
    /// content, and that content is the news: the outcome describes what was there before, so the
    /// entry stays with the new content and the batch does not count it.
    /// </summary>
    [Test]
    public async Task AReRunWhileItAppliesKeepsTheNewContent()
    {
        var state = ViewerSession.BeginAcceptAll(Fixtures.Inline(Fixtures.Patch()));
        state = ViewerSession.ClaimNext(state);
        var record = ViewerSession.ApplyClaimed(state.Batch!.Current!, Fixtures.Applied);

        state = ViewerSession.EnqueueInline(state, Fixtures.Patch(content: "third run"));
        state = ViewerSession.ClaimNext(record(state));

        await Assert.That(state.Queue.Single().LeftText).IsEqualTo("third run");
        await Assert.That(state.Batch).IsNull();
        await Assert.That(state.Message).IsEqualTo("Accepted 0");
    }

    /// <summary>
    /// A second framework reporting different content for an entry the batch has not reached yet
    /// makes a conflict of it, and a bulk accept never picks a side.
    /// </summary>
    [Test]
    public async Task AnEntryThatBecameAConflictIsLeftForReview()
    {
        var state = ViewerSession.BeginAcceptAll(
            Fixtures.Inline(
                Fixtures.Patch("ATests.cs", 1, framework: "net8.0"),
                Fixtures.Patch("BTests.cs", 2, framework: "net8.0")));
        state = ViewerSession.ClaimNext(state);
        state = ViewerSession.ApplyClaimed(state.Batch!.Current!, Fixtures.Applied)(state);

        state = ViewerSession.EnqueueInline(state, Fixtures.Patch("BTests.cs", 2, content: "nine", framework: "net9.0"));
        state = ViewerSession.ClaimNext(state);

        await Assert.That(state.Batch).IsNull();
        await Assert.That(state.Queue.Single().Conflicted).IsTrue();
        await Assert.That(state.Message).IsEqualTo("Accepted 1, 1 conflict needs review");
    }

    /// <summary>
    /// Nothing to apply is nothing to wait for: no progress to report, and the message at once.
    /// </summary>
    [Test]
    public async Task AQueueOfConflictsFinishesWhereItStarts()
    {
        var state = ViewerSession.BeginAcceptAll(
            Fixtures.Inline(
                Fixtures.Patch(content: "eight", framework: "net8.0"),
                Fixtures.Patch(content: "nine", framework: "net9.0")));

        await Assert.That(state.Batch).IsNull();
        await Assert.That(state.Message).IsEqualTo("Accepted 0, 1 conflict needs review");
    }

    /// <summary>
    /// A batch that is running is the one a second request was asking for, so starting another
    /// changes nothing.
    /// </summary>
    [Test]
    public async Task ASecondBeginLeavesTheRunningBatch()
    {
        var running = ViewerSession.ClaimNext(ViewerSession.BeginAcceptAll(Pending()));

        await Assert.That(ViewerSession.BeginAcceptAll(running)).IsSameReferenceAs(running);
    }

    /// <summary>
    /// The window refuses what would change the queue under the batch, so the buttons that would
    /// are disabled, and the keys and a second accept-all do nothing. Looking around carries on.
    /// </summary>
    [Test]
    public async Task TheWindowOffersNothingThatChangesTheQueueWhileItRuns()
    {
        var state = ViewerSession.ClaimNext(ViewerSession.BeginAcceptAll(Pending()));
        var screen = ScreenBuilder.Build(state);

        await Assert.That(screen.Status).IsEqualTo("Accepting 1 of 3");
        await Assert.That(EnabledQueueButtons(screen)).IsEmpty();

        var window = new Window();
        foreach (var key in queueCommands)
        {
            var pressed = ViewerProgram.Apply(state, Input(key), null, window);
            await Assert.That(pressed.Queue).IsSameReferenceAs(state.Queue);
            await Assert.That(pressed.Batch).IsEqualTo(state.Batch);
        }

        var stepped = ViewerProgram.Apply(state, Input(CommandKind.NextItem), null, window);
        await Assert.That(stepped.Selected).IsEqualTo(state.Selected + 1);
    }

    /// <summary>
    /// A viewer displaying someone else's queue shows the owner's batch the same way, from what
    /// the owner's listing said about it.
    /// </summary>
    [Test]
    public async Task AnAttachedWindowShowsTheOwnersProgress()
    {
        var state = ViewerSession.Sync(
            Fixtures.Attached(Fixtures.Pending(Fixtures.Patch())),
            Fixtures.Pending(Fixtures.Patch()),
            [],
            null,
            new(4, 9));

        var screen = ScreenBuilder.Build(state);
        await Assert.That(screen.Status).IsEqualTo("Accepting 5 of 9");
        await Assert.That(EnabledQueueButtons(screen)).IsEmpty();

        // And the listing after the batch is what gives the window back
        var after = ViewerSession.Sync(state, Fixtures.Pending(Fixtures.Patch()), [], "Accepted 8");
        await Assert.That(ScreenBuilder.Build(after).Status).IsEqualTo("Accepted 8");
    }

    /// <summary>
    /// The point of the runner: the lock is free while an entry applies, so the render loop - which
    /// takes it every frame - keeps drawing, and what it draws is the batch as it now stands.
    /// </summary>
    [Test]
    public async Task TheRunnerAppliesOutsideTheLock()
    {
        var host = new SessionHost(Pending());
        var seen = new List<string>();
        var actions = Fixtures.Applied with
        {
            ApplyInline = _ =>
            {
                // Another thread, standing in for the render loop, since the lock lets the
                // thread that holds it back in
                var drawn = Task.Run(() => host.Mutate(state => state));
                if (!drawn.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new("The lock was held across an apply.");
                }

                seen.Add(ScreenBuilder.Build(drawn.Result).Status);
                return InlineApplyResult.Applied;
            }
        };
        host.Mutate(ViewerSession.BeginAcceptAll);

        var message = new AcceptAllRunner(host, actions).Drive();

        await Assert.That(string.Join("\n", seen)).IsEqualTo(
            """
            Accepting 1 of 3
            Accepting 2 of 3
            Accepting 3 of 3
            """);
        await Assert.That(message).IsEqualTo("Accepted 3");
        await Assert.That(host.State.Queue).IsEmpty();
    }

    /// <summary>
    /// An applier that throws rather than answering still leaves a batch that finishes, with the
    /// throw recorded against the entry like any other failure.
    /// </summary>
    [Test]
    public async Task AThrowingApplyStillFinishesTheBatch()
    {
        var host = new SessionHost(Pending());
        var actions = Fixtures.Applied with
        {
            ApplyInline = _ => _.LineHint == 88 ? throw new("the disk went away") : InlineApplyResult.Applied
        };
        host.Mutate(ViewerSession.BeginAcceptAll);

        var message = new AcceptAllRunner(host, actions).Drive();

        await Assert.That(message).IsEqualTo("Accepted 2, 1 failed. the disk went away");
        await Assert.That(host.State.Batch).IsNull();
        await Assert.That(host.State.Queue.Single().Status).IsEqualTo("the disk went away");
    }

    /// <summary>
    /// The tray's accept-all against a viewer that owns the queue: the listener thread carries it
    /// out, and a listing taken part way through says how far it has got and no longer lists what
    /// has been accepted - which is the window's view of it too.
    /// </summary>
    [Test]
    public async Task AListingDuringAWireAcceptAllSaysHowFarItHasGot()
    {
        using var held = new HeldApply(2);
        using var owner = new ServerFixture(applier: held.Apply);
        owner.Send(Inline(Fixtures.Patch()));
        owner.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));

        // Waited on for as long as the test holds the batch, which the client's default does not
        var accepting = Task.Run(() => owner.Send(new(ViewerVerb.AcceptAll), TimeSpan.FromSeconds(30)));
        held.WaitUntilHeld();

        var partway = owner.Send(new(ViewerVerb.ListFull));
        await Assert.That(partway.Progress).IsEqualTo(new(1, 2));
        await Assert.That(partway.Items).HasSingleItem();
        await Assert.That(ScreenBuilder.Build(owner.Host.State).Status).IsEqualTo("Accepting 2 of 2");

        held.Release();
        var response = await accepting;

        await Assert.That(response.Message).IsEqualTo("Accepted 2");
        await Assert.That(owner.Send(new(ViewerVerb.ListFull)).Progress).IsNull();
    }

    /// <summary>
    /// A viewer displaying the queue follows an accept-all it forwarded while the owner is still
    /// carrying it out, rather than waiting on the one exchange that answers only at the end.
    /// </summary>
    [Test]
    public async Task AnAttachedViewerFollowsTheOwnerThroughTheBatch()
    {
        using var held = new HeldApply(2);
        using var owner = new ServerFixture(applier: held.Apply);
        owner.Send(Inline(Fixtures.Patch()));
        owner.Send(Inline(Fixtures.Patch("OtherTests.cs", 7, null, "new")));
        var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));
        var link = new OwnerLink(host, owner.Server.Port);
        using var cancel = new CancelSource();
        var polling = Task.Run(() => link.Run(cancel.Token), Cancel.None);
        await Until(() => host.State.Queue.Count == 2);

        link.Post(ViewerVerb.AcceptAll, null);
        held.WaitUntilHeld();
        // A listing taken while the first entry was still applying can land first, so wait for one
        // taken with the second held
        await Until(() => host.State.OwnerProgress is { Done: 1, Total: 2 });

        await Assert.That(ScreenBuilder.Build(host.State).Status).IsEqualTo("Accepting 2 of 2");
        await Assert.That(host.State.Queue).HasSingleItem();

        held.Release();
        // The last entry going closes a window that is only displaying someone else's queue
        await Until(() => host.State.Exit);
        await Assert.That(host.State.OwnerProgress).IsNull();
        await cancel.CancelAsync();
        await polling.WaitAsync(TimeSpan.FromSeconds(10));
    }

    static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new("Timed out waiting for the window to catch up.");
            }

            await Task.Delay(20);
        }
    }

    static ViewerMessage Inline(InlinePatch patch) =>
        new(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch));

    static SessionState Pending() =>
        Fixtures.Inline(
            Fixtures.Patch(),
            Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two"),
            Fixtures.Patch("OtherTests.cs", 12, null, "brand new"));

    /// <summary>
    /// What a batch refuses. Moving between changes and switching views only change what is being
    /// read, so those buttons stay live while one runs.
    /// </summary>
    static readonly CommandKind[] queueCommands = [CommandKind.Accept, CommandKind.Discard, CommandKind.AcceptAll];

    static IEnumerable<Button> EnabledQueueButtons(Screen screen) =>
        screen.Buttons.Where(_ => _.Enabled && queueCommands.Contains(_.Command));

    static ViewerInput Input(CommandKind key) =>
        new(key, -1, -1, 0, false, Fixtures.Columns, Fixtures.Rows);

    sealed class Window : IViewerWindow
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
}
