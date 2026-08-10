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
    const int queueWidth = 220;

    /// <summary>
    /// Marker, space, four digit line number, two spaces. Matches AsciiRenderer's gutter, so a
    /// line lands in the same column in both.
    /// </summary>
    const int gutterCells = 8;

    const int padding = 6;
    const int gap = 4;

    readonly Font font = MonoFont.Create();
    Screen? screen;

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
        var panesLeft = hasQueue ? padding + queueWidth + gap : padding;
        var panesWidth = Math.Max(2 * Cell.Width, Width - padding - panesLeft);
        var half = panesWidth / 2;

        DrawTitle(graphics, lineHeight);

        var firstRule = padding + lineHeight + gap;
        DrawRule(graphics, firstRule);

        var headerTop = firstRule + gap;
        if (hasQueue)
        {
            Painter.Draw(graphics, $"Pending ({screen.Queue.Count})", font, Palette.Text, Cellular(padding, headerTop, queueWidth, lineHeight));
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
                DrawQueueItem(graphics, index, new(padding, top, queueWidth, lineHeight));
            }

            DrawRow(graphics, screen.Left, index, new(panesLeft, top, half, lineHeight));
            DrawRow(graphics, screen.Right, index, new(panesLeft + half, top, panesWidth - half, lineHeight));
        }

        var bodyBottom = bodyTop + capacity * lineHeight;
        if (hasQueue)
        {
            DrawColumnRule(graphics, panesLeft - gap / 2, bodyTop, bodyBottom);
        }

        DrawColumnRule(graphics, panesLeft + half - gap / 2, bodyTop, bodyBottom);
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
            screen.Queue.Count == 0 ||
            e.X < padding ||
            e.X >= padding + queueWidth)
        {
            return;
        }

        var index = (e.Y - BodyTop) / Cell.Height;
        if (index >= 0 &&
            index < screen.Queue.Count)
        {
            QueueItemClicked?.Invoke(index);
        }
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
        }

        base.Dispose(disposing);
    }
}
