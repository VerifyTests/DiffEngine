/// <summary>
/// The pass that keeps an owned queue in step with the disk, over real files, because what it is
/// for is entirely about what the file system says: an entry whose received file has gone stops
/// being pending, and one whose file was rewritten shows the rewrite.
/// <para>
/// The rewrites here change the length as well as the content. A stamp is the write time and the
/// length, and a file system's write time granularity is coarse enough that two writes inside one
/// test can share one - so a same-length rewrite is a test that passes or fails on how fast the
/// machine is.
/// </para>
/// </summary>
public class TrackedWatchTests :
    IDisposable
{
    [Test]
    public async Task AVanishedReceivedFileDropsThePair()
    {
        var (temp, target) = Pair("Sample.Test");
        var host = Owned(TrackedEntry.ForMove(temp, target));
        File.Delete(temp);

        new TrackedWatch(host).Pump();

        await Assert.That(host.State.Queue).IsEmpty();
    }

    /// <summary>
    /// The other side is not the same thing. A brand new snapshot has no verified file at all, and
    /// offering to create it is the whole point of the entry.
    /// </summary>
    [Test]
    public async Task AVanishedTargetKeepsThePair()
    {
        var (temp, target) = Pair("Sample.Test");
        var host = Owned(TrackedEntry.ForMove(temp, target));
        File.Delete(target);

        new TrackedWatch(host).Pump();

        var entry = host.State.Queue.Single();
        await Assert.That(entry.Kind).IsEqualTo(QueueEntryKind.Move);
        await Assert.That(entry.RightText).IsEmpty();
    }

    [Test]
    public async Task ARewrittenReceivedFileReachesThePane()
    {
        var (temp, target) = Pair("Sample.Test");
        var host = Owned(TrackedEntry.ForMove(temp, target));
        await File.WriteAllTextAsync(temp, "rewritten by a later run");

        new TrackedWatch(host).Pump();

        await Assert.That(host.State.Queue.Single().LeftText).IsEqualTo("rewritten by a later run");
    }

    /// <summary>
    /// Accepting the pair elsewhere - the tray menu, an IDE, a hand copy - creates the target, and
    /// a window still offering the old empty side is describing a comparison nobody has.
    /// </summary>
    [Test]
    public async Task ACreatedTargetReachesThePane()
    {
        var temp = Path.Combine(directory, "New.Test.received.txt");
        var target = Path.Combine(directory, "New.Test.verified.txt");
        await File.WriteAllTextAsync(temp, "received");
        var host = Owned(TrackedEntry.ForMove(temp, target));
        await File.WriteAllTextAsync(target, "now verified");

        new TrackedWatch(host).Pump();

        await Assert.That(host.State.Queue.Single().RightText).IsEqualTo("now verified");
    }

    [Test]
    public async Task AVanishedDeleteFileDropsTheEntry()
    {
        var file = Path.Combine(directory, "Extra.verified.txt");
        await File.WriteAllTextAsync(file, "doomed");
        var host = Owned(TrackedEntry.ForDelete(file));
        File.Delete(file);

        new TrackedWatch(host).Pump();

        await Assert.That(host.State.Queue).IsEmpty();
    }

    /// <summary>
    /// The pass runs several times a second for as long as the window is up, so one that found
    /// nothing has to leave the state alone rather than replace it with an equal one.
    /// </summary>
    [Test]
    public async Task APassOverUnchangedFilesChangesNothing()
    {
        var (temp, target) = Pair("Sample.Test");
        var host = Owned(TrackedEntry.ForMove(temp, target));
        var before = host.State;

        new TrackedWatch(host).Pump();

        await Assert.That(host.State).IsSameReferenceAs(before);
    }

    /// <summary>
    /// An inline entry has no file on disk to follow - its content came over the socket - so a
    /// pass has to walk straight past it rather than reading its null paths.
    /// </summary>
    [Test]
    public async Task InlineEntriesAreLeftAlone()
    {
        var (temp, target) = Pair("Sample.Test");
        var host = Owned(TrackedEntry.ForMove(temp, target));
        host.Mutate(_ => ViewerSession.EnqueueInline(_, Fixtures.Patch()));
        File.Delete(temp);

        new TrackedWatch(host).Pump();

        await Assert.That(host.State.Queue.Single().Kind).IsEqualTo(QueueEntryKind.Inline);
    }

    static SessionHost Owned(QueueEntry entry) =>
        new(
            ViewerSession.EnqueueTracked(
                SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows),
                entry));

    (string Temp, string Target) Pair(string name)
    {
        var temp = Path.Combine(directory, $"{name}.received.txt");
        var target = Path.Combine(directory, $"{name}.verified.txt");
        File.WriteAllText(temp, "received");
        File.WriteAllText(target, "verified");
        return (temp, target);
    }

    readonly string directory = Path.Combine(Path.GetTempPath(), $"TrackedWatchTests_{Guid.NewGuid():N}");

    public TrackedWatchTests() =>
        Directory.CreateDirectory(directory);

    public void Dispose() =>
        Directory.Delete(directory, true);

    /// <summary>
    /// The real pass, parked between its stat and its apply by holding the host's lock: the stat
    /// runs outside the lock and the apply inside it. A re-run clears its old received file, the
    /// pass finds it gone, and before that is applied the re-run writes the new one and the pair
    /// arrives again under the same key. Dropped by key, the new pair went with the old.
    /// </summary>
    [Test]
    public async Task APassDoesNotDropAPairReStagedAfterItsStat()
    {
        var (temp, target) = Pair("Sample.Test");
        var host = Owned(TrackedEntry.ForMove(temp, target));
        new TrackedWatch(host).Pump();
        File.Delete(temp);

        Thread? pass = null;
        var parked = false;
        host.Mutate(state =>
        {
            pass = new(() => new TrackedWatch(host).Pump());
            pass.Start();
            parked = WaitUntilBlocked(pass);

            File.WriteAllText(temp, "second run");
            return ViewerSession.EnqueueTracked(state, TrackedEntry.ForMove(temp, target));
        });
        pass!.Join();

        await Assert.That(parked).IsTrue();
        await Assert.That(host.State.Queue.Select(_ => _.LeftText)).IsEquivalentTo(["second run"]);
    }

    /// <summary>
    /// The same interleaving from its two halves: what the pass found about the entry it saw, and
    /// the arrival that replaced that entry before the finding was applied.
    /// </summary>
    [Test]
    public async Task ARefreshLeavesAnEntryThatArrivedAfterThePass()
    {
        var (temp, target) = Pair("Other.Test");
        var seen = TrackedEntry.ForMove(temp, target);
        var state = Owned(seen).State;
        await File.WriteAllTextAsync(temp, "second run");
        var restaged = TrackedEntry.ForMove(temp, target);
        state = ViewerSession.EnqueueTracked(state, restaged);

        var refreshed = ViewerSession.Refresh(state, [seen], [(seen, Fixtures.Move())]);

        await Assert.That(refreshed).IsSameReferenceAs(state);
    }

    /// <summary>
    /// A file that stats but cannot be read is not re-read and re-diffed on every pass while it
    /// stays that way, and the entry for it is left alone.
    /// </summary>
    [Test]
    public async Task AnUnreadableFileIsNotReReadEveryPass()
    {
        var (temp, target) = Pair("Locked.Test");
        var host = Owned(TrackedEntry.ForMove(temp, target));
        var watch = new TrackedWatch(host);
        await File.WriteAllTextAsync(temp, "rewritten and then held");
        await using (new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            watch.Pump();
            var held = host.State;

            watch.Pump();
            watch.Pump();

            await Assert.That(host.State).IsSameReferenceAs(held);
        }
    }

    /// <summary>
    /// A queue longer than a pass looks at is looked at in turn. A thousand pending pairs were two
    /// thousand stats a pass, five passes a second, for as long as the viewer ran.
    /// </summary>
    [Test]
    public async Task AQueueLongerThanAPassLooksAtIsLookedAtInTurn()
    {
        var host = OwnedAll(5);
        var watch = new TrackedWatch(host)
        {
            Budget = 2
        };
        // The last in the queue, which a pass that starts at the front does not get to
        File.Delete(host.State.Queue[^1].LeftFile!);

        watch.Pump();
        await Assert.That(host.State.Queue.Count).IsEqualTo(5);

        watch.Pump();
        await Assert.That(host.State.Queue.Count).IsEqualTo(4);
    }

    /// <summary>
    /// And one no longer than that is looked at whole every pass, as every queue used to be.
    /// </summary>
    [Test]
    public async Task AQueueNoLongerThanAPassLooksAtIsLookedAtWhole()
    {
        var host = OwnedAll(5);
        var watch = new TrackedWatch(host)
        {
            Budget = 4
        };
        File.Delete(host.State.Queue[^1].LeftFile!);

        watch.Pump();

        await Assert.That(host.State.Queue.Count).IsEqualTo(4);
    }

    /// <summary>
    /// The entry on screen is the one being read, so it is looked at every pass, wherever the turn
    /// has got to.
    /// </summary>
    [Test]
    public async Task TheEntryOnScreenIsLookedAtEveryPass()
    {
        var host = OwnedAll(5);
        var watch = new TrackedWatch(host)
        {
            Budget = 1
        };
        // The turn moves on past the first entry, which is the one on screen
        watch.Pump();
        watch.Pump();
        await Assert.That(host.State.Selected).IsEqualTo(0);
        await File.WriteAllTextAsync(host.State.Current!.LeftFile!, "rewritten by a later run");

        watch.Pump();

        await Assert.That(host.State.Current!.LeftText).IsEqualTo("rewritten by a later run");
    }

    /// <summary>
    /// A snapshot has no file to look at, so it takes nothing from what a pass looks at: the pairs
    /// behind a queue's snapshots are still all reached.
    /// </summary>
    [Test]
    public async Task SnapshotsTakeNothingFromWhatAPassLooksAt()
    {
        // Two snapshots ahead of three pairs, and a pass that looks at three files
        var state = Fixtures.Inline(Fixtures.Patch("OneTests.cs", 10), Fixtures.Patch("TwoTests.cs", 20));
        for (var index = 0; index < 3; index++)
        {
            var (temp, target) = Pair($"Sample{index}.Test");
            state = ViewerSession.EnqueueTracked(state, TrackedEntry.ForMove(temp, target));
        }

        var host = new SessionHost(state);
        var watch = new TrackedWatch(host)
        {
            Budget = 3
        };
        await Assert.That(string.Join(" ", host.State.Queue.Select(_ => _.Kind))).IsEqualTo("Inline Inline Move Move Move");
        File.Delete(host.State.Queue[^1].LeftFile!);

        watch.Pump();

        await Assert.That(host.State.Queue.Count(_ => _.Kind == QueueEntryKind.Move)).IsEqualTo(2);
        await Assert.That(host.State.Queue.Count(_ => _.Kind == QueueEntryKind.Inline)).IsEqualTo(2);
    }

    /// <summary>
    /// A run that fails the same way writes its received file again with what it held. The entry
    /// is the one it was with a new stamp: its rows are not built again, since building them is
    /// the diff, and the pass after has nothing to do.
    /// </summary>
    [Test]
    public async Task AFileWrittenAgainWithWhatItHeldIsNotDiffedAgain()
    {
        var (temp, target) = Pair("Sample.Test");
        var host = Owned(TrackedEntry.ForMove(temp, target));
        var before = host.State.Queue.Single();
        File.SetLastWriteTimeUtc(temp, DateTime.UtcNow.AddMinutes(1));
        var watch = new TrackedWatch(host);

        watch.Pump();

        var after = host.State.Queue.Single();
        await Assert.That(ReferenceEquals(after.LeftRows, before.LeftRows)).IsTrue();
        await Assert.That(after.LeftStamp).IsNotEqualTo(before.LeftStamp);

        var settled = host.State;
        watch.Pump();
        await Assert.That(ReferenceEquals(host.State, settled)).IsTrue();
    }

    /// <summary>
    /// And nothing about the window is another thing for it. A run that fails the same way writes
    /// its received file again every time, and the pass that took the new stamp used to close a
    /// context menu the reader had open, as an entry that changed does. The menu, what it was
    /// opened over and the status line are all what they were, whether the file written again is
    /// the one on screen or another.
    /// </summary>
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task AFileWrittenAgainWithWhatItHeldLeavesAnOpenMenuOpen(int written)
    {
        var host = OwnedAll(2);
        host.Mutate(_ => ViewerSession.OpenMenu(_, 0) with { Message = "Accepted Sample9.Test" });
        var menu = host.State.Menu;
        await Assert.That(menu).IsNotNull();
        var before = host.State.Queue[written];
        File.SetLastWriteTimeUtc(before.LeftFile!, DateTime.UtcNow.AddMinutes(1));

        new TrackedWatch(host).Pump();

        var after = host.State.Queue[written];
        await Assert.That(after.LeftStamp).IsNotEqualTo(before.LeftStamp);
        await Assert.That(host.State.Menu).IsSameReferenceAs(menu);
        await Assert.That(host.State.Selected).IsEqualTo(0);
        await Assert.That(host.State.Message).IsEqualTo("Accepted Sample9.Test");
    }

    /// <summary>
    /// A file written again with something else is an entry that changed, and that still closes
    /// the menu: what it offered was offered of what the entry showed.
    /// </summary>
    [Test]
    public async Task AFileWrittenAgainWithSomethingElseStillClosesAnOpenMenu()
    {
        var host = OwnedAll(2);
        host.Mutate(_ => ViewerSession.OpenMenu(_, 0));
        await Assert.That(host.State.Menu).IsNotNull();
        await File.WriteAllTextAsync(host.State.Queue[1].LeftFile!, "what a later run received instead");

        new TrackedWatch(host).Pump();

        await Assert.That(host.State.Queue[1].LeftText).IsEqualTo("what a later run received instead");
        await Assert.That(host.State.Menu).IsNull();
    }

    /// <summary>
    /// The same over the socket, which is how the run itself says the pair is pending again.
    /// </summary>
    [Test]
    public async Task APairSentAgainUnchangedIsNotDiffedAgain()
    {
        var (temp, target) = Pair("Sample.Test");
        var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));
        IQueueOwner owner = new MessageHandler(host, Fixtures.Applied, _ => { });
        owner.TrackMove(temp, target, null);
        var before = host.State.Queue.Single();
        File.SetLastWriteTimeUtc(temp, DateTime.UtcNow.AddMinutes(1));

        owner.TrackMove(temp, target, null);

        var after = host.State.Queue.Single();
        await Assert.That(ReferenceEquals(after.LeftRows, before.LeftRows)).IsTrue();
        await Assert.That(after.LeftStamp).IsNotEqualTo(before.LeftStamp);

        await File.WriteAllTextAsync(temp, "what a later run received instead");
        owner.TrackMove(temp, target, null);

        await Assert.That(host.State.Queue.Single().LeftText).IsEqualTo("what a later run received instead");
    }

    SessionHost OwnedAll(int count)
    {
        var state = SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows);
        for (var index = 0; index < count; index++)
        {
            var (temp, target) = Pair($"Sample{index}.Test");
            state = ViewerSession.EnqueueTracked(state, TrackedEntry.ForMove(temp, target));
        }

        return new(state);
    }

    static bool WaitUntilBlocked(Thread thread)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            if ((thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0)
            {
                return true;
            }

            Thread.Sleep(1);
        }

        return false;
    }
}
