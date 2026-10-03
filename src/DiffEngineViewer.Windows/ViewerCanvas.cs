/// <summary>
/// Draws everything except the footer, which is real controls so the buttons keep native focus,
/// keyboard access and theming.
/// <para>
/// One owner drawn surface rather than a control per pane, because <see cref="ScreenBuilder" />
/// has already sliced each pane to the rows that fit. A scrolling control would want to own that
/// decision, and then the text snapshots would stop describing what this shows.
/// </para>
/// </summary>
/// <remarks>
/// The empty designer category opens this in the editor rather than on a design surface. Both
/// controls here are drawn entirely in code, and the designer cannot instantiate them: it would
/// have to run a constructor that loads a font.
/// </remarks>
[DesignerCategory("")]
sealed class ViewerCanvas : Control
{
    /// <summary>
    /// Queue column widths, counted in character cells rather than pixels so a scaled display gets
    /// a column that holds the same number of characters rather than a narrower one.
    /// </summary>
    const int defaultQueueCells = 34;

    const int minQueueCells = 8;

    /// <summary>
    /// What the drag leaves each of the two panes, so the splitter cannot be pushed far enough
    /// right to squeeze them out of existence.
    /// </summary>
    const int minPaneCells = 12;

    /// <summary>
    /// How far either side of the rule counts as grabbing it. The rule is a single pixel, which is
    /// not something a mouse can be asked to hit.
    /// </summary>
    const int grab = 4;

    /// <summary>
    /// Marker, space, four digit line number, two spaces. Matches AsciiRenderer's gutter, so a
    /// line lands in the same column in both.
    /// </summary>
    const int gutterCells = 8;

    const int padding = 6;
    const int gap = 4;

    /// <summary>
    /// The side of a checker square behind a picture, so an image with transparency reads as
    /// transparent rather than as whatever colour the pane happens to be.
    /// </summary>
    const int checker = 8;

    /// <summary>
    /// One turn of the spinner, and how often it is redrawn while one is showing: often enough to
    /// read as turning, and no more, since each step is a paint.
    /// </summary>
    const int spinPeriod = 1000;

    const int spinStep = 40;

    readonly Font font = MonoFont.Create();

    /// <summary>
    /// Where the last paint drew a spinner, which is what <see cref="Animate"/> repaints. Only
    /// those rectangles, so a turning spinner does not redraw every row a few dozen times a second.
    /// </summary>
    readonly List<Rectangle> spinners = [];

    long lastSpin;

    /// <summary>
    /// Pictures are decoded and composed during the paint rather than on the pool, and a spinner
    /// stands still at twelve o'clock. For a capture, which draws one frame and has no later paint
    /// for a background job to land in, and has to come out the same every time.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Synchronous { get; set; }

    /// <summary>
    /// Where the last paint drew a spinner, for the tests.
    /// </summary>
    internal IReadOnlyList<Rectangle> Spinners => spinners;

    readonly QueueTips tips = new();

    readonly ImageCache images;

    Screen? screen;

    /// <summary>
    /// The queue column as the reader last dragged it, in cells. Zero until they do, which is what
    /// <see cref="defaultQueueCells" /> answers for.
    /// <para>
    /// Cells rather than pixels so that it survives a change of display scaling: the column then
    /// holds the same number of characters on the new one rather than the same number of pixels.
    /// </para>
    /// </summary>
    int queueCells;

    /// <summary>
    /// Measured once, from a Graphics, and thrown away when the display scaling changes: the same
    /// point size is a different number of pixels there.
    /// </summary>
    Size cell;

    bool dragging;

    /// <summary>
    /// Whether the left button is down over a pane, and where it went down. The side is fixed for
    /// the life of the drag: a selection belongs to one pane, so crossing into the other extends
    /// within the first rather than jumping.
    /// </summary>
    bool selecting;

    PaneSide selectSide;

    int selectAnchorRow;

    int selectAnchorColumn;

    /// <summary>
    /// The drag as the input model wants it, or null. Held rather than raised as an event, because
    /// it has to be reported on every frame the button is held - the model takes both ends each
    /// time - and a press and release that both land between two drains still has to arrive.
    /// </summary>
    (PaneSide Side, int AnchorRow, int AnchorColumn, int FocusRow, int FocusColumn)? drag;

    public ViewerCanvas()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Palette.Background;
        images = new(Post);
    }

    /// <summary>
    /// Hands a finished background decode back to this thread. Throws when the handle has gone,
    /// which the cache takes as the window having closed under the decode.
    /// </summary>
    void Post(Action action) =>
        BeginInvoke(action);

    public event Action<int>? QueueItemClicked;

    /// <summary>
    /// The row, and where in this control it was clicked. The point anchors the popup, and this is
    /// the only place that knows it.
    /// </summary>
    public event Action<int, Point>? QueueItemRightClicked;

    /// <summary>
    /// A right click over a pane, and where in this control it landed, which anchors the popup as
    /// it does for a queue row.
    /// </summary>
    public event Action<PaneSide, Point>? PaneRightClicked;

    /// <summary>Notches, positive for up, matching what the shim reports.</summary>
    public event Action<int>? Scrolled;

    /// <summary>
    /// How many body rows fit. Reported back as part of the grid size so ScreenBuilder slices to
    /// exactly what is drawable, rather than to a guess from a hardcoded cell height.
    /// </summary>
    public int BodyCapacity =>
        Math.Max(1, (Height - BodyTop - padding) / Cell.Height);

    public int ColumnCapacity =>
        Math.Max(40, Width / Cell.Width);

    /// <summary>
    /// The drag in progress, for <see cref="ViewerForm.Drain" />. Cleared on the read after the
    /// button came up, so the last position is reported once more and then stops.
    /// </summary>
    public (PaneSide Side, int AnchorRow, int AnchorColumn, int FocusRow, int FocusColumn)? TakeDrag()
    {
        var taken = drag;
        if (!selecting)
        {
            drag = null;
        }

        return taken;
    }

    public void Draw(Screen value)
    {
        screen = value;
        images.Keep(PicturesOn(value));
        // Started now rather than at the first paint, which is at least a pump away. The pane
        // draws its rows meanwhile, and the picture under them once it is decoded
        if (!Synchronous)
        {
            foreach (var image in ImagesOn(value))
            {
                images.Get(image.Path, image.Hash, Invalidate);
            }
        }

        // A new screen renumbers the rows, so a kept index would describe a different entry.
        tips.Forget(this);
        Invalidate();
    }

    /// <summary>
    /// Turns any spinner the last paint drew, a step at a time. Called every frame, from the loop
    /// that pumps this window, so a spinner turns without a timer of its own and stops the paint
    /// after the picture lands, which draws none.
    /// </summary>
    public void Animate()
    {
        if (spinners.Count == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - lastSpin < spinStep)
        {
            return;
        }

        lastSpin = now;
        foreach (var spinner in spinners)
        {
            Invalidate(spinner);
        }
    }

    static IEnumerable<ImagePane> ImagesOn(Screen screen)
    {
        if (screen.Left.Image is { } left)
        {
            yield return left;
        }

        if (screen.Right.Image is { } right)
        {
            yield return right;
        }
    }

    static List<string> PicturesOn(Screen screen)
    {
        var paths = new List<string>(2);
        if (screen.Left.Image is { } left)
        {
            paths.Add(left.Path);
        }

        if (screen.Right.Image is { } right)
        {
            paths.Add(right.Path);
        }

        return paths;
    }

    Size Cell
    {
        get
        {
            if (cell.IsEmpty)
            {
                using var graphics = CreateGraphics();
                cell = MonoFont.Cell(graphics, font);
                advance = MonoFont.Advance(graphics, font);
            }

            return cell;
        }
    }

    /// <summary>
    /// Where glyphs actually land within a line, for anything placed under or against them. The
    /// cell stays whole pixels for laying out the grid. See <see cref="MonoFont.Advance"/>.
    /// </summary>
    float Advance
    {
        get
        {
            _ = Cell;
            return advance;
        }
    }

    float advance;

    /// <summary>
    /// The pixel offset of a column into a line of text.
    /// </summary>
    int Offset(int column) =>
        (int) Math.Round(column * Advance);

    /// <summary>
    /// Everything drawn here is laid out in character cells, and a cell is measured in pixels from
    /// a Graphics, which is per display. Dragging the window to a display with different scaling
    /// left that measurement behind: the framework drew the same eleven point glyphs half again as
    /// large while the row pitch, the gutter and the queue column stayed where they were - rows
    /// overlapping, labels clipped, and a body row count that did not match what was on screen.
    /// The other way round left gaps.
    /// </summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        cell = Size.Empty;
        Invalidate();
    }

    int BodyTop =>
        padding + (Cell.Height + gap) * 2 + gap * 2;

    /// <summary>
    /// Clamped on every read rather than only when dragged, so shrinking the window narrows the
    /// column instead of leaving the panes with nothing.
    /// </summary>
    int QueueWidth =>
        Clamp(Cell.Width * (queueCells == 0 ? defaultQueueCells : queueCells));

    int Clamp(int value)
    {
        var min = Cell.Width * minQueueCells;
        var max = Math.Max(min, Width - padding * 2 - gap - Cell.Width * minPaneCells * 2);
        return Math.Min(Math.Max(value, min), max);
    }

    /// <summary>
    /// Where the rule between the queue and the panes is drawn, which is also what the drag moves.
    /// </summary>
    int SplitterX =>
        padding + QueueWidth + gap / 2;

    bool OverSplitter(int x) =>
        screen is not null &&
        screen.Queue.Count > 0 &&
        Math.Abs(x - SplitterX) <= grab;

    /// <summary>
    /// Where the two panes ended up. Read by the paint and by the hit testing, which is the point:
    /// a highlight drawn from one set of numbers and a drag resolved from another would select one
    /// run of characters and colour a different one.
    /// </summary>
    (int Left, int Half, int Width) Panes()
    {
        var queue = screen is { Queue.Count: > 0 } ? QueueWidth : 0;
        var left = queue > 0 ? padding + queue + gap : padding;
        var width = Math.Max(2 * Cell.Width, Width - padding - left);
        return (left, width / 2, width);
    }

    /// <summary>
    /// Where a pane's row text starts, which is its column plus the gutter.
    /// </summary>
    int TextLeft(PaneSide side)
    {
        var panes = Panes();
        return (side == PaneSide.Left ? panes.Left : panes.Left + panes.Half) + gutterCells * Cell.Width;
    }

    /// <summary>
    /// The pane cell under a point, or null when the point is not over one. Rows are rows of the
    /// whole side rather than of the visible slice, since that is what a selection is anchored in.
    /// </summary>
    /// <summary>
    /// Internal so PaneHitTests can round-trip a drawn highlight back through it, which is the
    /// only check that the painter and the hit test are reading the same layout.
    /// </summary>
    internal (PaneSide Side, int Row, int Column)? PaneCellAt(Point point)
    {
        if (screen is null)
        {
            return null;
        }

        var panes = Panes();
        if (point.X < panes.Left ||
            point.Y < BodyTop)
        {
            return null;
        }

        var row = (point.Y - BodyTop) / Cell.Height;
        if (row >= SelectableRows)
        {
            return null;
        }

        var side = point.X < panes.Left + panes.Half ? PaneSide.Left : PaneSide.Right;
        return (side, ScrollTop(side) + row, ColumnAt(point.X, side));
    }

    /// <summary>
    /// The pane a point is in, or null when it is in neither: the whole of the pane under its
    /// header, whether or not a row or a picture is under the point.
    /// </summary>
    internal PaneSide? PaneAt(Point point)
    {
        if (screen is null)
        {
            return null;
        }

        var panes = Panes();
        if (point.X < panes.Left ||
            point.X >= panes.Left + panes.Width ||
            point.Y < BodyTop ||
            point.Y >= BodyTop + BodyCapacity * Cell.Height)
        {
            return null;
        }

        return point.X < panes.Left + panes.Half ? PaneSide.Left : PaneSide.Right;
    }

    int ScrollTop(PaneSide side) =>
        side == PaneSide.Left ? screen!.Left.ScrollTop : screen!.Right.ScrollTop;

    /// <summary>
    /// Rounded to the nearest boundary between characters rather than truncated to the one under
    /// the pointer, because a selection ends between two characters and the half a reader is
    /// pointing at is the one they mean.
    /// </summary>
    int ColumnAt(int x, PaneSide side) =>
        Math.Max(0, (int) Math.Floor((x - TextLeft(side)) / Advance + 0.5f));

    /// <summary>
    /// The body row a point is on, clamped into the body. Used while dragging, where a pointer
    /// above or below the rows means the first or last of them rather than nothing.
    /// </summary>
    int DraggedRow(int y) =>
        Math.Clamp((y - BodyTop) / Cell.Height, 0, Math.Max(0, SelectableRows - 1));

    /// <summary>
    /// The rows a pointer selects in: all the body has room for, or with a picture drawn under a
    /// pane's rows only those rows, since below them is the picture. A document's text and page
    /// share a pane, and a drag over the page selecting text rows nobody can see was the result.
    /// A page still being drawn takes the same space, under its spinner.
    /// </summary>
    int SelectableRows
    {
        get
        {
            if (screen!.Left is { Image: null, ImagePending: false } &&
                screen.Right is { Image: null, ImagePending: false })
            {
                return BodyCapacity;
            }

            return Math.Min(BodyCapacity, Math.Max(screen.Left.Rows.Count, screen.Right.Rows.Count));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Palette.Background);
        spinners.Clear();
        pictures.Clear();
        if (screen is null)
        {
            return;
        }

        Painter.Prepare(graphics);
        var clip = e.ClipRectangle;
        var lineHeight = Cell.Height;
        var hasQueue = screen.Queue.Count > 0;
        var queue = hasQueue ? QueueWidth : 0;
        var (panesLeft, half, panesWidth) = Panes();

        DrawTitle(graphics, lineHeight);

        var firstRule = padding + lineHeight + gap;
        DrawRule(graphics, firstRule);

        var headerTop = firstRule + gap;
        if (hasQueue)
        {
            Painter.Draw(graphics, $"Pending ({screen.PendingCount})", font, Palette.Text, Cellular(padding, headerTop, queue, lineHeight));
        }

        Painter.Draw(graphics, screen.Left.Header, font, Palette.Text, Cellular(panesLeft, headerTop, half, lineHeight));
        Painter.Draw(graphics, screen.Right.Header, font, Palette.Text, Cellular(panesLeft + half, headerTop, half, lineHeight));
        DrawRule(graphics, headerTop + lineHeight + gap);

        var bodyTop = BodyTop;
        var capacity = BodyCapacity;
        var rows = Math.Min(capacity, Math.Max(screen.Queue.Count, Math.Max(screen.Left.Rows.Count, screen.Right.Rows.Count)));
        for (var index = 0; index < rows; index++)
        {
            var top = bodyTop + index * lineHeight;
            // A paint that only turns a spinner is clipped to it, and laying out every row's text
            // only for all of it to be clipped away would be most of what that paint did
            if (top + lineHeight <= clip.Top ||
                top >= clip.Bottom)
            {
                continue;
            }

            if (hasQueue)
            {
                DrawQueueItem(graphics, index, new(padding, top, queue, lineHeight));
            }

            DrawRow(graphics, screen.Left, index, new(panesLeft, top, half, lineHeight));
            DrawRow(graphics, screen.Right, index, new(panesLeft + half, top, panesWidth - half, lineHeight));
        }

        var bodyBottom = bodyTop + capacity * lineHeight;

        // Under the rows rather than instead of them. The rows are what every head draws — format,
        // size and byte count, coloured against the other side — and this head can afford to also
        // show the thing they describe.
        // Both in the same width, not the pixel more an odd width leaves the right pane. Two
        // pictures of one size are then fitted to one size, rather than a pixel apart. And one
        // picture on both sides, which a page two identical documents share is, is composed once:
        // the cache keeps a composite per picture, so asked for at two sizes it composed each in
        // turn for as long as the entry was on screen, every landing throwing the other away.
        DrawImage(graphics, screen.Left, panesLeft, half, bodyTop, bodyBottom, lineHeight);
        DrawImage(graphics, screen.Right, panesLeft + half, half, bodyTop, bodyBottom, lineHeight);

        if (hasQueue)
        {
            DrawColumnRule(graphics, panesLeft - gap / 2, bodyTop, bodyBottom);
        }

        DrawColumnRule(graphics, panesLeft + half - gap / 2, bodyTop, bodyBottom);
    }

    void DrawImage(Graphics graphics, Pane pane, int left, int width, int bodyTop, int bodyBottom, int lineHeight)
    {
        if (pane is { Image: null, ImagePending: false })
        {
            return;
        }

        var top = bodyTop + pane.Rows.Count * lineHeight + lineHeight;
        var available = new Rectangle(left, top, width - gap, bodyBottom - top);
        if (available.Width <= 0 ||
            available.Height <= 0)
        {
            return;
        }

        // A page still being drawn, which has no size yet: the spinner goes where the page will be
        // centred once it lands
        if (pane.Image is not { } image)
        {
            DrawSpinner(graphics, available, lineHeight);
            return;
        }

        // Where the pointer finds it, whether or not it has been decoded yet: the wheel turned over
        // a spinner is still turned over a picture
        pictures.Add(new(available, image));

        var decoded = Synchronous
            ? images.Get(image.Path, image.Hash)
            : images.Get(image.Path, image.Hash, Invalidate);
        if (decoded is null)
        {
            // Nothing at all for a picture this machine cannot decode: the rows have said what it is
            if (images.Loading(image.Path))
            {
                DrawSpinner(graphics, available, lineHeight);
            }

            return;
        }

        var placement = PicturePlacement.Of(available, image);
        var bounds = placement.Bounds;
        if (image.Zoom > 1)
        {
            // From a copy of the whole of it at that size while one is small enough to keep, and
            // otherwise straight from the picture
            if (!DrawScaled(graphics, image, placement, available, lineHeight))
            {
                DrawEnlarged(graphics, image, placement);
            }

            return;
        }

        var drawn = bounds.Size;

        // Copied rather than drawn: the checkerboard and the scaled picture are composed once per
        // picture and size, on the pool, and every paint after that is a copy of the result
        var composite = Synchronous
            ? images.Composite(image.Path, drawn, Compose)
            : images.Composite(image.Path, drawn, Compose, Invalidate);
        if (composite is null)
        {
            if (images.Loading(image.Path))
            {
                DrawSpinner(graphics, available, lineHeight);
            }

            return;
        }

        var interpolation = graphics.InterpolationMode;
        var offset = graphics.PixelOffsetMode;
        // Pixel for pixel when it was composed at this size. Otherwise it is the size it was last
        // composed at, mid resize, stretched into place until this size has been composed: rough
        // for a frame or two, where a spinner would flash on every step of the drag
        graphics.InterpolationMode = composite.Size == drawn
            ? InterpolationMode.NearestNeighbor
            : InterpolationMode.Bilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(composite, bounds);
        // Put back, because the text drawing this shares a Graphics with is set up once by Painter
        // and would otherwise inherit whichever picture was drawn last.
        graphics.InterpolationMode = interpolation;
        graphics.PixelOffsetMode = offset;

        // An outline, so a picture whose edges are the colour of the pane still has visible extent.
        using var pen = new Pen(Palette.Rule);
        graphics.DrawRectangle(pen, bounds.X - 1, bounds.Y - 1, bounds.Width + 1, bounds.Height + 1);
    }

    /// <summary>
    /// A picture the reader has zoomed into, but only as far as half its own size or less: the
    /// part of it that shows, copied out of the whole of it scaled to that size. False when there
    /// is no such copy to draw from and none coming, which leaves it to
    /// <see cref="DrawEnlarged"/>: the picture is further in than that, or could not be scaled.
    /// <para>
    /// Drawn straight from the picture, this far out, the part that shows is many times the
    /// pane's pixels, and the filter a reduction needs reads every one of them on every paint: a
    /// pair of 4000 by 3000 pictures at 150% was 54 ms a paint, on every frame of a drag, and is 2
    /// copied. The whole of it at this size is at most a quarter of the picture's own pixels, so
    /// it is kept as the fitted one is, made on the pool and replaced when another size is asked
    /// for. Where the picture is dragged to is not part of what is kept, so a drag copies a
    /// different part of the same thing.
    /// </para>
    /// <para>
    /// Past half its size the whole of it is too much to keep a second copy of, up to the
    /// picture's own size again, and the part that shows is under four times the pane's pixels.
    /// </para>
    /// </summary>
    bool DrawScaled(Graphics graphics, ImagePane image, PicturePlacement placement, Rectangle available, int lineHeight)
    {
        if (image.Width < placement.Size.Width * 2)
        {
            return false;
        }

        var size = new Size(
            (int) Math.Round(placement.Size.Width),
            (int) Math.Round(placement.Size.Height));
        var scaled = Synchronous
            ? images.Composite(image.Path, size, Scale)
            : images.Composite(image.Path, size, Scale, Invalidate);
        var landed = scaled is not null &&
                     scaled.Size == size;
        if (!landed &&
            images.Idle(image.Path) is not null)
        {
            // Not at this size, and nothing on the pool is making it: it could not be made
            return false;
        }

        if (scaled is null)
        {
            DrawSpinner(graphics, available, lineHeight);
            return true;
        }

        var bounds = placement.Bounds;
        FillChecker(graphics, bounds);
        var interpolation = graphics.InterpolationMode;
        var offset = graphics.PixelOffsetMode;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        if (landed)
        {
            // Pixel for pixel, from a whole pixel of it
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.DrawImage(
                scaled,
                bounds,
                new Rectangle(
                    Math.Clamp(Whole(placement.Source.X * size.Width), 0, size.Width - bounds.Width),
                    Math.Clamp(Whole(placement.Source.Y * size.Height), 0, size.Height - bounds.Height),
                    bounds.Width,
                    bounds.Height),
                GraphicsUnit.Pixel);
        }
        else
        {
            // Whatever was last composed, at the size it was, with the same part of it stretched
            // into place: rough for the frame or two until this size lands and repaints
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.DrawImage(
                scaled,
                bounds,
                new RectangleF(
                    placement.Source.X * scaled.Width,
                    placement.Source.Y * scaled.Height,
                    placement.Source.Width * scaled.Width,
                    placement.Source.Height * scaled.Height),
                GraphicsUnit.Pixel);
        }

        graphics.InterpolationMode = interpolation;
        graphics.PixelOffsetMode = offset;

        using var pen = new Pen(Palette.Rule);
        graphics.DrawRectangle(pen, bounds.X - 1, bounds.Y - 1, bounds.Width + 1, bounds.Height + 1);
        return true;
    }

    /// <summary>
    /// The pixel of a scaled picture that the part showing starts at: the nearest to where the
    /// placement puts it, which is anywhere between two. Half way goes up, and so does a little
    /// short of half way. A picture centred in its pane starts on a whole pixel or exactly half
    /// way to the next, and a drag moves it a pixel at a time from there, so half way is where it
    /// stays for the whole of the drag. Rounded on that line it fell either way with whatever the
    /// arithmetic left in its last digit, and the picture stood still for one pixel of the drag
    /// and jumped two for the next.
    /// </summary>
    static int Whole(float position) =>
        (int) Math.Floor(position + 0.51f);

    /// <summary>
    /// The whole picture at <paramref name="size"/>, with nothing under it: the checkerboard
    /// behind an enlarged picture stays where the pane is while the picture is dragged across it,
    /// so it is not part of what is kept. Scaled with the filter the part that shows was drawn
    /// with when it was scaled on every paint, so what a reader sees at a size is what they saw.
    /// <para>
    /// Runs on the pool for the window, as <see cref="Compose"/> does.
    /// </para>
    /// </summary>
    static Bitmap Scale(Image picture, Size size)
    {
        var scaled = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(picture, new Rectangle(Point.Empty, size));
        return scaled;
    }

    /// <summary>
    /// A picture the reader has zoomed into past half its own size: the part of it that shows,
    /// drawn straight from the decoded picture rather than from a copy of the whole of it at that
    /// size, which at the last step would be hundreds of megabytes to show one corner.
    /// <para>
    /// On this thread, on every paint. Past its own size it is the pane's worth of the picture's
    /// pixels however far in it is, and between that and half its size no more than four times
    /// the pane's worth, where <see cref="DrawScaled"/> has the rest. A drag asks for a different
    /// part on every frame, so there is nothing a cache of what shows could keep.
    /// </para>
    /// </summary>
    void DrawEnlarged(Graphics graphics, ImagePane image, PicturePlacement placement)
    {
        var bounds = placement.Bounds;
        // The decoded picture, unless a compose on the pool still has it from when it was fitted.
        // Then the composite it last made, which is the same picture smaller: rough for the frame
        // or two until the compose lands and repaints.
        var picture = images.Idle(image.Path) ?? images.Composited(image.Path);
        if (picture is null)
        {
            return;
        }

        var source = new RectangleF(
            placement.Source.X * picture.Width,
            placement.Source.Y * picture.Height,
            placement.Source.Width * picture.Width,
            placement.Source.Height * picture.Height);

        FillChecker(graphics, bounds);
        var interpolation = graphics.InterpolationMode;
        var offset = graphics.PixelOffsetMode;
        // Its pixels as they are once it is past its own size, which is what zooming that far in
        // is for: smoothed, a one pixel difference between the two sides is a blur on both.
        // Short of that, the filter that reads every pixel it reduces. Plain bilinear would do for
        // a reduction of under two, and is the one GDI+ takes longer over: 14 ms a pane to 9.
        graphics.InterpolationMode = placement.Size.Width >= picture.Width
            ? InterpolationMode.NearestNeighbor
            : InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(picture, bounds, source, GraphicsUnit.Pixel);
        graphics.InterpolationMode = interpolation;
        graphics.PixelOffsetMode = offset;

        using var pen = new Pen(Palette.Rule);
        graphics.DrawRectangle(pen, bounds.X - 1, bounds.Y - 1, bounds.Width + 1, bounds.Height + 1);
    }

    /// <summary>
    /// The checkerboard under an enlarged picture, its squares starting at the corner of
    /// <paramref name="bounds"/> as the composed one's do, and staying there while the picture is
    /// dragged across them.
    /// <para>
    /// Drawn on every paint of an enlarged picture, where the composed one is drawn once per size.
    /// It was a brush tiling one pair of squares for that reason, and the brush was the dear way:
    /// GDI+ took over 3 ms to fill a pane from a texture, and takes half a millisecond to fill it
    /// with a colour and half its squares with another. Once the picture over it is a copy rather
    /// than a rescale, that was most of what a frame of a drag cost.
    /// </para>
    /// </summary>
    void FillChecker(Graphics graphics, Rectangle bounds) =>
        DrawChecker(
            graphics,
            bounds,
            Painter.Brush(Palette.CheckerLight),
            Painter.Brush(Palette.CheckerDark),
            ref darkSquares);

    /// <summary>
    /// Where the dark squares of the last checkerboard painted were, kept so that the next paint
    /// has somewhere to list its own.
    /// </summary>
    Rectangle[] darkSquares = [];

    /// <summary>
    /// Where the last paint put each side's picture, or the space one is on its way to. What the
    /// wheel and a drag are resolved against.
    /// </summary>
    readonly List<PictureArea> pictures = [];

    readonly record struct PictureArea(Rectangle Available, ImagePane Image);

    PictureArea? PictureAt(Point point)
    {
        foreach (var picture in pictures)
        {
            if (picture.Available.Contains(point))
            {
                return picture;
            }
        }

        return null;
    }

    /// <summary>
    /// An enlarged picture being dragged about: where the button went down, and the placement it
    /// had then, which is what the drag is measured against for as long as it is held.
    /// </summary>
    bool panning;

    Point panStart;

    PicturePlacement panFrom;

    /// <summary>
    /// Where a drag has left the middle of the picture, until <see cref="TakePan"/> reports it.
    /// </summary>
    PanPoint? pan;

    /// <summary>
    /// Where a drag of an enlarged picture has moved it to since the last call, for
    /// <see cref="ViewerForm.Drain" />, or null when it has not moved.
    /// </summary>
    public PanPoint? TakePan()
    {
        var taken = pan;
        pan = null;
        return taken;
    }

    /// <summary>
    /// Wheel notches turned over a picture, or with control held: positive for up, which is in.
    /// </summary>
    public event Action<int>? Zoomed;

    /// <summary>
    /// Where the last paint put the pictures, for the tests.
    /// </summary>
    internal IReadOnlyList<Rectangle> PictureAreas =>
        pictures
            .Select(_ => _.Available)
            .ToList();

    /// <summary>
    /// Something turning, centred in <paramref name="available"/>, while the picture for it is on
    /// its way. Stood still in a capture, which has to come out the same every time.
    /// </summary>
    void DrawSpinner(Graphics graphics, Rectangle available, int lineHeight)
    {
        var radius = lineHeight;
        var thickness = Math.Max(2, lineHeight / 6);
        var diameter = radius * 2;
        if (available.Width < diameter + thickness * 2 ||
            available.Height < diameter + thickness * 2)
        {
            return;
        }

        var bounds = new Rectangle(
            available.X + (available.Width - diameter) / 2,
            available.Y + (available.Height - diameter) / 2,
            diameter,
            diameter);
        var turned = Synchronous ? 0 : Environment.TickCount64 % spinPeriod * 360f / spinPeriod;

        var smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var track = new Pen(Palette.Rule, thickness))
        {
            graphics.DrawEllipse(track, bounds);
        }

        using (var arc = new Pen(Palette.Dim, thickness))
        {
            arc.StartCap = LineCap.Round;
            arc.EndCap = LineCap.Round;
            // From twelve o'clock, clockwise
            graphics.DrawArc(arc, bounds, turned - 90, 90);
        }

        graphics.SmoothingMode = smoothing;

        var dirty = bounds;
        dirty.Inflate(thickness + 1, thickness + 1);
        spinners.Add(dirty);
    }

    /// <summary>
    /// The picture as a pane shows it at <paramref name="size"/>: over the checkerboard, so an image
    /// with transparency reads as one, and scaled with the high quality filter a downscale needs.
    /// Premultiplied, which is what the double buffer it is copied into holds.
    /// <para>
    /// Runs on the pool for the window, so nothing it draws with is shared with the UI thread:
    /// GDI+ objects are not to be used from two threads at once.
    /// </para>
    /// </summary>
    static Bitmap Compose(Image picture, Size size)
    {
        var composite = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(composite);
        var bounds = new Rectangle(Point.Empty, size);
        DrawChecker(graphics, bounds);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(picture, bounds);
        return composite;
    }

    /// <summary>
    /// With brushes of its own rather than <see cref="Painter.Brush"/>'s, which the UI thread is
    /// drawing text with while this runs on the pool.
    /// </summary>
    static void DrawChecker(Graphics graphics, Rectangle bounds)
    {
        using var light = new SolidBrush(Palette.CheckerLight);
        using var dark = new SolidBrush(Palette.CheckerDark);
        Rectangle[] squares = [];
        DrawChecker(graphics, bounds, light, dark, ref squares);
    }

    /// <summary>
    /// One colour over all of it and the other over every second square, those as one list handed
    /// over together: a call a square is most of what a square costs. The list is written into
    /// <paramref name="squares"/>, which is made longer when it has to be.
    /// </summary>
    static void DrawChecker(Graphics graphics, Rectangle bounds, Brush light, Brush dark, ref Rectangle[] squares)
    {
        graphics.FillRectangle(light, bounds);
        var count = 0;
        for (var y = bounds.Y; y < bounds.Bottom; y += checker)
        {
            for (var x = bounds.X; x < bounds.Right; x += checker)
            {
                if ((x - bounds.X) / checker % 2 == (y - bounds.Y) / checker % 2)
                {
                    continue;
                }

                if (count == squares.Length)
                {
                    Array.Resize(ref squares, Math.Max(64, count * 2));
                }

                squares[count++] = Rectangle.Intersect(new(x, y, checker, checker), bounds);
            }
        }

        // None at all under a picture no larger than one square, and GDI+ takes a list of nothing
        // as a mistake rather than as nothing to do
        if (count > 0)
        {
            graphics.FillRectangles(dark, squares.AsSpan(0, count));
        }
    }

    void DrawTitle(Graphics graphics, int lineHeight)
    {
        Painter.Draw(graphics, screen!.Title, font, Palette.Text, Cellular(padding, padding, Width - padding * 2, lineHeight));
        if (screen.Subtitle.Length == 0)
        {
            return;
        }

        var width = Offset(screen.Subtitle.Length);
        Painter.Draw(graphics, screen.Subtitle, font, Palette.Dim, Cellular(Width - padding - width, padding, width, lineHeight));
    }

    void DrawQueueItem(Graphics graphics, int index, Rectangle bounds)
    {
        if (index >= screen!.Queue.Count)
        {
            return;
        }

        var item = screen.Queue[index];
        if (item.Kind == QueueRowKind.Header)
        {
            // Flush left with no selection fill, dimmed: a heading, not a clickable row.
            Painter.Draw(graphics, item.Label, font, Palette.Dim, Cellular(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            return;
        }

        if (item.Selected)
        {
            graphics.FillRectangle(Painter.Brush(Palette.Selected), bounds);
        }

        var failed = item.Status is not null;
        Painter.Draw(
            graphics,
            failed ? $"{item.Label} !" : item.Label,
            font,
            failed ? Palette.Foreground(RowKind.Removed) : Palette.Text,
            Cellular(bounds.X + Cell.Width, bounds.Y, bounds.Width - Cell.Width, bounds.Height));
    }

    void DrawRow(Graphics graphics, Pane pane, int index, Rectangle bounds)
    {
        if (index >= pane.Rows.Count)
        {
            return;
        }

        var row = pane.Rows[index];
        if (Palette.RowBackground(row.Kind) is { } background)
        {
            graphics.FillRectangle(Painter.Brush(background), bounds);
        }

        if (row.Kind == RowKind.Filler)
        {
            return;
        }

        var gutter = gutterCells * Cell.Width;
        // Behind the text rather than over it, and the text keeps its own colour: what kind of
        // change a line is has to survive being selected.
        if (row.Selection.Length > 0)
        {
            graphics.FillRectangle(
                Painter.Brush(Palette.Selection),
                Rectangle.Intersect(
                    new(
                        bounds.X + gutter + Offset(row.Selection.Start),
                        bounds.Y,
                        Offset(row.Selection.Start + row.Selection.Length) - Offset(row.Selection.Start),
                        bounds.Height),
                    bounds));
        }

        Painter.Draw(
            graphics,
            $"{Palette.Marker(row.Kind)} {row.LineNumber,4}",
            font,
            Palette.Dim,
            Cellular(bounds.X, bounds.Y, gutter, bounds.Height));
        // No more of the row than the pane has cells for. GDI+ lays out every character it is
        // handed before it clips any of them, so a row handed over whole cost by its length
        // rather than by what of it showed: 72 rows of long lines were 18 ms a paint where the 54
        // characters of each that show are 3.5, and every wheel notch and every frame of a
        // selection drag is a paint. Cut before it is segmented as well, which walked the whole
        // of a row that was not all ASCII: a megabyte of one was 14 ms a row.
        var text = RowText.Shown(row.Text, CellsAcross(bounds.Width - gutter));

        // Each segment at its column on the grid rather than the row as one string, so a character
        // the font draws wider or narrower than a cell moves nothing after it: see CellGrid. A row
        // of plain text is one segment at column 0, drawn exactly as the whole row was.
        foreach (var segment in CellGrid.Segments(text))
        {
            var left = bounds.X + gutter + Offset(segment.Column);
            if (left >= bounds.Right)
            {
                break;
            }

            Painter.Draw(
                graphics,
                // A character takes its marks into its cell with it, however many it has, so the
                // cells a pane holds do not bound what is in them. As many characters as the pane
                // is pixels wide does, which only a pile of marks reaches. Not as many as there
                // are pixels left of it, which this was: an emoji is two, so one whose first
                // column of pixels was the pane's last was cut to nothing, and a joined sequence
                // within its own length of the edge lost the last of what it joins
                RowText.Clip(text.Substring(segment.Start, segment.Length), bounds.Width),
                font,
                Palette.Foreground(row.Kind),
                Cellular(left, bounds.Y, bounds.Right - left, bounds.Height));
        }
    }

    /// <summary>
    /// How many cells of a row's text <paramref name="width"/> pixels show any part of, and one
    /// more. A glyph is not confined to its cell: GDI+ fits each to whole pixels, which can start
    /// one in the last pixel of the cell before its own, so the cell after the last one showing
    /// can still put ink in the pane. With that one kept, the first cell left out starts a whole
    /// cell past the edge. And where GDI+ puts a glyph does not depend on what follows it in the
    /// string, which is what makes the cut one that cannot be seen.
    /// </summary>
    int CellsAcross(int width) =>
        Math.Max(0, (int) Math.Ceiling(width / Advance)) + 1;

    void DrawRule(Graphics graphics, int top) =>
        graphics.FillRectangle(Painter.Brush(Palette.Rule), padding, top, Width - padding * 2, 1);

    static void DrawColumnRule(Graphics graphics, int left, int top, int bottom) =>
        graphics.FillRectangle(Painter.Brush(Palette.Rule), left, top, 1, bottom - top);

    /// <summary>
    /// GDI+ measures and clips in floats, and the layout is all integers, so the conversion lives
    /// in one place rather than at every call.
    /// </summary>
    static RectangleF Cellular(int left, int top, int width, int height) =>
        new(left, top, Math.Max(0, width), height);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (screen is null)
        {
            return;
        }

        // Checked before the queue hit test, because the grab zone overlaps the right edge of the
        // column and a drag that started there would otherwise also select whatever it began over.
        if (e.Button == MouseButtons.Left &&
            OverSplitter(e.X))
        {
            dragging = true;
            Capture = true;
            return;
        }

        var index = QueueRowAt(e.Location);
        if (index >= 0)
        {
            if (e.Button == MouseButtons.Right)
            {
                QueueItemRightClicked?.Invoke(index, e.Location);
                return;
            }

            QueueItemClicked?.Invoke(index);
            return;
        }

        // Anywhere in a pane, its text or under it: the menu is the pane's, and a file of three
        // lines has most of its pane under them.
        if (e.Button == MouseButtons.Right)
        {
            if (PaneAt(e.Location) is { } side)
            {
                PaneRightClicked?.Invoke(side, e.Location);
            }

            return;
        }

        // A picture enlarged past its pane is taken hold of and moved. One that fits has nowhere to
        // go, so a press on it is the nothing it always was.
        if (e.Button == MouseButtons.Left &&
            PictureAt(e.Location) is { Image.Zoom: > 1 } picture)
        {
            panning = true;
            Capture = true;
            panStart = e.Location;
            panFrom = PicturePlacement.Of(picture.Available, picture.Image);
            return;
        }

        // Not gated on there being a queue: file mode has two panes and no column, and its text is
        // as worth copying as anything else.
        if (e.Button != MouseButtons.Left ||
            PaneCellAt(e.Location) is not { } cell)
        {
            return;
        }

        selecting = true;
        Capture = true;
        selectSide = cell.Side;
        selectAnchorRow = cell.Row;
        selectAnchorColumn = cell.Column;
        // Both ends on the press, so a click with no drag behind it reports an empty selection,
        // which is what clears the previous one.
        drag = (cell.Side, cell.Row, cell.Column, cell.Row, cell.Column);
    }

    /// <summary>
    /// The queue row under a point, or -1. Shared by the click and the tooltip, so the two cannot
    /// disagree about what is being pointed at.
    /// </summary>
    int QueueRowAt(Point point)
    {
        if (screen is null ||
            screen.Queue.Count == 0 ||
            point.X < padding ||
            point.X >= padding + QueueWidth ||
            // Integer division truncates toward zero, so without this the whole header band above
            // the body answers row 0.
            point.Y < BodyTop)
        {
            return -1;
        }

        var index = (point.Y - BodyTop) / Cell.Height;
        return index < screen.Queue.Count ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // A move with the button up is a drag whose release went somewhere else - the button let go
        // over another window after an Alt+Tab, say - and not one still going.
        if ((selecting || dragging || panning) &&
            (e.Button & MouseButtons.Left) == 0)
        {
            EndDrag();
        }

        if (panning)
        {
            // From where the button went down rather than from the last move, so the picture is
            // where the pointer has taken it however the moves in between were reported
            pan = panFrom.Dragged(new(e.X - panStart.X, e.Y - panStart.Y));
            return;
        }

        if (selecting)
        {
            // Against the side the press landed in, whatever the pointer has wandered over since:
            // a selection is one pane's, and the other pane's rows are a different document.
            drag = (
                selectSide,
                selectAnchorRow,
                selectAnchorColumn,
                ScrollTop(selectSide) + DraggedRow(e.Y),
                ColumnAt(e.X, selectSide));
            return;
        }

        if (dragging)
        {
            // Clamped as a width, then held as cells, so the drag stops where it always stopped
            // and what is remembered is a number of characters
            var cells = Math.Max(1, Clamp(e.X - padding - gap / 2) / Cell.Width);
            if (cells != queueCells)
            {
                queueCells = cells;
                Invalidate();
            }

            return;
        }

        // Assigned only on a change: setting Cursor is a window message, and this runs on every
        // pixel the mouse moves over the canvas. The beam over a pane is the only thing that says
        // the text there can be selected at all.
        var wanted = OverSplitter(e.X)
            ? Cursors.VSplit
            : PaneCellAt(e.Location) is not null
                ? Cursors.IBeam
                // The four arrows over a picture that can be moved, which is the only thing that
                // says it can be
                : PictureAt(e.Location) is { Image.Zoom: > 1 }
                    ? Cursors.SizeAll
                    : Cursors.Default;
        if (Cursor != wanted)
        {
            Cursor = wanted;
        }

        ApplyTooltip(e.Location);
    }

    void ApplyTooltip(Point point)
    {
        // Composed by QueueProjection, so what a row has to add — and whether it has anything at
        // all — is decided once for all three heads rather than three times here.
        var row = QueueRowAt(point);
        tips.Apply(this, row, row < 0 ? null : screen!.Queue[row].Tooltip);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    /// <summary>
    /// The mouse was taken away mid drag: Alt+Tab, the Windows key, a UAC prompt, another window
    /// grabbing it. Only the window holding capture hears the button come up, so without this the
    /// selection followed the pointer with no button held, and the splitter dragged the queue
    /// column along, until the next click happened to land here.
    /// </summary>
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
        {
            EndDrag();
        }
    }

    void EndDrag()
    {
        if (dragging)
        {
            dragging = false;
            Capture = false;
        }

        if (selecting)
        {
            // The drag itself is left for one more read, so a press and release between two frames
            // still reports the click that cleared the selection.
            selecting = false;
            Capture = false;
        }

        if (panning)
        {
            // Where it was dragged to is left for the next read, as a selection's last end is
            panning = false;
            Capture = false;
        }
    }

    /// <summary>
    /// The resize cursor is set while hovering the rule, so it has to be given back on the way out
    /// rather than left on whatever the pointer moves onto next.
    /// </summary>
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!dragging &&
            Cursor != Cursors.Default)
        {
            Cursor = Cursors.Default;
        }

        tips.Forget(this);
    }

    readonly WheelNotches notches = new(SystemInformation.MouseWheelScrollDelta);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var scrolled = notches.Add(e.Delta);
        if (scrolled == 0)
        {
            return;
        }

        // Over a picture the wheel is for the picture, and anywhere with control held, as it is in
        // everything else that shows one. Everywhere else it scrolls the rows, as it always has.
        if ((ModifierKeys & Keys.Control) == Keys.Control ||
            PictureAt(e.Location) is not null)
        {
            Zoomed?.Invoke(scrolled);
            return;
        }

        Scrolled?.Invoke(scrolled);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            font.Dispose();
            tips.Dispose();
            images.Dispose();
        }

        base.Dispose(disposing);
    }
}
