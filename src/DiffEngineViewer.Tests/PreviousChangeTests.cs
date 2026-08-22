/// <summary>
/// Stepping back through the changed blocks of the entry on screen. Forty rows with changes at 3,
/// 17 and 33 - so rows 2, 16 and 32 - and sixteen body rows to show them in.
/// </summary>
public class PreviousChangeTests
{
    /// <summary>
    /// A block ending on the row immediately above the viewport is one the reader has not been
    /// taken to, and it used to be stepped over as though it were the block they were already in.
    /// </summary>
    [Test]
    public async Task Lands_on_a_block_ending_just_above_the_viewport()
    {
        var state = At(17);

        var moved = ViewerSession.Apply(state, CommandKind.PreviousChange);

        await Assert.That(moved.ScrollTop).IsEqualTo(16);
    }

    /// <summary>
    /// The same thing with nothing above it to fall through to, where the result was no movement
    /// at all rather than the wrong movement.
    /// </summary>
    [Test]
    public async Task Reaches_the_first_block_from_the_row_below_it()
    {
        var state = At(3);

        var moved = ViewerSession.Apply(state, CommandKind.PreviousChange);

        await Assert.That(moved.ScrollTop).IsEqualTo(2);
    }

    /// <summary>
    /// A viewport that really is inside a block still steps off it, or previous would never leave
    /// the block it is in.
    /// </summary>
    [Test]
    public async Task Steps_off_the_block_the_viewport_is_in()
    {
        var state = At(16);

        var moved = ViewerSession.Apply(state, CommandKind.PreviousChange);

        await Assert.That(moved.ScrollTop).IsEqualTo(2);
    }

    [Test]
    public async Task Stays_at_the_first_block()
    {
        var state = At(2);

        var moved = ViewerSession.Apply(state, CommandKind.PreviousChange);

        await Assert.That(moved.ScrollTop).IsEqualTo(2);
    }

    static SessionState At(int scrollTop) =>
        Fixtures.File(Fixtures.Long(true), Fixtures.Long(false)) with
        {
            ScrollTop = scrollTop
        };
}
