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
/// <param name="Hash">
/// What the file held when the model was built, so a head can tell one picture from another at the
/// same path and size. Not for drawing - the head reads the file - but for deciding whether what
/// is on screen is still this. A re-run that rewrites a received image at the same dimensions
/// changes nothing else in the model, and for BMP that is every re-run.
/// </param>
/// <param name="Zoom">
/// How many times the size that fits the pane to draw it at: 1 for fitted, which never enlarges a
/// picture past its own size, and more for a reader who has zoomed in, which may. What does not
/// fit the pane then is cut off at its edges rather than drawn over its neighbours.
/// </param>
/// <param name="CenterX">
/// The point of the picture to put at the middle of what is shown, as a fraction of its width.
/// Only meaningful once it is enlarged past the pane, and a head keeps it far enough from the
/// edges that the pane stays full: the model does not know how many pixels a pane has.
/// </param>
/// <param name="CenterY">As <paramref name="CenterX"/>, of its height.</param>
record ImagePane(string Path, int Width, int Height, string? Hash, double Zoom = 1, double CenterX = 0.5, double CenterY = 0.5);
