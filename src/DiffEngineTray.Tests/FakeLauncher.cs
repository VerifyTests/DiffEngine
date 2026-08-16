/// <summary>
/// A viewer that is never actually started. <see cref="OwnedInlineHost"/> launches one whenever a
/// patch arrives with nothing displaying the queue, and the tests supply their own display instead.
/// </summary>
sealed class FakeLauncher : IViewerLauncher
{
    public int Launches { get; private set; }

    public bool Running { get; set; }

    public bool Succeed { get; set; } = true;

    /// <summary>
    /// Held for as long as a test wants a launch to be in flight, standing in for the seconds a
    /// real process start can take with an antivirus in the way.
    /// </summary>
    public ManualResetEventSlim? Block { get; set; }

    /// <summary>
    /// Set once a launch is under way, so a test can act while one is.
    /// </summary>
    public ManualResetEventSlim Started { get; } = new();

    public bool Launch()
    {
        Launches++;
        Started.Set();
        Block?.Wait(TimeSpan.FromSeconds(10));
        Running = Succeed;
        return Succeed;
    }
}
