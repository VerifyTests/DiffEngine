static class PiperClient
{
    public static int Port = 3492;

    public static void SendDelete(string file) =>
        Send(BuildDeletePayload(file));

    public static Task SendDeleteAsync(
        string file,
        Cancel cancel = default)
    {
        var payload = BuildDeletePayload(file);
        return SendAsync(payload, cancel);
    }

    static string BuildDeletePayload(string file) =>
        $$"""
          {
          "Type":"Delete",
          "File":"{{file.JsonEscape()}}"
          }

          """;

    public static void SendMove(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId) =>
        Send(BuildMovePayload(tempFile, targetFile, exe, arguments, canKill, processId));

    public static Task SendMoveAsync(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId,
        Cancel cancel = default)
    {
        var payload = BuildMovePayload(tempFile, targetFile, exe, arguments, canKill, processId);
        return SendAsync(payload, cancel);
    }

    public static string BuildMovePayload(string tempFile, string targetFile, string? exe, string? arguments, bool canKill, int? processId)
    {
        var builder = new StringBuilder(
            $$"""
              {
              "Type":"Move",
              "Temp":"{{tempFile.JsonEscape()}}",
              "Target":"{{targetFile.JsonEscape()}}",
              "CanKill":{{canKill.ToString().ToLower()}}
              """);

        if (exe != null)
        {
            builder.Append(
                $"""
                 ,
                 "Exe":"{exe.JsonEscape()}",
                 "Arguments":"{arguments!.JsonEscape()}"
                 """);
        }

        if (processId != null)
        {
            builder.Append(
                $"""
                 ,
                 "ProcessId":{processId}
                 """);
        }

        builder.AppendLine();
        builder.Append('}');
        return builder.ToString();
    }

    public static void SendInlineMove(
        string tempFile,
        string targetFile,
        string patchFile,
        string? stagedVerified) =>
        Send(BuildInlineMovePayload(tempFile, targetFile, patchFile, stagedVerified));

    public static Task SendInlineMoveAsync(
        string tempFile,
        string targetFile,
        string patchFile,
        string? stagedVerified,
        Cancel cancel = default)
    {
        var payload = BuildInlineMovePayload(tempFile, targetFile, patchFile, stagedVerified);
        return SendAsync(payload, cancel);
    }

    public static string BuildInlineMovePayload(string tempFile, string targetFile, string patchFile, string? stagedVerified)
    {
        var builder = new StringBuilder(
            $$"""
              {
              "Type":"InlineMove",
              "Temp":"{{tempFile.JsonEscape()}}",
              "Target":"{{targetFile.JsonEscape()}}",
              "PatchFile":"{{patchFile.JsonEscape()}}"
              """);

        if (stagedVerified != null)
        {
            builder.Append(
                $"""
                 ,
                 "StagedVerified":"{stagedVerified.JsonEscape()}"
                 """);
        }

        builder.AppendLine();
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

    static async Task SendAsync(string payload, Cancel cancel)
    {
        try
        {
            await InnerSendAsync(payload, cancel);
        }
        // Let cancellation surface to the caller; only genuine send failures are swallowed.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleSendException(payload, exception);
        }
    }

    static void HandleSendException(string payload, Exception exception) =>
        Trace.WriteLine(
            $"""
             Failed to send payload to DiffEngineTray.

             Payload:
             {payload}

             Exception:
             {exception}
             """);

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

    static async Task InnerSendAsync(string payload, Cancel cancel)
    {
        using var client = new TcpClient();
        var endpoint = GetEndpoint();
        try
        {
#if NET6_0_OR_GREATER
            await client.ConnectAsync(endpoint.Address, endpoint.Port, cancel);
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(payload.AsMemory(), cancel);
#else
            cancel.ThrowIfCancellationRequested();
            // Older frameworks lack cancellable Connect/Write, so abort by closing the client.
            using (cancel.Register(client.Close))
            {
                await client.ConnectAsync(endpoint.Address, endpoint.Port);
                using var stream = client.GetStream();
                using var writer = new StreamWriter(stream);
                await writer.WriteAsync(payload);
            }
#endif
        }
        finally
        {
            client.Close();
        }
    }

    static IPEndPoint GetEndpoint() =>
        new(IPAddress.Loopback, Port);
}