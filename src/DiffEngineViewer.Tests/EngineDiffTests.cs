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
    /// And one that says what the last said, which is what a test that keeps failing the same way
    /// sends on every run, leaves the reader where they are. It used to open the entry again, at
    /// its first change, under whoever was half way down it.
    /// </summary>
    [Test]
    public async Task ARepeatOfAnUnchangedPairLeavesTheReaderWhereTheyAre()
    {
        using var scope = new EngineScope();
        var received = Path.Combine(directory, "Deep.Test.received.txt");
        var target = Path.Combine(directory, "Deep.Test.verified.txt");
        await File.WriteAllTextAsync(received, Fixtures.Deep(true));
        await File.WriteAllTextAsync(target, Fixtures.Deep(false));
        await EngineRunner.LaunchAsync(Viewer(), received, target);
        var host = scope.Fixture.Host;
        var opened = host.State.ScrollTop;
        var scrolled = host.Mutate(_ => ViewerSession.Apply(_, CommandKind.PageDown)).ScrollTop;
        await Assert.That(scrolled).IsNotEqualTo(opened);

        // The next run writes what the last one wrote
        await File.WriteAllTextAsync(received, Fixtures.Deep(true));
        await EngineRunner.LaunchAsync(Viewer(), received, target);

        await Assert.That(host.State.ScrollTop).IsEqualTo(scrolled);
        await Assert.That(host.State.Queue).HasSingleItem();
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

    /// <summary>
    /// The first run of a snapshot has no verified file, and the pair reaches the viewer as that.
    /// <para>
    /// The viewer was declared as needing a target, so EmptyFiles wrote a placeholder before the
    /// viewer heard of the pair, and the viewer compared against it as though it were the expected
    /// file: a blank page, or an empty PDF it could not open. An extension EmptyFiles has no file
    /// for stopped the launch there, which is every map but one, so nothing was raised over a new
    /// map snapshot at all.
    /// </para>
    /// </summary>
    [Test]
    [Arguments(".txt")]
    [Arguments(".png")]
    [Arguments(".pdf")]
    [Arguments(".geojson")]
    public async Task ANewSnapshotArrivesWithNoPlaceholderWritten(string extension)
    {
        using var scope = new EngineScope();
        var received = Path.Combine(directory, $"New.Test.received{extension}");
        var target = Path.Combine(directory, $"New.Test.verified{extension}");
        await File.WriteAllTextAsync(received, "received");

        var result = await EngineRunner.LaunchAsync(Viewer(), received, target);

        await Assert.That(result).IsEqualTo(EngineLaunchResult.AlreadyRunningAndSupportsRefresh);
        await Assert.That(File.Exists(target)).IsFalse();
        var entry = scope.Fixture.Host.State.Queue.Single();
        await Assert.That(entry.Key).IsEqualTo(TrackedKeys.ForMove(received));
        // Nothing on the expected side, rather than a file that happens to be empty
        await Assert.That(entry.RightStamp).IsNull();
        await Assert.That(scope.Fixture.Windows).IsEquivalentTo([WindowCommand.Focus]);
    }

    static EngineResolvedTool Viewer() =>
        new(
            nameof(EngineTool.DiffEngineViewer),
            EngineTool.DiffEngineViewer,
            // Guarded as existing, and never started: an owner answers on the port every time.
            Environment.ProcessPath!,
            new(
                (temp, target) => $"\"{target}\" \"{temp}\"",
                (temp, target) => $"\"{temp}\" \"{target}\""),
            isMdi: false,
            autoRefresh: false,
            binaryExtensions: [],
            // As the viewer that ships is declared, since whether a target is written first is
            // part of the route being covered
            requiresTarget: engine::DiffEngine.Definitions.Tools
                .Single(_ => _.Tool == EngineTool.DiffEngineViewer)
                .RequiresTarget,
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
