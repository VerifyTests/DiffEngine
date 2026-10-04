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
/// <param name="Asked">
/// The point the model asked for, before it was moved in. One point for both panes, so it can be
/// somewhere this pane cannot show and the other can.
/// </param>
readonly record struct PicturePlacement(Rectangle Bounds, RectangleF Source, SizeF Size, PanPoint Centre, PanPoint Asked)
{
    /// <summary>
    /// Whether there is more of the picture across than the space shows, by a whole pixel or more,
    /// which is whether a drag can move it that way.
    /// </summary>
    public bool MovesAcross =>
        (int) Size.Width > Bounds.Width;

    /// <summary>
    /// As <see cref="MovesAcross"/>, down.
    /// </summary>
    public bool MovesDown =>
        (int) Size.Height > Bounds.Height;

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
            return new(Centred(available, fitted), new(0, 0, 1, 1), fitted, PanPoint.Centre, new(image.CenterX, image.CenterY));
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
            centre,
            new(image.CenterX, image.CenterY));
    }

    /// <summary>
    /// The centre after the pointer has dragged the picture <paramref name="by"/> pixels: the
    /// picture follows the pointer, so the point at the middle moves the other way. Kept inside
    /// what the panes can show, which is why it is asked here rather than worked out by the model.
    /// <para>
    /// The centre is one point for both panes, and the two pictures need not be the same shape,
    /// so what is moved is the point the model asked for and not the one this pane drew about,
    /// and it is kept inside what the pane that shows less of its picture can show. Moved from
    /// this pane's own and kept to this pane's range, a drag put the other pane's picture where
    /// this one's could go: back to its middle row on the first move of a drag along the other
    /// axis, when all of this one shows from top to bottom, and in from its edge when both can
    /// move and the other can move further.
    /// </para>
    /// <para>
    /// A way this picture cannot move is left as the model had it, whatever the pointer does: the
    /// picture under the pointer is the one being dragged.
    /// </para>
    /// </summary>
    /// <param name="by">How far the pointer has gone since the button went down.</param>
    /// <param name="other">Where the other pane's picture is, or null when it has none.</param>
    public PanPoint Dragged(Size by, PicturePlacement? other = null)
    {
        // How much of its picture shows each way in whichever pane shows less of it
        double across = Source.Width;
        double down = Source.Height;
        if (other is { } beside)
        {
            if (beside.MovesAcross)
            {
                across = Math.Min(across, beside.Source.Width);
            }

            if (beside.MovesDown)
            {
                down = Math.Min(down, beside.Source.Height);
            }
        }

        return new(
            MovesAcross ? Kept(Kept(Asked.X, across) - by.Width / (double) Size.Width, across) : Asked.X,
            MovesDown ? Kept(Kept(Asked.Y, down) - by.Height / (double) Size.Height, down) : Asked.Y);
    }

    /// <summary>
    /// A centre moved in as far as it takes for a pane showing <paramref name="shown"/> of its
    /// picture to stay full.
    /// </summary>
    static double Kept(double value, double shown) =>
        Math.Clamp(value, shown / 2, 1 - shown / 2);

    static Rectangle Centred(Rectangle available, Size size) =>
        new(
            available.X + (available.Width - size.Width) / 2,
            available.Y + (available.Height - size.Height) / 2,
            size.Width,
            size.Height);
}
