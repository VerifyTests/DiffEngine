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

    [Test]
    public async Task NeverSplitsACharacterFromItsMarks()
    {
        // e and its two marks are cell 0, x cell 1
        const string text = "é̂x";

        await Assert.That(CellGrid.Index(text, 1)).IsEqualTo(3);
        await Assert.That(CellGrid.Index(text, 2)).IsEqualTo(4);
    }
}
