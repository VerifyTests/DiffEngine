static class PiperClient
{
    public static int Port = 3492;

    public static bool SendDelete(string file) =>
        Send(BuildDeletePayload(file));

    public static Task<bool> SendDeleteAsync(
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

    public static bool SendMove(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId) =>
        Send(BuildMovePayload(tempFile, targetFile, exe, arguments, canKill, processId));

    public static Task<bool> SendMoveAsync(
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

    /// <summary>
    /// True when the tray took it. False is not fatal on its own - the payload is traced either
    /// way - but it is what lets the caller send the pending file somewhere else instead of
    /// dropping it, which is what happened when this returned nothing.
    /// </summary>
    static bool Send(string payload)
    {
        try
        {
            InnerSend(payload);
            return true;
        }
        catch (Exception exception)
        {
            HandleSendException(payload, exception);
            return false;
        }
    }

    static async Task<bool> SendAsync(string payload, Cancel cancel)
    {
        try
        {
            await InnerSendAsync(payload, cancel);
            return true;
        }
        // Let cancellation surface to the caller; only genuine send failures are swallowed. A
        // NullReferenceException under a cancelled token is cancellation too - .NET Framework's
        // Dispose nulls Client - and reporting it as a send failure would lose the cancel
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NullReferenceException) when (cancel.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancel);
        }
        catch (Exception exception)
        {
            HandleSendException(payload, exception);
            return false;
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
            // ReSharper disable once UseAwaitUsing
            using var stream = client.GetStream();
            // ReSharper disable once UseAwaitUsing
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(payload.AsMemory(), cancel);
#else
            cancel.ThrowIfCancellationRequested();
            // Older frameworks lack cancellable Connect/Write, so abort by closing the client.
            using (cancel.Register(client.Close))
            {
                await client.ConnectAsync(endpoint.Address, endpoint.Port);
                // Dispose nulls Client on .NET Framework, so a token that fires around the connect
                // leaves GetStream dereferencing null instead of reporting cancellation. Asking
                // the token directly is what makes that an OperationCanceledException, which the
                // caller lets through rather than swallowing as a send failure
                cancel.ThrowIfCancellationRequested();
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