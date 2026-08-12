/// <summary>
/// Skips a test that can only be asserted with no DiffEngineTray running — which is to say, on
/// every machine except the ones actually using the thing.
/// <para>
/// The signal here is deliberately <em>not</em> the one under test. It reads the process list,
/// while <see cref="DiffEngine.DiffEngineTray.IsRunning"/> reads a named mutex. Skipping on
/// <c>IsRunning</c> itself would make the test vacuous: a detector wedged at true would skip
/// forever rather than fail, which is the one outcome the test exists to catch.
/// </para>
/// <para>
/// No race to worry about: <c>IsRunning</c> is filled by a static constructor, so it is fixed for
/// the life of the test process, and a tray started midway through a run cannot change the answer
/// under it.
/// </para>
/// </summary>
public sealed class SkipWhenTrayRunningAttribute() :
    SkipAttribute("A DiffEngineTray is running on this machine, so the no-tray case cannot be asserted.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        var trays = Process.GetProcessesByName("DiffEngineTray");
        foreach (var tray in trays)
        {
            tray.Dispose();
        }

        return Task.FromResult(trays.Length > 0);
    }
}
