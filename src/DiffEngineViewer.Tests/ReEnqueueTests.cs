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
