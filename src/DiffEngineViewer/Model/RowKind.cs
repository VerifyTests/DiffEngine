/// <summary>
/// How a <see cref="Row"/> relates to the other pane.
/// </summary>
enum RowKind
{
    Unchanged,
    Added,
    Removed,
    Modified,

    /// <summary>
    /// No line exists on this side. Rendered blank to keep the two panes vertically aligned.
    /// </summary>
    Filler
}
