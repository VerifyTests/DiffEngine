/// <summary>
/// The menu a right-click on a pane's text opens: copy what is selected, copy the whole side,
/// select the whole side. The copying a reader otherwise has to know the keys for.
/// </summary>
public class PaneMenuTests
{
    [Test]
    public async Task APaneOffersToCopyItAndToSelectIt()
    {
        var state = ViewerSession.OpenPaneMenu(Fixtures.File(), PaneSide.Right);

        await Assert.That(Labels(state)).IsEqualTo("Copy all, Select all");
        await Assert.That(state.Menu!.Pane).IsEqualTo(PaneSide.Right);
    }

    /// <summary>
    /// First, because it is what a reader who has just dragged across some text right-clicked for.
    /// </summary>
    [Test]
    public async Task ASelectionIsOfferedFirst()
    {
        var state = ViewerSession.Drag(Fixtures.File(), PaneSide.Left, 1, 0, 1, 9);

        state = ViewerSession.OpenPaneMenu(state, PaneSide.Left);

        await Assert.That(Labels(state)).IsEqualTo("Copy selection, Copy all, Select all");
    }

    /// <summary>
    /// A right-click on a queue row selects the row. One on a pane leaves what is selected in it
    /// alone: clearing it on the way to the menu would leave nothing for the menu to copy.
    /// </summary>
    [Test]
    public async Task OpeningTheMenuKeepsTheSelection()
    {
        var state = ViewerSession.Drag(Fixtures.File(), PaneSide.Left, 1, 0, 1, 9);
        var selection = state.Selection;

        state = ViewerSession.OpenPaneMenu(state, PaneSide.Left);

        await Assert.That(state.Selection).IsEqualTo(selection);
    }

    [Test]
    public async Task CopySelectionCopiesWhatWasDraggedAcross()
    {
        var window = new Recorder();
        var state = ViewerSession.Drag(Fixtures.File(), PaneSide.Left, 1, 0, 1, 9);
        state = ViewerSession.OpenPaneMenu(state, PaneSide.Left);

        state = Click(state, "Copy selection", window);

        await Assert.That(window.Copied).IsEqualTo("brown dog");
        await Assert.That(state.Menu).IsNull();
    }

    /// <summary>
    /// The side the menu was opened over, whichever side something is selected in.
    /// </summary>
    [Test]
    public async Task CopyAllCopiesThePaneItWasAskedIn()
    {
        var window = new Recorder();
        var state = ViewerSession.Drag(Fixtures.File("left one\nleft two", "right one\nright two"), PaneSide.Left, 0, 0, 0, 4);
        state = ViewerSession.OpenPaneMenu(state, PaneSide.Right);

        state = Click(state, "Copy all", window);

        await Assert.That(window.Copied).IsEqualTo("right one\nright two");
        await Assert.That(state.Menu).IsNull();
    }

    /// <summary>
    /// The key selects the side something is already selected in. The menu selects the side it
    /// was opened over, which is the one the reader pointed at.
    /// </summary>
    [Test]
    public async Task SelectAllSelectsThePaneItWasAskedIn()
    {
        var window = new Recorder();
        var state = ViewerSession.Drag(Fixtures.File("left one\nleft two", "right one\nright two"), PaneSide.Left, 0, 0, 0, 4);
        state = ViewerSession.OpenPaneMenu(state, PaneSide.Right);

        state = Click(state, "Select all", window);

        await Assert.That(state.LiveSelection!.Side).IsEqualTo(PaneSide.Right);
        await Assert.That(SelectionText.Of(state.LiveSelection, state.Current!)).IsEqualTo("right one\nright two");
        await Assert.That(state.Menu).IsNull();
    }

    /// <summary>
    /// The expected side of a brand new snapshot has nothing in it. No menu, rather than one of
    /// items that copy nothing, and a menu already open goes.
    /// </summary>
    [Test]
    public async Task APaneWithNothingInItHasNoMenu()
    {
        var state = ViewerSession.OpenPaneMenu(Fixtures.File("content", ""), PaneSide.Left);
        await Assert.That(state.Menu).IsNotNull();

        state = ViewerSession.OpenPaneMenu(state, PaneSide.Right);

        await Assert.That(state.Menu).IsNull();
    }

    /// <summary>
    /// A document seen as its pages shows rows describing it, and nothing is selected in those. Its
    /// text is still there to copy whole.
    /// </summary>
    [Test]
    public async Task ADocumentSeenAsItsPagesCanBeCopiedButNotSelected()
    {
        var state = DocumentScreenTests.Drawn(
                DocumentScreenTests.State(
                    DocumentScreenTests.Left,
                    DocumentScreenTests.Right,
                    DocumentScreenTests.LeftText,
                    DocumentScreenTests.RightText))
            .Showing(DrawingView.Picture);

        state = ViewerSession.OpenPaneMenu(state, PaneSide.Left);

        await Assert.That(Labels(state)).IsEqualTo("Copy all");
    }

    /// <summary>
    /// A head hangs it where the pointer was, so the screen says which pane and no row.
    /// </summary>
    [Test]
    public async Task TheScreenSaysWhichPaneTheMenuIsFor()
    {
        var screen = ScreenBuilder.Build(ViewerSession.OpenPaneMenu(Fixtures.File(), PaneSide.Right));

        await Assert.That(screen.Menu!.Pane).IsEqualTo(PaneSide.Right);
        await Assert.That(screen.Menu.Row).IsEqualTo(-1);
        await Assert.That(string.Join(", ", screen.Menu.Labels)).IsEqualTo("Copy all, Select all");
    }

    [Test]
    public Task OverTheExpectedPane() =>
        Verify(Fixtures.Render(ViewerSession.OpenPaneMenu(Fixtures.File(), PaneSide.Right)));

    [Test]
    public Task OverTheReceivedPaneOfAQueue() =>
        Verify(Fixtures.Render(ViewerSession.OpenPaneMenu(Fixtures.Inline(Fixtures.Patch()), PaneSide.Left)));

    /// <summary>
    /// A frame's input: a right-click a head reports over a pane. Not an idle frame, and a
    /// dismissal of the menu before it, arriving in the same frame, does not close the new one.
    /// </summary>
    [Test]
    public async Task ARightClickOverAPaneOpensItsMenu()
    {
        var state = Fixtures.File();
        var input = Input() with { RightClickedPane = 1 };
        await Assert.That(ViewerProgram.IsIdle(input, state)).IsFalse();

        state = ViewerProgram.Apply(state, input with { MenuClosed = true }, link: null, new Recorder());

        await Assert.That(state.Menu!.Pane).IsEqualTo(PaneSide.Right);
    }

    /// <summary>
    /// Any other input closes it, as it closes a queue row's.
    /// </summary>
    [Test]
    public async Task AnythingElseClosesIt()
    {
        var state = ViewerSession.OpenPaneMenu(Fixtures.File(), PaneSide.Right);

        await Assert.That(ViewerSession.Apply(state, CommandKind.ScrollDown).Menu).IsNull();
        await Assert.That(ViewerProgram.Apply(state, Input() with { MenuClosed = true }, link: null, new Recorder()).Menu).IsNull();
    }

    static string Labels(SessionState state) =>
        string.Join(", ", state.Menu!.Items.Select(_ => _.Label));

    /// <summary>
    /// A click on the item with this label, as a head reports one: by its place in the menu.
    /// </summary>
    static SessionState Click(SessionState state, string label, IViewerWindow window)
    {
        var index = state.Menu!.Items.ToList().FindIndex(_ => _.Label == label);
        return ViewerProgram.Apply(state, Input() with { ClickedMenuItem = index }, link: null, window);
    }

    static ViewerInput Input() =>
        new(CommandKind.None, -1, -1, 0, false, Fixtures.Columns, Fixtures.Rows);

    sealed class Recorder : IViewerWindow
    {
        public string? Copied { get; private set; }

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

        public void SetClipboard(string text) =>
            Copied = text;

        public bool Capture(Screen screen, int width, int height, string pngPath) =>
            false;

        public void Dispose()
        {
        }
    }
}
