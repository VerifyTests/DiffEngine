using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
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

            await Handle(receiveMove, receiveDelete, cancellation);
        }
    }

    static async Task Handle(Action<MovePayload> receiveMove, Action<DeletePayload> receiveDelete, CancellationToken cancellation)
    {
        await using var pipe = new NamedPipeServerStream(
            "DiffEngineUtil",
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(cancellation);
        using var reader = new StreamReader(pipe);
        var message = await reader.ReadToEndAsync();

        if (message.Contains("\"Type\":\"Move\""))
        {
            var payload = JsonSerializer.Deserialize<MovePayload>(message);
            receiveMove(payload);
        }
        else if (message.Contains("\"Type\":\"Delete\""))
        {
            var payload = JsonSerializer.Deserialize<DeletePayload>(message);
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