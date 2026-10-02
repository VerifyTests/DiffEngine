using static DocumentScreenTests;

/// <summary>
/// The commands that move around a document: the three views, turning pages, and moving between
/// the pages that differ when only the pages are on screen.
/// </summary>
public class DocumentSessionTests
{
    [Test]
    public async Task TheViewCycles()
    {
        var state = Drawn(State(Left, Right, LeftText, RightText));
        await Assert.That(state.Drawing).IsEqualTo(DrawingView.Both);

        state = ViewerSession.Apply(state, CommandKind.ToggleDrawing);
        await Assert.That(state.Drawing).IsEqualTo(DrawingView.Picture);

        state = ViewerSession.Apply(state, CommandKind.ToggleDrawing);
        await Assert.That(state.Drawing).IsEqualTo(DrawingView.Text);

        state = ViewerSession.Apply(state, CommandKind.ToggleDrawing);
        await Assert.That(state.Drawing).IsEqualTo(DrawingView.Both);
    }

    /// <summary>
    /// Keys skip the button's enabled check, so the command itself has to refuse an entry that is
    /// not a document.
    /// </summary>
    [Test]
    public async Task TheViewIsADocumentsToCycle()
    {
        var state = Fixtures.File();
        await Assert.That(ViewerSession.Apply(state, CommandKind.ToggleDrawing)).IsSameReferenceAs(state);
        await Assert.That(ViewerSession.Apply(state, CommandKind.NextPage)).IsSameReferenceAs(state);
    }

    /// <summary>
    /// An entry opens at its first change; a document opens at its first page that differs.
    /// </summary>
    [Test]
    public async Task OpensAtTheFirstPageThatDiffers()
    {
        var state = Drawn(State(Left, Right, LeftText, RightText));
        await Assert.That(state.Page).IsNull();
        await Assert.That(DocumentPages.Current(state)).IsEqualTo(1);
    }

    [Test]
    public async Task PagesTurnWithinThoseDrawn()
    {
        var state = Drawn(State(Left, Right, LeftText, RightText));

        state = ViewerSession.Apply(state, CommandKind.NextPage);
        await Assert.That(DocumentPages.Current(state)).IsEqualTo(2);

        state = ViewerSession.Apply(state, CommandKind.NextPage);
        await Assert.That(DocumentPages.Current(state)).IsEqualTo(2);

        state = ViewerSession.Apply(state, CommandKind.PreviousPage);
        state = ViewerSession.Apply(state, CommandKind.PreviousPage);
        state = ViewerSession.Apply(state, CommandKind.PreviousPage);
        await Assert.That(DocumentPages.Current(state)).IsEqualTo(0);
    }

    /// <summary>
    /// In the text view there is no page on screen to turn.
    /// </summary>
    [Test]
    public async Task NoPagesTurnInTheTextView()
    {
        var state = Drawn(State(Left, Right, LeftText, RightText)) with { Drawing = DrawingView.Text };
        await Assert.That(ViewerSession.Apply(state, CommandKind.NextPage)).IsSameReferenceAs(state);
    }

    /// <summary>
    /// With only the pages on screen, a change is a page that differs, so next and previous change
    /// step over the pages that are the same.
    /// </summary>
    [Test]
    public async Task ChangesArePagesWhenOnlyThePagesShow()
    {
        var state = State(Left, Right, LeftText, RightText) with { Drawing = DrawingView.Picture };
        state = ViewerSession.Rendered(state, Left.Hash!, new([Page("A"), Page("B"), Page("C"), Page("D")], true));
        state = ViewerSession.Rendered(state, Right.Hash!, new([Page("X"), Page("B"), Page("C"), Page("Y")], true));
        await Assert.That(DocumentPages.Current(state)).IsEqualTo(0);

        state = ViewerSession.Apply(state, CommandKind.NextChange);
        await Assert.That(DocumentPages.Current(state)).IsEqualTo(3);

        await Assert.That(ViewerSession.Apply(state, CommandKind.NextChange)).IsSameReferenceAs(state);

        state = ViewerSession.Apply(state, CommandKind.PreviousChange);
        await Assert.That(DocumentPages.Current(state)).IsEqualTo(0);
    }

    /// <summary>
    /// The page belongs to the entry being read, so moving to another and back opens it afresh.
    /// </summary>
    [Test]
    public async Task OpeningAnotherEntryForgetsThePage()
    {
        var state = Drawn(State(Left, Right, LeftText, RightText));
        state = ViewerSession.EnqueueFile(state, QueueEntry.ForFiles("a.txt", "b.txt", FileSide.OfText("a"), FileSide.OfText("b")));
        state = ViewerSession.Apply(state, CommandKind.NextPage);
        await Assert.That(state.Page).IsEqualTo(2);

        state = ViewerSession.Apply(state, Command.Select(1));
        state = ViewerSession.Apply(state, Command.Select(0));
        await Assert.That(state.Page).IsNull();
    }

    /// <summary>
    /// Rows describing a document are not its text, and a selection is held in rows of the text.
    /// </summary>
    [Test]
    public async Task NothingIsSelectedInThePropertyRows()
    {
        var state = Drawn(State(Left, Right, LeftText, RightText)) with { Drawing = DrawingView.Picture };

        state = ViewerSession.Drag(state, PaneSide.Left, 0, 0, 1, 5);
        await Assert.That(state.Selection).IsNull();

        state = ViewerSession.Apply(state, CommandKind.SelectAll);
        await Assert.That(state.Selection).IsNull();
    }

    /// <summary>
    /// A selection made in the text is kept, and offered again once the text is back on screen.
    /// </summary>
    [Test]
    public async Task ASelectionWaitsOutThePictureView()
    {
        var state = Drawn(State(Left, Right, LeftText, RightText));
        state = ViewerSession.Drag(state, PaneSide.Left, 0, 0, 1, 5);
        await Assert.That(state.LiveSelection).IsNotNull();

        state = ViewerSession.Apply(state, CommandKind.ToggleDrawing);
        await Assert.That(state.LiveSelection).IsNull();

        state = ViewerSession.Apply(state, CommandKind.ToggleDrawing);
        await Assert.That(state.LiveSelection).IsNotNull();
    }

    /// <summary>
    /// Scrolling counts the rows the pane shows, which with the page under the text is half the
    /// body.
    /// </summary>
    [Test]
    public async Task TheScrollEndsWhereTheHalfPaneDoes()
    {
        var state = Drawn(State(Left, Right, Fixtures.Long(false), Fixtures.Long(true)));
        state = ViewerSession.Apply(state, CommandKind.ScrollEnd);
        var total = state.View!.Count;
        await Assert.That(state.ScrollTop).IsEqualTo(total - ScreenBuilder.BodyRows(state) / 2);
    }

    /// <summary>
    /// A render landing for bytes no entry holds any more - the file went, or a re-run rewrote it -
    /// is not kept for nothing.
    /// </summary>
    [Test]
    public async Task ARenderForBytesNotHeldIsDropped()
    {
        var state = State(Left, Right, LeftText, RightText);
        await Assert.That(ViewerSession.Rendered(state, "CC", Rendering.Started)).IsSameReferenceAs(state);
    }

    [Test]
    public async Task RendersGoWithTheirDocuments()
    {
        var state = Drawn(State(Left, Right, LeftText, RightText));
        await Assert.That(ViewerSession.Forget(state)).IsSameReferenceAs(state);

        var replaced = state with
        {
            Queue = [QueueEntry.ForFiles("a.txt", "b.txt", FileSide.OfText("a"), FileSide.OfText("b"))]
        };
        await Assert.That(ViewerSession.Forget(replaced).Renders).IsEmpty();
    }

    /// <summary>
    /// The text read, the entry is replaced by the one built with it - by the entry that was read,
    /// so one replaced in the meantime is left alone - and opened at its first change, on the page
    /// the reader is already on.
    /// </summary>
    [Test]
    public async Task ReadTextReplacesTheEntryItWasReadFor()
    {
        var reading = State(Left with { Reading = true }, Right with { Reading = true });
        reading = Drawn(reading);
        reading = ViewerSession.Apply(reading, CommandKind.NextPage);
        var seen = reading.Current!;
        var read = State(Left, Right, LeftText, RightText).Current!;

        var state = ViewerSession.TextRead(reading, seen, read);
        await Assert.That(state.Current).IsSameReferenceAs(read);
        await Assert.That(state.Page).IsEqualTo(2);

        await Assert.That(ViewerSession.TextRead(state, seen, read)).IsSameReferenceAs(state);
    }
}
