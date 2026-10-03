/// <summary>
/// The memory of a port nothing was listening on, which is what stops a green run paying for a
/// refused connection once per inline verification.
/// <para>
/// A refused loopback connection is instant on most machines and two seconds on Windows with the
/// firewall's stealth mode on, which is the default. Each test here meets one refusal at most, on
/// an ephemeral port of its own, so it costs that once and never talks to the live viewer port.
/// </para>
/// </summary>
[NotInParallel]
public class ViewerClientUnownedTests
{
    static readonly ViewerMessage settle = new(ViewerVerb.Settle, InlineKey.For("Tests.cs", 1));

    // Read before any test can have changed it, so the restore below puts back the real default
    // rather than a copy of it kept here. A hook rather than a static field initializer: with no
    // static constructor that runs on first touching a static field, and TheMemoryExpires sets the
    // value before it touches one, so running first it captured Zero for every test after it.
    static TimeSpan recheckUnownedAfter;
    static TimeSpan recheckUnlistedAfter;

    [Before(Class)]
    public static void Remember()
    {
        recheckUnownedAfter = ViewerClient.RecheckUnownedAfter;
        recheckUnlistedAfter = ViewerClient.RecheckUnlistedAfter;
    }

    /// <summary>
    /// What the listener table found stands for a second, which a loaded machine can spend
    /// between two lines of a test. Most of these are about there being a memory at all, so for
    /// them it stands as long as a connect's answer does, and the ones about how long say so.
    /// </summary>
    [Before(Test)]
    public void Forget()
    {
        ViewerClient.RecheckUnlistedAfter = recheckUnownedAfter;
        ViewerClient.ForgetUnowned();
    }

    [After(Test)]
    public void Restore()
    {
        ViewerClient.RecheckUnownedAfter = recheckUnownedAfter;
        ViewerClient.RecheckUnlistedAfter = recheckUnlistedAfter;
        ViewerClient.ForgetUnowned();
    }

    /// <summary>
    /// The first refusal is remembered, so the next telling send returns without connecting - and
    /// keeps returning that way once somebody is there, until something asks.
    /// </summary>
    [Test]
    public async Task ARefusalIsRemembered()
    {
        var port = FreePort();
        await Assert.That(ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true)).IsFalse();

        using var owner = new Owner(port);
        await Assert.That(ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true)).IsFalse();
        await Assert.That(owner.Heard).IsEmpty();
    }

    /// <summary>
    /// An ask always connects, and having found the owner, corrects the memory for the sends
    /// after it.
    /// </summary>
    [Test]
    public async Task AnAskCorrectsTheMemory()
    {
        var port = FreePort();
        ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true);
        using var owner = new Owner(port);

        await Assert.That(ViewerClient.TrySend(settle, out _, port)).IsTrue();
        await Assert.That(ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true)).IsTrue();
        await Assert.That(owner.Heard.Count).IsEqualTo(2);
    }

    /// <summary>
    /// The probe is the ask that matters most. The launch gate runs it once a viewer is started,
    /// and the sends queued behind the gate have to reach the viewer it found.
    /// </summary>
    [Test]
    public async Task TheProbeCorrectsTheMemory()
    {
        var port = FreePort();
        ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true);
        using var owner = new Owner(port);

        await Assert.That(ViewerClient.IsOwned(port)).IsTrue();
        await Assert.That(ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true)).IsTrue();
        await Assert.That(owner.Heard.Count).IsEqualTo(1);
    }

    /// <summary>
    /// A probe finding nobody is remembered too. The gate probes once per launch, and a viewer
    /// that never binds would otherwise leave every send in the run paying for the refusal.
    /// </summary>
    [Test]
    public async Task AProbeFindingNobodyIsRemembered()
    {
        var port = FreePort();
        await Assert.That(ViewerClient.IsOwned(port)).IsFalse();

        using var owner = new Owner(port);
        await Assert.That(ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true)).IsFalse();
        await Assert.That(owner.Heard).IsEmpty();
    }

    /// <summary>
    /// A tray started mid run is found once the memory has expired, without anything asking.
    /// </summary>
    [Test]
    public async Task TheMemoryExpires()
    {
        ViewerClient.RecheckUnownedAfter = TimeSpan.Zero;
        ViewerClient.RecheckUnlistedAfter = TimeSpan.Zero;
        var port = FreePort();
        await Assert.That(ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true)).IsFalse();

        using var owner = new Owner(port);
        await Assert.That(ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true)).IsTrue();
        await Assert.That(owner.Heard.Count).IsEqualTo(1);
    }

    /// <summary>
    /// A tray started after the test process, with everything as shipped: the telling sends find
    /// it within moments, with nothing asking on their behalf. Where the listener table is what
    /// said the port was empty, asking it again costs a tenth of a millisecond, so there is no
    /// reason to go ten minutes on the old answer - which is how long the tray used to hear
    /// nothing of the run's settles and moves.
    /// <para>
    /// Waited for rather than timed. What is held to is that it happens while a test would still
    /// be running, and the limit is far short of the ten minutes it replaces.
    /// </para>
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task AnOwnerStartedLaterIsFoundByTheTellingSends()
    {
        ViewerClient.RecheckUnlistedAfter = recheckUnlistedAfter;
        var port = FreePort();
        await Assert.That(ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true)).IsFalse();

        using var owner = new Owner(port);
        var elapsed = Stopwatch.StartNew();
        var found = false;
        while (!found &&
               elapsed.Elapsed < TimeSpan.FromSeconds(60))
        {
            found = ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true);
            if (!found)
            {
                await Task.Delay(50);
            }
        }

        await Assert.That(found).IsTrue();
    }

    /// <summary>
    /// The shorter wait is only for what the table said. A port a connect found nobody on - here
    /// because the table could not be read - is one where asking again is the connect again, two
    /// seconds of it on Windows, so that answer stands as it always did.
    /// </summary>
    [Test]
    public async Task APortAConnectFoundEmptyIsNotAskedAboutAgainSoSoon()
    {
        ViewerClient.RecheckUnlistedAfter = TimeSpan.Zero;
        var port = FreePort();
        using var unreadable = new Lookup(port)
        {
            Unreadable = true
        };
        await Assert.That(ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true)).IsFalse();

        using var owner = new Owner(port);
        await Assert.That(ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true)).IsFalse();
        await Assert.That(owner.Heard).IsEmpty();
    }

    /// <summary>
    /// Per port, or a test suite's dead ephemeral port would silence the sends to its live one.
    /// </summary>
    [Test]
    public async Task PortsAreRememberedApart()
    {
        var dead = FreePort();
        using var owner = new Owner();
        await Assert.That(ViewerClient.TrySend(settle, out _, dead, skipIfUnowned: true)).IsFalse();

        await Assert.That(ViewerClient.TrySend(settle, out _, owner.Port, skipIfUnowned: true)).IsTrue();
        await Assert.That(owner.Heard.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TheAsyncSendRemembersToo()
    {
        var port = FreePort();
        await Assert.That(await ViewerClient.SendAsync(settle, Cancel.None, port, skipIfUnowned: true))
            .IsEqualTo(SendOutcome.NoOwner);

        using var owner = new Owner(port);
        await Assert.That(await ViewerClient.SendAsync(settle, Cancel.None, port, skipIfUnowned: true))
            .IsEqualTo(SendOutcome.NoOwner);
        await Assert.That(owner.Heard).IsEmpty();

        await Assert.That(ViewerClient.IsOwned(port)).IsTrue();
        await Assert.That(await ViewerClient.SendAsync(settle, Cancel.None, port, skipIfUnowned: true))
            .IsEqualTo(SendOutcome.Accepted);
    }

    /// <summary>
    /// A caller that does not say is asking, and an ask is never answered from memory. This is
    /// what keeps the hosts and the IDE plugin, which share this client, noticing an owner arrive.
    /// </summary>
    [Test]
    public async Task AskingIsTheDefault()
    {
        var port = FreePort();
        ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true);
        using var owner = new Owner(port);

        await Assert.That(await ViewerClient.SendAsync(settle, Cancel.None, port)).IsEqualTo(SendOutcome.Accepted);
        await Assert.That(ViewerClient.TrySend(settle, out _, port)).IsTrue();
        await Assert.That(owner.Heard.Count).IsEqualTo(2);
    }

    /// <summary>
    /// The first telling send of a process meets the refusal with nothing remembered, and paid for
    /// it: two seconds, once per test process, and again each time the memory ran out. The
    /// operating system knows nobody is listening without anything being connected, so where that
    /// refusal is slow it is asked first.
    /// <para>
    /// A bound of half what the connect takes to be refused, around something that takes well
    /// under a millisecond.
    /// </para>
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task APortNobodyHoldsIsNotWaitedOn()
    {
        var port = FreePort();

        var elapsed = Stopwatch.StartNew();
        var sent = ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true);
        elapsed.Stop();

        await Assert.That(sent).IsFalse();
        await Assert.That(elapsed.Elapsed).IsLessThan(TimeSpan.FromSeconds(1));
        await Assert.That(ViewerClient.FoundUnowned(port)).IsTrue();
    }

    /// <inheritdoc cref="APortNobodyHoldsIsNotWaitedOn" />
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task APortNobodyHoldsIsNotWaitedOnAsync()
    {
        var port = FreePort();

        var elapsed = Stopwatch.StartNew();
        var outcome = await ViewerClient.SendAsync(settle, Cancel.None, port, skipIfUnowned: true);
        elapsed.Stop();

        await Assert.That(outcome).IsEqualTo(SendOutcome.NoOwner);
        await Assert.That(elapsed.Elapsed).IsLessThan(TimeSpan.FromSeconds(1));
        await Assert.That(ViewerClient.FoundUnowned(port)).IsTrue();
    }

    /// <summary>
    /// The table is a way of not waiting, never the authority. Where it cannot be read the
    /// connect answers, as it did before anything asked the table: an owner is still found, and
    /// a port with nobody on it is still reported that way.
    /// </summary>
    [Test]
    public async Task ATableThatCannotBeReadLeavesTheConnectToAnswer()
    {
        using var owner = new Owner();
        var dead = FreePort();
        using var unreadable = new Lookup(owner.Port, dead)
        {
            Unreadable = true
        };

        await Assert.That(ViewerClient.IsOwned(owner.Port)).IsTrue();
        await Assert.That(ViewerClient.TrySend(settle, out _, owner.Port, skipIfUnowned: true)).IsTrue();
        await Assert.That(ViewerClient.IsOwned(dead)).IsFalse();
    }

    /// <summary>
    /// Reading the table costs more than the connect it stands in front of, and a green run with a
    /// tray answering settles thousands of times. So a port that accepted a connection a moment
    /// ago is connected to again without asking, and only one that has been quiet is looked up.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task AnOwnerThatJustAnsweredIsNotLookedUpAgain()
    {
        var trustOwnerFor = ViewerClient.TrustOwnerFor;
        using var owner = new Owner();
        using var lookup = new Lookup(owner.Port);
        try
        {
            // Long enough that nothing this machine is doing can run it out between two sends
            ViewerClient.TrustOwnerFor = TimeSpan.FromMinutes(1);

            // Nothing known about the port yet, so this one asks
            await Assert.That(ViewerClient.TrySend(settle, out _, owner.Port, skipIfUnowned: true)).IsTrue();
            for (var index = 0; index < 3; index++)
            {
                await Assert.That(ViewerClient.TrySend(settle, out _, owner.Port, skipIfUnowned: true)).IsTrue();
            }

            await Assert.That(ViewerClient.IsOwned(owner.Port)).IsTrue();
            await Assert.That(lookup.Asked).IsEqualTo(1);

            ViewerClient.TrustOwnerFor = TimeSpan.Zero;
            await Assert.That(ViewerClient.TrySend(settle, out _, owner.Port, skipIfUnowned: true)).IsTrue();
            await Assert.That(lookup.Asked).IsEqualTo(2);
            await Assert.That(owner.Heard.Count).IsEqualTo(5);
        }
        finally
        {
            ViewerClient.TrustOwnerFor = trustOwnerFor;
        }
    }

    /// <summary>
    /// A send the caller cancelled says nothing about the port. Cancelling closes the socket, the
    /// one thing that unblocks every framework, and what the closed socket threw was read as a
    /// connect that failed: the port was remembered as unowned, with its owner listening, and
    /// every settle and move after it went unsent for ten minutes.
    /// </summary>
    [Test]
    public async Task ASendCancelledBeforeItStartsSaysNothingAboutThePort()
    {
        using var owner = new Owner();
        using var cancel = new CancelSource();
        cancel.Cancel();

        await Assert.That(async () => await ViewerClient.SendAsync(settle, cancel.Token, owner.Port))
            .Throws<OperationCanceledException>();

        await Assert.That(ViewerClient.FoundUnowned(owner.Port)).IsFalse();
        await Assert.That(ViewerClient.TrySend(settle, out _, owner.Port, skipIfUnowned: true)).IsTrue();
        await Assert.That(owner.Heard.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The same while the connect is still out. A port that is bound and not listening is the
    /// connect that goes unanswered: Windows spends two seconds on it, which the cancel arrives
    /// inside. The table is made unreadable so that the connect is reached at all.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task ASendCancelledWhileConnectingSaysNothingAboutThePort()
    {
        using var holder = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true
        };
        holder.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint) holder.LocalEndPoint!).Port;
        using var unreadable = new Lookup(port)
        {
            Unreadable = true
        };
        using var cancel = new CancelSource(TimeSpan.FromMilliseconds(200));

        await Assert.That(async () => await ViewerClient.SendAsync(settle, cancel.Token, port))
            .Throws<OperationCanceledException>();

        await Assert.That(ViewerClient.FoundUnowned(port)).IsFalse();
    }

    /// <summary>
    /// Stands in front of the listener table for the ports a test names, counting how often each
    /// was asked about and, when told to, failing the way a table that cannot be read does. Every
    /// other port goes through untouched, since the tests beside this one are asking about theirs
    /// at the same time.
    /// </summary>
    sealed class Lookup : IDisposable
    {
        readonly Func<int, bool> previous = ListenerTable.Lookup;
        readonly int[] ports;
        int asked;

        public Lookup(params int[] ports)
        {
            this.ports = ports;
            ListenerTable.Lookup = Answer;
        }

        public bool Unreadable { get; init; }

        public int Asked => Volatile.Read(ref asked);

        bool Answer(int port)
        {
            if (!ports.Contains(port))
            {
                return previous(port);
            }

            Interlocked.Increment(ref asked);
            if (Unreadable)
            {
                throw new System.Net.NetworkInformation.NetworkInformationException();
            }

            return previous(port);
        }

        public void Dispose() =>
            ListenerTable.Lookup = previous;
    }

    /// <summary>
    /// A port that is free right now, found by binding and releasing it.
    /// </summary>
    static int FreePort()
    {
        if (!ViewerServer.TryBind(0, out var bound))
        {
            throw new("Could not bind an ephemeral port.");
        }

        var port = bound.Port;
        bound.Dispose();
        return port;
    }

    /// <summary>
    /// A queue owner on a port of the test's choosing, answering every verb and recording it.
    /// Zero binds an ephemeral port, for a test that only needs somebody to be there.
    /// </summary>
    sealed class Owner : IDisposable
    {
        readonly ViewerServer server;
        readonly CancelSource cancel = new();
        readonly Task listening;
        readonly Lock gate = new();
        readonly List<ViewerVerb> heard = [];

        public Owner(int port = 0)
        {
            if (!ViewerServer.TryBind(port, out var bound))
            {
                throw new($"Could not bind port {port}.");
            }

            server = bound;
            listening = server.Listen(
                _ =>
                {
                    lock (gate)
                    {
                        heard.Add(_.Verb);
                    }

                    return ViewerResponse.Success();
                },
                cancel.Token);
        }

        public int Port => server.Port;

        public IReadOnlyList<ViewerVerb> Heard
        {
            get
            {
                lock (gate)
                {
                    return heard.ToList();
                }
            }
        }

        public void Dispose()
        {
            cancel.Cancel();
            server.Dispose();
            try
            {
                listening.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Cancellation unwinding through the listener; nothing to report
            }

            cancel.Dispose();
        }
    }

    /// <summary>
    /// Network UPS Tools' upsd holds 3493, which IANA assigns it. It answers every line it does
    /// not understand with an error and closes once the client has finished sending, which is
    /// what this does. The composition is AddInlineAsync's, with the port made explicit and the
    /// launch observed rather than performed.
    /// </summary>
    [Test]
    public async Task ANonViewerOnThePortIsReportedRatherThanTakenForAnOwner()
    {
        ViewerClient.ForgetUnowned();
        using var upsd = new FakeUpsd();
        var port = upsd.Port;
        var trace = new CapturingListener();
        Trace.Listeners.Add(trace);
        try
        {
            var settle = new ViewerMessage(ViewerVerb.Settle, InlineKey.For("Tests.cs", 1));
            for (var index = 0; index < 5; index++)
            {
                ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true);
            }

            var settleConnections = upsd.Connections;

            var patch = new InlinePatch(Path.Combine(Path.GetTempPath(), "ViewerClientNoProject", "Tests.cs"), 1, "\"old\"", "new")
            {
                TestName = "Tests.Method"
            };
            var payload = InlinePatchFile.Build(patch, "net10.0");
            var inline = new ViewerMessage(ViewerVerb.Inline, Body: payload);
            var sent = await ViewerClient.SendAsync(inline, Cancel.None, port, skipIfUnowned: true);
            var launches = 0;
            var gated = ViewerLaunchGate.LaunchAsync(
                async () => await ViewerClient.SendAsync(inline, Cancel.None, port) == SendOutcome.Accepted,
                () =>
                {
                    launches++;
                    return Task.FromResult<Process?>(null);
                },
                Cancel.None,
                isOwned: () => ViewerClient.IsOwned(port));
            var first = await Task.WhenAny(gated, Task.Delay(TimeSpan.FromSeconds(30)));
            await Assert.That(first == gated).IsTrue();
            var outcome = await gated;

            Console.WriteLine($"settle connections: {settleConnections}, inline send: {sent}, gate: {outcome}, launches: {launches}, result: {DiffRunner.InlineResultFor(outcome)}");

            using (Assert.Multiple())
            {
                // Either a hint that names the variable to move off the port...
                await Assert.That(trace.Text).Contains(ViewerClient.PortVariable);
                // ...or at least not paying a connect per settle for a port with no viewer on it
                await Assert.That(settleConnections).IsEqualTo(1);
            }
        }
        finally
        {
            Trace.Listeners.Remove(trace);
            ViewerClient.ForgetUnowned();
        }
    }

    sealed class CapturingListener : TraceListener
    {
        readonly StringBuilder builder = new();

        public string Text
        {
            get
            {
                lock (builder)
                {
                    return builder.ToString();
                }
            }
        }

        public override void Write(string? message)
        {
            lock (builder)
            {
                builder.Append(message);
            }
        }

        public override void WriteLine(string? message)
        {
            lock (builder)
            {
                builder.AppendLine(message);
            }
        }
    }

    /// <summary>
    /// What upsd does with a client that is not speaking its protocol: one error per line, and the
    /// connection closed at end of input. One connection at a time, as its select loop is.
    /// </summary>
    sealed class FakeUpsd : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        int connections;

        public FakeUpsd()
        {
            listener.Start();
            new Thread(Serve)
            {
                IsBackground = true
            }.Start();
        }

        public int Port => ((IPEndPoint) listener.LocalEndpoint).Port;

        public int Connections => Volatile.Read(ref connections);

        void Serve()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (Exception exception)
                    when (exception is SocketException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                Interlocked.Increment(ref connections);
                using (client)
                {
                    try
                    {
                        var stream = client.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII);
                        using var writer = new StreamWriter(stream, Encoding.ASCII);
                        writer.NewLine = "\n";
                        writer.AutoFlush = true;
                        while (reader.ReadLine() != null)
                        {
                            writer.WriteLine("ERR UNKNOWN-COMMAND");
                        }
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        public void Dispose() =>
            listener.Stop();
    }
}
