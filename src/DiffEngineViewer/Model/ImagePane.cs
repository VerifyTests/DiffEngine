/// <summary>
/// A picture for a head to draw under the pane's rows, and the model's whole statement about it: a
/// path on this machine and the size the file says it is.
/// <para>
/// The rows are still the universal description — format, dimensions and byte count, coloured
/// against the other side — and every head draws those. This is an enrichment on top for a head
/// that has a decoder, so a head without one shows a comparison that is smaller rather than one
/// that is wrong. The heads therefore still consume one structure; they differ in how much of it
/// they can honour.
/// </para>
/// <para>
/// Null unless the bytes were read and recognized, so a head never has to decide whether a path is
/// worth trying.
/// </para>
/// </summary>
record ImagePane(string Path, int Width, int Height);
