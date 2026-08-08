using System.Net;
using System.Net.Sockets;

/// <summary>
/// Talks to an already running viewer. A refused connection means no viewer owns the port, which
/// the caller treats as "nothing pending" (tray) or "spawn one" (DiffEngine).
/// </summary>
static class ViewerClient
{
    public static int Port { get; set; } = ViewerPort.Resolve();

    /// <summary>
    /// Short by design. Both callers are on an interactive path, so a wedged viewer must not
    /// stall a tray menu or a test run.
    /// </summary>
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// <paramref name="port"/> overrides <see cref="Port"/> for a single call. Tests pass their
    /// own ephemeral port rather than mutating the static, so they can run in parallel.
    /// </summary>
    public static bool TrySend(
        ViewerMessage message,
        [NotNullWhen(true)] out ViewerResponse? response,
        int? port = null)
    {
        response = null;
        try
        {
            var text = Exchange(message.Build(), port ?? Port);
            return ViewerResponse.TryParse(text, out response);
        }
        catch (SocketException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    static string Exchange(string payload, int port)
    {
        using var client = new TcpClient();
        client.SendTimeout = (int) Timeout.TotalMilliseconds;
        client.ReceiveTimeout = (int) Timeout.TotalMilliseconds;
        Connect(client, port);

        using var stream = client.GetStream();
        var bytes = Encoding.UTF8.GetBytes(payload);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();

        // Half close so the server sees end of request without losing the socket it must reply
        // on. Reading to end is then unambiguous on both sides.
        client.Client.Shutdown(SocketShutdown.Send);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    static void Connect(TcpClient client, int port)
    {
        try
        {
            if (!client.ConnectAsync(IPAddress.Loopback, port).Wait(Timeout))
            {
                throw new SocketException((int) SocketError.TimedOut);
            }
        }
        // Task.Wait wraps the refusal, and every caller matches on SocketException.
        catch (AggregateException exception)
            when (exception.InnerException is SocketException socket)
        {
            throw socket;
        }
    }
}
