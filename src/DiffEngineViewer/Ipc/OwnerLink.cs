/// <summary>
/// The other half of a viewer that displays a queue rather than owning one. Every read and every
/// command goes to whoever holds the port.
/// <para>
/// Runs on its own thread, and that is not a detail. Accepting on the owner takes InlineApplier's
/// cross process mutex and can wait ten seconds for it, so a round trip on the render thread is
/// the same hazard as accepting there directly: a window that stops pumping for five seconds is
/// one Windows paints over with "Not Responding".
/// </para>
/// <para>
/// Polling rather than being pushed to keeps this to one port. Push would make every displaying
/// viewer a server as well, needing a second port and an order to discover them in.
/// </para>
/// </summary>
sealed class OwnerLink(SessionHost host, int port)
{
    /// <summary>
    /// What the owner asked be done to the window, for the render loop to drain. Owned here rather
    /// than handed in, because this is the only thing that produces into it when there is no
    /// server, and the two never coexist: a process either owns the queue or displays one.
    /// </summary>
    public ConcurrentQueue<WindowCommand> Windows { get; } = new();

    /// <summary>
    /// Fast enough that someone accepting from the tray sees the window follow, slow enough that
    /// an idle pair is not a busy loop over loopback.
    /// </summary>
    public static TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Sized to outlast a slow reply, not to detect absence. A dead owner refuses the connection
    /// in milliseconds, so failure still means gone almost immediately; a reply can legitimately
    /// take ten seconds, because accepting waits on InlineApplier's cross process mutex, and a
    /// wait shorter than that would read a busy owner as a dead one and close the window under
    /// the user.
    /// </summary>
    public static TimeSpan Wait { get; set; } = TimeSpan.FromSeconds(15);

    readonly ConcurrentQueue<Outbound> outbound = new();

    record Outbound(ViewerVerb Verb, string? Key, string? Body);

    public void Post(ViewerVerb verb, string? key, string? body = null) =>
        outbound.Enqueue(new(verb, key, body));

    public bool Pump() =>
        Pump(out _);

    /// <summary>
    /// Send everything posted since the last pass, then read the queue back. Returns false when
    /// the owner has gone, reported rather than acted on so the caller can decide whether that
    /// means "do not open a window" or "close the one that is open".
    /// </summary>
    public bool Pump(out bool sent)
    {
        sent = false;
        string? message = null;
        while (outbound.TryDequeue(out var command))
        {
            sent = true;
            message = Send(command);
        }

        if (!ViewerClient.TrySend(new(ViewerVerb.ListFull), out var response, port, Wait))
        {
            return false;
        }

        if (!response.Ok)
        {
            // An owner that answers is an owner. ViewerServer turns any exception in the listing
            // handler into an error reply, so reading one as death closed this window over a
            // single transient throw and lost the queue it was displaying. Said instead, and asked
            // again on the next pass.
            host.Mutate(_ => _ with
            {
                Message = response.Message ?? "The queue owner refused the listing."
            });
            return true;
        }

        var pending = InlineQueue.From(ViewerListing.Pending(response.Items));
        var changes = ReadChanges(response);
        host.Mutate(_ => ViewerSession.Sync(_, pending, changes, message));

        // The owner has no window of its own, so anything it wants raised, hidden or closed comes
        // back on the listing rather than being pushed at a port this process does not hold.
        if (response.Window is not null)
        {
            if (response.WindowKey is { } key)
            {
                host.Mutate(_ => ViewerSession.SelectKey(_, key));
            }

            Windows.Enqueue(response.Window.Value);
        }

        return true;
    }

    public void Run(Cancel cancel)
    {
        try
        {
            Pump(cancel);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            // This runs on a task nothing awaits until shutdown, so a throw here used to fault it
            // unobserved and leave a live window showing a queue that had stopped being read -
            // the worst of the available outcomes, since it looks exactly like a quiet queue. Said
            // out loud instead, through the same channel an owner that went away uses
            host.Mutate(_ => _ with
            {
                Message = $"The queue owner could not be read: {exception.Message}",
                Exit = true
            });
        }
    }

    void Pump(Cancel cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            if (!Pump(out var sent))
            {
                // The owner went away, so there is nothing left to display and no queue for this
                // window to be reopened from.
                host.Mutate(_ => _ with
                {
                    Message = "The queue owner is no longer running.",
                    Exit = true
                });
                return;
            }

            if (sent)
            {
                // Straight back round, so the window does not lag its own click by an interval.
                continue;
            }

            cancel.WaitHandle.WaitOne(Interval);
        }
    }

    string Send(Outbound command)
    {
        // The long wait matters most here: an accept is the command that takes ten seconds, and
        // failing it at three used to report the owner dead while it was mid apply.
        if (!ViewerClient.TrySend(new(command.Verb, command.Key, command.Body), out var response, port, Wait))
        {
            return "The queue owner is no longer running.";
        }

        return response.Message ?? (response.Ok ? "" : $"{command.Verb} was refused.");
    }

    /// <summary>
    /// Materializes the owner's tracked moves and deletes into displayable entries, reading the
    /// files here on the polling thread — the read seam, keeping the session IO free the way
    /// <see cref="ViewerActions"/> keeps it write free.
    /// <para>
    /// Building an entry reads two files and runs a diff, and this runs five times a second, so
    /// an entry whose paths and stamps are unchanged is reused rather than rebuilt. A stat per
    /// pump is the price of a pane that refreshes when a re-run rewrites the file underneath it.
    /// </para>
    /// </summary>
    List<QueueEntry> ReadChanges(ViewerResponse response)
    {
        var existing = host.State.Queue
            .Where(_ => _.Kind is QueueEntryKind.Move or QueueEntryKind.Delete)
            .ToDictionary(_ => _.Key);
        var changes = new List<QueueEntry>(response.Moves.Count + response.Deletes.Count);
        foreach (var move in response.Moves)
        {
            if (existing.TryGetValue(move.Key, out var entry) &&
                entry.LeftFile == move.Temp &&
                entry.TargetFile == move.Target &&
                entry.LeftStamp == FileSide.StampOf(move.Temp) &&
                entry.RightStamp == FileSide.StampOf(move.Target))
            {
                changes.Add(entry);
                continue;
            }

            changes.Add(QueueEntry.ForMove(
                move.Key,
                move.Name,
                move.Group,
                move.Temp,
                move.Target,
                FileSide.Read(move.Temp),
                FileSide.Read(move.Target)));
        }

        foreach (var delete in response.Deletes)
        {
            if (existing.TryGetValue(delete.Key, out var entry) &&
                entry.LeftFile == delete.File &&
                entry.LeftStamp == FileSide.StampOf(delete.File))
            {
                changes.Add(entry);
                continue;
            }

            changes.Add(QueueEntry.ForDelete(
                delete.Key,
                delete.Name,
                delete.Group,
                delete.File,
                FileSide.Read(delete.File)));
        }

        return changes;
    }
}
