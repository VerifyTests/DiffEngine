extern alias engine;

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
