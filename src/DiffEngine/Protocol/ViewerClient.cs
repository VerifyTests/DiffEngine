namespace DiffEngine;

/// <summary>
/// Talks to whoever owns the inline queue. A refused connection means nobody does, which the
/// caller turns into a launch (DiffEngine), "nothing pending" (the tray), or "the owner has gone"
/// (an attached viewer).
/// </summary>
/// <summary>
/// What came back from an exchange with the queue owner. Three outcomes rather than two, because
/// "nobody is there" and "the owner said no" call for opposite responses: the first is fixed by
/// launching a viewer, and the second is not.
/// </summary>
enum SendOutcome
{
    /// <summary>
    /// Nobody answered. No owner, or one present but unresponsive - the caller cannot tell, and
    /// for its purposes they are the same.
    /// </summary>
    NoOwner,

    /// <summary>
    /// The owner answered and took it.
    /// </summary>
    Accepted,

    /// <summary>
    /// The owner answered and declined it: a version it does not understand, or a handler that
    /// threw. Launching another viewer will not change that answer.
    /// </summary>
    Refused
}

static class ViewerClient
{
    public const int DefaultPort = 3493;

    /// <summary>
    /// The tray's piper sits on 3492. Tests override this so a run never talks to a live viewer,
    /// mirroring how PiperTest reassigns PiperClient.Port.
    /// </summary>
    public const string PortVariable = "DiffEngine_ViewerPort";

    /// <summary>
    /// Read from the environment on every call rather than cached, so a test that sets the
    /// variable does not depend on having done so before the first send.
    /// </summary>
    public static int Port
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(PortVariable);
            if (int.TryParse(value, out var port) &&
                port is > 0 and < 65536)
            {
                return port;
            }

            return DefaultPort;
        }
    }

    static readonly TimeSpan timeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The deadline for the async exchange. Longer than the synchronous one because the owner
    /// answers on its listener thread, so a connection can sit behind an accept that is itself
    /// waiting up to ten seconds on <see cref="InlineApplier"/>'s cross process mutex. Shorter
    /// than forever because there was no bound at all: SendTimeout and ReceiveTimeout apply only
    /// to synchronous calls, and the token every async call was given is the caller's, which is
    /// default from DiffRunner.AddInlineAsync - Verify passes none. An owner that accepted the
    /// connection and then stopped answering hung the failing test for good.
    /// </summary>
    static readonly TimeSpan asyncTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// For callers on a clock or an interactive path, such as the tray's scan timer and its menu.
    /// The exchange is loopback to a local process, so anything slower than this is a wedged owner
    /// rather than a slow one, and waiting the full timeout would let timer callbacks outlast
    /// their own period and pile up.
    /// </summary>
    public static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Whether anything is listening, without sending it anything. For a caller that has just
    /// started a viewer and wants to know when it can be talked to, which a send cannot answer
    /// without also handing over work.
    /// </summary>
    public static bool IsOwned(int? port = null)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(IPAddress.Loopback, port ?? Port).Wait(ShortTimeout);
        }
        catch (Exception exception)
            when (Ignorable(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// True when the owner acknowledged. A refused connection means nobody owns the queue.
    /// </summary>
    public static bool TrySend(ViewerMessage message) =>
        TrySend(message, out var response) &&
        response.Ok;

    /// <summary>
    /// True when a reply arrived and parsed, whatever it says. Callers that need the body, such as
    /// the tray listing pending snapshots, use this rather than <see cref="TrySend(ViewerMessage)"/>.
    /// <para>
    /// <paramref name="port"/> overrides <see cref="Port"/> for a single call. Tests pass their own
    /// ephemeral port rather than mutating anything static, so they can run in parallel.
    /// </para>
    /// </summary>
    public static bool TrySend(
        ViewerMessage message,
        [NotNullWhen(true)] out ViewerResponse? response,
        int? port = null,
        TimeSpan? wait = null)
    {
        response = null;
        var deadline = wait ?? timeout;
        try
        {
            using var client = new TcpClient();
            if (!client.ConnectAsync(IPAddress.Loopback, port ?? Port).Wait(deadline))
            {
                return false;
            }

            Configure(client, deadline);
            var stream = client.GetStream();
            var bytes = Encoding.UTF8.GetBytes(message.Build());
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            HalfClose(client);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return ViewerResponse.TryParse(reader.ReadToEnd(), out response);
        }
        catch (Exception exception)
            when (Ignorable(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Fully async, including the read. A blocking read here would tie up a thread pool thread for
    /// the whole exchange, and a parallel test run calling this once per failing snapshot would
    /// starve the pool on a small machine.
    /// <para>
    /// <paramref name="port"/> and <paramref name="wait"/> override <see cref="Port"/> and
    /// <see cref="asyncTimeout"/> for a single call, as they do on the synchronous overload. Tests
    /// pass their own ephemeral port rather than mutating anything static, so they can run in
    /// parallel.
    /// </para>
    /// </summary>
    public static async Task<bool> TrySendAsync(
        ViewerMessage message,
        Cancel cancel,
        int? port = null,
        TimeSpan? wait = null) =>
        await SendAsync(message, cancel, port, wait) == SendOutcome.Accepted;

    /// <summary>
    /// As <see cref="TrySendAsync" />, but says which of the two failures happened. A caller that
    /// would launch a viewer on absence needs that: launching one because the owner refused the
    /// payload leaves two processes and still no snapshot.
    /// </summary>
    public static async Task<SendOutcome> SendAsync(
        ViewerMessage message,
        Cancel cancel,
        int? port = null,
        TimeSpan? wait = null)
    {
        var endpointPort = port ?? Port;
        var timeToWait = wait ?? asyncTimeout;
        using var deadline = CancelSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(timeToWait);
        var token = deadline.Token;
        try
        {
            using var client = new TcpClient();
            // Closing the socket is the only thing that unblocks every framework: the pre-net7
            // ReadToEndAsync takes no token at all, and net462 has no cancellable connect or
            // write either. Registered after the client and so disposed before it, which is what
            // stops the callback firing on a disposed object
            using var abort = token.Register(() => Abort(client));
#if NET6_0_OR_GREATER
            await client.ConnectAsync(IPAddress.Loopback, endpointPort, token);
#else
            token.ThrowIfCancellationRequested();
            await client.ConnectAsync(IPAddress.Loopback, endpointPort);
            // The abort registration cancels by closing the client, and .NET Framework's
            // TcpClient.Dispose nulls its Client - so a token that fires around here leaves
            // Configure and HalfClose dereferencing null rather than reporting cancellation.
            // Asking the token directly is how that becomes the OperationCanceledException the
            // caller is written against
            token.ThrowIfCancellationRequested();
#endif
            Configure(client, timeToWait);
            var stream = client.GetStream();
            var bytes = Encoding.UTF8.GetBytes(message.Build());
#if NET6_0_OR_GREATER
            await stream.WriteAsync(bytes, token);
#else
            await stream.WriteAsync(bytes, 0, bytes.Length, token);
#endif
            await stream.FlushAsync(token);
            HalfClose(client);
            using var reader = new StreamReader(stream, Encoding.UTF8);
#if NET7_0_OR_GREATER
            var text = await reader.ReadToEndAsync(token);
#else
            var text = await reader.ReadToEndAsync();
#endif
            if (!ViewerResponse.TryParse(text, out var response))
            {
                return SendOutcome.NoOwner;
            }

            return response.Ok ? SendOutcome.Accepted : SendOutcome.Refused;
        }
        // The deadline, rather than the caller cancelling. Whatever the abort surfaced as - a
        // cancellation, a closed socket, a torn down stream - the owner is present but not
        // answering. Reported as absence because that is the recoverable answer: the caller
        // launches a viewer or stages the patch, rather than waiting on a process that has
        // stopped listening. Logged so the two are still tellable apart afterwards
        catch (Exception exception)
            when (!cancel.IsCancellationRequested && token.IsCancellationRequested)
        {
            // Trace rather than Logging, because this file is linked into the viewer too
            Trace.WriteLine(
                $"Timed out after {timeToWait} waiting for the inline queue owner on port {endpointPort}. " +
                $"Verb: {message.Verb}. The owner is present but unresponsive. {exception.GetType().Name}");
            return SendOutcome.NoOwner;
        }
        // Cancellation is the caller's business; a missing owner is not.
        catch (Exception exception)
            when (exception is not OperationCanceledException && Ignorable(exception))
        {
            return SendOutcome.NoOwner;
        }
    }

    /// <summary>
    /// Unblocks whatever the exchange is waiting on. Swallowing here rather than letting it out:
    /// this runs on the timer that fired the deadline, where a throw has nowhere to go.
    /// </summary>
    static void Abort(TcpClient client)
    {
        try
        {
            client.Close();
        }
        catch (Exception exception)
            when (Ignorable(exception))
        {
        }
    }
    static void Configure(TcpClient client, TimeSpan wait)
    {
        client.SendTimeout = (int) wait.TotalMilliseconds;
        client.ReceiveTimeout = (int) wait.TotalMilliseconds;
    }

    /// <summary>
    /// Signals the end of the request without losing the socket the owner replies on.
    /// </summary>
    static void HalfClose(TcpClient client) =>
        client.Client.Shutdown(SocketShutdown.Send);

    static bool Ignorable(Exception exception) =>
        exception is
            SocketException or
            IOException or
            ObjectDisposedException or
            // .NET Framework's TcpClient.Dispose nulls Client, so the abort registration closing
            // the socket mid exchange leaves Configure or HalfClose dereferencing null. It is a
            // torn down connection wearing the wrong exception type, and letting it escape turned
            // an absent owner into a crash in the caller's test
            NullReferenceException or
            AggregateException
            {
                InnerException: SocketException or IOException
            };
}
