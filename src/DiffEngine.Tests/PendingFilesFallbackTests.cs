// DiffEngineTray is the obsolete public shim, but its IsRunning is still where the tray check
// lives, and this test has to move it.
#pragma warning disable CS0618

/// <summary>
/// Where a pending file goes when the tray check is stale.
/// <para>
/// DiffEngineTray.IsRunning is read once, when the type initialises. A tray that exits while a
/// long lived host keeps running leaves that answer saying a tray is there, so the piper send went
/// to a port nobody was listening on - and, returning nothing, was swallowed into a trace line.
/// The move or delete was then pending in nothing at all: no fallback to the queue owner, no
/// LaunchDelete.
/// </para>
/// </summary>
[NotInParallel]
public class PendingFilesFallbackTests
{
    [Test]
    public async Task ADeadPiperFallsThroughToTheQueueOwner()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();

        var heard = new ConcurrentBag<string>();
        var listening = server.Listen(
            _ =>
            {
                heard.Add($"{_.Verb}:{_.Key}");
                return ViewerResponse.Success();
            },
            cancel.Token);

        var previousPort = PiperClient.Port;
        var previousViewerPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
        var previousRunning = DiffEngineTray.IsRunning;
        try
        {
            // A tray that says it is running, on a port nothing is listening on
            PiperClient.Port = DeadPort();
            DiffEngineTray.IsRunning = true;
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, server.Port.ToString());

            await PendingFiles.AddMoveAsync("temp.txt", "target.txt", null, null, false, null, cancel.Token);
            await PendingFilesAddDelete("gone.txt");

            await Assert.That(heard).Contains(_ => _.StartsWith("Move:", StringComparison.Ordinal));
            await Assert.That(heard).Contains(_ => _.StartsWith("Delete:", StringComparison.Ordinal));
        }
        finally
        {
            PiperClient.Port = previousPort;
            DiffEngineTray.IsRunning = previousRunning;
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousViewerPort);
            await cancel.CancelAsync();
            try
            {
                await listening.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or TimeoutException)
            {
            }
        }
    }

    // The delete path launches a viewer when nothing answers, so it is only safe to exercise with
    // an owner bound - which is the point of the test.
    static Task PendingFilesAddDelete(string file)
    {
        PendingFiles.AddDelete(file);
        return Task.CompletedTask;
    }

    static int DeadPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
