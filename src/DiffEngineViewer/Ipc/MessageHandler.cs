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

    int IQueueOwner.Enqueue(InlinePatch patch) =>
        host.Mutate(_ => ViewerSession.EnqueueInline(_, patch)).Queue.Count;

    void IQueueOwner.Settle(string key, string? origin) =>
        host.Mutate(_ => ViewerSession.Settle(_, key, origin));

    /// <summary>
    /// With patches, each item carries the payloads it was queued from — every variant of it —
    /// so a viewer showing someone else's queue can rebuild every pane locally and no diff has
    /// to cross the wire. Through the shared projection, so a conflicted entry lists identically
    /// whichever process owns the queue.
    /// </summary>
    ViewerResponse IQueueOwner.Listing(bool withPatches)
    {
        var items = ViewerListing.Items(
            host.State.Queue
                .Where(_ => _.Kind == QueueEntryKind.Inline)
                .Select(_ => new PendingInline(_.Variants, _.Status)),
            withPatches);
        return ViewerResponse.Listing(items);
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
