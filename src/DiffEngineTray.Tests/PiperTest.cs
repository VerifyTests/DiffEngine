using System.Net;
using System.Net.Sockets;

public class PiperTest :
    IDisposable
{
    readonly List<string> Logs = [];
    readonly TraceListener listener;

    public PiperTest()
    {
        listener = new LogCapture(Logs);
        Trace.Listeners.Add(listener);
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
        source.Cancel();
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
        source.Cancel();
        await task;
        await Verify(received);
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
            await client.ConnectAsync(IPAddress.Loopback, PiperClient.Port);
            // Linger with timeout 0 causes a RST (forcible close) on Close
            client.LingerState = new(true, 0);
        }

        // Give the server time to process the abrupt disconnect
        await Task.Delay(500);

        // Server should still work after the abrupt disconnect
        await PiperClient.SendDeleteAsync("Foo", source.Token);
        await Task.Delay(1000, source.Token);
        source.Cancel();
        await task;

        // Verify the server recovered and processed the subsequent valid message
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

        await Verify(Logs)
            .ScrubLinesContaining("temp.txt")
            //TODO: add "scrub source dir" to verify and remove the below
            .ScrubLinesContaining("PiperClient");
    }

    class LogCapture(List<string> logs) : TraceListener
    {
        public override void Write(string? message) { }
        public override void WriteLine(string? message) => logs.Add(message ?? "");
    }
}
