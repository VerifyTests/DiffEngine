/// <summary>
/// The connection the telling sends share, where the owner keeps one.
/// <para>
/// A settle was a connection of its own, and the client is the side that closes first, so each
/// left a port in TIME_WAIT: one a passing inline verification, for two minutes, out of the
/// 16,000 or so Windows gives out. What is asserted here is the count of connections an owner
/// accepted, and that nothing a send used to do is lost to sharing one: an owner that predates
/// it is still sent a connection each, and an owner that goes is still found gone.
/// </para>
/// </summary>
// The connection is one for the process, as the memory beside it is
[NotInParallel]
public class KeptConnectionTests
{
    static readonly ViewerMessage settle = new(ViewerVerb.Settle, InlineKey.For("Tests.cs", 1));

    [Before(Test)]
    public void Forget() =>
        ViewerClient.ForgetUnowned();

    [After(Test)]
    public void Restore() =>
        ViewerClient.ForgetUnowned();

    /// <summary>
    /// The first is an ordinary exchange, since its reply is what says the owner keeps one. The
    /// second connection is the kept one, and everything after goes down it.
    /// </summary>
    [Test]
    public async Task SendsAfterTheFirstShareAConnection()
    {
        using var owner = new Owner();

        for (var index = 0; index < 20; index++)
        {
            await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();
        }

        await Assert.That(owner.Heard.Count).IsEqualTo(20);
        await Assert.That(owner.Accepted).IsEqualTo(2);
    }

    /// <summary>
    /// What the owner said is still what the send reports, a refusal included.
    /// </summary>
    [Test]
    public async Task ARefusalComesBackDownTheKeptConnection()
    {
        using var owner = new Owner(_ => _.Key == "refuse" ? ViewerResponse.Error("no") : ViewerResponse.Success());

        await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();
        await Assert.That(ViewerClient.Tell(new(ViewerVerb.Settle, "refuse"), owner.Port)).IsFalse();
        await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();

        await Assert.That(owner.Accepted).IsEqualTo(2);
    }

    /// <summary>
    /// Everything a message carries crosses the kept connection as it crosses its own.
    /// </summary>
    [Test]
    public async Task AMessageArrivesWhole()
    {
        using var owner = new Owner();
        var message = new ViewerMessage(ViewerVerb.Settle, "key\nwith a break", "net10.0", "Member", "a value\n\nwith an empty line");

        ViewerClient.Tell(settle, owner.Port);
        await Assert.That(ViewerClient.Tell(message, owner.Port)).IsTrue();

        await Assert.That(owner.Heard[1]).IsEqualTo(message);
    }

    /// <summary>
    /// A parallel run's settles take turns at the one connection, and each is answered with its
    /// own answer. Several may go out as ordinary exchanges before the first of them has opened
    /// it, so the bound is on the order of the threads rather than of the sends.
    /// </summary>
    [Test]
    public async Task SendsFromManyThreadsAreEachAnswered()
    {
        using var owner = new Owner(_ => _.Key!.EndsWith("odd") ? ViewerResponse.Error("odd") : ViewerResponse.Success());
        var wrong = 0;

        // Threads of their own rather than the pool's. A send blocks its thread until it is
        // answered, and this owner answers from the pool, being in the same process: on a machine
        // of two or four cores eight blocked senders were every thread the pool had, the owner
        // could not answer, and a send timed out. A real owner is another process.
        var threads = Enumerable.Range(0, 8)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    for (var index = 0; index < 50; index++)
                    {
                        var odd = (_ + index) % 2 == 1;
                        var sent = ViewerClient.Tell(new(ViewerVerb.Settle, odd ? "odd" : "even"), owner.Port);
                        if (sent == odd)
                        {
                            Interlocked.Increment(ref wrong);
                        }
                    }
                },
                Cancel.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default));
        await Task.WhenAll(threads);

        await Assert.That(wrong).IsEqualTo(0);
        await Assert.That(owner.Heard.Count).IsEqualTo(400);
        await Assert.That(owner.Accepted).IsLessThan(20);
    }

    /// <summary>
    /// An owner from before any of this reads a request until its sender closes, so it cannot be
    /// kept a connection, and is not: its replies do not say it keeps one.
    /// </summary>
    [Test]
    public async Task AnOwnerThatPredatesItIsSentAConnectionEach()
    {
        using var owner = new OlderOwner();

        for (var index = 0; index < 5; index++)
        {
            await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();
        }

        var requests = owner.Requests;
        await Assert.That(requests.Count).IsEqualTo(5);
        foreach (var request in requests)
        {
            await Assert.That(request).IsEqualTo(settle.Build());
        }
    }

    /// <summary>
    /// And a client from before it reads past the line that says so, as it reads past any name
    /// it does not know.
    /// </summary>
    [Test]
    public async Task AnOlderClientReadsPastTheLine()
    {
        var text = $"{ViewerResponse.Success("done").Build()}{ViewerServer.Keeps}\n";

        await Assert.That(ViewerResponse.TryParse(text, out var response)).IsTrue();
        await Assert.That(response!.Ok).IsTrue();
        await Assert.That(response.Message).IsEqualTo("done");
    }

    /// <summary>
    /// An owner that stops closes what it kept, so the next send finds the port as it is now:
    /// with nobody on it.
    /// </summary>
    [Test]
    public async Task AnOwnerThatHasGoneIsFoundGone()
    {
        int port;
        using (var owner = new Owner())
        {
            port = owner.Port;
            ViewerClient.Tell(settle, port);
            await Assert.That(ViewerClient.Tell(settle, port)).IsTrue();
        }

        await Assert.That(ViewerClient.Tell(settle, port)).IsFalse();
        await Assert.That(ViewerClient.FoundUnowned(port)).IsTrue();
    }

    /// <summary>
    /// Or with the next owner on it, which the send reaches rather than failing on the last one's
    /// connection.
    /// </summary>
    [Test]
    public async Task TheNextOwnerOfThePortIsReached()
    {
        int port;
        using (var first = new Owner())
        {
            port = first.Port;
            ViewerClient.Tell(settle, port);
            await Assert.That(ViewerClient.Tell(settle, port)).IsTrue();
        }

        using var next = new Owner(port: port);

        await Assert.That(ViewerClient.Tell(settle, port)).IsTrue();
        await Assert.That(ViewerClient.Tell(settle, port)).IsTrue();
        await Assert.That(next.Heard.Count).IsEqualTo(2);
    }

    /// <summary>
    /// A send to another port leaves the one connection for that port's owner, and the first
    /// owner is still reached afterwards.
    /// </summary>
    [Test]
    public async Task EachPortIsSentItsOwn()
    {
        using var first = new Owner();
        using var second = new Owner();

        for (var index = 0; index < 3; index++)
        {
            await Assert.That(ViewerClient.Tell(settle, first.Port)).IsTrue();
            await Assert.That(ViewerClient.Tell(settle, second.Port)).IsTrue();
        }

        await Assert.That(first.Heard.Count).IsEqualTo(3);
        await Assert.That(second.Heard.Count).IsEqualTo(3);
    }

    /// <summary>
    /// An owner that is there and too busy to answer inside the timeout. The send is given up
    /// on, as one on a connection of its own is, and is not then sent a second time to wait
    /// again; the send after it starts over and is answered.
    /// </summary>
    [Test]
    public async Task AnOwnerTooBusyToAnswerIsNotWaitedOnTwice()
    {
        using var release = new ManualResetEventSlim();
        using var owner = new Owner(_ =>
        {
            if (_.Key == "slow")
            {
                release.Wait(TimeSpan.FromSeconds(30));
            }

            return ViewerResponse.Success();
        });
        ViewerClient.Tell(settle, owner.Port);

        var watch = Stopwatch.StartNew();
        var sent = ViewerClient.Tell(new(ViewerVerb.Settle, "slow"), owner.Port);
        watch.Stop();
        release.Set();

        await Assert.That(sent).IsFalse();
        // The client's timeout is three seconds, and twice that is what sending it again cost
        await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(5.5));
        await Assert.That(owner.Heard.Count(_ => _.Key == "slow")).IsEqualTo(1);
        await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();
    }

    /// <summary>
    /// A queue owner on a port of the test's choosing, recording what it was sent.
    /// </summary>
    sealed class Owner : IDisposable
    {
        readonly ViewerServer server;
        readonly CancelSource cancel = new();
        readonly Task listening;
        readonly List<ViewerMessage> heard = [];

        public Owner(Func<ViewerMessage, ViewerResponse>? answer = null, int port = 0)
        {
            if (!ViewerServer.TryBind(port, out var bound))
            {
                throw new($"Could not bind port {port}.");
            }

            server = bound;
            listening = server.Listen(
                _ =>
                {
                    lock (heard)
                    {
                        heard.Add(_);
                    }

                    return answer?.Invoke(_) ?? ViewerResponse.Success();
                },
                cancel.Token);
        }

        public int Port => server.Port;

        public int Accepted => server.Accepted;

        public IReadOnlyList<ViewerMessage> Heard
        {
            get
            {
                lock (heard)
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
    /// The owner every released tray and viewer is: one request a connection, read until the
    /// client closes its half, answered, and closed.
    /// </summary>
    sealed class OlderOwner : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly List<string> requests = [];

        public OlderOwner()
        {
            listener.Start();
            Port = ((IPEndPoint) listener.LocalEndpoint).Port;
            _ = Task.Run(Serve);
        }

        public int Port { get; }

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (requests)
                {
                    return requests.ToList();
                }
            }
        }

        async Task Serve()
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    var request = await reader.ReadToEndAsync();
                    lock (requests)
                    {
                        requests.Add(request);
                    }

                    var reply = Encoding.UTF8.GetBytes(ViewerResponse.Success().Build());
                    await stream.WriteAsync(reply, 0, reply.Length);
                    await stream.FlushAsync();
                }
            }
            catch (Exception exception)
                when (exception is SocketException or ObjectDisposedException or InvalidOperationException or IOException)
            {
                // Stopped
            }
        }

        public void Dispose() =>
            listener.Stop();
    }
}
