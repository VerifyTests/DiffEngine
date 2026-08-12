/// <summary>
/// The context menu this head builds for each kind of queue row.
/// <para>
/// Rendered without a window: Verify.WinForms hosts the strip in a throwaway form, sets TopLevel
/// false and asks it to draw itself, so nothing pops up and nothing steals focus. That is also why
/// this replaced a WindowsPixelTests case — a real popup is a top level window, and the capture
/// there photographs the client area, which cannot contain one.
/// </para>
/// <para>
/// The baseline does not move with the OS theme: <see cref="MenuRenderer" /> is assigned per strip,
/// so it beats the system dark mode renderer and every pixel here is ours.
/// </para>
/// </summary>
[NotInParallel]
[TUnit.Core.Executors.STAThreadExecutor]
public class ContextMenuTests
{
    /// <summary>
    /// The grid the other Windows captures are pinned to, so a menu is measured against the same
    /// screen they describe.
    /// </summary>
    const int columns = 122;

    const int rows = 37;

    [Test]
    public Task Entry() =>
        Verify(Build(Fixtures.GroupedConflicted(), 5));

    [Test]
    public Task SolutionHeader() =>
        Verify(Build(Fixtures.GroupedConflicted(), 0));

    [Test]
    public Task MoveEntry() =>
        Verify(Build(Fixtures.Attached(InlineQueue.Empty, Fixtures.Move(), Fixtures.Delete()), 0));

    /// <summary>
    /// The strip has to keep its own renderer, or it falls back to the manager's and lands light
    /// on the dark grid. Asserted rather than left to the images, because a capture of a strip
    /// drawn as a child does not prove what a real popup does.
    /// </summary>
    [Test]
    public async Task TheStripKeepsItsOwnRenderer()
    {
        using var strip = ViewerMenu.Create();

        await Assert.That(strip.RenderMode).IsEqualTo(ToolStripRenderMode.Custom);
        await Assert.That(strip.Renderer).IsTypeOf<MenuRenderer>();
    }

    static ContextMenuStrip Build(SessionState state, int row)
    {
        var opened = ViewerSession.Resize(ViewerSession.OpenMenu(state, row), columns, rows);
        return ViewerMenu.Build(ScreenBuilder.Build(opened).Menu!);
    }
}
