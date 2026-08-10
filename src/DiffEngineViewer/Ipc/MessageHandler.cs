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

    void IQueueOwner.Settle(string key) =>
        host.Mutate(_ => ViewerSession.Settle(_, key));

    /// <summary>
    /// With patches, each item carries the payload it was queued from, so a viewer showing
    /// someone else's queue can rebuild every pane locally and no diff has to cross the wire. A
    /// file entry has no patch to send and is listed without one.
    /// </summary>
    ViewerResponse IQueueOwner.Listing(bool withPatches)
    {
        var state = host.State;
        var items = new List<ViewerResponseItem>(state.Queue.Count);
        foreach (var entry in state.Queue)
        {
            var patch = withPatches && entry.Patch is not null
                ? InlinePatchFile.Build(entry.Patch)
                : null;
            items.Add(new(entry.Key, entry.Name, entry.Status, patch));
        }

        return ViewerResponse.Listing(items);
    }

    bool IQueueOwner.Has(string key) =>
        IndexOf(host.State, key) >= 0;

    (bool known, string? message) IQueueOwner.Accept(string key) =>
        Act(key, CommandKind.Accept);

    (bool known, string? message) IQueueOwner.Discard(string key) =>
        Act(key, CommandKind.Discard);

    (bool known, string? message) Act(string key, CommandKind command)
    {
        if (IndexOf(host.State, key) < 0)
        {
            return (false, null);
        }

        var state = host.Mutate(_ =>
        {
            var index = IndexOf(_, key);
            if (index < 0)
            {
                return _;
            }

            var selected = ViewerSession.Apply(_, Command.Select(index));
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
