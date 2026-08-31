/// <summary>
/// One rendered line in a diff pane. <paramref name="LineNumber"/> is null for
/// <see cref="RowKind.Filler"/> rows.
/// </summary>
record Row(int? LineNumber, RowKind Kind, string Text)
{
    /// <summary>
    /// What of this row the reader has selected. Empty on a row of a
    /// <see cref="QueueEntry"/>, which is the document rather than the frame, and filled in by
    /// <see cref="ScreenBuilder"/> for the visible slice.
    /// <para>
    /// Honoured by the three pixel heads and ignored by <see cref="AsciiRenderer"/>, which draws a
    /// character grid and has no way to invert part of one without changing its width. The same
    /// bargain <see cref="ImagePane"/> makes: what the model universally states about a selection
    /// goes in the status line, where every renderer draws it and the text snapshots show it, and
    /// the highlight is the enrichment on top. So a head can be missing the highlight and still
    /// show a screen that is smaller rather than wrong.
    /// </para>
    /// </summary>
    public SelectionSpan Selection { get; init; }
}
