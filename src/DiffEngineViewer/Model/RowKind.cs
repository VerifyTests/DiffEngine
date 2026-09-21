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
    Filler,

    /// <summary>
    /// A run of unchanged lines the minimal view left out, drawn as the one row standing for all
    /// of them. Its text says how many rather than being any of them, and it has no line number.
    /// Never produced by a diff: only <see cref="DiffView"/> makes one.
    /// </summary>
    Folded
}
