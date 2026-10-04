/// <summary>
/// A bulk discard throws a received file away for every pending pair, and used to do all of it in
/// one transition: under the lock every arrival waits on, and for a window's own discard on the
/// thread that draws. It is the batch an accept-all is, over the moves: the snapshots and the
/// pending deletes go as it begins, since neither touches a file, and each received file is
/// thrown away in a step of its own, outside the lock.
/// </summary>
public class DiscardBatchTests
{
    [Test]
    public async Task BeginningTouchesNoFile()
    {
        var deleted = new List<string>();
        var state = ViewerSession.BeginDiscardAll(Mixed());

        // The snapshot and the pending delete are gone, and the two moves are still listed
        await Assert.That(state.Queue.Select(_ => _.Kind)).IsEquivalentTo([QueueEntryKind.Move, QueueEntryKind.Move]);
        await Assert.That(state.Batch!.Discarding).IsTrue();
        await Assert.That(state.Batch.Current).IsNull();
        await Assert.That(deleted).IsEmpty();

        var seen = new List<string>();
        while ((state = ViewerSession.ClaimNext(state)).Batch?.Current is not null)
        {
            seen.Add($"{ScreenBuilder.Build(state).Status}, {state.Queue.Count} pending, {deleted.Count} thrown away");
            state = ViewerSession.ApplyClaimed(state, Deleting(deleted.Add))(state);
        }

        await Assert.That(string.Join("\n", seen)).IsEqualTo(
            """
            Discarding 1 of 2, 2 pending, 0 thrown away
            Discarding 2 of 2, 1 pending, 1 thrown away
            """);
        await Assert.That(deleted.Count).IsEqualTo(2);
        await Assert.That(state.Batch).IsNull();
        await Assert.That(state.Message).IsEqualTo("Discarded 1, plus 3 files");
        await Assert.That(state.Exit).IsTrue();
    }

    /// <summary>
    /// A step at a time and in one call come to the same thing, which is what a discard-all was
    /// before it was a batch: the tests and the wire's single discards still drive it whole.
    /// </summary>
    [Test]
    public async Task InOneCallItSaysWhatTheStepsSay()
    {
        var deleted = new List<string>();

        var state = ViewerSession.Apply(Mixed(), CommandKind.DiscardAll, Deleting(deleted.Add));

        await Assert.That(deleted.Count).IsEqualTo(2);
        await Assert.That(state.Queue).IsEmpty();
        await Assert.That(state.Message).IsEqualTo("Discarded 1, plus 3 files");
    }

    /// <summary>
    /// A received file that could not be thrown away stays pending, saying why, and is counted.
    /// </summary>
    [Test]
    public async Task AFileThatWouldNotGoIsKept()
    {
        var actions = Deleting(_ => throw new IOException("the file is held"));

        var state = ViewerSession.Apply(Mixed(), CommandKind.DiscardAll, actions);

        await Assert.That(state.Queue.Count).IsEqualTo(2);
        await Assert.That(state.Queue.All(_ => _.Status == "the file is held")).IsTrue();
        await Assert.That(state.Message).IsEqualTo("Discarded 1, plus 1 files (2 kept)");
        await Assert.That(state.Batch).IsNull();
    }

    /// <summary>
    /// A header's discard is the same batch over its own members, and leaves everything else.
    /// </summary>
    [Test]
    public async Task AGroupsDiscardIsTheBatchOverItsMembers()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 10),
            Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 20));
        state = ViewerSession.EnqueueTracked(state, Fixtures.Move("One.Test (txt)", "SolutionA"));
        state = ViewerSession.EnqueueTracked(state, Fixtures.Move("Two.Test (txt)", "SolutionB"));
        var header = QueueProjection.Rows(state).ToList().FindIndex(_ => _.GroupName == "SolutionA");
        var deleted = new List<string>();

        var begun = ViewerSession.BeginDiscardGroup(ViewerSession.OpenMenu(state, header));

        await Assert.That(begun.Menu).IsNull();
        await Assert.That(begun.Batch!.Remaining.Count).IsEqualTo(1);
        await Assert.That(begun.Queue.Count).IsEqualTo(3);
        var done = begun;
        while ((done = ViewerSession.ClaimNext(done)).Batch?.Current is not null)
        {
            done = ViewerSession.ApplyClaimed(done, Deleting(deleted.Add))(done);
        }

        await Assert.That(deleted.Count).IsEqualTo(1);
        await Assert.That(string.Join(",", done.Queue.Select(_ => _.Solution))).IsEqualTo("SolutionB,SolutionB");
        await Assert.That(done.Message).IsEqualTo("Discarded 1, plus 1 files");
    }

    /// <summary>
    /// What the batch is for. A discard-all asked over the socket throws each file away with the
    /// session's lock free, so the render loop and an arriving snapshot get in between two of
    /// them: seen by another thread taking the lock from inside a delete, which waited for the
    /// whole discard when the discard was one mutation.
    /// </summary>
    [Test]
    public async Task AFileIsThrownAwayOutsideTheLock()
    {
        var host = new SessionHost(Mixed());
        var took = new List<bool>();
        var actions = Deleting(_ =>
        {
            var taking = new Thread(() => host.Mutate(static _ => _))
            {
                IsBackground = true
            };
            taking.Start();
            // Held, it is never let go while this delete waits, so this is only ever the whole
            // wait when the test is failing
            took.Add(taking.Join(TimeSpan.FromSeconds(20)));
        });
        IQueueOwner owner = new MessageHandler(host, actions, _ => { });

        var message = owner.DiscardAll();

        await Assert.That(took.Count).IsEqualTo(2);
        await Assert.That(took.All(_ => _)).IsTrue();
        await Assert.That(message).IsEqualTo("Discarded 1, plus 3 files");
        await Assert.That(host.State.Queue).IsEmpty();
    }

    /// <summary>
    /// A window's own discard-all is only begun by the frame that asked for it, as its accept-all
    /// is, and the buttons that change the queue wait for it.
    /// </summary>
    [Test]
    public async Task AWindowOnlyBeginsItsDiscard()
    {
        var state = ViewerProgram.Apply(Mixed(), Input(CommandKind.DiscardAll), null, new Window());

        await Assert.That(state.Batch!.Discarding).IsTrue();
        await Assert.That(state.Queue.Count).IsEqualTo(2);
        var screen = ScreenBuilder.Build(state);
        await Assert.That(screen.Buttons.Where(_ => _.Command is CommandKind.Accept or CommandKind.Discard or CommandKind.AcceptAll).Any(_ => _.Enabled)).IsFalse();
        // And a listing says so, as a discard
        await Assert.That(state.ListedProgress).IsEqualTo(Discarding(0, 2));
    }

    /// <summary>
    /// A discard under way was on none of its owner's listings, so whoever displayed the queue
    /// saw it shrink with nothing saying why, and refused nothing meanwhile. Asked from inside a
    /// delete, which is the batch part way through: each listing says how far the discard has
    /// got and that it is one, under a tag of its own, and the one after it says nothing.
    /// </summary>
    [Test]
    public async Task AListingDuringADiscardSaysHowFarItHasGot()
    {
        var host = new SessionHost(Mixed());
        var listed = new List<AcceptProgress>();
        var tags = new List<string>();
        IQueueOwner? owner = null;
        var actions = Deleting(_ =>
        {
            listed.Add(owner!.Listing(true).Progress ?? new(-1, -1));
            tags.Add(owner.ListingTag() ?? "");
        });
        owner = new MessageHandler(host, actions, _ => { });

        owner.DiscardAll();

        await Assert.That(listed).IsEquivalentTo([Discarding(0, 2), Discarding(1, 2)]);
        await Assert.That(owner.Listing(true).Progress).IsNull();
        tags.Add(owner.ListingTag() ?? "");
        await Assert.That(tags.Distinct().Count()).IsEqualTo(3);
    }

    /// <summary>
    /// What a window displaying that queue does with it: says discarding, in the words the owner's
    /// own window uses, and refuses what changes the queue until the owner is done.
    /// </summary>
    [Test]
    public async Task AnAttachedWindowFollowsTheOwnersDiscard()
    {
        var attached = Fixtures.Attached(Fixtures.Pending(), Fixtures.Move("One.Test (txt)"), Fixtures.Move("Two.Test (txt)"));
        var state = ViewerSession.Sync(attached, Fixtures.Pending(), [..attached.Queue], null, Discarding(0, 2));

        var screen = ScreenBuilder.Build(state);
        await Assert.That(screen.Status).IsEqualTo("Discarding 1 of 2");
        await Assert.That(screen.Buttons.Where(_ => _.Command is CommandKind.Accept or CommandKind.Discard or CommandKind.AcceptAll).Any(_ => _.Enabled)).IsFalse();
        await Assert.That(ViewerProgram.Apply(state, Input(CommandKind.Discard), null, new Window()).Queue).IsSameReferenceAs(state.Queue);

        var done = ViewerSession.Sync(state, Fixtures.Pending(), [state.Queue[1]], null);
        await Assert.That(done.Progress).IsNull();
    }

    static AcceptProgress Discarding(int done, int total) =>
        new(done, total)
        {
            Discarding = true
        };

    // A snapshot, two pending moves and a pending delete
    static SessionState Mixed()
    {
        var state = Fixtures.Inline(Fixtures.Patch());
        state = ViewerSession.EnqueueTracked(state, Fixtures.Move("One.Test (txt)"));
        state = ViewerSession.EnqueueTracked(state, Fixtures.Move("Two.Test (txt)"));
        return ViewerSession.EnqueueTracked(state, Fixtures.Delete());
    }

    static ViewerActions Deleting(Action<string> delete) =>
        Fixtures.Applied with
        {
            MoveFile = static (_, _) => throw new("A discard moves nothing."),
            DeleteFile = delete
        };

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
