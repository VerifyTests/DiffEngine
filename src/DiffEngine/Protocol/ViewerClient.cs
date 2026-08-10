namespace DiffEngine;

/// <summary>
/// Talks to whoever owns the inline queue. A refused connection means nobody does, which the
/// caller turns into a launch (DiffEngine), "nothing pending" (the tray), or "the owner has gone"
/// (an attached viewer).
/// </summary>
static class ViewerClient
{
    public const int DefaultPort = 3493;

    /// <summary>
    /// The tray's piper sits on 3492. Tests override this so a run never talks to a live viewer,
    /// mirroring how PiperTest reassigns PiperClient.Port.
    /// </summary>
    public const string PortVariable = "DiffEngine_ViewerPort";

    /// <summary>
    /// Read from the environment on every call rather than cached, so a test that sets the
    /// variable does not depend on having done so before the first send.
    /// </summary>
    public static int Port
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(PortVariable);
            if (int.TryParse(value, out var port) &&
                port is > 0 and < 65536)
            {
                return port;
            }

            return DefaultPort;
        }
    }

    static readonly TimeSpan timeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// For callers on a clock or an interactive path, such as the tray's scan timer and its menu.
    /// The exchange is loopback to a local process, so anything slower than this is a wedged owner
    /// rather than a slow one, and waiting the full timeout would let timer callbacks outlast
    /// their own period and pile up.
    /// </summary>
    public static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// True when the owner acknowledged. A refused connection means nobody owns the queue.
    /// </summary>
    public static bool TrySend(ViewerMessage message) =>
        TrySend(message, out var response) &&
        response.Ok;

    /// <summary>
    /// True when a reply arrived and parsed, whatever it says. Callers that need the body, such as
    /// the tray listing pending snapshots, use this rather than <see cref="TrySend(ViewerMessage)"/>.
    /// <para>
    /// <paramref name="port"/> overrides <see cref="Port"/> for a single call. Tests pass their own
    /// ephemeral port rather than mutating anything static, so they can run in parallel.
    /// </para>
    /// </summary>
    public static bool TrySend(
        ViewerMessage message,
        [NotNullWhen(true)] out ViewerResponse? response,
        int? port = null,
        TimeSpan? wait = null)
    {
        response = null;
        var deadline = wait ?? timeout;
        try
        {
            using var client = new TcpClient();
            if (!client.ConnectAsync(IPAddress.Loopback, port ?? Port).Wait(deadline))
            {
                return false;
            }

            Configure(client, deadline);
            var stream = client.GetStream();
            var bytes = Encoding.UTF8.GetBytes(message.Build());
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            HalfClose(client);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return ViewerResponse.TryParse(reader.ReadToEnd(), out response);
        }
        catch (Exception exception)
            when (Ignorable(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Fully async, including the read. A blocking read here would tie up a thread pool thread for
    /// the whole exchange, and a parallel test run calling this once per failing snapshot would
    /// starve the pool on a small machine.
    /// </summary>
    public static async Task<bool> TrySendAsync(ViewerMessage message, Cancel cancel)
    {
        try
        {
            using var client = new TcpClient();
#if NET6_0_OR_GREATER
            await client.ConnectAsync(IPAddress.Loopback, Port, cancel);
#else
            cancel.ThrowIfCancellationRequested();
            using (cancel.Register(client.Close))
            {
                await client.ConnectAsync(IPAddress.Loopback, Port);
            }
#endif
            Configure(client, timeout);
            var stream = client.GetStream();
            var bytes = Encoding.UTF8.GetBytes(message.Build());
#if NET6_0_OR_GREATER
            await stream.WriteAsync(bytes, cancel);
#else
            await stream.WriteAsync(bytes, 0, bytes.Length, cancel);
#endif
            await stream.FlushAsync(cancel);
            HalfClose(client);
            using var reader = new StreamReader(stream, Encoding.UTF8);
#if NET7_0_OR_GREATER
            var text = await reader.ReadToEndAsync(cancel);
#else
            var text = await reader.ReadToEndAsync();
#endif
            return ViewerResponse.TryParse(text, out var response) &&
                   response.Ok;
        }
        // Cancellation is the caller's business; a missing owner is not.
        catch (Exception exception)
            when (exception is not OperationCanceledException && Ignorable(exception))
        {
            return false;
        }
    }

    static void Configure(TcpClient client, TimeSpan wait)
    {
        client.SendTimeout = (int) wait.TotalMilliseconds;
        client.ReceiveTimeout = (int) wait.TotalMilliseconds;
    }

    /// <summary>
    /// Signals the end of the request without losing the socket the owner replies on.
    /// </summary>
    static void HalfClose(TcpClient client) =>
        client.Client.Shutdown(SocketShutdown.Send);

    static bool Ignorable(Exception exception) =>
        exception is
            SocketException or
            IOException or
            ObjectDisposedException or
            AggregateException
            {
                InnerException: SocketException or IOException
            };
}
