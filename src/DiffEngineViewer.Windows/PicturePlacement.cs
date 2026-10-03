/// <summary>
/// Where a picture goes in the space under a pane's rows, and how much of it shows there. One
/// answer for the paint and for the pointer, so a drag moves the picture by the pixels it was
/// drawn with.
/// <para>
/// Fitted, and never enlarged past its own size: a snapshot is judged against the pixels it has,
/// and an eight pixel icon stretched across a pane is an interpolation of them rather than a look
/// at them. Past that only by the reader asking (<see cref="ImagePane.Zoom"/>), and then cut off at
/// the edges of the space rather than drawn over the pane beside it.
/// </para>
/// <para>
/// Scaled from the size the model carries rather than from the decoded bitmap, so all three heads
/// place a picture identically even where their decoders would not agree.
/// </para>
/// </summary>
/// <param name="Bounds">Where the picture, or the part of it that shows, is drawn.</param>
/// <param name="Source">
/// The part that shows, as fractions of the picture: all of it unless it is enlarged past the
/// space.
/// </param>
/// <param name="Size">The whole picture at the size it is drawn, in pixels, cut off or not.</param>
/// <param name="Centre">
/// The point at the middle of what shows, which is the one asked for moved as far as it has to
/// be for the picture to fill the space.
/// </param>
readonly record struct PicturePlacement(Rectangle Bounds, RectangleF Source, SizeF Size, PanPoint Centre)
{
    public static PicturePlacement Of(Rectangle available, ImagePane image)
    {
        var fit = Math.Min(
            Math.Min(
                available.Width / (double) image.Width,
                available.Height / (double) image.Height),
            1);
        var fitted = new Size(
            Math.Max(1, (int) (image.Width * fit)),
            Math.Max(1, (int) (image.Height * fit)));
        if (image.Zoom <= 1)
        {
            return new(Centred(available, fitted), new(0, 0, 1, 1), fitted, PanPoint.Centre);
        }

        var width = fitted.Width * image.Zoom;
        var height = fitted.Height * image.Zoom;
        var shown = new Size(
            Math.Min(available.Width, Math.Max(1, (int) width)),
            Math.Min(available.Height, Math.Max(1, (int) height)));
        var across = shown.Width / width;
        var down = shown.Height / height;
        var centre = new PanPoint(
            Math.Clamp(image.CenterX, across / 2, 1 - across / 2),
            Math.Clamp(image.CenterY, down / 2, 1 - down / 2));
        return new(
            Centred(available, shown),
            new((float) (centre.X - across / 2), (float) (centre.Y - down / 2), (float) across, (float) down),
            new((float) width, (float) height),
            centre);
    }

    /// <summary>
    /// The centre after the pointer has dragged the picture <paramref name="by"/> pixels: the
    /// picture follows the pointer, so the point at the middle moves the other way. Kept inside
    /// what the space can show, which is why it is asked here rather than worked out by the model.
    /// </summary>
    public PanPoint Dragged(Size by)
    {
        var across = Source.Width;
        var down = Source.Height;
        return new(
            Math.Clamp(Centre.X - by.Width / (double) Size.Width, across / 2.0, 1 - across / 2.0),
            Math.Clamp(Centre.Y - by.Height / (double) Size.Height, down / 2.0, 1 - down / 2.0));
    }

    static Rectangle Centred(Rectangle available, Size size) =>
        new(
            available.X + (available.Width - size.Width) / 2,
            available.Y + (available.Height - size.Height) / 2,
            size.Width,
            size.Height);
}
