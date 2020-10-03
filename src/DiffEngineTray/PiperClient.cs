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
    public static int PORT = 3492;

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
        TcpClient? client = default;
        
        try
        {
            var endpoint = GetEndpoint();
            
            client = CreateClient();
            client.Connect(endpoint);
            using var stream = new StreamWriter(client.GetStream());
            stream.Write(payload);
        }
        finally
        {
            client?.Close();
        }
    }

    static async Task InnerSendAsync(string payload, CancellationToken cancellation)
    {
        TcpClient? client = default;
        
        try
        {
            client = CreateClient();
            var endpoint = GetEndpoint();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);
            using var stream = new StreamWriter(client.GetStream());
            await stream.WriteAsync(payload);
        }
        finally
        {
            client?.Close();
        }
    }

    static TcpClient CreateClient()
    {
        var client = new TcpClient();
        return client;
    }

    static IPEndPoint GetEndpoint()
    {
        return new IPEndPoint(IPAddress.Loopback, PORT);
    }
}