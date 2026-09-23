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

    /// <summary>
    /// How long a posted command may take, which for an accept-all is as long as the queue is
    /// long. The listing taken beside it is what says whether the owner is still there, so this
    /// only has to bound one that took the command and wedged; fifteen seconds said a long queue's
    /// owner had gone while it was part way through accepting it.
    /// </summary>
    public static TimeSpan SendWait { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// What is waiting to be sent, each as the step that sends it and says what came of it. A step
    /// rather than a message, because a group accept is several messages whose last ones depend on
    /// what the first ones did.
    /// </summary>
    readonly ConcurrentQueue<Func<string>> outbound = new();

    /// <summary>
    /// Set by a post and by a send finishing, so the loop answers either at once rather than on
    /// its next interval.
    /// </summary>
    readonly AutoResetEvent wake = new(false);

    record Outbound(ViewerVerb Verb, string? Key, string? Body);

    public void Post(ViewerVerb verb, string? key, string? body = null) =>
        Enqueue(() => Send(new(verb, key, body)));

    /// <summary>
    /// "Accept all in" a group of someone else's queue: its moves, then its snapshots, then - only
    /// once every snapshot has been written - its deletes.
    /// <para>
    /// A snapshot moving inline arrives as a patch plus a delete of the verified file it replaces.
    /// Posting an accept per member sent the delete whether or not the patch landed, and the owner
    /// carries out a single accept as asked, so a patch it could not write cost the snapshot both
    /// copies. The owning viewer's group accept and every owner's accept-all hold the deletes for
    /// that; this is the same rule on the side of a viewer that owns nothing.
    /// </para>
    /// <para>
    /// Whether each snapshot landed is the reply's <see cref="ViewerResponse.Written"/>, not ok and
    /// not a listing: a patch whose call site moved is attempted, so ok, and dropped, so gone from
    /// the listing just as an applied one is. A reply without it - an owner that predates it, or
    /// none at all - holds the deletes too, because a delete is the one thing not safe to guess
    /// about. The deletes held are still queued, to be accepted on their own.
    /// </para>
    /// </summary>
    public void PostAcceptGroup(IReadOnlyList<string> moves, IReadOnlyList<string> snapshots, IReadOnlyList<string> deletes) =>
        Enqueue(() => AcceptGroup(moves, snapshots, deletes));

    public const string DeletesHeld = "Deletes kept: a snapshot in this group was not written, and a file being deleted may be the only copy of it left. Accept them on their own to delete them anyway.";

    void Enqueue(Func<string> send)
    {
        outbound.Enqueue(send);
        wake.Set();
    }

    string AcceptGroup(IReadOnlyList<string> moves, IReadOnlyList<string> snapshots, IReadOnlyList<string> deletes)
    {
        var message = "";
        foreach (var key in moves)
        {
            message = Send(new(ViewerVerb.Accept, key, null));
        }

        var landed = true;
        foreach (var key in snapshots)
        {
            message = Send(new(ViewerVerb.Accept, key, null), out var written);
            landed &= written;
        }

        if (deletes.Count == 0)
        {
            return message;
        }

        if (!landed)
        {
            return DeletesHeld;
        }

        foreach (var key in deletes)
        {
            message = Send(new(ViewerVerb.Accept, key, null));
        }

        return message;
    }

    public bool Pump() =>
        Pump(out _);

    /// <summary>
    /// Send everything posted since the last pass, then read the queue back. Returns false when
    /// the owner has gone, reported rather than acted on so the caller can decide whether that
    /// means "do not open a window" or "close the one that is open".
    /// <para>
    /// One after the other, which is what a first read and a test want. <see cref="Run"/> lists
    /// beside a send instead, so a long one does not stop the window following the owner.
    /// </para>
    /// </summary>
    public bool Pump(out bool sent) =>
        List(SendPosted(out sent));

    /// <summary>
    /// Everything posted, in order, and what the last of it said. Null when nothing was.
    /// </summary>
    string? SendPosted(out bool sent)
    {
        sent = false;
        string? message = null;
        while (outbound.TryDequeue(out var send))
        {
            sent = true;
            message = send();
        }

        return message;
    }

    /// <summary>
    /// Reads the queue back into the session, with <paramref name="message"/> being what a
    /// finished send said, to arrive in the same mutation as the listing that shows its effect.
    /// </summary>
    bool List(string? message)
    {
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
        host.Mutate(_ => ViewerSession.Sync(_, pending, changes, message, response.Progress));

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

    /// <summary>
    /// Sends go on a task of their own, and the listing carries on beside them.
    /// <para>
    /// Send, then list, was one step, and an accept-all is one send for as long as the owner takes
    /// to apply the whole queue. The window said "Waiting for the queue owner." over a list that
    /// did not move for all of that, and then emptied at once. Listed alongside, the window follows
    /// the owner as it goes - entries leaving as they land, and the owner's own count of how far
    /// it has got in the status line.
    /// </para>
    /// <para>
    /// Only this loop lists, and a finished send's message rides the first listing taken after it,
    /// so the two still reach the session together, and a listing from partway through can never
    /// land after the one that shows the result.
    /// </para>
    /// </summary>
    void Pump(Cancel cancel)
    {
        Task<string?>? sending = null;
        while (!cancel.IsCancellationRequested)
        {
            string? message = null;
            if (sending is { IsCompleted: true })
            {
                message = sending.Result;
                sending = null;
            }

            if (sending is null &&
                !outbound.IsEmpty)
            {
                sending = Task.Run(() => SendPosted(out _), Cancel.None);
                // Once it has finished rather than as it is finishing, so the pass it wakes finds
                // it complete instead of waiting out another interval for the message
                sending.ContinueWith(
                    _ => wake.Set(),
                    Cancel.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            if (!List(message))
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

            WaitHandle.WaitAny([cancel.WaitHandle, wake], Interval);
        }
    }

    string Send(Outbound command) =>
        Send(command, out _);

    /// <param name="written">The reply's <see cref="ViewerResponse.Written"/>, false when it had none.</param>
    string Send(Outbound command, out bool written)
    {
        written = false;
        // The long wait matters most here: an accept is the command that takes ten seconds, and
        // failing it at three used to report the owner dead while it was mid apply.
        if (!ViewerClient.TrySend(new(command.Verb, command.Key, command.Body), out var response, port, SendWait))
        {
            return "The queue owner is no longer running.";
        }

        written = response.Written == true;
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
