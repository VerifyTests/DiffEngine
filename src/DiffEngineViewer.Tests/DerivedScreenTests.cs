/// <summary>
/// What a document with files derived from it looks like, as every head draws it: the rows, the
/// footer and the menu are all text the heads are handed, so these are the coverage for all three.
/// </summary>
public class DerivedScreenTests
{
    /// <summary>
    /// One row for the document, counted, and the entry of some other test after it. The files
    /// beneath it are in the title's count and nowhere else, and the footer says how many the
    /// accept takes.
    /// </summary>
    [Test]
    public Task Folded() =>
        Verify(Fixtures.Render(Fixtures.DocumentWithDerived()));

    /// <summary>
    /// Asked for, each is a row under the document, named by what it adds to the document's name.
    /// </summary>
    [Test]
    public Task Unfolded() =>
        Verify(Fixtures.Render(ViewerSession.Apply(Fixtures.DocumentWithDerived(), CommandKind.ToggleDerived)));

    /// <summary>
    /// One of them being read, which is an ordinary pair with nothing beneath it: its accept is
    /// of that file alone.
    /// </summary>
    [Test]
    public Task ReadingOneOfThem()
    {
        var state = Fixtures.DocumentWithDerived();
        var page = state.Queue.Single(_ => _.Name == "Sample.Test#page_0001 (txt)");
        return Verify(Fixtures.Render(ViewerSession.SelectKey(state, page.Key)));
    }

    [Test]
    public Task MenuOnTheDocument() =>
        Verify(Fixtures.Render(ViewerSession.OpenMenu(Fixtures.DocumentWithDerived(), 0)));

    /// <summary>
    /// What each row says beyond its label. The document's says its count is of files that go
    /// with it, since a number in brackets after a name does not.
    /// </summary>
    [Test]
    public Task Tooltips()
    {
        var builder = new StringBuilder();
        foreach (var row in QueueProjection.Rows(ViewerSession.Apply(Fixtures.DocumentWithDerived(), CommandKind.ToggleDerived)))
        {
            builder.AppendLine($"[{row.Label}]");
            builder.AppendLine(
                row.Tooltip is null
                    ? "  (no tip)"
                    : string.Join("\n", row.Tooltip.Split('\n').Select(_ => $"  {_}")));
        }

        return Verify(builder.ToString());
    }
}
