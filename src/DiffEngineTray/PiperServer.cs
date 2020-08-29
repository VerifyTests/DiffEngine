using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

static class PiperServer
{
    public static async Task Start(
        Action<MovePayload> receiveMove,
        Action<DeletePayload> receiveDelete,
        CancellationToken cancellation = default)
    {
        while (true)
        {
            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Handle(receiveMove, receiveDelete, cancellation);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                ExceptionHandler.Handle("Failed to receive payload", exception);
            }
        }
    }

    static async Task Handle(Action<MovePayload> receiveMove, Action<DeletePayload> receiveDelete, CancellationToken cancellation)
    {
        await using var pipe = new NamedPipeServerStream(
            "DiffEngine",
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(cancellation);
        using var reader = new StreamReader(pipe);
        var payload = await reader.ReadToEndAsync();

        if (payload.Contains("\"Type\":\"Move\""))
        {
            var movePayload = Serializer.Deserialize<MovePayload>(payload);
            receiveMove(movePayload);
        }
        else if (payload.Contains("\"Type\":\"Delete\""))
        {
            var deletePayload = Serializer.Deserialize<DeletePayload>(payload);
            receiveDelete(deletePayload);
        }
        else
        {
            throw new Exception($"Unknown payload: {payload}");
        }

        if (pipe.IsConnected)
        {
            pipe.Disconnect();
        }
    }
}