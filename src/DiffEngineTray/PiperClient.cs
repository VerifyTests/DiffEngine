using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

static class PiperClient
{
    public static void SendDelete(string file)
    {
        Send(BuildDeletePayload(file));
    }

    public static Task SendDeleteAsync(
        string file,
        CancellationToken cancellation = default)
    {
        var payload = BuildDeletePayload(file);
        return SendAsync(payload, cancellation);
    }

    static string BuildDeletePayload(string file)
    {
        return $@"{{
""Type"":""Delete"",
""File"":""{file}""
}}
";
    }

    public static void SendMove(
        string tempFile,
        string targetFile,
        string exe,
        string arguments,
        bool canKill,
        int? processId)
    {
        Send(BuildMovePayload(tempFile, targetFile, exe, arguments, canKill, processId));
    }

    public static Task SendMoveAsync(
        string tempFile,
        string targetFile,
        string exe,
        string arguments,
        bool canKill,
        int? processId,
        CancellationToken cancellation = default)
    {
        var payload = BuildMovePayload(tempFile, targetFile, exe, arguments, canKill, processId);
        return SendAsync(payload, cancellation);
    }

    static string BuildMovePayload(string tempFile, string targetFile, string exe, string arguments, bool canKill, int? processId)
    {
        var builder = new StringBuilder($@"{{
""Type"":""Move"",
""Temp"":""{tempFile.JsonEscape()}"",
""Target"":""{targetFile.JsonEscape()}"",
""Exe"":""{exe.JsonEscape()}"",
""Arguments"":""{arguments.JsonEscape()}"",
""CanKill"":{canKill.ToString().ToLower()}");

        if (processId != null)
        {
            builder.AppendLine(",");
            builder.AppendLine($"\"ProcessId\":{processId}");
        }

        builder.Append('}');
        return builder.ToString();
    }

    static void Send(string payload)
    {
        try
        {
            InnerSend(payload);
        }
        catch (Exception exception)
        {
            HandleSendException(payload, exception);
        }
    }

    static async Task SendAsync(string payload, CancellationToken cancellation = default)
    {
        try
        {
            await InnerSendAsync(payload, cancellation);
        }
        catch (Exception exception)
        {
            HandleSendException(payload, exception);
        }
    }

    static void HandleSendException(string payload, Exception exception)
    {
        Trace.WriteLine($@"Failed to send payload to DiffEngineTray.

Payload:
{payload}

Exception:
{exception}");
    }

    static void InnerSend(string payload)
    {
#if(NETSTANDARD2_1)
        using var pipe = new NamedPipeClientStream(
            ".",
            "DiffEngine",
            PipeDirection.Out,
            PipeOptions.CurrentUserOnly);
        using var stream = new StreamWriter(pipe);
        pipe.Connect(1000);
        stream.Write(payload.AsMemory());
#else
        using var pipe = new NamedPipeClientStream(
            ".",
            "DiffEngine",
            PipeDirection.Out,
            PipeOptions.None);
        using var stream = new StreamWriter(pipe);
        pipe.Connect(1000);
        stream.Write(payload);
#endif
    }

    static async Task InnerSendAsync(string payload, CancellationToken cancellation)
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