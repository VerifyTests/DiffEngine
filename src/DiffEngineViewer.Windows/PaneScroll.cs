/// <summary>
/// A scrollbar's range for a pane.
/// </summary>
readonly record struct ScrollRange(int Maximum, int LargeChange, int Value);

/// <summary>
/// Maps a pane onto a <c>VScrollBar</c>. Its own type because of the off-by-one: a scrollbar's
/// thumb stops at <c>Maximum - LargeChange + 1</c> rather than at <c>Maximum</c>, and that has to
/// come out equal to the session's own clamp of <c>TotalRows - BodyRows</c>. If the two disagree by
/// one, every drag to the bottom snaps back a row.
/// </summary>
static class PaneScroll
{
    public static ScrollRange For(int totalRows, int visibleRows, int scrollTop)
    {
        var visible = Math.Max(1, visibleRows);
        var total = Math.Max(0, totalRows);
        var maximum = Math.Max(0, total - 1);
        var large = Math.Max(1, Math.Min(visible, maximum + 1));
        var highest = Math.Max(0, maximum - large + 1);
        return new(maximum, large, Math.Clamp(scrollTop, 0, highest));
    }
}
