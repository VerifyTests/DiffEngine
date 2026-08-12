/// <summary>
/// What each queue row says beyond its own label.
/// <para>
/// Snapshotted as label-to-tip pairs rather than through a renderer, because a tooltip is not on
/// screen until something is hovered and no capture hovers. All three heads show this text
/// verbatim, so this is the coverage for all three.
/// </para>
/// <para>
/// The rule these exist to hold: a row that has nothing to add gets no tip. The labels are already
/// the shortest form that tells one entry from another, so a tip repeating one is a popup that
/// told the reader nothing.
/// </para>
/// </summary>
public class QueueTooltipTests
{
    [Test]
    public Task GroupedAndConflicted() =>
        Verify(Tips(Fixtures.GroupedConflicted()));

    /// <summary>
    /// One entry, labelled by its test name. The path is still worth saying; nothing else is.
    /// </summary>
    [Test]
    public Task Single() =>
        Verify(Tips(Fixtures.Inline(Fixtures.Patch(testName: "Compare handles nulls"))));

    /// <summary>
    /// No test name, so the label is already the call site. The directories it leaves off are
    /// still worth saying, which is the case that matters when one file name appears in two
    /// projects.
    /// </summary>
    [Test]
    public Task NoTestName() =>
        Verify(Tips(Fixtures.Inline(
            Fixtures.Patch(Fixtures.SolutionFile("SolutionA", "Tests", "ATests.cs"), 10))));

    /// <summary>
    /// The degenerate case the rule exists for: a bare file name with no directories, no test and
    /// no failure, so everything the tip would say is already the label. No tip at all.
    /// </summary>
    [Test]
    public Task NothingBeyondTheLabel() =>
        Verify(Tips(Fixtures.Inline(Fixtures.Patch())));

    /// <summary>
    /// Headers describe the rows underneath, each of which answers for itself, so they get none.
    /// </summary>
    [Test]
    public async Task HeadersHaveNone()
    {
        var rows = QueueProjection.Rows(Fixtures.GroupedConflicted());
        foreach (var header in rows.Where(_ => _.Kind == QueueRowKind.Header))
        {
            await Assert.That(header.Tooltip).IsNull();
        }
    }

    /// <summary>
    /// The failure text is the one thing a row shows only as a mark, so it always earns a tip.
    /// </summary>
    [Test]
    public async Task FailureTextIsCarried()
    {
        var state = Fixtures.Inline(Fixtures.Patch());
        state = state with
        {
            Queue = [state.Queue[0] with { Status = "could not be applied" }]
        };

        var row = QueueProjection.Rows(state).Single(_ => _.Kind == QueueRowKind.Entry);
        await Assert.That(row.Tooltip).Contains("could not be applied");
    }

    static string Tips(SessionState state)
    {
        var builder = new StringBuilder();
        foreach (var row in QueueProjection.Rows(state))
        {
            builder.AppendLine($"[{row.Label}]");
            builder.AppendLine(
                row.Tooltip is null
                    ? "  (no tip)"
                    : string.Join("\n", row.Tooltip.Split('\n').Select(_ => $"  {Portable(_)}")));
        }

        return builder.ToString();
    }

    /// <summary>
    /// These tips carry real paths, which the fixtures build with Path.Combine and which therefore
    /// separate differently on each OS. One baseline for all three, rather than three baselines
    /// that only ever differ by a slash.
    /// </summary>
    static string Portable(string line) =>
        line.Replace('\\', '/');
}
