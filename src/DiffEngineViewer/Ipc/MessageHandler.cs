/// <summary>
/// Maps a wire message onto the session. Split from the transport so the whole protocol,
/// including the tray facing half, is testable without a socket or a window.
/// </summary>
class MessageHandler(SessionHost host, ViewerActions actions, Action<WindowCommand> window)
{
    public ViewerResponse Handle(ViewerMessage message)
    {
        switch (message.Verb)
        {
            case ViewerVerb.Inline:
                return Inline(message.Body);
            case ViewerVerb.Settle:
                return Settle(message.Key);
            case ViewerVerb.List:
                return List();
            case ViewerVerb.Accept:
                return Act(message.Key, CommandKind.Accept);
            case ViewerVerb.Discard:
                return Act(message.Key, CommandKind.Discard);
            case ViewerVerb.AcceptAll:
                return All(CommandKind.AcceptAll);
            case ViewerVerb.DiscardAll:
                return All(CommandKind.DiscardAll);
            case ViewerVerb.Focus:
                return Focus(message.Key);
            case ViewerVerb.Show:
                window(WindowCommand.Show);
                return ViewerResponse.Success();
            case ViewerVerb.Hide:
                window(WindowCommand.Hide);
                return ViewerResponse.Success();
            case ViewerVerb.Quit:
                host.Mutate(_ => ViewerSession.Apply(_, CommandKind.Quit));
                return ViewerResponse.Success("Closing");
            default:
                return ViewerResponse.Error($"Unsupported verb: {message.Verb}");
        }
    }

    ViewerResponse Inline(string? body)
    {
        if (body is null)
        {
            return ViewerResponse.Error("Inline requires a body");
        }

        if (!InlinePatchFile.TryParse(body, out var patch))
        {
            return ViewerResponse.Error("Inline body is not a readable patch payload");
        }

        // Remove strips a literal when inline is switched off. That is a configuration change with
        // nothing to review, so the sender applies it directly rather than queueing it here.
        if (patch.Mode == InlinePatchMode.Remove)
        {
            return ViewerResponse.Error($"{InlinePatchMode.Remove} patches are not reviewable");
        }

        var state = host.Mutate(_ => ViewerSession.EnqueueInline(_, patch));
        return ViewerResponse.Success($"Queued {state.Queue.Count}");
    }

    ViewerResponse Settle(string? key)
    {
        if (key is null)
        {
            return ViewerResponse.Error("Settle requires a key");
        }

        host.Mutate(_ => ViewerSession.Settle(_, key));
        return ViewerResponse.Success();
    }

    ViewerResponse List()
    {
        var state = host.State;
        var items = new List<ViewerResponseItem>(state.Queue.Count);
        foreach (var entry in state.Queue)
        {
            items.Add(new(entry.Key, entry.Name, entry.Status));
        }

        return ViewerResponse.Listing(items);
    }

    ViewerResponse Act(string? key, CommandKind command)
    {
        if (key is null)
        {
            return ViewerResponse.Error($"{command} requires a key");
        }

        if (IndexOf(key) < 0)
        {
            return ViewerResponse.Error($"No pending snapshot for {key}");
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

        return ViewerResponse.Success(state.Message);
    }

    ViewerResponse All(CommandKind command)
    {
        var state = host.Mutate(_ => ViewerSession.Apply(_, command, actions));
        return ViewerResponse.Success(state.Message);
    }

    ViewerResponse Focus(string? key)
    {
        if (key is not null)
        {
            if (IndexOf(key) < 0)
            {
                return ViewerResponse.Error($"No pending snapshot for {key}");
            }

            host.Mutate(_ =>
            {
                var index = IndexOf(_, key);
                return index < 0 ? _ : ViewerSession.Apply(_, Command.Select(index));
            });
        }

        window(WindowCommand.Focus);
        return ViewerResponse.Success();
    }

    int IndexOf(string key) =>
        IndexOf(host.State, key);

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
