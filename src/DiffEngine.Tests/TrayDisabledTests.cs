// DiffEngineTray is the obsolete public shim, but its IsRunning is still where the tray check
// lives, and this test has to move it.
#pragma warning disable CS0618

/// <summary>
/// <see cref="DiffRunner.TrayDisabled" />: a process that wants a diff tool launched but does not
/// want the tray collecting what it produces.
/// <para>
/// The case it exists for is a test suite driving a library that stages snapshots. Turning diff off
/// is not the same switch: in Verify it also turns off the inline staging such a suite exists to
/// test, and it does not stop the tracking anyway, since every exit of
/// <c>DiffRunner.InnerLaunch</c> - <c>Disabled</c> included - still adds the move. So a developer
/// box collected a pending move per snapshot per run, each pointing at a throwaway directory, and
/// each offering an accept that would write to it.
/// </para>
/// </summary>
[NotInParallel]
public class TrayDisabledTests
{
    const string Variable = "DiffEngine_TrayDisabled";

    [Test]
    public async Task Read_from_the_environment_until_set()
    {
        DiffRunner.ResetTrayDisabled();

        Environment.SetEnvironmentVariable(Variable, "true");
        await Assert.That(DiffRunner.TrayDisabled).IsTrue();

        // Setting pins it, exactly as Disabled does, so a consumer that opts back in is not
        // overruled by the machine it runs on.
        DiffRunner.TrayDisabled = false;
        await Assert.That(DiffRunner.TrayDisabled).IsFalse();
    }

    [Test]
    public async Task A_disabled_tray_leaves_the_move_to_the_queue_owner()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();

        var heardByOwner = new ConcurrentBag<string>();
        var listening = server.Listen(
            _ =>
            {
                heardByOwner.Add($"{_.Verb}:{_.Key}");
                return ViewerResponse.Success();
            },
            cancel.Token);

        using var tray = new PiperListener();

        var previousPort = PiperClient.Port;
        var previousViewerPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
        var previousRunning = DiffEngineTray.IsRunning;
        try
        {
            // A tray that is running and really would take it, so what follows is the switch
            // rather than an absent tray.
            PiperClient.Port = tray.Port;
            DiffEngineTray.IsRunning = true;
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, server.Port.ToString());

            DiffRunner.TrayDisabled = false;
            await PendingFiles.AddMoveAsync("taken.txt", "target.txt", null, null, false, null, cancel.Token);

            await tray.WaitFor(1);
            await Assert.That(heardByOwner.Count).IsEqualTo(0);

            DiffRunner.TrayDisabled = true;
            await PendingFiles.AddMoveAsync("skipped.txt", "target.txt", null, null, false, null, cancel.Token);

            // The owner took the second one, which is the fallback branch for no tray at all.
            await Assert.That(heardByOwner.Count).IsEqualTo(1);
            await Assert.That(heardByOwner).Contains(_ => _.StartsWith("Move:", StringComparison.Ordinal));

            // And the tray still holds only the first. Asserted after the owner heard the second,
            // because the piper decision is made before that send, so by here it has happened.
            await Assert.That(tray.Payloads.Count).IsEqualTo(1);
            await Assert.That(tray.Payloads).Contains(_ => _.Contains("taken.txt", StringComparison.Ordinal));
        }
        finally
        {
            PiperClient.Port = previousPort;
            DiffEngineTray.IsRunning = previousRunning;
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousViewerPort);
            await cancel.CancelAsync();
            try
            {
                // No token: the line above already cancelled it, so passing it here would return
                // before the listener had unwound rather than waiting for it to. The timeout is
                // what bounds the drain
                // ReSharper disable once MethodSupportsCancellation
                await listening.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or TimeoutException)
            {
            }
        }
    }

    /// <summary>
    /// Every other test in this assembly runs with the ambient value, which the module initializer
    /// leaves alone.
    /// </summary>
    [After(Test)]
    public void Restore()
    {
        Environment.SetEnvironmentVariable(Variable, null);
        DiffRunner.ResetTrayDisabled();
    }

    /// <summary>
    /// Stands in for the tray's PiperServer: the payload is written one way and never answered, so
    /// accepting the connection and reading it to the end is the whole protocol from this side.
    /// </summary>
    sealed class PiperListener : IDisposable
    {
        readonly TcpListener listener;
        readonly CancelSource cancellation = new();
        readonly Task loop;

        public PiperListener()
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint) listener.LocalEndpoint).Port;
            loop = Task.Run(Accept);
        }

        public int Port { get; }

        public ConcurrentBag<string> Payloads { get; } = [];

        public async Task WaitFor(int count)
        {
            for (var attempt = 0; attempt < 250; attempt++)
            {
                if (Payloads.Count >= count)
                {
                    return;
                }

                await Task.Delay(20);
            }

            throw new($"Only {Payloads.Count} payloads reached the tray, expected {count}.");
        }

        async Task Accept()
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    // No token: the cancellable overload is net6 and up, and this compiles for
                    // net48 too. Stop in Dispose is what breaks the accept, which lands in the
                    // catch below.
                    using var client = await listener.AcceptTcpClientAsync();
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream);
                    Payloads.Add(await reader.ReadToEndAsync());
                }
                catch (Exception exception)
                    when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }
            }
        }

        public void Dispose()
        {
            cancellation.Cancel();
            listener.Stop();
            try
            {
                loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            cancellation.Dispose();
        }
    }
}
