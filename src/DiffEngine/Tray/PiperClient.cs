namespace DiffEngine;

static class PiperClient
{
    public static int Port = 3492;

    public static bool SendDelete(string file, string? source = null) =>
        Send(BuildDeletePayload(file, source));

    public static Task<bool> SendDeleteAsync(
        string file,
        Cancel cancel = default,
        string? source = null)
    {
        var payload = BuildDeletePayload(file, source);
        return SendAsync(payload, cancel);
    }

    /// <summary>
    /// <paramref name="source" /> is the received file of the pending move this delete was derived
    /// from: see <see cref="BuildMovePayload" />, where it rides the same way.
    /// </summary>
    public static string BuildDeletePayload(string file, string? source = null)
    {
        // The payload every tray has always been sent, to the byte, when there is no source
        if (source == null)
        {
            return $$"""
                     {
                     "Type":"Delete",
                     "File":"{{file.JsonEscape()}}"
                     }

                     """;
        }

        return $$"""
                 {
                 "Type":"Delete",
                 "File":"{{file.JsonEscape()}}",
                 "Source":"{{source.JsonEscape()}}"
                 }

                 """;
    }

    public static bool SendMove(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId,
        string? source = null) =>
        Send(BuildMovePayload(tempFile, targetFile, exe, arguments, canKill, processId, source));

    public static Task<bool> SendMoveAsync(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId,
        Cancel cancel = default,
        string? source = null)
    {
        var payload = BuildMovePayload(tempFile, targetFile, exe, arguments, canKill, processId, source);
        return SendAsync(payload, cancel);
    }

    /// <summary>
    /// <paramref name="source" /> is the received file of the pending move this one was derived
    /// from, or null: a page of a document whose document is pending too.
    /// <para>
    /// A property added to the payload a tray already reads, rather than a payload type of its
    /// own, and that is the point of it. A tray from before it skips a property it has no member
    /// for - as every tray skips <c>Type</c>, which it reads by substring - and tracks the move
    /// as the ordinary one it also is. A type it did not know would be logged and dropped, and
    /// this send is fire and forget: the file would be pending in nothing, with no way to tell.
    /// </para>
    /// <para>
    /// Last, and absent rather than null when there is none, so the payload a move with no source
    /// sends is the one it always has been.
    /// </para>
    /// </summary>
    public static string BuildMovePayload(string tempFile, string targetFile, string? exe, string? arguments, bool canKill, int? processId, string? source = null)
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

        if (source != null)
        {
            builder.Append(
                $"""
                 ,
                 "Source":"{source.JsonEscape()}"
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
        if (!PortIsHeld())
        {
            HandleNoListener(payload);
            return false;
        }

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
        // Before the listener check, so a cancelled send says so whether or not a tray is there
        cancel.ThrowIfCancellationRequested();
        if (!PortIsHeld())
        {
            HandleNoListener(payload);
            return false;
        }

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

    static void HandleNoListener(string payload) =>
        Trace.WriteLine(
            $"""
             Failed to send payload to DiffEngineTray.

             Payload:
             {payload}

             Nothing is listening on the tray's port.
             """);

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

    /// <summary>
    /// Whether anything is listening, asked of the OS rather than found out by connecting. Whether
    /// a tray runs is read once per process, so after it exits every move and delete still came
    /// here, and a connect to a port nobody holds is not refused at once everywhere: where the SYN
    /// is dropped it runs to its timeout, two seconds a send. The listener table answers in well
    /// under a millisecond. A table that cannot be read leaves the connect to decide.
    /// </summary>
    static bool PortIsHeld() =>
        ListenerTable.IsHeld(Port);

    static IPEndPoint GetEndpoint() =>
        new(IPAddress.Loopback, Port);
}