enum QueueRowKind
{
    Entry,
    Header
}

/// <summary>
/// One row of the queue column. Headers are labels only: <paramref name="EntryIndex"/> maps an
/// entry row back to its <see cref="SessionState.Queue"/> index for click handling, and is -1 for
/// a header, whose left-clicks go nowhere.
/// </summary>
record QueueItem(
    string Label,
    bool Selected,
    string? Status,
    QueueRowKind Kind = QueueRowKind.Entry,
    int EntryIndex = -1)
{
    /// <summary>
    /// For a header: the name without its count, and the queue indexes it spans, which is what a
    /// right-click's group commands act on. Null on entry rows.
    /// </summary>
    public string? GroupName { get; init; }

    public IReadOnlyList<int>? GroupMembers { get; init; }

    /// <summary>
    /// What <see cref="SessionState.Collapsed"/> holds for this header, null on entry rows.
    /// <para>
    /// Not <see cref="GroupName"/>: two tests sharing a name in different files are two groups, and
    /// folding one by name would fold both. This carries the identity the grouping was built on.
    /// </para>
    /// </summary>
    public string? GroupKey { get; init; }

    /// <summary>
    /// What the row cannot say for itself, or null when there is nothing.
    /// <para>
    /// <see cref="Label"/> is deliberately the shortest form that tells one entry from another, so
    /// the full path, the test behind a call site and the failure text are all missing from it.
    /// Those are what this carries. Null rather than a copy of the label, because a tip that
    /// repeats the row it is over has told the reader nothing and cost them a popup.
    /// </para>
    /// </summary>
    public string? Tooltip { get; init; }

    // By value, because the members list is rebuilt every frame and reference equality would
    // defeat the WinForms head's idle repaint check the moment a header is on screen.
    public virtual bool Equals(QueueItem? other) =>
        other is not null &&
        Label == other.Label &&
        Selected == other.Selected &&
        Status == other.Status &&
        Kind == other.Kind &&
        EntryIndex == other.EntryIndex &&
        GroupName == other.GroupName &&
        GroupKey == other.GroupKey &&
        Tooltip == other.Tooltip &&
        (GroupMembers is null
            ? other.GroupMembers is null
            : other.GroupMembers is not null && GroupMembers.SequenceEqual(other.GroupMembers));

    public override int GetHashCode() =>
        HashCode.Combine(Label, Selected, Status, Kind, EntryIndex, GroupName, Tooltip);
}
