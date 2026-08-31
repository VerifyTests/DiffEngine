/// <summary>
/// Which pane something is in. The two sides are drawn from one <see cref="QueueEntry"/>, so a
/// selection has to say which of them it is against.
/// </summary>
enum PaneSide
{
    Left,
    Right
}

/// <summary>
/// A range of pane text the reader has selected, held as the two ends of the drag rather than as
/// an ordered pair, so extending a selection backwards past its own start keeps working.
/// <para>
/// Rows are indexes into the whole side, not into the visible slice: a drag that continues while
/// the wheel scrolls has to mean the same thing before and after. Columns are characters of the
/// row's flattened text, which is what is on screen and therefore what was pointed at.
/// </para>
/// <para>
/// <paramref name="Key"/> and <paramref name="Variant"/> are the entry this describes. Anything
/// that changes what is being read - selecting another entry, cycling a variant, a queue that
/// rebuilt underneath - leaves a selection that no longer matches, and a selection that does not
/// match the current entry is not shown or copied. That is one rule in one place rather than a
/// clear-the-selection call on every transition, one of which would eventually be missed.
/// </para>
/// </summary>
record TextSelection(
    string Key,
    int Variant,
    PaneSide Side,
    int AnchorRow,
    int AnchorColumn,
    int FocusRow,
    int FocusColumn)
{
    /// <summary>
    /// A press with no drag behind it. Nothing is highlighted and nothing would be copied, so a
    /// click in a pane reads as clearing the selection.
    /// </summary>
    public bool IsEmpty =>
        AnchorRow == FocusRow &&
        AnchorColumn == FocusColumn;

    public (int Row, int Column) Start =>
        Backwards ? (FocusRow, FocusColumn) : (AnchorRow, AnchorColumn);

    public (int Row, int Column) End =>
        Backwards ? (AnchorRow, AnchorColumn) : (FocusRow, FocusColumn);

    bool Backwards =>
        FocusRow < AnchorRow ||
        (FocusRow == AnchorRow && FocusColumn < AnchorColumn);

    /// <summary>
    /// Whether this describes what is currently on screen. False for a selection left behind by
    /// an entry that has been accepted, discarded or cycled away from.
    /// </summary>
    public bool Describes(QueueEntry? entry) =>
        entry is not null &&
        entry.Key == Key &&
        entry.SelectedVariant == Variant;
}
