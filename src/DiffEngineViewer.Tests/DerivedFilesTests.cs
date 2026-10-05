/// <summary>
/// The files derived from a document, shown beneath it.
/// <para>
/// A snapshot library that splits a document into pages reports the document and every page file,
/// and a viewer drawing the document is already showing those pages. So an entry that says it was
/// derived from the document has no row of its own until asked for, and goes where the document
/// goes. Hidden is a view and never a filter, as a fold is: what is beneath a document is still
/// queued, still counted, and still taken by accept-all.
/// </para>
/// <para>
/// And only beneath a document this viewer is drawing, which most of what follows is about. Where
/// that does not hold, an entry that names a source is an ordinary row, which is what it always
/// was.
/// </para>
/// </summary>
public class DerivedFilesTests
{
    [Test]
    public async Task TheFilesDerivedFromADocumentHaveNoRows()
    {
        var state = Fixtures.DocumentWithDerived();

        await Assert.That(Labels(state)).IsEquivalentTo(
        [
            "+ Sample.Test (pdf) (5)",
            "Other.Test (txt)"
        ]);
        // Still queued, which is what the title's count and accept-all go by
        await Assert.That(state.Queue.Count).IsEqualTo(7);
        await Assert.That(ScreenBuilder.Build(state).PendingCount).IsEqualTo(7);
    }

    /// <summary>
    /// Directly after their document, and by name, whatever order they arrived in: a tray lists
    /// its files in its dictionary's order, and a page that arrives second is not the second page.
    /// </summary>
    [Test]
    public async Task TheyFollowTheirDocumentByName()
    {
        var state = Fixtures.DocumentWithDerived();

        await Assert.That(state.Queue.Select(_ => _.Name)).IsEquivalentTo(
            [
                "Sample.Test (pdf)",
                "Sample.Test (txt)",
                "Sample.Test#page_0001 (png)",
                "Sample.Test#page_0001 (txt)",
                "Sample.Test#page_0002 (png)",
                "Sample.Test#page_0003.verified.png",
                "Other.Test (txt)"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>
    /// Every change to the queue orders it again, so ordering an ordered queue has to leave it be.
    /// </summary>
    [Test]
    public async Task OrderingAnOrderedQueueChangesNothing()
    {
        var queue = Fixtures.DocumentWithDerived().Queue;

        var again = QueueProjection.Order(queue);

        await Assert.That(again.Select(_ => _.Key)).IsEquivalentTo(
            queue.Select(_ => _.Key),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>
    /// Unfolded, each is a row under the document saying what it adds to the document's name.
    /// </summary>
    [Test]
    public async Task UnfoldingGivesEachARowBeneathTheDocument()
    {
        var unfolded = ViewerSession.Apply(Fixtures.DocumentWithDerived(), CommandKind.ToggleDerived);

        await Assert.That(Labels(unfolded)).IsEquivalentTo(
            [
                "- Sample.Test (pdf) (5)",
                "  (txt)",
                "  #page_0001 (png)",
                "  #page_0001 (txt)",
                "  #page_0002 (png)",
                "  #page_0003.verified.png",
                "Other.Test (txt)"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task UnfoldingTwiceFoldsAgain()
    {
        var state = Fixtures.DocumentWithDerived();

        var round = ViewerSession.Apply(ViewerSession.Apply(state, CommandKind.ToggleDerived), CommandKind.ToggleDerived);

        await Assert.That(Labels(round)).IsEquivalentTo(Labels(state));
    }

    /// <summary>
    /// The document's row carries the marker a header does, so a click on it does what a click on
    /// a header does, once the document is the one on screen. The click that puts it there is a
    /// selection and nothing more: reading a document never unfolds it.
    /// </summary>
    [Test]
    public async Task AClickOnTheDocumentOnScreenUnfoldsIt()
    {
        var state = ViewerSession.Apply(Fixtures.DocumentWithDerived(), CommandKind.NextItem);
        await Assert.That(state.Current!.Name).IsEqualTo("Other.Test (txt)");

        var selected = Click(state, row: 0);
        await Assert.That(selected.Current!.Key).IsEqualTo(Fixtures.DocumentKey);
        await Assert.That(selected.Unfolded).IsEmpty();

        var unfolded = Click(selected, row: 0);
        await Assert.That(unfolded.Unfolded).Contains(Fixtures.DocumentKey);
        await Assert.That(unfolded.Current!.Key).IsEqualTo(Fixtures.DocumentKey);

        var folded = Click(unfolded, row: 0);
        await Assert.That(folded.Unfolded).IsEmpty();
    }

    /// <summary>
    /// A click with a menu open is the click that closes it, and one on a selected row with
    /// nothing beneath it is the selection it always was.
    /// </summary>
    [Test]
    public async Task AClickThatIsNotForTheFoldLeavesItAlone()
    {
        var state = Fixtures.DocumentWithDerived();

        var closed = Click(ViewerSession.OpenMenu(state, 0), row: 0);
        await Assert.That(closed.Menu).IsNull();
        await Assert.That(closed.Unfolded).IsEmpty();

        var other = ViewerSession.Apply(state, CommandKind.NextItem);
        var clicked = Click(other, row: 1);
        await Assert.That(clicked.Current!.Name).IsEqualTo("Other.Test (txt)");
        await Assert.That(clicked.Unfolded).IsEmpty();
    }

    static SessionState Click(SessionState state, int row) =>
        ViewerProgram.Apply(
            state,
            new(CommandKind.None, -1, row, 0, false, Fixtures.Columns, Fixtures.Rows),
            link: null,
            new DerivedAcceptTests.NoWindow());

    /// <summary>
    /// The document's menu says how many files its accept and its discard take, and offers the
    /// opposite of how they are shown, after the items every move has.
    /// </summary>
    [Test]
    public async Task TheDocumentsMenuCountsThemAndOffersToShowThem()
    {
        var state = Fixtures.DocumentWithDerived();

        var folded = ViewerSession.OpenMenu(state, 0);
        await Assert.That(folded.Menu!.Items.Take(4).Select(_ => _.Label)).IsEquivalentTo(
            ["Accept move +5", "Discard +5", "Open target directory", "Expand"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var unfolded = ViewerSession.OpenMenu(ViewerSession.Apply(folded, CommandKind.ToggleDerived), 0);
        await Assert.That(unfolded.Menu!.Items[3].Label).IsEqualTo("Collapse");
    }

    [Test]
    public async Task TheFooterCountsThemToo()
    {
        var buttons = ScreenBuilder.Build(Fixtures.DocumentWithDerived()).Buttons;

        await Assert.That(buttons[0].Label).IsEqualTo("Accept move +5");
        await Assert.That(buttons[1].Label).IsEqualTo("Discard +5");
    }

    /// <summary>
    /// An entry with nothing beneath it has the menu and the buttons it always had.
    /// </summary>
    [Test]
    public async Task AnEntryWithNothingBeneathItSaysNothingOfIt()
    {
        var state = ViewerSession.Apply(Fixtures.DocumentWithDerived(), CommandKind.NextItem);
        await Assert.That(state.Current!.Name).IsEqualTo("Other.Test (txt)");

        var opened = ViewerSession.OpenMenu(state, 1);

        await Assert.That(opened.Menu!.Items.Select(_ => _.Label)).DoesNotContain("Expand");
        await Assert.That(opened.Menu.Items[0].Label).IsEqualTo("Accept move");
        await Assert.That(ScreenBuilder.Build(state).Buttons[0].Label).IsEqualTo("Accept move");
        // And the command that shows them has nothing to show
        await Assert.That(ViewerSession.Apply(state, CommandKind.ToggleDerived)).IsSameReferenceAs(state);
    }

    /// <summary>
    /// Stepping through the queue steps over what has no row, as it steps over a fold.
    /// </summary>
    [Test]
    public async Task TabStepsOverThem()
    {
        var state = Fixtures.DocumentWithDerived();
        await Assert.That(state.Current!.Key).IsEqualTo(Fixtures.DocumentKey);

        var stepped = ViewerSession.Apply(state, CommandKind.NextItem);

        await Assert.That(stepped.Current!.Name).IsEqualTo("Other.Test (txt)");
    }

    /// <summary>
    /// And into them once they have rows.
    /// </summary>
    [Test]
    public async Task TabStepsIntoThemOnceUnfolded()
    {
        var unfolded = ViewerSession.Apply(Fixtures.DocumentWithDerived(), CommandKind.ToggleDerived);

        var stepped = ViewerSession.Apply(unfolded, CommandKind.NextItem);

        await Assert.That(stepped.Current!.Name).IsEqualTo("Sample.Test (txt)");
    }

    /// <summary>
    /// The tray, or a second process, asking for one of them by key. A selection nobody can see is
    /// not a selection, so the document is unfolded on the way.
    /// </summary>
    [Test]
    public async Task SelectingOneFromOutsideUnfoldsItsDocument()
    {
        var state = Fixtures.DocumentWithDerived();
        var page = state.Queue.Single(_ => _.Name == "Sample.Test#page_0002 (png)");

        var selected = ViewerSession.SelectKey(state, page.Key);

        await Assert.That(selected.Current!.Key).IsEqualTo(page.Key);
        await Assert.That(selected.Unfolded).Contains(Fixtures.DocumentKey);
        await Assert.That(QueueProjection.VisibleEntries(selected)).Contains(selected.Selected);
    }

    /// <summary>
    /// The same for a window attached to someone else's queue, whose listings can bring a
    /// document in after one of its files: a tray lists what it holds in no order.
    /// </summary>
    [Test]
    public async Task AListingThatAddsTheDocumentTakesTheSelectionToo()
    {
        var page = Fixtures.DerivedMove("#page_0001", "png");
        var state = Fixtures.Attached(InlineQueue.Empty, page);
        await Assert.That(state.Current!.Key).IsEqualTo(page.Key);

        var synced = ViewerSession.Sync(state, InlineQueue.Empty, [page, Fixtures.DocumentMove()], null);

        await Assert.That(synced.Current!.Key).IsEqualTo(Fixtures.DocumentKey);
        await Assert.That(Labels(synced)).IsEquivalentTo(["+ Sample.Test (pdf) (1)"]);
    }

    /// <summary>
    /// A page that arrives ahead of its document is the only thing in the queue, so it is what is
    /// read. The document arriving puts it beneath the document, and the document is then what is
    /// read: the same change, from the entry that stands for it.
    /// </summary>
    [Test]
    public async Task ADocumentArrivingAfterItsFileTakesTheSelection()
    {
        var page = Fixtures.DerivedMove("#page_0001", "png");
        var state = ViewerSession.EnqueueTracked(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows), page);
        await Assert.That(state.Current!.Key).IsEqualTo(page.Key);
        await Assert.That(Labels(state)).IsEquivalentTo(["Sample.Test#page_0001 (png)"]);

        var arrived = ViewerSession.EnqueueTracked(state, Fixtures.DocumentMove());

        await Assert.That(arrived.Current!.Key).IsEqualTo(Fixtures.DocumentKey);
        await Assert.That(Labels(arrived)).IsEquivalentTo(["+ Sample.Test (pdf) (1)"]);
    }

    /// <summary>
    /// A file arriving beneath the document on screen changes its count and nothing about what is
    /// being read: not the scroll, and not the page turned to.
    /// </summary>
    [Test]
    public async Task AFileArrivingBeneathTheDocumentOnScreenLeavesTheReaderWhereTheyWere()
    {
        var state = ViewerSession.EnqueueTracked(
            SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows),
            Fixtures.DocumentMove());
        var reading = ViewerSession.Apply(state, CommandKind.ScrollDown) with { Page = 3 };

        var arrived = ViewerSession.EnqueueTracked(reading, Fixtures.DerivedMove("#page_0001", "png"));

        await Assert.That(arrived.Current!.Key).IsEqualTo(Fixtures.DocumentKey);
        await Assert.That(arrived.ScrollTop).IsEqualTo(reading.ScrollTop);
        await Assert.That(arrived.Page).IsEqualTo(3);
        await Assert.That(Labels(arrived)).IsEquivalentTo(["+ Sample.Test (pdf) (1)"]);
    }

    /// <summary>
    /// The document gone - accepted somewhere else, or settled by a run in which it stopped
    /// differing - leaves what was derived from it standing on its own, as the rows they are.
    /// </summary>
    [Test]
    public async Task WithTheDocumentGoneTheyAreOrdinaryRows()
    {
        var settled = ViewerSession.Settle(Fixtures.DocumentWithDerived(), Fixtures.DocumentKey);

        await Assert.That(Labels(settled)).IsEquivalentTo(
            [
                "Sample.Test (txt)",
                "Sample.Test#page_0001 (png)",
                "Sample.Test#page_0001 (txt)",
                "Sample.Test#page_0002 (png)",
                "Sample.Test#page_0003.verified.png",
                "Other.Test (txt)"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(QueueProjection.VisibleEntries(settled)).Contains(settled.Selected);
    }

    /// <summary>
    /// A source that is not a document this viewer draws. A text file with a picture taken of it,
    /// say: the picture is what there is to look at, and beneath the text it would never be seen.
    /// </summary>
    [Test]
    public async Task AFileDerivedFromSomethingThatIsNotDrawnIsAnOrdinaryRow()
    {
        var html = Fixtures.DerivedMove("", "html", sourceKey: null);
        var screenshot = Fixtures.DerivedMove("", "png", sourceKey: html.Key);

        var state = Queue(html, screenshot);

        await Assert.That(Labels(state)).IsEquivalentTo(["Sample.Test (html)", "Sample.Test (png)"]);
        await Assert.That(ScreenBuilder.Build(state).Buttons[0].Label).IsEqualTo("Accept move");
    }

    /// <summary>
    /// One level, which is what a sender says: it names the outermost source that is pending. A
    /// file naming one that is itself derived is not put beneath it.
    /// </summary>
    [Test]
    public async Task AFileDerivedFromADerivedFileIsAnOrdinaryRow()
    {
        var page = Fixtures.DerivedMove("#page_0001", "png");
        var ofPage = Fixtures.DerivedMove("#page_0001", "txt", sourceKey: page.Key);

        var state = Queue(Fixtures.DocumentMove(), page, ofPage);

        await Assert.That(Labels(state)).IsEquivalentTo(["+ Sample.Test (pdf) (1)", "Sample.Test#page_0001 (txt)"]);
    }

    /// <summary>
    /// A solution's entries are one run of the queue, so a row beneath another has to be in the
    /// same run. A sender never splits a document from its pages across two; this is what happens
    /// if something does.
    /// </summary>
    [Test]
    public async Task AFileInAnotherSolutionIsAnOrdinaryRow()
    {
        var state = Queue(
            Fixtures.DocumentMove("SolutionA"),
            Fixtures.DerivedMove("#page_0001", "png", "SolutionB"));

        await Assert.That(Labels(state)).IsEquivalentTo(
            [
                "- SolutionA (1)",
                "  Sample.Test (pdf)",
                "- SolutionB (1)",
                "  Sample.Test#page_0001 (png)"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>
    /// Beneath a solution's header the document is indented as its entries are, and its files
    /// once more.
    /// </summary>
    [Test]
    public async Task UnderASolutionHeaderTheyAreIndentedOnceMore()
    {
        var state = Queue(
            Fixtures.DocumentMove("SolutionA"),
            Fixtures.DerivedMove("#page_0001", "png", "SolutionA"),
            Fixtures.Move(solution: "SolutionB"));

        var unfolded = ViewerSession.Apply(state, CommandKind.ToggleDerived);

        await Assert.That(Labels(unfolded)).IsEquivalentTo(
            [
                "- SolutionA (2)",
                "  - Sample.Test (pdf) (1)",
                "    #page_0001 (png)",
                "- SolutionB (1)",
                "  Sample.Test (txt)"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>
    /// A file that failed has no row to carry the mark while it is hidden, so the document's row
    /// carries it, and its own failure first where it has one. Unfolded, each answers for itself.
    /// </summary>
    [Test]
    public async Task ADocumentsRowAnswersForAFailureBeneathIt()
    {
        var state = Fixtures.DocumentWithDerived();
        var failed = state with
        {
            Queue =
            [
                .. state.Queue
                    .Select(_ => _.Name == "Sample.Test#page_0002 (png)" ? _ with {Status = "the file is locked"} : _)
            ]
        };

        await Assert.That(QueueProjection.Rows(failed)[0].Status).IsEqualTo("the file is locked");

        var unfolded = QueueProjection.Rows(ViewerSession.Apply(failed, CommandKind.ToggleDerived));
        await Assert.That(unfolded[0].Status).IsNull();
        await Assert.That(unfolded.Single(_ => _.Label == "  #page_0002 (png)").Status).IsEqualTo("the file is locked");
    }

    /// <summary>
    /// The one that would be silent and destructive if it were wrong: accept all takes what has
    /// no row, as it takes what a fold hides.
    /// </summary>
    [Test]
    public async Task AcceptAllTakesThem()
    {
        var done = new List<string>();

        var accepted = ViewerSession.Apply(Fixtures.DocumentWithDerived(), CommandKind.AcceptAll, Recording(done));

        await Assert.That(accepted.Queue).IsEmpty();
        // The document after what is beneath it, so it is the last of them to leave
        await Assert.That(done).IsEquivalentTo(
            [
                "move Sample.Test.received.txt",
                "move Sample.Test#page_0001.received.png",
                "move Sample.Test#page_0001.received.txt",
                "move Sample.Test#page_0002.received.png",
                "delete Sample.Test#page_0003.verified.png",
                "move Sample.Test.received.pdf",
                "move sample.received.txt"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>
    /// What was said of a file is carried by every way an entry is built again: the watch reading
    /// its files again, and the pair arriving again with nothing said this time.
    /// </summary>
    [Test]
    public async Task WhatAFileWasDerivedFromSurvivesBeingReadAgain()
    {
        var directory = Directory.CreateTempSubdirectory("deview-derived-").FullName;
        try
        {
            var temp = Path.Combine(directory, "Sample.Test#page_0001.received.txt");
            var target = Path.Combine(directory, "Sample.Test#page_0001.verified.txt");
            var stale = Path.Combine(directory, "Sample.Test#page_0002.verified.txt");
            var document = Path.Combine(directory, "Sample.Test.received.pdf");
            await File.WriteAllTextAsync(temp, "received");
            await File.WriteAllTextAsync(target, "verified");
            await File.WriteAllTextAsync(stale, "stale");
            var sourceKey = TrackedKeys.ForMove(document);

            var move = TrackedEntry.ForMove(temp, target, source: document);
            var delete = TrackedEntry.ForDelete(stale, source: document);
            await Assert.That(move.SourceKey).IsEqualTo(sourceKey);
            await Assert.That(delete.SourceKey).IsEqualTo(sourceKey);

            // Unchanged, which is the entry it was with new stamps
            await Assert.That(TrackedEntry.MoveAgain(move, temp, target).SourceKey).IsEqualTo(sourceKey);
            await Assert.That(TrackedEntry.DeleteAgain(delete, stale).SourceKey).IsEqualTo(sourceKey);

            // Changed, which is an entry built from the files
            await File.WriteAllTextAsync(temp, "what a later run received");
            await File.WriteAllTextAsync(stale, "and what it found here");
            await Assert.That(TrackedEntry.MoveAgain(move, temp, target).SourceKey).IsEqualTo(sourceKey);
            await Assert.That(TrackedEntry.DeleteAgain(delete, stale).SourceKey).IsEqualTo(sourceKey);

            // And an arrival that names another says so
            var other = Path.Combine(directory, "Other.Test.received.pdf");
            await Assert.That(TrackedEntry.MoveAgain(move, temp, target, source: other).SourceKey).IsEqualTo(TrackedKeys.ForMove(other));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Over the wire, into a queue this process owns: what the sender said the file was derived
    /// from is on the entry, and on the listing another window is shown the queue through.
    /// </summary>
    [Test]
    public async Task AnOwnerTakesWhatAFileWasDerivedFromOffTheWireAndListsIt()
    {
        var directory = Directory.CreateTempSubdirectory("deview-derived-").FullName;
        try
        {
            var document = Path.Combine(directory, "Sample.Test.received.pdf");
            var page = Path.Combine(directory, "Sample.Test#page_0001.received.png");
            var stale = Path.Combine(directory, "Sample.Test#page_0002.verified.png");
            await File.WriteAllTextAsync(stale, "stale");
            var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));
            var handler = new MessageHandler(host, Fixtures.Applied, _ => { });

            handler.Handle(new(ViewerVerb.Move, page, Path.Combine(directory, "Sample.Test#page_0001.verified.png"))
            {
                Source = document
            });
            handler.Handle(new(ViewerVerb.Delete, stale)
            {
                Source = document
            });

            var sourceKey = TrackedKeys.ForMove(document);
            await Assert.That(host.State.Queue.Select(_ => _.SourceKey))
                .IsEquivalentTo<IEnumerable<string?>, string?>([sourceKey, sourceKey]);
            var listing = handler.Handle(new(ViewerVerb.ListFull));
            await Assert.That(ViewerResponse.TryParse(listing.Build(), out var parsed)).IsTrue();
            await Assert.That(parsed!.Moves.Single().SourceKey).IsEqualTo(sourceKey);
            await Assert.That(parsed.Deletes.Single().SourceKey).IsEqualTo(sourceKey);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Two documents pending at once, as two attachments of a mail are. Each has what was derived
    /// from it beneath it and nothing of the other's, whatever order the files arrived in, and
    /// each accept counts its own.
    /// </summary>
    [Test]
    public async Task EachDocumentHasOnlyItsOwnFilesBeneathIt()
    {
        var pdf = Document("Mail#Attachment1", "pdf", DocumentFormat.Pdf);
        var docx = Document("Mail#Attachment2", "docx", DocumentFormat.Word);

        var state = Queue(
            pdf,
            docx,
            DerivedOf(docx, "Mail#Attachment2.page_0001", "png"),
            DerivedOf(pdf, "Mail#Attachment1.page_0002", "png"),
            DerivedOf(pdf, "Mail#Attachment1.page_0002", "txt"));

        await Assert.That(Labels(state)).IsEquivalentTo(
            [
                "+ Mail#Attachment1 (pdf) (2)",
                "+ Mail#Attachment2 (docx) (1)"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(ScreenBuilder.Build(ViewerSession.SelectKey(state, pdf.Key)).Buttons[0].Label).IsEqualTo("Accept move +2");
        await Assert.That(ScreenBuilder.Build(ViewerSession.SelectKey(state, docx.Key)).Buttons[0].Label).IsEqualTo("Accept move +1");
    }

    /// <summary>
    /// A file whose name carries on from its document's with a dot rather than a hash, as the page
    /// of an attachment does, says what it adds to that name the same way.
    /// </summary>
    [Test]
    public async Task ANameThatCarriesOnWithADotSaysWhatItAddsToo()
    {
        var pdf = Document("Mail#Attachment1", "pdf", DocumentFormat.Pdf);
        var state = Queue(
            pdf,
            DerivedOf(pdf, "Mail#Attachment1.page_0002", "png"),
            DerivedOf(pdf, "Mail#Attachment1.page_0002", "txt"));

        var unfolded = ViewerSession.Apply(state, CommandKind.ToggleDerived);

        await Assert.That(Labels(unfolded)).IsEquivalentTo(
            [
                "- Mail#Attachment1 (pdf) (2)",
                "  .page_0002 (png)",
                "  .page_0002 (txt)"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>
    /// A document rendered to another: a Word file saved as a PDF, both pending, with the PDF and
    /// its page naming the Word file. The PDF is beneath it as the page is, and is still a
    /// document when it is the one being read, with nothing beneath it of its own.
    /// </summary>
    [Test]
    public async Task ADocumentBeneathAnotherIsStillReadAsOne()
    {
        var docx = Document("Letter", "docx", DocumentFormat.Word);
        var pdf = Document("Letter", "pdf", DocumentFormat.Pdf, docx.Key);

        var state = Queue(docx, DerivedOf(docx, "Letter#page_0001", "png"), pdf);

        await Assert.That(Labels(state)).IsEquivalentTo(["+ Letter (docx) (2)"]);

        var reading = ViewerSession.SelectKey(state, pdf.Key);

        await Assert.That(Labels(reading)).IsEquivalentTo(
            [
                "- Letter (docx) (2)",
                "  (pdf)",
                "  #page_0001 (png)"
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(reading.Current!.Key).IsEqualTo(pdf.Key);
        var buttons = ScreenBuilder.Build(reading).Buttons;
        await Assert.That(buttons[0].Label).IsEqualTo("Accept move");
        await Assert.That(buttons.Select(_ => _.Label)).Contains("Next page");
    }

    /// <summary>
    /// A document under a name of its own, for a queue that holds more than the one
    /// <see cref="Fixtures.DocumentMove"/> builds.
    /// </summary>
    static QueueEntry Document(string name, string extension, DocumentFormat format, string? sourceKey = null)
    {
        var temp = $"temp/{name}.received.{extension}";
        var target = $"code/{name}.verified.{extension}";
        return QueueEntry.ForMove(
            $"move:{temp}",
            $"{name} ({extension})",
            null,
            sourceKey,
            temp,
            target,
            new(Fixtures.Long(false), null, null, null, new DocumentFile(temp, 1_234, format, $"{name}.{extension}.received")),
            new(Fixtures.Long(true), null, null, null, new DocumentFile(target, 1_240, format, $"{name}.{extension}.verified")));
    }

    static QueueEntry DerivedOf(QueueEntry source, string name, string extension) =>
        QueueEntry.ForMove(
            $"move:temp/{name}.received.{extension}",
            $"{name} ({extension})",
            null,
            source.Key,
            $"temp/{name}.received.{extension}",
            $"code/{name}.verified.{extension}",
            FileSide.OfText(Fixtures.Received),
            FileSide.OfText(Fixtures.Expected));

    static SessionState Queue(params QueueEntry[] entries)
    {
        var state = SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows);
        foreach (var entry in entries)
        {
            state = ViewerSession.EnqueueTracked(state, entry);
        }

        return state;
    }

    static List<string> Labels(SessionState state) =>
    [
        .. QueueProjection.Rows(state)
            .Select(_ => _.Label)
    ];

    /// <summary>
    /// Actions that write down what they were asked to do to which file, by its name.
    /// </summary>
    internal static ViewerActions Recording(List<string> done) =>
        Fixtures.Applied with
        {
            MoveFile = (temp, _) => done.Add($"move {Path.GetFileName(temp)}"),
            DeleteFile = file => done.Add($"delete {Path.GetFileName(file)}")
        };
}
