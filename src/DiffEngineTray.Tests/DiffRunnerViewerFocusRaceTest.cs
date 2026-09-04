#pragma warning disable CS0618 // DiffEngineTray is obsolete; the test drives it directly to enable the send path.

/// <summary>
/// A failing pair whose diff tool is the viewer, sent while the tray has taken its move but not yet
/// tracked it.
/// <para>
/// The route sends two messages on two connections: the move to the piper port, then a focus to
/// whoever owns the queue, naming the key that move was tracked under. Nothing orders them. The
/// piper send is fire and forget - it reports that the bytes went out, not that the tray read them
/// - and the tray takes that connection on a task of its own, reads it to the end, and walks up
/// from the target looking for a solution before the entry is visible to anything asking. The focus
/// is already in flight through all of it.
/// </para>
/// <para>
/// Focus refuses a key it cannot find and raises nothing, so losing that race left the pair in the
/// tray menu with no window ever opened - and a second run, where the key was still tracked from
/// the first, opening one. Which is the report this is written from: the diff tool does not show,
/// then on the next run it does.
/// </para>
/// <para>
/// The race is held open rather than raced for: a real <see cref="PiperServer" /> takes the move
/// and never gives it to the tracker, which is what the real one looks like for as long as it is
/// still reading. What has to happen anyway is the window, so the assertions are on the owner - a
/// real <see cref="OwnedInlineHost" /> over a real socket - rather than on which verb got it there.
/// </para>
/// </summary>
public class DiffRunnerViewerFocusRaceTest
{
    [Test]
    public async Task A_sync_launch_raises_a_window_for_a_move_the_tray_has_not_tracked() =>
        await AssertRaisesAWindow(_ => Task.FromResult(DiffRunner.Launch(Viewer(), _.Temp, _.Target)));

    [Test]
    public async Task An_async_launch_raises_a_window_for_a_move_the_tray_has_not_tracked() =>
        await AssertRaisesAWindow(_ => DiffRunner.LaunchAsync(Viewer(), _.Temp, _.Target));

    static async Task AssertRaisesAWindow(Func<Fixture, Task<LaunchResult>> launch)
    {
        await using var fixture = new Fixture();

        var result = await launch(fixture);

        // The pair has a surface, which is all the caller is ever told.
        await Assert.That(result).IsEqualTo(LaunchResult.AlreadyRunningAndSupportsRefresh);

        // The move did go to the tray. It is still in flight there, which is the whole premise:
        // the focus that followed it could not find the key, exactly as it cannot while the real
        // tray is still reading that connection.
        var move = await fixture.PiperMove();
        await Assert.That(move.Temp).IsEqualTo(fixture.Temp);
        await Assert.That(move.Target).IsEqualTo(fixture.Target);

        // So the pair went over again as a Diff, which tracks and raises in the one message.
        await Assert.That(fixture.Tracks(TrackedKeys.ForMove(fixture.Temp))).IsTrue();
        await Assert.That(fixture.Launches).IsEqualTo(1);
    }

    /// <summary>
    /// An owner that answers the focus for a key it holds still takes the early return, so the
    /// fall through is the refusal and not something every pair now pays for.
    /// </summary>
    [Test]
    public async Task A_move_the_tray_has_tracked_is_a_focus_and_nothing_more()
    {
        await using var fixture = new Fixture();
        fixture.Track();

        var result = DiffRunner.Launch(Viewer(), fixture.Temp, fixture.Target);

        await Assert.That(result).IsEqualTo(LaunchResult.AlreadyRunningAndSupportsRefresh);
        await Assert.That(fixture.Launches).IsEqualTo(1);
        // Focus raised the window over the entry that was already there, so nothing re-tracked it:
        // a Diff would have replaced the move, losing the exe and arguments the piper send carries.
        await Assert.That(fixture.TrackedExe()).IsEqualTo(Exe);
    }

    sealed class Fixture :
        IAsyncDisposable
    {
        readonly string directory = Path.Combine(Path.GetTempPath(), $"Viewer Focus {Guid.NewGuid():N}");
        readonly OwnedInlineHost host;
        readonly RecordingTracker tracker;
        readonly FakeLauncher launcher = new();
        readonly CancelSource piperCancel = new();
        readonly Task piper;
        readonly TaskCompletionSource<MovePayload> move = new();
        readonly bool originalDisabled = DiffRunner.Disabled;
        readonly int originalPiperPort = PiperClient.Port;
        readonly string? originalViewerPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);

        public Fixture()
        {
            Directory.CreateDirectory(directory);
            Temp = Path.Combine(directory, "Sample.Test.received.txt");
            Target = Path.Combine(directory, "Sample.Test.verified.txt");
            File.WriteAllText(Temp, "received");

            // A real tray listener that takes the move and stops there, which is what the real one
            // is doing for as long as it is reading the connection and walking for a solution.
            PiperClient.Port = FreePort();
            piper = PiperServer.Start(_ => move.TrySetResult(_), _ => { }, piperCancel.Token);

            host = OwnedInlineHost.TryOwn(_ => { }, launcher, 0) ??
                   throw new("Could not bind an ephemeral port.");
            // Before the tracker, which builds a RemoteInlineHost of its own and would otherwise
            // ask the port the module initializer left pointing at nothing.
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, host.Port.ToString());
            tracker = new();
            host.TrackedFiles = tracker;
            host.Start();

            DiffEngine.DiffEngineTray.IsRunning = true;
            DiffRunner.Disabled = false;
        }

        public string Temp { get; }

        public string Target { get; }

        public int Launches => launcher.Launches;

        /// <summary>
        /// The move landing before the focus does, which is the other side of the race and the one
        /// that always worked.
        /// </summary>
        public void Track() =>
            tracker.AddMove(Temp, Target, Exe, "--diff", false, null);

        public bool Tracks(string key) =>
            ((ITrackedFiles) tracker).Has(key);

        public string? TrackedExe() =>
            tracker.Moves.Single().Exe;

        public async Task<MovePayload> PiperMove() =>
            await move.Task.WaitAsync(TimeSpan.FromSeconds(10));

        static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            try
            {
                return ((IPEndPoint) probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        public async ValueTask DisposeAsync()
        {
            DiffEngine.DiffEngineTray.IsRunning = false;
            DiffRunner.Disabled = originalDisabled;
            PiperClient.Port = originalPiperPort;
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, originalViewerPort);

            await piperCancel.CancelAsync();
            await piper;
            piperCancel.Dispose();
            await host.DisposeAsync();
            await tracker.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Guarded as existing and never started: the pair is the viewer's, so nothing here reaches a
    /// process launch.
    /// </summary>
    static readonly string Exe = Environment.ProcessPath!;

    static ResolvedTool Viewer() =>
        new(
            name: DiffTool.DiffEngineViewer.ToString(),
            tool: DiffTool.DiffEngineViewer,
            exePath: Exe,
            launchArguments: new(
                Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                Right: (temp, target) => $"\"{temp}\" \"{target}\""),
            isMdi: false,
            autoRefresh: false,
            binaryExtensions: [],
            requiresTarget: false,
            supportsText: true,
            useShellExecute: false);
}
