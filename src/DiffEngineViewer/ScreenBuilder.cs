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
        var view = state.View;
        var selection = state.LiveSelection;
        var left = BuildPane(
            current?.LeftHeader ?? "received",
            view,
            state.ScrollTop,
            body,
            current?.LeftImage,
            selection,
            PaneSide.Left);
        var right = BuildPane(
            current?.RightHeader ?? "expected",
            view,
            state.ScrollTop,
            body,
            current?.RightImage,
            selection,
            PaneSide.Right);

        var queue = BuildQueue(state, body, out var top);
        return new(
            Title: current?.Name ?? "nothing pending",
            Subtitle: BuildSubtitle(state),
            Mode: state.Mode,
            Queue: queue,
            Left: left,
            Right: right,
            Buttons: BuildButtons(state, body),
            Status: BuildStatus(state, current, body),
            Columns: state.Columns,
            Rows: state.Rows,
            PendingCount: state.Mode == ViewerMode.File ? 0 : state.Queue.Count,
            Menu: BuildMenu(state, queue.Count, top));
    }

    /// <summary>
    /// The open menu, anchored into the visible slice. An anchor that scrolled out draws nothing:
    /// the commands that scroll also close the menu, so this is a frame of belt and braces.
    /// </summary>
    static MenuOverlay? BuildMenu(SessionState state, int visibleRows, int top)
    {
        if (state.Menu is not { } menu)
        {
            return null;
        }

        var anchor = menu.Row - top;
        if (anchor < 0 ||
            anchor >= visibleRows)
        {
            return null;
        }

        return new(anchor, menu.Items.Select(_ => _.Label).ToList());
    }

    static Pane BuildPane(
        string header,
        DiffView? view,
        int scrollTop,
        int body,
        ImageFile? image,
        TextSelection? selection,
        PaneSide side)
    {
        if (view is null)
        {
            return new(header, [], scrollTop, 0, BuildImage(image));
        }

        var rows = view.Side(side);
        var end = Math.Min(scrollTop + body, rows.Count);
        var visible = new List<Row>(Math.Max(0, end - scrollTop));
        for (var index = Math.Max(0, scrollTop); index < end; index++)
        {
            var row = rows[index];
            // Attached to the visible slice rather than carried beside it, so a head draws a row
            // and its highlight from one thing and the frame comparison that decides whether to
            // repaint already covers both.
            var span = SelectionText.Span(selection, side, view, index);
            visible.Add(span.Length == 0 ? row : row with { Selection = span });
        }

        return new(header, visible, scrollTop, rows.Count, BuildImage(image));
    }

    /// <summary>
    /// Offered to a head only once the bytes have been read and recognized. A file that could not
    /// be read, or is not a format the viewer knows, has already said so in its rows, and asking a
    /// head to try anyway would put the answer to that in each renderer rather than here.
    /// </summary>
    static ImagePane? BuildImage(ImageFile? image)
    {
        if (image is not { Header: { HasSize: true } header } file)
        {
            return null;
        }

        return new(file.Path, header.Width, header.Height, file.Hash);
    }

    static IReadOnlyList<QueueItem> BuildQueue(SessionState state, int body, out int top)
    {
        // File mode is one window per invocation, so it has no queue to show.
        if (state.Mode == ViewerMode.File)
        {
            top = 0;
            return [];
        }

        return QueueProjection.Visible(state, body, out top);
    }

    static IReadOnlyList<Button> BuildButtons(SessionState state, int body)
    {
        var current = state.Current;
        var enabled = current is not null;
        if (state.Mode == ViewerMode.File)
        {
            return
            [
                new("Accept", enabled, CommandKind.Accept),
                new("Close", true, CommandKind.Quit),
                ..ViewButtons(state, body)
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

        // Ahead of the variant button, which comes and goes with the entry selected, so these
        // keep their place in the footer whatever is on screen.
        buttons.AddRange(ViewButtons(state, body));

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

    /// <summary>
    /// The buttons that move around the entry on screen rather than act on it, the same in both
    /// modes. Each change button is enabled only when there is a change to go to, so the pair
    /// also say whether any are left above or below, which otherwise takes scrolling to find out.
    /// <para>
    /// The fold is labelled with what it switches to. A picture's rows are its properties, none of
    /// which the minimal view leaves out, so for one there is nothing for it to do.
    /// </para>
    /// </summary>
    static IEnumerable<Button> ViewButtons(SessionState state, int body)
    {
        var view = state.View;
        yield return new(
            "Prev change",
            view?.Previous(state.ScrollTop, body) is not null,
            CommandKind.PreviousChange);
        yield return new(
            "Next change",
            view?.Next(state.ScrollTop, body) is not null,
            CommandKind.NextChange);
        yield return new(
            state.Minimal ? "All lines" : "Changes only",
            state.Current is { IsImage: false },
            CommandKind.ToggleMinimal);
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

        // Above the warning and the line count, because a selection is what the reader is doing
        // right now and both of those are still true the moment it goes. This is also the whole
        // of what a renderer with no way to invert text can say about one, which is why it is
        // stated here rather than left to the highlight.
        if (state.LiveSelection is { IsEmpty: false } selection)
        {
            return SelectionText.Summary(selection, current);
        }

        if (current.Warning is not null)
        {
            return current.Warning;
        }

        // A line count says nothing about a picture, and whether the two are the same file is the
        // one thing the rows cannot say: it belongs to the pair rather than to either side.
        if (current.IsImage)
        {
            return ImageStatus(current);
        }

        // Rows of the entry rather than of the view. In the minimal view the rows on screen run from
        // one line to another with folds between, and which stretch of the file that is says more
        // than how far down a list of rows it is - "lines 1-1 of 1" beside a fold of forty lines
        // said nothing true. In the full view the two are the same numbers.
        var view = current.View(state.Minimal);
        if (view.Count == 0)
        {
            return "lines 0-0 of 0";
        }

        var top = Math.Clamp(state.ScrollTop, 0, view.Count - 1);
        var bottom = Math.Min(top + body, view.Count) - 1;
        return $"lines {view.First(top) + 1}-{view.Last(bottom) + 1} of {current.TotalRows}";
    }

    static string ImageStatus(QueueEntry entry)
    {
        if (entry.LeftImage is not { } left)
        {
            return $"only {entry.RightHeader} exists";
        }

        if (entry.RightImage is not { } right)
        {
            return $"only {entry.LeftHeader} exists";
        }

        // A hash is missing when the bytes never arrived, which is not the same answer as "these
        // are not the same picture" and must not be reported as one.
        if (left.Hash is null ||
            right.Hash is null)
        {
            return "images could not be compared";
        }

        return left.Hash == right.Hash ? "images are identical" : "images differ";
    }
}
