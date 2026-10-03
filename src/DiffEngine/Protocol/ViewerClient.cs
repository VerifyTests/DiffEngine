namespace DiffEngine;

/// <summary>
/// Talks to whoever owns the inline queue. A refused connection means nobody does, which the
/// caller turns into a launch (DiffEngine), "nothing pending" (the tray), or "the owner has gone"
/// (an attached viewer). It is also remembered, per port and for a while, so the sends that are
/// only telling the owner something do not pay for the same refusal once per test: see
/// <see cref="ViewerClient.RecheckUnownedAfter"/>.
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

    static TimeSpan timeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The deadline for the async exchange. Longer than the synchronous one because the owner
    /// answers on its listener thread, so a connection can sit behind an accept that is itself
    /// waiting up to ten seconds on <see cref="InlineApplier"/>'s cross process mutex. Shorter
    /// than forever because there was no bound at all: SendTimeout and ReceiveTimeout apply only
    /// to synchronous calls, and the token every async call was given is the caller's, which is
    /// default from DiffRunner.AddInlineAsync - Verify passes none. An owner that accepted the
    /// connection and then stopped answering hung the failing test for good.
    /// </summary>
    static TimeSpan asyncTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// For callers on a clock or an interactive path, such as the tray's scan timer and its menu.
    /// The exchange is loopback to a local process, so anything slower than this is a wedged owner
    /// rather than a slow one, and waiting the full timeout would let timer callbacks outlast
    /// their own period and pile up.
    /// </summary>
    public static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long a port a connect found nobody on stands as unowned, during which the sends that
    /// only tell the owner something return without connecting. For a port the listener table
    /// found empty it is <see cref="RecheckUnlistedAfter"/>.
    /// <para>
    /// A refused loopback connection is not free everywhere. Windows Firewall's stealth mode, on
    /// by default, drops the reset a closed port would answer with, so the connect sits through
    /// the SYN retries and fails after two seconds rather than at once. A passing inline snapshot
    /// settles once per verification, and nothing owning the queue is the ordinary state of a
    /// machine with no tray - so a green run of two hundred inline tests spent six minutes
    /// connecting to nobody, and the same run took under a second with a tray answering.
    /// </para>
    /// <para>
    /// Long, because a recheck that has to connect buys almost nothing for those two seconds
    /// again. What the memory can delay is only a message the owner did not have to receive: an
    /// entry to settle in a queue that did not exist when the test failed, or a move to track in
    /// a tray that was not there to track it. Anything that has to reach an owner - a patch, a
    /// delete, a pair - goes through the launch gate, whose <see cref="IsOwned"/> probe always
    /// asks and corrects the memory with what it finds. Not the life of the process only for a
    /// long lived consumer that is not a test host, launching diff tools all day, where a tray
    /// started later would otherwise never see its moves until a restart.
    /// </para>
    /// <para>
    /// This is the wait wherever the connect is what answered: off Windows, on a Windows whose
    /// listener table could not be read, and for a port held by something that is not a viewer.
    /// </para>
    /// </summary>
    internal static TimeSpan RecheckUnownedAfter { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a port stands as unowned when it was the listener table that said nobody is
    /// there (<see cref="NothingListening"/>), which is how Windows is asked wherever it can be.
    /// <para>
    /// A second, where the connect's answer stands for ten minutes, because asking again is a
    /// read of the table, a tenth of a millisecond, rather than two seconds waiting to be
    /// refused. So a tray or a viewer started after the test process is told of the settles and
    /// moves that come a second after it is listening, where it was told of none for ten minutes.
    /// </para>
    /// <para>
    /// Not nothing, because a green run settles once per verification, as fast as the tests
    /// pass, and each would then read the table to learn what the one before it had: ten
    /// thousand settles are over a second of that. At a second the reads are one a second however
    /// many settles there are, a ten thousandth of the run. It is the same second an owner that
    /// answered is trusted for (<see cref="TrustOwnerFor"/>), so what was found about a port is
    /// believed for as long whichever way it went.
    /// </para>
    /// </summary>
    internal static TimeSpan RecheckUnlistedAfter { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a port that accepted a connection is connected to again without first asking the
    /// operating system whether anyone is there: see <see cref="NothingListening"/>.
    /// <para>
    /// For a run of settles to an owner that is there, which is a connection every third of a
    /// millisecond. Each renews this, so none of them reads the listener table, which costs as
    /// much as the connect does, and several times that where the listeners cannot be asked for
    /// alone. Short, because a send that comes a while after the last is the one
    /// most likely to find its owner gone, and asking is what spares that send its two seconds.
    /// </para>
    /// </summary>
    internal static TimeSpan TrustOwnerFor { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// What was last found on each port, and when, as a <see cref="Stopwatch"/> timestamp. Per
    /// port because tests talk to ephemeral ports of their own, in parallel, and what happened on
    /// those says nothing about the one live port on a developer machine.
    /// <para>
    /// And how it was found, for a port with nobody on it: by the listener table, which is cheap
    /// to ask again, or by a connect, which is not.
    /// </para>
    /// </summary>
    static ConcurrentDictionary<int, (bool Owned, long At, bool Unlisted)> lastFound = new();

    static bool RecentlyUnowned(int port) =>
        lastFound.TryGetValue(port, out var found) &&
        !found.Owned &&
        Since(found.At) < (found.Unlisted ? RecheckUnlistedAfter : RecheckUnownedAfter);

    static bool AnsweredLately(int port) =>
        lastFound.TryGetValue(port, out var found) &&
        found.Owned &&
        Since(found.At) < TrustOwnerFor;

    static TimeSpan Since(long timestamp) =>
        TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - timestamp) / (double) Stopwatch.Frequency);

    /// <summary>
    /// What was found on a port, reported by everything here that looks whether or not its caller
    /// consulted the memory first. An owner found by a probe or a listing is one every later send
    /// can talk to, and a port any of them found empty is what the memory is for.
    /// </summary>
    static void Found(int port, bool owned) =>
        lastFound[port] = (owned, Stopwatch.GetTimestamp(), false);

    /// <summary>
    /// <see cref="Found"/> for a port the listener table has nobody on, which stands for less
    /// long: see <see cref="RecheckUnlistedAfter"/>.
    /// </summary>
    static void FoundUnlisted(int port) =>
        lastFound[port] = (false, Stopwatch.GetTimestamp(), true);

    /// <summary>
    /// Whether a connect to <paramref name="port"/> would only be a wait to be refused: the
    /// operating system has no listener on the port, so nobody is there to accept one.
    /// <para>
    /// Asked in front of every connect here. The refusal is what was expensive: two seconds for a
    /// send, and half a second each time the launch gate probed, which with nothing to launch was
    /// half a second with the gate held, and again for every poll while a viewer it had started
    /// was still binding. The memory above spares a process all but the first of its telling
    /// sends, and nothing spared it that one, or any probe, or a host that asks.
    /// </para>
    /// <para>
    /// A listener that binds a moment after the table was read is missed, exactly as it is by a
    /// connect a moment early, and is found the same way: nothing that asks is answered from the
    /// memory, so the gate's next poll reads the table again.
    /// </para>
    /// <para>
    /// Only on Windows, which is where the refusal is slow. Elsewhere it arrives at once, so the
    /// connect is the cheaper question as well as the one whose answer cannot be wrong. And not
    /// for a port that accepted a connection within <see cref="TrustOwnerFor"/>, where the table
    /// would cost as much as the connect it stands in front of.
    /// </para>
    /// </summary>
    static bool NothingListening(int port) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
        !AnsweredLately(port) &&
        !ListenerTable.IsHeld(port);

    /// <summary>
    /// Whether the port stands as unowned: nothing was listening when it was last looked at, or
    /// what answered was not a viewer. For a caller whose exchange has just failed and has to tell
    /// an owner that did not answer from there being none, which a failed exchange reports the
    /// same way. The exchange is what makes this current, since every one records what it found.
    /// </summary>
    public static bool FoundUnowned(int? port = null) =>
        RecentlyUnowned(port ?? Port);

    /// <summary>
    /// For tests, which share this process and its memory with every other test's ports.
    /// </summary>
    internal static void ForgetUnowned()
    {
        lastFound.Clear();
        reportedForeign.Clear();
        lock (keptGate)
        {
            DropKept();
        }
    }

    /// <summary>
    /// Ports already reported as held by something that is not a viewer, so the hint is written
    /// once per process rather than once per send.
    /// </summary>
    static ConcurrentDictionary<int, byte> reportedForeign = new();

    /// <summary>
    /// A connection accepted and answered with something that is not this protocol: another
    /// program holds the port. 3493 is IANA's for Network UPS Tools, whose upsd answers every
    /// line it does not understand with an error.
    /// <para>
    /// Remembered as unowned, because for every purpose here it is: nothing on it will ever take
    /// a settle, a move or a delete. Taken for an owner instead, every telling send connected to
    /// it, and nothing said why inline snapshots had stopped reaching a viewer. The hint names
    /// the variable that moves DiffEngine off the port, which is the only fix - a viewer
    /// launched to take the queue cannot bind a port something else holds either.
    /// </para>
    /// <para>
    /// An empty reply is not this. That is an owner that closed without answering, shutting down
    /// or wedged, which is an owner behaving badly rather than no owner at all.
    /// </para>
    /// </summary>
    static void NotAViewer(int port, string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return;
        }

        Found(port, false);
        if (!reportedForeign.TryAdd(port, 0))
        {
            return;
        }

        var first = reply.Split('\n')[0].Trim();
        if (first.Length > 80)
        {
            first = first.Substring(0, 80);
        }

        // Trace rather than Logging, because this file is linked into the viewer too
        Trace.WriteLine(
            $"Port {port} is held by something that is not a DiffEngine viewer: it answered \"{first}\". " +
            "Inline snapshots and pending files cannot reach a viewer there. " +
            $"Set the {PortVariable} environment variable to a free port to move DiffEngine off it.");
    }

    /// <summary>
    /// Whether anything is listening, without sending it anything. For a caller that has just
    /// started a viewer and wants to know when it can be talked to, which a send cannot answer
    /// without also handing over work.
    /// <para>
    /// Always asks, and what it finds corrects the memory behind
    /// <see cref="RecheckUnownedAfter"/>: this is the probe the launch gate runs once a viewer
    /// is started, and the sends queued behind that gate have to reach the viewer it found.
    /// </para>
    /// </summary>
    public static bool IsOwned(int? port = null)
    {
        var endpointPort = port ?? Port;
        if (NothingListening(endpointPort))
        {
            FoundUnlisted(endpointPort);
            return false;
        }

        bool owned;
        try
        {
            using var client = new TcpClient();
            owned = Connect(client, endpointPort, ShortTimeout);
        }
        catch (Exception exception)
            when (Ignorable(exception))
        {
            if (NobodyThere(exception))
            {
                Found(endpointPort, false);
            }

            return false;
        }

        Found(endpointPort, owned);
        return owned;
    }

    /// <summary>
    /// Whether a connect that failed says nobody is on the port, which is the only failure the
    /// memory of an unowned port is for: the port refused it, or never answered.
    /// <para>
    /// A connect also fails when the machine cannot make one at all - it has no ports left to
    /// connect from, or no buffers - and that says nothing of who is listening. Taken for an
    /// empty port, it had every settle and move after it skipped for ten minutes with the owner
    /// still there, on exactly the machine a kept connection was meant to help. Which error a
    /// machine gives when it runs out differs by platform and was never pinned down for Windows,
    /// so this names the two that do mean nobody and takes everything else as not known.
    /// </para>
    /// </summary>
    internal static bool NobodyThere(Exception exception)
    {
        var socket = exception as SocketException ??
                     exception.InnerException as SocketException ??
                     exception.InnerException?.InnerException as SocketException;
        return socket?.SocketErrorCode is SocketError.ConnectionRefused or SocketError.TimedOut;
    }

    /// <summary>
    /// True when the owner acknowledged. A refused connection means nobody owns the queue.
    /// <para>
    /// Every caller of this overload is telling the owner something rather than asking it - a
    /// settle, a retire, a move or a delete to track - so a port recently found unowned is taken
    /// at its word rather than connected to again: see <see cref="RecheckUnownedAfter"/>.
    /// </para>
    /// <para>
    /// And they are the sends there can be thousands of, a settle for every passing inline
    /// verification, so they go down one connection where the owner keeps one: see
    /// <see cref="ViewerServer.Keep"/>. The first is an ordinary exchange, whose reply says
    /// whether the owner does, and so is every one to an owner that predates it.
    /// </para>
    /// </summary>
    public static bool TrySend(ViewerMessage message) =>
        Tell(message, Port);

    /// <summary>
    /// <see cref="TrySend(ViewerMessage)"/> with the port given, for the tests, which use a port
    /// of their own rather than change what <see cref="Port"/> reads for everything beside them.
    /// </summary>
    internal static bool Tell(ViewerMessage message, int port)
    {
        switch (SendKept(message, port, out var ok))
        {
            case KeptSend.Answered:
                return ok;
            case KeptSend.Unanswered:
                return false;
        }

        if (!Exchange(message, out var response, out var keeps, port, null, skipIfUnowned: true))
        {
            return false;
        }

        if (keeps)
        {
            KeepConnection(port);
        }

        return response.Ok;
    }

    enum KeptSend
    {
        /// <summary>
        /// No connection is kept to that port, or the one that was has gone, as it does when its
        /// owner exits. The ordinary exchange is what finds out who is there now.
        /// </summary>
        NotKept,

        Answered,

        /// <summary>
        /// Sent, and not answered inside the timeout: an owner that is there and busy, which the
        /// ordinary exchange would only wait on for as long again.
        /// </summary>
        Unanswered
    }

    sealed class KeptConnection(TcpClient client, int port) :
        IDisposable
    {
        public int Port { get; } = port;
        public NetworkStream Stream { get; } = client.GetStream();
        public StreamReader Reader { get; } = new(client.GetStream(), Encoding.UTF8);

        public void Dispose() =>
            client.Close();
    }

    /// <summary>
    /// Held for the whole of an exchange on the kept connection, which is one request and its
    /// answer at a time. A parallel run's settles take turns at it, each for about as long as
    /// the owner takes to answer.
    /// </summary>
    static readonly object keptGate = new();

    static KeptConnection? kept;

    static KeptSend SendKept(ViewerMessage message, int port, out bool ok)
    {
        ok = false;
        lock (keptGate)
        {
            if (kept is null)
            {
                return KeptSend.NotKept;
            }

            if (kept.Port != port)
            {
                DropKept();
                return KeptSend.NotKept;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes($"{message.Build()}\n");
                kept.Stream.Write(bytes, 0, bytes.Length);
                kept.Stream.Flush();
                var reply = new StringBuilder();
                string? line;
                while ((line = kept.Reader.ReadLine()) is { Length: > 0 })
                {
                    reply.Append(line);
                    reply.Append('\n');
                }

                if (line is not null &&
                    ViewerResponse.TryParse(reply.ToString(), out var response))
                {
                    Found(port, true);
                    ok = response.Ok;
                    return KeptSend.Answered;
                }

                // Closed by the owner, which is an owner that stopped or exited since the last
                // send. Whoever holds the port now is for the ordinary exchange to find
                DropKept();
                return KeptSend.NotKept;
            }
            catch (Exception exception)
                when (Ignorable(exception))
            {
                DropKept();
                return TimedOut(exception) ? KeptSend.Unanswered : KeptSend.NotKept;
            }
        }
    }

    static bool TimedOut(Exception exception) =>
        exception is IOException
        {
            InnerException: SocketException
            {
                SocketErrorCode: SocketError.TimedOut
            }
        };

    /// <summary>
    /// Opens the connection the telling sends after this one go down, to an owner whose reply
    /// has just said it keeps one. Failing to is nothing: the next send is an ordinary exchange,
    /// and tries again on the strength of its own reply.
    /// </summary>
    static void KeepConnection(int port)
    {
        lock (keptGate)
        {
            if (kept is not null)
            {
                if (kept.Port == port)
                {
                    return;
                }

                DropKept();
            }

            var client = new TcpClient();
            try
            {
                if (!Connect(client, port, ShortTimeout))
                {
                    client.Close();
                    return;
                }

                Configure(client, timeout);
                // Each request is one small write waiting on one small answer, which is the
                // pattern Nagle's algorithm holds back
                client.NoDelay = true;
                var connection = new KeptConnection(client, port);
                var bytes = Encoding.UTF8.GetBytes($"{ViewerServer.Keep}\n");
                connection.Stream.Write(bytes, 0, bytes.Length);
                connection.Stream.Flush();
                kept = connection;
            }
            catch (Exception exception)
                when (Ignorable(exception))
            {
                client.Close();
            }
        }
    }

    static void DropKept()
    {
        kept?.Dispose();
        kept = null;
    }

    /// <summary>
    /// True when a reply arrived and parsed, whatever it says. Callers that need the body, such as
    /// the tray listing pending snapshots, use this rather than <see cref="TrySend(ViewerMessage)"/>.
    /// <para>
    /// <paramref name="port"/> overrides <see cref="Port"/> for a single call. Tests pass their own
    /// ephemeral port rather than mutating anything static, so they can run in parallel.
    /// </para>
    /// <para>
    /// <paramref name="skipIfUnowned"/> lets a port found unowned within
    /// <see cref="RecheckUnownedAfter"/> answer for itself, with no connect. Off by default,
    /// because a host or a review surface asks precisely so that it notices an owner arriving,
    /// and a listing answered from what was found ten minutes ago is a listing that misses one.
    /// The library's own sends pass true.
    /// </para>
    /// </summary>
    public static bool TrySend(
        ViewerMessage message,
        [NotNullWhen(true)] out ViewerResponse? response,
        int? port = null,
        TimeSpan? wait = null,
        bool skipIfUnowned = false) =>
        Exchange(message, out response, out _, port, wait, skipIfUnowned);

    /// <summary>
    /// The ordinary exchange: a connection of its own, the request ended by closing the sending
    /// half, and the reply read until the owner closes. <paramref name="keeps"/> is whether the
    /// reply said its owner would take requests one after another on a connection that stays
    /// open, which is <see cref="ViewerServer.Keeps"/>.
    /// </summary>
    static bool Exchange(
        ViewerMessage message,
        [NotNullWhen(true)] out ViewerResponse? response,
        out bool keeps,
        int? port,
        TimeSpan? wait,
        bool skipIfUnowned)
    {
        response = null;
        keeps = false;
        var endpointPort = port ?? Port;
        if (skipIfUnowned &&
            RecentlyUnowned(endpointPort))
        {
            return false;
        }

        if (NothingListening(endpointPort))
        {
            FoundUnlisted(endpointPort);
            return false;
        }

        var deadline = wait ?? timeout;
        var connected = false;
        try
        {
            using var client = new TcpClient();
            if (!Connect(client, endpointPort, deadline))
            {
                Found(endpointPort, false);
                return false;
            }

            connected = true;
            Found(endpointPort, true);
            Configure(client, deadline);
            var stream = client.GetStream();
            var bytes = Encoding.UTF8.GetBytes(message.Build());
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            HalfClose(client);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            if (ViewerResponse.TryParse(text, out response))
            {
                // A line of its own, and the last: nothing a field holds can look like it, since
                // whatever could hold a line break is base64
                keeps = text.EndsWith($"\n{ViewerServer.Keeps}\n", StringComparison.Ordinal);
                return true;
            }

            NotAViewer(endpointPort, text);
            return false;
        }
        catch (Exception exception)
            when (Ignorable(exception))
        {
            // Only a connect that was refused says the port is unowned. A connection that was
            // accepted and then torn down is an owner behaving badly, and a connect the machine
            // could not make is no answer about the port, neither of which the memory records
            if (!connected &&
                NobodyThere(exception))
            {
                Found(endpointPort, false);
            }

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
        // Telling rather than asking, as the synchronous bool overload is, and so answered from
        // the memory of an unowned port the same way
        await SendAsync(message, cancel, port, wait, skipIfUnowned: true) == SendOutcome.Accepted;

    /// <summary>
    /// As <see cref="TrySendAsync" />, but says which of the two failures happened. A caller that
    /// would launch a viewer on absence needs that: launching one because the owner refused the
    /// payload leaves two processes and still no snapshot.
    /// <para>
    /// <paramref name="skipIfUnowned"/> is as on the synchronous overload: a port found unowned
    /// within <see cref="RecheckUnownedAfter"/> reports <see cref="SendOutcome.NoOwner"/> with no
    /// connect, which suits a caller about to probe for itself through the launch gate.
    /// </para>
    /// </summary>
    public static async Task<SendOutcome> SendAsync(
        ViewerMessage message,
        Cancel cancel,
        int? port = null,
        TimeSpan? wait = null,
        bool skipIfUnowned = false)
    {
        // Said here, before anything is made. Left to the connect, a send already cancelled was
        // a client closed by the abort below before it was ever connected, and on the modern
        // frameworks what that threw was read as a port with nobody on it
        cancel.ThrowIfCancellationRequested();
        var endpointPort = port ?? Port;
        if (skipIfUnowned &&
            RecentlyUnowned(endpointPort))
        {
            return SendOutcome.NoOwner;
        }

        if (NothingListening(endpointPort))
        {
            FoundUnlisted(endpointPort);
            return SendOutcome.NoOwner;
        }

        var timeToWait = wait ?? asyncTimeout;
        using var deadline = CancelSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(timeToWait);
        var token = deadline.Token;
        var connected = false;
        try
        {
            using var client = new TcpClient();
            // Closing the socket is the only thing that unblocks every framework: the pre-net7
            // ReadToEndAsync takes no token at all, and net462 has no cancellable connect or
            // write either. Registered after the client and so disposed before it, which is what
            // stops the callback firing on a disposed object
            // ReSharper disable once UseAwaitUsing
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
            connected = true;
            Found(endpointPort, true);
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
                NotAViewer(endpointPort, text);
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
            if (!connected)
            {
                Found(endpointPort, false);
            }

            // Trace rather than Logging, because this file is linked into the viewer too
            Trace.WriteLine(
                $"Timed out after {timeToWait} waiting for the inline queue owner on port {endpointPort}. " +
                $"Verb: {message.Verb}. " +
                (connected ? "The owner is present but unresponsive. " : "Nothing accepted the connection. ") +
                exception.GetType().Name);
            return SendOutcome.NoOwner;
        }
        // The caller cancelling, which reaches the exchange as its socket closing under it, and
        // so as whatever a closed socket throws where the call in hand takes no token: all of
        // them on .NET Framework, the read before net7. It says nothing about the port. Taken
        // for a connect that failed, it was remembered as unowned with the owner still
        // listening, and every settle and move after it went unsent until the memory ran out
        catch (Exception exception)
            when (cancel.IsCancellationRequested &&
                  exception is not OperationCanceledException &&
                  Ignorable(exception))
        {
            throw new OperationCanceledException(
                $"The send to the inline queue owner on port {endpointPort} was cancelled.",
                exception,
                cancel);
        }
        // Cancellation is the caller's business; a missing owner is not.
        catch (Exception exception)
            when (exception is not OperationCanceledException && Ignorable(exception))
        {
            // As on the synchronous overload: a connect that was refused is an unowned port, and
            // neither an accepted connection that fell over afterwards nor a connect the machine
            // could not make is
            if (!connected &&
                NobodyThere(exception))
            {
                Found(endpointPort, false);
            }

            return SendOutcome.NoOwner;
        }
    }

    /// <summary>
    /// A connect waited on for at most <paramref name="wait"/>. One given up on is still pending
    /// when the caller disposes the client, and faults once that tears it down - or, where the
    /// port refuses rather than hangs, when the refusal arrives - with nobody left to observe it.
    /// The finalizer then reported it as an unobserved task exception: once per probe the launch
    /// gate made while a viewer it had just started was still binding, in a test process that may
    /// treat those as fatal. Observed here instead, since there is nothing to do with the fault.
    /// </summary>
    static bool Connect(TcpClient client, int port, TimeSpan wait)
    {
        var connecting = client.ConnectAsync(IPAddress.Loopback, port);
        if (connecting.Wait(wait))
        {
            return true;
        }

        connecting.ContinueWith(
            static _ => _.Exception,
            Cancel.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return false;
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
