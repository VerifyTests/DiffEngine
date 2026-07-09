using System.Net;
using System.Net.Sockets;
using DiffEngine;

#pragma warning disable CS0618 // DiffEngineTray is obsolete; the test drives it directly to enable the send path.

// Regression test for the sync launch path sending the wrong `canKill` value.
// MDI tools host every diff in one shared window, so the tray must never kill them (CanKill=false);
// non-MDI tools get their own process, which the tray may kill (CanKill=true).
public class DiffRunnerCanKillTest :
    IDisposable
{
    string tempFile = Path.GetTempFileName();
    bool originalDisabled = DiffRunner.Disabled;
    string? originalMaxInstances = Environment.GetEnvironmentVariable("DiffEngine_MaxInstances");

    public DiffRunnerCanKillTest()
    {
        PiperClient.Port = GetFreePort();
        DiffEngine.DiffEngineTray.IsRunning = true;
        DiffRunner.Disabled = false;
        // Force the "too many running" branch so no real process is launched, while a move
        // payload is still sent to the tray. The env var takes precedence over the app-domain
        // value, so set it too; MaxInstancesToLaunch resets the cached lookup.
        Environment.SetEnvironmentVariable("DiffEngine_MaxInstances", "0");
        DiffRunner.MaxInstancesToLaunch(0);
    }

    [Test]
    public async Task Sync_launch_of_mdi_tool_marks_move_as_not_killable()
    {
        var received = await CaptureMove(() => Task.FromResult(DiffRunner.Launch(MdiTool(), tempFile, "target.txt")));

        await Assert.That(received.CanKill).IsFalse();
    }

    [Test]
    public async Task Async_launch_of_mdi_tool_marks_move_as_not_killable()
    {
        var received = await CaptureMove(() => DiffRunner.LaunchAsync(MdiTool(), tempFile, "target.txt"));

        await Assert.That(received.CanKill).IsFalse();
    }

    [Test]
    public async Task Sync_launch_of_non_mdi_tool_marks_move_as_killable()
    {
        var received = await CaptureMove(() => Task.FromResult(DiffRunner.Launch(NonMdiTool(), tempFile, "target.txt")));

        await Assert.That(received.CanKill).IsTrue();
    }

    static async Task<MovePayload> CaptureMove(Func<Task<LaunchResult>> launch)
    {
        MovePayload? received = null;
        var source = new CancelSource();
        var server = PiperServer.Start(move => received = move, _ => { }, source.Token);
        try
        {
            var result = await launch();
            await Assert.That(result).IsEqualTo(LaunchResult.TooManyRunningDiffTools);

            for (var i = 0; received == null && i < 50; i++)
            {
                await Task.Delay(100);
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

    static ResolvedTool MdiTool() => Tool(isMdi: true);

    static ResolvedTool NonMdiTool() => Tool(isMdi: false);

    static ResolvedTool Tool(bool isMdi) =>
        new(
            name: "FakeCanKillTool",
            exePath: Environment.ProcessPath!,
            launchArguments: new(
                Left: (temp, target) => $"\"{temp}\" \"{target}\"",
                Right: (temp, target) => $"\"{target}\" \"{temp}\""),
            isMdi: isMdi,
            autoRefresh: false,
            binaryExtensions: [],
            requiresTarget: false,
            supportsText: true,
            useShellExecute: false);

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

    public void Dispose()
    {
        DiffEngine.DiffEngineTray.IsRunning = false;
        DiffRunner.Disabled = originalDisabled;
        Environment.SetEnvironmentVariable("DiffEngine_MaxInstances", originalMaxInstances);
        DiffRunner.MaxInstancesToLaunch(5);
        File.Delete(tempFile);
    }
}
