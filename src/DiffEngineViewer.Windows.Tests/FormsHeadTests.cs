using System.Drawing.Text;

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
        using var host = new CanvasHost(2000, 700);
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
    /// Ten image pairs drawn one after another, each accepted (received moved over
    /// verified) before the next, and then a screen with no picture on it. Nothing needs more than
    /// the two on screen.
    /// </summary>
    [Test]
    public async Task EveryPictureEverDrawnStaysDecoded()
    {
        var directory = TempDirectory("deview-review-cache");
        using var host = new CanvasHost();
        for (var index = 0; index < 10; index++)
        {
            var received = Path.Combine(directory, $"Test{index}.received.png");
            var verified = Path.Combine(directory, $"Test{index}.verified.png");
            File.WriteAllBytes(received, SamplePng.Build(400, 300, 200, 40, 40));
            File.WriteAllBytes(verified, SamplePng.Build(400, 300, 40, 40, 200));
            var entry = QueueEntry.ForFiles(received, verified, FileSide.Read(received), FileSide.Read(verified));
            var state = ViewerSession.Resize(
                ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File, columns, rows), entry),
                columns,
                rows);
            host.Draw(ScreenBuilder.Build(state));
            File.Move(received, verified, true);
        }

        host.Draw(ScreenBuilder.Build(ViewerSession.Resize(Fixtures.File(), columns, rows)));
        var (count, bytes) = host.Canvas.CachedImages();
        Directory.Delete(directory, true);
        Console.WriteLine($"{count} decoded pictures held, {bytes / 1024} KB of pixels, on a screen showing none");
        await Assert.That(count).IsLessThanOrEqualTo(2);
    }

    /// <summary>
    /// The default 1100 by 700 window at the common scales, through the canvas's own layout
    /// code with the cell MonoFont measures at that scale and the chrome the form takes there: the
    /// scrollbar's system width and the footer's LogicalToDeviceUnits(40). The window itself stays
    /// 1100 by 700 device pixels, which is what ViewerForm's constructor asks for.
    /// </summary>
    [Test]
    public async Task TheFirstWindowIsSizedForTheDisplay()
    {
        await Assert.That(ViewerForm.InitialClientSize(new(1100, 700), 96, new(1920, 1040))).IsEqualTo(new Size(1100, 700));
        await Assert.That(ViewerForm.InitialClientSize(new(1100, 700), 192, new(3840, 2100))).IsEqualTo(new Size(2200, 1400));
        // 1050 tall at 150% does not fit a 1080p working area, and is kept inside it
        await Assert.That(ViewerForm.InitialClientSize(new(1100, 700), 144, new(1920, 1040))).IsEqualTo(new Size(1650, 936));
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
        var (left, half, width) = canvas.Panes();
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
    public async Task DraggingTheThumbStillRunsFrames()
    {
        var keys = new byte[256];
        GetKeyboardState(keys);
        var saved = (byte[]) keys.Clone();
        keys[1] = 0x80;
        SetKeyboardState(keys);
        try
        {
            await DragTheThumb();
        }
        finally
        {
            SetKeyboardState(saved);
        }
    }

    static async Task DragTheThumb()
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
        var y = (info.ThumbTop + info.ThumbBottom) / 2;

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
            PostMessage(handle, mouseMove, leftButtonFlag, Point(x, y + 80));
            Thread.Sleep(50);
            PostMessage(handle, leftButtonUp, IntPtr.Zero, Point(x, y + 80));
            // Only if the bar never let go, so a failure here cannot hang the run.
            for (var wait = 0; wait < 60 && !Volatile.Read(ref done); wait++)
            {
                Thread.Sleep(50);
            }

            if (!Volatile.Read(ref done))
            {
                PostMessage(handle, leftButtonUp, IntPtr.Zero, Point(x, y + 80));
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
            PostMessage(handle, mouseMove, leftButtonFlag, Point(x, y + 40));
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
            $"one DoEvents took {pumped}ms; scroll bar's own loop filtered {filtered} messages; " +
            $"Scroll events: {string.Join(", ", scrolls)}; frames during it: {tops.Count}, scroll tops {string.Join(" ", tops.Distinct())}");
        await Assert.That(tops.Count).IsGreaterThan(5);
        await Assert.That(tops.Max()).IsGreaterThan(0);
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

        using var other = new Form
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
        using var other = new Form
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
        OpenWindow open = (string title, int width, int height, bool hidden, out string? error) =>
        {
            var window = FormsViewerWindow.Open(title, width, height, true, out error);
            form = Field<ViewerForm>(window!, "form");
            handle = form.Handle;
            return window;
        };

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

        var code = ViewerProgram.Run(host, null, null, open);
        var runReturnedAt = clock.ElapsedMilliseconds;
        sender.Join();

        Console.WriteLine(
            $"WM_QUERYENDSESSION answered {query}; WM_ENDSESSION returned {end} at {endReturnedAt}ms with the form disposed " +
            $"{disposedAtReturn} and Run's finally started {closingAtReturn}; Run returned {code} at {runReturnedAt}ms");
        await Assert.That(query).IsEqualTo(new IntPtr(1));
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

    static string TempDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(path);
        return path;
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
        readonly Form form = new()
        {
            StartPosition = FormStartPosition.Manual,
            Location = new(-4000, -2000),
            ShowInTaskbar = false
        };

        readonly List<Bitmap> bitmaps = [];

        public ViewerCanvas Canvas { get; } = new()
        {
            Dock = DockStyle.Fill
        };

        public CanvasHost(int width = 1100, int height = 700)
        {
            form.ClientSize = new(width, height);
            form.Controls.Add(Canvas);
            form.Show();
        }

        public void Resize(int width, int height) =>
            form.ClientSize = new(width, height);

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
                ShowInTaskbar = false
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
