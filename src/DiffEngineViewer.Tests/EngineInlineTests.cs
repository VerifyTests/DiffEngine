extern alias engine;

using EnginePatch = engine::DiffEngine.InlinePatch;
using EngineResult = engine::DiffEngine.InlineResult;
using EngineRunner = engine::DiffEngine.DiffRunner;
using EngineViewerClient = engine::DiffEngine.ViewerClient;

/// <summary>
/// DiffEngine's public inline entry point, driven against a real <see cref="ViewerServer"/> and
/// <see cref="MessageHandler"/> rather than a stand in, so the whole path is covered: DiffRunner
/// builds the payload, sends it over a loopback socket, and the viewer's own reader queues it.
/// <para>
/// Nothing here launches a viewer process. Resolution and launching are covered by the end to end
/// check in the readme, because a test that spawns a window would open one on any machine that
/// happens to have DiffEngineViewer installed.
/// </para>
/// </summary>
[NotInParallel]
public class EngineInlineTests
{
    [Test]
    public async Task QueuesIntoARunningViewer()
    {
        using var scope = new EngineScope();
        var patch = new EnginePatch("Sample.cs", 42, "\"old\"", "new content");

        var result = await EngineRunner.AddInlineAsync(patch);

        await Assert.That(result).IsEqualTo(EngineResult.Queued);
        var queue = scope.Fixture.Host.State.Queue;
        await Assert.That(queue).HasSingleItem();
        await Assert.That(queue[0].Name).IsEqualTo("Sample.cs:42");
        await Assert.That(queue[0].LeftText).IsEqualTo("new content");
        await Assert.That(queue[0].RightText).IsEqualTo("old");
    }

    /// <summary>
    /// A second failing run of the same test replaces its entry rather than appending, which only
    /// works if both sides derive the same key.
    /// </summary>
    [Test]
    public async Task ARepeatOfTheSameCallSiteReplaces()
    {
        using var scope = new EngineScope();

        await EngineRunner.AddInlineAsync(new("Sample.cs", 42, "\"old\"", "first"));
        await EngineRunner.AddInlineAsync(new("Sample.cs", 42, "\"old\"", "second"));

        var queue = scope.Fixture.Host.State.Queue;
        await Assert.That(queue).HasSingleItem();
        await Assert.That(queue[0].LeftText).IsEqualTo("second");
    }

    [Test]
    public async Task SettleDropsTheEntry()
    {
        using var scope = new EngineScope();
        await EngineRunner.AddInlineAsync(new EnginePatch("Sample.cs", 42, "\"old\"", "new"));
        await EngineRunner.AddInlineAsync(new EnginePatch("Other.cs", 7, "\"old\"", "new"));

        EngineRunner.SettleInline("Sample.cs", 42);

        var queue = scope.Fixture.Host.State.Queue;
        await Assert.That(queue).HasSingleItem();
        await Assert.That(queue[0].Name).IsEqualTo("Other.cs:7");
    }

    [Test]
    public async Task SettleForAnUnknownCallSiteIsHarmless()
    {
        using var scope = new EngineScope();
        await EngineRunner.AddInlineAsync(new EnginePatch("Sample.cs", 42, "\"old\"", "new"));

        EngineRunner.SettleInline("Nothing.cs", 1);

        await Assert.That(scope.Fixture.Host.State.Queue).HasSingleItem();
    }

    /// <summary>
    /// Disabled covers build servers, continuous testing and AI CLIs, so nothing must reach the
    /// viewer even when one is listening.
    /// </summary>
    [Test]
    public async Task DisabledDoesNotReachTheViewer()
    {
        using var scope = new EngineScope(disabled: true);

        var result = await EngineRunner.AddInlineAsync(new EnginePatch("Sample.cs", 42, "\"old\"", "new"));

        await Assert.That(result).IsEqualTo(EngineResult.Disabled);
        await Assert.That(scope.Fixture.Host.State.Queue).IsEmpty();
    }

    [Test]
    public async Task TheOptOutDoesNotReachTheViewer()
    {
        using var scope = new EngineScope(optOut: true);

        var result = await EngineRunner.AddInlineAsync(new EnginePatch("Sample.cs", 42, "\"old\"", "new"));

        await Assert.That(result).IsEqualTo(EngineResult.NoViewerFound);
        await Assert.That(scope.Fixture.Host.State.Queue).IsEmpty();
    }

    /// <summary>
    /// Points DiffEngine at a real viewer on an ephemeral port and restores every piece of global
    /// state it touches.
    /// </summary>
    sealed class EngineScope : IDisposable
    {
        readonly string? previousPort;
        readonly string? previousOptOut;
        readonly bool previousDisabled;

        public EngineScope(bool disabled = false, bool optOut = false)
        {
            Fixture = new();
            previousPort = Environment.GetEnvironmentVariable(EngineViewerClient.PortVariable);
            previousOptOut = Environment.GetEnvironmentVariable(EngineRunner.InlineViewerVariable);
            previousDisabled = EngineRunner.Disabled;

            Environment.SetEnvironmentVariable(EngineViewerClient.PortVariable, Fixture.Server.Port.ToString());
            Environment.SetEnvironmentVariable(EngineRunner.InlineViewerVariable, optOut ? "false" : null);
            // Off by default in this process, because an AI CLI counts as disabled.
            EngineRunner.Disabled = disabled;
        }

        public ServerFixture Fixture { get; }

        public void Dispose()
        {
            Fixture.Dispose();
            EngineRunner.Disabled = previousDisabled;
            Environment.SetEnvironmentVariable(EngineViewerClient.PortVariable, previousPort);
            Environment.SetEnvironmentVariable(EngineRunner.InlineViewerVariable, previousOptOut);
        }
    }
}
