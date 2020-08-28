using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

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
                Log.Error(exception, "Failed to receive payload");
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
        var message = await reader.ReadToEndAsync();

        if (message.Contains("\"Type\":\"Move\""))
        {
            var payload = Serializer.Deserialize<MovePayload>(message);
            receiveMove(payload);
        }
        else if (message.Contains("\"Type\":\"Delete\""))
        {
            var payload = Serializer.Deserialize<DeletePayload>(message);
            receiveDelete(payload);
        }
        else
        {
            throw new Exception($"Unknown message: {message}");
        }

        if (pipe.IsConnected)
        {
            pipe.Disconnect();
        }
    }
}