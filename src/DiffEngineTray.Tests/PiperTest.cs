public class PiperTest :
    IDisposable
{
    readonly List<string> logs = [];
    readonly TraceListener listener;

    public PiperTest()
    {
        // Use a free ephemeral port rather than the hardcoded default (3492), so these tests
        // pass even when a real DiffEngineTray instance is running and holding that port.
        PiperClient.Port = GetFreePort();
        listener = new LogCapture(logs);
        Trace.Listeners.Add(listener);
    }

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
        Trace.Listeners.Remove(listener);
        listener.Dispose();
    }

    [Test]
    public Task MoveJson() =>
        Verify(
            PiperClient.BuildMovePayload(
                "theTempFilePath",
                "theTargetFilePath",
                "theExePath",
                "TheArguments",
                true,
                1000));

    [Test]
    public Task DeleteJson() =>
        Verify(
            PiperClient.BuildMovePayload(
                "theTempFilePath",
                "theTargetFilePath",
                "theExePath",
                "TheArguments",
                true,
                1000));

    [Test]
    public async Task Delete()
    {
        DeletePayload received = null!;
        var source = new CancelSource();
        var task = PiperServer.Start(_ => { }, s => received = s, source.Token);
        await PiperClient.SendDeleteAsync("Foo", source.Token);
        await Task.Delay(1000, source.Token);
        await source.CancelAsync();
        await task;
        await Verify(received);
    }

    [Test]
    public async Task Move()
    {
        MovePayload received = null!;
        var source = new CancelSource();
        var task = PiperServer.Start(s => received = s, _ => { }, source.Token);
        await PiperClient.SendMoveAsync("Foo", "Bar", "theExe", "TheArguments \"s\"", true, 10, source.Token);
        await Task.Delay(1000, source.Token);
        await source.CancelAsync();
        await task;
        await Verify(received);
    }

    [Test]
    public async Task SendMoveAsyncHonorsCancellation()
    {
        using var source = new CancelSource();
        await source.CancelAsync();

        var cancelled = false;
        try
        {
            await PiperClient.SendMoveAsync("Foo", "Bar", "theExe", "TheArguments", true, 10, source.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        await Assert.That(cancelled).IsTrue();
    }

    [Test]
    public async Task ClientDisconnectsAbruptly()
    {
        DeletePayload? received = null;
        var source = new CancelSource();
        var task = PiperServer.Start(_ => { }, s => received = s, source.Token);

        // Connect and immediately close with RST (no data sent),
        // simulating a client that was canceled mid-connection.
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, PiperClient.Port, source.Token);
            // Linger with timeout 0 causes a RST (forcible close) on Close
            client.LingerState = new(true, 0);
        }

        // Give the server time to process the abrupt disconnect
        await Task.Delay(500, source.Token);

        // Server should still work after the abrupt disconnect
        await PiperClient.SendDeleteAsync("Foo", source.Token);
        await Task.Delay(1000, source.Token);
        await source.CancelAsync();
        await task;

        // Verify the server recovered and processed the subsequent valid message
        await Assert.That(received).IsNotNull();
        await Assert.That(received!.File).IsEqualTo("Foo");
    }

    /// <summary>
    /// A client that connects and never finishes sending — a test process wedged mid write — used
    /// to hold the one accept loop for as long as it stayed that way, so every move and delete
    /// from every other process on the machine went nowhere and nothing timed the wait out.
    /// </summary>
    [Test]
    public async Task AClientThatStopsSendingDoesNotBlockTheNextOne()
    {
        DeletePayload? received = null;
        using var source = new CancelSource();
        var task = PiperServer.Start(_ => { }, _ => received = _, source.Token);

        // Connected, wrote nothing, and holds the stream open for the rest of the test
        using var wedged = new TcpClient();
        await wedged.ConnectAsync(IPAddress.Loopback, PiperClient.Port, source.Token);
        await using var held = wedged.GetStream();

        await PiperClient.SendDeleteAsync("Foo", source.Token);
        await Task.Delay(1000, source.Token);
        await source.CancelAsync();
        await task;

        await Assert.That(received).IsNotNull();
        await Assert.That(received!.File).IsEqualTo("Foo");
    }

    [Test]
    public async Task SendOnly()
    {
        var file = Path.GetFullPath("temp.txt");
        File.Delete(file);
        await File.WriteAllTextAsync(file, "a");
        try
        {
            await PiperClient.SendMoveAsync(file, file, "theExe", "TheArguments \"s\"", true, 10);
            await PiperClient.SendDeleteAsync(file);
        }
        catch (InvalidOperationException)
        {
        }

        await Verify(logs)
            .ScrubLinesContaining("temp.txt")
            //TODO: add "scrub source dir" to verify and remove the below
            .ScrubLinesContaining("PiperClient");
    }

    [Test]
    public async Task UnknownTypeIgnored()
    {
        DeletePayload? received = null;
        var source = new CancelSource();
        var task = PiperServer.Start(_ => { }, s => received = s, source.Token);

        // A payload type from a future client version must not throw
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, PiperClient.Port, source.Token);
            await using var stream = client.GetStream();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync("{\"Type\":\"Nonsense\"}");
        }

        await Task.Delay(500, source.Token);

        // Server should still process a subsequent valid message
        await PiperClient.SendDeleteAsync("Foo", source.Token);
        await Task.Delay(1000, source.Token);
        await source.CancelAsync();
        await task;

        await Assert.That(received).IsNotNull();
        await Assert.That(received!.File).IsEqualTo("Foo");
    }

    class LogCapture(List<string> logs) : TraceListener
    {
        public override void Write(string? message) { }
        public override void WriteLine(string? message) => logs.Add(message ?? "");
    }
}
