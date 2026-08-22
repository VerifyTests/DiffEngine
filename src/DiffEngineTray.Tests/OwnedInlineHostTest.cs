/// <summary>
/// The tray holding the inline queue, driven over a real socket the way DiffEngine and a viewer
/// drive it. The arrangement this replaces, where a viewer owns the queue and the tray is a remote
/// control, is still covered by <see cref="TrackerSnapshotTest"/>, because it is still what happens
/// when a viewer bound the port first.
/// </summary>
public class OwnedInlineHostTest
{
    sealed class Owner : IDisposable
    {
        public Owner(Func<InlinePatch, InlineApplyResult>? applier = null)
        {
            Host = OwnedInlineHost.TryOwn(Warnings.Add, Launcher, 0, applier) ??
                   throw new("Could not bind an ephemeral port.");
            // Binding claims ownership; serving is separate so the real tray can wire Changed
            // first. Nothing here watches Changed, so the two happen together.
            Host.Start();
        }

        public OwnedInlineHost Host { get; }
        public FakeLauncher Launcher { get; } = new();
        public List<string> Warnings { get; } = [];

        public ViewerResponse Send(ViewerMessage message)
        {
            if (!ViewerClient.TrySend(message, out var response, Host.Port))
            {
                throw new($"No response for {message.Verb}.");
            }

            return response;
        }

        public ViewerResponse Queue(
            string source = @"c:\repo\SampleTests.cs",
            int line = 42,
            string content = "new",
            string? framework = null,
            string? testName = null) =>
            Send(new(ViewerVerb.Inline, Body: InlinePatchFile.Build(new(source, line, "\"old\"", content)
            {
                Framework = framework,
                TestName = testName
            })));

        public void Dispose() =>
            Host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// The tray is the durable surface, but exiting it still takes the queue in its memory.
    /// Disposal writes what is still pending back to the staging layout, so accept tooling finds
    /// it on disk instead of finding nothing at all.
    /// </summary>
    [Test]
    public async Task DisposalPersistsThePendingSnapshots()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tray-persist-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Sample.csproj"), "<Project />");
            var source = Path.Combine(directory, "SampleTests.cs");
            await File.WriteAllTextAsync(source, "// sample");

            using (var owner = new Owner())
            {
                owner.Queue(source, framework: "net10.0");
            }

            var staging = Path.Combine(directory, "obj", InlineStaging.DirectoryName);
            var staged = Directory.GetFiles(staging);
            await Assert.That(staged.Length).IsEqualTo(3);
            var patchFile = staged.Single(_ => _.EndsWith(".inlinepatch"));
            await Assert.That(InlinePatchFile.TryRead(patchFile, out var read)).IsTrue();
            await Assert.That(read!.SourceFile).IsEqualTo(source);
            await Assert.That(read.NewContent).IsEqualTo("new");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The fixture's default patches name a source that does not exist on this machine, which is
    /// also true of a patch whose file has gone: there is nothing such a patch could ever apply
    /// to, so disposal skips it rather than staging something unusable.
    /// </summary>
    [Test]
    public async Task DisposalSkipsPatchesWhoseSourceIsGone()
    {
        using (var owner = new Owner())
        {
            owner.Queue();
            await Assert.That(owner.Host.Queued().Count).IsEqualTo(1);
        }

        // Nothing to assert on disk: the fake path has no project to stage under. The absence of
        // an exception out of Dispose is the point.
    }

    [Test]
    public async Task AQueuedPatchIsHeldHereAndShown()
    {
        using var owner = new Owner();

        var response = owner.Queue();

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(response.Message).IsEqualTo("Queued 1");
        await Assert.That(owner.Host.List().Select(_ => _.Name)).IsEquivalentTo(["SampleTests.cs:42"]);
        await Assert.That(owner.Launcher.Launches).IsEqualTo(1);
    }

    /// <summary>
    /// A second failing snapshot is a focus, not a second window.
    /// </summary>
    [Test]
    public async Task ASecondPatchDoesNotStartASecondViewer()
    {
        using var owner = new Owner();
        owner.Queue();

        owner.Queue(@"c:\repo\OtherTests.cs", 7);

        await Assert.That(owner.Launcher.Launches).IsEqualTo(1);
        await Assert.That(owner.Host.List().Count).IsEqualTo(2);
    }

    [Test]
    public async Task AViewerThatWentAwayIsStartedAgain()
    {
        using var owner = new Owner();
        owner.Queue();
        owner.Launcher.Running = false;

        owner.Queue(@"c:\repo\OtherTests.cs", 7);

        await Assert.That(owner.Launcher.Launches).IsEqualTo(2);
    }

    [Test]
    public async Task AViewerThatCannotBeStartedIsReported()
    {
        using var owner = new Owner();
        owner.Launcher.Succeed = false;

        owner.Queue();

        await Assert.That(owner.Warnings).HasSingleItem();
        // Still queued, so it is reachable from the tray menu even with no window.
        await Assert.That(owner.Host.List()).HasSingleItem();
    }

    [Test]
    public async Task APassingRerunSettlesTheEntry()
    {
        using var owner = new Owner();
        owner.Queue();

        var response = owner.Send(new(ViewerVerb.Settle, InlineKey.For(@"c:\repo\SampleTests.cs", 42)));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(owner.Host.List()).IsEmpty();
    }

    [Test]
    public async Task ARerunOfTheSameCallSiteReplacesItsEntry()
    {
        using var owner = new Owner();
        owner.Queue();

        owner.Queue();

        await Assert.That(owner.Host.List()).HasSingleItem();
    }

    /// <summary>
    /// What an attached viewer asks for: enough to rebuild every pane without a diff crossing the
    /// wire.
    /// </summary>
    [Test]
    public async Task AFullListingCarriesThePatches()
    {
        using var owner = new Owner();
        owner.Queue();

        var response = owner.Send(new(ViewerVerb.ListFull));

        var item = response.Items.Single();
        await Assert.That(InlinePatchFile.TryParse(item.Patch!, out var patch)).IsTrue();
        await Assert.That(patch!.NewContent).IsEqualTo("new");
    }

    /// <summary>
    /// The tray has no window, so what it wants done to one rides back on a listing. Taken rather
    /// than read, or the viewer would raise itself five times a second forever.
    /// </summary>
    [Test]
    public async Task FocusRidesBackOnTheNextListing()
    {
        using var owner = new Owner();
        owner.Queue();
        var snapshot = owner.Host.List().Single();

        owner.Host.Focus(snapshot);

        var first = owner.Send(new(ViewerVerb.ListFull));
        await Assert.That(first.Window).IsEqualTo(WindowCommand.Focus);
        await Assert.That(first.WindowKey).IsEqualTo(snapshot.Key);

        var second = owner.Send(new(ViewerVerb.ListFull));
        await Assert.That(second.Window).IsNull();
    }

    /// <summary>
    /// A plain List does not take the stashed window command. It is the documented IDE plugin API
    /// - InlineQueueClient.TryListKeys - and has no window to raise, so taking the command threw
    /// it away: whoever asked could not act on it, and the attached viewer polling beside it never
    /// raised for the new snapshot.
    /// </summary>
    [Test]
    public async Task APlainListingLeavesTheWindowCommandForAViewer()
    {
        using var owner = new Owner();
        owner.Queue();
        var snapshot = owner.Host.List().Single();

        owner.Host.Focus(snapshot);

        var plain = owner.Send(new(ViewerVerb.List));
        await Assert.That(plain.Window).IsNull();

        // Still there for the surface that has a window
        var full = owner.Send(new(ViewerVerb.ListFull));
        await Assert.That(full.Window).IsEqualTo(WindowCommand.Focus);
        await Assert.That(full.WindowKey).IsEqualTo(snapshot.Key);
    }

    [Test]
    public async Task ClosingTheViewerLeavesTheQueue()
    {
        using var owner = new Owner();
        owner.Queue();

        owner.Host.Close();

        await Assert.That(owner.Send(new(ViewerVerb.ListFull)).Window).IsEqualTo(WindowCommand.Close);
        await Assert.That(owner.Host.List()).HasSingleItem();
    }

    /// <summary>
    /// A file that is not there right now may be there after a branch switch or a pull, so the
    /// entry stays, carries what went wrong, and can be retried. The tray reports the failure
    /// rather than quietly dropping the snapshot.
    /// </summary>
    [Test]
    public async Task AFailedAcceptKeepsItsEntryAndSaysWhy()
    {
        using var owner = new Owner();
        var missing = Path.Combine(Path.GetTempPath(), "DiffEngineTray.NotHere.cs");
        owner.Queue(missing);
        var snapshot = owner.Host.List().Single();

        var accepted = owner.Host.Accept(snapshot, out var message);

        await Assert.That(accepted).IsEqualTo(AcceptOutcome.Failed);
        await Assert.That(message).Contains("Source file does not exist");
        await Assert.That(owner.Host.List().Single().Status).IsEqualTo(message);
    }

    /// <summary>
    /// A patch whose call site has moved is dropped rather than left to fail forever, but dropped
    /// is not accepted: it has to read as something the user must re-run, or the snapshot vanishing
    /// from the menu is indistinguishable from success.
    /// </summary>
    [Test]
    public async Task AStaleAcceptIsNotReportedAsApplied()
    {
        using var owner = new Owner(_ => InlineApplyResult.NotFound("the call site moved"));
        owner.Queue();
        var snapshot = owner.Host.List().Single();

        var accepted = owner.Host.Accept(snapshot, out var message);

        await Assert.That(accepted).IsEqualTo(AcceptOutcome.Stale);
        await Assert.That(message).IsEqualTo("SampleTests.cs:42 not written. the call site moved");
        await Assert.That(owner.Host.List()).IsEmpty();
    }

    /// <summary>
    /// The queue stays answerable while a viewer is starting. The launch used to hold the gate, so
    /// a process start - seconds of it with an antivirus in the way - queued behind it every
    /// listing the attached viewer polls for and every patch a test run was sending.
    /// </summary>
    [Test]
    public async Task AStartingViewerDoesNotHoldUpTheQueue()
    {
        using var owner = new Owner();
        using var block = new ManualResetEventSlim();
        owner.Launcher.Block = block;

        // Enqueuing is what triggers the launch, and it goes on a worker because the launch it
        // triggers is the thing being held open
        var enqueue = Task.Run(() => owner.Queue());
        await Assert.That(owner.Launcher.Started.Wait(TimeSpan.FromSeconds(5))).IsTrue();

        // Mid launch, and the queue still answers
        var listed = Task.Run(() => owner.Host.List());
        await Assert.That(listed.Wait(TimeSpan.FromSeconds(5))).IsTrue();

        block.Set();
        await enqueue;
        await Assert.That(owner.Launcher.Launches).IsEqualTo(1);
    }

    [Test]
    public async Task AcceptingSomethingAlreadyGoneIsUnknown()
    {
        using var owner = new Owner();
        owner.Queue();
        var snapshot = owner.Host.List().Single();
        owner.Host.Discard(snapshot, out _);

        var accepted = owner.Host.Accept(snapshot, out var message);

        await Assert.That(accepted).IsEqualTo(AcceptOutcome.Unknown);
        await Assert.That(message).IsNull();
    }

    /// <summary>
    /// The menu counts snapshots in "Discard (n)", so discarding has to include them.
    /// </summary>
    [Test]
    public async Task DiscardAllEmptiesTheQueue()
    {
        using var owner = new Owner();
        owner.Queue();
        owner.Queue(@"c:\repo\OtherTests.cs", 7);

        owner.Host.DiscardAll(out _);

        await Assert.That(owner.Host.List()).IsEmpty();
    }

    [Test]
    public async Task DiscardingRemovesWithoutApplying()
    {
        using var owner = new Owner();
        owner.Queue();
        var snapshot = owner.Host.List().Single();

        var discarded = owner.Host.Discard(snapshot, out var message);

        await Assert.That(discarded).IsTrue();
        await Assert.That(message).IsEqualTo("Discarded SampleTests.cs:42");
        await Assert.That(owner.Host.List()).IsEmpty();
    }

    /// <summary>
    /// The wedge this exists to prevent: applying can wait ten seconds on InlineApplier's cross
    /// process mutex, and the attached viewer polls a listing on a three second timeout, reading
    /// silence as the owner having died. So a listing must be answered while an accept is mid
    /// apply — which takes both the apply running outside the gate and the server handling
    /// connections concurrently.
    /// </summary>
    [Test]
    public async Task AListingIsAnsweredWhileAnAcceptIsApplying()
    {
        using var applying = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var owner = new Owner(_ =>
        {
            applying.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return InlineApplyResult.Applied;
        });
        try
        {
            owner.Queue();
            var snapshot = owner.Host.List().Single();

            var accepting = Task.Run(() => owner.Send(new(ViewerVerb.Accept, snapshot.Key)));
            applying.Wait(TimeSpan.FromSeconds(5));

            // On the default three second client timeout, so a listing that queued behind the
            // accept, on the gate or on the socket, fails here rather than passing slowly.
            var listing = owner.Send(new(ViewerVerb.ListFull));

            await Assert.That(listing.Ok).IsTrue();
            await Assert.That(listing.Items).HasSingleItem();

            release.Set();
            var accepted = await accepting;
            await Assert.That(accepted.Ok).IsTrue();
            await Assert.That(accepted.Message).IsEqualTo("Applied SampleTests.cs:42");
            await Assert.That(owner.Host.List()).IsEmpty();
        }
        finally
        {
            // Never leave the applier blocked when an assertion throws.
            release.Set();
        }
    }

    /// <summary>
    /// A re-run finishing while its old patch is mid apply must keep the new entry: the outcome
    /// describes the old patch and says nothing about the one that replaced it.
    /// </summary>
    [Test]
    public async Task AReplacedEntrySurvivesTheAcceptItInterrupted()
    {
        using var applying = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var owner = new Owner(_ =>
        {
            applying.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return InlineApplyResult.Applied;
        });
        try
        {
            owner.Queue(content: "first");
            var snapshot = owner.Host.List().Single();
            var accepting = Task.Run(() => owner.Send(new(ViewerVerb.Accept, snapshot.Key)));
            applying.Wait(TimeSpan.FromSeconds(5));

            // The same call site again, mid apply, saying something else: a re-run of a test
            // whose result moved on. What was applied is the old content, so the outcome
            // describes nothing that is pending now
            owner.Queue(content: "second");
            release.Set();
            var accepted = await accepting;

            await Assert.That(accepted.Ok).IsFalse();
            await Assert.That(owner.Host.List()).HasSingleItem();
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>
    /// The same window, and the far more common arrival in it: a still failing test re-running and
    /// producing what it produced before. Nothing was replaced, so the accept completes and the
    /// entry goes - rather than the patch reaching the file while the entry stays pending with
    /// nothing said about it.
    /// </summary>
    [Test]
    public async Task AnIdenticalReRunDoesNotInterruptTheAcceptItLandsIn()
    {
        using var applying = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var owner = new Owner(_ =>
        {
            applying.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return InlineApplyResult.Applied;
        });
        try
        {
            owner.Queue(content: "same");
            var snapshot = owner.Host.List().Single();
            var accepting = Task.Run(() => owner.Send(new(ViewerVerb.Accept, snapshot.Key)));
            applying.Wait(TimeSpan.FromSeconds(5));

            owner.Queue(content: "same");
            release.Set();
            var accepted = await accepting;

            await Assert.That(accepted.Ok).IsTrue();
            await Assert.That(owner.Host.List()).IsEmpty();
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>
    /// The ownership gate. A tray that starts while a viewer holds the port does not own the
    /// queue, and drives it remotely for the rest of its life instead.
    /// </summary>
    [Test]
    public async Task OnlyOneProcessCanOwnTheQueue()
    {
        using var owner = new Owner();

        var second = OwnedInlineHost.TryOwn(_ => { }, new FakeLauncher(), owner.Host.Port);

        await Assert.That(second).IsNull();
    }

    /// <summary>
    /// A multi-targeted run disagreeing with itself is one entry whose listing status says so, so
    /// the tray menu shows the conflict without knowing the variant model exists.
    /// </summary>
    [Test]
    public async Task TwoFrameworksWithDifferentContentConflict()
    {
        using var owner = new Owner();
        owner.Queue(content: "eight", framework: "net8.0");
        owner.Queue(content: "nine", framework: "net9.0");

        var snapshot = owner.Host.List().Single();

        await Assert.That(snapshot.Status).IsEqualTo("Conflicting snapshots (net8.0 / net9.0)");
    }

    [Test]
    public async Task AConflictedAcceptIsRefusedOverTheWire()
    {
        using var owner = new Owner(_ => throw new("must not be applied"));
        owner.Queue(content: "eight", framework: "net8.0");
        owner.Queue(content: "nine", framework: "net9.0");

        var response = owner.Send(new(ViewerVerb.Accept, InlineKey.For(@"c:\repo\SampleTests.cs", 42)));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.Message).IsEqualTo("Conflicting snapshots (net8.0 / net9.0), resolve in the viewer");
        await Assert.That(owner.Host.List()).HasSingleItem();
    }

    /// <summary>
    /// The accept body is the variant a reviewer picked, and accepting it resolves the whole call
    /// site.
    /// </summary>
    [Test]
    public async Task AnAcceptWithAnOriginBodyAppliesThatVariant()
    {
        var applied = new List<InlinePatch>();
        using var owner = new Owner(_ =>
        {
            applied.Add(_);
            return InlineApplyResult.Applied;
        });
        owner.Queue(content: "eight", framework: "net8.0");
        owner.Queue(content: "nine", framework: "net9.0");

        var response = owner.Send(new(ViewerVerb.Accept, InlineKey.For(@"c:\repo\SampleTests.cs", 42), "net9.0"));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(applied.Single().NewContent).IsEqualTo("nine");
        await Assert.That(owner.Host.List()).IsEmpty();
    }

    [Test]
    public async Task AcceptAllOverTheWireSkipsConflicts()
    {
        using var owner = new Owner(_ => InlineApplyResult.Applied);
        owner.Queue(@"c:\repo\OtherTests.cs", 7);
        owner.Queue(content: "eight", framework: "net8.0");
        owner.Queue(content: "nine", framework: "net9.0");

        var response = owner.Send(new(ViewerVerb.AcceptAll));

        await Assert.That(response.Message).IsEqualTo("Accepted 1, 1 conflict needs review");
        await Assert.That(owner.Host.List()).HasSingleItem();
    }

    [Test]
    public async Task ASettleWithABodyRemovesOneVariant()
    {
        using var owner = new Owner();
        owner.Queue(content: "eight", framework: "net8.0");
        owner.Queue(content: "nine", framework: "net9.0");

        owner.Send(new(ViewerVerb.Settle, InlineKey.For(@"c:\repo\SampleTests.cs", 42), "net8.0"));
        await Assert.That(owner.Host.List().Single().Status).IsNull();

        owner.Send(new(ViewerVerb.Settle, InlineKey.For(@"c:\repo\SampleTests.cs", 42), "net9.0"));
        await Assert.That(owner.Host.List()).IsEmpty();
    }

    [Test]
    public async Task AFullListingCarriesVariantLines()
    {
        using var owner = new Owner();
        owner.Queue(content: "eight", framework: "net8.0");
        owner.Queue(content: "nine", framework: "net9.0");

        var response = owner.Send(new(ViewerVerb.ListFull));

        var item = response.Items.Single();
        await Assert.That(item.Origins).IsEquivalentTo(["net8.0"]);
        var variant = item.Variants.Single();
        await Assert.That(variant.Origins).IsEquivalentTo(["net9.0"]);
        await Assert.That(InlinePatchFile.TryParse(variant.Patch, out var patch)).IsTrue();
        await Assert.That(patch!.NewContent).IsEqualTo("nine");
    }

    sealed class FakeTracked : ITrackedFiles
    {
        public List<string> Accepted { get; } = [];
        public List<string> Discarded { get; } = [];
        public (int accepted, int kept) SweepResult { get; set; }
        public IReadOnlyList<ViewerResponseMove> MoveList { get; set; } = [];
        public IReadOnlyList<ViewerResponseDelete> DeleteList { get; set; } = [];

        public IReadOnlyList<ViewerResponseMove> Moves() => MoveList;

        public IReadOnlyList<ViewerResponseDelete> Deletes() => DeleteList;

        public bool Has(string key) =>
            MoveList.Any(_ => _.Key == key) ||
            DeleteList.Any(_ => _.Key == key);

        public (bool ok, string? message) Accept(string key)
        {
            Accepted.Add(key);
            return (true, "Accepted tracked");
        }

        public (bool ok, string? message) Discard(string key)
        {
            Discarded.Add(key);
            return (true, "Discarded tracked");
        }

        public (int accepted, int kept) AcceptAll() => SweepResult;

        public int DiscardAll() => 0;

        public List<string> Added { get; } = [];

        public void AddMove(string temp, string target) =>
            Added.Add($"move {temp} > {target}");

        public void AddDelete(string file) =>
            Added.Add($"delete {file}");
    }

    [Test]
    public async Task AFullListingCarriesTrackedMovesAndDeletes()
    {
        using var owner = new Owner();
        owner.Host.TrackedFiles = new FakeTracked
        {
            MoveList = [new(@"move:c:\temp\a.txt", "Sample.Test (txt)", "MySolution", @"c:\temp\a.txt", @"c:\code\a.txt")],
            DeleteList = [new(@"delete:c:\code\b.txt", "b.txt", null, @"c:\code\b.txt")]
        };
        owner.Queue();

        var response = owner.Send(new(ViewerVerb.ListFull));

        await Assert.That(response.Moves.Single().Group).IsEqualTo("MySolution");
        await Assert.That(response.Deletes.Single().Name).IsEqualTo("b.txt");

        // The plain listing drives the tray menu, which reads the tracker directly.
        var plain = owner.Send(new(ViewerVerb.List));
        await Assert.That(plain.Moves).IsEmpty();
        await Assert.That(plain.Deletes).IsEmpty();
    }

    [Test]
    public async Task ATrackedKeyAcceptDispatchesToTrackedFiles()
    {
        using var owner = new Owner(_ => throw new("the inline queue must not be touched"));
        var tracked = new FakeTracked();
        owner.Host.TrackedFiles = tracked;
        owner.Queue();

        var response = owner.Send(new(ViewerVerb.Accept, @"move:c:\temp\a.txt"));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(response.Message).IsEqualTo("Accepted tracked");
        await Assert.That(tracked.Accepted).IsEquivalentTo([@"move:c:\temp\a.txt"]);
        await Assert.That(owner.Host.List()).HasSingleItem();
    }

    [Test]
    public async Task ATrackedKeyWithNoTrackedFilesIsUnknown()
    {
        using var owner = new Owner();
        owner.Queue();

        var response = owner.Send(new(ViewerVerb.Accept, @"move:c:\temp\a.txt"));

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.Message).IsEqualTo(@"No pending snapshot for move:c:\temp\a.txt");
    }

    /// <summary>
    /// The wire's accept-all sweeps what the viewer was shown: tracked files as well as the
    /// snapshots, with what stayed pending counted rather than hidden.
    /// </summary>
    [Test]
    public async Task AnAcceptAllSweepsTrackedFiles()
    {
        using var owner = new Owner(_ => InlineApplyResult.Applied);
        owner.Host.TrackedFiles = new FakeTracked
        {
            SweepResult = (2, 1)
        };
        owner.Queue();

        var response = owner.Send(new(ViewerVerb.AcceptAll));

        await Assert.That(response.Message).IsEqualTo("Accepted 1, plus 2 files (1 kept)");
    }
}
