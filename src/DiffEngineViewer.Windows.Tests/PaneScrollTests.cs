/// <summary>
/// The scrollbar range, which has to land on the same last row the session's own clamp does.
/// Pure, so no window and no STA.
/// </summary>
public class PaneScrollTests
{
    /// <summary>
    /// The highest value a thumb can reach is <c>Maximum - LargeChange + 1</c>, and the session
    /// clamps to <c>TotalRows - BodyRows</c>. This is the test that those are the same number.
    /// </summary>
    [Test]
    [Arguments(100, 29, 71)]
    [Arguments(40, 16, 24)]
    [Arguments(29, 29, 0)]
    [Arguments(10, 29, 0)]
    [Arguments(0, 29, 0)]
    public async Task TheThumbReachesTheSessionsLastPage(int total, int visible, int expected)
    {
        var range = PaneScroll.For(total, visible, int.MaxValue);

        await Assert.That(range.Maximum - range.LargeChange + 1).IsEqualTo(expected);
        await Assert.That(range.Value).IsEqualTo(expected);
        await Assert.That(Math.Max(0, total - visible)).IsEqualTo(expected);
    }

    [Test]
    public async Task ValueIsClamped()
    {
        await Assert.That(PaneScroll.For(100, 29, -5).Value).IsEqualTo(0);
        await Assert.That(PaneScroll.For(100, 29, 12).Value).IsEqualTo(12);
    }

    /// <summary>
    /// WinForms refuses a LargeChange below one, and a pane can legitimately be empty.
    /// </summary>
    [Test]
    public async Task AnEmptyPaneStaysValid()
    {
        var range = PaneScroll.For(0, 29, 0);

        await Assert.That(range.Maximum).IsEqualTo(0);
        await Assert.That(range.LargeChange).IsEqualTo(1);
    }

    /// <summary>
    /// Content that fits fills the track, which is what makes the thumb immovable rather than the
    /// bar being hidden and the layout jumping.
    /// </summary>
    [Test]
    public async Task ContentThatFitsFillsTheTrack()
    {
        var range = PaneScroll.For(10, 29, 0);

        await Assert.That(range.LargeChange).IsGreaterThanOrEqualTo(range.Maximum + 1);
    }
}
