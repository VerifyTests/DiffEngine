using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

static class PiperClient
{
    public static Task SendDelete(
        string file,
        CancellationToken cancellation = default)
    {
        var payload = $@"{{
""Type"":""Delete"",
""File"":""{file}""
}}
";
        return Send(payload, cancellation);
    }
    public static Task SendMove(
        string tempFile,
        string targetFile,
        bool isMdi,
        bool autoRefresh,
        int processId,
        CancellationToken cancellation = default)
    {
        var payload = $@"{{
""Type"":""Move"",
""Temp"":""{tempFile}"",
""Target"":""{targetFile}"",
""IsMdi"":{isMdi.ToString().ToLower()},
""AutoRefresh"":{autoRefresh.ToString().ToLower()},
""ProcessId"":{processId}
}}
";
        return Send(payload, cancellation);
    }

    static async Task Send(string payload, CancellationToken cancellation = default)
    {
#if(NETSTANDARD2_1)
        await using var pipe = new NamedPipeClientStream(
            ".",
            "DiffEngine",
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using var stream = new StreamWriter(pipe);
        await pipe.ConnectAsync(1000, cancellation);
        await stream.WriteAsync(System.MemoryExtensions.AsMemory(payload), cancellation);
#else
        using var pipe = new NamedPipeClientStream(
            ".",
            "DiffEngine",
            PipeDirection.Out,
            PipeOptions.Asynchronous);
        using var stream = new StreamWriter(pipe);
        await pipe.ConnectAsync(1000, cancellation);
        await stream.WriteAsync(payload);
#endif
    }
}