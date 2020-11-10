using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

static class PiperClient
{
    public static int Port = 3492;

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
        using var client = new TcpClient();
        var endpoint = GetEndpoint();
        try
        {
            client.Connect(endpoint);
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream);
            writer.Write(payload);
        }
        finally
        {
            client.Close();
        }
    }

    static async Task InnerSendAsync(string payload, CancellationToken cancellation)
    {
        using var client = new TcpClient();
        var endpoint = GetEndpoint();
        try
        {
            await client.ConnectAsync(endpoint.Address, endpoint.Port);
#if NETCOREAPP || netstandard21
            await using var stream = client.GetStream();
            await using var writer = new StreamWriter(stream);
#else
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream);
#endif
            await writer.WriteAsync(payload);
        }
        finally
        {
            client.Close();
        }
    }

    static IPEndPoint GetEndpoint()
    {
        return new IPEndPoint(IPAddress.Loopback, Port);
    }
}