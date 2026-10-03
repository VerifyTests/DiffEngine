namespace DiffEngine;

/// <summary>
/// A line diff of an expected and a received text. Lines compare exactly, whitespace and case
/// included, and split on <c>\r\n</c>, <c>\r</c> or <c>\n</c>.
/// <para>
/// As few lines as possible are reported as changed, with one exception: two texts of more than
/// 10,000 lines between them that share thousands of lines in a different order, 8,000 or more
/// of them out of place. Finding the fewest there takes from a fraction of a second to minutes,
/// so past about a sixth of a second of looking the diff settles for one that is correct, each
/// side being exactly its lines in order, and may report a line as changed that it could have
/// matched.
/// </para>
/// </summary>
public static class TextDiff
{
    /// <summary>
    /// Every line of both texts in inline order: unchanged lines, and each changed block as its
    /// <see cref="DiffLineKind.Removed"/> lines followed by its <see cref="DiffLineKind.Added"/>
    /// lines.
    /// </summary>
    public static IReadOnlyList<DiffLine> Compute(string expected, string received)
    {
        var diff = LineDiff.Build(expected, received);
        var lines = new List<DiffLine>(diff.Entries.Count);
        foreach (var entry in diff.Entries)
        {
            lines.Add(
                new(
                    entry.Kind,
                    diff.Text(entry).ToString(),
                    LineNumber(entry.Expected),
                    LineNumber(entry.Received)));
        }

        return lines;
    }

    /// <summary>
    /// The diff as text, for a failure message. Lines end with <c>\n</c>, and the result has no
    /// trailing line break.
    /// </summary>
    public static string Format(string expected, string received, TextDiffFormat format = TextDiffFormat.Compact) =>
        TextDiffFormatter.Format(LineDiff.Build(expected, received), format);

    static int? LineNumber(int index)
    {
        if (index < 0)
        {
            return null;
        }

        return index + 1;
    }
}
