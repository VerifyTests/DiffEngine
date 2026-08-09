namespace DiffEngine;

/// <summary>
/// Sends to an already running viewer. A refused connection means no viewer owns the port, which
/// the caller turns into a launch.
/// </summary>
static class ViewerClient
{
    public const int DefaultPort = 3493;
    public const string PortVariable = "DiffEngine_ViewerPort";

    public static int Port
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(PortVariable);
            if (int.TryParse(value, out var port) &&
                port > 0 &&
                port < 65536)
            {
                return port;
            }

            return DefaultPort;
        }
    }

    static readonly TimeSpan timeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// True when the viewer acknowledged. A refused connection means no viewer is running.
    /// </summary>
    public static bool TrySend(string payload) =>
        TryExchange(payload, out var response) &&
        response.Contains("status: ok");

    /// <summary>
    /// True when a reply arrived, whatever it says. Callers that need the body, such as the
    /// tray listing pending snapshots, use this rather than <see cref="TrySend"/>.
    /// </summary>
    public static bool TryExchange(string payload, out string response)
    {
        response = "";
        try
        {
            using var client = new TcpClient();
            if (!client.ConnectAsync(IPAddress.Loopback, Port).Wait(timeout))
            {
                return false;
            }

            response = Write(client, payload);
            return true;
        }
        catch (Exception exception)
            when (Ignorable(exception))
        {
            return false;
        }
    }

    public static async Task<bool> TrySendAsync(string payload, Cancel cancel)
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
            return Write(client, payload).Contains("status: ok");
        }
        // Cancellation is the caller's business; a missing viewer is not.
        catch (Exception exception)
            when (exception is not OperationCanceledException && Ignorable(exception))
        {
            return false;
        }
    }

    static string Write(TcpClient client, string payload)
    {
        client.SendTimeout = (int) timeout.TotalMilliseconds;
        client.ReceiveTimeout = (int) timeout.TotalMilliseconds;
        var stream = client.GetStream();
        var bytes = Encoding.UTF8.GetBytes(payload);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();

        // Half close so the viewer sees the end of the request without losing the socket it
        // replies on. The reply is only an acknowledgement, so it is read and dropped.
        client.Client.Shutdown(SocketShutdown.Send);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    static bool Ignorable(Exception exception) =>
        exception is SocketException or IOException or ObjectDisposedException ||
        exception is AggregateException { InnerException: SocketException or IOException };
}
