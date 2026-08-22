/// <summary>
/// Selecting the entry that is already selected. A left click on the highlighted row, a right
/// click to open its menu, and a focus naming it all arrive as a selection, and the reader is part
/// way down a comparison while they do.
/// </summary>
public class ReselectTests
{
    [Test]
    public async Task Opening_the_menu_on_the_entry_being_read_keeps_the_scroll()
    {
        var state = Scrolled();
        var row = VisibleRowOf(state, state.Selected);

        var opened = ViewerSession.OpenMenu(state, row);

        await Assert.That(opened.Menu).IsNotNull();
        await Assert.That(opened.ScrollTop).IsEqualTo(state.ScrollTop);
    }

    [Test]
    public async Task Focusing_the_entry_being_read_keeps_the_scroll()
    {
        var state = Scrolled();

        var focused = ViewerSession.SelectKey(state, state.Current!.Key);

        await Assert.That(focused.ScrollTop).IsEqualTo(state.ScrollTop);
    }

    /// <summary>
    /// A different entry is a different comparison, so that one does start at the top.
    /// </summary>
    [Test]
    public async Task Selecting_another_entry_starts_at_its_top()
    {
        var state = Scrolled();

        var selected = ViewerSession.SelectKey(state, state.Queue[0].Key);

        await Assert.That(selected.Current!.Key).IsEqualTo(state.Queue[0].Key);
        await Assert.That(selected.ScrollTop).IsEqualTo(0);
    }

    static SessionState Scrolled()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch("A.cs", 1, null, Fixtures.Long(true)),
            Fixtures.Patch("B.cs", 2, null, Fixtures.Long(true)));
        state = ViewerSession.SelectKey(state, state.Queue[1].Key);
        state = ViewerSession.Apply(state, CommandKind.PageDown);
        if (state.ScrollTop == 0)
        {
            throw new("The entry did not scroll, so nothing below asserts anything.");
        }

        return state;
    }

    static int VisibleRowOf(SessionState state, int entry)
    {
        var visible = QueueProjection.Visible(state, ScreenBuilder.BodyRows(state), out _);
        for (var index = 0; index < visible.Count; index++)
        {
            if (visible[index].Kind == QueueRowKind.Entry &&
                visible[index].EntryIndex == entry)
            {
                return index;
            }
        }

        throw new($"Entry {entry} is not on screen.");
    }
}
