/// <summary>
/// Labels for tracked moves and deletes. One verified file name in two projects of a solution is
/// the ordinary case - every project has a SampleTests with a sample.verified.txt - and the label
/// is the only thing on the row.
/// </summary>
public class TrackedLabelTests
{
    [Test]
    public async Task Two_moves_of_one_file_name_are_told_apart()
    {
        var state = Tracked(
            Move("ProjectA"),
            Move("ProjectB"));

        await Assert.That(Labels(state)).IsEquivalentTo(
        [
            "ProjectA/sample.verified.txt",
            "ProjectB/sample.verified.txt"
        ]);
    }

    [Test]
    public async Task Two_deletes_of_one_file_name_are_told_apart()
    {
        var state = Tracked(
            Delete("ProjectA"),
            Delete("ProjectB"));

        await Assert.That(Labels(state)).IsEquivalentTo(
        [
            "ProjectA/extra.verified.txt",
            "ProjectB/extra.verified.txt"
        ]);
    }

    /// <summary>
    /// A name that is already distinct is left as it is: the label is the shortest form that tells
    /// one entry from another, and the path is what the tooltip is for.
    /// </summary>
    [Test]
    public async Task One_move_keeps_its_bare_name()
    {
        var state = Tracked(Move("ProjectA"));

        await Assert.That(Labels(state)).IsEquivalentTo(["sample.verified.txt"]);
    }

    static IReadOnlyList<string> Labels(SessionState state) =>
        QueueProjection.Rows(state)
            .Where(_ => _.Kind == QueueRowKind.Entry)
            .Select(_ => _.Label.Trim())
            .ToList();

    static SessionState Tracked(params QueueEntry[] entries)
    {
        var state = SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows);
        foreach (var entry in entries)
        {
            state = ViewerSession.EnqueueTracked(state, entry);
        }

        return state;
    }

    static QueueEntry Move(string project) =>
        QueueEntry.ForMove(
            $"move:{project}",
            "sample.verified.txt",
            "SolutionA",
            $"temp/{project}/sample.received.txt",
            $"code/SolutionA/{project}/sample.verified.txt",
            FileSide.OfText("received"),
            FileSide.OfText("expected"));

    static QueueEntry Delete(string project) =>
        QueueEntry.ForDelete(
            $"delete:{project}",
            "extra.verified.txt",
            "SolutionA",
            $"code/SolutionA/{project}/extra.verified.txt",
            FileSide.OfText("expected"));
}
