using System.Net;
using System.Net.Sockets;

static class PiperServer
{
    public static async Task Start(
        Action<MovePayload> move,
        Action<DeletePayload> delete,
        Action<InlineMovePayload> inlineMove,
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
                    await Handle(listener, move, delete, inlineMove, cancel);
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

    static async Task Handle(TcpListener listener, Action<MovePayload> move, Action<DeletePayload> delete, Action<InlineMovePayload> inlineMove, Cancel cancel)
    {
        await using (cancel.Register(listener.Stop))
        {
            using var client = await listener.AcceptTcpClientAsync(cancel);
            using var reader = new StreamReader(client.GetStream());

            var payload = await reader.ReadToEndAsync(cancel);

            // InlineMove is checked before Move for specific-before-general ordering
            // (not strictly load bearing: "Type":"Move" is not a substring of "Type":"InlineMove")
            if (payload.Contains("\"Type\":\"InlineMove\"") ||
                payload.Contains("\"Type\": \"InlineMove\""))
            {
                inlineMove(Serializer.Deserialize<InlineMovePayload>(payload));
            }
            else if (payload.Contains("\"Type\":\"Move\"") ||
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