public class AsciiRendererTests
{
    /// <summary>
    /// The grid only reads correctly if every line is exactly as wide as the border, and a cell
    /// that forgets to pad silently collapses the columns to its right.
    /// </summary>
    [Test]
    [Arguments(40, 10)]
    [Arguments(96, 24)]
    [Arguments(97, 25)]
    [Arguments(200, 60)]
    public async Task EveryLineIsTheSameWidth(int columns, int rows)
    {
        foreach (var state in States(columns, rows))
        {
            var lines = AsciiRenderer.Render(state).Split('\n');
            foreach (var line in lines)
            {
                await Assert.That(line.Length).IsEqualTo(columns);
            }
        }
    }

    [Test]
    [Arguments(40, 10)]
    [Arguments(96, 24)]
    [Arguments(200, 60)]
    public async Task LineCountMatchesTheRequestedRows(int columns, int rows)
    {
        foreach (var state in States(columns, rows))
        {
            var lines = AsciiRenderer.Render(state).Split('\n');
            await Assert.That(lines.Length).IsEqualTo(rows);
        }
    }

    /// <summary>
    /// A window narrower than the minimum still has to produce a legal grid rather than throw.
    /// </summary>
    [Test]
    public async Task TinyWindowStillRenders()
    {
        var state = ViewerSession.Resize(Fixtures.File(), 1, 1);

        var lines = AsciiRenderer.Render(ScreenBuilder.Build(state)).Split('\n');

        await Assert.That(lines[0].Length).IsEqualTo(40);
        foreach (var line in lines)
        {
            await Assert.That(line.Length).IsEqualTo(40);
        }
    }

    [Test]
    public async Task TabsAndNewlinesDoNotBreakTheGrid()
    {
        var state = Fixtures.File("a\tb", "a\tc");

        var lines = AsciiRenderer.Render(ScreenBuilder.Build(state)).Split('\n');

        await Assert.That(lines.Length).IsEqualTo(Fixtures.Rows);
        foreach (var line in lines)
        {
            await Assert.That(line.Length).IsEqualTo(Fixtures.Columns);
        }
    }

    static IEnumerable<Screen> States(int columns, int rows)
    {
        yield return Build(Fixtures.File(), columns, rows);
        yield return Build(Fixtures.File(Fixtures.Long(true), Fixtures.Long(false)), columns, rows);
        yield return Build(Fixtures.File(left: ""), columns, rows);
        yield return Build(Fixtures.Inline(), columns, rows);
        yield return Build(Fixtures.Inline(Fixtures.Patch()), columns, rows);
        yield return Build(
            Fixtures.Inline(
                Fixtures.Patch(),
                Fixtures.Patch("OtherTests.cs", 12, null, "brand new"),
                Fixtures.Patch("AVeryLongTestFileNameIndeed.cs", 4001, "\"x\"", "y")),
            columns,
            rows);
    }

    static Screen Build(SessionState state, int columns, int rows) =>
        ScreenBuilder.Build(ViewerSession.Resize(state, columns, rows));
}
