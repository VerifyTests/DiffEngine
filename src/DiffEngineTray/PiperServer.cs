using System.Net;
using System.Net.Sockets;

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
                    throw new($"Unknown payload: {payload}");
                }
            }

            if (client.Connected)
            {
                client.Close();
            }
        }
    }
}