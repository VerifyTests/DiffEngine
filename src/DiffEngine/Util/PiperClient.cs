using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

static class PiperClient
{
    public static async Task Send(
        string tempFile,
        string targetFile,
        bool isMdi,
        bool autoRefresh,
        int processId,
        CancellationToken cancellation = default)
    {
        var payload=$@"{{
""Temp"":""{tempFile}"",
""Target"":""{targetFile}"",
""IsMdi"":{isMdi.ToString().ToLower()},
""AutoRefresh"":{autoRefresh.ToString().ToLower()},
""ProcessId"":{processId}
}}
";
#if(NETSTANDARD2_1)
        await using var pipe = new NamedPipeClientStream(
            ".",
            "DiffEngineUtil",
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using var stream = new StreamWriter(pipe);
        await pipe.ConnectAsync(1000, cancellation);
        await stream.WriteAsync(System.MemoryExtensions.AsMemory(payload), cancellation);
#else
        using var pipe = new NamedPipeClientStream(
            ".",
            "DiffEngineUtil",
            PipeDirection.Out,
            PipeOptions.Asynchronous);
        using var stream = new StreamWriter(pipe);
        await pipe.ConnectAsync(1000, cancellation);
        await stream.WriteAsync(payload);
#endif
    }
}