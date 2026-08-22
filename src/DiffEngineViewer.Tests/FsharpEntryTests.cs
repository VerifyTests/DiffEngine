/// <summary>
/// An entry from a producer whose language does not implement CallerArgumentExpression, which is
/// F#. There is no source text for the previous argument, so the patch carries its value instead -
/// the same snapshot the expression would have parsed to, and the same anchor one parse further
/// on.
/// </summary>
public class FsharpEntryTests
{
    [Test]
    public async Task The_previous_snapshot_fills_the_expected_pane()
    {
        var state = Fixtures.Inline(Patch());

        var entry = state.Queue[0];

        await Assert.That(entry.RightHeader).IsEqualTo("expected");
        await Assert.That(entry.RightText).IsEqualTo(Fixtures.Expected);
        await Assert.That(entry.Warning).IsNull();
    }

    /// <summary>
    /// With neither anchor there is genuinely nothing to compare against, which is what the empty
    /// side is for.
    /// </summary>
    [Test]
    public async Task A_first_run_still_reads_as_a_new_snapshot()
    {
        var state = Fixtures.Inline(
            new InlinePatch("SampleTests.fs", 42, null, Fixtures.Received)
            {
                TestName = null
            });

        var entry = state.Queue[0];

        await Assert.That(entry.RightHeader).IsEqualTo("expected (new snapshot)");
        await Assert.That(entry.RightText).IsEmpty();
    }

    /// <summary>
    /// And what that looks like: two full panes with one line differing, rather than five added
    /// lines beside nothing.
    /// </summary>
    [Test]
    public Task Screen() =>
        Verify(Fixtures.Render(Fixtures.Inline(Patch())));

    static InlinePatch Patch() =>
        new("SampleTests.fs", 42, null, Fixtures.Received)
        {
            TestName = null,
            OriginalValue = Fixtures.Expected
        };
}
