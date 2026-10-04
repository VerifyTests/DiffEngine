/// <summary>
/// A large picture zoomed into, through the real canvas. While it is drawn at half its own size or
/// less, what shows is copied out of the whole of it scaled to that size, which is made once and
/// kept. Further in than that it is drawn straight from the picture.
/// <para>
/// The pictures are stripes of two colours, the left one's across and the right one's down, so
/// where a picture was drawn can be read back from where its stripes change, to the pixel and
/// whatever filter scaled it. One pair for the class: nothing here writes to them.
/// </para>
/// </summary>
[NotInParallel]
[TUnit.Core.Executors.STAThreadExecutor]
public class EnlargedPictureTests
{
    const int columns = 120;
    const int rows = 37;

    /// <summary>
    /// Fits a pane of the default window at 15% of its size, so the six steps in go through all
    /// three ways of drawing it: the first three are still under half its size, the next two are
    /// between that and its own, and the sixth is past it.
    /// </summary>
    static readonly Size picture = new(3600, 2700);

    const int stripe = 300;

    static readonly Color dark = Color.FromArgb(40, 40, 160);
    static readonly Color light = Color.FromArgb(230, 210, 80);

    static string directory = "";
    static string left = "";
    static string right = "";

    [Before(Class)]
    public static void WritePictures()
    {
        directory = Directory.CreateTempSubdirectory("deview-enlarged-").FullName;
        left = Write("striped.received.png", across: true);
        right = Write("striped.verified.png", across: false);
    }

    [After(Class)]
    public static void DeletePictures() =>
        Directory.Delete(directory, true);

    /// <summary>
    /// Painted again and again, and dragged about between paints, a picture this far out is scaled
    /// once a side. Where it has been dragged to is no part of what is kept, so a drag copies a
    /// different part of the same thing rather than scaling the picture again for every frame.
    /// </summary>
    [Test]
    public async Task ItIsScaledOnceHoweverItIsDragged()
    {
        using var host = new Host();
        host.Canvas.Synchronous = true;
        var state = Zoomed(1);

        host.Draw(state);
        host.Draw(state);
        foreach (var centre in (double[]) [0.4, 0.45, 0.5, 0.55, 0.6])
        {
            host.Draw(ViewerSession.PanTo(state, centre, 0.5));
        }

        await Assert.That(host.Composed).IsEqualTo(2);
    }

    /// <summary>
    /// What is kept is the whole picture at the size it is drawn, so no more than a quarter of the
    /// picture's own pixels: at most half its width by half its height.
    /// </summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task WhatIsKeptIsTheWholeOfItAtTheSizeItIsDrawn(int steps)
    {
        using var host = new Host();
        host.Canvas.Synchronous = true;

        host.Draw(Zoomed(steps));
        var placement = host.Placement(PaneSide.Left);
        var kept = host.Kept(left);

        await Assert.That(kept).IsNotNull();
        await Assert.That(kept!.Value.Width).IsEqualTo((int) Math.Round(placement.Size.Width));
        await Assert.That(kept.Value.Height).IsEqualTo((int) Math.Round(placement.Size.Height));
        await Assert.That(kept.Value.Width * 2).IsLessThanOrEqualTo(picture.Width);
    }

    /// <summary>
    /// Past half its size the whole of it would be up to the picture's own size again, which is too
    /// much to hold twice. Nothing is kept, and it is drawn straight from the picture.
    /// </summary>
    [Test]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task PastHalfItsSizeNothingIsKept(int steps)
    {
        using var host = new Host();
        host.Canvas.Synchronous = true;

        var drawn = host.Draw(Zoomed(steps));

        await Assert.That(host.Composed).IsEqualTo(0);
        await Assert.That(host.Kept(left)).IsNull();
        await Assert.That(Misplaced(drawn, host.Placement(PaneSide.Left), across: true)).IsEmpty();
    }

    /// <summary>
    /// However it is drawn, it is the part the placement says, where the placement says: every
    /// change of stripe on screen is within a pixel and a half of where the picture's own stripes
    /// fall. Fitted, at each step in, and dragged off centre, so through the copy, through the
    /// filter that reduces and through the one that shows pixels as they are.
    /// </summary>
    [Test]
    [Arguments(0, 0.5, 0.5)]
    [Arguments(1, 0.5, 0.5)]
    [Arguments(1, 0.37, 0.52)]
    [Arguments(2, 0.5, 0.5)]
    [Arguments(2, 0.31, 0.64)]
    [Arguments(3, 0.5, 0.5)]
    [Arguments(3, 0.71, 0.33)]
    [Arguments(4, 0.5, 0.5)]
    [Arguments(4, 0.29, 0.68)]
    [Arguments(5, 0.62, 0.41)]
    [Arguments(6, 0.5, 0.5)]
    [Arguments(6, 0.13, 0.88)]
    public async Task ItIsDrawnWhereItIsPlaced(int steps, double centreX, double centreY)
    {
        using var host = new Host();
        host.Canvas.Synchronous = true;

        var drawn = host.Draw(ViewerSession.PanTo(Zoomed(steps), centreX, centreY));

        await Assert.That(Misplaced(drawn, host.Placement(PaneSide.Left), across: true)).IsEmpty();
        await Assert.That(Misplaced(drawn, host.Placement(PaneSide.Right), across: false)).IsEmpty();
    }

    /// <summary>
    /// A drag moves the picture a pixel for each pixel the pointer moves, with the copy as it does
    /// without it. The part that shows starts between two pixels of the copy as often as on one:
    /// in a pane an odd number of pixels wide a picture centred in it starts exactly half way,
    /// and stays half way for the whole of a drag. Rounded on that line, each frame falls whichever
    /// way the arithmetic's last digit sends it, and the picture stands still for one pixel of the
    /// drag and jumps two for the next.
    /// </summary>
    [Test]
    [Arguments(1100)]
    [Arguments(1102)]
    public async Task ADragMovesItAPixelForEachPixelDragged(int width)
    {
        using var host = new Host(width);
        host.Canvas.Synchronous = true;
        var state = Zoomed(2);
        var first = host.Draw(state);
        var from = host.Placement(PaneSide.Left);
        var start = Stripes(first, from.Bounds, across: true);
        await Assert.That(start).IsNotEmpty();

        var moves = new List<int>();
        var last = start[start.Count / 2];
        for (var by = 1; by <= 40; by++)
        {
            // As the canvas reports a drag: from where the button went down, not from the last move
            var centre = from.Dragged(new(by, 0));
            var drawn = host.Draw(ViewerSession.PanTo(state, centre.X, centre.Y));
            // The same change of stripe, which is the one nearest to where the last frame left it
            var now = Stripes(drawn, from.Bounds, across: true).MinBy(_ => Math.Abs(_ - last - 1));
            moves.Add(now - last);
            last = now;
        }

        Console.WriteLine($"{width} wide, a pane's picture {from.Bounds.Width} wide: moved {string.Join(" ", moves)}");
        await Assert.That(moves.Distinct()).IsEquivalentTo([1]);
    }

    /// <summary>
    /// In a window, where a picture is decoded and scaled on the pool: no paint scales it itself.
    /// Each one draws whatever there is to draw meanwhile, and the copy arrives through the message
    /// loop, after which nothing is left turning and the picture is where it belongs.
    /// </summary>
    [Test]
    public async Task InAWindowItIsScaledOffTheThreadThatPaints()
    {
        using var host = new Host();
        var state = Zoomed(1);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (host.Composed < 2 &&
               DateTime.UtcNow < deadline)
        {
            var before = host.Composed;
            host.Draw(state);
            await Assert.That(host.Composed).IsEqualTo(before);
            Application.DoEvents();
            Thread.Sleep(10);
        }

        var landed = host.Draw(state);

        await Assert.That(host.Composed).IsEqualTo(2);
        await Assert.That(host.Canvas.Spinners).IsEmpty();
        await Assert.That(Misplaced(landed, host.Placement(PaneSide.Left), across: true)).IsEmpty();
    }

    /// <summary>
    /// And while the copy at a new size is on its way, the last thing composed stands in for it,
    /// the same part of it stretched into place: zooming in from fitted shows the picture at once,
    /// rough, rather than a spinner on every step.
    /// </summary>
    [Test]
    public async Task UntilItLandsTheLastSizeStandsIn()
    {
        using var host = new Host();
        var fitted = Zoomed(0);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (host.Composed < 2 &&
               DateTime.UtcNow < deadline)
        {
            host.Draw(fitted);
            Application.DoEvents();
            Thread.Sleep(10);
        }

        await Assert.That(host.Composed).IsEqualTo(2);

        // The first paint after zooming in, before anything more has been handed back
        var meanwhile = host.Draw(Zoomed(1));

        await Assert.That(host.Composed).IsEqualTo(2);
        await Assert.That(host.Canvas.Spinners).IsEmpty();
        // Stretched half as large again, so an edge is a pixel or two wide and found within three
        await Assert.That(Misplaced(meanwhile, host.Placement(PaneSide.Left), across: true, within: 3)).IsEmpty();
    }

    /// <summary>
    /// The pair, the given number of steps in from fitted.
    /// </summary>
    static SessionState Zoomed(int steps)
    {
        var state = ViewerSession.EnqueueFile(
            SessionState.Start(ViewerMode.File, columns, rows),
            QueueEntry.ForFiles(left, right, FileSide.Read(left), FileSide.Read(right)));
        for (var step = 0; step < steps; step++)
        {
            state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        }

        return state;
    }

    /// <summary>
    /// What is wrong with where a picture's stripes were drawn, which is nothing when every change
    /// of stripe the placement puts on screen was drawn within <paramref name="within"/> pixels of
    /// there, and nothing else was drawn that looks like one. A change within a few pixels of the
    /// edge of what shows is not held to either, since it may or may not have a pixel beyond it.
    /// </summary>
    static List<string> Misplaced(Bitmap drawn, PicturePlacement placement, bool across, double within = 1.5)
    {
        const int margin = 4;
        var bounds = placement.Bounds;
        var origin = across ? bounds.X : bounds.Y;
        var end = across ? bounds.Right : bounds.Bottom;
        var full = across ? picture.Width : picture.Height;
        var source = across ? placement.Source.X : placement.Source.Y;
        var size = across ? placement.Size.Width : placement.Size.Height;
        var expected = new List<double>();
        for (var edge = stripe; edge < full; edge += stripe)
        {
            expected.Add(origin + ((double) edge / full - source) * size);
        }

        var found = Stripes(drawn, bounds, across);
        var wrong = new List<string>();
        var inside = expected.Where(_ => _ > origin + margin && _ < end - margin).ToList();
        if (inside.Count == 0)
        {
            wrong.Add("No change of stripe is placed where it would show, so nothing was checked");
        }

        foreach (var edge in inside)
        {
            if (!found.Any(_ => Math.Abs(_ - edge) <= within))
            {
                wrong.Add($"The change placed at {edge:F1} was drawn at none of {string.Join(" ", found)}");
            }
        }

        foreach (var change in found.Where(_ => _ > origin + margin && _ < end - margin))
        {
            if (!expected.Any(_ => Math.Abs(_ - change) <= within))
            {
                wrong.Add($"A change was drawn at {change}, and none is placed there: {string.Join(" ", expected.Select(_ => _.ToString("F1")))}");
            }
        }

        return wrong;
    }

    /// <summary>
    /// Where the drawn stripes change, along a line through the middle of what shows: the first
    /// pixel of each that is the other colour from the one before it.
    /// </summary>
    static List<int> Stripes(Bitmap drawn, Rectangle bounds, bool across)
    {
        var changes = new List<int>();
        var from = across ? bounds.X : bounds.Y;
        var to = across ? bounds.Right : bounds.Bottom;
        bool? before = null;
        for (var at = from; at < to; at++)
        {
            var pixel = across
                ? drawn.GetPixel(at, bounds.Y + bounds.Height / 2)
                : drawn.GetPixel(bounds.X + bounds.Width / 2, at);
            // The light stripe has little blue in it and the dark one little else
            var isLight = pixel.R > pixel.B;
            if (before is { } was &&
                was != isLight)
            {
                changes.Add(at);
            }

            before = isLight;
        }

        return changes;
    }

    static string Write(string name, bool across)
    {
        var path = Path.Combine(directory, name);
        using var bitmap = new Bitmap(picture.Width, picture.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            using var darkBrush = new SolidBrush(dark);
            using var lightBrush = new SolidBrush(light);
            var count = (across ? picture.Width : picture.Height) / stripe;
            for (var index = 0; index < count; index++)
            {
                graphics.FillRectangle(
                    index % 2 == 0 ? darkBrush : lightBrush,
                    across
                        ? new(index * stripe, 0, stripe, picture.Height)
                        : new Rectangle(0, index * stripe, picture.Width, stripe));
            }
        }

        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    /// <summary>
    /// A canvas in a real window, parked off screen and out of the taskbar, as the other tests of
    /// the canvas host one.
    /// </summary>
    sealed class Host : IDisposable
    {
        readonly Form form = new ParkedForm
        {
            StartPosition = FormStartPosition.Manual,
            Location = new(-4000, -2000),
            ShowInTaskbar = false
        };

        readonly List<Bitmap> bitmaps = [];

        Screen? screen;

        public ViewerCanvas Canvas { get; } = new();

        public Host(int width = 1100, int height = 700)
        {
            Canvas.Size = new(width, height);
            form.Controls.Add(Canvas);
            form.Show();
        }

        public Bitmap Draw(SessionState state)
        {
            screen = ScreenBuilder.Build(ViewerSession.Resize(state, columns, rows));
            Canvas.Draw(screen);
            Canvas.Refresh();
            var bitmap = new Bitmap(Canvas.Width, Canvas.Height);
            Canvas.DrawToBitmap(bitmap, new(0, 0, Canvas.Width, Canvas.Height));
            bitmaps.Add(bitmap);
            return bitmap;
        }

        /// <summary>
        /// Where the last paint put a side's picture, by the rule the paint and the pointer share.
        /// </summary>
        public PicturePlacement Placement(PaneSide side) =>
            PicturePlacement.Of(
                Canvas.PictureAreas[side == PaneSide.Left ? 0 : 1],
                side == PaneSide.Left ? screen!.Left.Image! : screen!.Right.Image!);

        ImageCache Cache =>
            (ImageCache) typeof(ViewerCanvas)
                .GetField("images", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(Canvas)!;

        /// <summary>
        /// How many times a picture has been composed or scaled to be kept.
        /// </summary>
        public int Composed =>
            Cache.Composed;

        /// <summary>
        /// The size of what is kept of a picture, or null when nothing is.
        /// </summary>
        public Size? Kept(string path) =>
            Cache.Composited(path)?.Size;

        public void Dispose()
        {
            foreach (var bitmap in bitmaps)
            {
                bitmap.Dispose();
            }

            form.Dispose();
        }
    }
}
