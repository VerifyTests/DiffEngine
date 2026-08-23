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
    // These cover the path a patch takes, not what a reviewer reads at the end of it, so nothing
    // here is named.
    static EnginePatch Patch(
        string source,
        int line,
        string? expression,
        string content,
        engine::DiffEngine.InlinePatchMode mode = engine::DiffEngine.InlinePatchMode.Set) =>
        new(source, line, expression, content, mode)
        {
            TestName = null
        };

    [Test]
    public async Task QueuesIntoARunningViewer()
    {
        using var scope = new EngineScope();
        var patch = Patch("Sample.cs", 42, "\"old\"", "new content");

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

        await EngineRunner.AddInlineAsync(Patch("Sample.cs", 42, "\"old\"", "first"));
        await EngineRunner.AddInlineAsync(Patch("Sample.cs", 42, "\"old\"", "second"));

        var queue = scope.Fixture.Host.State.Queue;
        await Assert.That(queue).HasSingleItem();
        await Assert.That(queue[0].LeftText).IsEqualTo("second");
    }

    [Test]
    public async Task SettleDropsTheEntry()
    {
        using var scope = new EngineScope();
        await EngineRunner.AddInlineAsync(Patch("Sample.cs", 42, "\"old\"", "new"));
        await EngineRunner.AddInlineAsync(Patch("Other.cs", 7, "\"old\"", "new"));

        EngineRunner.SettleInline("Sample.cs", 42);

        var queue = scope.Fixture.Host.State.Queue;
        await Assert.That(queue).HasSingleItem();
        await Assert.That(queue[0].Name).IsEqualTo("Other.cs:7");
    }

    [Test]
    public async Task SettleForAnUnknownCallSiteIsHarmless()
    {
        using var scope = new EngineScope();
        await EngineRunner.AddInlineAsync(Patch("Sample.cs", 42, "\"old\"", "new"));

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

        var result = await EngineRunner.AddInlineAsync(Patch("Sample.cs", 42, "\"old\"", "new"));

        await Assert.That(result).IsEqualTo(EngineResult.Disabled);
        await Assert.That(scope.Fixture.Host.State.Queue).IsEmpty();
    }

    /// <summary>
    /// Removing a literal is a configuration change with nothing to review. Rejected at the entry
    /// point rather than at the far end, so a caller cannot spend a viewer launch to find out.
    /// </summary>
    [Test]
    public async Task ARemovePatchIsRefused()
    {
        using var scope = new EngineScope();
        var patch = Patch("Sample.cs", 42, "\"old\"", "", engine::DiffEngine.InlinePatchMode.Remove);

        await Assert.That(() => EngineRunner.AddInlineAsync(patch)).Throws<ArgumentException>();
        await Assert.That(scope.Fixture.Host.State.Queue).IsEmpty();
    }

    /// <summary>
    /// The trap <see cref="EngineRunner.SettleAppliedInline" /> exists for. A settle names the
    /// running process's framework, which is the test run's for the caller that verb was written
    /// for, and something else entirely for a surface that applies a patch of its own. The owner
    /// finds no variant carrying that label and answers no differently than if it had.
    /// </summary>
    [Test]
    public async Task SettleMissesAnEntryQueuedByAnotherFramework()
    {
        using var scope = new EngineScope();
        var patch = Patch("Sample.cs", 42, "\"old\"", "new");
        // A moniker no process can report, standing in for a test project this one is not.
        patch.Framework = "net99.0";
        await EngineRunner.AddInlineAsync(patch);

        EngineRunner.SettleInline("Sample.cs", 42);

        await Assert.That(scope.Fixture.Host.State.Queue).HasSingleItem();
    }

    [Test]
    public async Task SettleAppliedDropsTheEntryWhateverFrameworkQueuedIt()
    {
        using var scope = new EngineScope();
        var patch = Patch("Sample.cs", 42, "\"old\"", "new");
        patch.Framework = "net99.0";
        await EngineRunner.AddInlineAsync(patch);
        await EngineRunner.AddInlineAsync(Patch("Other.cs", 7, "\"old\"", "new"));

        EngineRunner.SettleAppliedInline(patch);

        var queue = scope.Fixture.Host.State.Queue;
        await Assert.That(queue).HasSingleItem();
        await Assert.That(queue[0].Name).IsEqualTo("Other.cs:7");
    }

    /// <summary>
    /// Applying one call site moves every later one in the file, so the line an applier reports is
    /// no longer the line the entry was queued at. The member is what survives that, and the patch
    /// is carrying it.
    /// </summary>
    [Test]
    public async Task SettleAppliedFindsAnEntryWhoseLineHasMoved()
    {
        using var scope = new EngineScope();
        var queued = Patch("Sample.cs", 42, "\"old\"", "new");
        queued.MemberName = "TheTest";
        await EngineRunner.AddInlineAsync(queued);

        var applied = Patch("Sample.cs", 48, "\"old\"", "new");
        applied.MemberName = "TheTest";
        EngineRunner.SettleAppliedInline(applied);

        await Assert.That(scope.Fixture.Host.State.Queue).IsEmpty();
    }

    [Test]
    public async Task SettleAppliedForAnUnknownCallSiteIsHarmless()
    {
        using var scope = new EngineScope();
        await EngineRunner.AddInlineAsync(Patch("Sample.cs", 42, "\"old\"", "new"));

        EngineRunner.SettleAppliedInline(Patch("Nothing.cs", 1, "\"old\"", "new"));

        await Assert.That(scope.Fixture.Host.State.Queue).HasSingleItem();
    }

    [Test]
    public async Task TheOptOutDoesNotReachTheViewer()
    {
        using var scope = new EngineScope(optOut: true);

        var result = await EngineRunner.AddInlineAsync(Patch("Sample.cs", 42, "\"old\"", "new"));

        await Assert.That(result).IsEqualTo(EngineResult.NoViewerFound);
        await Assert.That(scope.Fixture.Host.State.Queue).IsEmpty();
    }
}
