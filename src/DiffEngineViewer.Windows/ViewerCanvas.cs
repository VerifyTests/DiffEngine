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

    readonly Font font = MonoFont.Create();

    readonly QueueTips tips = new();

    readonly ImageCache images = new();

    Screen? screen;

    /// <summary>
    /// Zero until first asked for, because the default is counted in cells and a cell can only be
    /// measured once a Graphics exists.
    /// </summary>
    int queueWidth;

    bool dragging;

    public ViewerCanvas()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Palette.Background;
    }

    public event Action<int>? QueueItemClicked;

    /// <summary>
    /// The row, and where in this control it was clicked. The point anchors the popup, and this is
    /// the only place that knows it.
    /// </summary>
    public event Action<int, Point>? QueueItemRightClicked;

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

    public void Draw(Screen value)
    {
        screen = value;
        // A new screen renumbers the rows, so a kept index would describe a different entry.
        tips.Forget(this);
        Invalidate();
    }

    Size Cell
    {
        get
        {
            if (field.IsEmpty)
            {
                using var graphics = CreateGraphics();
                field = MonoFont.Cell(graphics, font);
            }

            return field;
        }
    }

    int BodyTop =>
        padding + (Cell.Height + gap) * 2 + gap * 2;

    /// <summary>
    /// Clamped on every read rather than only when dragged, so shrinking the window narrows the
    /// column instead of leaving the panes with nothing.
    /// </summary>
    int QueueWidth
    {
        get
        {
            if (queueWidth == 0)
            {
                queueWidth = Cell.Width * defaultQueueCells;
            }

            return Clamp(queueWidth);
        }
    }

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

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Palette.Background);
        if (screen is null)
        {
            return;
        }

        Painter.Prepare(graphics);
        var lineHeight = Cell.Height;
        var hasQueue = screen.Queue.Count > 0;
        var queue = hasQueue ? QueueWidth : 0;
        var panesLeft = hasQueue ? padding + queue + gap : padding;
        var panesWidth = Math.Max(2 * Cell.Width, Width - padding - panesLeft);
        var half = panesWidth / 2;

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
        DrawImage(graphics, screen.Left, panesLeft, half, bodyTop, bodyBottom, lineHeight);
        DrawImage(graphics, screen.Right, panesLeft + half, panesWidth - half, bodyTop, bodyBottom, lineHeight);

        if (hasQueue)
        {
            DrawColumnRule(graphics, panesLeft - gap / 2, bodyTop, bodyBottom);
        }

        DrawColumnRule(graphics, panesLeft + half - gap / 2, bodyTop, bodyBottom);
    }

    void DrawImage(Graphics graphics, Pane pane, int left, int width, int bodyTop, int bodyBottom, int lineHeight)
    {
        if (pane.Image is not { } image)
        {
            return;
        }

        var picture = images.Get(image.Path);
        if (picture is null)
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

        // Fitted, and never enlarged past its own size: a snapshot is judged against the pixels it
        // has, and an eight pixel icon stretched across a pane is an interpolation of them rather
        // than a look at them.
        //
        // Scaled from the size the model carries rather than from the decoded bitmap, so all three
        // heads place a picture identically even where their decoders would not agree.
        var scale = Math.Min(
            Math.Min(
                available.Width / (double) image.Width,
                available.Height / (double) image.Height),
            1);
        var drawn = new Size(
            Math.Max(1, (int) (image.Width * scale)),
            Math.Max(1, (int) (image.Height * scale)));
        var bounds = new Rectangle(
            available.X + (available.Width - drawn.Width) / 2,
            available.Y + (available.Height - drawn.Height) / 2,
            drawn.Width,
            drawn.Height);

        DrawChecker(graphics, bounds);

        var interpolation = graphics.InterpolationMode;
        var offset = graphics.PixelOffsetMode;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(picture, bounds);
        // Put back, because the text drawing this shares a Graphics with is set up once by Painter
        // and would otherwise inherit whichever picture was drawn last.
        graphics.InterpolationMode = interpolation;
        graphics.PixelOffsetMode = offset;

        // An outline, so a picture whose edges are the colour of the pane still has visible extent.
        using var pen = new Pen(Palette.Rule);
        graphics.DrawRectangle(pen, bounds.X - 1, bounds.Y - 1, bounds.Width + 1, bounds.Height + 1);
    }

    static void DrawChecker(Graphics graphics, Rectangle bounds)
    {
        graphics.FillRectangle(Painter.Brush(Palette.CheckerLight), bounds);
        var dark = Painter.Brush(Palette.CheckerDark);
        for (var y = bounds.Y; y < bounds.Bottom; y += checker)
        {
            for (var x = bounds.X; x < bounds.Right; x += checker)
            {
                if ((x - bounds.X) / checker % 2 == (y - bounds.Y) / checker % 2)
                {
                    continue;
                }

                graphics.FillRectangle(
                    dark,
                    Rectangle.Intersect(new(x, y, checker, checker), bounds));
            }
        }
    }

    void DrawTitle(Graphics graphics, int lineHeight)
    {
        Painter.Draw(graphics, screen!.Title, font, Palette.Text, Cellular(padding, padding, Width - padding * 2, lineHeight));
        if (screen.Subtitle.Length == 0)
        {
            return;
        }

        var width = screen.Subtitle.Length * Cell.Width;
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
        Painter.Draw(
            graphics,
            $"{Palette.Marker(row.Kind)} {row.LineNumber,4}",
            font,
            Palette.Dim,
            Cellular(bounds.X, bounds.Y, gutter, bounds.Height));
        Painter.Draw(
            graphics,
            RowText.Flatten(row.Text),
            font,
            Palette.Foreground(row.Kind),
            Cellular(bounds.X + gutter, bounds.Y, bounds.Width - gutter, bounds.Height));
    }

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
        if (screen is null ||
            screen.Queue.Count == 0)
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
        if (index < 0)
        {
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            QueueItemRightClicked?.Invoke(index, e.Location);
            return;
        }

        QueueItemClicked?.Invoke(index);
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
        if (dragging)
        {
            var width = Clamp(e.X - padding - gap / 2);
            if (width != queueWidth)
            {
                queueWidth = width;
                Invalidate();
            }

            return;
        }

        // Assigned only on a change: setting Cursor is a window message, and this runs on every
        // pixel the mouse moves over the canvas.
        var wanted = OverSplitter(e.X) ? Cursors.VSplit : Cursors.Default;
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
        if (dragging)
        {
            dragging = false;
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

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var notches = e.Delta / SystemInformation.MouseWheelScrollDelta;
        if (notches != 0)
        {
            Scrolled?.Invoke(notches);
        }
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
