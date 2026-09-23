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

    /// <summary>
    /// A drag from one change's context into the next, across the fold between them. A fold stands
    /// for the lines it leaves out, so it is highlighted whole and they are copied: what lies
    /// between a selection's two ends is what a selection is, on screen or not.
    /// </summary>
    [Test]
    public Task AcrossAFoldedRow() =>
        Verify(Report(Drag(Minimal(), PaneSide.Left, 4, 0, 8, 4)));

    /// <summary>
    /// A drag finishing on a fold takes in every line it stands for, since part of a label is not
    /// part of anything.
    /// </summary>
    [Test]
    public Task EndingOnAFoldedRow() =>
        Verify(Report(Drag(Minimal(), PaneSide.Left, 2, 0, 6, 3)));

    /// <summary>
    /// The same fold with the drag going up from it. It is still the end the selection finishes
    /// at, so it still takes in all of its lines, rather than stopping at the first of them because
    /// that is where the press landed.
    /// </summary>
    [Test]
    public Task StartingOnAFoldedRowGoingUp() =>
        Verify(Report(Drag(Minimal(), PaneSide.Left, 6, 3, 2, 0)));

    /// <summary>
    /// A selection is held in the entry's rows rather than the view's, so switching views leaves it
    /// selecting the same text: the middle of this one folds away and is still copied.
    /// </summary>
    [Test]
    public Task ASelectionSurvivesSwitchingViews()
    {
        var full = Files(Fixtures.Long(true), Fixtures.Long(false));
        var selected = Drag(full, PaneSide.Left, 4, 0, 14, 4);
        return Verify(Report(Key(selected, CommandKind.ToggleMinimal)));
    }

    /// <summary>
    /// A press with no drag behind it is a click whatever it lands on, and a click clears. On a
    /// fold, taking it for a selection of everything the fold stands for would select seven lines
    /// with a tap.
    /// </summary>
    [Test]
    public async Task AClickOnAFoldedRowClearsTheSelection()
    {
        var selected = Drag(Minimal(), PaneSide.Left, 1, 0, 3, 4);
        await Assert.That(selected.LiveSelection).IsNotNull();

        var clicked = Drag(selected, PaneSide.Left, 6, 3, 6, 3);

        await Assert.That(clicked.LiveSelection!.IsEmpty).IsTrue();
    }

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

    /// <summary>
    /// A wide character is two cells, and a drag that starts or ends inside one takes it whole or
    /// not at all: the highlight and the copy both end at the same character boundary.
    /// </summary>
    [Test]
    public async Task ADragInsideWideCharactersTakesThemWhole()
    {
        var window = new Recorder();
        // 中 is cells 0 and 1, 文 cells 2 and 3, a cell 4. From inside 中 to inside 文
        var state = Drag(Fixtures.File("中文a", "中文a"), PaneSide.Left, 0, 1, 0, 3);

        ViewerProgram.Apply(state, Input(CommandKind.Copy), link: null, window);
        var highlighted = ScreenBuilder.Build(state).Left.Rows[0].Selection;

        await Assert.That(window.Copied).IsEqualTo("文");
        await Assert.That((highlighted.Start, highlighted.Length)).IsEqualTo((2, 2));
    }

    /// <summary>
    /// A combining mark takes no cell: the bar after "é" is in cell 1, where every head draws it,
    /// and selecting that cell copies the bar. Counted a cell a code point, cell 1 was the mark.
    /// </summary>
    [Test]
    public async Task ACombiningMarkTakesNoCell()
    {
        var state = Drag(Fixtures.File("é|", "x"), PaneSide.Left, 0, 1, 0, 2);

        await Assert.That(Copy(state)).IsEqualTo("|");
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

    /// <summary>
    /// Forty lines in the minimal view: lines 1-6 on rows 0-5, a fold of lines 7-13 on row 6, and
    /// lines 14-20 from row 7.
    /// </summary>
    static SessionState Minimal() =>
        Key(Files(Fixtures.Long(true), Fixtures.Long(false)), CommandKind.ToggleMinimal);

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

    /// <summary>
    /// A re-run lands on the same key with other text: two lines more at the top, so what the
    /// reader selected is now two rows down. The selection was made on text that is gone, so it
    /// goes with it, rather than highlighting and copying rows the reader never selected.
    /// </summary>
    [Test]
    public async Task A_rerun_that_replaces_the_text_under_a_selection_ends_the_selection()
    {
        var state = Drag(Fixtures.Inline(Fixtures.Patch()), PaneSide.Left, 1, 0, 1, 9);
        await Assert.That(Copy(state)).IsEqualTo("brown dog");

        var rerun = ViewerSession.EnqueueInline(
            state,
            Fixtures.Patch(content: $"added one\nadded two\n{Fixtures.Received}"));
        await Assert.That(rerun.Current!.Key).IsEqualTo(state.Current!.Key);

        await Assert.That(Copy(rerun)).IsNull();
    }

    /// <summary>
    /// A status change is not new text, so a selection survives it.
    /// </summary>
    [Test]
    public async Task A_status_change_keeps_the_selection()
    {
        var state = Drag(Fixtures.Inline(Fixtures.Patch()), PaneSide.Left, 1, 0, 1, 9);

        var failed = ViewerSession.Apply(state, CommandKind.Accept, Fixtures.Applying(InlineApplyResult.Failed("locked")));

        await Assert.That(failed.Current!.Status).IsNotNull();
        await Assert.That(Copy(failed)).IsEqualTo("brown dog");
    }

    /// <summary>
    /// A head reports cells, and a character outside the basic plane is one character on the grid,
    /// not two: a drag across 𝐀 (U+1D400) alone ends at column 1, and copies all of it rather than
    /// half. Not an emoji, which is wide and so two cells: see CellGridTests.
    /// </summary>
    [Test]
    public async Task A_drag_across_one_non_bmp_character_copies_all_of_it()
    {
        var state = Drag(Fixtures.File("\U0001D400x", "x"), PaneSide.Left, 0, 0, 0, 1);

        await Assert.That(Copy(state)).IsEqualTo("\U0001D400");
    }

    /// <summary>
    /// "ab" drawn in cells 1 and 2, after 𝐀 in cell 0: the copy is what was highlighted.
    /// </summary>
    [Test]
    public async Task A_drag_after_a_non_bmp_character_copies_what_was_highlighted()
    {
        var state = Drag(Fixtures.File("\U0001D400ab", "x"), PaneSide.Left, 0, 1, 0, 3);

        await Assert.That(Copy(state)).IsEqualTo("ab");
        await Assert.That(ScreenBuilder.Build(state).Left.Rows[0].Selection).IsEqualTo(new(1, 2));
    }

    /// <summary>
    /// Select all ends at the last cell of the last row, not a cell further per wide character.
    /// </summary>
    [Test]
    public async Task Select_all_ends_on_the_last_cell()
    {
        var state = Key(Files("\U0001D400ab", "x"), CommandKind.SelectAll);

        await Assert.That(state.Selection!.FocusColumn).IsEqualTo(3);
        await Assert.That(Copy(state)).IsEqualTo("\U0001D400ab");
    }

    /// <summary>
    /// What ctrl+c puts on the clipboard, or null when it puts nothing there.
    /// </summary>
    static string? Copy(SessionState state)
    {
        var window = new Recorder();
        ViewerProgram.Apply(state, Input(CommandKind.Copy), link: null, window);
        return window.Copied;
    }
}
