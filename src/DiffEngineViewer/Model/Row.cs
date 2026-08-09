/// <summary>
/// One rendered line in a diff pane. <paramref name="LineNumber"/> is null for
/// <see cref="RowKind.Filler"/> rows.
/// </summary>
record Row(int? LineNumber, RowKind Kind, string Text);
