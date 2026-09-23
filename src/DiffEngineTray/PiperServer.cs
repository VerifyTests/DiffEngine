/// <summary>
/// Receives moves and deletes from the DiffEngine library. One way by design: nothing is ever
/// written back, dispatch is by substring, and unknown payloads are ignored so a newer client
/// never surfaces an error dialog on an older tray.
/// <para>
/// Deliberately not the viewer protocol, and deliberately not sharing its port. This format is
/// frozen: every stable DiffEngine embeds <see cref="PiperClient"/>, pinned inside test projects
/// while this tray updates independently as a global tool, so old library plus new tray is the
/// normal pairing. And the two ports answer different questions — 3492 means "a tray is here",
/// 3493 means "the inline queue owner is here", and the owner is sometimes a viewer. Merged,
/// a late starting tray could not receive moves while a viewer owned the queue.
/// </para>
/// <para>
/// If this listener ever needs to answer anything, for example acknowledging a move, do it by
/// sniffing a versioned payload beside this format rather than replacing it: fire and forget
/// means a new library can never detect an old tray ignoring a new format, so this reader can
/// never be retired detectably.
/// </para>
/// </summary>
static class PiperServer
{
    /// <summary>
    /// Binds and serves, with a bind that fails faulting the task. For the tests; the tray binds
    /// with <see cref="TryBind"/> first, so it can say so.
    /// </summary>
    public static async Task Start(
        Action<MovePayload> move,
        Action<DeletePayload> delete,
        Cancel cancel = default)
    {
        var listener = new TcpListener(IPAddress.Loopback, PiperClient.Port);
        listener.Start();
        await Serve(listener, move, delete, cancel);
    }

    /// <summary>
    /// The port, taken now, or null with why not.
    /// <para>
    /// Synchronous, and before anything is served. A bind that failed inside the serving task
    /// faulted a task nothing looked at until the tray exited: it ran for the whole session with
    /// no listener, holding the mutex that keeps a second tray from starting, while every move and
    /// delete went to whatever did hold 3492 - and then crashed on the way out, logged as having
    /// failed at startup.
    /// </para>
    /// </summary>
    public static TcpListener? TryBind(out SocketException? error)
    {
        var listener = new TcpListener(IPAddress.Loopback, PiperClient.Port);
        try
        {
            listener.Start();
            error = null;
            return listener;
        }
        catch (SocketException exception)
        {
            listener.Stop();
            error = exception;
            return null;
        }
    }

    public static async Task Serve(
        TcpListener listener,
        Action<MovePayload> move,
        Action<DeletePayload> delete,
        Cancel cancel = default)
    {
        try
        {
            // Kept from when the accept lived inside the per-connection method: cancelling stops
            // the listener, which is what brings a pending accept down with it
            await using var registration = cancel.Register(listener.Stop);

            while (true)
            {
                if (cancel.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var client = await listener.AcceptTcpClientAsync(cancel);
                    // On its own task, the way ViewerServer takes its connections. Handled in
                    // turn, one client that connected and never closed its stream held up every
                    // move and delete from every other process for as long as it stayed that way,
                    // with nothing to end the wait. The callbacks are the tracker's concurrent
                    // collections, which the viewer port already writes to off this thread
                    //
                    // Task.Run rather than a bare call, which ran the handler on this loop until
                    // its first await that did not complete at once - for a payload already
                    // buffered, all of it, parse and tracker included - with no accept pending.
                    _ = Task.Run(() => Handle(client, move, delete, cancel), Cancel.None);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    //when task is cancelled socket is disposed
                    break;
                }
                catch (SocketException exception)
                    when (exception.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // A client that reset while it waited to be accepted - a test run cancelled
                    // mid send. The accept throws that bare, where a read throws it wrapped in an
                    // IOException, so the wrapped catch below never matched it here and it went to
                    // the "open an issue" box, modal, on this loop's thread.
                }
                catch (Exception exception)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        break;
                    }

                    ExceptionHandler.Handle("Failed to receive payload", exception);
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// How long one client gets to send its payload and close. A client is expected to write and
    /// go, so anything near this is one that has stopped rather than one that is slow, and the
    /// read has to end by itself: nothing else here will end it.
    /// </summary>
    static readonly TimeSpan readTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Nothing awaits this, so it reports rather than throws — an unobserved throw on the
    /// finaliser thread is not a way to hear about a dropped move.
    /// </summary>
    static async Task Handle(TcpClient client, Action<MovePayload> move, Action<DeletePayload> delete, Cancel cancel)
    {
        try
        {
            using (client)
            {
                using var reader = new StreamReader(client.GetStream());
                using var deadline = CancelSource.CreateLinkedTokenSource(cancel);
                deadline.CancelAfter(readTimeout);

                string payload;
                try
                {
                    payload = await reader.ReadToEndAsync(deadline.Token);
                }
                catch (OperationCanceledException)
                    when (!cancel.IsCancellationRequested)
                {
                    Log.Error("A client connected and did not finish sending within {timeout}. Ignoring it.", readTimeout);
                    return;
                }

                Dispatch(payload, move, delete);
            }
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or ObjectDisposedException)
        {
            // Shutting down, or the socket went with it
        }
        catch (IOException exception)
            when (exception.InnerException is SocketException {SocketErrorCode: SocketError.ConnectionReset})
        {
            //client disconnected abruptly, e.g. test was canceled
        }
        catch (Exception exception)
        {
            if (!cancel.IsCancellationRequested)
            {
                ExceptionHandler.Handle("Failed to receive payload", exception);
            }
        }
    }

    static void Dispatch(string payload, Action<MovePayload> move, Action<DeletePayload> delete)
    {
        if (payload.Contains("\"Type\":\"Move\"") ||
            payload.Contains("\"Type\": \"Move\""))
        {
            move(Serializer.Deserialize<MovePayload>(payload));
            return;
        }

        if (payload.Contains("\"Type\":\"Delete\"") ||
            payload.Contains("\"Type\": \"Delete\""))
        {
            delete(Serializer.Deserialize<DeletePayload>(payload));
            return;
        }

        if (payload.Length > 0)
        {
            // Tolerate payloads from newer clients so future additions dont
            // surface an error dialog on this tray version
            Log.Error("Received unknown payload type. Ignoring. Payload: {payload}", payload);
        }
    }
}