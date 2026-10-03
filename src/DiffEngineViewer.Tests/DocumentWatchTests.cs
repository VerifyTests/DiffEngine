/// <summary>
/// The pass that reads documents and draws their pages, driven a job at a time as
/// <see cref="TrackedWatchTests"/> drives its own, over real files and a fake renderer.
/// <para>
/// The fake reads a "document" as lines of text: its text is the file, and each line draws as a
/// page whose colour comes from the line. Two documents' pages therefore differ exactly where their
/// lines do, which is the property the real renderer's determinism gives the viewer.
/// </para>
/// </summary>
public class DocumentWatchTests :
    IDisposable
{
    [Test]
    public async Task TheTextArrivesAndTheEntryIsBuiltWithIt()
    {
        var (host, documents) = Owned("alpha\nbravo", "alpha\nBRAVO");
        await Assert.That(host.State.Current!.LeftDocument!.Value.Reading).IsTrue();

        await Assert.That(new DocumentWatch(host, documents.Plugin).Pump()).IsTrue();

        var entry = host.State.Current!;
        await Assert.That(entry.HasText).IsTrue();
        await Assert.That(entry.LeftText).IsEqualTo("alpha\nbravo");
        await Assert.That(entry.RightText).IsEqualTo("alpha\nBRAVO");
    }

    /// <summary>
    /// What the last accept said is about the entry, not its text, and a rebuild for the text keeps it.
    /// </summary>
    [Test]
    public async Task TheStatusOutlivesTheRebuild()
    {
        var (host, documents) = Owned("alpha", "bravo");
        host.Mutate(_ => _ with { Queue = [_.Queue[0] with { Status = "locked by an editor" }] });

        new DocumentWatch(host, documents.Plugin).Pump();

        await Assert.That(host.State.Current!.Status).IsEqualTo("locked by an editor");
    }

    [Test]
    public async Task PagesLandOneAtATime()
    {
        var landed = new List<int>();
        var (host, documents) = Owned("alpha\nbravo\ncharlie", "alpha\nBRAVO\ncharlie");
        var watch = new DocumentWatch(host, documents.Plugin);
        watch.Pump();

        // Left, then right, a job each, each page published as it lands
        documents.Landing = () => landed.Add(Pages(host.State, host.State.Current!.LeftDocument).Count);
        await Assert.That(watch.Pump()).IsTrue();
        await Assert.That(landed).IsEquivalentTo([0, 1, 2]);
        await Assert.That(watch.Pump()).IsTrue();

        var state = host.State;
        var left = DocumentPages.Of(state, state.Current!.LeftDocument)!;
        var right = DocumentPages.Of(state, state.Current!.RightDocument)!;
        await Assert.That(left.Complete && right.Complete).IsTrue();
        await Assert.That(DocumentPages.Differing(state.Current!, left, right)).IsEquivalentTo([1]);
        await Assert.That(DocumentPages.Current(state)).IsEqualTo(1);
    }

    /// <summary>
    /// Only the entry on screen is read and drawn. The one after it waits until it is stepped to,
    /// rather than holding the thread when the reader picks some other entry instead.
    /// </summary>
    [Test]
    public async Task OnlyTheEntryOnScreenIsReadAndDrawn()
    {
        var documents = new FakeDocuments();
        disposables.Add(documents);
        var first = TrackedEntry.ForMove(Write("first.received.pdf", "alpha"), Write("first.verified.pdf", "bravo"), documents.Plugin);
        var second = TrackedEntry.ForMove(Write("second.received.pdf", "charlie"), Write("second.verified.pdf", "delta"), documents.Plugin);
        var state = ViewerSession.EnqueueTracked(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows), first);
        state = ViewerSession.EnqueueTracked(state, second);
        var host = new SessionHost(ViewerSession.SelectKey(state, first.Key));
        var watch = new DocumentWatch(host, documents.Plugin);
        while (watch.Pump())
        {
        }

        await Assert.That(documents.Texts).IsEqualTo(2);
        await Assert.That(documents.Renders).IsEqualTo(2);
        var waiting = host.State.Queue.Single(_ => _.Key == second.Key);
        await Assert.That(waiting.LeftDocument!.Value.Reading).IsTrue();
        await Assert.That(DocumentPages.Of(host.State, waiting.LeftDocument)).IsNull();

        host.Mutate(_ => ViewerSession.SelectKey(_, second.Key));
        while (watch.Pump())
        {
        }

        await Assert.That(documents.Texts).IsEqualTo(4);
        await Assert.That(documents.Renders).IsEqualTo(4);
        await Assert.That(host.State.Current!.LeftText).IsEqualTo("charlie");
    }

    /// <summary>
    /// Nothing left to do is a pass that does nothing: no job, and the state as it was, so an open
    /// menu stays open through it.
    /// </summary>
    [Test]
    public async Task AnIdlePassChangesNothing()
    {
        var (host, documents) = Owned("alpha", "bravo");
        var watch = new DocumentWatch(host, documents.Plugin);
        while (watch.Pump())
        {
        }

        var state = host.State;
        await Assert.That(watch.Pump()).IsFalse();
        await Assert.That(host.State).IsSameReferenceAs(state);
    }

    /// <summary>
    /// The same bytes on both sides are read and drawn once.
    /// </summary>
    [Test]
    public async Task IdenticalDocumentsAreDrawnOnce()
    {
        var (host, documents) = Owned("alpha", "alpha");
        var watch = new DocumentWatch(host, documents.Plugin);
        while (watch.Pump())
        {
        }

        await Assert.That(documents.Renders).IsEqualTo(1);
        await Assert.That(host.State.Renders.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TheTextViewDrawsNothing()
    {
        var (host, documents) = Owned("alpha", "bravo");
        host.Mutate(_ => _ with { Drawing = DrawingView.Text });
        var watch = new DocumentWatch(host, documents.Plugin);
        while (watch.Pump())
        {
        }

        await Assert.That(documents.Renders).IsEqualTo(0);
    }

    /// <summary>
    /// A document that cannot be read says why, and is not tried again on every pass.
    /// </summary>
    [Test]
    public async Task TextThatCannotBeReadIsSaidOnce()
    {
        var (host, documents) = Owned("alpha", "bravo");
        documents.TextFailure = "it is encrypted.";
        var watch = new DocumentWatch(host, documents.Plugin);
        while (watch.Pump())
        {
        }

        await Assert.That(documents.Texts).IsEqualTo(2);
        await Assert.That(host.State.Current!.LeftDocument!.Value.Unreadable).IsEqualTo("it is encrypted.");
        await Assert.That(ScreenBuilder.Build(host.State).Status).Contains("could not read the text of");
    }

    [Test]
    public async Task PagesThatCannotBeDrawnSayWhy()
    {
        var (host, documents) = Owned("alpha", "bravo");
        documents.RenderFailure = "PDFium could not open it.";
        var watch = new DocumentWatch(host, documents.Plugin);
        while (watch.Pump())
        {
        }

        var rendering = DocumentPages.Of(host.State, host.State.Current!.LeftDocument)!;
        await Assert.That(rendering.Complete).IsTrue();
        await Assert.That(rendering.Failure).IsEqualTo("PDFium could not open it.");
    }

    /// <summary>
    /// What is read is the bytes the side describes, never whatever a re-run wrote since: those are
    /// a different document, which the entry is rebuilt to describe and which is then read in turn.
    /// </summary>
    [Test]
    public async Task AFileRewrittenBeforeItIsReadIsReadAsItIsNow()
    {
        var (host, documents) = Owned("alpha", "bravo");
        await File.WriteAllTextAsync(host.State.Current!.LeftFile!, "rewritten by a later run");

        var watch = new DocumentWatch(host, documents.Plugin);
        while (watch.Pump())
        {
        }

        await Assert.That(host.State.Current!.LeftText).IsEqualTo("rewritten by a later run");
    }

    /// <summary>
    /// The pages and copies of a document go with its entry, from the state and from the disk.
    /// </summary>
    [Test]
    public async Task ADocumentThatLeavesTheQueueIsForgotten()
    {
        var (host, documents) = Owned("alpha", "bravo");
        var watch = new DocumentWatch(host, documents.Plugin);
        while (watch.Pump())
        {
        }

        var cache = documents.Cache.Root;
        await Assert.That(Directory.EnumerateDirectories(cache)).IsNotEmpty();

        host.Mutate(_ => _ with { Queue = [], Selected = -1 });
        watch.Pump();

        await Assert.That(host.State.Renders).IsEmpty();
        await Assert.That(Directory.EnumerateDirectories(cache)).IsEmpty();
    }

    /// <summary>
    /// A call that never returns is left behind after the timeout and reported, so the next
    /// document is not stuck behind it.
    /// </summary>
    [Test]
    public async Task ACallThatNeverReturnsIsLeftBehind()
    {
        // Not disposed: the call left behind is still waiting on it when this test ends.
        var release = new ManualResetEventSlim();
        try
        {
            var (host, documents) = Owned("alpha", "bravo");
            var watch = new DocumentWatch(host, documents.Plugin)
            {
                Timeout = TimeSpan.FromMilliseconds(200)
            };
            watch.Pump();
            documents.Landing = () => release.Wait();

            watch.Pump();

            var rendering = DocumentPages.Of(host.State, host.State.Current!.LeftDocument)!;
            await Assert.That(rendering.Failure).IsEqualTo("gave up after 0.2 seconds.");
            await Assert.That(rendering.Complete).IsTrue();
            await Assert.That(rendering.Pages).IsEmpty();
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>
    /// Replacing an entry an accept-all has claimed would lose what it records about it, so the
    /// text waits for the batch to finish.
    /// </summary>
    [Test]
    public async Task TextWaitsForAnAcceptAll()
    {
        var (host, documents) = Owned("alpha", "bravo");
        host.Mutate(_ => _ with { OwnerProgress = new(0, 1) });

        new DocumentWatch(host, documents.Plugin).Pump();

        await Assert.That(host.State.Current!.LeftDocument!.Value.Reading).IsTrue();
    }

    (SessionHost Host, FakeDocuments Documents) Owned(string left, string right)
    {
        var documents = new FakeDocuments();
        disposables.Add(documents);
        var leftFile = Write("sample.received.pdf", left);
        var rightFile = Write("sample.verified.pdf", right);
        var entry = QueueEntry.ForFiles(
            leftFile,
            rightFile,
            FileSide.Read(leftFile, documents.Plugin),
            FileSide.Read(rightFile, documents.Plugin));
        var state = ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File, Fixtures.Columns, Fixtures.Rows), entry);
        return (new(state), documents);
    }

    static IReadOnlyList<RenderedPage> Pages(SessionState state, DocumentFile? side) =>
        DocumentPages.Of(state, side)?.Pages ?? [];

    string Write(string name, string content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    readonly string directory = Directory.CreateTempSubdirectory("deview-document-watch-").FullName;
    readonly List<IDisposable> disposables = [];

    public void Dispose()
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }

        Directory.Delete(directory, true);
    }

    /// <summary>
    /// A renderer that reads a document as lines: the text is the file, and each line is a page.
    /// </summary>
    sealed class FakeDocuments :
        IDisposable
    {
        public FakeDocuments() =>
            Plugin = new(Text, Render);

        public DocumentPlugin Plugin { get; }

        public RenderCache Cache => Plugin.Cache;

        public string? TextFailure { get; set; }

        public string? RenderFailure { get; set; }

        /// <summary>
        /// Called as each page is about to land.
        /// </summary>
        public Action? Landing { get; set; }

        public int Texts { get; private set; }

        public int Renders { get; private set; }

        string Text(string path)
        {
            Texts++;
            if (TextFailure is not null)
            {
                throw new(TextFailure);
            }

            return File.ReadAllText(path);
        }

        int Render(string path, string directory, Action<string> landed)
        {
            Renders++;
            if (RenderFailure is not null)
            {
                throw new(RenderFailure);
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var colour = SHA256.HashData(Encoding.UTF8.GetBytes(lines[index]));
                var page = Path.Combine(directory, $"page_{index + 1:0000}.png");
                File.WriteAllBytes(page, SamplePng.Build(8, 8, colour[0], colour[1], colour[2]));
                Landing?.Invoke();
                landed(page);
            }

            return lines.Length;
        }

        public void Dispose() =>
            Plugin.Dispose();
    }
}
