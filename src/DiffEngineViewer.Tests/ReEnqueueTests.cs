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

        var again = ViewerSession.EnqueueInline(state, Patch(Fixtures.Long(true)));

        await Assert.That(again.Queue[0]).IsSameReferenceAs(state.Queue[0]);
        await Assert.That(again.ScrollTop).IsEqualTo(state.ScrollTop);
    }

    /// <summary>
    /// A re-send that says something else is a new comparison, and that one does start at the top.
    /// </summary>
    [Test]
    public async Task A_re_send_of_different_content_starts_at_the_top()
    {
        var state = Scrolled();

        var again = ViewerSession.EnqueueInline(state, Patch($"{Fixtures.Long(true)}\nand one more line"));

        await Assert.That(again.ScrollTop).IsEqualTo(0);
    }

    static SessionState Scrolled()
    {
        var state = ViewerSession.Apply(Fixtures.Inline(Patch(Fixtures.Long(true))), CommandKind.PageDown);
        if (state.ScrollTop == 0)
        {
            throw new("The entry did not scroll, so nothing below asserts anything.");
        }

        return state;
    }

    static InlinePatch Patch(string content) =>
        Fixtures.Patch("A.cs", 1, null, content);
}
