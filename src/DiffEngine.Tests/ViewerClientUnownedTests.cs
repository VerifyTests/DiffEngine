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

    [Before(Class)]
    public static void Remember() =>
        recheckUnownedAfter = ViewerClient.RecheckUnownedAfter;

    [Before(Test)]
    public void Forget() =>
        ViewerClient.ForgetUnowned();

    [After(Test)]
    public void Restore()
    {
        ViewerClient.RecheckUnownedAfter = recheckUnownedAfter;
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
        var port = FreePort();
        await Assert.That(ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true)).IsFalse();

        using var owner = new Owner(port);
        await Assert.That(ViewerClient.TrySend(settle, out _, port, skipIfUnowned: true)).IsTrue();
        await Assert.That(owner.Heard.Count).IsEqualTo(1);
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
                    return Task.FromResult(true);
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
