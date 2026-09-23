/// <summary>
/// The viewer as queue owner: <see cref="IQueueOwner"/> over the session, for
/// <see cref="ViewerMessageHandler"/> to map the wire onto. What a verb means lives there; what
/// stays here is the projection into <see cref="SessionState"/>, so the display follows the queue
/// in the same mutation — acting on an entry selects it, and a focus lands on its item.
/// </summary>
/// <param name="runner">
/// The window's, when there is one, so an accept-all from the tray and one clicked in the window
/// take turns rather than claiming the same entries. A handler with no window makes its own.
/// </param>
class MessageHandler(
    SessionHost host,
    ViewerActions actions,
    Action<WindowCommand> window,
    AcceptAllRunner? runner = null) :
    IQueueOwner
{
    readonly AcceptAllRunner runner = runner ?? new(host, actions);

    public ViewerResponse Handle(ViewerMessage message) =>
        ViewerMessageHandler.Handle(this, message);

    int IQueueOwner.Enqueue(InlinePatch patch)
    {
        // Inline entries only, which is what a tray owner counts. This queue also holds tracked
        // moves and deletes, so counting all of it had the two owners answering the same verb
        // with different numbers
        var state = host.Mutate(_ => ViewerSession.EnqueueInline(_, patch));
        RefuseWhenClosing(state);
        var count = state
            .Queue
            .Count(_ => _.Kind == QueueEntryKind.Inline);
        // Brought forward on the entry that arrived, which is what a tray owner does with one of
        // these. Without it a patch landing in a window that is hidden - which this one is
        // whenever a tray is running and the queue last emptied - showed up only as a tray icon
        // on the next scan, and one landing in a window behind the editor showed up not at all
        ((IQueueOwner) this).Window(WindowCommand.Focus, InlineKey.For(patch.SourceFile, patch.LineHint));
        return count;
    }

    void IQueueOwner.Settle(string key, string? origin, string? member) =>
        host.Mutate(_ => ViewerSession.Settle(_, key, origin, member));

    /// <summary>
    /// The files are read here, on the listener thread, so the session stays IO free — the same
    /// seam <see cref="OwnerLink"/> materializes the tray's tracked files through. Before the lock
    /// rather than inside it: building an entry reads both files and diffs them, and the render
    /// loop takes the same lock every frame.
    /// </summary>
    void IQueueOwner.TrackMove(string temp, string target)
    {
        var entry = TrackedEntry.ForMove(temp, target);
        RefuseWhenClosing(host.Mutate(_ => ViewerSession.EnqueueTracked(_, entry)));
    }

    void IQueueOwner.TrackDelete(string file)
    {
        var entry = TrackedEntry.ForDelete(file);
        RefuseWhenClosing(host.Mutate(_ => ViewerSession.EnqueueTracked(_, entry)));
    }

    /// <summary>
    /// Thrown rather than returned, because <see cref="IQueueOwner"/> has no refusal to return for
    /// these verbs, and a throwing handler is answered with an error: the sender then stages or
    /// relaunches instead of believing a window that is on its way out took what it sent. See
    /// <see cref="SessionState.Closing"/>.
    /// </summary>
    static void RefuseWhenClosing(SessionState state)
    {
        if (state.Closing)
        {
            throw new InvalidOperationException("This viewer is closing and can take nothing more. Send it again once it has gone.");
        }
    }

    /// <summary>
    /// With patches, each item carries the payloads it was queued from — every variant of it —
    /// so a viewer showing someone else's queue can rebuild every pane locally and no diff has
    /// to cross the wire. Through the shared projection, so a conflicted entry lists identically
    /// whichever process owns the queue.
    /// <para>
    /// The tracked moves and deletes ride a full listing only, matching a tray owner: the plain
    /// listing drives the tray menu, which reads its own tracker rather than the wire for those.
    /// </para>
    /// </summary>
    ViewerResponse IQueueOwner.Listing(bool withPatches)
    {
        // One read, so the queue and the progress describe the same moment of a batch
        var state = host.State;
        var queue = state.Queue;
        var items = ViewerListing.Items(
            queue
                .Where(_ => _.Kind == QueueEntryKind.Inline)
                .Select(_ => new PendingInline(_.Variants, _.Status)),
            withPatches);
        if (!withPatches)
        {
            return ViewerResponse.Listing(items, progress: state.Progress);
        }

        return ViewerResponse.Listing(
            items,
            moves: queue
                .Where(_ => _.Kind == QueueEntryKind.Move)
                .Select(_ => new ViewerResponseMove(_.Key, _.Name, _.Solution, _.LeftFile!, _.TargetFile!))
                .ToList(),
            deletes: queue
                .Where(_ => _.Kind == QueueEntryKind.Delete)
                .Select(_ => new ViewerResponseDelete(_.Key, _.Name, _.Solution, _.LeftFile!))
                .ToList(),
            progress: state.Progress);
    }

    /// <summary>
    /// The session's queue is immutable and replaced on every change - its moves and deletes are
    /// entries in it - so a new reference is a new generation. The progress rides the listing
    /// beside it. No window command is ever stashed: this owner has its own window.
    /// </summary>
    string IQueueOwner.ListingTag()
    {
        var state = host.State;
        lock (tagGate)
        {
            if (!ReferenceEquals(state.Queue, taggedQueue))
            {
                taggedQueue = state.Queue;
                generation++;
            }

            return $"{instance}.{generation}.{state.Progress?.Build()}";
        }
    }

    readonly string instance = Guid.NewGuid().ToString("N");
    readonly Lock tagGate = new();
    IReadOnlyList<QueueEntry>? taggedQueue;
    long generation;

    bool IQueueOwner.Has(string key) =>
        IndexOf(host.State, key) >= 0;

    (bool ok, string? message, bool written) IQueueOwner.Accept(string key, string? origin) =>
        Act(key, CommandKind.Accept, origin);

    (bool ok, string? message) IQueueOwner.Discard(string key)
    {
        var (ok, message, _) = Act(key, CommandKind.Discard);
        return (ok, message);
    }

    (bool ok, string? message, bool written) Act(string key, CommandKind command, string? origin = null)
    {
        // What the applier answered, for whoever sent this to know whether the snapshot is in the
        // source now. The session drops a patch whose call site moved just as it drops one that
        // landed, so the queue afterwards cannot tell the two apart.
        InlineApplyResult? applied = null;
        var recording = actions with
        {
            ApplyInline = _ => applied = actions.ApplyInline(_)
        };
        // Looked up and refused inside the same mutation that acts, rather than on a read taken
        // before it: the queue can change in between, which threw on an index that had gone, and
        // let an accept through on an entry a second framework had just made a conflict of.
        var found = false;
        string? refusal = null;
        var state = host.Mutate(_ =>
        {
            var index = IndexOf(_, key);
            if (index < 0)
            {
                return _;
            }

            found = true;
            // Refused before anything moves, and as a wire error, matching the tray owner: an
            // un-targeted accept of a conflicted entry has no honest way to pick a side.
            var entry = _.Queue[index];
            if (command == CommandKind.Accept &&
                origin is null &&
                entry.Conflicted)
            {
                refusal = new PendingInline(entry.Variants, entry.Status).ConflictRefusal;
                return _;
            }

            var selected = ViewerSession.Apply(_, Command.Select(index));
            if (origin is not null)
            {
                selected = ViewerSession.SelectVariant(selected, origin);
            }

            return ViewerSession.Apply(selected, command, recording);
        });

        if (!found)
        {
            return (false, null, false);
        }

        if (refusal is not null)
        {
            return (false, refusal, false);
        }

        var written = applied?.Status is InlineApplyStatus.Applied or InlineApplyStatus.AlreadyApplied;
        return (true, state.Message, written);
    }

    /// <summary>
    /// Started and carried out here, on the listener thread, since the tray asking is waiting for
    /// the answer - but a mutation per entry rather than one around the whole batch. Held as one,
    /// the lock kept the render loop out for as long as the queue took to apply, so the window
    /// froze exactly when there was progress to show.
    /// </summary>
    string? IQueueOwner.AcceptAll()
    {
        host.Mutate(ViewerSession.BeginAcceptAll);
        return runner.Drive();
    }

    string? IQueueOwner.DiscardAll() =>
        host.Mutate(_ => ViewerSession.Apply(_, CommandKind.DiscardAll, actions)).Message;

    void IQueueOwner.Window(WindowCommand command, string? key)
    {
        if (key is not null)
        {
            host.Mutate(_ => ViewerSession.SelectKey(_, key));
        }

        window(command);
    }

    static int IndexOf(SessionState state, string key)
    {
        for (var index = 0; index < state.Queue.Count; index++)
        {
            if (state.Queue[index].Key == key)
            {
                return index;
            }
        }

        return -1;
    }
}
