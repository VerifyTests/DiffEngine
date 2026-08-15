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
    public static async Task Start(
        Action<MovePayload> move,
        Action<DeletePayload> delete,
        Cancel cancel = default)
    {
        TcpListener? listener = default;

        try
        {
            listener = new(IPAddress.Loopback, PiperClient.Port);
            listener.Start();
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
                    _ = Handle(client, move, delete, cancel);
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
                catch (IOException exception)
                    when (exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
                {
                    //client disconnected abruptly, e.g. test was canceled
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
            listener?.Stop();
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
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
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