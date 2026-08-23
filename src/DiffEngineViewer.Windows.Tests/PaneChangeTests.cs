/// <summary>
/// Whether two panes are the same pane, which is what decides whether the window repaints.
/// ScreenBuilder allocates a fresh Screen every frame, so record equality would report a change
/// sixty times a second and the comparison is by hand.
/// </summary>
public class PaneChangeTests
{
    /// <summary>
    /// The rows an image side shows are format, dimensions and byte count, and a re-run that
    /// rewrites a received image at the same size changes none of them - for BMP, which is
    /// uncompressed, that is every re-run. So nothing about the screen differed, Apply returned
    /// before repainting, and the pane kept the previous picture while the rows beside it
    /// described the new one.
    /// </summary>
    [Test]
    public async Task A_picture_that_changed_is_not_the_same_pane()
    {
        var before = ImagePane("A1B2");
        var after = ImagePane("C3D4");

        await Assert.That(ViewerForm.Same(before, after)).IsFalse();
    }

    [Test]
    public async Task A_picture_that_did_not_change_is_the_same_pane() =>
        await Assert.That(ViewerForm.Same(ImagePane("A1B2"), ImagePane("A1B2"))).IsTrue();

    [Test]
    public async Task A_picture_replaced_by_one_of_another_size_is_not_the_same_pane()
    {
        var before = ImagePane("A1B2");
        var after = before with
        {
            Image = new("sample.received.png", 20, 10, "A1B2")
        };

        await Assert.That(ViewerForm.Same(before, after)).IsFalse();
    }

    [Test]
    public async Task Text_panes_are_unaffected()
    {
        var pane = new Pane("received", [new(1, RowKind.Unchanged, "one")], 0, 1);

        await Assert.That(ViewerForm.Same(pane, pane with { })).IsTrue();
        await Assert.That(ViewerForm.Same(pane, pane with { ScrollTop = 1 })).IsFalse();
    }

    static Pane ImagePane(string hash) =>
        new(
            "received",
            [],
            0,
            0,
            new("sample.received.png", 10, 10, hash));
}
