/// <summary>
/// Maps wire messages onto whoever owns the queue.
/// <para>
/// This used to be written twice, once per host, and the two copies agreed by care rather than by
/// construction: the same twelve verb switch, the same validation, the same error strings. What a
/// verb means now lives here once, and a host supplies only the storage behind it through
/// <see cref="IQueueOwner"/>.
/// </para>
/// </summary>
static class ViewerMessageHandler
{
    public static ViewerResponse Handle(IQueueOwner owner, ViewerMessage message)
    {
        switch (message.Verb)
        {
            case ViewerVerb.Inline:
                return Inline(owner, message.Body);
            case ViewerVerb.Settle:
                return Settle(owner, message.Key);
            case ViewerVerb.List:
                return owner.Listing(false);
            case ViewerVerb.ListFull:
                return owner.Listing(true);
            case ViewerVerb.Accept:
            case ViewerVerb.Discard:
                return Act(owner, message.Key, message.Verb);
            case ViewerVerb.AcceptAll:
                return ViewerResponse.Success(owner.AcceptAll());
            case ViewerVerb.DiscardAll:
                return ViewerResponse.Success(owner.DiscardAll());
            case ViewerVerb.Focus:
                return Focus(owner, message.Key);
            case ViewerVerb.Show:
                owner.Window(WindowCommand.Show, null);
                return ViewerResponse.Success();
            case ViewerVerb.Hide:
                owner.Window(WindowCommand.Hide, null);
                return ViewerResponse.Success();
            case ViewerVerb.Quit:
                // A window command rather than a state change, because that is the one route that
                // also works when the window belongs to another process. For a viewer that owns
                // the queue, closing the window is the process exiting; a tray owned queue just
                // loses its display until something reopens one.
                owner.Window(WindowCommand.Close, null);
                return ViewerResponse.Success("Closing");
            default:
                return ViewerResponse.Error($"Unsupported verb: {message.Verb}");
        }
    }

    static ViewerResponse Inline(IQueueOwner owner, string? body)
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

        return ViewerResponse.Success($"Queued {owner.Enqueue(patch)}");
    }

    static ViewerResponse Settle(IQueueOwner owner, string? key)
    {
        if (key is null)
        {
            return ViewerResponse.Error("Settle requires a key");
        }

        owner.Settle(key);
        return ViewerResponse.Success();
    }

    static ViewerResponse Act(IQueueOwner owner, string? key, ViewerVerb verb)
    {
        if (key is null)
        {
            return ViewerResponse.Error($"{verb} requires a key");
        }

        var (known, message) = verb == ViewerVerb.Accept
            ? owner.Accept(key)
            : owner.Discard(key);
        if (!known)
        {
            return ViewerResponse.Error($"No pending snapshot for {key}");
        }

        return ViewerResponse.Success(message);
    }

    static ViewerResponse Focus(IQueueOwner owner, string? key)
    {
        if (key is not null &&
            !owner.Has(key))
        {
            return ViewerResponse.Error($"No pending snapshot for {key}");
        }

        owner.Window(WindowCommand.Focus, key);
        return ViewerResponse.Success();
    }
}
