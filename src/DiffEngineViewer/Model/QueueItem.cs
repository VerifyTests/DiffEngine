enum QueueRowKind
{
    Entry,
    Header
}

/// <summary>
/// One row of the queue column. Headers are labels only: <paramref name="EntryIndex"/> maps an
/// entry row back to its <see cref="SessionState.Queue"/> index for click handling, and is -1 for
/// a header, whose clicks go nowhere.
/// </summary>
record QueueItem(
    string Label,
    bool Selected,
    string? Status,
    QueueRowKind Kind = QueueRowKind.Entry,
    int EntryIndex = -1);
