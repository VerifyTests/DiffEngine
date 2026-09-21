/// <summary>
/// An entry's two sides as the panes lay them out, and where each shown row sits in the entry.
/// <para>
/// Every entry has two: all of its rows, and the minimal view, which keeps each change and
/// <see cref="Context"/> unchanged rows either side of it and folds every longer unchanged run into
/// one <see cref="RowKind.Folded"/> row. Both are built with the entry, since they are pure
/// functions of its rows, so switching between them costs nothing.
/// </para>
/// <para>
/// A view, never a filter, the way a folded queue group is one. Scrolling, the scrollbar and change
/// navigation count rows of the view on screen, because those are what is being scrolled. A
/// selection is held in rows of the entry, because it describes text rather than the screen: it
/// survives switching views, and one spanning a folded row copies the lines that row stands for,
/// since those are what lie between its two ends.
/// </para>
/// </summary>
sealed class DiffView
{
    /// <summary>
    /// How many unchanged rows the minimal view keeps either side of a change, and how many rows
    /// navigation leaves showing above a change it brings into view. One number, so a change
    /// navigated to in the minimal view arrives with exactly the context kept for it above it.
    /// </summary>
    public const int Context = 3;

    /// <summary>
    /// For each shown row, the first row of the entry it stands for, ascending. A folded row stands
    /// for every row up to the next one's. Null in the full view, where every row is its own.
    /// </summary>
    readonly int[]? lines;

    /// <summary>
    /// How many rows the entry has, which only the last folded row needs: it stands for every row
    /// up to the end.
    /// </summary>
    readonly int total;

    DiffView(IReadOnlyList<Row> left, IReadOnlyList<Row> right, int[]? lines, int total)
    {
        Left = left;
        Right = right;
        this.lines = lines;
        this.total = total;
        Changes = FindChanges(left, right);
    }

    public IReadOnlyList<Row> Left { get; }

    public IReadOnlyList<Row> Right { get; }

    public int Count => Left.Count;

    /// <summary>
    /// The first row of each run of changed rows, in order.
    /// </summary>
    public IReadOnlyList<int> Changes { get; }

    /// <summary>
    /// Both views of an entry's rows. The minimal one is the full one itself when nothing folds,
    /// which lets switching between them tell that nothing on screen would change.
    /// </summary>
    /// <param name="fold">
    /// False for a picture. Its rows are its properties, three of them and each worth reading, so
    /// there is nothing to leave out.
    /// </param>
    public static (DiffView Full, DiffView Minimal) Build(
        (IReadOnlyList<Row> Left, IReadOnlyList<Row> Right) rows,
        bool fold)
    {
        var full = new DiffView(rows.Left, rows.Right, null, rows.Left.Count);
        if (!fold)
        {
            return (full, full);
        }

        return (full, full.Fold());
    }

    public IReadOnlyList<Row> Side(PaneSide side)
    {
        if (side == PaneSide.Left)
        {
            return Left;
        }

        return Right;
    }

    public bool IsFolded(int row) =>
        Left[row].Kind == RowKind.Folded;

    /// <summary>
    /// The entry row a shown row is, or for a folded row the first of those it stands for.
    /// </summary>
    public int First(int row)
    {
        if (lines is null)
        {
            return row;
        }

        return lines[row];
    }

    /// <summary>
    /// The entry row a shown row is, or for a folded row the last of those it stands for.
    /// </summary>
    public int Last(int row)
    {
        if (lines is null)
        {
            return row;
        }

        if (row + 1 < lines.Length)
        {
            return lines[row + 1] - 1;
        }

        return total - 1;
    }

    /// <summary>
    /// The shown row an entry row is on: itself, or the folded row standing for it.
    /// </summary>
    public int Find(int line)
    {
        if (lines is null)
        {
            return line;
        }

        var index = Array.BinarySearch(lines, line);
        if (index >= 0)
        {
            return index;
        }

        // The complement is the first row starting after the line, so the one before it is the
        // folded row the line is inside.
        return Math.Max(0, ~index - 1);
    }

    /// <summary>
    /// Where the view opens: at its first change, with <see cref="Context"/> rows above it, or at
    /// the top when there is none. A snapshot that fails on line 200 is about line 200, and a reader
    /// handed line 1 had to go looking for it.
    /// </summary>
    public int Opening(int body)
    {
        if (Changes.Count == 0)
        {
            return 0;
        }

        return Target(Changes[0], body);
    }

    /// <summary>
    /// The scroll top that brings the next change into view, or null when none is left below.
    /// <para>
    /// Next is the first change whose place is below the current one, rather than the first change
    /// below the top row. Where a change is put is <see cref="Context"/> rows under the top, so
    /// counting from the top row would take the change already there for the next one and move the
    /// view three rows to arrive where it started. Clamped like any scroll, so the changes the last
    /// page already shows are reached together rather than one row at a time.
    /// </para>
    /// </summary>
    public int? Next(int top, int body)
    {
        foreach (var change in Changes)
        {
            var target = Target(change, body);
            if (target > top)
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>
    /// The scroll top that brings the previous change into view, or null when none is left above.
    /// The mirror of <see cref="Next"/>: from a change it put there, that is the one before it, and
    /// from anywhere else it is the nearest change above the view's own position.
    /// </summary>
    public int? Previous(int top, int body)
    {
        for (var index = Changes.Count - 1; index >= 0; index--)
        {
            var target = Target(Changes[index], body);
            if (target < top)
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>
    /// The scroll top in this view that keeps the reader where they were in
    /// <paramref name="from"/>: the first row on screen that both views show stays on the same
    /// line of the screen. Changes and their context are in both, so it is almost always one of
    /// those, and the change being read stays put while what is around it folds or unfolds.
    /// Unclamped, since clamping is the session's.
    /// </summary>
    public int Follow(DiffView from, int top, int body)
    {
        if (from.Count == 0 ||
            Count == 0)
        {
            return 0;
        }

        for (var offset = 0; offset < body && top + offset < from.Count; offset++)
        {
            var row = top + offset;
            if (from.IsFolded(row))
            {
                continue;
            }

            var shown = Find(from.First(row));
            if (IsFolded(shown))
            {
                continue;
            }

            return shown - offset;
        }

        // Nothing on screen is in both: a screen of lines this view folds, or of folded rows
        // standing for lines this one shows. Whatever this view has for the top line goes at the
        // top.
        return Find(from.First(Math.Clamp(top, 0, from.Count - 1)));
    }

    /// <summary>
    /// A drag's two ends, from rows of this view into rows of the entry, which is what a selection
    /// is held in. A shown row is the entry's own row, text and all, so a column needs nothing.
    /// <para>
    /// A folded row is the lines it stands for, whole: an end on one takes in all of them, from the
    /// first when it is the end the selection starts at and to the last when it is the one it
    /// finishes at. Part of a label is not part of anything. A press with no drag behind it is
    /// still a click, which clears, rather than a selection of everything the row folds.
    /// </para>
    /// <para>
    /// Rows are clamped into the view first, because a head reports where the pointer is rather
    /// than where the rows are. The column a finishing end is pushed to is left for
    /// <see cref="SelectionText.Clamp"/> to pull back to its row's length, since that is the one
    /// step with the text to measure.
    /// </para>
    /// </summary>
    public (int AnchorRow, int AnchorColumn, int FocusRow, int FocusColumn) Unfold(
        int anchorRow,
        int anchorColumn,
        int focusRow,
        int focusColumn)
    {
        if (lines is null)
        {
            return (anchorRow, anchorColumn, focusRow, focusColumn);
        }

        var lastRow = Count - 1;
        anchorRow = Math.Clamp(anchorRow, 0, lastRow);
        focusRow = Math.Clamp(focusRow, 0, lastRow);
        if (anchorRow == focusRow &&
            anchorColumn == focusColumn)
        {
            var (line, column) = End(anchorRow, anchorColumn, start: true);
            return (line, column, line, column);
        }

        var backwards = focusRow < anchorRow ||
                        (focusRow == anchorRow && focusColumn < anchorColumn);
        var anchor = End(anchorRow, anchorColumn, start: !backwards);
        var focus = End(focusRow, focusColumn, start: backwards);
        return (anchor.Line, anchor.Column, focus.Line, focus.Column);
    }

    (int Line, int Column) End(int row, int column, bool start)
    {
        if (!IsFolded(row))
        {
            return (First(row), column);
        }

        if (start)
        {
            return (First(row), 0);
        }

        return (Last(row), int.MaxValue);
    }

    int Target(int change, int body) =>
        Math.Clamp(change - Context, 0, Math.Max(0, Count - body));

    /// <summary>
    /// The minimal view of this one. Every row within <see cref="Context"/> of a change is kept,
    /// and each run of the rest becomes one folded row on both sides, which keeps the two panes
    /// aligned: an unchanged row always has an unchanged row beside it, so the run is the same
    /// length on each.
    /// </summary>
    DiffView Fold()
    {
        var count = Count;
        var kept = new bool[count];
        for (var index = 0; index < count; index++)
        {
            if (!IsChange(Left, Right, index))
            {
                continue;
            }

            var from = Math.Max(0, index - Context);
            var to = Math.Min(count - 1, index + Context);
            for (var near = from; near <= to; near++)
            {
                kept[near] = true;
            }
        }

        var left = new List<Row>(count);
        var right = new List<Row>(count);
        var starts = new List<int>(count);
        var row = 0;
        while (row < count)
        {
            var end = row;
            while (end < count && !kept[end])
            {
                end++;
            }

            // A run of one is shown rather than folded: its row would take the same space as the
            // line it stands for and say less.
            var run = end - row;
            if (run > 1)
            {
                var folded = new Row(null, RowKind.Folded, $"... {run} unchanged lines ...");
                left.Add(folded);
                right.Add(folded);
                starts.Add(row);
                row = end;
                continue;
            }

            left.Add(Left[row]);
            right.Add(Right[row]);
            starts.Add(row);
            row++;
        }

        if (starts.Count == count)
        {
            return this;
        }

        return new(left, right, starts.ToArray(), count);
    }

    static List<int> FindChanges(IReadOnlyList<Row> left, IReadOnlyList<Row> right)
    {
        var changes = new List<int>();
        var inside = false;
        for (var index = 0; index < left.Count; index++)
        {
            var changed = IsChange(left, right, index);
            if (changed && !inside)
            {
                changes.Add(index);
            }

            inside = changed;
        }

        return changes;
    }

    /// <summary>
    /// Either side, rather than the left alone, which is all a text diff needs. An image's rows
    /// are coloured per side, and a row is a change if the reader would see one on either.
    /// </summary>
    static bool IsChange(IReadOnlyList<Row> left, IReadOnlyList<Row> right, int index) =>
        IsChange(left[index]) ||
        (index < right.Count && IsChange(right[index]));

    static bool IsChange(Row row) =>
        row.Kind is not (RowKind.Unchanged or RowKind.Folded);
}
