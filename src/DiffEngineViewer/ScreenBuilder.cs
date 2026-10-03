using System.Globalization;

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

    /// <summary>
    /// The rows of text a pane has room for: the whole body, or its top half when a document's page
    /// is drawn under its text. The heads place a picture under whatever rows a pane has, so
    /// slicing fewer is all it takes to give the page the rest. Everything that scrolls a pane
    /// counts these; the queue column keeps the whole body.
    /// </summary>
    public static int PaneRows(SessionState state)
    {
        var body = BodyRows(state);
        if (state.Drawing == DrawingView.Both &&
            state.Current is { IsDocument: true } &&
            !state.ShowsProperties)
        {
            return Math.Max(1, body / 2);
        }

        return body;
    }

    public static Screen Build(SessionState state)
    {
        var body = BodyRows(state);
        var paneRows = PaneRows(state);
        var current = state.Current;
        var view = state.View;
        var selection = state.LiveSelection;
        var left = BuildPane(
            Header(state, current, PaneSide.Left),
            view,
            state.ScrollTop,
            paneRows,
            Picture(state, current, PaneSide.Left),
            selection,
            PaneSide.Left) with
        {
            ImagePending = PagePending(state, current, PaneSide.Left)
        };
        var right = BuildPane(
            Header(state, current, PaneSide.Right),
            view,
            state.ScrollTop,
            paneRows,
            Picture(state, current, PaneSide.Right),
            selection,
            PaneSide.Right) with
        {
            ImagePending = PagePending(state, current, PaneSide.Right)
        };

        var queue = BuildQueue(state, body, out var top);
        return new(
            Title: current?.Name ?? "nothing pending",
            Subtitle: BuildSubtitle(state),
            Mode: state.Mode,
            Queue: queue,
            Left: left,
            Right: right,
            Buttons: BuildButtons(state, paneRows),
            Status: BuildStatus(state, current, paneRows),
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
        ImagePane? picture,
        TextSelection? selection,
        PaneSide side)
    {
        if (view is null)
        {
            return new(header, [], scrollTop, 0, picture);
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

        return new(header, visible, scrollTop, rows.Count, picture);
    }

    /// <summary>
    /// What a side draws as: its image, or the page of its document being read. A document is only
    /// drawn once a page of it has landed, and never in the text view.
    /// </summary>
    static ImagePane? Picture(SessionState state, QueueEntry? entry, PaneSide side)
    {
        if (entry is null)
        {
            return null;
        }

        if (!entry.IsDocument)
        {
            return BuildImage(side == PaneSide.Left ? entry.LeftImage : entry.RightImage);
        }

        if (state.Drawing == DrawingView.Text ||
            DocumentPages.Of(state, Document(entry, side)) is not { } rendering)
        {
            return null;
        }

        var index = DocumentPages.Current(state);
        if (index >= rendering.Pages.Count)
        {
            return null;
        }

        var page = rendering.Pages[index];
        return new(page.Path, page.Width, page.Height, page.Hash);
    }

    /// <summary>
    /// Whether the page a side would show is still to come: waiting its turn, or being drawn and
    /// not that far yet. Not once drawing has finished or failed, when the header says why there is
    /// no page, and never in the text view, which has nowhere to put one.
    /// </summary>
    static bool PagePending(SessionState state, QueueEntry? entry, PaneSide side)
    {
        if (entry is not { IsDocument: true } ||
            state.Drawing == DrawingView.Text ||
            Document(entry, side) is not { Hash: not null } document)
        {
            return false;
        }

        if (DocumentPages.Of(state, document) is not { } rendering)
        {
            return true;
        }

        return !rendering.Complete &&
               DocumentPages.Current(state) >= rendering.Pages.Count;
    }

    /// <summary>
    /// The side's own header, and while its pages are on screen which one is showing, in the form a
    /// variant's framework takes. Built here rather than carried by the entry, so the copy menu,
    /// which names a pane by the entry's header, still names the file.
    /// </summary>
    static string Header(SessionState state, QueueEntry? entry, PaneSide side)
    {
        if (entry is null)
        {
            return side == PaneSide.Left ? "received" : "expected";
        }

        var header = side == PaneSide.Left ? entry.LeftHeader : entry.RightHeader;
        if (state.Drawing == DrawingView.Text ||
            Document(entry, side) is not { Hash: not null } document ||
            PageLabel(document, DocumentPages.Of(state, document), DocumentPages.Current(state)) is not { } label)
        {
            return header;
        }

        return $"{header} ({label})";
    }

    static string? PageLabel(DocumentFile document, Rendering? rendering, int index)
    {
        if (rendering is null)
        {
            return "drawing";
        }

        if (index < rendering.Pages.Count)
        {
            // One picture, so naming its page says nothing.
            if (document.IsDrawn)
            {
                return null;
            }

            if (rendering.Complete)
            {
                return $"page {index + 1} of {rendering.Pages.Count}";
            }

            return $"page {index + 1}";
        }

        if (rendering.Failure is not null)
        {
            return "not drawn";
        }

        if (!rendering.Complete)
        {
            return "drawing";
        }

        return $"no page {index + 1}";
    }

    static DocumentFile? Document(QueueEntry entry, PaneSide side) =>
        side == PaneSide.Left ? entry.LeftDocument : entry.RightDocument;

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
        // Nothing that changes the queue while an accept-all is working through it. Kept in their
        // slots, disabled, which is what a footer does for a queue that drained
        var idle = state.Progress is null;
        var enabled = current is not null && idle;
        if (state.Mode == ViewerMode.File)
        {
            return
            [
                new("Accept", enabled, CommandKind.Accept),
                new("Close", true, CommandKind.Quit),
                ..ViewButtons(state, body),
                ..DocumentButtons(state)
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
            new("Accept all", state.Queue.Count > 0 && idle, CommandKind.AcceptAll)
        };

        // Ahead of the variant button, which comes and goes with the entry selected, so these
        // keep their place in the footer whatever is on screen.
        buttons.AddRange(ViewButtons(state, body));
        buttons.AddRange(DocumentButtons(state));

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
        if (TurnsPages(state, out var current))
        {
            // A document seen as its pages changes page by page, so that is what moving between
            // changes moves between.
            var page = DocumentPages.Current(state);
            var (left, right) = DocumentPages.Of(state, current);
            var differing = DocumentPages.Differing(current, left, right);
            yield return new("Prev change", differing.Any(_ => _ < page), CommandKind.PreviousChange);
            yield return new("Next change", differing.Any(_ => _ > page), CommandKind.NextChange);
        }
        else
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
        }

        yield return new(
            state.Minimal ? "All lines" : "Changes only",
            state.Current is { IsImage: false } && !state.ShowsProperties,
            CommandKind.ToggleMinimal);
    }

    /// <summary>
    /// Whether the current entry is a document shown as its pages alone, which is when moving
    /// between changes moves between pages rather than rows.
    /// </summary>
    public static bool TurnsPages(SessionState state, [NotNullWhen(true)] out QueueEntry? current)
    {
        current = state.Current;
        return current is { IsDocument: true } &&
               state.Drawing == DrawingView.Picture;
    }

    /// <summary>
    /// For a document: the view switch, labelled with what it switches to, and the page buttons.
    /// Those stay in the footer whenever a paged document is on screen, disabled when there is no
    /// page to turn to, so a click resolved by position cannot land on a button that moved under
    /// it as pages arrived.
    /// </summary>
    static IEnumerable<Button> DocumentButtons(SessionState state)
    {
        if (state.Current is not { IsDocument: true } current)
        {
            yield break;
        }

        yield return new(
            state.Drawing switch
            {
                DrawingView.Both => "Picture only",
                DrawingView.Picture => "Text only",
                _ => "Text and picture"
            },
            true,
            CommandKind.ToggleDrawing);

        if ((current.LeftDocument ?? current.RightDocument)?.IsDrawn == true)
        {
            yield break;
        }

        var pages = state.Drawing != DrawingView.Text;
        var page = DocumentPages.Current(state);
        var (left, right) = DocumentPages.Of(state, current);
        yield return new("Prev page", pages && page > 0, CommandKind.PreviousPage);
        yield return new("Next page", pages && page < DocumentPages.Count(left, right) - 1, CommandKind.NextPage);
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
        // Over whatever the last command said, which while a batch runs is at best "Waiting for
        // the queue owner." - and saying that over a list that is visibly shrinking is not news
        if (state.Progress is { } progress)
        {
            return progress.Describe();
        }

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

        if (current.IsDocument)
        {
            return DocumentStatus(state, current, body);
        }

        return Lines(state, current, body);
    }

    static string Lines(SessionState state, QueueEntry current, int body)
    {
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

    /// <summary>
    /// The pair first, as for an image, then whatever the view on screen can add: where the text
    /// is, and which page is showing and which differ. Everything a renderer with no picture can
    /// say about the pages is said here, which is what keeps the ASCII snapshots a description of
    /// every head rather than of the ones that draw.
    /// </summary>
    static string DocumentStatus(SessionState state, QueueEntry current, int body)
    {
        if (current.LeftDocument is not { } left)
        {
            return $"only {current.RightHeader} exists";
        }

        if (current.RightDocument is not { } right)
        {
            return $"only {current.LeftHeader} exists";
        }

        if (left.Hash is null ||
            right.Hash is null)
        {
            return "documents could not be compared";
        }

        if (left.Hash == right.Hash)
        {
            return "documents are identical";
        }

        var parts = new List<string>(3);
        if (state.Drawing != DrawingView.Picture)
        {
            parts.Add(TextStatus(state, current, left, right, body));
        }

        if (state.Drawing != DrawingView.Text)
        {
            parts.AddRange(PageStatus(state, current, left.IsDrawn));
        }

        return string.Join(", ", parts);
    }

    static string TextStatus(SessionState state, QueueEntry current, DocumentFile left, DocumentFile right, int body)
    {
        if (left.Reading ||
            right.Reading)
        {
            return "reading text";
        }

        if (left.Unreadable is { } leftReason)
        {
            return $"could not read the text of {current.LeftHeader}: {Reason(leftReason)}";
        }

        if (right.Unreadable is { } rightReason)
        {
            return $"could not read the text of {current.RightHeader}: {Reason(rightReason)}";
        }

        return Lines(state, current, body);
    }

    /// <summary>
    /// Its first line, without its closing full stop: it is one clause of a status line joined by
    /// commas, and a status line is one line. A first line ending in a colon only introduces the
    /// lines that cannot follow it, so its last sentence goes with them: Morph names a font it could
    /// not find, then "Checked:" and every folder it looked in.
    /// </summary>
    static string Reason(string reason)
    {
        var end = reason.AsSpan().IndexOfAny('\r', '\n');
        var line = (end < 0 ? reason : reason[..end]).TrimEnd();
        if (line.EndsWith(':'))
        {
            var sentence = line.LastIndexOf(". ", StringComparison.Ordinal);
            line = sentence < 0 ? line.TrimEnd(':') : line[..sentence];
        }

        return line.TrimEnd('.', ' ');
    }

    static IEnumerable<string> PageStatus(SessionState state, QueueEntry current, bool drawn)
    {
        var (left, right) = DocumentPages.Of(state, current);
        if ((Failure(current.LeftHeader, left) ?? Failure(current.RightHeader, right)) is { } failure)
        {
            yield return failure;
            yield break;
        }

        var count = DocumentPages.Count(left, right);
        if (left is not { Complete: true } ||
            right is not { Complete: true })
        {
            yield return count == 0 ? "drawing" : $"drawing page {count + 1}";
            yield break;
        }

        var differing = DocumentPages.Differing(current, left, right);
        if (drawn)
        {
            yield return differing.Count == 0 ? "drawn the same" : "drawings differ";
            yield break;
        }

        yield return $"page {DocumentPages.Current(state) + 1} of {count}";
        yield return differing.Count switch
        {
            0 => "every page draws the same",
            1 => $"page {differing[0] + 1} differs",
            _ => $"pages {Pages(differing)} differ"
        };
    }

    static string? Failure(string header, Rendering? rendering) =>
        rendering is { Failure: { } reason } ? $"could not draw {header}: {Reason(reason)}" : null;

    /// <summary>
    /// "2, 5 and 9", and past a handful how many more, so the status line still fits a footer.
    /// </summary>
    static string Pages(IReadOnlyList<int> pages)
    {
        const int shown = 5;
        var names = pages
            .Take(shown)
            .Select(_ => (_ + 1).ToString(CultureInfo.InvariantCulture))
            .ToList();
        if (pages.Count > shown)
        {
            return $"{string.Join(", ", names)} and {pages.Count - shown} more";
        }

        return $"{string.Join(", ", names[..^1])} and {names[^1]}";
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
