/// <summary>
/// What a <see cref="TextSelection"/> covers, as spans to draw and as text to copy.
/// <para>
/// Everything here reads the flattened row text rather than the file's own. A tab is four cells on
/// screen and one character in the file, and the columns a selection carries were pointed at on
/// screen, so measuring anything else would highlight one run and copy another.
/// </para>
/// </summary>
static class SelectionText
{
    public static IReadOnlyList<Row> Rows(QueueEntry entry, PaneSide side) =>
        side == PaneSide.Left ? entry.LeftRows : entry.RightRows;

    public static string Header(QueueEntry entry, PaneSide side) =>
        side == PaneSide.Left ? entry.LeftHeader : entry.RightHeader;

    /// <summary>
    /// The ends put in reading order and pulled back inside the rows that exist, so everything
    /// downstream can index without checking.
    /// </summary>
    public static TextSelection Clamp(TextSelection selection, QueueEntry entry)
    {
        var rows = Rows(entry, selection.Side);
        return selection with
        {
            AnchorRow = ClampRow(selection.AnchorRow, rows),
            AnchorColumn = ClampColumn(selection.AnchorRow, selection.AnchorColumn, rows),
            FocusRow = ClampRow(selection.FocusRow, rows),
            FocusColumn = ClampColumn(selection.FocusRow, selection.FocusColumn, rows)
        };
    }

    /// <summary>
    /// What of one row is selected, for a row of the visible slice. Empty for the other side and
    /// for a row outside the selection, which is most of them.
    /// </summary>
    public static SelectionSpan Span(TextSelection? selection, PaneSide side, int row, string text)
    {
        if (selection is not { IsEmpty: false } range ||
            range.Side != side)
        {
            return default;
        }

        var (startRow, startColumn) = range.Start;
        var (endRow, endColumn) = range.End;
        if (row < startRow ||
            row > endRow)
        {
            return default;
        }

        var length = RowText.Flatten(text).Length;
        var from = row == startRow ? Math.Min(startColumn, length) : 0;
        var to = row == endRow ? Math.Min(endColumn, length) : length;
        return to <= from ? default : new(from, to - from);
    }

    /// <summary>
    /// The selected text, ready for the clipboard. Empty when the selection covers nothing.
    /// <para>
    /// Filler rows are left out rather than copied as blank lines. They are the padding that keeps
    /// the two panes aligned, not content, so pasting them back would put lines into a file that
    /// were never in one.
    /// </para>
    /// </summary>
    public static string Of(TextSelection selection, QueueEntry entry)
    {
        var rows = Rows(entry, selection.Side);
        var (startRow, _) = selection.Start;
        var (endRow, _) = selection.End;
        var lines = new List<string>();
        for (var index = Math.Max(0, startRow); index <= endRow && index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Kind == RowKind.Filler)
            {
                continue;
            }

            var text = RowText.Flatten(row.Text);
            var span = Span(selection, selection.Side, index, row.Text);
            lines.Add(span.Length == 0 ? "" : text.Substring(span.Start, span.Length));
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// One whole side, which is what the copy commands that name a pane hand over. Filler rows are
    /// dropped for the same reason they are dropped from a selection.
    /// </summary>
    public static string All(QueueEntry entry, PaneSide side) =>
        string.Join(
            "\n",
            Rows(entry, side)
                .Where(_ => _.Kind != RowKind.Filler)
                .Select(_ => RowText.Flatten(_.Text)));

    /// <summary>
    /// What the status line says while something is selected. The universal statement about a
    /// selection: the heads that can draw a highlight also draw this, and the one that cannot
    /// still says a selection exists and how much of one.
    /// </summary>
    public static string Summary(TextSelection selection, QueueEntry entry)
    {
        var text = Of(selection, entry);
        if (text.Length == 0)
        {
            return "nothing selected";
        }

        var lines = text.Count(_ => _ == '\n') + 1;
        var characters = $"{text.Length} character{(text.Length == 1 ? "" : "s")}";
        return lines == 1 ? $"selected {characters}" : $"selected {lines} lines, {characters}";
    }

    static int ClampRow(int row, IReadOnlyList<Row> rows) =>
        Math.Clamp(row, 0, Math.Max(0, rows.Count - 1));

    static int ClampColumn(int row, int column, IReadOnlyList<Row> rows)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        var text = RowText.Flatten(rows[ClampRow(row, rows)].Text);
        return Math.Clamp(column, 0, text.Length);
    }
}
