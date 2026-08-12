#pragma warning disable CS0618 // Type or member is obsolete
public class DiffEngineTrayTest
{
    /// <summary>
    /// Detection does not report a tray that is not there. Skipped rather than failed when one is,
    /// because a machine running the tray cannot be asked the question — and the people most
    /// likely to run these tests are the ones most likely to have it running.
    /// </summary>
    [Test]
    [SkipWhenTrayRunning]
    public async Task IsRunning() =>
        await Assert.That(DiffEngineTray.IsRunning).IsFalse();
}
