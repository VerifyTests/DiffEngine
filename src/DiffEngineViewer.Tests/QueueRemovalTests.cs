/// <summary>
/// What a removal does to the entry being read. Removals arrive for entries other than that one -
/// a settle from a test that has started passing, a sweep from a group header, a bulk accept that
/// skipped this entry - and the reader is in the middle of a comparison while they do.
/// </summary>
public class QueueRemovalTests
{
    [Test]
    public async Task Settling_another_entry_leaves_the_one_being_read_where_it_was()
    {
        var state = Scrolled(out var reading);

        var settled = ViewerSession.Settle(state, state.Queue[0].Key);

        await Assert.That(settled.Current!.Key).IsEqualTo(reading);
        await Assert.That(settled.ScrollTop).IsEqualTo(state.ScrollTop);
    }

    /// <summary>
    /// The entry on screen going is the one case where the reader has to be moved, and the top of
    /// the next entry is where they go.
    /// </summary>
    [Test]
    public async Task Settling_the_entry_being_read_moves_on_to_the_next()
    {
        var state = Scrolled(out var reading);

        var settled = ViewerSession.Settle(state, reading);

        await Assert.That(settled.Current!.Key).IsNotEqualTo(reading);
        await Assert.That(settled.ScrollTop).IsEqualTo(0);
    }

    /// <summary>
    /// Three entries, the middle one selected and scrolled into - so that a removal above it moves
    /// every index below, which is the thing being asserted about.
    /// </summary>
    static SessionState Scrolled(out string reading)
    {
        var state = Fixtures.Inline(
            Fixtures.Patch("A.cs", 1, null, Fixtures.Long(true)),
            Fixtures.Patch("B.cs", 2, null, Fixtures.Long(true)),
            Fixtures.Patch("C.cs", 3, null, Fixtures.Long(true)));
        reading = state.Queue[1].Key;
        state = ViewerSession.SelectKey(state, reading);
        state = ViewerSession.Apply(state, CommandKind.PageDown);
        if (state.ScrollTop == 0)
        {
            throw new("The entry did not scroll, so nothing below asserts anything.");
        }

        return state;
    }
}
