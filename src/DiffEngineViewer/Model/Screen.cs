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
    MenuOverlay? Menu = null)
{
    /// <summary>
    /// The most cells of row text a pane can show, for a renderer that is handed a row's text
    /// before it knows where its panes are: no more than this of a row is worth handing it.
    /// <para>
    /// A bound rather than a pane's width, which is each head's to decide: its queue column can be
    /// dragged, and its gutter is as wide as its line numbers. What every head agrees on is that
    /// the two panes share the window equally, so neither is wider than half of it. Half of
    /// <see cref="Columns"/> rounded up, since the window's width in cells is rounded down, and
    /// one more for the cell a pane's edge can cut through. The gutter in front of the text is
    /// room to spare on top of that.
    /// </para>
    /// </summary>
    public int PaneCells =>
        (Columns + 1) / 2 + 1;
}
