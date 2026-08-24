extern alias viewer;

using ViewerCommandLine = viewer::CommandLine;
using ViewerMode = viewer::ViewerMode;

#pragma warning disable CS0618 // DiffEngineTray is obsolete; the test drives it directly to enable the send path.

/// <summary>
/// What a tray is told about a pair whose diff tool is the viewer itself.
/// <para>
/// The tray stores the exe and the arguments against the tracked move and re-runs them for "Open
/// diff tool", so they have to name the queue. The plain two path form would open a window of its
/// own beside the one the pair is already in, which is the arrangement this whole route exists to
/// remove.
/// </para>
/// <para>
/// Parsed back through Windows' own splitter and then the viewer's own command line, rather than
/// compared against a string, because those two are what the arguments actually have to survive -
/// and a received file staged under a path with a space in it is the case where surviving them is
/// not free. The tray is Windows only, so CommandLineToArgvW is the splitter that will be used.
/// </para>
/// <para>
/// The move must also not be killable: there is no window of its own to kill, and the one it is
/// drawn in is holding every other pending pair.
/// </para>
/// </summary>
public class DiffRunnerViewerMoveTest :
    IDisposable
{
    [Test]
    public async Task A_sync_launch_tells_the_tray_how_to_reopen_the_queue() =>
        await AssertReopens(await CaptureMove(() => Task.FromResult(DiffRunner.Launch(Viewer(), temp, target))));

    [Test]
    public async Task An_async_launch_tells_the_tray_how_to_reopen_the_queue() =>
        await AssertReopens(await CaptureMove(() => DiffRunner.LaunchAsync(Viewer(), temp, target)));

    async Task AssertReopens(MovePayload received)
    {
        await Assert.That(received.Temp).IsEqualTo(temp);
        await Assert.That(received.Target).IsEqualTo(target);
        await Assert.That(received.Exe).IsEqualTo(Environment.ProcessPath);
        await Assert.That(received.CanKill).IsFalse();
        await Assert.That(received.ProcessId).IsNull();

        var request = ViewerCommandLine.Parse(Split(received.Arguments!));
        await Assert.That(request.Error).IsNull();
        await Assert.That(request.Diff).IsTrue();
        // Queue mode, which is the whole point of the relaunch naming --diff.
        await Assert.That(request.Mode).IsEqualTo(ViewerMode.Inline);
        await Assert.That(request.Left).IsEqualTo(temp);
        await Assert.That(request.Right).IsEqualTo(target);
    }

    static async Task<MovePayload> CaptureMove(Func<Task<LaunchResult>> launch)
    {
        MovePayload? received = null;
        var source = new CancelSource();
        var server = PiperServer.Start(move => received = move, _ => { }, source.Token);
        try
        {
            var result = await launch();
            // The tray took it, so nothing was launched and no window was opened for the pair.
            await Assert.That(result).IsEqualTo(LaunchResult.AlreadyRunningAndSupportsRefresh);

            for (var i = 0; received == null && i < 50; i++)
            {
                await Task.Delay(100, source.Token);
            }
        }
        finally
        {
            await source.CancelAsync();
            await server;
        }

        await Assert.That(received).IsNotNull();
        return received!;
    }

    static ResolvedTool Viewer() =>
        new(
            name: DiffTool.DiffEngineViewer.ToString(),
            tool: DiffTool.DiffEngineViewer,
            // Guarded as existing, and never started: the tray takes the move, so nothing here
            // reaches a launch.
            exePath: Environment.ProcessPath!,
            launchArguments: new(
                Left: (t, target) => $"\"{target}\" \"{t}\"",
                Right: (t, target) => $"\"{t}\" \"{target}\""),
            isMdi: false,
            autoRefresh: false,
            binaryExtensions: [],
            requiresTarget: false,
            supportsText: true,
            useShellExecute: false);

    /// <summary>
    /// The splitter the OS will use on this string, so the quoting is asserted as Windows reads it
    /// rather than as this test would like to read it.
    /// </summary>
    static string[] Split(string arguments)
    {
        var pointer = CommandLineToArgvW("exe " + arguments, out var count);
        if (pointer == IntPtr.Zero)
        {
            throw new("Could not split the arguments.");
        }

        try
        {
            var split = new string[count];
            for (var index = 0; index < count; index++)
            {
                split[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size))!;
            }

            // The exe stands in for argv[0], which the process would consume.
            return split[1..];
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr handle);

    static int GetFreePort()
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

    // A space in the path, because that is where the quoting the tray stores has to earn itself.
    readonly string directory = Path.Combine(Path.GetTempPath(), $"Viewer Move {Guid.NewGuid():N}");
    readonly string temp;
    readonly string target;
    readonly bool originalDisabled = DiffRunner.Disabled;
    readonly string? originalViewerPort = Environment.GetEnvironmentVariable("DiffEngine_ViewerPort");

    public DiffRunnerViewerMoveTest()
    {
        Directory.CreateDirectory(directory);
        temp = Path.Combine(directory, "Sample.Test.received.txt");
        target = Path.Combine(directory, "Sample.Test.verified.txt");
        File.WriteAllText(temp, "received");
        PiperClient.Port = GetFreePort();
        // The route sends a focus to whoever owns the queue after the tray has taken the move.
        // Pointed at a free port so a live viewer on the machine running these is not raised.
        Environment.SetEnvironmentVariable("DiffEngine_ViewerPort", GetFreePort().ToString());
        DiffEngine.DiffEngineTray.IsRunning = true;
        DiffRunner.Disabled = false;
    }

    public void Dispose()
    {
        DiffEngine.DiffEngineTray.IsRunning = false;
        DiffRunner.Disabled = originalDisabled;
        Environment.SetEnvironmentVariable("DiffEngine_ViewerPort", originalViewerPort);
        Directory.Delete(directory, true);
    }
}
