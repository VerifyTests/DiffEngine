public class LinkLauncherTests
{
    /// <summary>
    /// Opening a link can fail - no browser registered, or one that will not start - and it is
    /// asked for from a click in the options form and from the handler that reports an error. A
    /// throw from either is on the UI thread with nothing to catch it, and from the second it
    /// replaces the error being reported.
    /// <para>
    /// A file that does not exist is the one thing certain to fail to open without anything being
    /// opened.
    /// </para>
    /// </summary>
    [Test]
    public async Task ALinkThatCannotBeOpenedDoesNotThrow()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.missing");

        await Assert.That(() => LinkLauncher.LaunchUrl(missing)).ThrowsNothing();
    }
}
