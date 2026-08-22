#if NET10_0
/// <summary>
/// Reading the command line of a 32-bit process from this 64-bit one.
/// <para>
/// NtQueryInformationProcess with ProcessBasicInformation answers a 64-bit caller with the 64-bit
/// PEB, even when the target is running under WOW64. Reading that with 32-bit offsets produced
/// nothing, so every 32-bit diff tool - which is most of the %ProgramFiles(x86)% installs the
/// resolver goes out of its way to find - had no command line: never seen as already running, and
/// never killed. The 32-bit PEB has to be asked for by name.
/// </para>
/// </summary>
[NotInParallel]
[RunOn(TUnit.Core.Enums.OS.Windows)]
public class Wow64CommandLineTests
{
    [Test]
    public async Task AThirtyTwoBitProcessHasAReadableCommandLine()
    {
        var wow = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SysWOW64",
            "cmd.exe");
        if (!Environment.Is64BitProcess ||
            !File.Exists(wow))
        {
            // A 32-bit host, or a Windows with no WOW64 layer. Nothing to say here
            return;
        }

        // Distinctive, so this cannot match any other cmd on the machine
        var marker = $"DiffEngineWow64Probe{Guid.NewGuid():N}";
        var process = Process.Start(
            new ProcessStartInfo
            {
                FileName = wow,
                // Shaped like a diff tool invocation, because FindAll only keeps command
                // lines with two file path arguments
                Arguments = $"/c ping -n 30 127.0.0.1 >nul & rem C:\\probe\\{marker}.received.txt C:\\probe\\{marker}.verified.txt",
                UseShellExecute = false,
                CreateNoWindow = true
            })!;

        try
        {
            await Assert.That(await WaitForCommandLine(marker)).IsTrue();
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Nothing useful to do if it has already gone
            }

            process.Dispose();
        }
    }

    static async Task<bool> WaitForCommandLine(string marker)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var found = WindowsProcess
                .FindAll([with(StringComparer.OrdinalIgnoreCase), "cmd.exe"])
                .Any(_ => _.Command.Contains(marker, StringComparison.Ordinal));
            if (found)
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
    }
}
#endif
