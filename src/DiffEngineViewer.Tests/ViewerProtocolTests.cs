extern alias engine;

using EngineMode = engine::DiffEngine.InlinePatchMode;
using EnginePatch = engine::DiffEngine.InlinePatch;
using EnginePatchFile = engine::DiffEngine.InlinePatchFile;
using EnginePayload = engine::DiffEngine.ViewerPayload;

/// <summary>
/// DiffEngine and the viewer each own their half of the wire format, because DiffEngine targets
/// down to net462 and stays AOT compatible while the viewer is net10 only. These tests are what
/// stops the two halves drifting.
/// </summary>
public class ViewerProtocolTests
{
    [Test]
    public async Task EngineInlineMessageIsReadableByTheViewer()
    {
        var patch = new EnginePatch("Tests.cs", 42, "\"old\"", "new content");

        var payload = EnginePayload.Inline(EnginePatchFile.Build(patch));

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Verb).IsEqualTo(ViewerVerb.Inline);
        await Assert.That(InlinePatchFile.TryParse(message.Body!, out var roundTripped)).IsTrue();
        await Assert.That(roundTripped!.SourceFile).IsEqualTo("Tests.cs");
        await Assert.That(roundTripped.LineHint).IsEqualTo(42);
        await Assert.That(roundTripped.OriginalExpression).IsEqualTo("\"old\"");
        await Assert.That(roundTripped.NewContent).IsEqualTo("new content");
    }

    /// <summary>
    /// Snapshot text routinely contains quotes, braces and newlines, which is why every value on
    /// the wire is base64 rather than escaped.
    /// </summary>
    [Test]
    public async Task AwkwardSnapshotTextSurvivesTheRoundTrip()
    {
        var content = "line \"one\"\n\tbraces {} and | pipes\r\nversion: 1\nverb: quit\n";
        var patch = new EnginePatch("Tests.cs", 1, null, content);

        var payload = EnginePayload.Inline(EnginePatchFile.Build(patch));

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(InlinePatchFile.TryParse(message!.Body!, out var roundTripped)).IsTrue();
        await Assert.That(roundTripped!.NewContent).IsEqualTo(content);
        await Assert.That(roundTripped.OriginalExpression).IsNull();
    }

    /// <summary>
    /// The mode is written by name, so the two enums have to stay member for member identical.
    /// Both sides are enumerated rather than listed, so a member added to one and not the other
    /// fails here rather than at a call site nobody wrote yet.
    /// </summary>
    [Test]
    public async Task ModesAgree()
    {
        var engineModes = Enum.GetNames(typeof(EngineMode));

        await Assert.That(engineModes).IsEquivalentTo(Enum.GetNames<InlinePatchMode>());

        foreach (var name in engineModes)
        {
            var engineMode = (EngineMode) Enum.Parse(typeof(EngineMode), name);
            var payload = EnginePayload.Inline(EnginePatchFile.Build(new("Tests.cs", 1, null, "content", engineMode)));

            await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
            await Assert.That(InlinePatchFile.TryParse(message!.Body!, out var roundTripped)).IsTrue();
            await Assert.That(roundTripped!.Mode).IsEqualTo(Enum.Parse<InlinePatchMode>(name));
        }
    }

    [Test]
    public async Task EngineSettleMessageIsReadableByTheViewer()
    {
        var payload = EnginePayload.Settle("Tests.cs", 42);

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Verb).IsEqualTo(ViewerVerb.Settle);
        await Assert.That(message.Key).IsEqualTo(QueueEntry.KeyForInline("Tests.cs", 42));
    }

    /// <summary>
    /// Settle only works if both sides derive the same key from the same source and line.
    /// </summary>
    [Test]
    [Arguments("Tests.cs", 42)]
    [Arguments(@"C:\Repo\Some.Tests\Sample.cs", 1)]
    [Arguments("/home/user/Sample.cs", 9999)]
    [Arguments("MiXeDCase.CS", 7)]
    public async Task KeysAgree(string sourceFile, int line) =>
        await Assert.That(EnginePayload.Key(sourceFile, line))
            .IsEqualTo(QueueEntry.KeyForInline(sourceFile, line));

    /// <summary>
    /// DiffEngine's client treats this literal as the acknowledgement, so the viewer has to keep
    /// emitting it.
    /// </summary>
    [Test]
    public async Task ViewerAcknowledgementMatchesWhatTheEngineLooksFor()
    {
        await Assert.That(ViewerResponse.Success().Build()).Contains("status: ok");
        await Assert.That(ViewerResponse.Success("queued 1").Build()).Contains("status: ok");
        await Assert.That(ViewerResponse.Error("nope").Build()).DoesNotContain("status: ok");
    }

    /// <summary>
    /// A full listing carries the payload each entry was queued from, which is what lets a viewer
    /// showing a queue it does not own rebuild every pane without a diff crossing the wire.
    /// </summary>
    [Test]
    public async Task FullListingRoundTripsThePatch()
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
    /// The tray reads a listing by looking for `item: ` and splitting three base64 fields with its
    /// own decoder. Nothing else holds that reader to this writer, so the shape is pinned here.
    /// </summary>
    [Test]
    public async Task ListItemsKeepTheShapeTheTrayReads()
    {
        var text = ViewerResponse.Listing([new("the key", "Sample.cs:42", "locked")]).Build();

        var parts = Fields(text, "item: ").Single();
        await Assert.That(parts.Length).IsEqualTo(3);
        await Assert.That(Decoded(parts[0])).IsEqualTo("the key");
        await Assert.That(Decoded(parts[1])).IsEqualTo("Sample.cs:42");
        await Assert.That(Decoded(parts[2])).IsEqualTo("locked");
    }

    /// <summary>
    /// Patches ride on their own line name, so that reader skips a full listing entirely rather
    /// than tripping over a fourth field it knows nothing about.
    /// </summary>
    [Test]
    public async Task AFullListingHasNoItemLines()
    {
        var patch = InlinePatchFile.Build(new("Tests.cs", 1, null, "content"));
        var text = ViewerResponse.Listing([new("key", "Tests.cs:1", null, patch)]).Build();

        await Assert.That(Fields(text, "item: ")).IsEmpty();
        await Assert.That(Fields(text, "full: ").Single().Length).IsEqualTo(4);
    }

    static List<string[]> Fields(string text, string prefix) =>
        text.Split('\n')
            .Where(_ => _.StartsWith(prefix, StringComparison.Ordinal))
            .Select(_ => _[prefix.Length..].Split('|'))
            .ToList();

    static string Decoded(string value)
    {
        EnginePayload.TryDecode(value, out var decoded);
        return decoded;
    }

    /// <summary>
    /// Read into locals rather than compared directly, because both sides declare these as
    /// constants and a constant to constant assertion is compiled away.
    /// </summary>
    [Test]
    public async Task ContractConstantsAgree()
    {
        var engineVersion = EnginePayload.Version;
        var enginePort = engine::DiffEngine.ViewerClient.DefaultPort;
        var engineVariable = engine::DiffEngine.ViewerClient.PortVariable;

        await Assert.That(engineVersion).IsEqualTo(Payload.Version);
        await Assert.That(enginePort).IsEqualTo(ViewerPort.Default);
        await Assert.That(engineVariable).IsEqualTo(ViewerPort.Variable);
    }
}
