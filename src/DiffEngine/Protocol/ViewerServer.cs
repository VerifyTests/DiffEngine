namespace DiffEngine;

/// <summary>
/// The single instance gate and the queue's inbox.
/// <para>
/// Ownership is decided by the bind, not a named mutex: whoever binds the port owns the queue, and
/// a process that fails to bind talks to the owner instead. That is race free without any extra
/// coordination, and it sidesteps the mac named mutex IOException already documented in
/// <see cref="TrayDetector"/>.
/// </para>
/// <para>
/// Lives here rather than in the viewer because either process can be the owner, and a second
/// implementation on the tray side is exactly the drift this protocol move exists to remove.
/// </para>
/// </summary>
sealed class ViewerServer : IDisposable
{
    readonly TcpListener listener;

    ViewerServer(TcpListener listener, int port)
    {
        this.listener = listener;
        Port = port;
    }

    public int Port { get; }

    public static bool TryBind(int port, [NotNullWhen(true)] out ViewerServer? server)
    {
        server = null;
        // Without ExclusiveAddressUse a second bind can succeed on some platforms, and then two
        // processes race for the same queue.
        var listener = new TcpListener(IPAddress.Loopback, port)
        {
            ExclusiveAddressUse = true
        };
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            // Already in use, so someone else owns the queue.
            return false;
        }

        // Port 0 asks the OS to choose, which the tests use to avoid colliding with a live owner.
        server = new(listener, ((IPEndPoint) listener.LocalEndpoint).Port);
        return true;
    }

    /// <summary>
    /// How long a failed accept is waited out before the next, once two have failed in a row.
    /// See <see cref="Serve"/>.
    /// </summary>
    internal static readonly TimeSpan FailedAcceptWait = TimeSpan.FromMilliseconds(100);

    public async Task Listen(Func<ViewerMessage, ViewerResponse> handle, Cancel cancel = default)
    {
        // Sync dispose: CancellationTokenRegistration is only IAsyncDisposable from net6, and
        // waiting for an in flight Stop callback buys nothing here.
        // ReSharper disable once UseAwaitUsing
        using var registration = cancel.Register(Stop);
        await Serve(
                Accept,
                _ =>
                {
                    Interlocked.Increment(ref accepted);
                    Task.Run(() => Handle(_, handle, cancel), Cancel.None);
                },
                cancel)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The accept loop, with the accept and what is done with a connection handed in: a test has
    /// no way to make a real listener's accept fail, and how the loop takes a failure is the
    /// part of it that has gone wrong.
    /// </summary>
    internal static async Task Serve(Func<Cancel, Task<TcpClient>> accept, Action<TcpClient> serve, Cancel cancel)
    {
        var failedInARow = 0;
        while (!cancel.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                // Off whatever thread started listening, here and in Accept. The Windows viewer
                // starts on its UI thread, which has a WinForms context by then, and resuming there
                // waited for the render loop to pump: every connection went unanswered for as long
                // as that thread was busy, which an accept holding InlineApplier's mutex makes up
                // to ten seconds. Both awaits, because the first Accept runs on the caller's thread.
                client = await accept(cancel).ConfigureAwait(false);
                failedInARow = 0;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                // The listener was stopped by cancellation.
                return;
            }
            catch (InvalidOperationException)
            {
                // Same, on the frameworks where a stopped listener reports it this way.
                return;
            }
            catch (SocketException exception)
                when (IsStop(exception, cancel))
            {
                return;
            }
            catch (SocketException)
            {
                // A failure of one accept rather than of the listener. A peer that resets while
                // its connection sits in the backlog surfaces exactly this way - WSAECONNRESET on
                // Windows, ECONNABORTED on BSD and macOS - and returning gave the queue away for
                // the life of the process: the socket stays bound, so nobody else can take it,
                // and every later client lands in a backlog nothing is draining.
                //
                // That one is over as soon as it is reported, so the first failure is retried at
                // once. One that fails again is not that. A process out of descriptors is refused
                // every accept, at once, with the connection left waiting in the backlog, until
                // something is closed: retried straight away that was ten thousand failed accepts
                // a second and a whole core, for as long as it lasted. So from the second on there
                // is a wait between them
                failedInARow++;
                if (failedInARow > 1 &&
                    !await Pause(cancel).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            // Each connection on its own task, so one slow exchange does not stop the next from
            // being answered. Accepting an inline snapshot legitimately takes seconds, and a
            // client whose listing goes unanswered for that long concludes the owner has died.
            serve(client);
        }
    }

    /// <summary>
    /// False when the listener was stopped during the wait.
    /// </summary>
    static async Task<bool> Pause(Cancel cancel)
    {
        try
        {
            await Task.Delay(FailedAcceptWait, cancel).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a socket failure means the listener itself has stopped, rather than one accept
    /// having failed. Cancellation is the ordinary way that happens; the two error codes are how a
    /// stopped listener reports itself when the token has not been observed yet.
    /// </summary>
    internal static bool IsStop(SocketException exception, Cancel cancel) =>
        cancel.IsCancellationRequested ||
        exception.SocketErrorCode is SocketError.OperationAborted or SocketError.Interrupted;

    // ReSharper disable once ReplaceAsyncWithTaskReturn
    async Task<TcpClient> Accept(Cancel cancel)
    {
#if NET6_0_OR_GREATER
        return await listener.AcceptTcpClientAsync(cancel).ConfigureAwait(false);
#else
        // No token overload here, so cancellation arrives as the registered Stop, which faults
        // this await with one of the exceptions the caller already treats as "stop serving".
        cancel.ThrowIfCancellationRequested();
        return await listener.AcceptTcpClientAsync().ConfigureAwait(false);
#endif
    }

    /// <summary>
    /// The first line of a connection that is kept: one request after another, each ended by an
    /// empty line and answered the same way, until either side closes.
    /// <para>
    /// The ordinary exchange is a connection each, ended by the client closing its half, and the
    /// side that closes first is the side whose port then waits out TIME_WAIT. Windows has about
    /// 16,000 ports to give out and keeps each for two minutes, a passing inline verification
    /// settles once, and so a large enough green run with a tray answering used up the machine's
    /// ports on telling the tray nothing. A client that has many of them to send keeps one
    /// connection instead: see <see cref="ViewerClient.TrySend(ViewerMessage)"/>.
    /// </para>
    /// <para>
    /// No request starts with this line, since every one starts with its version, so an owner
    /// that predates it reads it as an unreadable request and says so. It is never sent to one:
    /// a client keeps a connection only to an owner that has just said it <see cref="Keeps"/>.
    /// </para>
    /// </summary>
    internal const string Keep = "keep: 1";

    /// <summary>
    /// The line an owner that takes <see cref="Keep"/> ends every ordinary reply with, which is
    /// how a client knows to ask. A reader that predates it skips the line, as it skips any name
    /// it does not know.
    /// </summary>
    internal const string Keeps = "keeps: 1";

    /// <summary>
    /// The kept connections, so that stopping closes them. Nothing else would: each is a read
    /// waiting on a client that has nothing to say yet, and before net7 that read takes no token.
    /// A client still being answered on one by an owner that had stopped listening would be told
    /// about a queue that is no longer the port's.
    /// </summary>
    readonly ConcurrentDictionary<TcpClient, byte> kept = new();

    volatile bool stopped;

    int accepted;

    /// <summary>
    /// How many connections have been accepted. For the tests and the benchmark, to which a
    /// connection kept and a connection each look the same from the answers.
    /// </summary>
    internal int Accepted => Volatile.Read(ref accepted);

    async Task Handle(TcpClient client, Func<ViewerMessage, ViewerResponse> handle, Cancel cancel)
    {
        try
        {
            using (client)
            {
                // ReSharper disable once UseAwaitUsing
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var first = await ReadLine(reader, cancel);
                if (first == Keep)
                {
                    await HandleKept(client, stream, reader, handle, cancel);
                    return;
                }

#if NET7_0_OR_GREATER
                var rest = await reader.ReadToEndAsync(cancel);
#else
                var rest = await reader.ReadToEndAsync();
#endif

                // Put back together with the line that was read to tell the two kinds apart
                var response = Respond(handle, first is null ? rest : $"{first}\n{rest}");
                await Write(stream, $"{response.Build()}{Keeps}\n", cancel);
            }
        }
        catch (Exception exception)
            when (exception is
                IOException or
                SocketException or
                ObjectDisposedException or
                OperationCanceledException)
        {
            // A client that vanished mid exchange, or shutdown arriving midway. Nothing left to
            // answer, and nothing the owner of the queue needs to hear about.
        }
    }

    /// <summary>
    /// One request after another on a connection the client keeps. Answered in turn rather than
    /// each on a task of its own, which is the order the client sent them in and all it can use:
    /// it waits for each answer before it sends the next.
    /// </summary>
    async Task HandleKept(
        TcpClient client,
        NetworkStream stream,
        StreamReader reader,
        Func<ViewerMessage, ViewerResponse> handle,
        Cancel cancel)
    {
        // Each request is one small write waiting on one small answer, which is the pattern
        // Nagle's algorithm holds back
        client.NoDelay = true;
        kept[client] = 0;
        try
        {
            // Checked after it is listed, so that a stop on either side of the listing closes it
            while (!stopped)
            {
                var request = new StringBuilder();
                string? line;
                while ((line = await ReadLine(reader, cancel)) is { Length: > 0 })
                {
                    request.Append(line);
                    request.Append('\n');
                }

                if (line is null)
                {
                    // The client has gone. Anything it had half sent is nothing to answer
                    return;
                }

                var response = Respond(handle, request.ToString());
                await Write(stream, $"{response.Build()}\n", cancel);
            }
        }
        finally
        {
            kept.TryRemove(client, out _);
        }
    }

    // ReSharper disable once ReplaceAsyncWithTaskReturn
    static async Task<string?> ReadLine(StreamReader reader, Cancel cancel)
    {
#if NET7_0_OR_GREATER
        return await reader.ReadLineAsync(cancel);
#else
        cancel.ThrowIfCancellationRequested();
        return await reader.ReadLineAsync();
#endif
    }

    static async Task Write(NetworkStream stream, string text, Cancel cancel)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
#if NET6_0_OR_GREATER
        await stream.WriteAsync(bytes, cancel);
#else
        await stream.WriteAsync(bytes, 0, bytes.Length, cancel);
#endif
        await stream.FlushAsync(cancel);
    }

    static ViewerResponse Respond(Func<ViewerMessage, ViewerResponse> handle, string text)
    {
        if (!ViewerMessage.TryParse(text, out var message))
        {
            return ViewerResponse.Error("Unreadable request");
        }

        try
        {
            return handle(message);
        }
        catch (Exception exception)
        {
            // Answered rather than thrown: the connection runs on an untracked task, so a throwing
            // handler would otherwise vanish silently and leave the client waiting out its timeout.
            return ViewerResponse.Error(exception.Message);
        }
    }

    void Stop()
    {
        stopped = true;
        listener.Stop();
        foreach (var client in kept.Keys)
        {
            // Unblocks the read it is waiting in, which ends its task
            client.Close();
        }
    }

    public void Dispose() =>
        Stop();
}
