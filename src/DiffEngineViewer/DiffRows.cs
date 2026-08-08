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
        var model = SideBySideDiffBuilder.Diff(rightText, leftText);
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
