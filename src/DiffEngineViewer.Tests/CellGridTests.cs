/// <summary>
/// The character grid every head draws a row on and every selection counts in. What a character
/// takes is decided here and not by any head's fonts, which is what keeps the highlight, the hit
/// test and the copy on one run of text.
/// </summary>
public class CellGridTests
{
    [Test]
    [Arguments("", 0)]
    [Arguments("plain text", 10)]
    // Latin with its extensions, Greek and Cyrillic: one cell each, like ASCII
    [Arguments("Ωμέγα Привет żółw", 17)]
    // Wide: two cells each
    [Arguments("中文", 4)]
    [Arguments("ｆｕｌｌ", 8)]
    [Arguments("한국어", 6)]
    // A combining mark takes no cell of its own
    [Arguments("é", 1)]
    [Arguments("é̂x", 2)]
    // Outside the basic plane: a surrogate pair is one character
    [Arguments("\U0001D400", 1)]
    // Emoji are wide, and a joined sequence is one picture
    [Arguments("\U0001F600", 2)]
    [Arguments("\U0001F468‍\U0001F469‍\U0001F467", 2)]
    // A mark with nothing before it still takes a cell, or it could not be selected
    [Arguments("́", 1)]
    public async Task Cells(string text, int cells) =>
        await Assert.That(CellGrid.Cells(text)).IsEqualTo(cells);

    [Test]
    public async Task PlainTextIsOneSegmentAtColumnZero() =>
        await Assert.That(CellGrid.Segments("the quick brown fox")).IsEquivalentTo([new CellGrid.Segment(0, 19, 0)]);

    [Test]
    public async Task EmptyTextHasNoSegments() =>
        await Assert.That(CellGrid.Segments("")).IsEmpty();

    /// <summary>
    /// Text the monospace font has is drawn in runs; anything else on its own at its column, so the
    /// characters after it land where the grid says whatever width it is drawn at.
    /// </summary>
    [Test]
    public async Task EveryOtherClusterIsASegmentOfItsOwn() =>
        await Assert.That(CellGrid.Segments("ab中ćd"))
            .IsEquivalentTo<IReadOnlyList<CellGrid.Segment>, CellGrid.Segment>(
            [
                new(0, 2, 0),
                new(2, 1, 2),
                new(3, 2, 4),
                new(5, 1, 5)
            ]);

    [Test]
    public async Task CyrillicIsARunLikeAscii() =>
        await Assert.That(CellGrid.Segments("Привет, мир")).IsEquivalentTo([new CellGrid.Segment(0, 11, 0)]);

    /// <summary>
    /// Whatever the embedded font has is drawn in runs, from whichever block: a table drawn in a
    /// snapshot is rows of box drawing, and was a segment a character.
    /// </summary>
    [Test]
    public async Task BoxDrawingArrowsAndPunctuationAreARunLikeAscii() =>
        await Assert.That(CellGrid.Segments("├── a → b … “c” ≤ d"))
            .IsEquivalentTo([new CellGrid.Segment(0, 19, 0)]);

    /// <summary>
    /// A character the font does not have is drawn from whatever font the machine finds for it, at
    /// that font's width, so it is never inside a run. That holds in the blocks the font mostly
    /// covers too, where it used to be taken on trust: Latin Extended-B's ƀ and Cyrillic's Ѡ.
    /// </summary>
    [Test]
    [Arguments("aƀb")]
    [Arguments("aѠb")]
    public async Task ACharacterTheFontLacksIsASegmentOfItsOwn(string text) =>
        await Assert.That(CellGrid.Segments(text))
            .IsEquivalentTo<IReadOnlyList<CellGrid.Segment>, CellGrid.Segment>(
            [
                new(0, 1, 0),
                new(1, 1, 1),
                new(2, 1, 2)
            ]);

    /// <summary>
    /// The font has a glyph for the high voltage sign, a cell wide, and the grid gives it two as
    /// it does every emoji. The grid decides, so it is drawn on its own and what follows is where
    /// the grid says.
    /// </summary>
    [Test]
    public async Task ACharacterGivenTwoCellsIsOnItsOwnWhateverTheFontHas() =>
        await Assert.That(CellGrid.Segments("a⚡b"))
            .IsEquivalentTo<IReadOnlyList<CellGrid.Segment>, CellGrid.Segment>(
            [
                new(0, 1, 0),
                new(1, 1, 1),
                new(2, 1, 3)
            ]);

    /// <summary>
    /// Half a surrogate pair is read as the replacement character, which the font has a glyph
    /// for. The text still holds half a pair, which no font has, so it stays out of a run.
    /// </summary>
    [Test]
    public async Task ASurrogateWithNoPartnerIsASegmentOfItsOwn() =>
        await Assert.That(CellGrid.Segments("a\uD800b"))
            .IsEquivalentTo<IReadOnlyList<CellGrid.Segment>, CellGrid.Segment>(
            [
                new(0, 1, 0),
                new(1, 1, 1),
                new(2, 1, 2)
            ]);

    /// <summary>
    /// A row of nothing but what the font draws a cell wide is measured as a row of ASCII is, with
    /// no walk through its clusters: as many cells as characters, and each character at its own.
    /// </summary>
    [Test]
    public async Task ARowTheFontDrawsThroughoutIsMeasuredAsPlainTextIs()
    {
        const string rule = "├──┼──┤ → é";

        await Assert.That(CellGrid.Cells(rule)).IsEqualTo(11);
        await Assert.That(CellGrid.Snap(rule, 4)).IsEqualTo(4);
        await Assert.That(CellGrid.Index(rule, 4)).IsEqualTo(4);
        await Assert.That(CellGrid.Snap(rule, 40)).IsEqualTo(11);
        await Assert.That(CellGrid.Index(rule, 40)).IsEqualTo(11);
        await Assert.That(CellGrid.Segments(rule)).IsEquivalentTo([new CellGrid.Segment(0, 11, 0)]);
    }

    /// <summary>
    /// And one wide character in such a row puts it back on the grid a cluster at a time.
    /// </summary>
    [Test]
    public async Task AWideCharacterInSuchARowIsStillCounted()
    {
        const string rule = "──中─";

        await Assert.That(CellGrid.Cells(rule)).IsEqualTo(5);
        await Assert.That(CellGrid.Index(rule, 4)).IsEqualTo(3);
        await Assert.That(CellGrid.Segments(rule))
            .IsEquivalentTo<IReadOnlyList<CellGrid.Segment>, CellGrid.Segment>(
            [
                new(0, 2, 0),
                new(2, 1, 2),
                new(3, 1, 4)
            ]);
    }

    /// <summary>
    /// What the grid asks the font: every printable ASCII character, since a row of those is drawn
    /// as one string without asking; what it has past ASCII, and past the basic plane; and not a
    /// character it lacks, nor a mark, which it has at no width at all.
    /// </summary>
    [Test]
    public async Task TheEmbeddedFontSaysWhatItDrawsACellWide()
    {
        for (var character = 0x20; character <= 0x7E; character++)
        {
            await Assert.That(FontCoverage.Has(character)).IsTrue();
        }

        await Assert.That(FontCoverage.Has(0x2500)).IsTrue();
        await Assert.That(FontCoverage.Has(0x2192)).IsTrue();
        await Assert.That(FontCoverage.Has(0x044F)).IsTrue();
        await Assert.That(FontCoverage.Has(0x1D538)).IsTrue();

        await Assert.That(FontCoverage.Has(0x4E2D)).IsFalse();
        await Assert.That(FontCoverage.Has(0x0180)).IsFalse();
        await Assert.That(FontCoverage.Has(0x0301)).IsFalse();
        await Assert.That(FontCoverage.Has(0x1F600)).IsFalse();
        await Assert.That(FontCoverage.Has(0x10FFFF)).IsFalse();
        await Assert.That(FontCoverage.Has(-1)).IsFalse();
    }

    /// <summary>
    /// Bytes that are not a font say nothing is covered, rather than throwing out of the first row
    /// to be drawn: every character is then a segment of its own, which is slower and still right.
    /// </summary>
    [Test]
    public async Task BytesThatAreNotAFontCoverNothing()
    {
        await Assert.That(FontCoverage.Read([]).Has('a')).IsFalse();
        await Assert.That(FontCoverage.Read([0, 1, 0, 0, 0, 200, 9, 9, 9, 9, 9, 9, 9, 9]).Has('a')).IsFalse();
        var truncated = EmbeddedFont.Bytes().AsSpan(0, 2000).ToArray();
        await Assert.That(FontCoverage.Read(truncated).Has('a')).IsFalse();
    }

    /// <summary>
    /// A column inside a wide character moves to its end, so a selection takes it whole or not at
    /// all, and the index it maps to is after the whole character.
    /// </summary>
    [Test]
    [Arguments(0, 0, 0)]
    [Arguments(1, 1, 1)]
    [Arguments(2, 3, 2)]
    [Arguments(3, 3, 2)]
    [Arguments(4, 4, 3)]
    [Arguments(9, 4, 3)]
    public async Task SnapsToWholeCharacters(int cell, int snapped, int index)
    {
        // a is cell 0, 中 cells 1 and 2, b cell 3
        const string text = "a中b";

        await Assert.That(CellGrid.Snap(text, cell)).IsEqualTo(snapped);
        await Assert.That(CellGrid.Index(text, cell)).IsEqualTo(index);
    }

    /// <summary>
    /// The segments of the start of a row, found in one walk, are the segments of the row cut
    /// where its first cells end: every kind of character, cut at every cell.
    /// </summary>
    [Test]
    public async Task TheSegmentsOfTheStartOfARowAreThoseOfTheRowCutThere()
    {
        string[] pieces = ["a", "bc", " ", "中", "ｆ", "é", "é̂", "\U0001D400", "\U0001F600", "\U0001F468‍\U0001F469‍\U0001F467", "─", "\uD800", "́", "→"];
        var random = new Random(31);
        for (var iteration = 0; iteration < 300; iteration++)
        {
            var builder = new StringBuilder();
            for (var count = random.Next(0, 12); count > 0; count--)
            {
                builder.Append(pieces[random.Next(pieces.Length)]);
            }

            var text = builder.ToString();
            for (var cells = -1; cells <= CellGrid.Cells(text) + 1; cells++)
            {
                var start = CellGrid.Segments(text, cells, out var end);

                await Assert.That(end).IsEqualTo(CellGrid.Index(text, cells));
                await Assert.That(start.SequenceEqual(CellGrid.Segments(text[..end]))).IsTrue();
            }
        }
    }

    [Test]
    public async Task NeverSplitsACharacterFromItsMarks()
    {
        // e and its two marks are cell 0, x cell 1
        const string text = "é̂x";

        await Assert.That(CellGrid.Index(text, 1)).IsEqualTo(3);
        await Assert.That(CellGrid.Index(text, 2)).IsEqualTo(4);
    }
}
