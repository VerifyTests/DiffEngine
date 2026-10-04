/// <summary>
/// Renders real frames through the native shim and verifies the pixels.
/// <para>
/// The shim owns one process wide window, so these run serially and share a single hidden window
/// rather than opening one per test.
/// </para>
/// <para>
/// Two sets of baselines, because the two platforms no longer share a renderer: Linux is raylib
/// and ImGui, macOS is AppKit and Core Text.
/// </para>
/// <para>
/// The Linux images come from the CI job under Xvfb with Mesa llvmpipe. Determinism there comes
/// from pinning the rasteriser rather than the platform: llvmpipe is pure software and therefore
/// more reproducible than any GPU driver, and ImGui rasterises glyphs with its own stb_truetype so
/// text is identical everywhere.
/// </para>
/// <para>
/// The macOS images come from the pinned macos-14 runner, and are the weaker guarantee of the two.
/// Core Text is the system text stack, so the capture pins everything it can reach — scale, colour
/// space, and the six font smoothing and subpixel switches — but Apple can still change glyph
/// rasterisation within a runner image. That shows up as one legible diff to re-accept, not as
/// flakiness.
/// </para>
/// <para>
/// Opting in on a developer machine will render correctly but may not match either set pixel for
/// pixel.
/// </para>
/// <para>
/// Every capture is one frame drawn in the shared window. The ImGui layout state that used to
/// carry between those frames once moved the picture placement in <see cref="Images" /> by half a
/// pixel with the test order, which itself moved with the test framework's own ordering;
/// deview_capture now draws each capture in a fresh ImGui context, so a capture is a function of
/// the screen model alone. The order is pinned as well, so what the shared window still holds —
/// the texture cache, the GL state — is inherited identically on every run.
/// </para>
/// </summary>
public class PixelTests
{
    const int width = 1100;
    const int height = 700;

    /// <summary>
    /// The grid JetBrains Mono at 15px gives at this window size. Fixed here rather than taken
    /// from the shim's own measurement, so the baselines stay pinned to one layout: these are the
    /// numbers they were captured at.
    /// </summary>
    const int columns = width / 9;

    const int rows = height / 18;

    static IViewerWindow? window;

    /// <summary>
    /// The one thread the shim is ever called on. On Linux its window has a GL context, which
    /// belongs to the thread that made it: a capture started on any other fails to create its
    /// framebuffer. Serial is not enough for that, since each test starts on whichever pool
    /// thread picks it up, and whether that was the same one each time depended on what else was
    /// running. Alone, these passed; with the rest of the suite beside them, all but the first
    /// failed.
    /// </summary>
    static readonly BlockingCollection<Action> shimWork = [];

    static Thread? shimThread;

    static Task<T> OnShimThread<T>(Func<T> job)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        shimWork.Add(
            () =>
            {
                try
                {
                    done.SetResult(job());
                }
                catch (Exception exception)
                {
                    done.SetException(exception);
                }
            });
        return done.Task;
    }

    [Before(Class)]
    public static async Task Open()
    {
        if (Environment.GetEnvironmentVariable(PixelTestAttribute.Variable) != "true")
        {
            return;
        }

        shimThread = new(
            () =>
            {
                foreach (var job in shimWork.GetConsumingEnumerable())
                {
                    job();
                }
            })
        {
            IsBackground = true,
            Name = "PixelTests shim"
        };
        shimThread.Start();

        string? error = null;
        window = await OnShimThread(() => NativeViewerWindow.Open("DiffEngineViewer", width, height, true, null, out error));
        if (window is null)
        {
            throw new(error!);
        }
    }

    [After(Class)]
    public static async Task Close()
    {
        if (shimThread is null)
        {
            return;
        }

        await OnShimThread(
            () =>
            {
                window?.Dispose();
                window = null;
                return true;
            });
        shimWork.CompleteAdding();
    }

    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 1)]
    public Task FileDiff() =>
        Capture(Fixtures.File());

    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 2)]
    public Task InlineSingle() =>
        Capture(Fixtures.Inline(Fixtures.Patch()));

    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 3)]
    public Task InlineQueue() =>
        Capture(
            Fixtures.Inline(
                Fixtures.Patch(),
                Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two"),
                Fixtures.Patch("OtherTests.cs", 12, null, "brand new")));

    /// <summary>
    /// A file name wider than the queue column, which has to stop at the divider rather than paint
    /// over the pane beside it. The WinForms head has the same case, so all three are described.
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 4)]
    public Task LongQueueLabel() =>
        Capture(
            Fixtures.Inline(
                Fixtures.Patch("HeaderPropagationExtensionsTests.cs", 130),
                Fixtures.Patch()));

    /// <summary>
    /// The new queue primitives in one frame: solution headers, a test sub-group, the conflict
    /// marker and the variant button. Mirrored in WindowsPixelTests, as ever.
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 5)]
    public Task GroupedConflictedQueue() =>
        Capture(Fixtures.GroupedConflicted());

    /// <summary>
    /// An image comparison: the pictures drawn under the rows every head draws. Mirrored in
    /// WindowsPixelTests over the same fixture, which is what holds the three heads to one
    /// placement rule — one blank line under the pane's own rows, fitted, never enlarged.
    /// <para>
    /// PNG, which every head decodes. Where they differ is which other formats they can read at
    /// all, and that difference is not visible in a picture: a format a head has no decoder for
    /// draws nothing and the rows carry the comparison, which the ASCII screens already describe.
    /// </para>
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 6)]
    public Task Images() =>
        Capture(Fixtures.Images());

    /// <summary>
    /// The context menu floated over the grouped queue, opened on the conflicted entry.
    /// <para>
    /// Linux only. That head draws the menu itself, so a capture has it; the macOS head pops a
    /// real <c>NSMenu</c>, which buys the keyboard, Escape and VoiceOver and costs this baseline —
    /// a capture makes no window, and a menu cannot be shown without one. The WinForms head made
    /// the same trade, and covers its <c>ContextMenuStrip</c> in ContextMenuTests instead. There
    /// is no equivalent here: the menu is behind the C ABI, so nothing managed can reach it.
    /// </para>
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 7)]
    [SkipOnMac("The macOS head pops a real NSMenu, which a capture has no window to show.")]
    public Task ContextMenu() =>
        Capture(ViewerSession.OpenMenu(Fixtures.GroupedConflicted(), 5));

    /// <summary>
    /// A selection dragged across three rows of the received pane. Mirrored in WindowsPixelTests
    /// over the same range, because the highlight is the one part of a selection the ASCII
    /// snapshots cannot describe - a character grid has no way to invert part of a line without
    /// changing its width - so these baselines are what hold the three heads to one appearance.
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 8)]
    public Task Selection() =>
        Capture(ViewerSession.Drag(Fixtures.File(), PaneSide.Left, 1, 6, 3, 4));

    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 9)]
    public Task InlineAccepted()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch(),
            Fixtures.Patch("OtherTests.cs", 12, null, "brand new"));
        return Capture(ViewerSession.Apply(state, CommandKind.Accept, Fixtures.Applied));
    }

    /// <summary>
    /// The minimal view, whose folded rows are drawn dimmed on a band of their own and with no
    /// line number. Mirrored in WindowsPixelTests over the same fixture.
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 10)]
    public Task Minimal() =>
        Capture(ViewerSession.Apply(Fixtures.File(Fixtures.Long(true), Fixtures.Long(false)), CommandKind.ToggleMinimal));

    /// <summary>
    /// The image comparison six steps in, eight times the size that fits, and dragged to the top
    /// right corner: each pane filled with the same part of its picture, cut off at the edges of
    /// the space under its rows. Mirrored in WindowsPixelTests over the same state, which is what
    /// holds the three heads to one enlargement and one clamp.
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 11)]
    public Task ImagesEnlarged()
    {
        var state = Fixtures.Images();
        for (var step = 0; step < 6; step++)
        {
            state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        }

        return Capture(ViewerSession.PanTo(state, 1, 0));
    }

    /// <summary>
    /// The menu a right-click on a pane opens, over the expected pane. Linux only, for the reason
    /// <see cref="ContextMenu"/> is. In a window it hangs where the pointer was; a capture was
    /// never fed one, so there it hangs from the top of the pane it is for.
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 12)]
    [SkipOnMac("The macOS head pops a real NSMenu, which a capture has no window to show.")]
    public Task PaneMenu() =>
        Capture(ViewerSession.OpenPaneMenu(Fixtures.File(), PaneSide.Right));

    /// <summary>
    /// A document's page under its text, in a window of its own. Its ten buttons leave 54 pixels
    /// beside them, and the status line - which lines, which page, which pages differ - is the
    /// one place those are said: it takes a line of its own under the buttons rather than running
    /// off the window from wherever they ended.
    /// <para>
    /// Linux only, until a baseline for the macOS head has been taken on the runner its others
    /// come from. How a footer that does not fit is laid out is each head's own, so this one says
    /// nothing about that one.
    /// </para>
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 13)]
    [SkipOnMac("There is no macOS baseline for this scene: a footer that does not fit is laid out by each head in its own way.")]
    public Task DocumentPage() =>
        Capture(Fixtures.Document());

    /// <summary>
    /// The same document pending in a queue, which is the fullest footer there is: eleven buttons
    /// that need 1199 pixels of a window with 1084. They wrap onto a second row, where the last
    /// used to be drawn past the window's edge and could not be clicked, and the status line goes
    /// beside what wrapped rather than past that. Linux only, as <see cref="DocumentPage"/> is.
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 14)]
    [SkipOnMac("There is no macOS baseline for this scene: a footer that does not fit is laid out by each head in its own way.")]
    public Task DocumentPageInQueue() =>
        Capture(Fixtures.DocumentInQueue());

    /// <summary>
    /// Text the embedded font has no glyphs for: wide characters, which the grid gives two cells,
    /// narrow ones, marks that take none, and one from outside the basic plane.
    /// <para>
    /// In its window the Linux head draws these from the machine's own fonts, and in a capture it
    /// never does, because a baseline would then be a picture of whatever a runner had installed.
    /// So this is the replacement glyph at each character's column, on a machine with every font
    /// and on one with none. It is shown in the window first, and for long enough that the fonts
    /// this machine has for it have been found and merged: that is the state a capture has to be
    /// indifferent to, and one that drew with the window's font would fail here on any machine
    /// with a font for one of these characters.
    /// </para>
    /// <para>
    /// Linux only. The macOS head draws through Core Text, in a capture as in its window, so there
    /// these characters are the runner's fonts, and a baseline of them has to come from that runner.
    /// </para>
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 15)]
    [SkipOnMac("There is no macOS baseline for this scene: that head draws these characters from the runner's own fonts, and a capture host never creates its window.")]
    public async Task OutsideTheFont()
    {
        var state = Fixtures.File(
            "plain text\n日本語 and 漢字\n한국어 텍스트\nעברית عربي ไทย น้ำ\nemoji \U0001F600 and ★\nmixed 中a文b字c",
            "plain text\n日本語 and 漢子\n한국어 텍스트\nעברית عربي ไทย น้ำ\nemoji \U0001F600 and ★\nmixed 中a文b字c");
        var screen = ScreenBuilder.Build(ViewerSession.Resize(state, columns, rows));
        await OnShimThread(
            () =>
            {
                // A second of frames. A font is asked for on the first of them, found on a thread
                // of its own, and merged at the top of the next frame after it lands
                for (var frame = 0; frame < 60; frame++)
                {
                    window!.Present(screen);
                }

                return true;
            });

        await Capture(state);
    }

    /// <summary>
    /// The context menu opened on the last row of a queue that fills its column, where hung under
    /// its row it would run off the bottom of the window, its last item with it: it goes over the
    /// row instead. Linux only, for the reason <see cref="ContextMenu"/> is.
    /// <para>
    /// At the rows the Linux head measures for a window this size, which is three more than the
    /// other scenes are pinned to: its lines are 17 pixels and not 18, and it is that grid that
    /// puts the last row 96 pixels above the window's bottom edge. The entry is a conflicted one
    /// because its menu is the longest a row has, six items and 114 pixels.
    /// </para>
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 16)]
    [SkipOnMac("The macOS head pops a real NSMenu, which a capture has no window to show.")]
    public Task ContextMenuOnTheLastRow()
    {
        const int measuredRows = height / 17;
        InlinePatch[] patches =
        [
            .. Enumerable.Range(1, 32).Select(_ => Fixtures.Patch($"Tests{_:D2}.cs", _)),
            Fixtures.Patch("Tests33.cs", 33, content: "eight", framework: "net8.0"),
            Fixtures.Patch("Tests33.cs", 33, content: "nine", framework: "net9.0")
        ];
        var state = ViewerSession.Resize(Fixtures.Inline(patches), columns, measuredRows);
        return Capture(ViewerSession.OpenMenu(state, ScreenBuilder.BodyRows(state) - 1), measuredRows);
    }

    /// <summary>
    /// Names with <c>##</c> in them, everywhere the Linux head hands a name to ImGui as an item's
    /// label: a queue row, a group's heading, the two pane headers and the items of a menu. ImGui
    /// takes everything from <c>##</c> on as the item's identity and does not draw it, so each of
    /// these stopped there. Linux only: no other head has a toolkit that reads a label that way.
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 17)]
    [SkipOnMac("There is no macOS baseline for this scene: it is about how Dear ImGui reads a label, which that head does not use.")]
    public Task NamesWithHashes()
    {
        var state = ViewerSession.EnqueueTracked(
            SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows),
            QueueEntry.ForMove(
                "move:temp/Notes##2.received.txt",
                "Notes##2 (txt)",
                null,
                "temp/Notes##2.received.txt",
                "code/Notes##2.verified.txt",
                FileSide.OfText(Fixtures.Received),
                FileSide.OfText(Fixtures.Expected)));
        state = ViewerSession.EnqueueInline(
            state,
            Fixtures.Patch(Fixtures.SolutionFile("Solution##A", "Tests", "A##Tests.cs"), 10));
        state = ViewerSession.EnqueueInline(
            state,
            Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 12));
        // The fifth row is the move, under the two solutions and their one entry each: the menu
        // names its files, and opening it selects it, which puts them in the pane headers
        return Capture(ViewerSession.OpenMenu(state, 4));
    }

    /// <summary>
    /// Two pictures fitted at a third of their size: white, with a black line one pixel wide every
    /// sixteen, across and down. Sampled between its own pixels and no others, which is all a
    /// picture near its own size needs, a line survives only where a sample lands on it, so a grid
    /// came out with some of its lines faint and some gone. The Linux head now draws a picture
    /// under half its size from reduced copies of it, in which every line is there and fainter.
    /// <para>
    /// Linux only. How a picture is reduced is each head's own toolkit's, so this one says
    /// nothing about the others.
    /// </para>
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 18)]
    [SkipOnMac("There is no macOS baseline for this scene: how a picture is reduced is each head's own.")]
    public Task ImagesReduced()
    {
        var left = WriteBitmap("grid.received.bmp", 1600, 1200, 255, 16);
        var right = WriteBitmap("grid.verified.bmp", 1200, 1600, 255, 16);
        return Capture(
            ViewerSession.EnqueueFile(
                SessionState.Start(ViewerMode.File, Fixtures.Columns, Fixtures.Rows),
                QueueEntry.ForFiles(left, right, FileSide.Read(left), FileSide.Read(right))));
    }

    /// <summary>
    /// A picture longer on one side than a texture can be, beside one that is not. It is brought
    /// down to the largest size of its own shape that a texture takes as it is read, and drawn
    /// from that, fitted as any other. It was first a black box the shape of the picture, which
    /// is what GL makes of a texture it was handed and would not take, and then nothing at all
    /// under rows that said what it was.
    /// <para>
    /// Linux only. It is past the limit where the limit is what it is under Mesa's software
    /// rasteriser, which is what these baselines are pinned to: 16384 pixels, one fewer than this
    /// picture is wide. Where a texture can be larger the picture is drawn as it is, and the
    /// scene is of the same thing by the other way.
    /// </para>
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 19)]
    [SkipOnMac("There is no macOS baseline for this scene: it is about the largest texture the Linux head's GL takes.")]
    public Task ImageTooLargeForATexture()
    {
        var left = WriteBitmap("wide.received.bmp", 16385, 512, 160, 0);
        var right = WriteBitmap("wide.verified.bmp", 160, 120, 160, 0);
        return Capture(
            ViewerSession.EnqueueFile(
                SessionState.Start(ViewerMode.File, Fixtures.Columns, Fixtures.Rows),
                QueueEntry.ForFiles(left, right, FileSide.Read(left), FileSide.Read(right))));
    }

    /// <summary>
    /// An eight bit greyscale bitmap of one shade, with a black line one pixel wide every
    /// <paramref name="spacing"/> pixels across and down when that is not zero. A bitmap because
    /// it is its header, a palette and its pixels, with nothing to compress, so its bytes are the
    /// same on every runtime: the pane prints how many there are.
    /// </summary>
    static string WriteBitmap(string name, int width, int height, byte shade, int spacing)
    {
        const int headers = 14 + 40;
        const int palette = 256 * 4;
        // Each row is padded to a multiple of four bytes
        var stride = (width + 3) / 4 * 4;
        var bytes = new byte[headers + palette + stride * height];
        bytes[0] = (byte) 'B';
        bytes[1] = (byte) 'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), headers + palette);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        // One plane, eight bits a pixel, uncompressed
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), stride * height);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(46), 256);
        for (var index = 0; index < 256; index++)
        {
            bytes.AsSpan(headers + index * 4, 3).Fill((byte) index);
        }

        for (var y = 0; y < height; y++)
        {
            // Rows are stored from the bottom one up
            var row = bytes.AsSpan(headers + palette + (height - 1 - y) * stride, width);
            row.Fill(shade);
            if (spacing == 0)
            {
                continue;
            }

            if (y % spacing == 0)
            {
                row.Clear();
                continue;
            }

            for (var x = 0; x < width; x += spacing)
            {
                row[x] = 0;
            }
        }

        // A fixed directory and a fixed name, as Fixtures.Images has and for its reason
        var directory = Path.Combine(Path.GetTempPath(), "deview-fixture-images");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// raylib does three things at the end of a frame, behind one flag: puts it on the screen,
    /// reads input, and waits for the next frame. raylib 6.0's CMake turned that flag on, so
    /// deview_present did none of them - the window stayed blank and took no keys, and the loop
    /// drew as fast as it could. A capture draws into a texture and never gets that far, so every
    /// snapshot above kept passing. The wait is the one of the three that can be timed from here,
    /// so it stands for all of them. Last in the order, so the frames it draws in the live context
    /// come after the captures rather than between two of them. <see cref="OutsideTheFont"/> is
    /// the one capture with frames of its own ahead of it, which are what it is about.
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 20)]
    [SkipOnMac("A capture host never creates the macOS window, and that head waits for the next frame in its event pump rather than after drawing one.")]
    public async Task PresentWaitsForTheNextFrame()
    {
        var screen = ScreenBuilder.Build(ViewerSession.Resize(Fixtures.File(), columns, rows));
        // So the timing starts on a frame boundary
        await Assert.That(await OnShimThread(() => window!.Present(screen))).IsTrue();

        var elapsed = await OnShimThread(
            () =>
            {
                var watch = Stopwatch.StartNew();
                for (var frame = 0; frame < 60; frame++)
                {
                    window!.Present(screen);
                }

                return watch.Elapsed;
            });

        // Sixty frames at sixty a second. Unpaced, a bare loop ran at tens of thousands a second.
        await Assert.That(elapsed).IsGreaterThan(TimeSpan.FromMilliseconds(750));
    }

    /// <summary>
    /// A window nothing is happening to is left alone: the Linux head builds no frame for it and
    /// draws nothing into it, where it used to draw the frame already there sixty times a second,
    /// which under a software rasteriser was more than half a core for a viewer left open. Nothing
    /// a capture does can tell the two apart, so this is asked of the window itself, shown for
    /// as long as the test takes: the same screen is presented a second at a time until a second
    /// goes by in which nothing was drawn.
    /// <para>
    /// A second, and not the first one. The frame after a window is shown is drawn, and the head
    /// goes on building frames for a second after anything changes before it leaves a window
    /// alone, so the first second draws and a later one does not. Which later one is not asserted:
    /// the window system may ask for the window again, and a runner may be slow. A head that has
    /// gone back to drawing every frame never has such a second.
    /// </para>
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 21)]
    [SkipOnMac("A capture host never creates the macOS window, and the counts are asked of OpenGL, which that head does not draw with.")]
    public async Task AWindowLeftAloneIsNotDrawn()
    {
        var screen = ScreenBuilder.Build(ViewerSession.Resize(Fixtures.File(), columns, rows));
        var (first, rested) = await OnShimThread(
            () =>
            {
                window!.SetHidden(false);
                try
                {
                    var first = Drawn(60, screen);
                    var watch = Stopwatch.StartNew();
                    var rested = false;
                    while (!rested &&
                           watch.Elapsed < TimeSpan.FromSeconds(30))
                    {
                        rested = Drawn(60, screen) == 0;
                    }

                    return (first, rested);
                }
                finally
                {
                    window.SetHidden(true);
                }
            });

        // Or the count is of nothing, and a second with none drawn says nothing either
        await Assert.That(first).IsGreaterThan(0);
        await Assert.That(rested).IsTrue();
    }

    /// <summary>
    /// A hidden window is handed another screen by every arrival in its queue, and the Linux head
    /// built and drew each of them into a window nobody could see. Hidden, it now draws none,
    /// however the screen changes, and the first present after it is shown draws the screen it is
    /// handed. That what it draws then is the right screen is not something a count can say: it
    /// was photographed off the X server when this was changed.
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 22)]
    [SkipOnMac("A capture host never creates the macOS window, and the counts are asked of OpenGL, which that head does not draw with.")]
    public async Task AHiddenWindowIsNotDrawn()
    {
        var screen = ScreenBuilder.Build(ViewerSession.Resize(Fixtures.File(), columns, rows));
        // The same frame twice, told apart by its status line, so that every present is of a
        // screen that is not the one before it
        Screen[] screens =
        [
            screen with {Status = "tick"},
            screen with {Status = "tock"}
        ];
        var (hidden, shown) = await OnShimThread(
            () =>
            {
                // A window's first frame is built wherever the window is, and this test may be
                // the first to present anything
                window!.Present(screen);
                var hidden = Drawn(60, screens);
                window.SetHidden(false);
                try
                {
                    return (hidden, Drawn(1, screens[0]));
                }
                finally
                {
                    window.SetHidden(true);
                }
            });

        await Assert.That(hidden).IsEqualTo(0);
        await Assert.That(shown).IsEqualTo(1);
    }

    /// <summary>
    /// A footer taller than the lines the model keeps for it takes its rows from the body: the
    /// Linux head reports fewer rows for a window whose footer is that tall, and the screen built
    /// for what it reports ends above the footer. It reported the window's height in rows whatever
    /// the footer came to, so the last rows of the body were sliced, handed over and laid out
    /// under the buttons.
    /// <para>
    /// The rows are asked of the window, shown for as long as that takes, since a capture measures
    /// nothing: first with the one row of buttons a pair of files has, which fits in what the
    /// model allows, and then with those buttons eight times over, which is five rows. That is
    /// more than any real screen has at this width, and is what a paged document has in a window
    /// under 450 pixels wide, a size the one window these tests share is never given. The picture
    /// is the second screen, built for the rows the window reported for it: forty lines a side,
    /// of which the last one sliced is the one above the footer.
    /// </para>
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 23)]
    [SkipOnMac("A capture host never creates the macOS window, and it is the window whose rows are measured.")]
    public async Task ATallFooterTakesRowsFromTheBody()
    {
        const int measuredRows = height / 17;
        var state = ViewerSession.Resize(Fixtures.File(Fixtures.Long(true), Fixtures.Long(false)), columns, measuredRows);
        var (fitting, tall) = await OnShimThread(
            () =>
            {
                window!.SetHidden(false);
                try
                {
                    return (Measured(ScreenBuilder.Build(state)), Measured(TallFooter(state)));
                }
                finally
                {
                    window.SetHidden(true);
                }
            });

        await Assert.That(fitting).IsEqualTo(measuredRows);
        await Assert.That(tall).IsLessThan(measuredRows);
        await Capture(TallFooter(ViewerSession.Resize(state, columns, tall)));
    }

    /// <summary>
    /// The rows the window reports once it has laid a screen out. Three presents, since a table
    /// can take a second frame to settle on where its rows are.
    /// </summary>
    static int Measured(Screen screen)
    {
        for (var frame = 0; frame < 3; frame++)
        {
            window!.Present(screen);
        }

        return window!.Poll().Rows;
    }

    static Screen TallFooter(SessionState state)
    {
        var screen = ScreenBuilder.Build(state);
        return screen with
        {
            Buttons =
            [
                .. Enumerable
                    .Range(1, 8)
                    .SelectMany(_ => screen.Buttons.Select(button => button with {Label = $"{button.Label} {_}"}))
            ]
        };
    }

    static uint drawnQuery;

    /// <summary>
    /// How many of so many presents, of the screens given in turn, put anything on the screen.
    /// Asked of OpenGL, as <c>NativeHead</c> in the benchmarks asks it: the primitives generated
    /// between the start of a present and its end are the triangles the shim submitted, whichever
    /// rasteriser then filled them, and a present that generated none drew nothing. On the shim's
    /// thread, which is the one the GL context belongs to.
    /// </summary>
    static int Drawn(int presents, params Screen[] screens)
    {
        if (drawnQuery == 0)
        {
            Gl.GenQueries(1, out drawnQuery);
        }

        var drawn = 0;
        for (var present = 0; present < presents; present++)
        {
            Gl.BeginQuery(Gl.PrimitivesGenerated, drawnQuery);
            window!.Present(screens[present % screens.Length]);
            Gl.EndQuery(Gl.PrimitivesGenerated);
            Gl.GetQueryObject(drawnQuery, Gl.QueryResult, out var primitives);
            if (primitives > 0)
            {
                drawn++;
            }
        }

        return drawn;
    }

    /// <summary>
    /// The four OpenGL calls a query takes, from the library the shim's own context came from.
    /// </summary>
    static class Gl
    {
        const string library = "libGL.so.1";

        public const uint PrimitivesGenerated = 0x8C87;
        public const uint QueryResult = 0x8866;

        [DllImport(library, EntryPoint = "glGenQueries")]
        public static extern void GenQueries(int count, out uint id);

        [DllImport(library, EntryPoint = "glBeginQuery")]
        public static extern void BeginQuery(uint target, uint id);

        [DllImport(library, EntryPoint = "glEndQuery")]
        public static extern void EndQuery(uint target);

        [DllImport(library, EntryPoint = "glGetQueryObjectuiv")]
        public static extern void GetQueryObject(uint id, uint name, out uint value);
    }

    static Task Capture(SessionState state, int gridRows = rows) =>
        Capture(ScreenBuilder.Build(ViewerSession.Resize(state, columns, gridRows)));

    static async Task Capture(Screen screen)
    {
        var path = Path.Combine(Path.GetTempPath(), $"deview-{Guid.NewGuid():N}.png");
        try
        {
            await Assert.That(await OnShimThread(() => window!.Capture(screen, width, height, path))).IsTrue();
            // Linux and macOS run the same tests against different renderers, so the baselines
            // have to be told apart.
            await VerifyFile(path)
                .UniqueForOSPlatform();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
