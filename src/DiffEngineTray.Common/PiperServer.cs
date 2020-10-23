using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

static class PiperServer
{
    public static async Task Start(
        Action<MovePayload> move,
        Action<DeletePayload> delete,
        CancellationToken cancellation = default)
    {
        TcpListener? listener = default;

        try
        {
            listener = new TcpListener(IPAddress.Loopback, PiperClient.Port);
            listener.Start();

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await Handle(listener, move, delete, cancellation);
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
                    ExceptionHandler.Handle("Failed to receive payload", exception);
                }
            }
        }
        finally
        {
            listener?.Stop();
        }
    }

    static async Task Handle(TcpListener listener, Action<MovePayload> move, Action<DeletePayload> delete, CancellationToken cancellation)
    {
        await using (cancellation.Register(listener.Stop))
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var reader = new StreamReader(client.GetStream());
            var payload = await reader.ReadToEndAsync();

            if (payload.Contains("\"Type\":\"Move\""))
            {
                var movePayload = Serializer.Deserialize<MovePayload>(payload);
                move(movePayload);
            }
            else if (payload.Contains("\"Type\":\"Delete\""))
            {
                var deletePayload = Serializer.Deserialize<DeletePayload>(payload);
                delete(deletePayload);
            }
            else
            {
                throw new Exception($"Unknown payload: {payload}");
            }

            if (client.Connected)
            {
                client.Close();
            }
        }
    }
}