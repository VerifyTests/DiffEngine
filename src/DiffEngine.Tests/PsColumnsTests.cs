#if NET10_0
/// <summary>
/// A terminal width in the environment. Shells keep COLUMNS for themselves and some setups export
/// it, and procps then takes it over the unlimited width it gives a pipe: every command line came
/// back cut to that many characters, so a diff tool running with two long paths was never found,
/// and never killed.
/// </summary>
// The variable is the test process's own, which every ps another test starts would inherit
[NotInParallel]
[RunOn(TUnit.Core.Enums.OS.Linux | TUnit.Core.Enums.OS.MacOs)]
public class PsColumnsTests
{
    [Test]
    public async Task AnExportedWidthDoesNotCutCommandLines()
    {
        // Past any width a terminal has, with the part that tells it from every other process at
        // the far end, where a cut loses it
        var marker = new string('a', 300) + Guid.NewGuid().ToString("N");
        var previous = Environment.GetEnvironmentVariable("COLUMNS");
        // A list rather than one command, so that the shell stays to run it instead of becoming
        // the sleep, and the marker stays on a command line as its $0
        using var process = Process.Start(
            new ProcessStartInfo("sh", $"-c \"sleep 30; true\" {marker}")
            {
                UseShellExecute = false
            })!;
        Environment.SetEnvironmentVariable("COLUMNS", "80");
        try
        {
            var commands = LinuxOsxProcess.FindAll();

            var found = commands.Where(_ => _.Process == process.Id).ToList();
            await Assert.That(found.Count).IsEqualTo(1);
            await Assert.That(found[0].Command).EndsWith(marker);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLUMNS", previous);
            process.Kill(true);
        }
    }
}
#endif
