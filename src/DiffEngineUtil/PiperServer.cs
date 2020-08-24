using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

static class PiperServer
{
    public static async Task Start(Action<string[]> receive, CancellationToken cancellation = default)
    {
        while (true)
        {
            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            await Handle(receive, cancellation);
        }
    }

    static async Task Handle(Action<string[]> receive, CancellationToken cancellation)
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
        receive(message.Split(Environment.NewLine));

        if (pipe.IsConnected)
        {
            pipe.Disconnect();
        }
    }
}