/// <summary>
/// A batch claims a file's snapshots under the session's lock and writes them outside it, and the
/// wait between is the file's own lock, up to ten seconds. The queue goes on changing meanwhile.
/// A snapshot discarded then, or settled by a test that started passing, had already been handed
/// over, and was written with the rest of its file and counted nowhere: thrown away by the
/// reviewer and in the source all the same. It is asked about as the file is about to be written
/// now, and left out when it is no longer the entry that was claimed.
/// </summary>
public class WithdrawnSnapshotTests
{
    [Test]
    public async Task ASnapshotDiscardedWhileItsFileIsWaitedForIsNotWritten()
    {
        using var file = new SourceFile();
        var host = new SessionHost(
            Fixtures.Inline(
                Fixtures.Patch(file.Path, 3, "\"a\"", "one"),
                Fixtures.Patch(file.Path, 4, "\"b\"", "two"),
                Fixtures.Patch(file.Path, 5, "\"c\"", "three")));
        var discarded = host.State.Queue.Single(_ => _.Patch!.LineHint == 4).Key;
        var actions = Meanwhile(
            () => host.Mutate(_ => ViewerSession.Apply(ViewerSession.SelectKey(_, discarded), CommandKind.Discard, ViewerActions.Real)));
        host.Mutate(ViewerSession.BeginAcceptAll);

        var message = new AcceptAllRunner(host, actions).Drive();

        var written = file.Text;
        await Assert.That(written).Contains("One() => Verify(value).Snapshot(\"one\")");
        await Assert.That(written).Contains("Two() => Verify(value).Snapshot(\"b\")");
        await Assert.That(written).Contains("Three() => Verify(value).Snapshot(\"three\")");
        // Neither accepted nor failed: it was taken back
        await Assert.That(message).IsEqualTo("Accepted 2");
        await Assert.That(host.State.Queue).IsEmpty();
        await Assert.That(host.State.Batch).IsNull();
    }

    /// <summary>
    /// A test that started passing settles its entry, and its source already holds what it
    /// passes with: the patch claimed for it would put the failing run's content over that.
    /// What is left of the file is taken to where its call sites are in the file that was
    /// written, which the snapshot left out moved nothing in.
    /// </summary>
    [Test]
    public async Task ASnapshotSettledWhileItsFileIsWaitedForIsNotWrittenAndMovesNothing()
    {
        using var file = new SourceFile();
        var host = new SessionHost(
            Fixtures.Inline(
                Fixtures.Patch(file.Path, 3, "\"a\"", "one\nmore"),
                Fixtures.Patch(file.Path, 4, "\"b\"", "two\nmore\nand more\nand again"),
                Fixtures.Patch(file.Path, 5, "\"c\"", "three"),
                // A conflict, which no bulk accept takes: what is left of the file afterwards
                Fixtures.Patch(file.Path, 6, "\"d\"", "four", framework: "net8.0"),
                Fixtures.Patch(file.Path, 6, "\"d\"", "vier", framework: "net9.0")));
        var settled = host.State.Queue.Single(_ => _.Patch!.LineHint == 4).Key;
        var actions = Meanwhile(() => host.Mutate(_ => ViewerSession.Settle(_, settled)));
        host.Mutate(ViewerSession.BeginAcceptAll);

        var message = new AcceptAllRunner(host, actions).Drive();

        var written = file.Text;
        await Assert.That(written).Contains("Two() => Verify(value).Snapshot(\"b\")");
        await Assert.That(written).DoesNotContain("and again");
        await Assert.That(written).Contains("more");
        await Assert.That(written).Contains("Three() => Verify(value).Snapshot(\"three\")");
        await Assert.That(message).IsEqualTo("Accepted 2, 1 conflict needs review");
        var left = host.State.Queue.Single();
        await Assert.That(left.Conflicted).IsTrue();
        var lines = written.Split('\n');
        await Assert.That(lines[left.Patch!.LineHint - 1]).Contains("void Four()");
        await Assert.That(left.Key).IsEqualTo(InlineKey.For(file.Path, left.Patch.LineHint));
    }

    /// <summary>
    /// A re-run that replaces a claimed snapshot is the other way an entry stops being the one
    /// that was claimed. The content it was claimed with is stale, and was written over the
    /// source and then not recorded, because the entry had changed. It is not written now, and
    /// the entry keeps what the re-run sent. With an applier that takes them one at a time,
    /// each is asked about before it is applied.
    /// </summary>
    [Test]
    public async Task ASnapshotReplacedWhileItsFileIsWaitedForIsNotApplied()
    {
        var host = new SessionHost(
            Fixtures.Inline(
                Fixtures.Patch(),
                Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two"),
                Fixtures.Patch("SampleTests.cs", 90, "\"three\"", "four")));
        var applied = new List<int>();
        var actions = Fixtures.Applied with
        {
            ApplyInline = _ =>
            {
                applied.Add(_.LineHint);
                if (_.LineHint == 42)
                {
                    host.Mutate(state => ViewerSession.EnqueueInline(state, Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "third run")));
                }

                return InlineApplyResult.Applied;
            }
        };
        host.Mutate(ViewerSession.BeginAcceptAll);

        var message = new AcceptAllRunner(host, actions).Drive();

        await Assert.That(applied).IsEquivalentTo([42, 90]);
        await Assert.That(message).IsEqualTo("Accepted 2");
        var left = host.State.Queue.Single();
        await Assert.That(left.Name).IsEqualTo("SampleTests.cs:88");
        await Assert.That(left.LeftText).IsEqualTo("third run");
        await Assert.That(left.Status).IsNull();
    }

    /// <summary>
    /// A batch inside one transition asks nothing, since nothing else can have touched the queue
    /// between its claim and its write: every claimed snapshot is applied, as before.
    /// </summary>
    [Test]
    public async Task ABatchInOneTransitionAsksNothing()
    {
        var applied = new List<int>();
        var actions = Fixtures.Applied with
        {
            ApplyInline = _ =>
            {
                applied.Add(_.LineHint);
                return InlineApplyResult.Applied;
            },
            ApplyInlineWanted = (_, _) => throw new("Nothing was there to ask.")
        };
        var state = Fixtures.Inline(
            Fixtures.Patch(),
            Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two"));

        var done = ViewerSession.Apply(state, CommandKind.AcceptAll, actions);

        await Assert.That(applied).IsEquivalentTo([42, 88]);
        await Assert.That(done.Message).IsEqualTo("Accepted 2");
    }

    /// <summary>
    /// The question is asked with the source file's lock held, on the thread that applies. A
    /// single accept arriving over the socket applies inside the session's lock, so it holds
    /// that while it waits for the file. Were the question to take the session's lock, each
    /// thread would be waiting on what the other holds, for good. It reads the state, which
    /// takes no lock, and both finish.
    /// </summary>
    [Test]
    public async Task TheQuestionDoesNotWaitOnTheSessionsLock()
    {
        using var file = new SourceFile();
        var host = new SessionHost(Fixtures.Inline(Fixtures.Patch(file.Path, 3, "\"a\"", "one")));
        using var asking = new ManualResetEventSlim();
        using var holding = new ManualResetEventSlim();
        var actions = ViewerActions.Real with
        {
            ApplyInlineWanted = (patches, wanted) => InlineApplier.ApplyAll(
                patches,
                _ =>
                {
                    // The file's lock is held here. Not answered until the other thread has the
                    // session's lock and is on its way to this file
                    asking.Set();
                    holding.Wait(TimeSpan.FromSeconds(30));
                    return wanted(_);
                })
        };
        host.Mutate(ViewerSession.BeginAcceptAll);

        // On threads of their own: both block, and the pool is what resumes this test
        var batch = Task.Factory.StartNew(
            () => new AcceptAllRunner(host, actions).Drive(),
            Cancel.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var single = Task.Factory.StartNew(
            () =>
            {
                asking.Wait(TimeSpan.FromSeconds(30));
                // What MessageHandler does with a wire accept: the apply is inside the mutation
                return host.Mutate(
                    _ =>
                    {
                        holding.Set();
                        InlineApplier.Apply(Fixtures.Patch(file.Path, 4, "\"b\"", "two"));
                        return _;
                    });
            },
            Cancel.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        await Task.WhenAll(batch, single).WaitAsync(TimeSpan.FromSeconds(60));

        await Assert.That(await batch).IsEqualTo("Accepted 1");
        var written = file.Text;
        await Assert.That(written).Contains("Snapshot(\"one\")");
        await Assert.That(written).Contains("Snapshot(\"two\")");
    }

    /// <summary>
    /// The viewer's real actions, with something done to the queue at the moment a claim has
    /// been handed over and its file not yet read: what another thread does while the batch
    /// waits for the file. On both ways of handing a file's snapshots over, so the moment is
    /// the same whether or not the batch then asks about them.
    /// </summary>
    static ViewerActions Meanwhile(Action change) =>
        ViewerActions.Real with
        {
            ApplyInline = _ =>
            {
                change();
                return InlineApplier.Apply(_);
            },
            ApplyInlineTogether = _ =>
            {
                change();
                return InlineApplier.ApplyAll(_);
            },
            ApplyInlineWanted = (patches, wanted) =>
            {
                change();
                return InlineApplier.ApplyAll(patches, wanted);
            }
        };

    /// <summary>
    /// A real source file with four snapshots, one a line, the first on line 3.
    /// </summary>
    sealed class SourceFile : IDisposable
    {
        readonly string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"WithdrawnSnapshotTests_{Guid.NewGuid():N}");

        public SourceFile()
        {
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, "SampleTests.cs");
            File.WriteAllText(
                Path,
                """
                class C
                {
                    void One() => Verify(value).Snapshot("a");
                    void Two() => Verify(value).Snapshot("b");
                    void Three() => Verify(value).Snapshot("c");
                    void Four() => Verify(value).Snapshot("d");
                }
                """);
        }

        public string Path { get; }

        public string Text => File.ReadAllText(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // Best effort cleanup of the temp directory
            }
        }
    }
}
