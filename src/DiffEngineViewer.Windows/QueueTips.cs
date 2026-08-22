/// <summary>
/// The queue column's tooltip, and the one thing that is easy to get wrong about it: leaving the
/// last row's text behind when the cursor reaches a row that has none.
/// <para>
/// Queue labels are clipped at the column edge with no ellipsis, and are anyway the shortest form
/// that tells one entry from another, so a tip is the only way to read the rest. Which rows have
/// one is <see cref="QueueItem.Tooltip"/>'s business, decided once for all three heads.
/// </para>
/// <para>
/// Its own type for the same reason <see cref="PaneScroll"/> is: what matters here is a state
/// transition, and a transition can be tested where a window cannot.
/// </para>
/// </summary>
sealed class QueueTips : IDisposable
{
    readonly ToolTip tip = new()
    {
        InitialDelay = 500,
        ReshowDelay = 200,
        AutoPopDelay = 10000
    };

    /// <summary>
    /// The row the tip currently describes. Tracked because SetToolTip is a window message and
    /// OnMouseMove runs for every pixel crossed, the same reason Cursor is only assigned on a
    /// change.
    /// </summary>
    int row = -1;

    /// <summary>
    /// <paramref name="text"/> is null when there is no row under the cursor, or the row is a
    /// header, or its label already says everything.
    /// <para>
    /// The text is cleared then, not merely hidden. Hide dismisses the popup that is up, but the
    /// control keeps the caption, so the next rest anywhere on the canvas brings the previous
    /// row's tip straight back — over a header that reads as the header describing the entry
    /// underneath it.
    /// </para>
    /// </summary>
    public void Apply(Control owner, int queueRow, string? text)
    {
        if (queueRow == row)
        {
            return;
        }

        row = queueRow;
        if (text is null)
        {
            tip.Hide(owner);
            tip.SetToolTip(owner, null);
            return;
        }

        tip.SetToolTip(owner, text);
    }

    /// <summary>
    /// Forgets which row is described, so the next move re-applies. For a new screen, which
    /// renumbers the rows under a cursor that has not moved, and for the cursor leaving.
    /// <para>
    /// The caption goes with it. Resetting only the row left the last row's text registered on the
    /// whole canvas, and <see cref="Apply" />'s own early return then kept it there: a cursor on
    /// the canvas but not on a queue row arrives as row -1, which is the row this just set. So the
    /// tip popped up over the diff panes, which is the thing this type exists to prevent.
    /// </para>
    /// </summary>
    public void Forget(Control owner)
    {
        row = -1;
        tip.Hide(owner);
        tip.SetToolTip(owner, null);
    }

    /// <summary>
    /// What would be shown, which is what the tests assert on: the bug this type exists to prevent
    /// is invisible from anywhere else, since a tooltip is a separate window that no capture sees.
    /// </summary>
    public string Current(Control owner) =>
        // Empty for a control with no tip registered, which GetToolTip reports as null.
        tip.GetToolTip(owner) ?? "";

    public void Dispose() =>
        tip.Dispose();
}
