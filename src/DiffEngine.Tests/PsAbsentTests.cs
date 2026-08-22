/// <summary>
/// A machine with no ps.
/// <para>
/// process.Start was unguarded and a non-zero exit threw, and both propagate out of
/// ProcessCleanup's static constructor - so a minimal container without procps, which is also one
/// that does not set DOTNET_RUNNING_IN_CONTAINER, got a permanent TypeInitializationException on
/// every launch and kill rather than "no running processes". The timeout path already degraded.
/// </para>
/// <para>
/// Windows is the machine with no ps, which is what makes this testable at all: the code is only
/// used on Linux and macOS, but nothing about it refuses to run here, and here the executable is
/// genuinely missing.
/// </para>
/// </summary>
[RunOn(TUnit.Core.Enums.OS.Windows)]
public class PsAbsentTests
{
    [Test]
    public async Task NoPsMeansNoProcessesRatherThanAThrow()
    {
        var commands = LinuxOsxProcess.FindAll();

        await Assert.That(commands).IsEmpty();
    }
}
