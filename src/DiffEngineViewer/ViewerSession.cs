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
    /// Adds a patch, or folds it into the existing entry for the same call site so a re-run of
    /// the same test updates its entry rather than appending a duplicate.
    /// </summary>
    public static SessionState EnqueueInline(SessionState state, InlinePatch patch)
    {
        var key = InlineKey.For(patch.SourceFile, patch.LineHint);
        var replacedCurrent = state.Current?.Key == key;
        var queue = Project(state, Pending(state).Enqueue(patch));
        // Grouping can reorder the list, so the selection follows its key rather than its index.
        var currentKey = state.Current?.Key;
        var selected = currentKey is null ? 0 : IndexOf(queue, currentKey);
        return Clamp(state with
        {
            Queue = queue,
            Selected = selected < 0 ? 0 : selected,
            // The text under the reader just changed, so start it at the top again. Only when it
            // is the item on screen; folding into one further down the list should not move
            // anything.
            ScrollTop = replacedCurrent ? 0 : state.ScrollTop,
            // The open menu indexes the queue it was opened over, which just changed.
            Menu = null
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
    /// Drops the item for a key, used when a previously failing test starts passing — or, with an
    /// origin, just that framework's variant of it.
    /// </summary>
    public static SessionState Settle(SessionState state, string key, string? origin = null)
    {
        // Nothing can settle a file comparison: settles arrive over the socket, and file mode runs
        // without one. Guarded rather than assumed, because Pending would dereference the null
        // patch a file entry carries.
        if (state.Mode != ViewerMode.Inline)
        {
            return state;
        }

        var pending = Pending(state);
        var settled = pending.Settle(key, origin);
        if (ReferenceEquals(settled, pending))
        {
            return state;
        }

        return Remove(state, Project(state, settled), null);
    }

    /// <summary>
    /// Replaces the queue with what its owner reports, for a viewer that is displaying rather
    /// than owning: the inline entries plus the owner's tracked moves and deletes, already
    /// materialized by the poller. Selection follows the key, so something accepted elsewhere in
    /// the list does not silently change what is on screen, and the scroll position is only given
    /// up when the item being read has gone.
    /// </summary>
    public static SessionState Sync(
        SessionState state,
        InlineQueue pending,
        IReadOnlyList<QueueEntry> changes,
        string? message)
    {
        var entries = new List<QueueEntry>(Project(state, pending));
        entries.AddRange(changes);
        var queue = QueueProjection.Order(entries);
        var key = state.Current?.Key;
        var selected = key is null ? -1 : IndexOf(queue, key);
        return Clamp(state with
        {
            Queue = queue,
            Selected = selected < 0 ? state.Selected : selected,
            ScrollTop = selected < 0 ? 0 : state.ScrollTop,
            Message = message ?? state.Message,
            // Nothing left to show, and this window is not what is holding the queue.
            Exit = queue.Count == 0,
            // The open menu indexes the queue it was opened over, which was just replaced.
            Menu = null
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

    /// <summary>
    /// Opens the context menu for a visible queue row. Opening on an entry selects it first, the
    /// way every menu-driven UI reads a right-click, so the menu's commands act on what is
    /// highlighted.
    /// </summary>
    public static SessionState OpenMenu(SessionState state, int visibleRow)
    {
        var body = ScreenBuilder.BodyRows(state);
        var visible = QueueProjection.Visible(state, body, out var top);
        if (visibleRow < 0 ||
            visibleRow >= visible.Count)
        {
            return state with { Menu = null };
        }

        var row = visible[visibleRow];
        var fullRow = top + visibleRow;
        if (row.Kind == QueueRowKind.Header)
        {
            if (row.GroupName is null ||
                row.GroupMembers is null)
            {
                return state with { Menu = null };
            }

            // Solution headers carry entries of every kind; a test header only ever spans inline
            // entries from one file.
            var items = state.Queue[row.GroupMembers[0]].TestName == row.GroupName
                ? ContextMenu.ForTest(row.GroupName)
                : ContextMenu.ForSolution(row.GroupName);
            return state with
            {
                Menu = new(fullRow, items, row.GroupMembers)
            };
        }

        var selected = Select(state, row.EntryIndex);
        return selected with
        {
            Menu = new(fullRow, ContextMenu.ForEntry(selected.Queue[row.EntryIndex]), [row.EntryIndex])
        };
    }

    public static SessionState Apply(SessionState state, Command command, ViewerActions actions)
    {
        // Any command closes the menu: acting is what its own items do, and everything else —
        // a scroll, a click, a key — is the user moving on. The group commands still need what
        // the menu described, so it is captured before it goes.
        var menu = state.Menu;
        if (menu is not null)
        {
            state = state with { Menu = null };
        }

        var inline = state.Mode == ViewerMode.Inline;
        var body = ScreenBuilder.BodyRows(state);
        switch (command.Kind)
        {
            case CommandKind.AcceptGroup:
                return menu is null || !inline ? state : AcceptGroup(state, menu, actions);
            case CommandKind.DiscardGroup:
                return menu is null || !inline ? state : DiscardGroup(state, menu);
            case CommandKind.RevealSource:
                return Reveal(state, actions);
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
            case CommandKind.ScrollTo:
                return Scroll(state, command.Index);
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
            case CommandKind.NextVariant:
                return NextVariant(state);
            case CommandKind.Quit:
                return state with { Exit = true };
            default:
                return state;
        }
    }

    static SessionState AcceptInline(SessionState state, ViewerActions actions)
    {
        var current = state.Current;
        if (current is not { Kind: QueueEntryKind.Inline })
        {
            return state;
        }

        var pending = Pending(state);
        InlineQueue accepted;
        string? message;
        if (current.Conflicted &&
            current.Variants[current.SelectedVariant].Origins is { Count: > 0 } origins &&
            origins[0] is { } origin)
        {
            // The reviewer picked a side by cycling to it; accepting applies exactly what is on
            // screen and resolves the whole call site.
            accepted = pending.Accept(current.Key, origin, actions.ApplyInline, out message);
        }
        else
        {
            accepted = pending.Accept(current.Key, actions.ApplyInline, out message);
        }

        var queue = Project(state, accepted);
        if (accepted.Count < pending.Count)
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

    /// <summary>
    /// Accepts every inline member of the group a header's menu described, skipping conflicted
    /// entries the way accept-all does. By key rather than index, because each accept rebuilds
    /// the queue underneath the next.
    /// </summary>
    static SessionState AcceptGroup(SessionState state, MenuState menu, ViewerActions actions)
    {
        var members = menu.Members
            .Where(_ => _ >= 0 && _ < state.Queue.Count)
            .Select(_ => state.Queue[_])
            .Where(_ => _.Kind == QueueEntryKind.Inline)
            .ToList();
        var queue = Pending(state);
        var accepted = 0;
        var failed = 0;
        var conflicted = 0;
        string? failure = null;
        foreach (var member in members)
        {
            if (member.Conflicted)
            {
                conflicted++;
                continue;
            }

            var before = queue.Count;
            queue = queue.Accept(member.Key, actions.ApplyInline, out var outcome);
            if (queue.Count < before)
            {
                accepted++;
                continue;
            }

            if (outcome is not null)
            {
                failed++;
                failure = outcome;
            }
        }

        // The same wording accept-all uses, so a group sweep and a full sweep read alike.
        var builder = new StringBuilder($"Accepted {accepted}");
        if (failed > 0)
        {
            builder.Append($", {failed} failed");
        }

        if (conflicted > 0)
        {
            builder.Append(conflicted == 1
                ? ", 1 conflict needs review"
                : $", {conflicted} conflicts need review");
        }

        if (failure is not null)
        {
            builder.Append($". {failure}");
        }

        return Remove(state, Project(state, queue), builder.ToString());
    }

    static SessionState DiscardGroup(SessionState state, MenuState menu)
    {
        var keys = menu.Members
            .Where(_ => _ >= 0 && _ < state.Queue.Count)
            .Select(_ => state.Queue[_])
            .Where(_ => _.Kind == QueueEntryKind.Inline)
            .Select(_ => _.Key)
            .ToList();
        var queue = Pending(state);
        foreach (var key in keys)
        {
            queue = queue.Discard(key, out _);
        }

        return Remove(state, Project(state, queue), $"Discarded {keys.Count}");
    }

    /// <summary>
    /// Shows the current entry's file in the platform's file manager: the source for an inline
    /// entry, the target for a move, the doomed file for a delete, the left file in file mode.
    /// </summary>
    static SessionState Reveal(SessionState state, ViewerActions actions)
    {
        var current = state.Current;
        var path = current?.Kind switch
        {
            QueueEntryKind.Inline => current.Patch?.SourceFile,
            QueueEntryKind.Move => current.TargetFile,
            QueueEntryKind.Delete or QueueEntryKind.File => current.LeftFile,
            _ => null
        };
        if (path is not null)
        {
            actions.Reveal(path);
        }

        return state;
    }

    /// <summary>
    /// Rebuilds the current entry showing its next variant. A no-op unless the entry is a
    /// conflicted inline one, and purely view state: it changes what is being read, never a file.
    /// </summary>
    static SessionState NextVariant(SessionState state)
    {
        var current = state.Current;
        if (current is not { Kind: QueueEntryKind.Inline, Conflicted: true })
        {
            return state;
        }

        var rebuilt = QueueEntry.ForInline(
            new(current.Variants, current.Status),
            (current.SelectedVariant + 1) % current.Variants.Count);
        var queue = state.Queue.ToList();
        queue[state.Selected] = rebuilt;
        // The text under the reader changed, the same reason a fold resets it.
        return Clamp(state with
        {
            Queue = queue,
            ScrollTop = 0
        });
    }

    /// <summary>
    /// Points the current selection's entry at the variant carrying an origin, for a wire accept
    /// that named one. A key or origin that is not here leaves the state alone.
    /// </summary>
    public static SessionState SelectVariant(SessionState state, string origin)
    {
        var current = state.Current;
        if (current is not { Kind: QueueEntryKind.Inline })
        {
            return state;
        }

        for (var index = 0; index < current.Variants.Count; index++)
        {
            if (!current.Variants[index].Origins.Contains(origin))
            {
                continue;
            }

            if (index == current.SelectedVariant)
            {
                return state;
            }

            var queue = state.Queue.ToList();
            queue[state.Selected] = QueueEntry.ForInline(new(current.Variants, current.Status), index);
            return Clamp(state with
            {
                Queue = queue,
                ScrollTop = 0
            });
        }

        return state;
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
    /// disagreeing. Tracked moves and deletes belong to the tray, not the queue, so only the
    /// inline entries round-trip.
    /// </summary>
    static InlineQueue Pending(SessionState state) =>
        InlineQueue.From(state.Queue
            .Where(_ => _.Kind == QueueEntryKind.Inline)
            .Select(_ => new PendingInline(_.Variants, _.Status)));

    /// <summary>
    /// And back onto the display list, in display order. Building an entry runs the diff, so an
    /// entry already built for the same variants is reused, keeping its selected variant, and
    /// only its status carried across.
    /// <para>
    /// Compared by value rather than by reference, because an attached viewer parses fresh patch
    /// instances out of every refresh and would otherwise re-diff the whole queue five times a
    /// second.
    /// </para>
    /// </summary>
    static IReadOnlyList<QueueEntry> Project(SessionState state, InlineQueue queue)
    {
        var existing = state.Queue
            .Where(_ => _.Kind == QueueEntryKind.Inline)
            .ToDictionary(_ => _.Key);
        var entries = new List<QueueEntry>(queue.Count);
        foreach (var pending in queue.Items)
        {
            if (existing.TryGetValue(pending.Key, out var entry))
            {
                if (VariantsMatch(entry.Variants, pending.Variants))
                {
                    entries.Add(entry.Status == pending.Status ? entry : entry with { Status = pending.Status });
                    continue;
                }

                // The variants changed, so the entry rebuilds, but what the reader had cycled to
                // survives where it still exists.
                entries.Add(QueueEntry.ForInline(pending, entry.SelectedVariant));
                continue;
            }

            entries.Add(QueueEntry.ForInline(pending));
        }

        return QueueProjection.Order(entries);
    }

    static bool VariantsMatch(IReadOnlyList<InlineVariant> left, IReadOnlyList<InlineVariant> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!left[index].Patch.Matches(right[index].Patch) ||
                !left[index].Origins.SequenceEqual(right[index].Origins))
            {
                return false;
            }
        }

        return true;
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
            Exit = queue.Count == 0,
            Menu = null
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
