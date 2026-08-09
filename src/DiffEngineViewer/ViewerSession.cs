/// <summary>
/// The application logic, as pure state transitions. Every screen the user can reach is
/// reproducible by replaying commands, which is what the snapshot tests do.
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
    /// Adds an item, or replaces the existing one with the same key so a re-run of the same test
    /// updates its entry rather than appending a duplicate.
    /// </summary>
    public static SessionState Enqueue(SessionState state, QueueEntry entry)
    {
        var queue = state.Queue.ToList();
        var existing = queue.FindIndex(_ => _.Key == entry.Key);
        if (existing >= 0)
        {
            queue[existing] = entry;
            var scroll = existing == state.Selected ? 0 : state.ScrollTop;
            return Clamp(state with
            {
                Queue = queue,
                ScrollTop = scroll
            });
        }

        queue.Add(entry);
        return Clamp(state with
        {
            Queue = queue,
            Selected = state.Selected < 0 ? 0 : state.Selected
        });
    }

    /// <summary>
    /// Drops the item for a key, used when a previously failing test starts passing.
    /// </summary>
    public static SessionState Settle(SessionState state, string key)
    {
        var queue = state.Queue.Where(_ => _.Key != key).ToList();
        if (queue.Count == state.Queue.Count)
        {
            return state;
        }

        return Remove(state, queue, null);
    }

    public static SessionState Apply(SessionState state, Command command, ViewerActions actions)
    {
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
                return Accept(state, actions);
            case CommandKind.AcceptAll:
                return AcceptAll(state, actions);
            case CommandKind.Discard:
                return Discard(state);
            case CommandKind.DiscardAll:
                return Remove(state, [], $"Discarded {state.Queue.Count}");
            case CommandKind.Quit:
                return state with { Exit = true };
            default:
                return state;
        }
    }

    static SessionState Accept(SessionState state, ViewerActions actions)
    {
        var current = state.Current;
        if (current is null)
        {
            return state;
        }

        var (removed, message) = ApplyOne(current, actions);
        if (removed)
        {
            var queue = state.Queue.ToList();
            queue.RemoveAt(state.Selected);
            return Remove(state, queue, message);
        }

        // Kept pending so it can be retried, for example when an IDE holds the file open.
        var kept = state.Queue.ToList();
        kept[state.Selected] = current with { Status = message };
        return state with
        {
            Queue = kept,
            Message = message
        };
    }

    static SessionState AcceptAll(SessionState state, ViewerActions actions)
    {
        var remaining = new List<QueueEntry>();
        var accepted = 0;
        string? failure = null;
        foreach (var entry in state.Queue)
        {
            var (removed, message) = ApplyOne(entry, actions);
            if (removed)
            {
                accepted++;
                continue;
            }

            failure = message;
            remaining.Add(entry with { Status = message });
        }

        var summary = failure is null
            ? $"Accepted {accepted}"
            : $"Accepted {accepted}, {remaining.Count} failed. {failure}";
        return Remove(state, remaining, summary);
    }

    static SessionState Discard(SessionState state)
    {
        var current = state.Current;
        if (current is null)
        {
            return state;
        }

        var queue = state.Queue.ToList();
        queue.RemoveAt(state.Selected);
        return Remove(state, queue, $"Discarded {current.Name}");
    }

    static (bool removed, string message) ApplyOne(QueueEntry entry, ViewerActions actions)
    {
        if (entry.Patch is not null)
        {
            var result = actions.ApplyInline(entry.Patch);
            return result.Status switch
            {
                InlineApplyStatus.Applied => (true, $"Applied {entry.Name}"),
                InlineApplyStatus.AlreadyApplied => (true, $"Already applied {entry.Name}"),
                // The patch is stale. A re-run regenerates a fresh one, so drop it rather than
                // leaving an item that can never succeed.
                InlineApplyStatus.NotFound => (true, $"{entry.Name} source changed, re-run the test"),
                _ => (false, result.Message ?? $"Failed to apply {entry.Name}")
            };
        }

        if (entry.LeftFile is null ||
            entry.TargetFile is null)
        {
            return (false, $"Nothing to accept for {entry.Name}");
        }

        try
        {
            actions.CopyFile(entry.LeftFile, entry.TargetFile);
            return (true, $"Accepted {entry.Name}");
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
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
