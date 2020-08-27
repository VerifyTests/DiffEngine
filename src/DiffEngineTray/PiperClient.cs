using System;
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
        bool canKill,
        int? processId,
        DateTime? processStartTime,
        CancellationToken cancellation = default)
    {
        string datePart = "";
        if (processStartTime != null)
        {
            datePart = $@",
""ProcessStartTime"":""{processStartTime.Value:O}""";
        }

        var payload = $@"{{
""Type"":""Move"",
""Temp"":""{tempFile.JsonEscape()}"",
""Target"":""{targetFile.JsonEscape()}"",
""CanKill"":{canKill.ToString().ToLower()},
""ProcessId"":{processId}" + datePart + @"
}
";
        return Send(payload, cancellation);
    }

    static string JsonEscape(this string tempFile)
    {
        return tempFile.Replace(@"\", @"\\");
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
        await stream.WriteAsync(payload.AsMemory(), cancellation);
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