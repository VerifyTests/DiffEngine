namespace DiffEngine;

/// <summary>
/// A line diff of an expected and a received text. Lines compare exactly, whitespace and case
/// included, and split on <c>\r\n</c>, <c>\r</c> or <c>\n</c>.
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
