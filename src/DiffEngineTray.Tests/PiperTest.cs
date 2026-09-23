using System.Collections.Concurrent;
using System.Reflection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

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

    /// <summary>
    /// A connection that resets while it waits in the backlog - a test process cancelled
    /// mid send, arriving while the loop is between one accept and the next - surfaces from the
    /// accept as a bare SocketException. The loop's reset catch is written for an IOException
    /// wrapping one, which is what a read throws, so it never matches, and the reset is reported as
    /// "Failed to receive payload" with the open-an-issue box, on the accept loop's own thread.
    /// </summary>
    [Test]
    public async Task AClientThatResetsBeforeItIsAcceptedIsNotReportedAsAnError()
    {
        var previousLogger = Log.Logger;
        var events = new ConcurrentQueue<LogEvent>();
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Sink(new Capture(events))
            .CreateLogger();
        // ExceptionHandler follows the log with a modal "open an issue?" box, which here would wait
        // for a click forever. It asks once per message, so this one is marked as already asked.
        var asked = (ConcurrentBag<string>) typeof(IssueLauncher)
            .GetField("recorded", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        asked.Add("Failed to receive payload");

        using var cancel = new CancelSource();
        var held = new HeldContext();
        var second = new TcpClient();
        try
        {
            Task serving;
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(held);
            try
            {
                serving = PiperServer.Start(_ => { }, _ => { }, cancel.Token);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            // Accepting this one completes the loop's pending accept. The loop resuming is held, so
            // for now nothing is accepting: the gap between one accept and the next, held open
            using (var first = new TcpClient())
            {
                await first.ConnectAsync(IPAddress.Loopback, PiperClient.Port);
            }

            await Assert.That(await held.WaitForPending(TimeSpan.FromSeconds(5))).IsTrue();

            // Connects into the backlog during that gap, and resets there
            await second.ConnectAsync(IPAddress.Loopback, PiperClient.Port);
            second.Client.Close(0);
            await Task.Delay(200);

            // The loop goes on: it handles the first client, then accepts again
            held.RunFor(TimeSpan.FromSeconds(1));

            await cancel.CancelAsync();
            held.RunUntil(serving, TimeSpan.FromSeconds(5));
            await Assert.That(serving.IsCompleted).IsTrue();
        }
        finally
        {
            second.Dispose();
            Log.Logger = previousLogger;
        }

        var errors = events
            .Where(_ => _.Level >= LogEventLevel.Error)
            .Select(_ => $"{_.MessageTemplate.Text}: {_.Exception?.GetType().Name} {(_.Exception as SocketException)?.SocketErrorCode}")
            .ToList();
        await Assert.That(errors).IsEmpty();
    }


    /// <summary>
    /// The tray binds the port itself, before serving, so a port something else holds is reported
    /// at startup rather than faulting a task nobody looks at until exit.
    /// </summary>
    [Test]
    public async Task ABindThatFailsSaysWhy()
    {
        var holder = new TcpListener(IPAddress.Loopback, PiperClient.Port);
        holder.Start();
        try
        {
            var listener = PiperServer.TryBind(out var error);

            await Assert.That(listener).IsNull();
            await Assert.That(error!.SocketErrorCode).IsEqualTo(SocketError.AddressAlreadyInUse);
        }
        finally
        {
            holder.Stop();
        }

        var bound = PiperServer.TryBind(out var none);
        bound!.Stop();
        await Assert.That(none).IsNull();
    }

    /// <summary>
    /// Queues what is posted to it until told to run it, so a test decides when an awaiting loop
    /// resumes.
    /// </summary>
    sealed class HeldContext :
        SynchronizationContext
    {
        readonly BlockingCollection<(SendOrPostCallback callback, object? state)> queue = [];

        public override void Post(SendOrPostCallback d, object? state) =>
            queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) =>
            d(state);

        public async Task<bool> WaitForPending(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (queue.Count > 0)
                {
                    return true;
                }

                await Task.Delay(10);
            }

            return false;
        }

        public void RunFor(TimeSpan duration) =>
            Run(() => false, duration);

        public void RunUntil(Task task, TimeSpan timeout) =>
            Run(() => task.IsCompleted, timeout);

        void Run(Func<bool> done, TimeSpan duration)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                var deadline = DateTime.UtcNow + duration;
                while (!done())
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        return;
                    }

                    if (queue.TryTake(out var item, TimeSpan.FromMilliseconds(Math.Min(50, remaining.TotalMilliseconds))))
                    {
                        item.callback(item.state);
                    }
                }
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }

    sealed class Capture(ConcurrentQueue<LogEvent> events) :
        ILogEventSink
    {
        public void Emit(LogEvent logEvent) =>
            events.Enqueue(logEvent);
    }
}
