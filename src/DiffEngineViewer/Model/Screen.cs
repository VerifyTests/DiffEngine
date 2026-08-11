/// <summary>
/// Everything needed to draw one frame, and nothing else. Built by <see cref="ScreenBuilder"/>,
/// rendered either as text by <see cref="AsciiRenderer"/> or as pixels by the native shim. Both
/// renderers consume the identical structure, which is what makes the text snapshots meaningful.
/// <para>
/// <see cref="Pane.Rows"/> holds only the visible slice, so a renderer never decides what
/// scrolls into view.
/// </para>
/// </summary>
record Screen(
    string Title,
    string Subtitle,
    ViewerMode Mode,
    IReadOnlyList<QueueItem> Queue,
    Pane Left,
    Pane Right,
    IReadOnlyList<Button> Buttons,
    string Status,
    int Columns,
    int Rows,
    // Entries only. Queue.Count stopped meaning this once the column gained header rows and a
    // selection-anchored slice.
    int PendingCount,
    // The open context menu, or null. Sliced to the visible rows like everything else.
    MenuOverlay? Menu = null);
