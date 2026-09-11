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
    // rather than a copy of it kept here
    static readonly TimeSpan recheckUnownedAfter = ViewerClient.RecheckUnownedAfter;

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
}
