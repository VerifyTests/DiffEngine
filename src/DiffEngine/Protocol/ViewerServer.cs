namespace DiffEngine;

/// <summary>
/// The single instance gate and the queue's inbox.
/// <para>
/// Ownership is decided by the bind, not a named mutex: whoever binds the port owns the queue, and
/// a process that fails to bind talks to the owner instead. That is race free without any extra
/// coordination, and it sidesteps the mac named mutex IOException already documented in
/// <see cref="TrayDetector"/>.
/// </para>
/// <para>
/// Lives here rather than in the viewer because either process can be the owner, and a second
/// implementation on the tray side is exactly the drift this protocol move exists to remove.
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
        // processes race for the same queue.
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
            // Already in use, so someone else owns the queue.
            return false;
        }

        // Port 0 asks the OS to choose, which the tests use to avoid colliding with a live owner.
        server = new(listener, ((IPEndPoint) listener.LocalEndpoint).Port);
        return true;
    }

    public async Task Listen(Func<ViewerMessage, ViewerResponse> handle, Cancel cancel = default)
    {
        // Sync dispose: CancellationTokenRegistration is only IAsyncDisposable from net6, and
        // waiting for an in flight Stop callback buys nothing here.
        // ReSharper disable once UseAwaitUsing
        using var registration = cancel.Register(listener.Stop);
        while (!cancel.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await Accept(cancel);
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
            catch (InvalidOperationException)
            {
                // Same, on the frameworks where a stopped listener reports it this way.
                return;
            }
            catch (SocketException)
            {
                return;
            }

            // Each connection on its own task, so one slow exchange does not stop the next from
            // being answered. Accepting an inline snapshot legitimately takes seconds, and a
            // client whose listing goes unanswered for that long concludes the owner has died.
            _ = Task.Run(() => Handle(client, handle, cancel), Cancel.None);
        }
    }

    // ReSharper disable once ReplaceAsyncWithTaskReturn
    async Task<TcpClient> Accept(Cancel cancel)
    {
#if NET6_0_OR_GREATER
        return await listener.AcceptTcpClientAsync(cancel);
#else
        // No token overload here, so cancellation arrives as the registered Stop, which faults
        // this await with one of the exceptions the caller already treats as "stop serving".
        cancel.ThrowIfCancellationRequested();
        return await listener.AcceptTcpClientAsync();
#endif
    }

    static async Task Handle(TcpClient client, Func<ViewerMessage, ViewerResponse> handle, Cancel cancel)
    {
        try
        {
            using (client)
            {
                // ReSharper disable once UseAwaitUsing
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
#if NET7_0_OR_GREATER
                var text = await reader.ReadToEndAsync(cancel);
#else
                var text = await reader.ReadToEndAsync();
#endif

                var response = Respond(handle, text);
                var bytes = Encoding.UTF8.GetBytes(response.Build());
#if NET6_0_OR_GREATER
                await stream.WriteAsync(bytes, cancel);
#else
                await stream.WriteAsync(bytes, 0, bytes.Length, cancel);
#endif
                await stream.FlushAsync(cancel);
            }
        }
        catch (Exception exception)
            when (exception is
                IOException or
                SocketException or
                ObjectDisposedException or
                OperationCanceledException)
        {
            // A client that vanished mid exchange, or shutdown arriving midway. Nothing left to
            // answer, and nothing the owner of the queue needs to hear about.
        }
    }

    static ViewerResponse Respond(Func<ViewerMessage, ViewerResponse> handle, string text)
    {
        if (!ViewerMessage.TryParse(text, out var message))
        {
            return ViewerResponse.Error("Unreadable request");
        }

        try
        {
            return handle(message);
        }
        catch (Exception exception)
        {
            // Answered rather than thrown: the connection runs on an untracked task, so a throwing
            // handler would otherwise vanish silently and leave the client waiting out its timeout.
            return ViewerResponse.Error(exception.Message);
        }
    }

    public void Dispose() =>
        listener.Stop();
}
