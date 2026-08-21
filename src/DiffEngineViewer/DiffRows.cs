using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

/// <summary>
/// Turns two texts into two equal length row lists, padded with <see cref="RowKind.Filler"/> so
/// the panes stay vertically aligned.
/// </summary>
static class DiffRows
{
    public static (IReadOnlyList<Row> Left, IReadOnlyList<Row> Right) Build(string leftText, string rightText)
    {
        // DiffPlex is old/new oriented. Left is the received (new) side, right the expected (old).
        // ignoreWhiteSpace defaults to true, which is wrong for a snapshot: a test that fails only
        // on indentation or a trailing space came back Unchanged on every row, so the panes drew no
        // markers, NextChange found nothing, and the reviewer was shown a failure with no visible
        // difference. Whitespace is exactly what the F# layout convention is about.
        var model = SideBySideDiffBuilder.Diff(
            rightText,
            leftText,
            ignoreWhiteSpace: false,
            ignoreCase: false);
        return (Convert(model.NewText.Lines), Convert(model.OldText.Lines));
    }

    static List<Row> Convert(List<DiffPiece> lines)
    {
        var rows = new List<Row>(lines.Count);
        foreach (var line in lines)
        {
            rows.Add(new(line.Position, Kind(line.Type), line.Text ?? ""));
        }

        return rows;
    }

    static RowKind Kind(ChangeType type) =>
        type switch
        {
            ChangeType.Inserted => RowKind.Added,
            ChangeType.Deleted => RowKind.Removed,
            ChangeType.Modified => RowKind.Modified,
            ChangeType.Imaginary => RowKind.Filler,
            _ => RowKind.Unchanged
        };
}
