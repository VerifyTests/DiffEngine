/// <summary>
/// One side of the diff. <paramref name="Rows"/> holds only the visible slice;
/// <paramref name="ScrollTop"/> and <paramref name="TotalRows"/> describe where that slice sits
/// so a scrollbar can be drawn.
/// </summary>
record Pane(string Header, IReadOnlyList<Row> Rows, int ScrollTop, int TotalRows);
