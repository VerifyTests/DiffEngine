/// <summary>
/// The wire format between DiffEngine, DiffEngineTray and DiffEngineViewer.
/// <para>
/// Every value is base64 for the same reason <see cref="InlinePatchFile"/>'s are: snapshot text
/// routinely contains quotes, braces and newlines, and the `inline` body carries a whole
/// InlinePatchFile payload verbatim rather than escaped into something else. These tests are the
/// only thing pinning that shape, and they run on every framework DiffEngine targets, which is
/// where the async socket paths differ.
/// </para>
/// </summary>
public class ViewerProtocolTests
{
    // These pin the wire shape rather than what a reviewer reads, so nothing here is named. The
    // one test that is about the name says so itself.
    static InlinePatch Patch(
        string source,
        int line,
        string? expression,
        string content,
        InlinePatchMode mode = InlinePatchMode.Set,
        string? framework = null) =>
        new(source, line, expression, content, mode)
        {
            TestName = null,
            Framework = framework
        };

    [Test]
    public async Task InlineMessageRoundTrips()
    {
        var patch = Patch("Tests.cs", 42, "\"old\"", "new content");

        var payload = new ViewerMessage(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch)).Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Verb).IsEqualTo(ViewerVerb.Inline);
        await Assert.That(InlinePatchFile.TryParse(message.Body!, out var roundTripped)).IsTrue();
        await Assert.That(roundTripped!.SourceFile).IsEqualTo("Tests.cs");
        await Assert.That(roundTripped.LineHint).IsEqualTo(42);
        await Assert.That(roundTripped.OriginalExpression).IsEqualTo("\"old\"");
        await Assert.That(roundTripped.NewContent).IsEqualTo("new content");
    }

    /// <summary>
    /// Including text that looks like the protocol itself, which is what the encoding is for.
    /// </summary>
    [Test]
    public async Task AwkwardSnapshotTextSurvivesTheRoundTrip()
    {
        var content = "line \"one\"\n\tbraces {} and | pipes\r\nversion: 1\nverb: quit\n";
        var patch = Patch("Tests.cs", 1, null, content);

        var payload = new ViewerMessage(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch)).Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(InlinePatchFile.TryParse(message!.Body!, out var roundTripped)).IsTrue();
        await Assert.That(roundTripped!.NewContent).IsEqualTo(content);
        await Assert.That(roundTripped.OriginalExpression).IsNull();
    }

    /// <summary>
    /// The mode is written by name, so every member has to survive the trip. Enumerated rather
    /// than listed, so one added later is covered without anyone remembering to add it here.
    /// </summary>
    [Test]
    public async Task EveryModeRoundTrips()
    {
        foreach (var name in Enum.GetNames(typeof(InlinePatchMode)))
        {
            var mode = (InlinePatchMode) Enum.Parse(typeof(InlinePatchMode), name);
            var patch = Patch("Tests.cs", 1, null, "content", mode);
            var payload = new ViewerMessage(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch)).Build();

            await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
            await Assert.That(InlinePatchFile.TryParse(message!.Body!, out var roundTripped)).IsTrue();
            await Assert.That(roundTripped!.Mode).IsEqualTo(mode);
        }
    }

    /// <summary>
    /// Verbs go on the wire lower cased and parse back case insensitively, so a two word one like
    /// acceptall is not quietly a different verb at each end.
    /// </summary>
    [Test]
    public async Task EveryVerbRoundTrips()
    {
        foreach (var name in Enum.GetNames(typeof(ViewerVerb)))
        {
            var verb = (ViewerVerb) Enum.Parse(typeof(ViewerVerb), name);

            await Assert.That(new ViewerMessage(verb).Build()).Contains($"verb: {name.ToLowerInvariant()}\n");
            await Assert.That(ViewerMessage.TryParse(new ViewerMessage(verb).Build(), out var message)).IsTrue();
            await Assert.That(message!.Verb).IsEqualTo(verb);
        }
    }

    [Test]
    public async Task SettleCarriesTheKey()
    {
        // Already lower case, so what the key survives is the round trip rather than the folding
        var payload = new ViewerMessage(ViewerVerb.Settle, InlineKey.For("tests.cs", 42)).Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Verb).IsEqualTo(ViewerVerb.Settle);
        await Assert.That(message.Key).IsEqualTo("tests.cs|42");
    }

    /// <summary>
    /// Settling only works if the sender and the queue owner derive the same key from the same
    /// call site, so the format is pinned rather than left to whatever ToLower happens to do.
    /// </summary>
    /// These are already lower case, so they say the same thing wherever they run.
    [Test]
    [Arguments("tests.cs", 42, "tests.cs|42")]
    [Arguments(@"c:\repo\some.tests\sample.cs", 1, @"c:\repo\some.tests\sample.cs|1")]
    [Arguments("/home/user/sample.cs", 9999, "/home/user/sample.cs|9999")]
    public async Task KeyFormat(string sourceFile, int line, string expected) =>
        await Assert.That(InlineKey.For(sourceFile, line)).IsEqualTo(expected);

    /// <summary>
    /// A Windows path reaches here from several sources with several casings, and every one of
    /// them is the same call site.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    [Arguments("Tests.cs", 42, "tests.cs|42")]
    [Arguments(@"C:\Repo\Some.Tests\Sample.cs", 1, @"c:\repo\some.tests\sample.cs|1")]
    [Arguments("MiXeDCase.CS", 7, "mixedcase.cs|7")]
    public async Task KeyIsFoldedWhereThePathsAre(string sourceFile, int line, string expected) =>
        await Assert.That(InlineKey.For(sourceFile, line)).IsEqualTo(expected);

    /// <summary>
    /// And not where they are not. On Linux these are two files, and one key for both meant the
    /// second patch took over the first's entry and settling either settled both.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Linux)]
    public async Task KeysDifferingOnlyInCaseStayApartWhereTheFilesDo()
    {
        await Assert.That(InlineKey.For("/home/user/Sample.cs", 1)).IsEqualTo("/home/user/Sample.cs|1");
        await Assert.That(InlineKey.For("/home/user/sample.cs", 1))
            .IsNotEqualTo(InlineKey.For("/home/user/Sample.cs", 1));
    }

    /// <summary>
    /// A newer sender can add a field without breaking an older owner.
    /// </summary>
    [Test]
    public async Task AnUnknownFieldIsIgnored()
    {
        await Assert.That(ViewerMessage.TryParse("version: 1\nverb: quit\nwat: nope\n", out var message)).IsTrue();
        await Assert.That(message!.Verb).IsEqualTo(ViewerVerb.Quit);
    }

    [Test]
    [Arguments("")]
    [Arguments("verb: quit\n")]
    [Arguments("version: 99\nverb: quit\n")]
    [Arguments("version: 1\nverb: nonsense\n")]
    [Arguments("version: 1\n")]
    public async Task UnreadableRequestsAreRejected(string text) =>
        await Assert.That(ViewerMessage.TryParse(text, out _)).IsFalse();

    /// <summary>
    /// DiffEngine's client treats this literal as the acknowledgement, so the owner has to keep
    /// emitting it.
    /// </summary>
    [Test]
    public async Task TheAcknowledgementIsWhatTheClientLooksFor()
    {
        await Assert.That(ViewerResponse.Success().Build()).Contains("status: ok");
        await Assert.That(ViewerResponse.Success("queued 1").Build()).Contains("status: ok");
        await Assert.That(ViewerResponse.Error("nope").Build()).DoesNotContain("status: ok");
    }

    [Test]
    public async Task AListingItemCarriesKeyNameAndStatus()
    {
        var text = ViewerResponse.Listing([new("the key", "Sample.cs:42", "locked")]).Build();

        var parts = Fields(text, "item: ").Single();
        await Assert.That(parts.Length).IsEqualTo(3);
        await Assert.That(Decoded(parts[0])).IsEqualTo("the key");
        await Assert.That(Decoded(parts[1])).IsEqualTo("Sample.cs:42");
        await Assert.That(Decoded(parts[2])).IsEqualTo("locked");
    }

    /// <summary>
    /// A full listing carries the payload each entry was queued from, which is what lets a viewer
    /// showing a queue it does not own rebuild every pane without a diff crossing the wire.
    /// </summary>
    [Test]
    public async Task AFullListingRoundTripsThePatch()
    {
        var patch = Patch("Tests.cs", 42, "\"old\"", "new content");
        var listing = ViewerResponse.Listing(
        [
            new("tests.cs|42", "Tests.cs:42", "locked", InlinePatchFile.Build(patch))
        ]);

        await Assert.That(ViewerResponse.TryParse(listing.Build(), out var parsed)).IsTrue();
        var item = parsed!.Items.Single();
        await Assert.That(item.Key).IsEqualTo("tests.cs|42");
        await Assert.That(item.Status).IsEqualTo("locked");
        await Assert.That(InlinePatchFile.TryParse(item.Patch!, out var roundTripped)).IsTrue();
        await Assert.That(roundTripped!.SourceFile).IsEqualTo("Tests.cs");
        await Assert.That(roundTripped.LineHint).IsEqualTo(42);
        await Assert.That(roundTripped.NewContent).IsEqualTo("new content");
    }

    /// <summary>
    /// Patches ride on their own line name, so a reader that only wants a listing skips them
    /// rather than tripping over a fourth field.
    /// </summary>
    [Test]
    public async Task AFullListingHasNoItemLines()
    {
        var patch = InlinePatchFile.Build(Patch("Tests.cs", 1, null, "content"));
        var text = ViewerResponse.Listing([new("key", "Tests.cs:1", null, patch)]).Build();

        await Assert.That(Fields(text, "item: ")).IsEmpty();
        await Assert.That(Fields(text, "full: ").Single().Length).IsEqualTo(5);
    }

    [Test]
    public async Task MetadataRidesTheInlineBody()
    {
        var patch = new InlinePatch("Tests.cs", 42, "\"old\"", "new content")
        {
            TestName = "Compare handles nulls",
            Framework = "net9.0"
        };

        var payload = new ViewerMessage(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch)).Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(InlinePatchFile.TryParse(message!.Body!, out var roundTripped)).IsTrue();
        await Assert.That(roundTripped!.TestName).IsEqualTo("Compare handles nulls");
        await Assert.That(roundTripped.Framework).IsEqualTo("net9.0");
    }

    /// <summary>
    /// The body is the settling framework, so a multi-targeted run only settles its own variant.
    /// </summary>
    [Test]
    public async Task SettleCarriesTheOriginInTheBody()
    {
        var payload = new ViewerMessage(ViewerVerb.Settle, InlineKey.For("tests.cs", 42), "net9.0").Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Key).IsEqualTo("tests.cs|42");
        await Assert.That(message.Body).IsEqualTo("net9.0");
    }

    [Test]
    public async Task SettleCarriesTheMember()
    {
        var payload = new ViewerMessage(ViewerVerb.Settle, InlineKey.For("tests.cs", 42), "net9.0", "MyTest")
            .Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Key).IsEqualTo("tests.cs|42");
        await Assert.That(message.Body).IsEqualTo("net9.0");
        await Assert.That(message.Member).IsEqualTo("MyTest");
    }

    /// <summary>
    /// The member is an added field, so a payload written before it existed still reads, which is
    /// what lets a newer sender talk to an older owner.
    /// </summary>
    [Test]
    public async Task SettleWithoutAMemberParses()
    {
        var payload = new ViewerMessage(ViewerVerb.Settle, InlineKey.For("tests.cs", 42), "net9.0").Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Member).IsNull();
    }

    [Test]
    public async Task AFullListingCarriesThePrimaryOrigins()
    {
        var patch = InlinePatchFile.Build(Patch("Tests.cs", 42, "\"old\"", "new content"));
        var listing = ViewerResponse.Listing(
        [
            new("tests.cs|42", "Tests.cs:42", null, patch)
            {
                Origins = ["net8.0", "net9.0"]
            }
        ]);

        var text = listing.Build();
        await Assert.That(Decoded(Fields(text, "full: ").Single()[3])).IsEqualTo("net8.0,net9.0");
        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        await Assert.That(parsed!.Items.Single().Origins).IsEquivalentTo(["net8.0", "net9.0"]);
    }

    /// <summary>
    /// Non-primary variants ride their own line name, keyed back to their entry, so an entry line
    /// stays one per call site however many frameworks disagree about it.
    /// </summary>
    [Test]
    public async Task AVariantLineRoundTrips()
    {
        var primary = InlinePatchFile.Build(Patch("Tests.cs", 42, "\"old\"", "eight"));
        var other = InlinePatchFile.Build(Patch("Tests.cs", 42, "\"old\"", "nine"));
        var listing = ViewerResponse.Listing(
        [
            new("tests.cs|42", "Tests.cs:42", null, primary)
            {
                Origins = ["net8.0"],
                Variants = [new(["net9.0"], other)]
            }
        ]);

        var text = listing.Build();
        await Assert.That(Fields(text, "variant: ").Single().Length).IsEqualTo(3);
        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        var variant = parsed!.Items.Single().Variants.Single();
        await Assert.That(variant.Origins).IsEquivalentTo(["net9.0"]);
        await Assert.That(InlinePatchFile.TryParse(variant.Patch, out var roundTripped)).IsTrue();
        await Assert.That(roundTripped!.NewContent).IsEqualTo("nine");
    }

    [Test]
    public async Task AMoveLineRoundTrips()
    {
        var listing = ViewerResponse.Listing(
            [],
            moves: [new(@"move:c:\temp\a.received.txt", "Sample.Test (txt)", "MySolution", @"c:\temp\a.received.txt", @"c:\code\a.verified.txt")]);

        var text = listing.Build();
        await Assert.That(Fields(text, "move: ").Single().Length).IsEqualTo(5);
        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        var move = parsed!.Moves.Single();
        await Assert.That(move.Key).IsEqualTo(@"move:c:\temp\a.received.txt");
        await Assert.That(move.Group).IsEqualTo("MySolution");
        await Assert.That(move.Temp).IsEqualTo(@"c:\temp\a.received.txt");
        await Assert.That(move.Target).IsEqualTo(@"c:\code\a.verified.txt");
    }

    [Test]
    public async Task ADeleteLineRoundTrips()
    {
        var listing = ViewerResponse.Listing(
            [],
            deletes: [new(@"delete:c:\code\extra.verified.txt", "extra.verified.txt", null, @"c:\code\extra.verified.txt")]);

        var text = listing.Build();
        await Assert.That(Fields(text, "delete: ").Single().Length).IsEqualTo(4);
        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        var delete = parsed!.Deletes.Single();
        await Assert.That(delete.Group).IsNull();
        await Assert.That(delete.File).IsEqualTo(@"c:\code\extra.verified.txt");
    }

    [Test]
    public async Task AListingWithoutTrackedItemsParsesEmpty()
    {
        var text = ViewerResponse.Listing([new("key", "Sample.cs:42", null)]).Build();

        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        await Assert.That(parsed!.Moves).IsEmpty();
        await Assert.That(parsed.Deletes).IsEmpty();
        await Assert.That(parsed.Items.Single().Variants).IsEmpty();
    }

    // Field counts are strict, like item and full: growth means a new line name, never a new field
    [Test]
    public async Task AMalformedMoveLineRejectsTheResponse()
    {
        var valid = ViewerResponse.Listing(
            [],
            moves: [new("move:x", "x", null, "x", "y")]).Build();
        var truncated = valid.Replace(
            $"|{ViewerPayload.Encode("y")}\n",
            "\n");

        await Assert.That(ViewerResponse.TryParse(truncated, out _)).IsFalse();
    }

    /// <summary>
    /// The routing contract: only the tray parses keys, and it tells its collections apart by
    /// prefix, which an inline key can never carry because a Windows path cannot put a colon
    /// there.
    /// </summary>
    [Test]
    public async Task TrackedKeysCannotCollideWithInlineKeys()
    {
        await Assert.That(TrackedKeys.ForMove(@"C:\Temp\A.txt")).IsEqualTo(@"move:c:\temp\a.txt");
        await Assert.That(TrackedKeys.ForDelete(@"C:\Code\B.txt")).IsEqualTo(@"delete:c:\code\b.txt");
        await Assert.That(TrackedKeys.IsTracked(@"move:c:\temp\a.txt")).IsTrue();
        await Assert.That(TrackedKeys.IsTracked(@"delete:c:\code\b.txt")).IsTrue();
        await Assert.That(TrackedKeys.IsTracked(InlineKey.For(@"C:\Repo\Tests.cs", 42))).IsFalse();
        await Assert.That(TrackedKeys.TryStrip(@"move:c:\temp\a.txt", TrackedKeys.MovePrefix, out var path)).IsTrue();
        await Assert.That(path).IsEqualTo(@"c:\temp\a.txt");
    }

    /// <summary>
    /// The shared projection both hosts list through. Without patches the conflict has to ride the
    /// status, because there are no variant lines to carry it; with them it stays structural and
    /// the real status is preserved.
    /// </summary>
    [Test]
    public async Task AConflictedEntryListsItsStatus()
    {
        var eight = Patch("Tests.cs", 42, "\"old\"", "eight", framework: "net8.0");
        var nine = Patch("Tests.cs", 42, "\"old\"", "nine", framework: "net9.0");
        var entry = new PendingInline([new(eight, ["net8.0"]), new(nine, ["net9.0"])]);

        var listed = ViewerListing.Items([entry], withPatches: false).Single();
        await Assert.That(listed.Status).IsEqualTo("Conflicting snapshots (net8.0 / net9.0)");

        var full = ViewerListing.Items([entry], withPatches: true).Single();
        await Assert.That(full.Status).IsNull();
        await Assert.That(full.Origins).IsEquivalentTo(["net8.0"]);
        await Assert.That(full.Variants).HasSingleItem();
    }

    /// <summary>
    /// The wire mapping for refusals: an entry that exists but was not acted on answers an error
    /// carrying the reason, distinct from the unknown-key error, so a remote surface shows why
    /// nothing happened.
    /// </summary>
    [Test]
    public async Task ARefusedAcceptGoesOnTheWireAsAnError()
    {
        var refusing = new FakeOwner((false, "Conflicting snapshots (net8.0 / net9.0), resolve in the viewer"));
        var refused = ViewerMessageHandler.Handle(refusing, new(ViewerVerb.Accept, "key"));
        await Assert.That(refused.Ok).IsFalse();
        await Assert.That(refused.Message).IsEqualTo("Conflicting snapshots (net8.0 / net9.0), resolve in the viewer");

        var unknown = ViewerMessageHandler.Handle(new FakeOwner((false, null)), new(ViewerVerb.Accept, "key"));
        await Assert.That(unknown.Ok).IsFalse();
        await Assert.That(unknown.Message).IsEqualTo("No pending snapshot for key");

        var done = ViewerMessageHandler.Handle(new FakeOwner((true, "Applied Tests.cs:42")), new(ViewerVerb.Accept, "key"));
        await Assert.That(done.Ok).IsTrue();
        await Assert.That(done.Message).IsEqualTo("Applied Tests.cs:42");
    }

    /// <summary>
    /// A pending file with no tray running. The paths ride key and body rather than an encoded
    /// payload, because that is all a tracked move or delete is.
    /// </summary>
    [Test]
    public async Task MoveAndDeleteReachTheOwner()
    {
        var owner = new FakeOwner((true, null));

        var move = ViewerMessageHandler.Handle(owner, new(ViewerVerb.Move, @"c:\temp\a.received.txt", @"c:\code\a.verified.txt"));
        var delete = ViewerMessageHandler.Handle(owner, new(ViewerVerb.Delete, @"c:\code\b.verified.txt"));

        await Assert.That(move.Ok).IsTrue();
        await Assert.That(delete.Ok).IsTrue();
        await Assert.That(owner.Tracked).IsEquivalentTo(
        [
            @"move c:\temp\a.received.txt > c:\code\a.verified.txt",
            @"delete c:\code\b.verified.txt"
        ]);
    }

    [Test]
    public async Task AMoveWithoutBothPathsIsRefused()
    {
        var owner = new FakeOwner((true, null));

        var noTarget = ViewerMessageHandler.Handle(owner, new(ViewerVerb.Move, @"c:\temp\a.received.txt"));
        var noFile = ViewerMessageHandler.Handle(owner, new(ViewerVerb.Delete));

        await Assert.That(noTarget.Ok).IsFalse();
        await Assert.That(noTarget.Message).IsEqualTo("Move requires a key and a body");
        await Assert.That(noFile.Ok).IsFalse();
        await Assert.That(noFile.Message).IsEqualTo("Delete requires a key");
        await Assert.That(owner.Tracked).IsEmpty();
    }

    /// <summary>
    /// The accept body is the variant origin a reviewer picked, and it has to reach the owner.
    /// </summary>
    [Test]
    public async Task AnAcceptForwardsItsOriginToTheOwner()
    {
        var owner = new FakeOwner((true, null));
        ViewerMessageHandler.Handle(owner, new(ViewerVerb.Accept, "key", "net9.0"));

        await Assert.That(owner.AcceptedOrigin).IsEqualTo("net9.0");
    }

    class FakeOwner((bool ok, string? message) act) :
        IQueueOwner
    {
        public string? AcceptedOrigin { get; private set; }

        public int Enqueue(InlinePatch patch) => 1;

        public void Settle(string key, string? origin, string? member)
        {
        }

        public List<string> Tracked { get; } = [];

        public void TrackMove(string temp, string target) =>
            Tracked.Add($"move {temp} > {target}");

        public void TrackDelete(string file) =>
            Tracked.Add($"delete {file}");

        public ViewerResponse Listing(bool withPatches) => ViewerResponse.Listing([]);

        public bool Has(string key) => true;

        public (bool ok, string? message) Accept(string key, string? origin)
        {
            AcceptedOrigin = origin;
            return act;
        }

        public (bool ok, string? message) Discard(string key) => act;

        public string? AcceptAll() => null;

        public string? DiscardAll() => null;

        public void Window(WindowCommand command, string? key)
        {
        }
    }

    /// <summary>
    /// How an owner with no window of its own drives one: answered on a listing, so there is still
    /// one port and no discovery order.
    /// </summary>
    [Test]
    public async Task EveryWindowCommandRidesOnAListing()
    {
        foreach (var name in Enum.GetNames(typeof(WindowCommand)))
        {
            var command = (WindowCommand) Enum.Parse(typeof(WindowCommand), name);
            var text = ViewerResponse.Listing([], command).Build();

            await Assert.That(text).Contains($"window: {name.ToLowerInvariant()}\n");
            await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
            await Assert.That(parsed!.Window).IsEqualTo(command);
        }
    }

    [Test]
    public async Task AListingWithNoWindowCommandSaysNothing()
    {
        var text = ViewerResponse.Listing([]).Build();

        await Assert.That(text).DoesNotContain("window:");
        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        await Assert.That(parsed!.Window).IsNull();
    }

    /// <summary>
    /// The client's three second default is what a real caller uses to decide the owner has died.
    /// The tests below are about what the owner answers rather than how fast, and CI starts six
    /// test assemblies at once on a two core runner, where an answer arriving on a scheduled task
    /// has twice missed that deadline. <see cref="ASlowExchangeDoesNotBlockTheNext"/> keeps the
    /// default, because being answered inside it while another exchange is held is the point there.
    /// </summary>
    static readonly TimeSpan underLoad = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Bind, serve and exchange for real. The one test here that is not pure string work, because
    /// the async socket calls take a different path on the frameworks without a token overload.
    /// </summary>
    [Test]
    public async Task AnOwnerAnswersAClient()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();
        var listening = server.Listen(_ => ViewerResponse.Success($"heard {_.Verb}"), cancel.Token);

        var sent = ViewerClient.TrySend(new(ViewerVerb.List), out var response, server.Port, underLoad);

        await Assert.That(sent).IsTrue();
        await Assert.That(response!.Ok).IsTrue();
        await Assert.That(response.Message).IsEqualTo("heard List");

        await cancel.CancelAsync();
        await Wait(listening);
    }

    /// <summary>
    /// Connections are handled concurrently, so one slow exchange does not stop the next from
    /// being answered. Accepting an inline snapshot legitimately takes seconds, and a client
    /// whose listing goes unanswered for that long concludes the owner has died.
    /// </summary>
    [Test]
    public async Task ASlowExchangeDoesNotBlockTheNext()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var listening = server.Listen(
            _ =>
            {
                if (_.Verb == ViewerVerb.Accept)
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }

                return ViewerResponse.Success($"heard {_.Verb}");
            },
            cancel.Token);

        try
        {
            var accepting = Task.Run(() =>
                ViewerClient.TrySend(new(ViewerVerb.Accept, "key"), out var slow, server.Port, TimeSpan.FromSeconds(15))
                    ? slow
                    : null, cancel.Token);
            entered.Wait(TimeSpan.FromSeconds(5));

            // Default timeout, while the accept is still held open.
            var sent = ViewerClient.TrySend(new(ViewerVerb.List), out var response, server.Port);

            await Assert.That(sent).IsTrue();
            await Assert.That(response!.Message).IsEqualTo("heard List");

            release.Set();
            var accepted = await accepting;
            await Assert.That(accepted!.Message).IsEqualTo("heard Accept");
        }
        finally
        {
            release.Set();
            await cancel.CancelAsync();
            await Wait(listening);
        }
    }

    /// <summary>
    /// Connections run on untracked tasks, so a throwing handler is answered as an error rather
    /// than vanishing silently and leaving the client to wait out its timeout.
    /// </summary>
    [Test]
    public async Task AThrowingHandlerAnswersAnError()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();
        var listening = server.Listen(_ => throw new("the handler is broken"), cancel.Token);

        var sent = ViewerClient.TrySend(new(ViewerVerb.List), out var response, server.Port, underLoad);

        await Assert.That(sent).IsTrue();
        await Assert.That(response!.Ok).IsFalse();
        await Assert.That(response.Message).IsEqualTo("the handler is broken");

        await cancel.CancelAsync();
        await Wait(listening);
    }

    /// <summary>
    /// The whole ownership model: whoever binds owns the queue, and nobody else can.
    /// </summary>
    [Test]
    public async Task ASecondBindIsRefused()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var first = bound!;

        await Assert.That(ViewerServer.TryBind(first.Port, out var second)).IsFalse();
        await Assert.That(second).IsNull();
    }

    [Test]
    public async Task AnAbsentOwnerIsNotAnError()
    {
        ViewerServer.TryBind(0, out var server);
        var port = server!.Port;
        server.Dispose();

        await Assert.That(ViewerClient.TrySend(new(ViewerVerb.List), out _, port)).IsFalse();
    }

    static async Task Wait(Task listening)
    {
        try
        {
            await listening.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            // Cancellation unwinds through the listener; nothing to report.
        }
    }

    static List<string[]> Fields(string text, string prefix) =>
        text.Split('\n')
            .Where(_ => _.StartsWith(prefix, StringComparison.Ordinal))
            .Select(_ => _.Substring(prefix.Length).Split('|'))
            .ToList();

    static string Decoded(string value)
    {
        ViewerPayload.TryDecode(value, out var decoded);
        return decoded!;
    }
}
