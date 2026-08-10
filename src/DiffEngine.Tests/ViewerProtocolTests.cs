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
    [Test]
    public async Task InlineMessageRoundTrips()
    {
        var patch = new InlinePatch("Tests.cs", 42, "\"old\"", "new content");

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
        var patch = new InlinePatch("Tests.cs", 1, null, content);

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
            var patch = new InlinePatch("Tests.cs", 1, null, "content", mode);
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
        var payload = new ViewerMessage(ViewerVerb.Settle, InlineKey.For("Tests.cs", 42)).Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Verb).IsEqualTo(ViewerVerb.Settle);
        await Assert.That(message.Key).IsEqualTo("tests.cs|42");
    }

    /// <summary>
    /// Settling only works if the sender and the queue owner derive the same key from the same
    /// call site, so the format is pinned rather than left to whatever ToLower happens to do.
    /// </summary>
    [Test]
    [Arguments("Tests.cs", 42, "tests.cs|42")]
    [Arguments(@"C:\Repo\Some.Tests\Sample.cs", 1, @"c:\repo\some.tests\sample.cs|1")]
    [Arguments("/home/user/Sample.cs", 9999, "/home/user/sample.cs|9999")]
    [Arguments("MiXeDCase.CS", 7, "mixedcase.cs|7")]
    public async Task KeyFormat(string sourceFile, int line, string expected) =>
        await Assert.That(InlineKey.For(sourceFile, line)).IsEqualTo(expected);

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
        var patch = new InlinePatch("Tests.cs", 42, "\"old\"", "new content");
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
        var patch = InlinePatchFile.Build(new("Tests.cs", 1, null, "content"));
        var text = ViewerResponse.Listing([new("key", "Tests.cs:1", null, patch)]).Build();

        await Assert.That(Fields(text, "item: ")).IsEmpty();
        await Assert.That(Fields(text, "full: ").Single().Length).IsEqualTo(4);
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
    /// Bind, serve and exchange for real. The one test here that is not pure string work, because
    /// the async socket calls take a different path on the frameworks without a token overload.
    /// </summary>
    [Test]
    public async Task AnOwnerAnswersAClient()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancellationTokenSource();
        var listening = server.Listen(_ => ViewerResponse.Success($"heard {_.Verb}"), cancel.Token);

        var sent = ViewerClient.TrySend(new(ViewerVerb.List), out var response, server.Port);

        await Assert.That(sent).IsTrue();
        await Assert.That(response!.Ok).IsTrue();
        await Assert.That(response.Message).IsEqualTo("heard List");

        cancel.Cancel();
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
