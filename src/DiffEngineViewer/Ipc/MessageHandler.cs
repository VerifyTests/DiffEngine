/// <summary>
/// The viewer as queue owner: <see cref="IQueueOwner"/> over the session, for
/// <see cref="ViewerMessageHandler"/> to map the wire onto. What a verb means lives there; what
/// stays here is the projection into <see cref="SessionState"/>, so the display follows the queue
/// in the same mutation — acting on an entry selects it, and a focus lands on its item.
/// </summary>
class MessageHandler(SessionHost host, ViewerActions actions, Action<WindowCommand> window) :
    IQueueOwner
{
    public ViewerResponse Handle(ViewerMessage message) =>
        ViewerMessageHandler.Handle(this, message);

    int IQueueOwner.Enqueue(InlinePatch patch)
    {
        // Inline entries only, which is what a tray owner counts. This queue also holds tracked
        // moves and deletes, so counting all of it had the two owners answering the same verb
        // with different numbers
        var count = host
            .Mutate(_ => ViewerSession.EnqueueInline(_, patch))
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
    /// seam <see cref="OwnerLink"/> materializes the tray's tracked files through.
    /// </summary>
    void IQueueOwner.TrackMove(string temp, string target) =>
        host.Mutate(_ => ViewerSession.EnqueueTracked(_, TrackedEntry.ForMove(temp, target)));

    void IQueueOwner.TrackDelete(string file) =>
        host.Mutate(_ => ViewerSession.EnqueueTracked(_, TrackedEntry.ForDelete(file)));

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
        var queue = host.State.Queue;
        var items = ViewerListing.Items(
            queue
                .Where(_ => _.Kind == QueueEntryKind.Inline)
                .Select(_ => new PendingInline(_.Variants, _.Status)),
            withPatches);
        if (!withPatches)
        {
            return ViewerResponse.Listing(items);
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
                .ToList());
    }

    bool IQueueOwner.Has(string key) =>
        IndexOf(host.State, key) >= 0;

    (bool ok, string? message) IQueueOwner.Accept(string key, string? origin) =>
        Act(key, CommandKind.Accept, origin);

    (bool ok, string? message) IQueueOwner.Discard(string key) =>
        Act(key, CommandKind.Discard);

    (bool ok, string? message) Act(string key, CommandKind command, string? origin = null)
    {
        var index = IndexOf(host.State, key);
        if (index < 0)
        {
            return (false, null);
        }

        // Refused before anything moves, and as a wire error, matching the tray owner: an
        // un-targeted accept of a conflicted entry has no honest way to pick a side.
        var entry = host.State.Queue[index];
        if (command == CommandKind.Accept &&
            origin is null &&
            entry.Conflicted)
        {
            return (false, new PendingInline(entry.Variants, entry.Status).ConflictRefusal);
        }

        var state = host.Mutate(_ =>
        {
            var found = IndexOf(_, key);
            if (found < 0)
            {
                return _;
            }

            var selected = ViewerSession.Apply(_, Command.Select(found));
            if (origin is not null)
            {
                selected = ViewerSession.SelectVariant(selected, origin);
            }

            return ViewerSession.Apply(selected, command, actions);
        });

        return (true, state.Message);
    }

    string? IQueueOwner.AcceptAll() =>
        host.Mutate(_ => ViewerSession.Apply(_, CommandKind.AcceptAll, actions)).Message;

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
