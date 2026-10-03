using static DocumentScreenTests;

/// <summary>
/// Text, picture, or both is chosen per kind of document and kept: for the next one of that kind
/// in the queue, and for the next run. One setting for the whole window meant a reader who wanted
/// spreadsheets as text and maps as pictures switched on every entry.
/// </summary>
public class DrawingViewTests :
    IDisposable
{
    [Test]
    public async Task AViewChosenForOneKindLeavesTheOthersAlone()
    {
        var state = PdfThenSvg();
        state = ViewerSession.Apply(state, CommandKind.ToggleDrawing);
        await Assert.That(state.Drawing).IsEqualTo(DrawingView.Picture);

        state = ViewerSession.Apply(state, Command.Select(1));
        await Assert.That(state.Drawing).IsEqualTo(DrawingView.Both);

        state = ViewerSession.Apply(state, CommandKind.ToggleDrawing);
        state = ViewerSession.Apply(state, CommandKind.ToggleDrawing);
        await Assert.That(state.Drawing).IsEqualTo(DrawingView.Text);

        state = ViewerSession.Apply(state, Command.Select(0));
        await Assert.That(state.Drawing).IsEqualTo(DrawingView.Picture);
    }

    [Test]
    public async Task TheNextOfThatKindOpensTheSameWay()
    {
        var state = State(Left, Right, LeftText, RightText);
        state = ViewerSession.Apply(state, CommandKind.ToggleDrawing);
        state = ViewerSession.Apply(state, CommandKind.ToggleDrawing);

        state = ViewerSession.EnqueueFile(state, Pair("other", ".pdf", DocumentFormat.Pdf));
        state = ViewerSession.Apply(state, Command.Select(1));

        await Assert.That(state.Current!.LeftHeader).IsEqualTo("other.received.pdf");
        await Assert.That(state.Drawing).IsEqualTo(DrawingView.Text);
        await Assert.That(ScreenBuilder.Build(state).Buttons.Select(_ => _.Label)).Contains("Text and picture");
    }

    /// <summary>
    /// The rows a pane has room for turn on the view, so an entry opened in another kind's view
    /// would scroll and page by the wrong count.
    /// </summary>
    [Test]
    public async Task ThePaneIsLaidOutForTheKindOnScreen()
    {
        var state = PdfThenSvg().Showing(DrawingView.Text);
        var body = ScreenBuilder.BodyRows(state);
        await Assert.That(ScreenBuilder.PaneRows(state)).IsEqualTo(body);

        state = ViewerSession.Apply(state, Command.Select(1));
        await Assert.That(ScreenBuilder.PaneRows(state)).IsEqualTo(body / 2);
    }

    /// <summary>
    /// The identical state when there is nothing to change, so a frame that set nothing repaints
    /// nothing and writes nothing.
    /// </summary>
    [Test]
    public async Task ShowingWhatIsAlreadyShownChangesNothing()
    {
        var state = State(Left, Right, LeftText, RightText);
        await Assert.That(state.Showing(DrawingView.Both)).IsSameReferenceAs(state);

        var text = Fixtures.File();
        await Assert.That(text.Showing(DrawingView.Text)).IsSameReferenceAs(text);
        await Assert.That(text.Drawing).IsEqualTo(DrawingView.Both);
    }

    [Test]
    public async Task TheViewsAreRemembered()
    {
        var path = Path.Combine(directory, "viewer.settings");
        var state = PdfThenSvg().Showing(DrawingView.Text);
        state = ViewerSession.Apply(state, Command.Select(1)).Showing(DrawingView.Picture);

        new ViewerPreferences(path).Remember(state);

        var opened = new ViewerPreferences(path).Apply(PdfThenSvg());
        await Assert.That(opened.Drawing).IsEqualTo(DrawingView.Text);
        await Assert.That(ViewerSession.Apply(opened, Command.Select(1)).Drawing).IsEqualTo(DrawingView.Picture);
        await Assert.That(File.ReadAllLines(path)).IsEquivalentTo(["drawing.Pdf=Text", "drawing.Svg=Picture"]);
    }

    /// <summary>
    /// Both is what there is with nothing remembered, so going back to it is forgetting.
    /// </summary>
    [Test]
    public async Task BothIsNotWrittenDown()
    {
        var path = Path.Combine(directory, "viewer.settings");
        var preferences = new ViewerPreferences(path);
        var state = State(Left, Right, LeftText, RightText).Showing(DrawingView.Text);
        preferences.Remember(state);

        preferences.Remember(state.Showing(DrawingView.Both));

        await Assert.That(new ViewerPreferences(path).Drawings).IsEmpty();
        await Assert.That(new ViewerPreferences(path).Get("drawing.Pdf")).IsNull();
    }

    [Test]
    [Arguments("drawing.Pdf=Sideways")]
    [Arguments("drawing.Pdf=2")]
    [Arguments("drawing.Pdf=")]
    [Arguments("drawing.Papyrus=Text")]
    public async Task AViewThatIsNotOneIsBoth(string line)
    {
        var path = Path.Combine(directory, "viewer.settings");
        await File.WriteAllTextAsync(path, line);

        var state = new ViewerPreferences(path).Apply(State(Left, Right, LeftText, RightText));

        await Assert.That(state.Drawing).IsEqualTo(DrawingView.Both);
    }

    /// <summary>
    /// Asked after every frame that did anything, so a frame that changed no view must cost no
    /// write: scrolling a long document is hundreds of them.
    /// </summary>
    [Test]
    public async Task AFrameThatChangedNoViewWritesNothing()
    {
        var path = Path.Combine(directory, "viewer.settings");
        var preferences = new ViewerPreferences(path);
        var state = State(Left, Right, LeftText, RightText).Showing(DrawingView.Text);
        preferences.Remember(state);
        File.Delete(path);

        preferences.Remember(ViewerSession.Apply(state, CommandKind.ScrollDown));

        await Assert.That(File.Exists(path)).IsFalse();
    }

    /// <summary>
    /// The loop itself: a window opens with each kind as it was left, and a view the reader
    /// switches to is kept as they switch.
    /// </summary>
    [Test]
    public async Task TheLoopOpensEachKindAsItWasLeftAndKeepsTheNext()
    {
        var preferences = new ViewerPreferences();
        preferences.Remember(State(Left, Right, LeftText, RightText).Showing(DrawingView.Picture));
        var host = new SessionHost(State(Left, Right, LeftText, RightText));
        var window = new KeyWindow();

        IViewerWindow Open(string title, int width, int height, bool hidden, WindowPlacement? placement, out string? error)
        {
            error = null;
            return window;
        }

        ViewerProgram.Run(host, server: null, link: null, Open, preferences: preferences);

        // Opened as the picture alone, whose button offers the text, and switched to that
        await Assert.That(window.First!.Buttons.Select(_ => _.Label)).Contains("Text only");
        await Assert.That(host.State.Drawing).IsEqualTo(DrawingView.Text);
        await Assert.That(preferences.Drawings[DocumentFormat.Pdf]).IsEqualTo(DrawingView.Text);
    }

    static SessionState PdfThenSvg() =>
        ViewerSession.Apply(
            ViewerSession.EnqueueFile(State(Left, Right, LeftText, RightText), Pair("logo", ".svg", DocumentFormat.Svg)),
            Command.Select(0));

    static QueueEntry Pair(string name, string extension, DocumentFormat format) =>
        QueueEntry.ForFiles(
            $"temp/{name}.received{extension}",
            $"code/{name}.verified{extension}",
            new("left", null, null, null, new($"temp/{name}.received{extension}", 120, format, $"{name}-1")),
            new("right", null, null, null, new($"code/{name}.verified{extension}", 121, format, $"{name}-2")));

    readonly string directory = Directory.CreateTempSubdirectory("deview-drawing-view-").FullName;

    public void Dispose() =>
        Directory.Delete(directory, true);

    /// <summary>
    /// A window that presses r on its first frame and has closed by its second.
    /// </summary>
    sealed class KeyWindow : IViewerWindow
    {
        int frames;

        public Screen? First { get; private set; }

        public bool Present(Screen screen)
        {
            First ??= screen;
            return frames++ == 0;
        }

        public ViewerInput Poll() =>
            new(CommandKind.ToggleDrawing, -1, -1, 0, false, 180, Fixtures.Rows);

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
