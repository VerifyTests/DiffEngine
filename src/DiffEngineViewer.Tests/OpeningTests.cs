/// <summary>
/// Where an entry is scrolled to when it comes on screen: its first change, with three rows above
/// it, rather than its first line. <see cref="Fixtures.Deep"/> changes line 30 of 60, row 29, so
/// that is row 26 - well past the sixteen body rows a top of zero would show.
/// <para>
/// Every way an entry arrives is here, because each used to reset the scroll on its own and one
/// left out would be the one entry a reader still has to go looking through.
/// </para>
/// </summary>
public class OpeningTests
{
    [Test]
    public async Task A_file_comparison()
    {
        var state = Fixtures.File(Fixtures.Deep(true), Fixtures.Deep(false));

        await Assert.That(state.ScrollTop).IsEqualTo(26);
    }

    [Test]
    public async Task An_inline_snapshot()
    {
        var state = Fixtures.Inline(Patch("A.cs", 1));

        await Assert.That(state.ScrollTop).IsEqualTo(26);
    }

    /// <summary>
    /// A pair arriving in an empty owning viewer, which is how a failing file snapshot reaches one.
    /// </summary>
    [Test]
    public async Task A_tracked_pair()
    {
        var state = ViewerSession.EnqueueTracked(
            SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows),
            Fixtures.Move(left: Fixtures.Deep(true), right: Fixtures.Deep(false)));

        await Assert.That(state.ScrollTop).IsEqualTo(26);
    }

    /// <summary>
    /// An attached viewer's first listing from the owner.
    /// </summary>
    [Test]
    public async Task A_queue_read_from_its_owner()
    {
        var state = Fixtures.Attached(Fixtures.Pending(Patch("A.cs", 1)));

        await Assert.That(state.ScrollTop).IsEqualTo(26);
    }

    [Test]
    public async Task Stepping_to_the_next_entry()
    {
        var state = Fixtures.Inline(Fixtures.Patch(), Patch("B.cs", 2));

        var stepped = ViewerSession.Apply(state, CommandKind.NextItem);

        await Assert.That(stepped.Current!.Name).IsEqualTo("B.cs:2");
        await Assert.That(stepped.ScrollTop).IsEqualTo(26);
    }

    /// <summary>
    /// A variant is different text, so it is met the way a different entry is.
    /// </summary>
    [Test]
    public async Task Cycling_to_another_variant()
    {
        var shifted = Fixtures.Deep(true).Replace("line 50 changed", "line 50");
        var state = Fixtures.Inline(
            Patch("A.cs", 1, Fixtures.Deep(true), "net8.0"),
            Patch("A.cs", 1, $"line 01 changed{shifted[7..]}", "net9.0"));
        var scrolled = ViewerSession.Apply(state, CommandKind.PageDown);

        var cycled = ViewerSession.Apply(scrolled, CommandKind.NextVariant);

        await Assert.That(cycled.Current!.SelectedVariant).IsEqualTo(1);
        // That variant's first change is line 1, which has no rows above it to show.
        await Assert.That(cycled.ScrollTop).IsEqualTo(0);
    }

    [Test]
    public async Task Nothing_changed_opens_at_the_top()
    {
        var state = Fixtures.File(Fixtures.Deep(false), Fixtures.Deep(false));

        await Assert.That(state.ScrollTop).IsEqualTo(0);
    }

    /// <summary>
    /// A first change already on the first page, with fewer than three rows above it, leaves the
    /// view where it was: there is nothing above it to scroll off.
    /// </summary>
    [Test]
    public async Task A_change_near_the_top_opens_at_the_top()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));

        await Assert.That(state.ScrollTop).IsEqualTo(0);
    }

    /// <summary>
    /// A first change on the last line opens on the last page, like any scroll that far.
    /// </summary>
    [Test]
    public async Task A_change_on_the_last_line_opens_on_the_last_page()
    {
        var state = Fixtures.File($"{Fixtures.Deep(false)}!", Fixtures.Deep(false));

        await Assert.That(state.ScrollTop).IsEqualTo(60 - ScreenBuilder.BodyRows(state));
    }

    static InlinePatch Patch(string source, int line, string? content = null, string? framework = null) =>
        Fixtures.Patch(
            source,
            line,
            Fixtures.Literal(Fixtures.Deep(false)),
            content ?? Fixtures.Deep(true),
            framework: framework);
}
