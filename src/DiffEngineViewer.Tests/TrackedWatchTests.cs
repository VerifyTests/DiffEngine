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
        File.WriteAllText(temp, "second run");
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
        File.WriteAllText(temp, "rewritten and then held");
        using (new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            watch.Pump();
            var held = host.State;

            watch.Pump();
            watch.Pump();

            await Assert.That(host.State).IsSameReferenceAs(held);
        }
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
