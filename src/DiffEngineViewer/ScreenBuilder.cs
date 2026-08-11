/// <summary>
/// Projects a <see cref="SessionState"/> into the frame to draw. Owns the viewport slicing, so
/// the text and pixel renderers never disagree about what is on screen.
/// </summary>
static class ScreenBuilder
{
    /// <summary>
    /// Lines the frame spends on borders, the title, the pane headers and the footer.
    /// </summary>
    public const int Chrome = 8;

    public static int BodyRows(SessionState state) =>
        Math.Max(1, state.Rows - Chrome);

    public static Screen Build(SessionState state)
    {
        var body = BodyRows(state);
        var current = state.Current;
        var left = BuildPane(
            current?.LeftHeader ?? "received",
            current?.LeftRows ?? [],
            state.ScrollTop,
            body);
        var right = BuildPane(
            current?.RightHeader ?? "expected",
            current?.RightRows ?? [],
            state.ScrollTop,
            body);

        return new(
            Title: current?.Name ?? "nothing pending",
            Subtitle: BuildSubtitle(state),
            Mode: state.Mode,
            Queue: BuildQueue(state, body),
            Left: left,
            Right: right,
            Buttons: BuildButtons(state),
            Status: BuildStatus(state, current, body),
            Columns: state.Columns,
            Rows: state.Rows,
            PendingCount: state.Mode == ViewerMode.File ? 0 : state.Queue.Count);
    }

    static Pane BuildPane(string header, IReadOnlyList<Row> rows, int scrollTop, int body)
    {
        var end = Math.Min(scrollTop + body, rows.Count);
        var visible = new List<Row>(Math.Max(0, end - scrollTop));
        for (var index = Math.Max(0, scrollTop); index < end; index++)
        {
            visible.Add(rows[index]);
        }

        return new(header, visible, scrollTop, rows.Count);
    }

    static IReadOnlyList<QueueItem> BuildQueue(SessionState state, int body)
    {
        // File mode is one window per invocation, so it has no queue to show.
        if (state.Mode == ViewerMode.File)
        {
            return [];
        }

        return QueueProjection.Visible(state, body);
    }

    static IReadOnlyList<Button> BuildButtons(SessionState state)
    {
        var current = state.Current;
        var enabled = current is not null;
        if (state.Mode == ViewerMode.File)
        {
            return
            [
                new("Accept", enabled, CommandKind.Accept),
                new("Close", true, CommandKind.Quit)
            ];
        }

        // Named per kind, because "Accept delete" is a destructive act worth naming as itself.
        var accept = current?.Kind switch
        {
            QueueEntryKind.Move => "Accept move",
            QueueEntryKind.Delete => "Accept delete",
            _ => "Accept"
        };

        var buttons = new List<Button>
        {
            new(accept, enabled, CommandKind.Accept),
            new("Discard", enabled, CommandKind.Discard),
            // Enabled from one, not two. Shift+A has always accepted a queue of one, and a button
            // that refuses what the key it names does reads as a bug rather than a nicety.
            new("Accept all", state.Queue.Count > 0, CommandKind.AcceptAll)
        };

        if (current is { Kind: QueueEntryKind.Inline, Conflicted: true })
        {
            var variant = current.Variants[current.SelectedVariant];
            buttons.Add(new(
                $"Variant {current.SelectedVariant + 1}/{current.Variants.Count}: {variant.Label ?? "unknown"}",
                true,
                CommandKind.NextVariant));
        }

        return buttons;
    }

    static string BuildSubtitle(SessionState state)
    {
        if (state.Mode == ViewerMode.File)
        {
            return "diff";
        }

        if (state.Queue.Count == 0)
        {
            return "inline";
        }

        return $"inline   {state.Selected + 1} of {state.Queue.Count}";
    }

    static string BuildStatus(SessionState state, QueueEntry? current, int body)
    {
        if (state.Message is not null)
        {
            return state.Message;
        }

        if (current is null)
        {
            return "nothing pending";
        }

        if (current.Warning is not null)
        {
            return current.Warning;
        }

        var total = current.TotalRows;
        var from = total == 0 ? 0 : state.ScrollTop + 1;
        var to = Math.Min(state.ScrollTop + body, total);
        return $"lines {from}-{to} of {total}";
    }
}
