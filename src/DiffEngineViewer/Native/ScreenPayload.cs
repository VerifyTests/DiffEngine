// ReSharper disable RedundantUnsafeContext
/// <summary>
/// Flattens a <see cref="Screen"/> into the blittable form the shim reads. The buffers are reused
/// across frames, so a steady state frame allocates nothing.
/// </summary>
sealed class ScreenPayload
{
    readonly List<byte> strings = [];
    readonly List<DeviewRow> rows = [];
    readonly List<DeviewSegment> segments = [];
    readonly List<DeviewButton> buttons = [];
    readonly List<DeviewQueueItem> queue = [];
    readonly List<DeviewMenuItem> menu = [];
    readonly DeviewPane[] panes = new DeviewPane[2];
    int titleOffset;
    int titleLength;
    int subtitleOffset;
    int subtitleLength;
    int statusOffset;
    int statusLength;
    int menuRow;
    int menuPane;
    int pendingCount;
    Screen? built;

    /// <summary>
    /// What was encoded, for the tests: a shim reads these through pointers, and nothing here on
    /// the managed side reads them at all.
    /// </summary>
    internal ReadOnlySpan<byte> Strings => CollectionsMarshal.AsSpan(strings);

    internal IReadOnlyList<DeviewRow> Rows => rows;

    internal IReadOnlyList<DeviewSegment> Segments => segments;

    public void Build(Screen screen)
    {
        // The loop hands over the screen it handed over last frame for as long as nothing has
        // happened (ScreenCache), and a screen is immutable, so the buffers already hold this one.
        // Encoding it again was most of what an idle frame cost here.
        if (ReferenceEquals(screen, built))
        {
            return;
        }

        built = screen;
        strings.Clear();
        rows.Clear();
        segments.Clear();
        buttons.Clear();
        queue.Clear();
        menu.Clear();
        menuRow = -1;
        menuPane = -1;
        pendingCount = screen.PendingCount;

        (titleOffset, titleLength) = Add(screen.Title);
        (subtitleOffset, subtitleLength) = Add(screen.Subtitle);
        (statusOffset, statusLength) = Add(screen.Status);

        panes[0] = AddPane(screen.Left, screen.PaneCells);
        panes[1] = AddPane(screen.Right, screen.PaneCells);

        foreach (var button in screen.Buttons)
        {
            var (offset, length) = Add(button.Label);
            buttons.Add(
                new()
                {
                    LabelOffset = offset,
                    LabelLength = length,
                    Flags = button.Enabled ? (int) DeviewButtonFlags.Enabled : 0
                });
        }

        foreach (var item in screen.Queue)
        {
            var (offset, length) = Add(item.Label);
            // Empty rather than absent when the row has nothing to add: the shim reads a length,
            // and a zero one is what tells it to show no tip at all.
            var (tooltip, tooltipLength) = Add(item.Tooltip ?? "");
            var flags = DeviewQueueFlags.None;
            if (item.Selected)
            {
                flags |= DeviewQueueFlags.Selected;
            }

            if (item.Status is not null)
            {
                flags |= DeviewQueueFlags.Failed;
            }

            if (item.Kind == QueueRowKind.Header)
            {
                flags |= DeviewQueueFlags.Header;
            }

            queue.Add(
                new()
                {
                    LabelOffset = offset,
                    LabelLength = length,
                    Flags = (int) flags,
                    TooltipOffset = tooltip,
                    TooltipLength = tooltipLength
                });
        }

        if (screen.Menu is { } overlay)
        {
            menuRow = overlay.Row;
            menuPane = overlay.Pane is { } pane ? (int) pane : -1;
            foreach (var label in overlay.Labels)
            {
                var (offset, length) = Add(label);
                menu.Add(
                    new()
                    {
                        LabelOffset = offset,
                        LabelLength = length
                    });
            }
        }
    }

    public unsafe int Present()
    {
        fixed (byte* stringsPtr = CollectionsMarshal.AsSpan(strings))
        fixed (DeviewRow* rowsPtr = CollectionsMarshal.AsSpan(rows))
        fixed (DeviewSegment* segmentsPtr = CollectionsMarshal.AsSpan(segments))
        fixed (DeviewButton* buttonsPtr = CollectionsMarshal.AsSpan(buttons))
        fixed (DeviewQueueItem* queuePtr = CollectionsMarshal.AsSpan(queue))
        fixed (DeviewMenuItem* menuPtr = CollectionsMarshal.AsSpan(menu))
        fixed (DeviewPane* panesPtr = panes)
        {
            var native = Native(stringsPtr, panesPtr, rowsPtr, segmentsPtr, buttonsPtr, queuePtr, menuPtr);
            return Deview.Present(&native);
        }
    }

    public unsafe int Capture(int width, int height, string pngPath)
    {
        fixed (byte* stringsPtr = CollectionsMarshal.AsSpan(strings))
        fixed (DeviewRow* rowsPtr = CollectionsMarshal.AsSpan(rows))
        fixed (DeviewSegment* segmentsPtr = CollectionsMarshal.AsSpan(segments))
        fixed (DeviewButton* buttonsPtr = CollectionsMarshal.AsSpan(buttons))
        fixed (DeviewQueueItem* queuePtr = CollectionsMarshal.AsSpan(queue))
        fixed (DeviewMenuItem* menuPtr = CollectionsMarshal.AsSpan(menu))
        fixed (DeviewPane* panesPtr = panes)
        {
            var native = Native(stringsPtr, panesPtr, rowsPtr, segmentsPtr, buttonsPtr, queuePtr, menuPtr);
            return Deview.Capture(&native, width, height, pngPath);
        }
    }

    unsafe DeviewScreen Native(
        byte* stringsPtr,
        DeviewPane* panesPtr,
        DeviewRow* rowsPtr,
        DeviewSegment* segmentsPtr,
        DeviewButton* buttonsPtr,
        DeviewQueueItem* queuePtr,
        DeviewMenuItem* menuPtr) =>
        new()
        {
            Strings = stringsPtr,
            StringsLength = strings.Count,
            Panes = panesPtr,
            PaneCount = panes.Length,
            Rows = rowsPtr,
            RowCount = rows.Count,
            Segments = segmentsPtr,
            SegmentCount = segments.Count,
            Buttons = buttonsPtr,
            ButtonCount = buttons.Count,
            Queue = queuePtr,
            QueueCount = queue.Count,
            PendingCount = pendingCount,
            TitleOffset = titleOffset,
            TitleLength = titleLength,
            SubtitleOffset = subtitleOffset,
            SubtitleLength = subtitleLength,
            StatusOffset = statusOffset,
            StatusLength = statusLength,
            Menu = menuPtr,
            MenuCount = menu.Count,
            MenuRow = menuRow,
            MenuPane = menuPane
        };

    DeviewPane AddPane(Pane pane, int cells)
    {
        var (headerOffset, headerLength) = Add(pane.Header);
        var rowOffset = rows.Count;
        foreach (var row in pane.Rows)
        {
            // Flattened, so a tab is drawn as the four cells a selection counts it as, and cut
            // where a pane's cells end, for the reason RowText.Shown gives: marshalled and laid
            // out whole every frame otherwise. In cells, and a pane's rather than the window's.
            // Cut at the window's width in characters, a row of two cell characters was encoded
            // four times as far as any pane could show it, a segment a character.
            //
            // The cut is found by the walk that segments the row, and what is encoded is that
            // much of the string the row already is, so a row costs no string of its own. Only a
            // row much longer than a pane is read from its front first, since flattening and
            // measuring all of a megabyte line is what RowText.Shown is there to avoid
            var text = row.Text.Length > cells * 2 + 2
                ? RowText.Shown(row.Text, cells)
                : RowText.Flatten(row.Text);
            var cut = CellGrid.Segments(text, cells, out var end);
            var (textOffset, textLength) = Add(text.AsSpan(0, end));
            var segmentOffset = segments.Count;
            AddSegments(text, cut, textOffset);
            rows.Add(
                new()
                {
                    Kind = (int) row.Kind,
                    LineNumber = row.LineNumber ?? -1,
                    TextOffset = textOffset,
                    TextLength = textLength,
                    SegmentOffset = segmentOffset,
                    SegmentCount = segments.Count - segmentOffset,
                    SelectStart = row.Selection.Start,
                    SelectLength = row.Selection.Length
                });
        }

        // Empty rather than absent when the side is text or its picture was unreadable: the shim
        // reads a length, and a zero one is what tells it there is nothing to draw.
        var (imagePath, imagePathLength) = Add(pane.Image?.Path ?? "");
        return new()
        {
            HeaderOffset = headerOffset,
            HeaderLength = headerLength,
            RowOffset = rowOffset,
            RowCount = pane.Rows.Count,
            ScrollTop = pane.ScrollTop,
            TotalRows = pane.TotalRows,
            ImagePathOffset = imagePath,
            ImagePathLength = imagePathLength,
            ImageWidth = pane.Image?.Width ?? 0,
            ImageHeight = pane.Image?.Height ?? 0,
            ImagePending = pane.ImagePending ? 1 : 0,
            ImageZoom = (float) (pane.Image?.Zoom ?? 1),
            ImageCenterX = (float) (pane.Image?.CenterX ?? 0.5),
            ImageCenterY = (float) (pane.Image?.CenterY ?? 0.5)
        };
    }

    /// <summary>
    /// The row's <see cref="CellGrid.Segments"/>, as byte ranges of the UTF-8 the row's text was
    /// just written as at <paramref name="textOffset"/>, so no text is written twice.
    /// <para>
    /// Segments come in the order of the text, so where one starts in bytes is where the last one
    /// ended plus whatever lies between them, and the row is measured once. Each used to count the
    /// row's bytes again from its start, which for a row of CJK, a segment a character, was the
    /// row's length squared.
    /// </para>
    /// </summary>
    void AddSegments(string text, IReadOnlyList<CellGrid.Segment> cut, int textOffset)
    {
        var measured = 0;
        var bytes = textOffset;
        foreach (var segment in cut)
        {
            bytes += Encoding.UTF8.GetByteCount(text.AsSpan(measured, segment.Start - measured));
            var length = Encoding.UTF8.GetByteCount(text.AsSpan(segment.Start, segment.Length));
            segments.Add(
                new()
                {
                    TextOffset = bytes,
                    TextLength = length,
                    Column = segment.Column
                });
            bytes += length;
            measured = segment.Start + segment.Length;
        }
    }

    (int Offset, int Length) Add(ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
        {
            return (0, 0);
        }

        var offset = strings.Count;
        var max = Encoding.UTF8.GetMaxByteCount(text.Length);
        CollectionsMarshal.SetCount(strings, offset + max);
        var written = Encoding.UTF8.GetBytes(text, CollectionsMarshal.AsSpan(strings)[offset..]);
        CollectionsMarshal.SetCount(strings, offset + written);
        return (offset, written);
    }
}
