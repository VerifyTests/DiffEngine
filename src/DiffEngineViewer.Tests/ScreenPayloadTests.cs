/// <summary>
/// What the macOS and Linux heads are handed: a screen as flat buffers. Nothing on Windows reads
/// them, so these read them the way a shim does, by offset and length.
/// </summary>
public class ScreenPayloadTests
{
    /// <summary>
    /// A row is written once, as UTF-8, and its segments are byte ranges of that. Each range has
    /// to be the bytes of the characters the segment is, whatever came before it in the row: two
    /// cell characters, characters outside the BMP, marks that attach to the one before, and a
    /// surrogate with no partner, which is written as the three bytes of a replacement character.
    /// </summary>
    [Test]
    public async Task ASegmentIsTheBytesOfItsOwnCharacters()
    {
        string[] lines =
        [
            "plain ascii",
            "漢字 then ascii then 漢字",
            "tab\tseparated\t漢",
            "thumb \U0001F44D\U0001F3FD family \U0001F468‍\U0001F469‍\U0001F467 done",
            "é and ─│┼ and → ←",
            "lone \uD800 surrogate 漢",
            "漢"
        ];
        var screen = ScreenBuilder.Build(Fixtures.File(string.Join("\n", lines), "other"));
        var payload = new ScreenPayload();

        payload.Build(screen);

        var checkedSegments = 0;
        for (var index = 0; index < screen.Left.Rows.Count; index++)
        {
            var text = RowText.Clip(RowText.Flatten(screen.Left.Rows[index].Text), screen.Columns);
            var row = payload.Rows[index];
            var segments = CellGrid.Segments(text);
            await Assert.That(row.SegmentCount).IsEqualTo(segments.Count);
            for (var position = 0; position < segments.Count; position++)
            {
                var segment = segments[position];
                var encoded = payload.Segments[row.SegmentOffset + position];
                var bytes = Convert.ToHexString(payload.Strings.Slice(encoded.TextOffset, encoded.TextLength));
                var expected = Convert.ToHexString(Encoding.UTF8.GetBytes(text.Substring(segment.Start, segment.Length)));
                await Assert.That(bytes).IsEqualTo(expected);
                await Assert.That(encoded.Column).IsEqualTo(segment.Column);
                // Inside the row's own bytes, which is the only text a shim may read for it
                await Assert.That(encoded.TextOffset).IsGreaterThanOrEqualTo(row.TextOffset);
                await Assert.That(encoded.TextOffset + encoded.TextLength).IsLessThanOrEqualTo(row.TextOffset + row.TextLength);
                checkedSegments++;
            }
        }

        // Most of these rows are several segments each; a screen that came out as one a row would
        // have checked nothing.
        await Assert.That(checkedSegments).IsGreaterThan(15);
    }

    /// <summary>
    /// The loop hands over the screen it handed over last frame for as long as nothing happens,
    /// and encoding that one again was most of what an idle frame cost these heads. Seen by what
    /// it allocates: an encode cuts every row that is not plain text into a list of segments, and
    /// a screen already held is not looked at.
    /// </summary>
    [Test]
    public async Task TheScreenAlreadyHeldIsNotEncodedAgain()
    {
        var rows = string.Join("\n", Enumerable.Range(0, 40).Select(_ => $"漢字 {_} 漢字"));
        var state = Fixtures.File(rows, "other");
        var screen = ScreenBuilder.Build(state);
        var another = ScreenBuilder.Build(state);
        var payload = new ScreenPayload();
        payload.Build(screen);

        var before = GC.GetAllocatedBytesForCurrentThread();
        payload.Build(screen);
        var held = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        payload.Build(another);
        var encoded = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(held).IsEqualTo(0);
        await Assert.That(encoded).IsGreaterThan(0);
    }

    /// <summary>
    /// And a screen it has not been handed before is encoded, even one that says what the last
    /// did: the buffers after it describe that screen.
    /// </summary>
    [Test]
    public async Task AnotherScreenReplacesWhatWasHeld()
    {
        var payload = new ScreenPayload();
        payload.Build(ScreenBuilder.Build(Fixtures.File("one\ntwo\nthree", "other")));
        await Assert.That(payload.Rows.Count).IsGreaterThanOrEqualTo(6);

        payload.Build(ScreenBuilder.Build(Fixtures.File("one", "one")));

        // One row a side
        await Assert.That(payload.Rows.Count).IsEqualTo(2);
    }
}
