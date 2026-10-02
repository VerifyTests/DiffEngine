/// <summary>
/// Rows for a side that is described rather than diffed line by line: a picture, or a document shown
/// as its pages. One row per property, each carrying its own side's value and coloured by how it
/// stands against the other: a matching value reads as unchanged and a differing one as modified,
/// which is the same vocabulary a line of text gets. A side with no file at all is filler the whole
/// way down, the way an empty text side is.
/// </summary>
static class PropertyRows
{
    /// <summary>
    /// Wide enough for the longest label plus a gap.
    /// </summary>
    const int labelWidth = 12;

    public static void Add(List<Row> left, List<Row> right, string label, string? leftValue, string? rightValue)
    {
        // Numbered rather than left without a number. Filler is the only row the shim reads a
        // missing number for; a numberless row of any other kind draws its gutter differently on
        // each of the three heads, and these are rows, so numbering them costs nothing.
        var number = left.Count + 1;
        left.Add(Cell(number, label, leftValue, rightValue, RowKind.Added));
        right.Add(Cell(number, label, rightValue, leftValue, RowKind.Removed));
    }

    /// <param name="only">
    /// What this side is when the other has no file: added on the received side, removed on the
    /// expected one, matching which way round <see cref="DiffRows"/> reads.
    /// </param>
    static Row Cell(int number, string label, string? value, string? other, RowKind only)
    {
        if (value is null)
        {
            return new(null, RowKind.Filler, "");
        }

        RowKind kind;
        if (other is null)
        {
            kind = only;
        }
        else
        {
            kind = value == other ? RowKind.Unchanged : RowKind.Modified;
        }

        return new(number, kind, $"{label,-labelWidth}{value}");
    }
}
