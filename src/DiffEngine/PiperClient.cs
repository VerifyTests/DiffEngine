using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

static class PiperClient
{
    public static async Task Send(string[] args, CancellationToken cancellation = default)
    {
        #if(NETSTANDARD2_1)
        await using var pipe = new NamedPipeClientStream(
            ".",
            "DiffEngineUtil",
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using var stream = new StreamWriter(pipe);
        await pipe.ConnectAsync(1000, cancellation);
        var message = string.Join(Environment.NewLine, args);
        await stream.WriteAsync(message.AsMemory(), cancellation);
        #else
        using var pipe = new NamedPipeClientStream(
            ".",
            "DiffEngineUtil",
            PipeDirection.Out,
            PipeOptions.Asynchronous);
        using var stream = new StreamWriter(pipe);
        await pipe.ConnectAsync(1000, cancellation);
        var message = string.Join(Environment.NewLine, args);
        await stream.WriteAsync(message);
        #endif
    }
}