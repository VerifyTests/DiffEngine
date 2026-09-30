/// <summary>
/// The side by side pairing over <see cref="TextDiff.Compute"/>: left is received, right is
/// expected, and a changed block pairs its lines as modified before padding the longer side's
/// rest against filler.
/// </summary>
public class DiffRowsTests
{
    [Test]
    public Task OneChangedLine() =>
        Verify(Render("a\nX\nc", "a\nb\nc"));

    [Test]
    public Task MoreRemovedThanAdded() =>
        Verify(Render("a\nX\nd", "a\nb\nc\nd"));

    [Test]
    public Task MoreAddedThanRemoved() =>
        Verify(Render("a\nX\nY\nd", "a\nb\nd"));

    [Test]
    public Task PureInsertAndDelete() =>
        Verify(Render("a\nnew\nb\nd", "a\nb\nold\nd"));

    [Test]
    public Task LeftEmpty() =>
        Verify(Render("", "a\nb"));

    [Test]
    public Task RightEmpty() =>
        Verify(Render("a\nb", ""));

    [Test]
    public Task WhitespaceOnly() =>
        Verify(Render("a\n  b\nc ", "a\nb\nc"));

    static string Render(string left, string right)
    {
        var (leftRows, rightRows) = DiffRows.Build(left, right);
        var builder = new StringBuilder();
        for (var index = 0; index < leftRows.Count; index++)
        {
            builder.Append($"{Cell(leftRows[index]),-20} | {Cell(rightRows[index])}".TrimEnd());
            builder.Append('\n');
        }

        return builder.ToString();
    }

    static string Cell(Row row) =>
        $"{row.LineNumber?.ToString() ?? "."} {row.Kind} '{row.Text}'";
}
