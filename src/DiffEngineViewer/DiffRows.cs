/// <summary>
/// Turns two texts into two equal length row lists, padded with <see cref="RowKind.Filler"/> so
/// the panes stay vertically aligned.
/// </summary>
static class DiffRows
{
    /// <summary>
    /// Left is the received side and right the expected one. Within a changed block the removed
    /// and added lines pair up as <see cref="RowKind.Modified"/>, and whichever side has more
    /// carries the rest against filler.
    /// <para>
    /// Lines compare exactly. A snapshot that fails only on indentation or a trailing space has to
    /// come back as a change, or the panes draw no markers, NextChange finds nothing, and the
    /// reviewer is shown a failure with no visible difference. Whitespace is exactly what the F#
    /// layout convention is about.
    /// </para>
    /// </summary>
    public static (IReadOnlyList<Row> Left, IReadOnlyList<Row> Right) Build(string leftText, string rightText)
    {
        var lines = TextDiff.Compute(expected: rightText, received: leftText);
        var left = new List<Row>(lines.Count);
        var right = new List<Row>(lines.Count);
        var index = 0;
        while (index < lines.Count)
        {
            var line = lines[index];
            if (line.Kind == DiffLineKind.Unchanged)
            {
                left.Add(new(line.ReceivedLine, RowKind.Unchanged, line.Text));
                right.Add(new(line.ExpectedLine, RowKind.Unchanged, line.Text));
                index++;
                continue;
            }

            var removedStart = index;
            while (index < lines.Count &&
                   lines[index].Kind == DiffLineKind.Removed)
            {
                index++;
            }

            var addedStart = index;
            while (index < lines.Count &&
                   lines[index].Kind == DiffLineKind.Added)
            {
                index++;
            }

            var removed = addedStart - removedStart;
            var added = index - addedStart;
            for (var row = 0; row < Math.Max(removed, added); row++)
            {
                left.Add(Side(lines, addedStart, row, added, removed, RowKind.Added));
                right.Add(Side(lines, removedStart, row, removed, added, RowKind.Removed));
            }
        }

        return (left, right);
    }

    /// <summary>
    /// One side's row of a changed block: modified while the other side still has a line to pair
    /// with, then added or removed, then filler once this side has run out.
    /// </summary>
    static Row Side(IReadOnlyList<DiffLine> lines, int start, int row, int count, int otherCount, RowKind unpaired)
    {
        if (row >= count)
        {
            return new(null, RowKind.Filler, "");
        }

        var line = lines[start + row];
        var number = line.ReceivedLine ?? line.ExpectedLine;
        if (row < otherCount)
        {
            return new(number, RowKind.Modified, line.Text);
        }

        return new(number, unpaired, line.Text);
    }
}
