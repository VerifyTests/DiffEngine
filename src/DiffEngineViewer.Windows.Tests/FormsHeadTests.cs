/// <summary>
/// The WinForms head's own behaviour, through a real form and canvas: where glyphs land, how big the
/// first window is, input order, the context menu, modal loops, mouse capture and logoff.
/// <para>
/// Input is posted as real window messages and pumped through the real form, one frame at a time
/// in the order <c>ViewerProgram.Loop</c> runs them: Apply, DoEvents, Drain, then the model.
/// </para>
/// </summary>
[NotInParallel]
[TUnit.Core.Executors.STAThreadExecutor]
public class FormsHeadTests
{
    const int columns = 120;
    const int rows = 37;

    /// <summary>
    /// What <see cref="MonoFont.Cell" /> reports against where <see cref="Painter.Draw" />
    /// actually puts glyphs, at several display scales.
    /// <para>
    /// The cell is measured the way the canvas measures it: a screen Graphics with no hint set, so
    /// whatever the system default is. The glyphs are drawn the way the canvas draws them:
    /// <see cref="Painter.Prepare" /> and <see cref="Painter.Format" />. Where each glyph landed is
    /// read back from the pixels, as the centre of each of 100 drawn bars.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheAdvanceIsWhereGlyphsLand()
    {
        using var font = MonoFont.Create();
        var report = new StringBuilder();
        var worst = 0d;
        foreach (var dpi in (int[]) [96, 120, 144, 168, 192])
        {
            var (cell, advance) = Measure(font, dpi);
            var bars = DrawnBars(font, dpi, 100);
            var at99 = bars[99] - bars[0] - 99 * advance;
            worst = Math.Max(worst, Math.Abs(at99));
            report.AppendLine($"dpi {dpi}: cell {cell}, advance {advance:F3}, drawn {(bars[99] - bars[0]) / 99:F3}, drift at column 99 {at99:F2}px");
        }

        Console.WriteLine(report);
        await Assert.That(worst).IsLessThan(1d);
    }

    /// <summary>
    /// through the real canvas at the test host's scale (96): a bar at column 66 of a pane
    /// row, with columns 66 to 67 selected. The bar's ink has to sit inside the highlight.
    /// </summary>
    [Test]
    public async Task HighlightAtColumn66CoversItsGlyph()
    {
        var line = new string(' ', 66) + "|";
        var state = ViewerSession.Resize(
            ViewerSession.Drag(Fixtures.File(line, line), PaneSide.Left, 0, 66, 0, 67),
            columns,
            rows);

        // Wide enough that column 66 is on screen in a pane: at 1100 each pane holds about 52.
        using var host = new CanvasHost(2000);
        var bitmap = host.Draw(ScreenBuilder.Build(state));
        var highlight = Bounds(bitmap, _ => _.ToArgb() == Palette.Selection.ToArgb());
        await Assert.That(highlight).IsNotNull();

        // The bar is the only bright ink in the left pane's row: the gutter's line number is drawn
        // Dim, which is darker than the threshold, and the right pane's copy is far to the right.
        var band = highlight!.Value;
        var cell = host.Canvas.CellSize();
        var ink = new List<int>();
        for (var x = 0; x < band.Right + cell.Width * 10; x++)
        {
            if (bitmap.GetPixel(x, band.Top + band.Height / 2).GetBrightness() > 0.6f)
            {
                ink.Add(x);
            }
        }

        // Which column the hit test gives for a press on the bar itself.
        var hit = host.Canvas.PaneCellAt(new((ink.Min() + ink.Max()) / 2, band.Top + band.Height / 2));

        Console.WriteLine(
            $"cell {cell}, highlight x {band.Left}..{band.Right - 1}, bar ink x {ink.Min()}..{ink.Max()}, " +
            $"hit test on the bar gives column {hit?.Column}");
        await Assert.That(ink).IsNotEmpty();
        await Assert.That(ink.Min()).IsGreaterThanOrEqualTo(band.Left);
        await Assert.That(ink.Max()).IsLessThan(band.Right);
    }

    /// <summary>
    /// A left header too long for its pane ends short of where the right one starts. It was cut
    /// at the very pixel the right pane begins on, so in a narrow window the two read as one
    /// line. Each is drawn alone here, with nothing else bright on the canvas, so where its ink
    /// is can be read back.
    /// </summary>
    [Test]
    public async Task ALongLeftHeaderStopsShortOfTheRightOne()
    {
        // Full blocks, which the font draws from one edge of a cell to the other, so where a
        // header's ink starts and stops is where its cells do
        var header = new string('█', 200);
        var screen = ScreenBuilder.Build(ViewerSession.Resize(Fixtures.File(), 60, 26)) with
        {
            Title = "",
            Subtitle = ""
        };
        var none = new Pane("", [], 0, 0);
        using var host = new CanvasHost(560, 560);

        var left = Bounds(
            host.Draw(
                screen with
                {
                    Left = none with
                    {
                        Header = header
                    },
                    Right = none
                }),
            _ => _.GetBrightness() > 0.6f);
        var right = Bounds(
            host.Draw(
                screen with
                {
                    Left = none,
                    Right = none with
                    {
                        Header = header
                    }
                }),
            _ => _.GetBrightness() > 0.6f);

        await Assert.That(left).IsNotNull();
        await Assert.That(right).IsNotNull();
        Console.WriteLine($"left header's ink ends at {left!.Value.Right}, right header's starts at {right!.Value.Left}");
        // No less than the gap the canvas keeps between its columns, which is four pixels. The
        // ellipsis is in the last whole cell, so whatever part of a cell is left over is more
        await Assert.That(right.Value.Left - left.Value.Right).IsGreaterThanOrEqualTo(4);
    }

    /// <summary>
    /// A header is cut to whole cells with an ellipsis in the last of them, and one that fits is
    /// left as it is. A character two cells wide is kept whole or not at all.
    /// </summary>
    [Test]
    public async Task AHeaderTooLongForItsPaneEndsInAnEllipsis()
    {
        await Assert.That(ViewerCanvas.HeaderShown("a.txt (new)", 11)).IsEqualTo("a.txt (new)");
        await Assert.That(ViewerCanvas.HeaderShown("a.txt (new)", 10)).IsEqualTo("a.txt (ne…");
        await Assert.That(ViewerCanvas.HeaderShown("文件.txt", 6)).IsEqualTo("文件.…");
        // Three cells before the ellipsis, and the second character would be half in the fourth
        await Assert.That(ViewerCanvas.HeaderShown("文件.txt", 4)).IsEqualTo("文…");
        await Assert.That(ViewerCanvas.HeaderShown("文件.txt", 3)).IsEqualTo("文…");
        await Assert.That(ViewerCanvas.HeaderShown("a.txt", 0)).IsEqualTo("…");
    }

    /// <summary>
    /// A bar after characters the font does not draw a cell wide, selected at the column the grid
    /// puts it in: its ink has to be inside the highlight. Drawn as one string, GDI+ put the bar
    /// wherever the fallback font's widths left it - after two CJK characters about a third of a
    /// cell short of column 4, and after a combining mark a whole cell before the column the
    /// selection counted.
    /// </summary>
    [Test]
    [Arguments("中中|", 4)]
    [Arguments("é|", 1)]
    [Arguments("a한b|", 4)]
    public async Task HighlightAfterACharacterOffTheGridCoversTheNext(string line, int column)
    {
        var state = ViewerSession.Resize(
            ViewerSession.Drag(Fixtures.File(line, line), PaneSide.Left, 0, column, 0, column + 1),
            columns,
            rows);

        using var host = new CanvasHost();
        var bitmap = host.Draw(ScreenBuilder.Build(state));
        var highlight = Bounds(bitmap, _ => _.ToArgb() == Palette.Selection.ToArgb());
        await Assert.That(highlight).IsNotNull();

        var band = highlight!.Value;
        var ink = new List<int>();
        for (var x = band.Left - 2; x < band.Right + 2; x++)
        {
            if (bitmap.GetPixel(x, band.Top + band.Height / 2).GetBrightness() > 0.6f)
            {
                ink.Add(x);
            }
        }

        Console.WriteLine($"{line}: highlight x {band.Left}..{band.Right - 1}, ink {string.Join(",", ink)}");
        await Assert.That(ink).IsNotEmpty();
        // Centred, as a bar is in its own cell. Inside the highlight is not enough: drawn as one
        // string the bar after two CJK characters was still inside it, three pixels short
        var offCentre = Math.Abs((ink.Min() + ink.Max()) / 2.0 - (band.Left + band.Right - 1) / 2.0);
        await Assert.That(offCentre).IsLessThanOrEqualTo(1);
    }

    /// <summary>
    /// Rows several times longer than their pane, against a canvas wide enough to hold every one
    /// of them whole. A row is handed to GDI+ cut to the cells its pane has room for, and the cut
    /// must not show: every pixel of the narrow pane's rows is the pixel the wide canvas has
    /// there. Twenty widths a pixel apart, so the cut falls at every offset into a cell it can.
    /// </summary>
    [Test]
    public async Task ARowCutAtItsPaneIsDrawnAsTheWholeOfItIs()
    {
        var lines = LongRows();
        var text = string.Join('\n', lines);
        var screen = ScreenBuilder.Build(ViewerSession.Resize(Fixtures.File(text, text), columns, rows));

        using var whole = new CanvasHost(5600, 400);
        var reference = whole.Draw(screen);
        var cell = whole.Canvas.CellSize();
        var top = whole.Canvas.BodyTop();
        // Or the reference is cut as well, and this compares one cut with another
        var room = (whole.Canvas.Panes().Half - 8 * cell.Width) / cell.Width;
        await Assert.That(room).IsGreaterThanOrEqualTo(longRowCells);

        var wrong = new List<string>();
        for (var width = 1100; width < 1120; width++)
        {
            using var host = new CanvasHost(width, 400);
            var drawn = host.Draw(screen);
            var (left, half, _) = host.Canvas.Panes();
            // The rule between the panes is drawn over the left one's last pixels but one
            var rule = left + half - 2;
            var differing = 0;
            Point? first = null;
            for (var y = top; y < top + lines.Length * cell.Height; y++)
            {
                for (var x = left + 8 * cell.Width; x < left + half; x++)
                {
                    if (x == rule ||
                        drawn.GetPixel(x, y) == reference.GetPixel(x, y))
                    {
                        continue;
                    }

                    differing++;
                    first ??= new(x, y);
                }
            }

            if (differing > 0)
            {
                wrong.Add($"{width} wide: {differing} pixels differ, the first at {first}");
            }
        }

        await Assert.That(wrong).IsEmpty();
    }

    const int longRowCells = 300;

    /// <summary>
    /// A row of each kind of character the grid places differently, every one 300 cells or just
    /// under.
    /// </summary>
    static string[] LongRows() =>
    [
        Filling("{\"id\":1000,\"name\":\"item 1000\",\"tags\":[\"alpha\",\"beta\"],\"price\":12.5},"),
        Filling("M"),
        Filling("the quick brown fox jumps over the lazy dog "),
        // A glyph whose ink starts at its very left, once at each place in four: GDI+ fits glyphs
        // to whole pixels, which can start one in the last pixel of the cell before its own, and
        // that is why a cell past the last one showing is kept
        Filling("Wi. "),
        Filling("Wi. ", "x"),
        Filling("Wi. ", "xx"),
        Filling("Wi. ", "xxx"),
        Filling("\tcolumn"),
        Filling("Привет, мир αβγ éñü "),
        // Wide characters, each a segment of its own, and a row of nothing else, where every
        // other cut would fall inside one
        Filling("中文 and ascii "),
        Filling("中"),
        // A mark on the character before it, and a family joined into one picture. The long form
        // of each escape, since neither can be seen in a source file
        Filling("e\U00000301a\U00000308 marks "),
        Filling("\U0001F600 \U0001F468\U0000200D\U0001F469\U0000200D\U0001F467 ")
    ];

    static string Filling(string unit, string lead = "") =>
        lead + string.Concat(Enumerable.Repeat(unit, (longRowCells - lead.Length) / CellGrid.Cells(RowText.Flatten(unit))));

    /// <summary>
    /// A megabyte of one line, with one wide character half way along it. The row was segmented
    /// whole, which walked all of it, and the run before that character was copied out to be cut
    /// down to the start of it that shows: a megabyte allocated for each such row, on every paint.
    /// Counted in what a paint allocates, which is the same number on a busy machine.
    /// </summary>
    [Test]
    public async Task AMegabyteRowIsPaintedFromItsStart()
    {
        const int megabyte = 1024 * 1024;
        var line = new string('x', megabyte / 2) + "中" + new string('y', megabyte / 2);
        var screen = ScreenBuilder.Build(ViewerSession.Resize(Fixtures.File(line, line), columns, rows));
        using var host = new CanvasHost();
        // Once before it is measured: the font, the brushes, and anything compiled on first use
        host.Draw(screen);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var bitmap = host.Draw(screen);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // The row's own ink, so what was measured is a paint that drew it
        var cell = host.Canvas.CellSize();
        var textLeft = host.Canvas.Panes().Left + 8 * cell.Width;
        var inked = 0;
        for (var y = host.Canvas.BodyTop(); y < host.Canvas.BodyTop() + cell.Height; y++)
        {
            for (var x = textLeft; x < textLeft + 20 * cell.Width; x++)
            {
                if (bitmap.GetPixel(x, y).GetBrightness() > 0.6f)
                {
                    inked++;
                }
            }
        }

        Console.WriteLine($"{allocated / 1024} KB allocated by a paint of two megabyte rows, {inked} bright pixels in the first");
        await Assert.That(inked).IsGreaterThan(0);
        await Assert.That(allocated).IsLessThan(megabyte / 8);
    }

    /// <summary>
    /// Ten image pairs drawn one after another, each accepted (received moved over
    /// verified) before the next, and then a screen with no picture on it. Nothing needs more than
    /// the two on screen.
    /// </summary>
    [Test]
    public async Task EveryPictureEverDrawnStaysDecoded()
    {
        // A folder of this test's own: under one fixed name, two runs on a machine at once moved
        // and deleted each other's pictures
        var directory = Directory.CreateTempSubdirectory("deview-review-cache-").FullName;
        try
        {
            using var host = new CanvasHost();
            for (var index = 0; index < 10; index++)
            {
                var received = Path.Combine(directory, $"Test{index}.received.png");
                var verified = Path.Combine(directory, $"Test{index}.verified.png");
                await File.WriteAllBytesAsync(received, SamplePng.Build(400, 300, 200, 40, 40));
                await File.WriteAllBytesAsync(verified, SamplePng.Build(400, 300, 40, 40, 200));
                var entry = QueueEntry.ForFiles(received, verified, FileSide.Read(received), FileSide.Read(verified));
                var state = ViewerSession.Resize(
                    ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File, columns, rows), entry),
                    columns,
                    rows);
                host.Draw(ScreenBuilder.Build(state));
                ViewerActions.Real.MoveFile(received, verified);
            }

            host.Draw(ScreenBuilder.Build(ViewerSession.Resize(Fixtures.File(), columns, rows)));
            var (count, bytes) = host.Canvas.CachedImages();
            Console.WriteLine($"{count} decoded pictures held, {bytes / 1024} KB of pixels, on a screen showing none");
            await Assert.That(count).IsLessThanOrEqualTo(2);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Through the real canvas: a pair of pictures painted twice at one size is composed once, and
    /// the second paint copies it. Both paints show the pictures.
    /// </summary>
    [Test]
    public async Task RepaintingAPictureComposesItOnce()
    {
        using var host = new CanvasHost();
        var screen = ScreenBuilder.Build(ViewerSession.Resize(Fixtures.Images(), columns, rows));
        host.Canvas.Synchronous = true;

        var first = host.Draw(screen);
        var second = host.Draw(screen);

        await Assert.That(host.Canvas.Composed()).IsEqualTo(2);
        // The left picture's colour, which only a drawn picture puts on the canvas
        var red = Bounds(second, _ => _ is {R: 198, G: 64, B: 64});
        await Assert.That(red).IsNotNull();
        await Assert.That(Bounds(first, _ => _ is {R: 198, G: 64, B: 64})).IsEqualTo(red);
    }

    /// <summary>
    /// A picture no larger than one square of the checkerboard behind it has no dark square under
    /// it at all. The dark squares are handed to GDI+ as one list, and it takes a list of nothing
    /// as a mistake, which would fail the compose and leave an icon of eight pixels drawn as
    /// nothing. Composed on the pool, as the window does, where a compose that fails is one that
    /// never lands rather than one that throws out of a paint.
    /// </summary>
    [Test]
    public async Task APictureNoLargerThanOneSquareOfTheCheckerboardIsDrawn()
    {
        var directory = Directory.CreateTempSubdirectory("deview-small-picture-").FullName;
        try
        {
            var received = Path.Combine(directory, "icon.received.png");
            var verified = Path.Combine(directory, "icon.verified.png");
            await File.WriteAllBytesAsync(received, SamplePng.Build(8, 6, 198, 64, 64));
            await File.WriteAllBytesAsync(verified, SamplePng.Build(8, 6, 64, 150, 198));
            var entry = QueueEntry.ForFiles(received, verified, FileSide.Read(received), FileSide.Read(verified));
            var screen = ScreenBuilder.Build(
                ViewerSession.Resize(
                    ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File, columns, rows), entry),
                    columns,
                    rows));
            using var host = new CanvasHost();

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (host.Canvas.Composed() < 2 &&
                   DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                host.Draw(screen);
                Thread.Sleep(10);
            }

            var drawn = host.Draw(screen);

            await Assert.That(Bounds(drawn, _ => _ is {R: 198, G: 64, B: 64})).IsNotNull();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// One picture on both sides, which a page that two identical documents share is, on a canvas
    /// an odd number of pixels wide. The right pane is then a pixel wider than the left, and a
    /// picture fitted to each was asked for at two sizes. The cache keeps one composite per
    /// picture, so every paint composed it twice over, each landing throwing the other away, for
    /// as long as the entry was on screen.
    /// </summary>
    [Test]
    [Arguments(1100)]
    [Arguments(1101)]
    public async Task OnePictureOnBothSidesIsComposedOnce(int width)
    {
        var directory = Directory.CreateTempSubdirectory("deview-one-picture-").FullName;
        try
        {
            // Wide, so it is the pane's width that it is fitted to
            var path = Path.Combine(directory, "page.png");
            await File.WriteAllBytesAsync(path, SamplePng.Build(2000, 500, 198, 64, 64));
            var entry = QueueEntry.ForFiles(path, path, FileSide.Read(path), FileSide.Read(path));
            var screen = ScreenBuilder.Build(
                ViewerSession.Resize(
                    ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File, columns, rows), entry),
                    columns,
                    rows));
            using var host = new CanvasHost(width);
            host.Canvas.Synchronous = true;

            host.Draw(screen);
            host.Draw(screen);

            await Assert.That(host.Canvas.Composed()).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Through the real canvas, loading its pictures the way the window does, on the pool: a
    /// spinner where each picture goes until it lands, and the pictures once they have, with nothing
    /// left turning.
    /// </summary>
    [Test]
    public async Task APictureOnItsWayIsASpinnerUntilItLands()
    {
        using var host = new CanvasHost();
        var screen = ScreenBuilder.Build(ViewerSession.Resize(Fixtures.Images(), columns, rows));

        var waiting = host.Draw(screen);
        await Assert.That(host.Canvas.Spinners.Count).IsEqualTo(2);
        await Assert.That(Bounds(waiting, _ => _ is {R: 198, G: 64, B: 64})).IsNull();

        // Decoded, then composed, each landing through the message loop
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (host.Canvas.Composed() < 2 &&
               DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            host.Draw(screen);
            Thread.Sleep(10);
        }

        var landed = host.Draw(screen);
        await Assert.That(host.Canvas.Spinners).IsEmpty();
        await Assert.That(Bounds(landed, _ => _ is {R: 198, G: 64, B: 64})).IsNotNull();
    }

    /// <summary>
    /// A document's page still being drawn has no path or size yet, and a spinner stands where it
    /// will go.
    /// </summary>
    [Test]
    public async Task APageStillBeingDrawnIsASpinner()
    {
        using var host = new CanvasHost();
        host.Canvas.Synchronous = true;
        host.Draw(ScreenBuilder.Build(ViewerSession.Resize(Fixtures.DocumentDrawing(), columns, rows)));
        await Assert.That(host.Canvas.Spinners.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The footer's buttons are pooled and relabelled as the screen changes, and each is sized to
    /// the label it has now. At WinForms' default, GrowOnly, a button stayed as wide as the longest
    /// label it had ever held, so the footer was the history of the session - and the pixel
    /// baselines the history of the test run, which moved whenever a test was added.
    /// </summary>
    [Test]
    public async Task AFooterButtonIsSizedToItsCurrentLabel()
    {
        using var host = new FormHost(Fixtures.File(Fixtures.Long(true), Fixtures.Long(false)));
        host.Frame();
        var button = FooterButtons(host.Form)[4];
        var before = (button.Text, button.Width);

        host.Form.Apply(ScreenBuilder.Build(ViewerSession.Apply(host.State, CommandKind.ToggleMinimal)));
        var after = (button.Text, button.Width);

        Console.WriteLine($"{before} then {after}");
        await Assert.That(before.Text).IsEqualTo("Changes only");
        await Assert.That(after.Text).IsEqualTo("All lines");
        await Assert.That(after.Width).IsLessThan(before.Width);
        await Assert.That(after.Width).IsGreaterThanOrEqualTo(button.MinimumSize.Width);
    }

    /// <summary>
    /// A document's ten buttons in a window too narrow for one row of them: every one of them is
    /// inside the footer, on more rows than one. In one row, those past the window's edge could
    /// not be reached at all.
    /// </summary>
    [Test]
    public async Task ButtonsThatDoNotFitOneRowWrap()
    {
        using var host = new FormHost(Fixtures.Document());
        host.Settle();
        var footer = Field<Panel>(host.Form, "footer");
        var oneRow = footer.Height;

        host.Form.ClientSize = new(560, 700);
        host.Settle();

        var buttons = FooterButtons(host.Form);
        await Assert.That(buttons.Count).IsEqualTo(10);
        await Assert.That(buttons.Select(_ => _.Top).Distinct().Count()).IsEqualTo(2);
        await Assert.That(buttons.All(_ => footer.ClientRectangle.Contains(_.Bounds))).IsTrue();
        await Assert.That(footer.Height).IsGreaterThan(oneRow);
    }

    /// <summary>
    /// What the footer takes comes off the canvas, which says so: the model is asked for the
    /// rows the canvas has left, so none is drawn behind a footer that grew.
    /// </summary>
    [Test]
    public async Task ATallerFooterIsRowsTheBodyIsNotAskedFor()
    {
        using var host = new FormHost(Fixtures.File(Lines(300, 3), Lines(300)));
        host.Settle();
        var footer = Field<Panel>(host.Form, "footer");
        var before = (Rows: host.State.Rows, Footer: footer.Height, Canvas: host.Canvas.Height);

        // A file's five buttons are on two rows at this width
        host.Form.ClientSize = new(300, host.Form.ClientSize.Height);
        host.Settle();

        var grew = footer.Height - before.Footer;
        var cell = host.Canvas.CellSize();
        await Assert.That(grew).IsGreaterThan(cell.Height);
        await Assert.That(host.Canvas.Height).IsEqualTo(before.Canvas - grew);
        await Assert.That(host.State.Rows).IsEqualTo(host.Canvas.BodyCapacity + ScreenBuilder.Chrome);
        await Assert.That(host.State.Rows).IsLessThan(before.Rows);
        // The last row the model sliced ends inside the canvas
        var rows = ScreenBuilder.Build(host.State).Left.Rows.Count;
        await Assert.That(host.Canvas.BodyTop() + rows * cell.Height).IsLessThanOrEqualTo(host.Canvas.Height);
    }

    /// <summary>
    /// A status with no room beside the buttons goes under them, and the label is as tall as the
    /// lines it wraps to: nothing of it is cut. Then one that fits beside them is beside them
    /// again, in a footer the height it always was.
    /// </summary>
    [Test]
    public async Task AStatusWithNoRoomBesideTheButtonsHasLinesOfItsOwn()
    {
        using var form = new ViewerForm("title", 700, 600);
        var screen = ScreenBuilder.Build(ViewerSession.Resize(Fixtures.Document(), columns, rows));
        var footer = Field<Panel>(form, "footer");
        var status = Field<Label>(form, "status");
        form.Apply(screen);
        var oneRow = footer.Height;

        form.Apply(
            screen with
            {
                Status = LongStatus
            });

        var buttons = FooterButtons(form);
        await Assert.That(status.Top).IsGreaterThanOrEqualTo(buttons.Max(_ => _.Bottom));
        await Assert.That(status.Width).IsEqualTo(footer.ClientSize.Width - footer.Padding.Horizontal);
        var needed = status.GetPreferredSize(new(status.Width, 0)).Height;
        var line = status.GetPreferredSize(Size.Empty).Height;
        await Assert.That(needed).IsGreaterThan(line);
        await Assert.That(status.Height).IsEqualTo(needed);
        await Assert.That(status.TextAlign).IsEqualTo(ContentAlignment.TopLeft);
        await Assert.That(status.Bottom).IsLessThanOrEqualTo(footer.ClientSize.Height);

        form.Apply(
            screen with
            {
                Status = "page 1"
            });

        await Assert.That(footer.Height).IsEqualTo(oneRow);
        // Beside the last row of them, which at this width is the second
        var last = buttons.Where(_ => _.Top == buttons.Max(button => button.Top)).ToList();
        await Assert.That(status.Top).IsEqualTo(last[0].Top);
        await Assert.That(status.Left).IsGreaterThan(last.Max(_ => _.Right));
    }

    const string LongStatus =
        "START lines 1-14 of 40, page 1 of 1, page 1 differs, 200% zoom, selected 3 lines of sample.received.pdf, " +
        "which is 112 characters, and could not be drawn: Not a readable PDF document: it was cut short END";

    /// <summary>
    /// The label is as tall as its lines at the font it has, which on a scaled display is a
    /// larger one, and they start at its top. Two lines high whatever it held and centred, more
    /// lines than that showed the middle of the status. A display cannot be scaled from a test,
    /// so the font is.
    /// </summary>
    [Test]
    public async Task TheStatusIsAsTallAsItsLinesAtALargerFont()
    {
        using var form = new ViewerForm("title", 700, 600);
        var screen = ScreenBuilder.Build(ViewerSession.Resize(Fixtures.Document(), columns, rows));
        var status = Field<Label>(form, "status");
        form.Apply(
            screen with
            {
                Status = LongStatus
            });
        var small = status.Height;

        using var larger = new Font(status.Font.FontFamily, status.Font.Size * 1.5f);
        status.Font = larger;
        form.Apply(
            screen with
            {
                Status = LongStatus + "."
            });

        await Assert.That(status.Height).IsGreaterThan(small);
        // All of it, or the three lines it is given at most, from the first of them
        var line = status.GetPreferredSize(Size.Empty).Height;
        var needed = status.GetPreferredSize(new(status.Width, 0)).Height;
        await Assert.That(status.Height).IsEqualTo(Math.Min(needed, line * 3));
        await Assert.That(status.TextAlign).IsEqualTo(ContentAlignment.TopLeft);
    }

    /// <summary>
    /// A status under the buttons goes back beside them only with room to spare. What it says can
    /// turn on how many rows the body has, and the body has a row more with the status beside
    /// the buttons: one that fitted by a character there and not under them changed places on
    /// every frame.
    /// </summary>
    [Test]
    public async Task AStatusUnderTheButtonsIsSlowToGoBackBesideThem()
    {
        using var form = new ViewerForm("title", 700, 600);
        var screen = ScreenBuilder.Build(ViewerSession.Resize(Fixtures.File(), columns, rows));
        var status = Field<Label>(form, "status");
        bool Below(int length)
        {
            form.Apply(
                screen with
                {
                    Status = new('x', length)
                });
            return status.Top > FooterButtons(form).Max(_ => _.Top);
        }

        // The first length with no room beside the buttons
        var length = 1;
        while (!Below(length))
        {
            length++;
        }

        await Assert.That(length).IsGreaterThan(10);
        // A character shorter fitted on the way up, and does not bring it back
        await Assert.That(Below(length - 1)).IsTrue();
        await Assert.That(Below(length - 10)).IsFalse();
        // And from beside them it stays there until it does not fit
        await Assert.That(Below(length - 1)).IsFalse();
    }

    static List<System.Windows.Forms.Button> FooterButtons(ViewerForm form) =>
        ((IList) typeof(ViewerForm).GetField("pool", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!)
        .Cast<System.Windows.Forms.Button>()
        .ToList();

    /// <summary>
    /// The default 1100 by 700 window at the common scales, through the canvas's own layout
    /// code with the cell MonoFont measures at that scale and the chrome the form takes there: the
    /// scrollbar's system width and the footer's LogicalToDeviceUnits(40). The window itself stays
    /// 1100 by 700 device pixels, which is what ViewerForm's constructor asks for.
    /// </summary>
    [Test]
    public async Task TheFirstWindowIsSizedForTheDisplay()
    {
        await Assert.That(ViewerForm.InitialClientSize(new(1100, 700), 96, new(1920, 1040))).IsEqualTo(new(1100, 700));
        await Assert.That(ViewerForm.InitialClientSize(new(1100, 700), 192, new(3840, 2100))).IsEqualTo(new(2200, 1400));
        // 1050 tall at 150% does not fit a 1080p working area, and is kept inside it
        await Assert.That(ViewerForm.InitialClientSize(new(1100, 700), 144, new(1920, 1040))).IsEqualTo(new(1650, 936));
    }

    /// <summary>
    /// The first window at the common scales, through the canvas's own layout code with the cell
    /// MonoFont measures at that scale and the chrome the form takes there. At 1100 by 700 device
    /// pixels a pane held 4 characters at 200%.
    /// </summary>
    [Test]
    public async Task WhatTheFirstWindowHoldsAtEachScale()
    {
        using var font = MonoFont.Create();
        using var host = new CanvasHost();
        var screen = ScreenBuilder.Build(ViewerSession.Resize(Fixtures.Inline(Fixtures.Patch()), columns, rows));
        var report = new StringBuilder();
        var fewest = int.MaxValue;
        foreach (var dpi in (int[]) [96, 120, 144, 192])
        {
            using var bitmap = new Bitmap(1, 1);
            bitmap.SetResolution(dpi, dpi);
            using var graphics = Graphics.FromImage(bitmap);
            var cell = MonoFont.Cell(graphics, font);
            // Straight from user32: SystemInformation answers 17 at every DPI in an unaware host.
            var bar = GetSystemMetricsForDpi(verticalScrollBarWidth, (uint) dpi);
            var footer = 40 * dpi / 96;
            var window = ViewerForm.InitialClientSize(new(1100, 700), dpi, new(3840, 2100));
            host.Resize(window.Width - bar, window.Height - footer);
            typeof(ViewerCanvas).GetField("cell", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host.Canvas, cell);
            host.Canvas.Draw(screen);
            var (_, half, _) = host.Canvas.Panes();
            var characters = (half - 8 * cell.Width) / cell.Width;
            fewest = Math.Min(fewest, characters);
            report.AppendLine($"dpi {dpi}: cell {cell}, window {window}, {Layout(host.Canvas)}");
        }

        Console.WriteLine(report);
        await Assert.That(fewest).IsGreaterThanOrEqualTo(30);
    }

    static string Layout(ViewerCanvas canvas)
    {
        var cell = canvas.CellSize();
        var (left, half, _) = canvas.Panes();
        var text = half - 8 * cell.Width;
        return $"canvas {canvas.Size}, cell {cell}, queue column {left} px, pane {half} px, text {text} px = {text / cell.Width} characters a pane, {canvas.BodyCapacity} body rows";
    }

    /// <summary>
    /// Two presses of Down before the pump runs: both are real key messages, and both reach the
    /// model, a frame each. One slot per kind of input kept only the last.
    /// </summary>
    [Test]
    public async Task TwoKeysInOnePumpAreTwoCommands()
    {
        using var host = new FormHost(Fixtures.File(Lines(300, 3), Lines(300)));
        host.Settle();
        var before = ScreenBuilder.Build(host.State).Left.ScrollTop;

        host.PostKey(Keys.Down);
        host.PostKey(Keys.Down);
        var first = host.Frame();
        // The second is waiting for the next frame, which does not wait for more input first
        await Assert.That(host.Form.Pending).IsTrue();
        var second = host.Frame();

        var after = ScreenBuilder.Build(host.State).Left.ScrollTop;
        Console.WriteLine($"drained {first.Key} then {second.Key}; scroll top {before} -> {after}");
        await Assert.That(after - before).IsEqualTo(2);
    }

    /// <summary>
    /// A key posted by one of these tests is that key, on a thread that takes Alt to be down as
    /// much as on any other. A form reads a key message beside whatever its thread takes to be
    /// held, and Alt with Down is no command: these failed, once, on a machine somebody was
    /// using, the way this did until a frame said what was held before pumping.
    /// </summary>
    [Test]
    public async Task APostedKeyIsNoChordWhateverTheThreadTakesToBeHeld()
    {
        using var host = new FormHost(Fixtures.File(Lines(300, 3), Lines(300)));
        host.Settle();
        var before = ScreenBuilder.Build(host.State).Left.ScrollTop;

        ThreadKeys.Hold(Keys.Menu);
        host.PostKey(Keys.Down);
        host.Frame();

        await Assert.That(ScreenBuilder.Build(host.State).Left.ScrollTop - before).IsEqualTo(1);
    }

    /// <summary>
    /// The form these tests post to is never the window the keyboard goes to, and neither is the
    /// one a canvas is hosted in. Each was, shown the ordinary way: off every display and still
    /// the foreground window, so whatever was typed at the machine while a test ran was taken
    /// from what it was meant for and applied here as commands.
    /// </summary>
    [Test]
    public async Task AFormATestPostsToDoesNotTakeTheKeyboard()
    {
        using var host = new FormHost(Fixtures.File());
        host.Settle();
        using var canvas = new CanvasHost();
        Application.DoEvents();

        var foreground = GetForegroundWindow();
        await Assert.That(foreground).IsNotEqualTo(host.Form.Handle);
        await Assert.That(foreground).IsNotEqualTo(canvas.Canvas.FindForm()!.Handle);
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// d pressed while one entry is on screen, then a click on another row, both before the
    /// pump runs. In the order they happened, the first entry is discarded and the second selected.
    /// </summary>
    [Test]
    public async Task AKeyThenAClickInOnePumpAreAppliedInTheirOrder()
    {
        using var host = new FormHost(
            Fixtures.Inline(
                Fixtures.Patch("ATests.cs", 10, content: "one"),
                Fixtures.Patch("BTests.cs", 20, content: "two"),
                Fixtures.Patch("CTests.cs", 30, content: "three")));
        host.Settle();
        var onScreen = host.State.Current!.Key;
        var clicked = host.State.Queue.Last(_ => _.Key != onScreen).Key;

        host.PostKey(Keys.D);
        host.PostClick(host.QueueRowOf(clicked), right: false);
        var input = host.Frame();

        var left = host.State.Queue.Select(_ => _.Key).ToList();
        Console.WriteLine(
            $"drained Key {input.Key}, ClickedQueueItem {input.ClickedQueueItem}; on screen at the key: {onScreen}; " +
            $"clicked: {clicked}; still queued: {string.Join(" | ", left)}");
        await Assert.That(left).DoesNotContain(onScreen);
        await Assert.That(left).Contains(clicked);
    }

    /// <summary>
    /// d held down past the repeat delay, with three snapshots queued. The press discards the one
    /// on screen. The repeats would each have discarded whichever took its place, which nobody had
    /// read.
    /// </summary>
    [Test]
    public async Task AHeldDiscardIsOneDiscard()
    {
        using var host = new FormHost(
            Fixtures.Inline(
                Fixtures.Patch("ATests.cs", 10, content: "one"),
                Fixtures.Patch("BTests.cs", 20, content: "two"),
                Fixtures.Patch("CTests.cs", 30, content: "three")));
        host.Settle();
        var onScreen = host.State.Current!.Key;

        host.PostHeld(Keys.D, repeats: 4);
        for (var frame = 0; frame < 6; frame++)
        {
            host.Frame();
        }

        var left = host.State.Queue.Select(_ => _.Key).ToList();
        await Assert.That(left.Count).IsEqualTo(2);
        await Assert.That(left).DoesNotContain(onScreen);
    }

    /// <summary>
    /// Down held: every repeat is a row, which is what holding it is for.
    /// </summary>
    [Test]
    public async Task AHeldScrollKeepsItsRepeats()
    {
        using var host = new FormHost(Fixtures.File(Lines(300, 3), Lines(300)));
        host.Settle();
        var before = ScreenBuilder.Build(host.State).Left.ScrollTop;

        host.PostHeld(Keys.Down, repeats: 4);
        for (var frame = 0; frame < 6; frame++)
        {
            host.Frame();
        }

        await Assert.That(ScreenBuilder.Build(host.State).Left.ScrollTop - before).IsEqualTo(5);
    }

    /// <summary>
    /// Right click a row, which opens its menu, then right click the same row again.
    /// </summary>
    [Test]
    public async Task RightClickingTheRowWhoseMenuIsOpen()
    {
        using var host = new FormHost(
            Fixtures.Inline(
                Fixtures.Patch("ATests.cs", 10, content: "one"),
                Fixtures.Patch("BTests.cs", 20, content: "two")));
        host.Settle();
        var row = host.QueueRowOf(host.State.Current!.Key);
        var popup = Field<ContextMenuStrip>(host.Form, "contextMenu");

        host.PostClick(row, right: true);
        host.Frame();
        host.Frame();
        var opened = $"first: model menu {host.State.Menu is not null}, popup {popup.Visible}";

        host.PostClick(row, right: true);
        host.Settle();
        var reopened = $"second: model menu {host.State.Menu is not null}, popup {popup.Visible}";

        host.PostClick(row, right: true);
        host.Settle();
        Console.WriteLine($"{opened}\n{reopened}\nthird: model menu {host.State.Menu is not null}, popup {popup.Visible}");
        await Assert.That(popup.Visible).IsEqualTo(host.State.Menu is not null);
    }

    /// <summary>
    /// Press on the scrollbar thumb, drag, hold, drag again, release half a second later: real mouse
    /// messages, posted to the bar. The one DoEvents that dispatches the press does not return until
    /// the release, because the bar tracks the thumb in a modal loop of its own - so the frames the
    /// panes follow the thumb by have to come from inside it.
    /// <para>
    /// A posted press alone is not enough: the bar looks at the button state and, finding it up,
    /// ends the track at once. So this thread's own key state says the left button is down for the
    /// length of the drag, which is what it says during a real one. Nothing outside this thread
    /// sees that, and it is put back after.
    /// </para>
    /// </summary>
    [Test]
    public Task DraggingTheThumbStillRunsFrames() =>
        WithTheLeftButtonHeld(() => TrackTheBar(BarPart.Thumb));

    /// <summary>
    /// The arrow at the foot of the bar, pressed and held for half a second. user32 tracks that in
    /// the loop it tracks the thumb in, sending a line down and then one for every repeat, and the
    /// panes follow only if frames come from inside it. They were entered for the thumb alone, so
    /// the panes stood still for as long as the arrow was held and jumped when it was let go.
    /// </summary>
    [Test]
    public Task HoldingTheArrowStillRunsFrames() =>
        WithTheLeftButtonHeld(() => TrackTheBar(BarPart.Arrow));

    /// <summary>
    /// The trough under the thumb, held: a page down and then one for every repeat, from the same
    /// loop.
    /// </summary>
    [Test]
    public Task HoldingTheTroughStillRunsFrames() =>
        WithTheLeftButtonHeld(() => TrackTheBar(BarPart.Trough));

    /// <summary>
    /// A line down sent to the bar with no press behind it, and so with no EndScroll after it:
    /// nothing is tracking, and the frames started for a loop that never was stop when the real
    /// loop next presents.
    /// </summary>
    [Test]
    public async Task AScrollWithNoEndStopsItsFramesAtTheNextPresent()
    {
        using var form = new ViewerForm("title", 800, 600)
        {
            Frame = () => ScreenBuilder.Build(Fixtures.File())
        };
        var bar = Field<VScrollBar>(form, "scrollBar");
        var frames = Field<System.Windows.Forms.Timer>(form, "modalFrames");

        typeof(ScrollBar)
            .GetMethod("OnScroll", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, [new ScrollEventArgs(ScrollEventType.SmallIncrement, 1)]);
        var entered = frames.Enabled;
        form.LoopReturned();

        await Assert.That(entered).IsTrue();
        await Assert.That(frames.Enabled).IsFalse();
    }

    enum BarPart
    {
        Thumb,
        Arrow,
        Trough
    }

    static async Task WithTheLeftButtonHeld(Func<Task> track)
    {
        var keys = new byte[256];
        GetKeyboardState(keys);
        var saved = (byte[]) keys.Clone();
        keys[1] = 0x80;
        SetKeyboardState(keys);
        try
        {
            await track();
        }
        finally
        {
            SetKeyboardState(saved);
        }
    }

    /// <summary>
    /// The bar counts the rows the pane shows, which for a document with its page under its text is
    /// the top half of the body, so the thumb and the keyboard agree on where the text ends.
    /// </summary>
    [Test]
    public async Task TheBarCountsTheRowsThePaneShows()
    {
        var entry = QueueEntry.ForFiles(
            "temp/sample.received.pdf",
            "code/sample.verified.pdf",
            new(Lines(400), null, null, null, new DocumentFile("temp/sample.received.pdf", 10, DocumentFormat.Pdf, "AA")),
            new(Lines(400, 3), null, null, null, new DocumentFile("code/sample.verified.pdf", 11, DocumentFormat.Pdf, "BB")));
        using var host = new FormHost(ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File), entry));
        host.Settle();

        var bar = Field<VScrollBar>(host.Form, "scrollBar");
        await Assert.That(ScreenBuilder.PaneRows(host.State)).IsEqualTo(ScreenBuilder.BodyRows(host.State) / 2);
        await Assert.That(bar.LargeChange).IsEqualTo(ScreenBuilder.PaneRows(host.State));
    }

    static async Task TrackTheBar(BarPart part)
    {
        using var host = new FormHost(Fixtures.File(Lines(400, 3), Lines(400)));
        host.Settle();
        var bar = Field<VScrollBar>(host.Form, "scrollBar");
        var info = new ScrollBarInfo
        {
            Size = Marshal.SizeOf<ScrollBarInfo>()
        };
        GetScrollBarInfo(bar.Handle, objectClient, ref info);
        var x = bar.Width / 2;
        // The arrow is a square at the foot of the bar, and the trough is what lies between the
        // thumb and it
        var arrowTop = bar.Height - info.LineButton;
        var y = part switch
        {
            BarPart.Thumb => (info.ThumbTop + info.ThumbBottom) / 2,
            BarPart.Arrow => arrowTop + info.LineButton / 2,
            _ => (info.ThumbBottom + arrowTop) / 2
        };
        // Only the thumb is dragged. An arrow or the trough is held where it was pressed.
        var (dragged, released) = part == BarPart.Thumb ? (y + 40, y + 80) : (y, y);

        var clock = Stopwatch.StartNew();
        var scrolls = new List<string>();
        bar.Scroll += (_, e) => scrolls.Add($"{e.Type} {e.NewValue} at {clock.ElapsedMilliseconds}ms");
        var filtered = 0;
        HookProc filter = (code, wParam, lParam) =>
        {
            if (code == messageFilterScrollBar)
            {
                filtered++;
            }

            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        };
        var hook = SetWindowsHookEx(messageFilterHook, filter, IntPtr.Zero, GetCurrentThreadId());
        var tops = new List<int>();
        host.Form.Frame = () =>
        {
            host.Apply(host.Form.Drain());
            var screen = ScreenBuilder.Build(host.State);
            tops.Add(screen.Left.ScrollTop);
            return screen;
        };
        var handle = bar.Handle;
        var done = false;
        var release = new Thread(() =>
        {
            Thread.Sleep(500);
            PostMessage(handle, mouseMove, leftButtonFlag, Point(x, released));
            Thread.Sleep(50);
            PostMessage(handle, leftButtonUp, IntPtr.Zero, Point(x, released));
            // Only if the bar never let go, so a failure here cannot hang the run.
            for (var wait = 0; wait < 60 && !Volatile.Read(ref done); wait++)
            {
                Thread.Sleep(50);
            }

            if (!Volatile.Read(ref done))
            {
                PostMessage(handle, leftButtonUp, IntPtr.Zero, Point(x, released));
                PostMessage(handle, cancelMode, IntPtr.Zero, IntPtr.Zero);
            }
        })
        {
            IsBackground = true
        };

        long pumped;
        try
        {
            PostMessage(handle, leftButtonDown, leftButtonFlag, Point(x, y));
            PostMessage(handle, mouseMove, leftButtonFlag, Point(x, dragged));
            release.Start();
            var pump = Stopwatch.StartNew();
            Application.DoEvents();
            pumped = pump.ElapsedMilliseconds;
        }
        finally
        {
            Volatile.Write(ref done, true);
            release.Join();
            UnhookWindowsHookEx(hook);
            GC.KeepAlive(filter);
        }

        Console.WriteLine(
            $"{part}: one DoEvents took {pumped}ms; scroll bar's own loop filtered {filtered} messages; " +
            $"Scroll events: {string.Join(", ", scrolls)}; frames during it: {tops.Count}, scroll tops {string.Join(" ", tops.Distinct())}");
        await Assert.That(tops.Count).IsGreaterThan(5);
        await Assert.That(tops.Max()).IsGreaterThan(0);
        // Left with the loop: frames that went on coming from the timer afterwards would be a
        // second loop beside the real one
        await Assert.That(Field<System.Windows.Forms.Timer>(host.Form, "modalFrames").Enabled).IsFalse();
    }

    /// <summary>
    /// A press in a pane, then the capture goes elsewhere, as it does when another window
    /// takes the mouse, and the button comes up somewhere the canvas never hears about. Then the
    /// pointer comes back over the pane with no button down.
    /// </summary>
    [Test]
    public async Task LosingCaptureMidDragEndsTheDrag()
    {
        using var host = new CanvasHost();
        host.Draw(ScreenBuilder.Build(ViewerSession.Resize(Fixtures.File(Lines(60, 3), Lines(60)), columns, rows)));
        var canvas = host.Canvas;
        var cell = canvas.CellSize();
        var textLeft = canvas.Panes().Left + 8 * cell.Width;
        var press = new Point(textLeft + 2 * cell.Width, canvas.BodyTop() + cell.Height / 2);
        var later = new Point(textLeft + 20 * cell.Width, canvas.BodyTop() + 5 * cell.Height + cell.Height / 2);

        SendMessage(canvas.Handle, leftButtonDown, leftButtonFlag, Point(press.X, press.Y));
        var captured = canvas.Capture;

        using var other = new ParkedForm
        {
            StartPosition = FormStartPosition.Manual,
            Location = new(-4000, -2000),
            ShowInTaskbar = false
        };
        other.Show();
        other.Capture = true;
        other.Capture = false;
        var capturedAfter = canvas.Capture;

        SendMessage(canvas.Handle, mouseMove, IntPtr.Zero, Point(later.X, later.Y));
        var first = canvas.TakeDrag();
        var second = canvas.TakeDrag();
        Console.WriteLine(
            $"captured on press {captured}, after the loss {capturedAfter}; selecting still {Field<bool>(canvas, "selecting")}; " +
            $"a move with no button reported {first}; the next frame reported {second}");
        await Assert.That(second).IsNull();
    }

    /// <summary>
    /// the splitter: the same loss while dragging the rule between the queue and the panes.
    /// </summary>
    [Test]
    public async Task LosingCaptureMidSplitterDragEndsTheDrag()
    {
        using var host = new CanvasHost();
        host.Draw(ScreenBuilder.Build(ViewerSession.Resize(Fixtures.Inline(Fixtures.Patch()), columns, rows)));
        var canvas = host.Canvas;
        var splitter = canvas.Panes().Left - 2;
        var y = canvas.BodyTop() + 40;

        SendMessage(canvas.Handle, leftButtonDown, leftButtonFlag, Point(splitter, y));
        using var other = new ParkedForm
        {
            StartPosition = FormStartPosition.Manual,
            Location = new(-4000, -2000),
            ShowInTaskbar = false
        };
        other.Show();
        other.Capture = true;
        other.Capture = false;

        var before = canvas.Panes().Left;
        SendMessage(canvas.Handle, mouseMove, IntPtr.Zero, Point(splitter + 200, y));
        var after = canvas.Panes().Left;
        Console.WriteLine($"dragging still {Field<bool>(canvas, "dragging")}; queue column edge {before} -> {after} on a move with no button");
        await Assert.That(after).IsEqualTo(before);
    }

    /// <summary>
    /// The real loop, <c>ViewerProgram.Run</c>, over a real hidden window, sent the two
    /// messages a logoff sends, from another thread as the system sends them. What matters is what
    /// had happened by the time WM_ENDSESSION returned, since the session may end from then on.
    /// <see cref="SessionState.Closing" /> is the first thing Run's finally sets, before it persists.
    /// </summary>
    [Test]
    public async Task EndSessionReturnsBeforeTheLoopHasStartedToPersist()
    {
        var host = new SessionHost(Fixtures.File());
        var handle = IntPtr.Zero;
        ViewerForm? form = null;

        IViewerWindow? Open(string title, int width, int height, bool hidden, WindowPlacement? placement, out string? error)
        {
            var window = FormsViewerWindow.Open(title, width, height, true, null, out error);
            form = Field<ViewerForm>(window!, "form");
            handle = form.Handle;
            return window;
        }

        var clock = Stopwatch.StartNew();
        var query = IntPtr.Zero;
        var end = IntPtr.Zero;
        long endReturnedAt = 0;
        var disposedAtReturn = false;
        var closingAtReturn = true;
        var sender = new Thread(() =>
        {
            while (handle == IntPtr.Zero)
            {
                Thread.Sleep(10);
            }

            Thread.Sleep(200);
            query = SendMessage(handle, queryEndSession, IntPtr.Zero, endSessionLogoff);
            end = SendMessage(handle, endSession, new(1), endSessionLogoff);
            endReturnedAt = clock.ElapsedMilliseconds;
            closingAtReturn = host.State.Closing;
            disposedAtReturn = form!.IsDisposed;
        })
        {
            IsBackground = true
        };
        sender.Start();

        var code = ViewerProgram.Run(host, null, null, Open);
        var runReturnedAt = clock.ElapsedMilliseconds;
        sender.Join();

        Console.WriteLine(
            $"WM_QUERYENDSESSION answered {query}; WM_ENDSESSION returned {end} at {endReturnedAt}ms with the form disposed " +
            $"{disposedAtReturn} and Run's finally started {closingAtReturn}; Run returned {code} at {runReturnedAt}ms");
        await Assert.That(query).IsEqualTo(new(1));
        await Assert.That(closingAtReturn).IsTrue();
    }

    static string Lines(int count, int changedAt = -1)
    {
        var builder = new StringBuilder();
        for (var index = 1; index <= count; index++)
        {
            if (index > 1)
            {
                builder.Append('\n');
            }

            builder.Append($"line {index}");
            if (index == changedAt)
            {
                builder.Append(" changed");
            }
        }

        return builder.ToString();
    }

    static T Field<T>(object target, string name) =>
        (T) target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    static (int Cell, float Advance) Measure(Font font, int dpi)
    {
        if (dpi == 96)
        {
            // The host is DPI unaware, so a screen Graphics here is exactly 96.
            using var screen = Graphics.FromHwnd(IntPtr.Zero);
            return Measure(screen, font);
        }

        // Per monitor on this thread only, for long enough to get a screen Graphics at the real
        // system DPI. Other scales than the machine's come from a bitmap at that resolution.
        var previous = SetThreadDpiAwarenessContext(perMonitorV2);
        try
        {
            using var screen = Graphics.FromHwnd(IntPtr.Zero);
            if ((int) screen.DpiX == dpi)
            {
                return Measure(screen, font);
            }
        }
        finally
        {
            SetThreadDpiAwarenessContext(previous);
        }

        using var bitmap = new Bitmap(1, 1);
        bitmap.SetResolution(dpi, dpi);
        using var graphics = Graphics.FromImage(bitmap);
        return Measure(graphics, font);
    }

    static (int Cell, float Advance) Measure(Graphics graphics, Font font) =>
        (MonoFont.Cell(graphics, font).Width, MonoFont.Advance(graphics, font));

    /// <summary>
    /// The horizontal centre of each of <paramref name="count" /> bars drawn as one string.
    /// </summary>
    static double[] DrawnBars(Font font, int dpi, int count)
    {
        using var bitmap = new Bitmap(2600, 80);
        bitmap.SetResolution(dpi, dpi);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            Painter.Prepare(graphics);
            Painter.Draw(graphics, new('|', count), font, Color.Black, new(10, 10, 2580, 60));
        }

        var profile = new double[bitmap.Width];
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                profile[x] += 255 - bitmap.GetPixel(x, y).R;
            }
        }

        var centres = new List<double>();
        var start = -1;
        for (var x = 0; x <= profile.Length; x++)
        {
            var inked = x < profile.Length && profile[x] > 0;
            if (inked && start < 0)
            {
                start = x;
            }
            else if (!inked && start >= 0)
            {
                double weight = 0;
                double moment = 0;
                for (var column = start; column < x; column++)
                {
                    weight += profile[column];
                    moment += profile[column] * column;
                }

                centres.Add(moment / weight);
                start = -1;
            }
        }

        if (centres.Count != count)
        {
            throw new($"Expected {count} bars at {dpi} dpi, found {centres.Count}");
        }

        return centres.ToArray();
    }

    static Rectangle? Bounds(Bitmap bitmap, Func<Color, bool> match)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (!match(bitmap.GetPixel(x, y)))
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < 0)
        {
            return null;
        }

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    static IntPtr Point(int x, int y) =>
        new((y << 16) | (x & 0xFFFF));

    static readonly IntPtr perMonitorV2 = new(-4);
    const int keyDown = 0x0100;
    const int keyUp = 0x0101;
    const int mouseMove = 0x0200;
    const int leftButtonDown = 0x0201;
    const int leftButtonUp = 0x0202;
    const int rightButtonDown = 0x0204;
    const int rightButtonUp = 0x0205;
    const int cancelMode = 0x001F;
    const int queryEndSession = 0x0011;
    const int endSession = 0x0016;
    static readonly IntPtr endSessionLogoff = new(unchecked((int) 0x80000000));
    static readonly IntPtr leftButtonFlag = new(1);
    static readonly IntPtr rightButtonFlag = new(2);
    const int messageFilterHook = -1;
    const int messageFilterScrollBar = 5;
    const int objectClient = unchecked((int) 0xFFFFFFFC);

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct ScrollBarInfo
    {
        public int Size;
        public int BarLeft;
        public int BarTop;
        public int BarRight;
        public int BarBottom;
        public int LineButton;
        public int ThumbTop;
        public int ThumbBottom;
        public int Reserved;
        public int State0;
        public int State1;
        public int State2;
        public int State3;
        public int State4;
        public int State5;
    }

    [DllImport("user32.dll")]
    static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    const int verticalScrollBarWidth = 2;

    [DllImport("user32.dll")]
    static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    static extern bool GetKeyboardState(byte[] state);

    [DllImport("user32.dll")]
    static extern bool SetKeyboardState(byte[] state);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr SetWindowsHookEx(int hook, HookProc procedure, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    static extern bool GetScrollBarInfo(IntPtr window, int objectId, ref ScrollBarInfo info);

    /// <summary>
    /// A canvas in a real window, parked off screen and out of the taskbar, as PaneHitTests hosts
    /// one.
    /// </summary>
    sealed class CanvasHost : IDisposable
    {
        readonly Form form = new ParkedForm
        {
            StartPosition = FormStartPosition.Manual,
            Location = new(-4000, -2000),
            ShowInTaskbar = false
        };

        readonly List<Bitmap> bitmaps = [];

        /// <summary>
        /// Sized itself rather than docked to the form. A top level window is held to the screen's
        /// size, so on a 1024 by 768 build agent every canvas docked to one came out 1028 by 749,
        /// whatever was asked for. A child control has no such limit, and draws to a bitmap
        /// whole whether or not it fits the form.
        /// </summary>
        public ViewerCanvas Canvas { get; } = new();

        public CanvasHost(int width = 1100, int height = 700)
        {
            Canvas.Size = new(width, height);
            form.Controls.Add(Canvas);
            form.Show();
        }

        public void Resize(int width, int height) =>
            Canvas.Size = new(width, height);

        public Bitmap Draw(Screen screen)
        {
            Canvas.Draw(screen);
            Canvas.Refresh();
            var bitmap = new Bitmap(Canvas.Width, Canvas.Height);
            Canvas.DrawToBitmap(bitmap, new(0, 0, Canvas.Width, Canvas.Height));
            bitmaps.Add(bitmap);
            return bitmap;
        }

        public void Dispose()
        {
            foreach (var bitmap in bitmaps)
            {
                bitmap.Dispose();
            }

            form.Dispose();
        }
    }

    /// <summary>
    /// A real <see cref="ViewerForm" /> driven one frame at a time the way ViewerProgram.Loop drives
    /// it, minus the session lock: Apply the screen, DoEvents, Drain, and hand the input to the
    /// same ViewerProgram.Apply the loop calls.
    /// </summary>
    sealed class FormHost : IDisposable
    {
        readonly IViewerWindow window = new NoWindow();

        public FormHost(SessionState state)
        {
            State = state;
            Form = new("DiffEngineViewer", 1100, 700)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new(-4000, -2000),
                ShowInTaskbar = false,
                // Or it is the foreground window for as long as the test runs, and whatever is
                // typed at the machine meanwhile arrives here beside what the test posts
                Parked = true
            };
            Form.Show();
            Canvas = Field<ViewerCanvas>(Form, "canvas");
        }

        public ViewerForm Form { get; }

        public ViewerCanvas Canvas { get; }

        public SessionState State { get; private set; }

        public ViewerInput Frame()
        {
            Form.Apply(ScreenBuilder.Build(State));
            // What is posted here is a key and no chord, whatever is held at the keyboard as
            // this runs: see ThreadKeys
            ThreadKeys.ReleaseModifiers();
            Application.DoEvents();
            var input = Form.Drain();
            Apply(input);
            return input;
        }

        public void Apply(ViewerInput input)
        {
            if (!ViewerProgram.IsIdle(input, State))
            {
                State = ViewerProgram.Apply(State, input, null, window);
            }
        }

        /// <summary>
        /// Until the model holds the grid the canvas reports.
        /// </summary>
        public void Settle()
        {
            for (var index = 0; index < 3; index++)
            {
                Frame();
            }
        }

        public int QueueRowOf(string key)
        {
            var entry = -1;
            for (var index = 0; index < State.Queue.Count; index++)
            {
                if (State.Queue[index].Key == key)
                {
                    entry = index;
                }
            }

            var items = ScreenBuilder.Build(State).Queue;
            for (var row = 0; row < items.Count; row++)
            {
                if (items[row].EntryIndex == entry)
                {
                    return row;
                }
            }

            throw new($"No queue row for {key}");
        }

        public void PostKey(Keys key)
        {
            PostMessage(Canvas.Handle, keyDown, new((int) key), new(1));
            PostMessage(Canvas.Handle, keyUp, new((int) key), new(unchecked((int) 0xC0000001)));
        }

        /// <summary>
        /// A key pressed and held: the press, then what the keyboard sends for as long as it stays
        /// down, which is the same message with bit 30 saying the key was already down.
        /// </summary>
        public void PostHeld(Keys key, int repeats)
        {
            PostMessage(Canvas.Handle, keyDown, new((int) key), new(1));
            for (var index = 0; index < repeats; index++)
            {
                PostMessage(Canvas.Handle, keyDown, new((int) key), new(0x40000001));
            }

            PostMessage(Canvas.Handle, keyUp, new((int) key), new(unchecked((int) 0xC0000001)));
        }

        public void PostClick(int queueRow, bool right)
        {
            var cell = Canvas.CellSize();
            var at = Point(6 + cell.Width * 2, Canvas.BodyTop() + queueRow * cell.Height + cell.Height / 2);
            if (right)
            {
                PostMessage(Canvas.Handle, rightButtonDown, rightButtonFlag, at);
                PostMessage(Canvas.Handle, rightButtonUp, IntPtr.Zero, at);
                return;
            }

            PostMessage(Canvas.Handle, leftButtonDown, leftButtonFlag, at);
            PostMessage(Canvas.Handle, leftButtonUp, IntPtr.Zero, at);
        }

        public void Dispose() =>
            Form.Dispose();
    }

    sealed class NoWindow : IViewerWindow
    {
        public bool Present(Screen screen) => true;

        public ViewerInput Poll() => default;

        public void SetHidden(bool hidden)
        {
        }

        public void SetClipboard(string text)
        {
        }

        public void Focus()
        {
        }

        public bool Capture(Screen screen, int width, int height, string pngPath) => false;

        public void Dispose()
        {
        }
    }
}

static class CanvasReflection
{
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static Size CellSize(this ViewerCanvas canvas) =>
        (Size) typeof(ViewerCanvas).GetProperty("Cell", flags)!.GetValue(canvas)!;

    public static int BodyTop(this ViewerCanvas canvas) =>
        (int) typeof(ViewerCanvas).GetProperty("BodyTop", flags)!.GetValue(canvas)!;

    public static (int Left, int Half, int Width) Panes(this ViewerCanvas canvas) =>
        ((int, int, int)) typeof(ViewerCanvas).GetMethod("Panes", flags)!.Invoke(canvas, null)!;

    public static int Composed(this ViewerCanvas canvas) =>
        ((ImageCache) typeof(ViewerCanvas).GetField("images", flags)!.GetValue(canvas)!).Composed;

    public static (int Count, long Bytes) CachedImages(this ViewerCanvas canvas)
    {
        var cache = typeof(ViewerCanvas).GetField("images", flags)!.GetValue(canvas)!;
        var entries = (IDictionary) typeof(ImageCache).GetField("entries", flags)!.GetValue(cache)!;
        long bytes = 0;
        foreach (var entry in entries.Values)
        {
            if (entry.GetType().GetProperty("Image")!.GetValue(entry) is Image image)
            {
                bytes += (long) image.Width * image.Height * Image.GetPixelFormatSize(image.PixelFormat) / 8;
            }
        }

        return (entries.Count, bytes);
    }
}
