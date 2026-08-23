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
                return Settle(owner, message.Key, message.Body, message.Member);
            case ViewerVerb.Move:
                return Move(owner, message.Key, message.Body);
            case ViewerVerb.Diff:
                return Diff(owner, message.Key, message.Body);
            case ViewerVerb.Delete:
                return Delete(owner, message.Key);
            case ViewerVerb.List:
                return owner.Listing(false);
            case ViewerVerb.ListFull:
                return owner.Listing(true);
            case ViewerVerb.Accept:
            case ViewerVerb.Discard:
                return Act(owner, message.Key, message.Body, message.Verb);
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

    static ViewerResponse Settle(IQueueOwner owner, string? key, string? origin, string? member)
    {
        if (key is null)
        {
            return ViewerResponse.Error("Settle requires a key");
        }

        owner.Settle(key, origin, member);
        return ViewerResponse.Success();
    }

    /// <summary>
    /// The paths ride key and body rather than an encoded payload, because that is all a tracked
    /// move is. What DiffEngine knows beside them — the diff tool it launched and that tool's
    /// process id — is the tray's kill machinery and means nothing to an owner that does not have
    /// any, so it is not sent.
    /// </summary>
    static ViewerResponse Move(IQueueOwner owner, string? temp, string? target)
    {
        if (temp is null ||
            target is null)
        {
            return ViewerResponse.Error("Move requires a key and a body");
        }

        owner.TrackMove(temp, target);
        return ViewerResponse.Success();
    }

    /// <summary>
    /// The same tracking <see cref="Move"/> performs, plus the window it deliberately withholds.
    /// The focus names the entry just tracked, so an owner with a window selects it and an owner
    /// without one - a tray - starts a viewer and hands it the same selection.
    /// </summary>
    static ViewerResponse Diff(IQueueOwner owner, string? temp, string? target)
    {
        if (temp is null ||
            target is null)
        {
            return ViewerResponse.Error("Diff requires a key and a body");
        }

        owner.TrackMove(temp, target);
        owner.Window(WindowCommand.Focus, TrackedKeys.ForMove(temp));
        return ViewerResponse.Success();
    }

    static ViewerResponse Delete(IQueueOwner owner, string? file)
    {
        if (file is null)
        {
            return ViewerResponse.Error("Delete requires a key");
        }

        owner.TrackDelete(file);
        return ViewerResponse.Success();
    }

    static ViewerResponse Act(IQueueOwner owner, string? key, string? body, ViewerVerb verb)
    {
        if (key is null)
        {
            return ViewerResponse.Error($"{verb} requires a key");
        }

        // The body is the variant origin a reviewer picked, and only an accept carries one.
        var (ok, message) = verb == ViewerVerb.Accept
            ? owner.Accept(key, body)
            : owner.Discard(key);
        if (!ok)
        {
            return ViewerResponse.Error(message ?? $"No pending snapshot for {key}");
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
