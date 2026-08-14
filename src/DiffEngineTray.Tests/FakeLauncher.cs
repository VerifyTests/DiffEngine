/// <summary>
/// A viewer that is never actually started. <see cref="OwnedInlineHost"/> launches one whenever a
/// patch arrives with nothing displaying the queue, and the tests supply their own display instead.
/// </summary>
sealed class FakeLauncher : IViewerLauncher
{
    public int Launches { get; private set; }

    public bool Running { get; set; }

    public bool Succeed { get; set; } = true;

    public bool Launch()
    {
        Launches++;
        Running = Succeed;
        return Succeed;
    }
}
