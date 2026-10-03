/// <summary>
/// One side of the diff. <paramref name="Rows"/> holds only the visible slice;
/// <paramref name="ScrollTop"/> and <paramref name="TotalRows"/> describe where that slice sits
/// so a scrollbar can be drawn.
/// </summary>
/// <param name="Image">
/// The picture this side is, for a head that can draw one. Null for a text side, and for an image
/// side whose bytes could not be read or recognized.
/// </param>
/// <param name="ImagePending">
/// A picture for this side is still being drawn: a document's page that has not landed yet. A head
/// shows something moving where it will go, under the rows, until <paramref name="Image"/> arrives.
/// The status line says the same thing in words, which is what <see cref="AsciiRenderer"/> draws.
/// </param>
record Pane(string Header, IReadOnlyList<Row> Rows, int ScrollTop, int TotalRows, ImagePane? Image = null, bool ImagePending = false);
