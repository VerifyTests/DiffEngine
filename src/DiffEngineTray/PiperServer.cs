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

            while (true)
            {
                if (cancel.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await Handle(listener, move, delete, cancel);
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

    static async Task Handle(TcpListener listener, Action<MovePayload> move, Action<DeletePayload> delete, Cancel cancel)
    {
        await using (cancel.Register(listener.Stop))
        {
            using var client = await listener.AcceptTcpClientAsync(cancel);
            using var reader = new StreamReader(client.GetStream());

            var payload = await reader.ReadToEndAsync(cancel);

            if (payload.Contains("\"Type\":\"Move\"") ||
                payload.Contains("\"Type\": \"Move\""))
            {
                move(Serializer.Deserialize<MovePayload>(payload));
            }
            else if (payload.Contains("\"Type\":\"Delete\"") ||
                     payload.Contains("\"Type\": \"Delete\""))
            {
                delete(Serializer.Deserialize<DeletePayload>(payload));
            }
            else
            {
                if (payload.Length > 0)
                {
                    // Tolerate payloads from newer clients so future additions dont
                    // surface an error dialog on this tray version
                    Log.Error("Received unknown payload type. Ignoring. Payload: {payload}", payload);
                }
            }

            if (client.Connected)
            {
                client.Close();
            }
        }
    }
}