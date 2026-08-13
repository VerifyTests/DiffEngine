/// <summary>
/// One side of the diff. <paramref name="Rows"/> holds only the visible slice;
/// <paramref name="ScrollTop"/> and <paramref name="TotalRows"/> describe where that slice sits
/// so a scrollbar can be drawn.
/// </summary>
/// <param name="Image">
/// The picture this side is, for a head that can draw one. Null for a text side, and for an image
/// side whose bytes could not be read or recognized.
/// </param>
record Pane(string Header, IReadOnlyList<Row> Rows, int ScrollTop, int TotalRows, ImagePane? Image = null);
