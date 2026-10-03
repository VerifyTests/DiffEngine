using static DocumentScreenTests;

/// <summary>
/// Enlarging the picture on screen, whichever kind it is: an image, a document's page, an SVG or a
/// map. What the model holds is the step and the point at the middle; drawing it is a head's.
/// </summary>
public class ZoomTests
{
    [Test]
    public async Task ZoomingInGoesAStepAtATimeAndStopsAtTheLast()
    {
        var state = Images;
        await Assert.That(Zoom(state)).IsEqualTo(1);

        state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        await Assert.That(Zoom(state)).IsEqualTo(1.5);

        for (var step = 0; step < 20; step++)
        {
            state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        }

        await Assert.That(state.Zoom).IsEqualTo(PictureZoom.Last);
        await Assert.That(Zoom(state)).IsEqualTo(16);
        await Assert.That(ViewerSession.Apply(state, CommandKind.ZoomIn)).IsSameReferenceAs(state);
    }

    /// <summary>
    /// In three and out three is where it started, which is what stepping rather than scaling by a
    /// factor buys.
    /// </summary>
    [Test]
    public async Task ZoomingOutRetracesTheSteps()
    {
        var state = Images;
        for (var step = 0; step < 3; step++)
        {
            state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        }

        await Assert.That(Zoom(state)).IsEqualTo(3);

        state = ViewerSession.Apply(state, CommandKind.ZoomOut);
        await Assert.That(Zoom(state)).IsEqualTo(2);

        state = ViewerSession.Apply(state, CommandKind.ZoomOut);
        state = ViewerSession.Apply(state, CommandKind.ZoomOut);
        await Assert.That(Zoom(state)).IsEqualTo(1);
        await Assert.That(ViewerSession.Apply(state, CommandKind.ZoomOut)).IsSameReferenceAs(state);
    }

    /// <summary>
    /// The two panes are the same enlargement of the same part of each picture, which is the
    /// reason to zoom a comparison at all.
    /// </summary>
    [Test]
    public async Task BothSidesShowTheSamePartAtTheSameSize()
    {
        var state = ViewerSession.Apply(Images, CommandKind.ZoomIn);
        state = ViewerSession.PanTo(state, 0.25, 0.75);

        var screen = ScreenBuilder.Build(state);
        await Assert.That(screen.Left.Image!.Zoom).IsEqualTo(1.5);
        await Assert.That(screen.Right.Image!.Zoom).IsEqualTo(1.5);
        await Assert.That((screen.Left.Image.CenterX, screen.Left.Image.CenterY)).IsEqualTo((0.25, 0.75));
        await Assert.That((screen.Right.Image.CenterX, screen.Right.Image.CenterY)).IsEqualTo((0.25, 0.75));
    }

    /// <summary>
    /// Fitted, the whole picture shows, so where it had been dragged to means nothing and is not
    /// kept for the next time it is enlarged.
    /// </summary>
    [Test]
    public async Task BackAtFittedThePictureIsCentredAgain()
    {
        var state = ViewerSession.Apply(Images, CommandKind.ZoomIn);
        state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        state = ViewerSession.PanTo(state, 0.1, 0.9);

        var out1 = ViewerSession.Apply(state, CommandKind.ZoomOut);
        await Assert.That(out1.Pan).IsEqualTo(new PanPoint(0.1, 0.9));

        var reset = ViewerSession.Apply(state, CommandKind.ZoomReset);
        await Assert.That(reset.Zoom).IsEqualTo(0);
        await Assert.That(reset.Pan).IsEqualTo(PanPoint.Centre);
        await Assert.That(ViewerSession.Apply(out1, CommandKind.ZoomOut).Pan).IsEqualTo(PanPoint.Centre);
    }

    /// <summary>
    /// A fitted picture has nowhere to be dragged to, and a point off the picture is not one.
    /// </summary>
    [Test]
    public async Task OnlyAnEnlargedPictureMovesAndOnlyWithinItself()
    {
        var fitted = Images;
        await Assert.That(ViewerSession.PanTo(fitted, 0.2, 0.2)).IsSameReferenceAs(fitted);

        var zoomed = ViewerSession.Apply(fitted, CommandKind.ZoomIn);
        await Assert.That(ViewerSession.PanTo(zoomed, -3, 7).Pan).IsEqualTo(new PanPoint(0, 1));
        await Assert.That(ViewerSession.PanTo(zoomed, 0.5, 0.5)).IsSameReferenceAs(zoomed);
    }

    /// <summary>
    /// Keys skip the buttons' enabled check, so the commands refuse an entry with no picture and a
    /// document in its text view themselves.
    /// </summary>
    [Test]
    public async Task OnlyAPictureOnScreenIsZoomed()
    {
        var text = Fixtures.File();
        await Assert.That(ViewerSession.Apply(text, CommandKind.ZoomIn)).IsSameReferenceAs(text);

        var textOnly = Drawn(State(Left, Right, LeftText, RightText)).Showing(DrawingView.Text);
        await Assert.That(ViewerSession.Apply(textOnly, CommandKind.ZoomIn)).IsSameReferenceAs(textOnly);

        var page = Drawn(State(Left, Right, LeftText, RightText));
        await Assert.That(ViewerSession.Apply(page, CommandKind.ZoomIn).Zoom).IsEqualTo(1);
    }

    /// <summary>
    /// A document's page is enlarged as an image is, and stays so through its pages: the same part
    /// of the next page is what a reader comparing a header or a footer wants.
    /// </summary>
    [Test]
    public async Task APageIsZoomedAndStaysZoomedAsPagesTurn()
    {
        var state = Drawn(State(Left, Right, LeftText, RightText));
        state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        state = ViewerSession.PanTo(state, 0.3, 0.1);

        state = ViewerSession.Apply(state, CommandKind.NextPage);

        var screen = ScreenBuilder.Build(state);
        await Assert.That(screen.Left.Image!.Path).IsEqualTo("render/L3.png");
        await Assert.That(screen.Left.Image.Zoom).IsEqualTo(1.5);
        await Assert.That(screen.Left.Image.CenterX).IsEqualTo(0.3);
    }

    /// <summary>
    /// Where one picture was worth looking at closely says nothing about the next.
    /// </summary>
    [Test]
    public async Task AnotherEntryOpensFitted()
    {
        var state = ViewerSession.EnqueueFile(
            Images,
            QueueEntry.ForFiles("a.txt", "b.txt", FileSide.OfText("a"), FileSide.OfText("b")));
        state = ViewerSession.Apply(state, Command.Select(0));
        state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        state = ViewerSession.PanTo(state, 0.2, 0.2);

        state = ViewerSession.Apply(state, Command.Select(1));
        state = ViewerSession.Apply(state, Command.Select(0));

        await Assert.That(state.Zoom).IsEqualTo(0);
        await Assert.That(state.Pan).IsEqualTo(PanPoint.Centre);
    }

    /// <summary>
    /// What a renderer that draws no picture can say about it, and what tells a reader looking at
    /// one corner of a page that a corner is what they have.
    /// </summary>
    [Test]
    public async Task TheStatusLineSaysHowFarIn()
    {
        var state = Images;
        await Assert.That(ScreenBuilder.Build(state).Status).IsEqualTo("images differ");

        state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        await Assert.That(ScreenBuilder.Build(state).Status).IsEqualTo("images differ, zoom 150%");

        var page = ViewerSession.Apply(Drawn(State(Left, Right, LeftText, RightText)), CommandKind.ZoomIn);
        page = ViewerSession.Apply(page, CommandKind.ZoomIn);
        await Assert.That(ScreenBuilder.Build(page).Status).EndsWith("page 2 differs, zoom 200%");
    }

    [Test]
    public async Task TheButtonsSayWhichWayThereIsToGo()
    {
        var state = Images;
        await Assert.That(Button(state, CommandKind.ZoomOut)).IsEqualTo(new Button("Zoom out", false, CommandKind.ZoomOut));
        await Assert.That(Button(state, CommandKind.ZoomIn)).IsEqualTo(new Button("Zoom in", true, CommandKind.ZoomIn));

        state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        await Assert.That(Button(state, CommandKind.ZoomOut)!.Enabled).IsTrue();

        state = state with { Zoom = PictureZoom.Last };
        await Assert.That(Button(state, CommandKind.ZoomIn)!.Enabled).IsFalse();
    }

    /// <summary>
    /// Kept in the footer for a document in its text view, disabled, so the buttons do not move
    /// about as the view is cycled, and not there at all for an entry that is only text.
    /// </summary>
    [Test]
    public async Task TheButtonsAreForEntriesThatDrawAPicture()
    {
        await Assert.That(Button(Fixtures.File(), CommandKind.ZoomIn)).IsNull();

        var textOnly = Drawn(State(Left, Right, LeftText, RightText)).Showing(DrawingView.Text);
        await Assert.That(Button(textOnly, CommandKind.ZoomIn)).IsEqualTo(new Button("Zoom in", false, CommandKind.ZoomIn));
        await Assert.That(Button(textOnly, CommandKind.ZoomOut)).IsEqualTo(new Button("Zoom out", false, CommandKind.ZoomOut));
    }

    /// <summary>
    /// A frame's input: wheel notches a head said were for the picture, and where a drag has left
    /// it. Neither is an idle frame, and a wheel spun hard still stops at the last step.
    /// </summary>
    [Test]
    public async Task AFrameOfWheelAndDragIsApplied()
    {
        var state = Images;
        var wheel = Input(zoom: 2);
        await Assert.That(ViewerProgram.IsIdle(wheel, state)).IsFalse();

        state = ViewerProgram.Apply(state, wheel, link: null, new Window());
        await Assert.That(state.Zoom).IsEqualTo(2);

        var drag = Input(panX: 0.8, panY: 0.3);
        await Assert.That(ViewerProgram.IsIdle(drag, state)).IsFalse();
        state = ViewerProgram.Apply(state, drag, link: null, new Window());
        await Assert.That(state.Pan).IsEqualTo(new PanPoint(0.8, 0.3));

        state = ViewerProgram.Apply(state, Input(zoom: -1), link: null, new Window());
        await Assert.That(state.Zoom).IsEqualTo(1);

        state = ViewerProgram.Apply(state, Input(zoom: 500), link: null, new Window());
        await Assert.That(state.Zoom).IsEqualTo(PictureZoom.Last);
    }

    /// <summary>
    /// The wheel over the rows is still the rows': zoom is only ever what a head reports as zoom.
    /// </summary>
    [Test]
    public async Task ScrollingIsStillScrolling()
    {
        var state = Images;
        var scrolled = ViewerProgram.Apply(state, Input() with { ScrollDelta = -1 }, link: null, new Window());

        await Assert.That(scrolled.Zoom).IsEqualTo(0);
    }

    /// <summary>
    /// Built once. The fixture writes its two pictures to a fixed path, and these tests run beside
    /// each other.
    /// </summary>
    static SessionState Images { get; } = Fixtures.Images();

    static double Zoom(SessionState state) =>
        ScreenBuilder.Build(state).Left.Image!.Zoom;

    static Button? Button(SessionState state, CommandKind command) =>
        ScreenBuilder.Build(state).Buttons.SingleOrDefault(_ => _.Command == command);

    static ViewerInput Input(int zoom = 0, double panX = -1, double panY = -1) =>
        new(CommandKind.None, -1, -1, 0, false, Fixtures.Columns, Fixtures.Rows, ZoomDelta: zoom, PanX: panX, PanY: panY);

    sealed class Window : IViewerWindow
    {
        public bool Present(Screen screen) =>
            true;

        public ViewerInput Poll() =>
            default;

        public void SetHidden(bool hidden)
        {
        }

        public void Focus()
        {
        }

        public void SetClipboard(string text)
        {
        }

        public bool Capture(Screen screen, int width, int height, string pngPath) =>
            false;

        public void Dispose()
        {
        }
    }
}
