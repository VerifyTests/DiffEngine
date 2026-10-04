/// <summary>
/// Accepting or discarding a document takes the files derived from it too.
/// <para>
/// A batch over the document and what is shown beneath it, the same one "Accept all in" a header
/// is: a file a step, each outside the lock, with what could not be moved left in the queue saying
/// why. A document with fifty pages is fifty-one files, and in one transition they would be moved
/// on the render thread.
/// </para>
/// <para>
/// Only the window's accept. An accept by key over the wire is another surface saying which file
/// it means, and is carried out as asked.
/// </para>
/// </summary>
public class DerivedAcceptTests
{
    /// <summary>
    /// What the window does with the key: begins the batch, and moves nothing. Real actions are
    /// what a key is dispatched with, so anything moved here would be an attempt on files that do
    /// not exist, and would show as failed entries.
    /// </summary>
    [Test]
    public async Task AcceptingTheDocumentBeginsABatchOverItAndWhatIsBeneathIt()
    {
        var state = Fixtures.DocumentWithDerived();

        var accepted = ViewerProgram.Apply(state, Key(CommandKind.Accept), link: null, new NoWindow());

        await Assert.That(accepted.Batch).IsNotNull();
        await Assert.That(accepted.Batch!.Total).IsEqualTo(6);
        await Assert.That(accepted.Batch.Cascade).IsEqualTo("Sample.Test (pdf)");
        await Assert.That(accepted.Batch.Covers(state.Queue.Single(_ => _.Name == "Other.Test (txt)").Key)).IsFalse();
        await Assert.That(accepted.Queue.Count).IsEqualTo(7);
        await Assert.That(accepted.Queue.All(_ => _.Status is null)).IsTrue();
        await Assert.That(ScreenBuilder.Build(accepted).Status).IsEqualTo("Accepting 1 of 6");
    }

    /// <summary>
    /// Carried out the way the loop has it carried out: what is beneath the document first, the
    /// document last, and the entry of some other test left alone.
    /// </summary>
    [Test]
    public async Task TheDocumentIsTheLastToGo()
    {
        var done = new List<string>();
        var host = new SessionHost(Fixtures.DocumentWithDerived());
        host.Mutate(ViewerSession.BeginAcceptWithDerived);

        var message = new AcceptAllRunner(host, DerivedFilesTests.Recording(done)).Drive();

        await Assert.That(done).IsEquivalentTo(
            [
                "move Sample.Test.received.txt",
                "move Sample.Test#page_0001.received.png",
                "move Sample.Test#page_0001.received.txt",
                "move Sample.Test#page_0002.received.png",
                "delete Sample.Test#page_0003.verified.png",
                "move Sample.Test.received.pdf"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(message).IsEqualTo("Accepted Sample.Test (pdf) and 5 derived files");
        await Assert.That(host.State.Queue.Select(_ => _.Name)).IsEquivalentTo(["Other.Test (txt)"]);
        await Assert.That(host.State.Batch).IsNull();
    }

    /// <summary>
    /// Taken last, the document is in the queue for as long as its files are, so none of them is
    /// ever a row of its own on the way out: at every step the rows are the document and what was
    /// never beneath it.
    /// </summary>
    [Test]
    public async Task NothingBeneathTheDocumentBecomesARowWhileItGoes()
    {
        var host = new SessionHost(Fixtures.DocumentWithDerived());
        var rows = new List<int>();
        var actions = DerivedFilesTests.Recording([]) with
        {
            MoveFile = (_, _) => rows.Add(QueueProjection.Rows(host.State).Count),
            DeleteFile = _ => rows.Add(QueueProjection.Rows(host.State).Count)
        };
        host.Mutate(ViewerSession.BeginAcceptWithDerived);

        new AcceptAllRunner(host, actions).Drive();

        await Assert.That(rows.All(_ => _ == 2)).IsTrue();
    }

    /// <summary>
    /// A file that could not be moved stays, saying why, and the rest go. With the document gone
    /// it is an ordinary row, where its failure can be read and it can be tried again.
    /// </summary>
    [Test]
    public async Task AFileThatCouldNotBeMovedStaysAndSaysWhy()
    {
        var host = new SessionHost(Fixtures.DocumentWithDerived());
        var actions = DerivedFilesTests.Recording([]) with
        {
            MoveFile = (temp, _) =>
            {
                if (temp.Contains("#page_0002"))
                {
                    throw new IOException("the file is locked");
                }
            }
        };
        host.Mutate(ViewerSession.BeginAcceptWithDerived);

        var message = new AcceptAllRunner(host, actions).Drive();

        await Assert.That(message).IsEqualTo("Accepted 5 of 6 files of Sample.Test (pdf) (1 kept)");
        var kept = host.State.Queue.Single(_ => _.Name == "Sample.Test#page_0002 (png)");
        await Assert.That(kept.Status).IsEqualTo("the file is locked");
        await Assert.That(QueueProjection.Rows(host.State).Select(_ => _.Label)).IsEquivalentTo(
        [
            "Sample.Test#page_0002 (png)",
            "Other.Test (txt)"
        ]);
    }

    /// <summary>
    /// And when the one that could not be moved is the document, its files have gone and it is
    /// what is left to try again.
    /// </summary>
    [Test]
    public async Task ADocumentThatCouldNotBeMovedStaysWithoutItsFiles()
    {
        var host = new SessionHost(Fixtures.DocumentWithDerived());
        var actions = DerivedFilesTests.Recording([]) with
        {
            MoveFile = (temp, _) =>
            {
                if (temp.EndsWith(".pdf", StringComparison.Ordinal))
                {
                    throw new IOException("open in another program");
                }
            }
        };
        host.Mutate(ViewerSession.BeginAcceptWithDerived);

        var message = new AcceptAllRunner(host, actions).Drive();

        await Assert.That(message).IsEqualTo("Accepted 5 of 6 files of Sample.Test (pdf) (1 kept)");
        await Assert.That(QueueProjection.Rows(host.State).Select(_ => _.Label)).IsEquivalentTo(
        [
            "Sample.Test (pdf)",
            "Other.Test (txt)"
        ]);
        await Assert.That(host.State.Queue[0].Status).IsEqualTo("open in another program");
    }

    /// <summary>
    /// Discarding throws away the document's received file and those of the files derived from
    /// it. A pending delete among them is only untracked, which is what discarding one has always
    /// meant: the file it would have removed stays.
    /// </summary>
    [Test]
    public async Task DiscardingTheDocumentDiscardsWhatIsBeneathIt()
    {
        var done = new List<string>();
        var state = Fixtures.DocumentWithDerived();

        var begun = ViewerProgram.Apply(state, Key(CommandKind.Discard), link: null, new NoWindow());
        await Assert.That(begun.Batch!.Discarding).IsTrue();
        var host = new SessionHost(begun);
        var message = new AcceptAllRunner(host, DerivedFilesTests.Recording(done)).Drive();

        // Received files only, and the document's last
        await Assert.That(done).IsEquivalentTo(
            [
                "delete Sample.Test.received.txt",
                "delete Sample.Test#page_0001.received.png",
                "delete Sample.Test#page_0001.received.txt",
                "delete Sample.Test#page_0002.received.png",
                "delete Sample.Test.received.pdf"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(message).IsEqualTo("Discarded Sample.Test (pdf) and 5 derived files");
        await Assert.That(host.State.Queue.Select(_ => _.Name)).IsEquivalentTo(["Other.Test (txt)"]);
    }

    /// <summary>
    /// Unfolded changes what has a row, not what goes with the document.
    /// </summary>
    [Test]
    public async Task UnfoldedTheyStillGoWithTheDocument()
    {
        var unfolded = ViewerSession.Apply(Fixtures.DocumentWithDerived(), CommandKind.ToggleDerived);

        var accepted = ViewerProgram.Apply(unfolded, Key(CommandKind.Accept), link: null, new NoWindow());

        await Assert.That(accepted.Batch!.Total).IsEqualTo(6);
    }

    /// <summary>
    /// One of them accepted on its own, from its own row, is that one file: it has nothing beneath
    /// it, and the document and the rest are still pending.
    /// </summary>
    [Test]
    public async Task OneOfThemAcceptedOnItsOwnIsThatOneFile()
    {
        var done = new List<string>();
        var state = Fixtures.DocumentWithDerived();
        var reading = ViewerSession.SelectKey(state, state.Queue.Single(_ => _.Name == "Sample.Test#page_0002 (png)").Key);
        await Assert.That(ViewerSession.HasDerived(reading)).IsFalse();

        var accepted = ViewerSession.Apply(reading, CommandKind.Accept, DerivedFilesTests.Recording(done));

        await Assert.That(done).IsEquivalentTo(["move Sample.Test#page_0002.received.png"]);
        await Assert.That(accepted.Queue.Count).IsEqualTo(6);
        await Assert.That(accepted.Batch).IsNull();
    }

    /// <summary>
    /// An entry with nothing beneath it is not a batch, and the transition says so by leaving the
    /// state as it was: the window accepts it the ordinary way.
    /// </summary>
    [Test]
    public async Task AnEntryWithNothingBeneathItBeginsNoBatch()
    {
        var state = ViewerSession.Apply(Fixtures.DocumentWithDerived(), CommandKind.NextItem);

        await Assert.That(ViewerSession.HasDerived(state)).IsFalse();
        await Assert.That(ViewerSession.BeginAcceptWithDerived(state).Batch).IsNull();
        await Assert.That(ViewerSession.BeginDiscardWithDerived(state).Batch).IsNull();
    }

    /// <summary>
    /// An accept by key over the wire is one entry, as it always was. Whoever sent it named the
    /// file it meant, and what was derived from that file is left standing as rows of its own.
    /// </summary>
    [Test]
    public async Task AnAcceptByKeyOverTheWireIsThatOneEntry()
    {
        var done = new List<string>();
        var host = new SessionHost(Fixtures.DocumentWithDerived());
        IQueueOwner owner = new MessageHandler(host, DerivedFilesTests.Recording(done), _ => { });

        var (ok, _, _) = owner.Accept(Fixtures.DocumentKey, null);

        await Assert.That(ok).IsTrue();
        await Assert.That(done).IsEquivalentTo(["move Sample.Test.received.pdf"]);
        await Assert.That(host.State.Queue.Count).IsEqualTo(6);
        await Assert.That(QueueProjection.Rows(host.State).Count).IsEqualTo(6);
    }

    /// <summary>
    /// A window showing someone else's queue applies nothing itself, so its accept of a document
    /// is the same thing sent as keys: what is beneath the document, then the document.
    /// </summary>
    [Test]
    public async Task AnAttachedWindowSendsWhatIsBeneathTheDocumentThenTheDocument()
    {
        using var files = new DocumentFiles();
        using var owner = new RecordingOwner(files.Listing);
        using var documents = Documents();
        var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));
        var link = new OwnerLink(host, owner.Port, documents);
        link.Pump();
        await Assert.That(QueueProjection.Rows(host.State).Select(_ => _.Label)).IsEquivalentTo(["+ Sample.Test (pdf) (2)"]);

        host.Mutate(_ => ViewerProgram.Apply(_, Key(CommandKind.Accept), link, new NoWindow()));
        link.Pump();

        await Assert.That(owner.Heard).IsEquivalentTo(
            [
                $"{ViewerVerb.Accept} {TrackedKeys.ForMove(files.Page)}",
                $"{ViewerVerb.Accept} {TrackedKeys.ForDelete(files.Stale)}",
                $"{ViewerVerb.Accept} {TrackedKeys.ForMove(files.Document)}"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnAttachedWindowDiscardsTheSameWay()
    {
        using var files = new DocumentFiles();
        using var owner = new RecordingOwner(files.Listing);
        using var documents = Documents();
        var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));
        var link = new OwnerLink(host, owner.Port, documents);
        link.Pump();

        host.Mutate(_ => ViewerProgram.Apply(_, Key(CommandKind.Discard), link, new NoWindow()));
        link.Pump();

        await Assert.That(owner.Heard).IsEquivalentTo(
            [
                $"{ViewerVerb.Discard} {TrackedKeys.ForMove(files.Page)}",
                $"{ViewerVerb.Discard} {TrackedKeys.ForDelete(files.Stale)}",
                $"{ViewerVerb.Discard} {TrackedKeys.ForMove(files.Document)}"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>
    /// An owner from before any of this says nothing of what was derived from what. Nothing is
    /// then beneath anything, so every file is a row and an accept is of the entry on screen, as
    /// it was.
    /// </summary>
    [Test]
    public async Task AnOwnerThatSaysNothingOfSourcesIsShownAsItAlwaysWas()
    {
        using var files = new DocumentFiles();
        using var owner = new RecordingOwner(() => files.Listing(sources: false));
        using var documents = Documents();
        var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));
        var link = new OwnerLink(host, owner.Port, documents);
        link.Pump();
        await Assert.That(QueueProjection.Rows(host.State).Count).IsEqualTo(3);

        host.Mutate(_ => ViewerSession.SelectKey(_, TrackedKeys.ForMove(files.Document)));
        host.Mutate(_ => ViewerProgram.Apply(_, Key(CommandKind.Accept), link, new NoWindow()));
        link.Pump();

        await Assert.That(owner.Heard).IsEquivalentTo([$"{ViewerVerb.Accept} {TrackedKeys.ForMove(files.Document)}"]);
    }

    /// <summary>
    /// And a window with no documents folder draws no document, so it puts nothing beneath one
    /// whatever its owner says: the pages are the only pictures it has to show.
    /// </summary>
    [Test]
    public async Task AWindowThatDrawsNoDocumentsShowsEveryFile()
    {
        using var files = new DocumentFiles();
        using var owner = new RecordingOwner(files.Listing);
        var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));

        new OwnerLink(host, owner.Port).Pump();

        await Assert.That(QueueProjection.Rows(host.State).Count).IsEqualTo(3);
        await Assert.That(host.State.Queue.Count(_ => _.SourceKey is not null)).IsEqualTo(2);
    }

    static ViewerInput Key(CommandKind key) =>
        new(key, -1, -1, 0, false, Fixtures.Columns, Fixtures.Rows);

    /// <summary>
    /// A documents folder that reads and draws nothing. Having one at all is what makes a PDF a
    /// document to this process rather than text, which is all these tests need of it.
    /// </summary>
    static DocumentPlugin Documents() =>
        new(
            static _ => throw new("Not read in these tests."),
            static (_, _, _, _) => throw new("Not drawn in these tests."));

    /// <summary>
    /// A document, a page of it and a page it has lost, on disk, as an owner lists them.
    /// </summary>
    sealed class DocumentFiles :
        IDisposable
    {
        readonly string directory = Directory.CreateTempSubdirectory("deview-derived-").FullName;

        public string Document { get; }
        public string Page { get; }
        public string Stale { get; }

        public DocumentFiles()
        {
            Document = Write("Sample.Test.received.pdf", "received document");
            Write("Sample.Test.verified.pdf", "verified document");
            Page = Write("Sample.Test#page_0001.received.png", "received page");
            Stale = Write("Sample.Test#page_0002.verified.png", "a page it no longer has");
        }

        public ViewerResponse Listing() =>
            Listing(sources: true);

        public ViewerResponse Listing(bool sources)
        {
            var sourceKey = sources ? TrackedKeys.ForMove(Document) : null;
            return ViewerResponse.Listing(
                [],
                moves:
                [
                    // The page ahead of its document, as a tray's dictionary can list them
                    new(TrackedKeys.ForMove(Page), "Sample.Test#page_0001 (png)", null, Page, Path.Combine(directory, "Sample.Test#page_0001.verified.png"))
                    {
                        SourceKey = sourceKey
                    },
                    new(TrackedKeys.ForMove(Document), "Sample.Test (pdf)", null, Document, Path.Combine(directory, "Sample.Test.verified.pdf"))
                ],
                deletes:
                [
                    new(TrackedKeys.ForDelete(Stale), "Sample.Test#page_0002.verified.png", null, Stale)
                    {
                        SourceKey = sourceKey
                    }
                ]);
        }

        string Write(string name, string content)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() =>
            Directory.Delete(directory, true);
    }

    /// <summary>
    /// An owner that lists what it is given and writes down what it is asked to accept and
    /// discard, so the assertions are about what a window sends.
    /// </summary>
    sealed class RecordingOwner :
        IDisposable
    {
        readonly ViewerServer server;
        readonly CancelSource cancel = new();

        public ConcurrentQueue<string> Heard { get; } = new();

        public int Port => server.Port;

        public RecordingOwner(Func<ViewerResponse> listing)
        {
            if (!ViewerServer.TryBind(0, out var bound))
            {
                throw new("Could not bind an ephemeral port.");
            }

            server = bound;
            _ = server.Listen(
                message =>
                {
                    if (message.Verb is ViewerVerb.Accept or ViewerVerb.Discard)
                    {
                        Heard.Enqueue($"{message.Verb} {message.Key}");
                        return ViewerResponse.Success("Done");
                    }

                    return listing();
                },
                cancel.Token);
        }

        public void Dispose()
        {
            cancel.Cancel();
            server.Dispose();
            cancel.Dispose();
        }
    }

    internal sealed class NoWindow : IViewerWindow
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
