/// <summary>
/// The queue tooltip's state transitions.
/// <para>
/// A tooltip is a separate top level window, so no capture in this project can see one. These
/// assert on what the control has been told to show instead, which is the only place the bug below
/// is visible at all.
/// </para>
/// </summary>
[NotInParallel]
[TUnit.Core.Executors.STAThreadExecutor]
public class QueueTipsTests
{
    /// <summary>
    /// The reported bug: hovering a solution header showed the path of the entry underneath it.
    /// <para>
    /// Hide dismisses the popup that is up, but the control keeps its caption, so the next rest
    /// anywhere on the canvas brought the previous row's tip back. Nothing about the header said
    /// so, which is exactly how it read: as the header describing that entry.
    /// </para>
    /// </summary>
    [Test]
    public async Task ARowWithNothingToAddClearsTheRowBefore()
    {
        using var owner = new Control();
        using var tips = new QueueTips();

        tips.Apply(owner, 1, "SolutionA/Tests/ATests.cs:6");
        await Assert.That(tips.Current(owner)).IsEqualTo("SolutionA/Tests/ATests.cs:6");

        tips.Apply(owner, 0, null);
        await Assert.That(tips.Current(owner)).IsEmpty();
    }

    [Test]
    public async Task LeavingTheColumnClearsIt()
    {
        using var owner = new Control();
        using var tips = new QueueTips();

        tips.Apply(owner, 2, "something");
        tips.Apply(owner, -1, null);
        await Assert.That(tips.Current(owner)).IsEmpty();
    }

    /// <summary>
    /// Re-applying the same row is a window message for nothing, and OnMouseMove runs for every
    /// pixel crossed.
    /// </summary>
    [Test]
    public async Task TheSameRowIsNotReapplied()
    {
        using var owner = new Control();
        using var tips = new QueueTips();

        tips.Apply(owner, 3, "first");
        tips.Apply(owner, 3, "second");
        await Assert.That(tips.Current(owner)).IsEqualTo("first");
    }

    /// <summary>
    /// A new screen renumbers the rows, so the row under a cursor that has not moved may now be a
    /// different entry — or a header.
    /// </summary>
    [Test]
    public async Task ForgettingLetsTheSameRowChange()
    {
        using var owner = new Control();
        using var tips = new QueueTips();

        tips.Apply(owner, 3, "before");
        tips.Forget();
        tips.Apply(owner, 3, null);
        await Assert.That(tips.Current(owner)).IsEmpty();
    }
}
