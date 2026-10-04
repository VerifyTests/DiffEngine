/// <summary>
/// How a document comparison reads, in each of its three views.
/// <para>
/// Snapshotted through <see cref="AsciiRenderer"/> like every other screen. The page is drawn by a
/// head and never by this renderer, so everything a reader can learn from the pages - which one is
/// showing, which differ, that they are still being drawn - has to be in the headers and the status
/// line, and these are what pin it there.
/// </para>
/// <para>
/// Sides and pages are numbers and fake paths, as an image side is in <see cref="ImageScreenTests"/>:
/// no document or png is ever opened to build a screen.
/// </para>
/// </summary>
public class DocumentScreenTests
{
    [Test]
    public Task ReadingText() =>
        Verify(Fixtures.Render(State(Reading(Left), Reading(Right))));

    /// <summary>
    /// The default view: the text in the top half of each pane, the opening page under it, which is
    /// the first that differs.
    /// </summary>
    [Test]
    public Task TextAndPicture() =>
        Verify(Fixtures.Render(Drawn(State(Left, Right, LeftText, RightText))));

    [Test]
    public Task PictureOnly() =>
        Verify(Fixtures.Render(Drawn(State(Left, Right, LeftText, RightText)).Showing(DrawingView.Picture)));

    [Test]
    public Task TextOnly() =>
        Verify(Fixtures.Render(Drawn(State(Left, Right, LeftText, RightText)).Showing(DrawingView.Text)));

    /// <summary>
    /// Pages still landing: those drawn are shown, and nothing is said to differ until both sides
    /// have drawn the page.
    /// </summary>
    [Test]
    public Task Drawing()
    {
        var state = State(Left, Right, LeftText, RightText);
        state = ViewerSession.Rendered(state, Left.Hash!, new([Page("L1")], false));
        state = ViewerSession.Rendered(state, Right.Hash!, Rendering.Started);
        return Verify(Fixtures.Render(state));
    }

    [Test]
    public Task Identical() =>
        Verify(Fixtures.Render(State(Left, Left with { Path = Right.Path }, LeftText, LeftText)));

    /// <summary>
    /// A brand new document snapshot: nothing committed to compare against yet.
    /// </summary>
    [Test]
    public Task NewDocument() =>
        Verify(Fixtures.Render(State(Left, null, LeftText)));

    [Test]
    public Task CouldNotReadTheText() =>
        Verify(
            Fixtures.Render(
                State(
                    Left,
                    Right with
                    {
                        Unreadable = "it is encrypted."
                    },
                    LeftText)));

    [Test]
    public Task CouldNotDraw()
    {
        var state = State(Left, Right, LeftText, RightText);
        state = ViewerSession.Rendered(state, Left.Hash!, new([Page("L1")], true));
        state = ViewerSession.Rendered(state, Right.Hash!, new([], true, "PDFium could not open it."));
        return Verify(Fixtures.Render(state));
    }

    /// <summary>
    /// A status line is one line, and Morph's reasons can run to several: a font it could not find
    /// is followed by every folder it looked in.
    /// </summary>
    [Test]
    public async Task AReasonIsOneLine()
    {
        var state = State(Left, Right, LeftText, RightText);
        state = ViewerSession.Rendered(state, Left.Hash!, new([Page("L1")], true));
        state = ViewerSession.Rendered(
            state,
            Right.Hash!,
            new([], true, "Font 'Calibri' not found. Checked:\n  /usr/share/fonts\n  (Morph embedded fonts)"));
        await Assert.That(ScreenBuilder.Build(state).Status)
            .EndsWith("could not draw sample.verified.pdf: Font 'Calibri' not found");
    }

    /// <summary>
    /// A file that is not a document of its kind fails to be read and fails to be drawn, for the
    /// one reason. Said once, about the file, rather than once for each with the second running off
    /// the end of the footer.
    /// </summary>
    [Test]
    public async Task ADamagedFileIsSaidOnce()
    {
        const string reason = "Not a readable PDF: file is not a PDF or is corrupt";
        var state = Damaged(reason, reason);

        await Assert.That(ScreenBuilder.Build(state).Status)
            .IsEqualTo($"could not read sample.verified.pdf: {reason}");

        // Each view on its own still says what it could not do
        await Assert.That(ScreenBuilder.Build(state.Showing(DrawingView.Text)).Status)
            .IsEqualTo($"could not read the text of sample.verified.pdf: {reason}");
        await Assert.That(ScreenBuilder.Build(state.Showing(DrawingView.Picture)).Status)
            .IsEqualTo($"could not draw sample.verified.pdf: {reason}");
    }

    /// <summary>
    /// Two different things wrong are two things to say: a document that reads and will not draw,
    /// or whose text and pages failed differently, is not a damaged file.
    /// </summary>
    [Test]
    public async Task TwoReasonsAreBothSaid()
    {
        var state = Damaged("it is encrypted.", "gave up after 120 seconds.");

        await Assert.That(ScreenBuilder.Build(state).Status).IsEqualTo(
            "could not read the text of sample.verified.pdf: it is encrypted, could not draw sample.verified.pdf: gave up after 120 seconds");
    }

    static SessionState Damaged(string text, string drawing)
    {
        var state = State(Left, Right with { Unreadable = text }, LeftText);
        state = ViewerSession.Rendered(state, Left.Hash!, new([Page("L1")], true));
        return ViewerSession.Rendered(state, Right.Hash!, new([], true, drawing));
    }

    /// <summary>
    /// One side has a page the other has not, which is a page that differs, and the side without
    /// it says so in its header rather than drawing nothing silently.
    /// </summary>
    [Test]
    public Task PageOnlyOneSideHas()
    {
        var state = State(Left, Right, LeftText, RightText);
        state = ViewerSession.Rendered(state, Left.Hash!, new([Page("L1"), Page("L2"), Page("L3")], true));
        state = ViewerSession.Rendered(state, Right.Hash!, new([Page("L1"), Page("L2")], true));
        return Verify(Fixtures.Render(state));
    }

    [Test]
    public Task SvgTextAndPicture() =>
        Verify(Fixtures.Render(DrawnSvg(SvgState())));

    [Test]
    public Task SvgPictureOnly() =>
        Verify(Fixtures.Render(DrawnSvg(SvgState()).Showing(DrawingView.Picture)));

    [Test]
    public Task SvgTextOnly() =>
        Verify(Fixtures.Render(DrawnSvg(SvgState()).Showing(DrawingView.Text)));

    /// <summary>
    /// The enrichment a head draws under the rows: the page being read, as an ordinary picture.
    /// </summary>
    [Test]
    public async Task PanesCarryThePage()
    {
        var screen = ScreenBuilder.Build(Drawn(State(Left, Right, LeftText, RightText)));
        await Assert.That(screen.Left.Image).IsEqualTo(new("render/L2.png", 625, 417, "L2"));
        await Assert.That(screen.Right.Image).IsEqualTo(new("render/R2.png", 625, 417, "R2"));
    }

    /// <summary>
    /// A page still to come is said to be coming, so a head can show something turning where it
    /// will go: before drawing has started, and while it has not got as far as the page on screen.
    /// </summary>
    [Test]
    public async Task APageStillToComeIsPending()
    {
        var state = State(Left, Right, LeftText, RightText);
        var waiting = ScreenBuilder.Build(state);
        await Assert.That(waiting.Left.ImagePending).IsTrue();
        await Assert.That(waiting.Right.ImagePending).IsTrue();

        // The left has drawn the opening page and the right has not started
        state = ViewerSession.Rendered(state, Left.Hash!, new([Page("L1")], false));
        state = ViewerSession.Rendered(state, Right.Hash!, Rendering.Started);
        var drawing = ScreenBuilder.Build(state);
        await Assert.That(drawing.Left.Image).IsNotNull();
        await Assert.That(drawing.Left.ImagePending).IsFalse();
        await Assert.That(drawing.Right.Image).IsNull();
        await Assert.That(drawing.Right.ImagePending).IsTrue();
    }

    /// <summary>
    /// Nothing is coming once drawing has finished or failed, when the header says why there is no
    /// page, for a side with no file, or in the text view, which has nowhere to put a page.
    /// </summary>
    [Test]
    public async Task NothingIsPendingWhereNoPageIsComing()
    {
        var state = State(Left, Right, LeftText, RightText);
        state = ViewerSession.Rendered(state, Left.Hash!, new([Page("L1")], true));
        state = ViewerSession.Rendered(state, Right.Hash!, new([], true, "PDFium could not open it."));
        state = state with { Page = 1 };
        var stopped = ScreenBuilder.Build(state);
        await Assert.That(stopped.Left.ImagePending).IsFalse();
        await Assert.That(stopped.Right.ImagePending).IsFalse();

        var text = ScreenBuilder.Build(State(Left, Right, LeftText, RightText).Showing(DrawingView.Text));
        await Assert.That(text.Left.ImagePending).IsFalse();
        await Assert.That(text.Right.ImagePending).IsFalse();

        await Assert.That(ScreenBuilder.Build(State(Left, null, LeftText)).Right.ImagePending).IsFalse();
    }

    [Test]
    public async Task TheTextViewDrawsNothing()
    {
        var screen = ScreenBuilder.Build(Drawn(State(Left, Right, LeftText, RightText)).Showing(DrawingView.Text));
        await Assert.That(screen.Left.Image).IsNull();
        await Assert.That(screen.Right.Image).IsNull();
    }

    /// <summary>
    /// The page takes the bottom half, by the text taking only the top half: the heads place a
    /// picture under whatever rows a pane has.
    /// </summary>
    [Test]
    public async Task TheTextTakesTheTopHalf()
    {
        var state = Drawn(State(Left, Right,Fixtures.Long(false), Fixtures.Long(true)));
        var screen = ScreenBuilder.Build(state);
        await Assert.That(screen.Left.Rows.Count).IsEqualTo(ScreenBuilder.BodyRows(state) / 2);
        await Assert.That(ScreenBuilder.Build(state.Showing(DrawingView.Text)).Left.Rows.Count)
            .IsEqualTo(ScreenBuilder.BodyRows(state));
    }

    /// <summary>
    /// The copy menu names a pane by the entry's own header, which stays the file's name whatever
    /// page is showing.
    /// </summary>
    [Test]
    public async Task ThePageIsNotPartOfTheEntryHeader()
    {
        var state = Drawn(State(Left, Right, LeftText, RightText));
        await Assert.That(ScreenBuilder.Build(state).Left.Header).IsEqualTo("sample.received.pdf (page 2 of 3)");
        await Assert.That(state.Current!.LeftHeader).IsEqualTo("sample.received.pdf");
    }

    internal const string LeftText =
        """
        --- page 1 ---
        alpha
        --- page 2 ---
        bravo
        --- page 3 ---
        charlie
        """;

    internal const string RightText =
        """
        --- page 1 ---
        alpha
        --- page 2 ---
        BRAVO
        --- page 3 ---
        charlie
        """;

    internal static DocumentFile Left { get; } = new("temp/sample.received.pdf", 1_234, DocumentFormat.Pdf, "AA");
    internal static DocumentFile Right { get; } = new("code/sample.verified.pdf", 1_240, DocumentFormat.Pdf, "BB");

    static DocumentFile Reading(DocumentFile file) =>
        file with { Reading = true };

    internal static RenderedPage Page(string name) =>
        new($"render/{name}.png", 625, 417, name);

    /// <summary>
    /// Three pages a side, the second drawing differently.
    /// </summary>
    internal static SessionState Drawn(SessionState state)
    {
        state = ViewerSession.Rendered(state, Left.Hash!, new([Page("L1"), Page("L2"), Page("L3")], true));
        return ViewerSession.Rendered(state, Right.Hash!, new([Page("L1"), Page("R2"), Page("L3")], true));
    }

    /// <summary>
    /// Wider than <see cref="Fixtures.Columns"/>: a document's footer carries its view and page
    /// buttons as well, and a footer that runs out of room cuts the status line short.
    /// </summary>
    const int columns = 210;

    internal static SessionState State(DocumentFile? left, DocumentFile? right, string leftText = "", string rightText = "") =>
        ViewerSession.EnqueueFile(
            SessionState.Start(ViewerMode.File, columns, Fixtures.Rows),
            QueueEntry.ForFiles(
                "temp/sample.received.pdf",
                "code/sample.verified.pdf",
                new(leftText, null, null, null, left),
                new(rightText, null, null, null, right)));

    static SessionState SvgState() =>
        ViewerSession.EnqueueFile(
            SessionState.Start(ViewerMode.File, columns, Fixtures.Rows),
            QueueEntry.ForFiles(
                "temp/logo.received.svg",
                "code/logo.verified.svg",
                new(Svg("red"), null, null, null, new("temp/logo.received.svg", 120, DocumentFormat.Svg, "S1")),
                new(Svg("blue"), null, null, null, new("code/logo.verified.svg", 121, DocumentFormat.Svg, "S2"))));

    static SessionState DrawnSvg(SessionState state)
    {
        state = ViewerSession.Rendered(state, "S1", new([new("render/S1.png", 1024, 1024, "S1P")], true));
        return ViewerSession.Rendered(state, "S2", new([new("render/S2.png", 1024, 1024, "S2P")], true));
    }

    static string Svg(string colour) =>
        $"""
         <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24">
           <circle cx="12" cy="12" r="10" fill="{colour}" />
         </svg>
         """;
}
