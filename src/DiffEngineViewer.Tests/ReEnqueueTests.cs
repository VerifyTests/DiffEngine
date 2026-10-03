/// <summary>
/// A patch arriving for the entry already on screen. A continuous test runner sends one every few
/// seconds for as long as the test keeps failing, and most of them say exactly what the last one
/// said.
/// </summary>
public class ReEnqueueTests
{
    [Test]
    public async Task An_identical_re_send_leaves_the_scroll_alone()
    {
        var state = Scrolled();

        var again = ViewerSession.EnqueueInline(state, Patch(Fixtures.Deep(true)));

        await Assert.That(again.Queue[0]).IsSameReferenceAs(state.Queue[0]);
        await Assert.That(again.ScrollTop).IsEqualTo(state.ScrollTop);
    }

    /// <summary>
    /// A re-send that says something else is a new comparison, and that one does start again, at
    /// its first change.
    /// </summary>
    [Test]
    public async Task A_re_send_of_different_content_starts_at_its_first_change()
    {
        var state = Scrolled();

        var again = ViewerSession.EnqueueInline(state, Patch($"{Fixtures.Deep(true)}\nand one more line"));

        await Assert.That(again.ScrollTop).IsEqualTo(26);
    }

    /// <summary>
    /// Scrolled away from where the entry opened, so neither staying put nor starting again can
    /// pass by coincidence.
    /// </summary>
    static SessionState Scrolled()
    {
        var state = ViewerSession.Apply(Fixtures.Inline(Patch(Fixtures.Deep(true))), CommandKind.PageDown);
        if (state.ScrollTop == 26)
        {
            throw new("The entry did not scroll, so nothing below asserts anything.");
        }

        return state;
    }

    /// <summary>
    /// The same for a pair of files. A test that keeps failing the same way sends its pair on every
    /// run, built again from files that hold what they held, and each one used to open the entry
    /// afresh: back at its first change, with the menu closed.
    /// </summary>
    [Test]
    public async Task An_identical_pair_arriving_again_leaves_the_reader_alone()
    {
        var state = ViewerSession.OpenMenu(ScrolledPair(), 0);
        await Assert.That(state.Menu).IsNotNull();

        var again = ViewerSession.EnqueueTracked(state, Pair(Fixtures.Deep(true), written: 2));

        await Assert.That(again.ScrollTop).IsEqualTo(state.ScrollTop);
        await Assert.That(again.Menu).IsSameReferenceAs(state.Menu);
        await Assert.That(again.Queue).HasSingleItem();
        // The run rewrote the file, and the entry has to say so or the watch reads it a third time
        await Assert.That(again.Queue[0].LeftStamp).IsEqualTo(new FileStamp(2, 1));
    }

    [Test]
    public async Task A_pair_arriving_again_with_different_content_starts_at_its_first_change()
    {
        var state = ViewerSession.OpenMenu(ScrolledPair(), 0);

        var again = ViewerSession.EnqueueTracked(state, Pair($"{Fixtures.Deep(true)}\nand one more line", written: 2));

        await Assert.That(again.ScrollTop).IsEqualTo(26);
        // Opened over the entry that was there, which this one is not
        await Assert.That(again.Menu).IsNull();
    }

    /// <summary>
    /// Kept as it reads, but not as the object it was. <see cref="TrackedWatch"/> applies what a
    /// pass found by reference, and a pass that looked while the run had cleared its received file
    /// found it gone: applied to the pair the run then staged again, that dropped an entry whose
    /// file was there.
    /// </summary>
    [Test]
    public async Task A_pass_that_found_the_file_gone_does_not_take_the_pair_staged_again()
    {
        var state = ScrolledPair();
        var seen = state.Queue[0];

        var again = ViewerSession.EnqueueTracked(state, Pair(Fixtures.Deep(true), written: 2));
        var refreshed = ViewerSession.Refresh(again, [seen], []);

        await Assert.That(refreshed.Queue).HasSingleItem();
        await Assert.That(refreshed.ScrollTop).IsEqualTo(state.ScrollTop);
    }

    /// <summary>
    /// A document's text is read after it arrives, so a pair arriving again has less in it than the
    /// entry already read. That entry stays, on the page and at the zoom it was being looked at.
    /// </summary>
    [Test]
    public async Task A_document_arriving_again_keeps_its_text_its_page_and_its_zoom()
    {
        var state = ViewerSession.EnqueueTracked(
            SessionState.Start(ViewerMode.Inline, 210, Fixtures.Rows),
            Document(read: true));
        state = DocumentScreenTests.Drawn(state);
        state = ViewerSession.Apply(state, CommandKind.NextPage);
        state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        await Assert.That(state.Page).IsEqualTo(2);
        await Assert.That(state.Zoom).IsEqualTo(1);

        var again = ViewerSession.EnqueueTracked(state, Document(read: false));

        await Assert.That(again.Current!.HasText).IsTrue();
        await Assert.That(again.Current.LeftText).IsEqualTo(DocumentScreenTests.LeftText);
        await Assert.That(again.Page).IsEqualTo(2);
        await Assert.That(again.Zoom).IsEqualTo(1);
    }

    /// <summary>
    /// The same file holding other bytes is another document, however far along the old one was.
    /// </summary>
    [Test]
    public async Task A_document_rewritten_with_other_bytes_is_opened_afresh()
    {
        var state = ViewerSession.EnqueueTracked(
            SessionState.Start(ViewerMode.Inline, 210, Fixtures.Rows),
            Document(read: true));
        state = ViewerSession.Apply(DocumentScreenTests.Drawn(state), CommandKind.NextPage);

        var again = ViewerSession.EnqueueTracked(state, Document(read: false, leftHash: "CC"));

        await Assert.That(again.Current!.HasText).IsFalse();
        await Assert.That(again.Page).IsNull();
    }

    /// <summary>
    /// A tracked pair of sixty lines, scrolled away from where it opened, as <see cref="Scrolled"/>
    /// is for a patch.
    /// </summary>
    static SessionState ScrolledPair()
    {
        var opened = ViewerSession.EnqueueTracked(
            SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows),
            Pair(Fixtures.Deep(true), written: 1));
        var state = ViewerSession.Apply(opened, CommandKind.PageDown);
        if (opened.ScrollTop != 26 ||
            state.ScrollTop == 26)
        {
            throw new("The entry did not open at its first change and then scroll, so nothing below asserts anything.");
        }

        return state;
    }

    static QueueEntry Pair(string received, long written) =>
        QueueEntry.ForMove(
            "move:temp/sample.received.txt",
            "Sample.Test (txt)",
            null,
            "temp/sample.received.txt",
            "code/sample.verified.txt",
            new(received, new FileStamp(written, 1), null, null),
            new(Fixtures.Deep(false), new FileStamp(1, 1), null, null));

    /// <summary>
    /// A PDF pair as it arrives, hashed and with its text still to read, or as it is once
    /// <see cref="DocumentWatch"/> has read it.
    /// </summary>
    static QueueEntry Document(bool read, string leftHash = "AA")
    {
        var left = DocumentScreenTests.Left with { Hash = leftHash, Reading = !read };
        var right = DocumentScreenTests.Right with { Reading = !read };
        return QueueEntry.ForMove(
            "move:temp/sample.received.pdf",
            "Sample.Test (pdf)",
            null,
            "temp/sample.received.pdf",
            "code/sample.verified.pdf",
            new(read ? DocumentScreenTests.LeftText : "", new FileStamp(1, 1), null, null, left),
            new(read ? DocumentScreenTests.RightText : "", new FileStamp(1, 1), null, null, right));
    }

    static InlinePatch Patch(string content) =>
        Fixtures.Patch("A.cs", 1, Fixtures.Literal(Fixtures.Deep(false)), content);

    static SessionState ThreeVariants() =>
        Fixtures.Inline(
            Fixtures.Patch(content: "eight", framework: "net8.0"),
            Fixtures.Patch(content: "nine", framework: "net9.0"),
            Fixtures.Patch(content: "ten", framework: "net10.0"));

    /// <summary>
    /// net8.0 starts passing, so its variant goes. The reader had cycled to net9.0's, which is
    /// still there: kept by index, the screen switched to net10.0's with nothing to say so, and
    /// Accept would have applied that.
    /// </summary>
    [Test]
    public async Task A_settle_of_an_earlier_variant_keeps_the_variant_on_screen()
    {
        var onNine = ViewerSession.Apply(ThreeVariants(), CommandKind.NextVariant);
        await Assert.That(onNine.Current!.LeftHeader).IsEqualTo("received (net9.0)");

        var settled = ViewerSession.Settle(onNine, onNine.Current.Key, "net8.0");

        await Assert.That(settled.Current!.LeftHeader).IsEqualTo("received (net9.0)");
    }

    /// <summary>
    /// net8.0 re-runs and now agrees with net10.0: its variant merges into that one. Nothing
    /// happened to the net9.0 variant the reader is on.
    /// </summary>
    [Test]
    public async Task A_rerun_that_merges_an_earlier_variant_keeps_the_variant_on_screen()
    {
        var onNine = ViewerSession.Apply(ThreeVariants(), CommandKind.NextVariant);

        var merged = ViewerSession.EnqueueInline(onNine, Fixtures.Patch(content: "ten", framework: "net8.0"));

        await Assert.That(merged.Current!.LeftHeader).IsEqualTo("received (net9.0)");
    }
}
