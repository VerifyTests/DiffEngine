/// <summary>
/// The wheel and the pointer over a picture, through the messages a mouse sends: which notches
/// are for the picture and which for the rows, and that dragging an enlarged picture reports
/// where it has been dragged to.
/// </summary>
[NotInParallel]
[TUnit.Core.Executors.STAThreadExecutor]
public class PictureZoomTests
{
    const int width = 1100;
    const int height = 700;
    const int columns = 120;
    const int rows = 37;

    [Test]
    public async Task TheWheelOverAPictureZoomsAndOverTheRowsScrolls()
    {
        using var host = new Host();
        host.Draw(Fixtures.Images());
        var picture = Middle(host.Canvas.PictureAreas[0]);
        var zoomed = 0;
        var scrolled = 0;
        host.Canvas.Zoomed += _ => zoomed += _;
        host.Canvas.Scrolled += _ => scrolled += _;

        host.Wheel(picture, 120);
        host.Wheel(picture, 120);
        host.Wheel(picture, -120);
        // The first row of the received pane, which is text
        host.Wheel(new(picture.X, 70), -120);

        await Assert.That(zoomed).IsEqualTo(1);
        await Assert.That(scrolled).IsEqualTo(-1);
    }

    /// <summary>
    /// Both sides have one, and it is the space under each pane's rows rather than the picture
    /// alone: a small picture is a small target, and the wheel beside it means the same thing.
    /// </summary>
    [Test]
    public async Task EachSideHasASpaceForItsPicture()
    {
        using var host = new Host();
        host.Draw(Fixtures.Images());

        var areas = host.Canvas.PictureAreas;
        await Assert.That(areas.Count).IsEqualTo(2);
        await Assert.That(areas[0].Right).IsLessThanOrEqualTo(areas[1].Left);
        await Assert.That(areas[0].Width).IsGreaterThan(300);
        await Assert.That(areas[0].Height).IsGreaterThan(300);
    }

    [Test]
    public async Task ATextEntryHasNoPictureForTheWheelToFind()
    {
        using var host = new Host();
        host.Draw(Fixtures.File());
        var zoomed = 0;
        var scrolled = 0;
        host.Canvas.Zoomed += _ => zoomed += _;
        host.Canvas.Scrolled += _ => scrolled += _;

        host.Wheel(new(width / 4, height / 2), 120);

        await Assert.That(host.Canvas.PictureAreas).IsEmpty();
        await Assert.That(zoomed).IsEqualTo(0);
        await Assert.That(scrolled).IsEqualTo(1);
    }

    /// <summary>
    /// Dragging right brings what is to the left of the middle into view, by as much of the
    /// enlarged picture as the pointer crossed. Reported once per move and then not again.
    /// </summary>
    [Test]
    public async Task DraggingAnEnlargedPictureReportsWhereItWent()
    {
        using var host = new Host();
        var state = Enlarged(Fixtures.Images());
        host.Draw(state);
        var area = host.Canvas.PictureAreas[0];
        var image = ScreenBuilder.Build(ViewerSession.Resize(state, columns, rows)).Left.Image!;
        var placement = PicturePlacement.Of(area, image);
        var press = Middle(area);

        host.Press(press);
        await Assert.That(host.Canvas.TakePan()).IsNull();

        host.Move(new(press.X + 96, press.Y - 48));
        var dragged = host.Canvas.TakePan();
        host.Release(new(press.X + 96, press.Y - 48));

        await Assert.That(dragged).IsEqualTo(placement.Dragged(new(96, -48)));
        await Assert.That(dragged!.Value.X).IsLessThan(0.5);
        await Assert.That(dragged.Value.Y).IsGreaterThan(0.5);
        await Assert.That(host.Canvas.TakePan()).IsNull();
    }

    /// <summary>
    /// A picture that fits has nowhere to go, so pressing on one and moving is nothing.
    /// </summary>
    [Test]
    public async Task AFittedPictureIsNotDragged()
    {
        using var host = new Host();
        host.Draw(Fixtures.Images());
        var press = Middle(host.Canvas.PictureAreas[0]);

        host.Press(press);
        host.Move(new(press.X + 96, press.Y - 48));
        host.Release(new(press.X + 96, press.Y - 48));

        await Assert.That(host.Canvas.TakePan()).IsNull();
    }

    /// <summary>
    /// The button let go over another window never tells this one. A move with no button held is
    /// the drag having ended, rather than the picture following the pointer for good.
    /// </summary>
    [Test]
    public async Task AMoveWithNoButtonHeldEndsTheDrag()
    {
        using var host = new Host();
        host.Draw(Enlarged(Fixtures.Images()));
        var press = Middle(host.Canvas.PictureAreas[0]);
        host.Press(press);
        host.Move(new(press.X + 20, press.Y));
        host.Canvas.TakePan();

        host.Hover(new(press.X + 200, press.Y));

        await Assert.That(host.Canvas.TakePan()).IsNull();
    }

    /// <summary>
    /// Six steps in, which is eight times the size that fits: past the space both ways.
    /// </summary>
    static SessionState Enlarged(SessionState state)
    {
        for (var step = 0; step < 6; step++)
        {
            state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        }

        return state;
    }

    static Point Middle(Rectangle area) =>
        new(area.X + area.Width / 2, area.Y + area.Height / 2);

    /// <summary>
    /// A canvas in a real window, parked off screen, as <see cref="PaneHitTests"/> has.
    /// </summary>
    sealed class Host : IDisposable
    {
        readonly Form form = new()
        {
            StartPosition = FormStartPosition.Manual,
            Location = new(-2000, -2000),
            ShowInTaskbar = false,
            ClientSize = new(width, height)
        };

        public ViewerCanvas Canvas { get; } = new()
        {
            Dock = DockStyle.Fill,
            // The pictures are there on the first paint, as in a capture
            Synchronous = true
        };

        public Host()
        {
            form.Controls.Add(Canvas);
            form.Show();
        }

        public void Draw(SessionState state)
        {
            Canvas.Draw(ScreenBuilder.Build(ViewerSession.Resize(state, columns, rows)));
            // Where the pictures are is known once it has painted, and a window parked off every
            // display is never asked to: drawing it to a bitmap is what sends the paint.
            using var bitmap = new Bitmap(Canvas.Width, Canvas.Height);
            Canvas.DrawToBitmap(bitmap, new(0, 0, Canvas.Width, Canvas.Height));
        }

        public void Wheel(Point at, int delta)
        {
            // The one mouse message whose point is in screen coordinates
            var screen = Canvas.PointToScreen(at);
            SendMessage(Canvas.Handle, mouseWheel, new(delta << 16), Pack(screen));
        }

        public void Press(Point at) =>
            SendMessage(Canvas.Handle, leftButtonDown, leftButtonFlag, Pack(at));

        /// <summary>
        /// Raised rather than sent. WinForms reads the buttons of a move from the mouse itself,
        /// not from the message, so a move sent with the left button flagged arrives as one with
        /// none held, which is the drag ending.
        /// </summary>
        public void Move(Point at) =>
            typeof(Control)
                .GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Canvas, [new MouseEventArgs(MouseButtons.Left, 0, at.X, at.Y, 0)]);

        public void Hover(Point at) =>
            SendMessage(Canvas.Handle, mouseMove, IntPtr.Zero, Pack(at));

        public void Release(Point at) =>
            SendMessage(Canvas.Handle, leftButtonUp, IntPtr.Zero, Pack(at));

        public void Dispose() =>
            form.Dispose();

        /// <summary>
        /// Each coordinate as a signed sixteen bits, since the window is parked at a negative one.
        /// </summary>
        static IntPtr Pack(Point point) =>
            new(((point.Y & 0xFFFF) << 16) | (point.X & 0xFFFF));

        const int mouseMove = 0x0200;
        const int leftButtonDown = 0x0201;
        const int leftButtonUp = 0x0202;
        const int mouseWheel = 0x020A;
        static readonly IntPtr leftButtonFlag = new(1);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
