/// <summary>
/// The application logic, as pure state transitions. Every screen the user can reach is
/// reproducible by replaying commands, which is what the snapshot tests do.
/// <para>
/// What changes the inline queue is delegated to <see cref="InlineQueue"/>, the same
/// implementation DiffEngineTray hosts, so what enqueueing, settling or accepting means cannot
/// differ between the two. What stays here is the view: selection, scrolling, and the projection
/// back onto <see cref="QueueEntry"/>.
/// </para>
/// <para>
/// File mode keeps its own accept, because copying left over right is not something the tray ever
/// queues. It is deliberately not a queue: <c>RunFile</c> enqueues one comparison and runs without
/// a socket, so nothing arrives after it and every acting command is that one entry.
/// </para>
/// </summary>
static class ViewerSession
{
    public static SessionState Resize(SessionState state, int columns, int rows) =>
        Clamp(state with
        {
            Columns = Math.Max(40, columns),
            Rows = Math.Max(10, rows)
        });

    /// <summary>
    /// Adds a patch, or replaces the existing one for the same call site so a re-run of the same
    /// test updates its entry rather than appending a duplicate.
    /// </summary>
    public static SessionState EnqueueInline(SessionState state, InlinePatch patch)
    {
        var replaced = IndexOf(state.Queue, InlineKey.For(patch.SourceFile, patch.LineHint));
        var queue = Project(state, Pending(state).Enqueue(patch));
        if (replaced < 0)
        {
            return Clamp(state with
            {
                Queue = queue,
                Selected = state.Selected < 0 ? 0 : state.Selected
            });
        }

        // The text under the reader just changed, so start it at the top again. Only when it is
        // the item on screen; replacing one further down the list should not move anything.
        return Clamp(state with
        {
            Queue = queue,
            ScrollTop = replaced == state.Selected ? 0 : state.ScrollTop
        });
    }

    /// <summary>
    /// The single entry a file comparison shows. Nothing arrives after it, because file mode runs
    /// without a socket.
    /// </summary>
    public static SessionState EnqueueFile(SessionState state, QueueEntry entry) =>
        Clamp(state with
        {
            Queue = [..state.Queue, entry],
            Selected = state.Selected < 0 ? 0 : state.Selected
        });

    /// <summary>
    /// Drops the item for a key, used when a previously failing test starts passing.
    /// </summary>
    public static SessionState Settle(SessionState state, string key)
    {
        // Nothing can settle a file comparison: settles arrive over the socket, and file mode runs
        // without one. Guarded rather than assumed, because Pending would dereference the null
        // patch a file entry carries.
        if (state.Mode != ViewerMode.Inline)
        {
            return state;
        }

        var pending = Pending(state);
        var settled = pending.Settle(key);
        if (ReferenceEquals(settled, pending))
        {
            return state;
        }

        return Remove(state, Project(state, settled), null);
    }

    /// <summary>
    /// Replaces the queue with what its owner reports, for a viewer that is displaying rather
    /// than owning. Selection follows the key, so something accepted elsewhere in the list does
    /// not silently change what is on screen, and the scroll position is only given up when the
    /// item being read has gone.
    /// </summary>
    public static SessionState Sync(SessionState state, InlineQueue pending, string? message)
    {
        var queue = Project(state, pending);
        var key = state.Current?.Key;
        var selected = key is null ? -1 : IndexOf(queue, key);
        return Clamp(state with
        {
            Queue = queue,
            Selected = selected < 0 ? state.Selected : selected,
            ScrollTop = selected < 0 ? 0 : state.ScrollTop,
            Message = message ?? state.Message,
            // Nothing left to show, and this window is not what is holding the queue.
            Exit = queue.Count == 0
        });
    }

    /// <summary>
    /// Selects by key rather than index, for a queue owner asking that a particular item be the
    /// one on screen. A key that is not here leaves the selection alone, because a listing and the
    /// command that came with it can disagree by one refresh.
    /// </summary>
    public static SessionState SelectKey(SessionState state, string key)
    {
        var index = IndexOf(state.Queue, key);
        return index < 0 ? state : Select(state, index);
    }

    /// <summary>
    /// For commands that only move the view. Accept and accept all reach disk, so they go through
    /// the overload that takes the actions; passing one here throws rather than doing nothing.
    /// </summary>
    public static SessionState Apply(SessionState state, Command command) =>
        Apply(state, command, ViewerActions.None);

    public static SessionState Apply(SessionState state, Command command, ViewerActions actions)
    {
        var inline = state.Mode == ViewerMode.Inline;
        var body = ScreenBuilder.BodyRows(state);
        switch (command.Kind)
        {
            case CommandKind.ScrollUp:
                return Scroll(state, state.ScrollTop - 1);
            case CommandKind.ScrollDown:
                return Scroll(state, state.ScrollTop + 1);
            case CommandKind.PageUp:
                return Scroll(state, state.ScrollTop - body);
            case CommandKind.PageDown:
                return Scroll(state, state.ScrollTop + body);
            case CommandKind.ScrollHome:
                return Scroll(state, 0);
            case CommandKind.ScrollEnd:
                return Scroll(state, int.MaxValue);
            case CommandKind.NextChange:
                return Scroll(state, NextChange(Rows(state), state.ScrollTop));
            case CommandKind.PreviousChange:
                return Scroll(state, PreviousChange(Rows(state), state.ScrollTop));
            case CommandKind.NextItem:
                return Select(state, state.Selected + 1);
            case CommandKind.PreviousItem:
                return Select(state, state.Selected - 1);
            case CommandKind.SelectItem:
                return Select(state, command.Index);
            case CommandKind.Accept:
                return inline ? AcceptInline(state, actions) : AcceptFile(state, actions);
            case CommandKind.AcceptAll:
                // File mode shows one comparison and cannot grow, so accepting all of it is
                // accepting it. Reachable through shift+A even though the button is disabled for
                // a single item, so it behaves rather than being a hole.
                return inline ? AcceptAllInline(state, actions) : AcceptFile(state, actions);
            case CommandKind.Discard:
                return inline ? DiscardInline(state) : DiscardFile(state);
            case CommandKind.DiscardAll:
                return inline
                    ? Remove(state, Project(state, Pending(state).DiscardAll(out var summary)), summary)
                    : DiscardFile(state);
            case CommandKind.Quit:
                return state with { Exit = true };
            default:
                return state;
        }
    }

    static SessionState AcceptInline(SessionState state, ViewerActions actions)
    {
        var current = state.Current;
        if (current is null)
        {
            return state;
        }

        var accepted = Pending(state).Accept(current.Key, actions.ApplyInline, out var message);
        var queue = Project(state, accepted);
        if (accepted.Count < state.Queue.Count)
        {
            return Remove(state, queue, message);
        }

        // Kept pending so it can be retried, for example when an IDE holds the file open.
        return state with
        {
            Queue = queue,
            Message = message
        };
    }

    static SessionState AcceptAllInline(SessionState state, ViewerActions actions)
    {
        var accepted = Pending(state).AcceptAll(actions.ApplyInline, out var message);
        return Remove(state, Project(state, accepted), message);
    }

    static SessionState DiscardInline(SessionState state)
    {
        var current = state.Current;
        if (current is null)
        {
            return state;
        }

        var discarded = Pending(state).Discard(current.Key, out var message);
        return Remove(state, Project(state, discarded), message);
    }

    /// <summary>
    /// Copies left over right, which is the whole of accepting in file mode.
    /// <para>
    /// The paths are known non null: a file entry only comes from
    /// <see cref="QueueEntry.ForFiles"/>, whose parameters are not nullable, and only file mode
    /// reaches here.
    /// </para>
    /// </summary>
    static SessionState AcceptFile(SessionState state, ViewerActions actions)
    {
        var current = state.Current;
        if (current is null)
        {
            return state;
        }

        try
        {
            actions.CopyFile(current.LeftFile!, current.TargetFile!);
        }
        catch (Exception exception)
        {
            // Kept so it can be retried, for example while the target is locked.
            return state with
            {
                Queue = [current with { Status = exception.Message }],
                Message = exception.Message
            };
        }

        return Remove(state, [], $"Accepted {current.Name}");
    }

    static SessionState DiscardFile(SessionState state)
    {
        var current = state.Current;
        if (current is null)
        {
            return state;
        }

        return Remove(state, [], $"Discarded {current.Name}");
    }

    /// <summary>
    /// The queue as its owner holds it. The display list is the only copy the viewer keeps, so
    /// this is rebuilt from it rather than stored beside it, which is what stops the two from
    /// disagreeing.
    /// </summary>
    static InlineQueue Pending(SessionState state) =>
        InlineQueue.From(state.Queue.Select(_ => new PendingInline(_.Patch!, _.Status)));

    /// <summary>
    /// And back onto the display list. Building an entry runs the diff, so an entry already built
    /// for the same patch is reused and only its status carried across.
    /// <para>
    /// Compared by value rather than by reference, because an attached viewer parses fresh patch
    /// instances out of every refresh and would otherwise re-diff the whole queue five times a
    /// second.
    /// </para>
    /// </summary>
    static IReadOnlyList<QueueEntry> Project(SessionState state, InlineQueue queue)
    {
        var existing = state.Queue.ToDictionary(_ => _.Key);
        var entries = new List<QueueEntry>(queue.Count);
        foreach (var pending in queue.Items)
        {
            if (existing.TryGetValue(pending.Key, out var entry) &&
                entry.Patch is not null &&
                entry.Patch.Matches(pending.Patch))
            {
                entries.Add(entry.Status == pending.Status ? entry : entry with { Status = pending.Status });
                continue;
            }

            var built = QueueEntry.ForInline(pending.Patch);
            entries.Add(pending.Status is null ? built : built with { Status = pending.Status });
        }

        return entries;
    }

    static int IndexOf(IReadOnlyList<QueueEntry> queue, string key)
    {
        for (var index = 0; index < queue.Count; index++)
        {
            if (queue[index].Key == key)
            {
                return index;
            }
        }

        return -1;
    }

    static SessionState Remove(SessionState state, IReadOnlyList<QueueEntry> queue, string? message) =>
        Clamp(state with
        {
            Queue = queue,
            ScrollTop = 0,
            Message = message,
            // Nothing left to manage, so the window has no reason to stay open.
            Exit = queue.Count == 0
        });

    static SessionState Select(SessionState state, int index)
    {
        if (state.Queue.Count == 0 ||
            index < 0 ||
            index >= state.Queue.Count)
        {
            return state;
        }

        return Clamp(state with
        {
            Selected = index,
            ScrollTop = 0
        });
    }

    static SessionState Scroll(SessionState state, int top) =>
        Clamp(state with { ScrollTop = top });

    static SessionState Clamp(SessionState state)
    {
        var selected = state.Queue.Count == 0
            ? -1
            : Math.Clamp(state.Selected, 0, state.Queue.Count - 1);
        var total = selected < 0 ? 0 : state.Queue[selected].TotalRows;
        var maxScroll = Math.Max(0, total - ScreenBuilder.BodyRows(state));
        return state with
        {
            Selected = selected,
            ScrollTop = Math.Clamp(state.ScrollTop, 0, maxScroll)
        };
    }

    static IReadOnlyList<Row> Rows(SessionState state) =>
        state.Current?.LeftRows ?? [];

    static bool IsChange(Row row) =>
        row.Kind != RowKind.Unchanged;

    static int NextChange(IReadOnlyList<Row> rows, int from)
    {
        var index = from;
        // Step off the block currently at the top of the viewport before looking for the next one.
        while (index < rows.Count &&
               IsChange(rows[index]))
        {
            index++;
        }

        while (index < rows.Count &&
               !IsChange(rows[index]))
        {
            index++;
        }

        return index < rows.Count ? index : from;
    }

    static int PreviousChange(IReadOnlyList<Row> rows, int from)
    {
        var index = Math.Min(from, rows.Count) - 1;
        while (index >= 0 &&
               IsChange(rows[index]))
        {
            index--;
        }

        while (index >= 0 &&
               !IsChange(rows[index]))
        {
            index--;
        }

        if (index < 0)
        {
            return from;
        }

        // Land on the first row of the block, not its last.
        while (index > 0 &&
               IsChange(rows[index - 1]))
        {
            index--;
        }

        return index;
    }
}
