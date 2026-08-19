/// <summary>
/// The review surface half of the queue protocol: what a tool that is neither the viewer nor the
/// tray sees when it lists and accepts.
/// <para>
/// Against a real socket, a real <see cref="ViewerMessageHandler" /> and a real
/// <see cref="InlineQueue" />, because what is being pinned is that the three agree — a client
/// tested against a hand written responder would pass while the owner said something else.
/// </para>
/// </summary>
[NotInParallel]
public class InlineQueueClientTests
{
    static InlinePatch Patch(
        string source = "Sample.cs",
        int line = 42,
        string content = "new content",
        string? framework = null) =>
        new(source, line, "\"old\"", content)
        {
            TestName = "Sample.Test",
            Framework = framework
        };

    [Test]
    public async Task ListsWhatTheOwnerHolds()
    {
        using var owner = new Owner();
        owner.Enqueue(Patch("A.cs", 1, "first"));
        owner.Enqueue(Patch("B.cs", 2, "second"));

        var listed = InlineQueueClient.TryList(out var pending);

        await Assert.That(listed).IsTrue();
        await Assert.That(pending.Select(_ => _.Name)).IsEquivalentTo(["A.cs:1", "B.cs:2"]);
        // The patches ride the listing, which is what lets a surface render the snapshot without
        // reading anything from disk.
        await Assert.That(pending.Select(_ => _.Patch.NewContent)).IsEquivalentTo(["first", "second"]);
        await Assert.That(pending[0].Patch.OriginalValue).IsNull();
        await Assert.That(pending[0].Patch.OriginalExpression).IsEqualTo("\"old\"");
        await Assert.That(pending[0].Patch.TestName).IsEqualTo("Sample.Test");
    }

    /// <summary>
    /// Not the same answer as an empty queue: a surface that falls back to the files a test run
    /// staged has to be able to tell "nobody is holding this" from "nothing is pending".
    /// </summary>
    [Test]
    public async Task ReportsWhenNoOwnerAnswers()
    {
        using var nobody = new NoOwner();

        var listed = InlineQueueClient.TryList(out var pending);

        await Assert.That(listed).IsFalse();
        await Assert.That(pending).IsEmpty();
    }

    /// <summary>
    /// The cheap listing a surface builds a menu from: which call sites are pending, without the
    /// patch payload of every one of them crossing the wire.
    /// </summary>
    [Test]
    public async Task ListsKeysWithoutPatches()
    {
        using var owner = new Owner();
        owner.Enqueue(Patch("A.cs", 1));
        owner.Enqueue(Patch("B.cs", 2));

        var listed = InlineQueueClient.TryListKeys(out var keys);

        await Assert.That(listed).IsTrue();
        await Assert.That(keys).IsEquivalentTo([InlineKey.For("A.cs", 1), InlineKey.For("B.cs", 2)]);
    }

    [Test]
    public async Task ReportsWhenNoOwnerAnswersKeys()
    {
        using var nobody = new NoOwner();

        var listed = InlineQueueClient.TryListKeys(out var keys);

        await Assert.That(listed).IsFalse();
        await Assert.That(keys).IsEmpty();
    }

    [Test]
    public async Task FindsTheEntryForACallSite()
    {
        using var owner = new Owner();
        owner.Enqueue(Patch("A.cs", 1));
        owner.Enqueue(Patch("B.cs", 2));

        var entry = InlineQueueClient.Find(InlineKey.For("B.cs", 2));

        await Assert.That(entry!.Name).IsEqualTo("B.cs:2");
    }

    [Test]
    public async Task AcceptAppliesInTheOwnerAndDropsTheEntry()
    {
        using var owner = new Owner();
        owner.Enqueue(Patch());

        var outcome = InlineQueueClient.Accept(InlineKey.For("Sample.cs", 42), out var message);

        await Assert.That(outcome).IsEqualTo(InlineAcceptOutcome.Accepted);
        await Assert.That(message).IsEqualTo("Applied Sample.cs:42");
        // Applied where the queue is, not where the client is: one writer per source file.
        await Assert.That(owner.Applied.Single().NewContent).IsEqualTo("new content");
        await Assert.That(InlineQueueClient.TryList(out var pending)).IsTrue();
        await Assert.That(pending).IsEmpty();
    }

    /// <summary>
    /// An apply that could not write the file keeps its entry, so it can be retried once whatever
    /// blocked it is gone. The wire says the verb was carried out either way, which is why the
    /// client asks whether the entry survived rather than reading the answer off <c>ok</c>.
    /// </summary>
    [Test]
    public async Task AFailedApplyStaysPending()
    {
        using var owner = new Owner
        {
            Apply = _ => InlineApplyResult.Failed("the file is locked")
        };
        owner.Enqueue(Patch());

        var outcome = InlineQueueClient.Accept(InlineKey.For("Sample.cs", 42), out var message);

        await Assert.That(outcome).IsEqualTo(InlineAcceptOutcome.Failed);
        await Assert.That(message).IsEqualTo("the file is locked");
        await Assert.That(InlineQueueClient.Find(InlineKey.For("Sample.cs", 42))).IsNotNull();
    }

    /// <summary>
    /// A stale patch is dropped rather than kept, so it reads as accepted. The owner's message is
    /// what carries the difference, which is why it is handed back rather than swallowed.
    /// </summary>
    [Test]
    public async Task AStalePatchReportsWhatTheOwnerSaid()
    {
        using var owner = new Owner
        {
            Apply = _ => InlineApplyResult.NotFound("no call site")
        };
        owner.Enqueue(Patch());

        var outcome = InlineQueueClient.Accept(InlineKey.For("Sample.cs", 42), out var message);

        await Assert.That(outcome).IsEqualTo(InlineAcceptOutcome.Accepted);
        await Assert.That(message).IsEqualTo("Sample.cs:42 not written. no call site");
    }

    /// <summary>
    /// Two frameworks disagreeing is refused rather than picked between, and nothing is applied.
    /// </summary>
    [Test]
    public async Task AConflictIsRefusedWithItsReason()
    {
        using var owner = new Owner();
        owner.Enqueue(Patch(content: "from net8", framework: "net8.0"));
        owner.Enqueue(Patch(content: "from net9", framework: "net9.0"));

        var outcome = InlineQueueClient.Accept(InlineKey.For("Sample.cs", 42), out var message);

        await Assert.That(outcome).IsEqualTo(InlineAcceptOutcome.Failed);
        await Assert.That(message).IsEqualTo("Conflicting snapshots (net8.0 / net9.0), resolve in the viewer");
        await Assert.That(owner.Applied).IsEmpty();
    }

    [Test]
    public async Task AcceptOfAnUnknownCallSiteDoesNothing()
    {
        using var owner = new Owner();
        owner.Enqueue(Patch());

        var outcome = InlineQueueClient.Accept(InlineKey.For("Other.cs", 1), out var message);

        await Assert.That(outcome).IsEqualTo(InlineAcceptOutcome.Unknown);
        await Assert.That(message).IsNull();
        await Assert.That(owner.Applied).IsEmpty();
        // The one it does hold is untouched.
        await Assert.That(InlineQueueClient.Find(InlineKey.For("Sample.cs", 42))).IsNotNull();
    }

    /// <summary>
    /// Whether the entry survived is what says an accept applied, and that takes a second round
    /// trip. When it cannot be made — the owner exited, or answered with an error — there is no
    /// answer to give, and the one that used to be given was Accepted: an empty item list read as
    /// an empty queue, so a surface stopped offering a snapshot nothing had confirmed.
    /// </summary>
    [Test]
    public async Task AnAcceptThatCannotBeConfirmedIsNotCalledAccepted()
    {
        using var owner = new Owner();
        owner.Enqueue(Patch());
        owner.ListingFails = true;

        var outcome = InlineQueueClient.Accept(InlineKey.For("Sample.cs", 42), out _);

        await Assert.That(outcome).IsEqualTo(InlineAcceptOutcome.Unknown);
        // The owner did carry it out; only the confirmation was unavailable
        await Assert.That(owner.Applied.Single().NewContent).IsEqualTo("new content");
    }

    /// <summary>
    /// The same shape one level down: an error listing is not a statement that nothing is pending,
    /// so a caller that falls back when no owner answered has to fall back here too.
    /// </summary>
    [Test]
    public async Task AnErrorListingIsNotAnEmptyQueue()
    {
        using var owner = new Owner
        {
            ListingFails = true
        };
        owner.Enqueue(Patch());

        await Assert.That(InlineQueueClient.TryList(out var pending)).IsFalse();
        await Assert.That(pending).IsEmpty();
        await Assert.That(InlineQueueClient.TryListKeys(out var keys)).IsFalse();
        await Assert.That(keys).IsEmpty();
    }

    /// <summary>
    /// The sending process stamps its framework onto the payload, and leaves the caller's patch
    /// as it was. It is a public entry point taking an object the caller still holds and may send
    /// again, so editing it is not the send's business.
    /// </summary>
    [Test]
    public async Task AddInlineStampsThePayloadAndNotTheCallersPatch()
    {
        using var owner = new Owner();
        var patch = Patch();
        var wasDisabled = DiffRunner.Disabled;
        DiffRunner.Disabled = false;
        try
        {
            await Assert.That(await DiffRunner.AddInlineAsync(patch)).IsEqualTo(InlineResult.Queued);
        }
        finally
        {
            DiffRunner.Disabled = wasDisabled;
        }

        // It reached the queue stamped
        await Assert.That(InlineQueueClient.TryList(out var pending)).IsTrue();
        await Assert.That(pending.Single().Patch.Framework).IsEqualTo(RuntimeMoniker.Current);

        // And the caller's own patch never acquired one
        await Assert.That(patch.Framework).IsNull();
    }

    [Test]
    public async Task DiscardDropsWithoutApplying()
    {
        using var owner = new Owner();
        owner.Enqueue(Patch());

        var discarded = InlineQueueClient.Discard(InlineKey.For("Sample.cs", 42), out var message);

        await Assert.That(discarded).IsTrue();
        await Assert.That(message).IsEqualTo("Discarded Sample.cs:42");
        await Assert.That(owner.Applied).IsEmpty();
        await Assert.That(InlineQueueClient.Find(InlineKey.For("Sample.cs", 42))).IsNull();
    }

    [Test]
    public async Task FocusAsksForTheWindow()
    {
        using var owner = new Owner();
        owner.Enqueue(Patch());

        var focused = InlineQueueClient.Focus(InlineKey.For("Sample.cs", 42));

        await Assert.That(focused).IsTrue();
        await Assert.That(owner.Window).IsEqualTo(WindowCommand.Focus);
    }

    /// <summary>
    /// Points DiffEngine_ViewerPort at a port nothing is listening on, so what the client meets is
    /// an absent owner rather than whichever viewer or tray happens to be running on this machine's
    /// default port — which is what the assert would otherwise be talking to.
    /// </summary>
    sealed class NoOwner : IDisposable
    {
        readonly string? previousPort;

        public NoOwner()
        {
            // Bound only to be given a port that is free, then released, so nothing can answer on it.
            if (!ViewerServer.TryBind(0, out var bound))
            {
                throw new("Could not bind an ephemeral port.");
            }

            var port = bound.Port;
            bound.Dispose();
            previousPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, port.ToString());
        }

        public void Dispose() =>
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousPort);
    }

    /// <summary>
    /// A queue owner on an ephemeral port, with DiffEngine_ViewerPort pointed at it so a viewer
    /// or tray running on this machine is not the thing being talked to.
    /// <para>
    /// Everything below the socket is the real thing: <see cref="ViewerMessageHandler" /> maps the
    /// verbs and <see cref="InlineQueue" /> holds the entries. Only applying is substituted, so a
    /// failure or a stale patch can be arranged without a locked file.
    /// </para>
    /// </summary>
    sealed class Owner : IQueueOwner, IDisposable
    {
        readonly ViewerServer server;
        readonly CancelSource cancel = new();
        readonly Task listening;
        readonly string? previousPort;
        readonly Lock gate = new();
        InlineQueue queue = InlineQueue.Empty;

        public Owner()
        {
            if (!ViewerServer.TryBind(0, out var bound))
            {
                throw new("Could not bind an ephemeral port.");
            }

            server = bound;
            previousPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, server.Port.ToString());
            listening = server.Listen(_ => ViewerMessageHandler.Handle(this, _), cancel.Token);
        }

        public Func<InlinePatch, InlineApplyResult> Apply { get; init; } = _ => InlineApplyResult.Applied;

        public List<InlinePatch> Applied { get; } = [];

        public WindowCommand? Window { get; private set; }

        public void Enqueue(InlinePatch patch)
        {
            lock (gate)
            {
                queue = queue.Enqueue(patch);
            }
        }

        InlineApplyResult Record(InlinePatch patch)
        {
            var result = Apply(patch);
            if (result.Status is InlineApplyStatus.Applied or InlineApplyStatus.AlreadyApplied)
            {
                Applied.Add(patch);
            }

            return result;
        }

        int IQueueOwner.Enqueue(InlinePatch patch)
        {
            Enqueue(patch);
            lock (gate)
            {
                return queue.Count;
            }
        }

        void IQueueOwner.Settle(string key, string? origin, string? member)
        {
            lock (gate)
            {
                queue = queue.Settle(key, origin, member);
            }
        }

        void IQueueOwner.TrackMove(string temp, string target)
        {
        }

        void IQueueOwner.TrackDelete(string file)
        {
        }

        /// <summary>
        /// When set, every listing comes back as an error, which is how an owner that cannot say
        /// what it holds is arranged without racing its own shutdown.
        /// </summary>
        public bool ListingFails { get; set; }

        ViewerResponse IQueueOwner.Listing(bool withPatches)
        {
            if (ListingFails)
            {
                return ViewerResponse.Error("the owner is going away");
            }

            lock (gate)
            {
                return ViewerResponse.Listing(ViewerListing.Items(queue.Items, withPatches));
            }
        }

        bool IQueueOwner.Has(string key)
        {
            lock (gate)
            {
                return queue.Find(key) is not null;
            }
        }

        (bool ok, string? message) IQueueOwner.Accept(string key, string? origin)
        {
            lock (gate)
            {
                if (queue.Find(key) is null)
                {
                    return (false, null);
                }

                var before = queue;
                queue = origin is null
                    ? queue.Accept(key, Record, out var message)
                    : queue.Accept(key, origin, Record, out message);

                // The queue refuses a conflict by returning itself with the reason, which is a
                // refusal rather than an attempt and goes on the wire as an error.
                if (ReferenceEquals(before, queue))
                {
                    return (false, message);
                }

                return (true, message);
            }
        }

        (bool ok, string? message) IQueueOwner.Discard(string key)
        {
            lock (gate)
            {
                if (queue.Find(key) is null)
                {
                    return (false, null);
                }

                queue = queue.Discard(key, out var message);
                return (true, message);
            }
        }

        string IQueueOwner.AcceptAll()
        {
            lock (gate)
            {
                queue = queue.AcceptAll(Record, out var message);
                return message;
            }
        }

        string IQueueOwner.DiscardAll()
        {
            lock (gate)
            {
                queue = queue.DiscardAll(out var message);
                return message;
            }
        }

        void IQueueOwner.Window(WindowCommand command, string? key) =>
            Window = command;

        public void Dispose()
        {
            cancel.Cancel();
            server.Dispose();
            try
            {
                listening.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Cancellation unwinds through the listener; nothing to report.
            }

            cancel.Dispose();
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousPort);
        }
    }
}
