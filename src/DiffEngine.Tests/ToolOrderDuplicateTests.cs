/// <summary>
/// A repeated name in DiffEngine_ToolOrder.
/// <para>
/// Sort looked each requested tool up in Definitions, which holds every tool whether it is
/// installed or not - so the lookup only ever failed on a second occurrence, because the first had
/// removed it from the list. That was then reported as "is not installed", which was untrue, and
/// since DiffTools resolves the order in a static constructor it was also permanent: every later
/// use of DiffTools in that process threw TypeInitializationException.
/// </para>
/// </summary>
[NotInParallel]
public class ToolOrderDuplicateTests :
    IDisposable
{
    [Test]
    public async Task ARepeatedToolIsNotAnUninstalledOne()
    {
        // An installed one, chosen from what this machine actually has, since throwForNoTool is
        // the flag under test and naming an absent tool would throw for the right reason
        var installed = DiffTools.Resolved.FirstOrDefault(_ => _.Tool != null)?.Tool;
        if (installed == null)
        {
            // No built in tool resolved here, so there is nothing to repeat
            return;
        }

        DiffTools.UseOrder(true, installed.Value, installed.Value);

        // Still ordered, rather than dropped along with the duplicate
        await Assert.That(DiffTools.Resolved.Any(_ => _.Tool == installed)).IsTrue();
    }

    /// <summary>
    /// And the flag still means what it says for a tool that genuinely is not here.
    /// </summary>
    [Test]
    public async Task AnUninstalledToolStillThrows()
    {
        var installed = DiffTools.Resolved
            .Where(_ => _.Tool != null)
            .Select(_ => _.Tool!.Value)
            .ToHashSet();

        var absent = Enum.GetValues<DiffTool>().FirstOrDefault(_ => !installed.Contains(_));
        if (installed.Count == Enum.GetValues<DiffTool>().Length)
        {
            // Every tool installed, which no real machine is
            return;
        }

        await Assert.That(() => DiffTools.UseOrder(true, absent))
            .Throws<Exception>();
    }

    public void Dispose() =>
        DiffTools.Reset();
}
