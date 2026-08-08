using System.Net;
using System.Net.Sockets;

/// <summary>
/// The single instance gate and the queue's inbox.
/// <para>
/// Ownership is decided by the bind, not a named mutex: whoever binds the port owns the window,
/// and a process that fails to bind forwards its patch to the owner and exits. That is race free
/// without any extra coordination, and it sidesteps the mac named mutex IOException already
/// documented in DiffEngine's TrayDetector.
/// </para>
/// </summary>
sealed class ViewerServer : IDisposable
{
    readonly TcpListener listener;

    ViewerServer(TcpListener listener, int port)
    {
        this.listener = listener;
        Port = port;
    }

    public int Port { get; }

    public static bool TryBind(int port, [NotNullWhen(true)] out ViewerServer? server)
    {
        server = null;
        // Without ExclusiveAddressUse a second bind can succeed on some platforms, and then two
        // windows race for the same queue.
        var listener = new TcpListener(IPAddress.Loopback, port)
        {
            ExclusiveAddressUse = true
        };
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            // Already in use, so another viewer owns the queue.
            return false;
        }

        // Port 0 asks the OS to choose, which the tests use to avoid colliding with a live viewer.
        server = new(listener, ((IPEndPoint) listener.LocalEndpoint).Port);
        return true;
    }

    public async Task Listen(Func<ViewerMessage, ViewerResponse> handle, Cancel cancel = default)
    {
        await using var registration = cancel.Register(listener.Stop);
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(cancel);
                await Handle(client, handle, cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                // The listener was stopped by cancellation.
                return;
            }
            catch (SocketException)
            {
                return;
            }
            catch (IOException)
            {
                // A client disconnected part way through. Keep serving.
            }
        }
    }

    static async Task Handle(TcpClient client, Func<ViewerMessage, ViewerResponse> handle, Cancel cancel)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(cancel);

        var response = ViewerMessage.TryParse(text, out var message)
            ? handle(message)
            : ViewerResponse.Error("Unreadable request");

        var bytes = Encoding.UTF8.GetBytes(response.Build());
        await stream.WriteAsync(bytes, cancel);
        await stream.FlushAsync(cancel);
    }

    public void Dispose() =>
        listener.Stop();
}
