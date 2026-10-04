/// <summary>
/// The start of a row, which is all of it the WinForms head hands to GDI+: no more than its pane
/// has cells for, cut where the grid would cut it, and found without reading the rest of the row.
/// </summary>
public class RowTextTests
{
    /// <summary>
    /// Whatever a row holds and wherever in it the cut falls, what is kept is what flattening all
    /// of it and cutting at the grid's boundary keeps. So nothing downstream can tell the start
    /// from the whole row: a tab is its four cells, a wide character or a character and its marks
    /// is whole or absent, and a pair of surrogates is never read as its first half.
    /// </summary>
    [Test]
    public async Task TheStartOfARowIsTheWholeRowCutAtACellBoundary()
    {
        var wrong = new List<string>();
        foreach (var row in rows)
        {
            var flattened = RowText.Flatten(row);
            for (var cells = -1; cells <= CellGrid.Cells(flattened) + 2; cells++)
            {
                var expected = flattened[..CellGrid.Index(flattened, cells)];
                var shown = RowText.Shown(row, cells);
                if (shown != expected)
                {
                    wrong.Add($"{Escaped(row)} at {cells} cells: {Escaped(shown)} rather than {Escaped(expected)}");
                }
            }
        }

        await Assert.That(wrong).IsEmpty();
    }

    // Every character that cannot be seen in a source file is written as the long form of its
    // escape: marks, joiners, and the two that take no cell and sit on nothing.
    static readonly string[] rows =
    [
        "",
        "x",
        "plain text, which is nearly every row there is",
        "\tindented\twith\ttabs, each four cells",
        // Flattened away, so the characters read are more than the cells they fill
        "\r\r\r\r\r\rreturns before the text",
        "a\rb\nc",
        "ends in a return\r",
        // Wide: a cut inside one moves past it
        "中文 and 한국어 and ｆｕｌｌ",
        new('中', 40),
        // A mark belongs to the character before it, however many there are
        "e\U00000301\U00000302x and a\U00000308",
        "z" + new string('\U00000301', 100) + "algo",
        "\U00000301",
        new('\U00000301', 50),
        // No cell and no character to sit on: a soft hyphen and a zero width space
        "soft\U000000ADhyphen and zero\U0000200Bwidth",
        // Outside the basic plane, so two units to a cell
        "\U0001D400\U0001D401\U0001D402 narrow, and \U0001F600\U0001F601 wide",
        string.Concat(Enumerable.Repeat("\U0001D400", 30)),
        // A joiner takes what follows it into the same cell
        "\U0001F468\U0000200D\U0001F469\U0000200D\U0001F467 and a\U0000200Db and a trailing one\U0000200D",
        // A mark that is a surrogate pair. Read as far as its first half alone, it would be a
        // character of its own and the cut would come between it and the one it marks
        "a\U000E0100b\U000E0101c",
        string.Concat(Enumerable.Repeat("a\U000E0100", 20)),
        // Halves with no other half: one unit and one cell each
        "x\uD83D",
        "\uDC00y",
        "a\uD83Db\uDC00c",
        // Long, as the rows this exists for are, with the one character that is not ASCII far along
        new string('x', 500) + "中" + new string('y', 500)
    ];

    /// <summary>
    /// A megabyte of one line, starting with a tab. Flattening all of it to find its first sixty
    /// cells copied the megabyte, on every paint of every such row. Counted in what is allocated,
    /// which is the same number on a busy machine.
    /// </summary>
    [Test]
    public async Task TheStartOfARowIsFoundWithoutReadingTheRest()
    {
        var row = "\t" + new string('x', megabyte);
        // Once before it is measured, so nothing compiled on first use is counted
        RowText.Shown(row, 60);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var shown = RowText.Shown(row, 60);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(shown).IsEqualTo("    " + new string('x', 56));
        await Assert.That(allocated).IsLessThan(4096);
    }

    /// <summary>
    /// And one that is nothing but marks on one character, which fills a single cell however much
    /// of it is read. It is read twice over at most rather than once for every cell asked for.
    /// </summary>
    [Test]
    public async Task ARowThatNeverFillsItsCellsIsReadTwiceAtMost()
    {
        var row = "e" + new string('\U00000301', megabyte);
        RowText.Shown(row, 60);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var shown = RowText.Shown(row, 60);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(shown).IsEqualTo(row);
        // Each read is of a copy, two bytes a character
        await Assert.That(allocated).IsLessThan(2 * 2 * (megabyte + 1024));
    }

    const int megabyte = 1024 * 1024;

    static string Escaped(string text) =>
        string.Concat(text.Select(_ => _ is >= ' ' and <= '~' ? _.ToString() : $"\\u{(int) _:X4}"));
}
