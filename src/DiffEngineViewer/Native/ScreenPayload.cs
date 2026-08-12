/// <summary>
/// Flattens a <see cref="Screen"/> into the blittable form the shim reads. The buffers are reused
/// across frames, so a steady state frame allocates nothing.
/// </summary>
sealed class ScreenPayload
{
    readonly List<byte> strings = [];
    readonly List<DeviewRow> rows = [];
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

    public void Build(Screen screen)
    {
        strings.Clear();
        rows.Clear();
        buttons.Clear();
        queue.Clear();
        menu.Clear();
        menuRow = -1;

        (titleOffset, titleLength) = Add(screen.Title);
        (subtitleOffset, subtitleLength) = Add(screen.Subtitle);
        (statusOffset, statusLength) = Add(screen.Status);

        panes[0] = AddPane(screen.Left);
        panes[1] = AddPane(screen.Right);

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
            // Empty rather than absent when there is no failure: the shim reads a length, and the
            // Failed flag below already says whether there is anything to read.
            var (itemStatus, itemStatusLength) = Add(item.Status ?? "");
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
                    StatusOffset = itemStatus,
                    StatusLength = itemStatusLength
                });
        }

        if (screen.Menu is { } overlay)
        {
            menuRow = overlay.Row;
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
        fixed (DeviewButton* buttonsPtr = CollectionsMarshal.AsSpan(buttons))
        fixed (DeviewQueueItem* queuePtr = CollectionsMarshal.AsSpan(queue))
        fixed (DeviewMenuItem* menuPtr = CollectionsMarshal.AsSpan(menu))
        fixed (DeviewPane* panesPtr = panes)
        {
            var native = Native(stringsPtr, panesPtr, rowsPtr, buttonsPtr, queuePtr, menuPtr);
            return Deview.Present(&native);
        }
    }

    public unsafe int Capture(int width, int height, string pngPath)
    {
        fixed (byte* stringsPtr = CollectionsMarshal.AsSpan(strings))
        fixed (DeviewRow* rowsPtr = CollectionsMarshal.AsSpan(rows))
        fixed (DeviewButton* buttonsPtr = CollectionsMarshal.AsSpan(buttons))
        fixed (DeviewQueueItem* queuePtr = CollectionsMarshal.AsSpan(queue))
        fixed (DeviewMenuItem* menuPtr = CollectionsMarshal.AsSpan(menu))
        fixed (DeviewPane* panesPtr = panes)
        {
            var native = Native(stringsPtr, panesPtr, rowsPtr, buttonsPtr, queuePtr, menuPtr);
            return Deview.Capture(&native, width, height, pngPath);
        }
    }

    unsafe DeviewScreen Native(
        byte* stringsPtr,
        DeviewPane* panesPtr,
        DeviewRow* rowsPtr,
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
            Buttons = buttonsPtr,
            ButtonCount = buttons.Count,
            Queue = queuePtr,
            QueueCount = queue.Count,
            TitleOffset = titleOffset,
            TitleLength = titleLength,
            SubtitleOffset = subtitleOffset,
            SubtitleLength = subtitleLength,
            StatusOffset = statusOffset,
            StatusLength = statusLength,
            Menu = menuPtr,
            MenuCount = menu.Count,
            MenuRow = menuRow
        };

    DeviewPane AddPane(Pane pane)
    {
        var (headerOffset, headerLength) = Add(pane.Header);
        var rowOffset = rows.Count;
        foreach (var row in pane.Rows)
        {
            var (textOffset, textLength) = Add(row.Text);
            rows.Add(
                new()
                {
                    Kind = (int) row.Kind,
                    LineNumber = row.LineNumber ?? -1,
                    TextOffset = textOffset,
                    TextLength = textLength
                });
        }

        return new()
        {
            HeaderOffset = headerOffset,
            HeaderLength = headerLength,
            RowOffset = rowOffset,
            RowCount = pane.Rows.Count,
            ScrollTop = pane.ScrollTop,
            TotalRows = pane.TotalRows
        };
    }

    (int Offset, int Length) Add(string text)
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
