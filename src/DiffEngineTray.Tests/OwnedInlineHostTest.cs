/// <summary>
/// The tray holding the inline queue, driven over a real socket the way DiffEngine and a viewer
/// drive it. The arrangement this replaces, where a viewer owns the queue and the tray is a remote
/// control, is still covered by <see cref="TrackerSnapshotTest"/>, because it is still what happens
/// when a viewer bound the port first.
/// </summary>
public class OwnedInlineHostTest
{
    sealed class FakeLauncher : IViewerLauncher
    {
        public int Launches { get; private set; }

        public bool Running { get; set; }

        public bool Succeed { get; set; } = true;

        public bool Launch()
        {
            Launches++;
            Running = Succeed;
            return Succeed;
        }
    }

    sealed class Owner : IDisposable
    {
        public Owner() =>
            Host = OwnedInlineHost.TryOwn(Warnings.Add, Launcher, 0) ??
                   throw new("Could not bind an ephemeral port.");

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

        public ViewerResponse Queue(string source = @"c:\repo\SampleTests.cs", int line = 42) =>
            Send(new(ViewerVerb.Inline, Body: InlinePatchFile.Build(new(source, line, "\"old\"", "new"))));

        public void Dispose() =>
            Host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
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

        await Assert.That(accepted).IsFalse();
        await Assert.That(message).Contains("Source file does not exist");
        await Assert.That(owner.Host.List().Single().Status).IsEqualTo(message);
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
}
