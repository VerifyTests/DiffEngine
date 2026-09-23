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
    /// What of one row of a view is selected. A shown row is one of the entry's own, so it is
    /// measured as one. A folded row stands for lines rather than being any, so it is highlighted
    /// whole when the selection takes in any of them and not at all otherwise, which is also what
    /// copying it does: the lines between a selection's ends are copied whether or not they are on
    /// screen.
    /// </summary>
    public static SelectionSpan Span(TextSelection? selection, PaneSide side, DiffView view, int row)
    {
        var shown = view.Side(side)[row];
        if (shown.Kind != RowKind.Folded)
        {
            return Span(selection, side, view.First(row), shown.Text);
        }

        if (selection is not { IsEmpty: false } range ||
            range.Side != side ||
            range.Start.Row > view.Last(row) ||
            range.End.Row < view.First(row))
        {
            return default;
        }

        return new(0, Cells(RowText.Flatten(shown.Text)));
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

        var length = Cells(RowText.Flatten(text));
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
            var from = Index(text, span.Start);
            lines.Add(text[from..Index(text, span.Start + span.Length)]);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// One whole side, which is what the copy commands that name a pane hand over. Filler rows are
    /// dropped for the same reason they are dropped from a selection.
    /// <para>
    /// The file's own text rather than the flattened row: a whole side has no columns to keep in
    /// step with the screen, and flattening turned every tab into four spaces, so pasting a copied
    /// side into a verified file changed it.
    /// </para>
    /// </summary>
    public static string All(QueueEntry entry, PaneSide side) =>
        string.Join(
            "\n",
            Rows(entry, side)
                .Where(_ => _.Kind != RowKind.Filler)
                .Select(_ => _.Text));

    /// <summary>
    /// What the status line says while something is selected. The universal statement about a
    /// selection: the heads that can draw a highlight also draw this, and the one that cannot
    /// still says a selection exists and how much of one.
    /// <para>
    /// Counted from the spans rather than by building the text, because this runs every frame for
    /// as long as a selection exists: ctrl+a over a large file built megabytes of string sixty
    /// times a second only to measure it. The counts are what <see cref="Of"/> would produce - one
    /// line per non-filler row, joined by one newline each.
    /// </para>
    /// </summary>
    public static string Summary(TextSelection selection, QueueEntry entry)
    {
        var rows = Rows(entry, selection.Side);
        var (startRow, _) = selection.Start;
        var (endRow, _) = selection.End;
        var lines = 0;
        var length = 0;
        for (var index = Math.Max(0, startRow); index <= endRow && index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Kind == RowKind.Filler)
            {
                continue;
            }

            lines++;
            length += Span(selection, selection.Side, index, row.Text).Length;
        }

        if (lines == 0)
        {
            return "nothing selected";
        }

        // The newlines joining the lines
        length += lines - 1;
        if (length == 0)
        {
            return "nothing selected";
        }

        var characters = $"{length} character{(length == 1 ? "" : "s")}";
        if (lines == 1)
        {
            return $"selected {characters}";
        }

        return $"selected {lines} lines, {characters}";
    }

    /// <summary>
    /// How many cells a row's flattened text takes, which is what a selection's columns count.
    /// <para>
    /// A head reports a drag in cells, and every head draws one code point to a cell: GDI+ draws a
    /// character outside the basic plane one cell wide, as ImGui lays out one glyph per code point.
    /// Counted in UTF-16 units instead, each such character shifted the copy one place from the
    /// highlight, and a selection could end between the two halves of it and copy half a
    /// character. Wide CJK and combining marks still do not fit this; that takes each head
    /// reporting string positions from its own layout.
    /// </para>
    /// </summary>
    public static int Cells(string flattened)
    {
        var cells = 0;
        foreach (var character in flattened)
        {
            if (!char.IsLowSurrogate(character))
            {
                cells++;
            }
        }

        return cells;
    }

    /// <summary>
    /// Where in the flattened text a cell starts: never inside a surrogate pair.
    /// </summary>
    static int Index(string flattened, int cell)
    {
        var index = 0;
        for (var count = 0; count < cell && index < flattened.Length; count++)
        {
            index++;
            if (index < flattened.Length &&
                char.IsLowSurrogate(flattened[index]))
            {
                index++;
            }
        }

        return index;
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
        return Math.Clamp(column, 0, Cells(text));
    }
}
