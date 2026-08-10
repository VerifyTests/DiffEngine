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
sealed class OwnerLink(SessionHost host, int port, Action<WindowCommand> window)
{
    /// <summary>
    /// Fast enough that someone accepting from the tray sees the window follow, slow enough that
    /// an idle pair is not a busy loop over loopback.
    /// </summary>
    public static TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(200);

    readonly ConcurrentQueue<Outbound> outbound = new();

    record Outbound(ViewerVerb Verb, string? Key);

    public void Post(ViewerVerb verb, string? key) =>
        outbound.Enqueue(new(verb, key));

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

        if (!ViewerClient.TrySend(new(ViewerVerb.ListFull), out var response, port) ||
            !response.Ok)
        {
            return false;
        }

        var pending = InlineQueue.From(response.Items.Select(Read).OfType<PendingInline>());
        host.Mutate(_ => ViewerSession.Sync(_, pending, message));

        // The owner has no window of its own, so anything it wants raised, hidden or closed comes
        // back on the listing rather than being pushed at a port this process does not hold.
        if (response.Window is not null)
        {
            window(response.Window.Value);
        }

        return true;
    }

    public void Run(CancellationToken cancel)
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
        if (!ViewerClient.TrySend(new(command.Verb, command.Key), out var response, port))
        {
            return "The queue owner is no longer running.";
        }

        return response.Message ?? (response.Ok ? "" : $"{command.Verb} was refused.");
    }

    /// <summary>
    /// An item with no patch is a file comparison, which no owner queues and this viewer cannot
    /// display, so it is dropped rather than shown as a blank pane.
    /// </summary>
    static PendingInline? Read(ViewerResponseItem item)
    {
        if (item.Patch is null ||
            !InlinePatchFile.TryParse(item.Patch, out var patch))
        {
            return null;
        }

        return new(patch, item.Status);
    }
}
