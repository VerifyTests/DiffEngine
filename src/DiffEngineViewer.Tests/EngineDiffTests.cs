extern alias engine;

using EngineLaunchResult = engine::DiffEngine.LaunchResult;
using EngineRunner = engine::DiffEngine.DiffRunner;
using EngineTool = engine::DiffEngine.DiffTool;
using EngineResolvedTool = engine::DiffEngine.ResolvedTool;

/// <summary>
/// A failing pair whose resolved diff tool is the viewer itself, driven through DiffEngine's
/// public launch and a real socket into a real <see cref="MessageHandler"/>.
/// <para>
/// The behaviour under test is that no window is launched per pair: the pair is queued with
/// whoever owns the port and a window is raised over the queue. Every other tool gets a process
/// of its own for every pair, which is what this replaces.
/// </para>
/// <para>
/// The tool is constructed rather than resolved, because resolution depends on a viewer being
/// installed or bundled beside the test run, and what is being covered is the route DiffEngine
/// takes once it knows the tool is the viewer.
/// </para>
/// </summary>
[NotInParallel]
public class EngineDiffTests :
    IDisposable
{
    [Test]
    public async Task APairJoinsTheQueueRatherThanTakingAWindow()
    {
        using var scope = new EngineScope();
        var (received, target) = Pair("Sample.Test");

        var result = await EngineRunner.LaunchAsync(Viewer(), received, target);

        await Assert.That(result).IsEqualTo(EngineLaunchResult.AlreadyRunningAndSupportsRefresh);
        var entry = scope.Fixture.Host.State.Queue.Single();
        await Assert.That(entry.Kind).IsEqualTo(QueueEntryKind.Move);
        await Assert.That(entry.Key).IsEqualTo(TrackedKeys.ForMove(received));
        await Assert.That(entry.LeftText).IsEqualTo("received");
        await Assert.That(entry.RightText).IsEqualTo("verified");
        // Raised over the entry that arrived, which is what a per pair window used to do by
        // existing at all.
        await Assert.That(scope.Fixture.Windows).IsEquivalentTo([WindowCommand.Focus]);
    }

    /// <summary>
    /// The whole point: the second pair is a second row, not a second window.
    /// </summary>
    [Test]
    public async Task ASecondPairJoinsTheSameQueue()
    {
        using var scope = new EngineScope();
        var first = Pair("First.Test");
        var second = Pair("Second.Test");

        await EngineRunner.LaunchAsync(Viewer(), first.Received, first.Target);
        await EngineRunner.LaunchAsync(Viewer(), second.Received, second.Target);

        await Assert.That(scope.Fixture.Host.State.Queue.Select(_ => _.Key))
            .IsEquivalentTo([TrackedKeys.ForMove(first.Received), TrackedKeys.ForMove(second.Received)]);
    }

    /// <summary>
    /// A re-run of the same failing test stages the same received file again, and a second row for
    /// it would be a duplicate rather than news.
    /// </summary>
    [Test]
    public async Task ARepeatOfThePairReplacesIt()
    {
        using var scope = new EngineScope();
        var (received, target) = Pair("Sample.Test");

        await EngineRunner.LaunchAsync(Viewer(), received, target);
        await File.WriteAllTextAsync(received, "changed");
        await EngineRunner.LaunchAsync(Viewer(), received, target);

        var entry = scope.Fixture.Host.State.Queue.Single();
        await Assert.That(entry.LeftText).IsEqualTo("changed");
    }

    /// <summary>
    /// Settling is what replaces killing the window for a tool that had one per pair, and it names
    /// one entry: the pair beside it stays.
    /// </summary>
    [Test]
    public async Task SettlingAPairLeavesTheRest()
    {
        using var scope = new EngineScope();
        var first = Pair("First.Test");
        var second = Pair("Second.Test");
        await EngineRunner.LaunchAsync(Viewer(), first.Received, first.Target);
        await EngineRunner.LaunchAsync(Viewer(), second.Received, second.Target);

        var response = scope.Fixture.Send(new(ViewerVerb.Settle, TrackedKeys.ForMove(first.Received)));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(scope.Fixture.Host.State.Queue.Single().Key)
            .IsEqualTo(TrackedKeys.ForMove(second.Received));
    }

    static EngineResolvedTool Viewer() =>
        new(
            EngineTool.DiffEngineViewer.ToString(),
            EngineTool.DiffEngineViewer,
            // Guarded as existing, and never started: an owner answers on the port every time.
            Environment.ProcessPath!,
            new(
                (temp, target) => $"\"{target}\" \"{temp}\"",
                (temp, target) => $"\"{temp}\" \"{target}\""),
            isMdi: false,
            autoRefresh: false,
            binaryExtensions: [],
            requiresTarget: true,
            supportsText: true,
            useShellExecute: false);

    (string Received, string Target) Pair(string name)
    {
        var received = Path.Combine(directory, $"{name}.received.txt");
        var target = Path.Combine(directory, $"{name}.verified.txt");
        File.WriteAllText(received, "received");
        File.WriteAllText(target, "verified");
        return (received, target);
    }

    readonly string directory = Path.Combine(Path.GetTempPath(), $"EngineDiffTests_{Guid.NewGuid():N}");

    public EngineDiffTests() =>
        Directory.CreateDirectory(directory);

    public void Dispose() =>
        Directory.Delete(directory, true);
}
