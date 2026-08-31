/// <summary>
/// Text selection: what a drag covers, what is highlighted, and what lands on the clipboard.
/// <para>
/// Driven through <see cref="ViewerProgram.Apply"/> rather than <see cref="ViewerSession"/>
/// directly, because a drag arrives as input fields and copying reaches a window, and those two
/// joints are the whole of what makes the feature work in a head.
/// </para>
/// <para>
/// The highlight is reported here rather than by <see cref="AsciiRenderer"/>, which draws a fixed
/// width character grid and cannot invert part of one without changing its width. That is why the
/// status line carries the model's universal statement about a selection: it is what the text
/// snapshots, and a renderer with no highlight, can still show.
/// </para>
/// </summary>
public class SelectionTests
{
    [Test]
    public Task AcrossRows() =>
        Verify(Report(Drag(Files(), PaneSide.Left, 1, 6, 3, 4)));

    /// <summary>
    /// The same range dragged the other way. The ends are kept as anchor and focus rather than
    /// ordered, so extending back past where the press landed keeps working; the ordering happens
    /// where the range is read.
    /// </summary>
    [Test]
    public Task DraggedBackwards() =>
        Verify(Report(Drag(Files(), PaneSide.Left, 3, 4, 1, 6)));

    [Test]
    public Task WithinOneRow() =>
        Verify(Report(Drag(Files(), PaneSide.Right, 1, 6, 1, 9)));

    /// <summary>
    /// A drag that ran off the bottom and off the ends of the lines. Heads report where the
    /// pointer was without knowing how long a line is, so everything is pulled back inside the
    /// text here.
    /// </summary>
    [Test]
    public Task PastTheEnds() =>
        Verify(Report(Drag(Files(), PaneSide.Left, 0, 400, 90, 400)));

    /// <summary>
    /// Filler rows keep the two panes aligned and are not content, so a selection that spans one
    /// copies the lines either side of it rather than a blank line between them.
    /// </summary>
    [Test]
    public Task OverAFillerRow() =>
        Verify(
            Report(
                Drag(
                    Files("one\ntwo\nthree\nfour", "one\nfour"),
                    PaneSide.Right,
                    0,
                    0,
                    3,
                    4)));

    [Test]
    public Task SelectAll() =>
        Verify(Report(Key(Files(), CommandKind.SelectAll)));

    /// <summary>
    /// Select all takes the side something is already selected in, so it reads as widening what
    /// the reader was pointing at rather than as jumping to the other pane.
    /// </summary>
    [Test]
    public Task SelectAllAfterClickingTheExpectedPane() =>
        Verify(Report(Key(Drag(Files(), PaneSide.Right, 0, 2, 0, 5), CommandKind.SelectAll)));

    [Test]
    public async Task AClickWithNoDragBehindItClearsTheSelection()
    {
        var selected = Drag(Files(), PaneSide.Left, 1, 6, 3, 4);
        await Assert.That(selected.LiveSelection).IsNotNull();

        // Both ends in the same place, which is what a head reports for a press.
        var clicked = Drag(selected, PaneSide.Left, 2, 3, 2, 3);

        await Assert.That(clicked.LiveSelection!.IsEmpty).IsTrue();
        await Assert.That(ScreenBuilder.Build(clicked).Status).IsEqualTo("lines 1-5 of 5");
    }

    /// <summary>
    /// A selection names the entry it was dragged in, so moving to another one leaves it behind
    /// rather than highlighting the same rows of a different file. One rule in one place, instead
    /// of a clear-the-selection call on every transition.
    /// </summary>
    [Test]
    public async Task ASelectionDoesNotFollowTheSelectedEntry()
    {
        var state = Drag(
            Fixtures.Inline(Fixtures.Patch(), Fixtures.Patch("OtherTests.cs", 7)),
            PaneSide.Left,
            1,
            0,
            1,
            5);
        await Assert.That(state.LiveSelection).IsNotNull();

        var stepped = ViewerSession.Apply(state, CommandKind.NextItem);

        await Assert.That(stepped.Selection).IsNotNull();
        await Assert.That(stepped.LiveSelection).IsNull();
        await Assert.That(ScreenBuilder.Build(stepped).Left.Rows.Any(_ => _.Selection.Length > 0))
            .IsFalse();
    }

    [Test]
    public async Task CopyPutsTheSelectionOnTheClipboard()
    {
        var window = new Recorder();
        var state = Drag(Files(), PaneSide.Left, 1, 6, 3, 4);

        var copied = ViewerProgram.Apply(state, Input(CommandKind.Copy), link: null, window);

        await Assert.That(window.Copied).IsEqualTo("dog\njumps over\nthe ");
        await Assert.That(copied.Message).IsEqualTo("Copied 3 lines from the selection.");
    }

    [Test]
    public async Task CopyWithNothingSelectedSaysSoAndWritesNothing()
    {
        var window = new Recorder();

        var copied = ViewerProgram.Apply(Files(), Input(CommandKind.Copy), link: null, window);

        await Assert.That(window.Copied).IsNull();
        await Assert.That(copied.Message)
            .IsEqualTo("Nothing is selected. Drag across a pane, or press ctrl+a.");
    }

    [Test]
    public async Task CopyASideTakesAllOfItWithoutTheFiller()
    {
        var window = new Recorder();
        var state = Files("one\ntwo\nthree\nfour", "one\nfour");

        var copied = ViewerProgram.Apply(state, Input(CommandKind.CopyRight), link: null, window);

        await Assert.That(window.Copied).IsEqualTo("one\nfour");
        await Assert.That(copied.Message).IsEqualTo("Copied 2 lines from Sample.verified.txt.");
    }

    /// <summary>
    /// Copying reads what is on screen and writes it to this machine's clipboard, so it is never
    /// forwarded to a queue owner - whose answer would be the text this process already holds.
    /// </summary>
    [Test]
    public async Task CopyIsLocalEvenWhenDisplayingSomeoneElsesQueue()
    {
        var window = new Recorder();
        var state = Drag(
            Fixtures.Attached(InlineQueue.Empty, Fixtures.Move()),
            PaneSide.Left,
            0,
            0,
            0,
            3);
        var link = new OwnerLink(new(state), port: 1);

        var copied = ViewerProgram.Apply(state, Input(CommandKind.Copy), link, window);

        await Assert.That(window.Copied).IsEqualTo("the");
        await Assert.That(copied.Message).IsEqualTo("Copied 1 line from the selection.");
    }

    /// <summary>
    /// Select all then copy, which is the keyboard's whole route to the clipboard and the reason
    /// ctrl has to be answered before the plain letters: ctrl+a used to reach A, which accepts.
    /// </summary>
    [Test]
    public async Task SelectAllThenCopyTakesTheWholeSide()
    {
        var window = new Recorder();
        var all = Key(Files(), CommandKind.SelectAll);

        ViewerProgram.Apply(all, Input(CommandKind.Copy), link: null, window);

        await Assert.That(window.Copied).IsEqualTo(Fixtures.Received);
    }

    static SessionState Files(string left = Fixtures.Received, string right = Fixtures.Expected) =>
        Fixtures.File(left, right);

    static SessionState Drag(
        SessionState state,
        PaneSide side,
        int anchorRow,
        int anchorColumn,
        int focusRow,
        int focusColumn) =>
        ViewerProgram.Apply(
            state,
            Input() with
            {
                DragSide = (int) side,
                DragAnchorRow = anchorRow,
                DragAnchorColumn = anchorColumn,
                DragFocusRow = focusRow,
                DragFocusColumn = focusColumn
            },
            link: null,
            new Recorder());

    static SessionState Key(SessionState state, CommandKind key) =>
        ViewerProgram.Apply(state, Input(key), link: null, new Recorder());

    static ViewerInput Input(CommandKind key = CommandKind.None) =>
        new(key, -1, -1, 0, false, Fixtures.Columns, Fixtures.Rows);

    /// <summary>
    /// The frame as far as a selection is concerned: what the status says about it, which
    /// characters of which rows are highlighted, and what copying it would hand over.
    /// </summary>
    static string Report(SessionState state)
    {
        var screen = ScreenBuilder.Build(state);
        var builder = new StringBuilder();
        builder.AppendLine($"status: {screen.Status}");
        Append(builder, screen.Left);
        Append(builder, screen.Right);
        builder.AppendLine();
        builder.AppendLine("copied:");
        builder.Append(
            state.LiveSelection is { } selection
                ? SelectionText.Of(selection, state.Current!)
                : "<nothing selected>");
        return builder.ToString();
    }

    /// <summary>
    /// Brackets rather than a highlight, and only on the rows that have one. The width changes,
    /// which is exactly why this cannot be what the grid renderer does.
    /// </summary>
    static void Append(StringBuilder builder, Pane pane)
    {
        builder.AppendLine();
        builder.AppendLine(pane.Header);
        foreach (var row in pane.Rows)
        {
            var text = RowText.Flatten(row.Text);
            if (row.Selection.Length > 0)
            {
                text = text
                    .Insert(row.Selection.Start + row.Selection.Length, "]")
                    .Insert(row.Selection.Start, "[");
            }

            builder.AppendLine($"  {row.LineNumber,4}  {text}");
        }
    }

    /// <summary>
    /// A window that draws nothing and remembers what was copied, which is the only thing about a
    /// head this needs.
    /// </summary>
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
