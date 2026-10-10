#if NET10_0
/// <summary>
/// Windows tools from inside a real WSL distribution: the host read, a tool found on it, started
/// through WSL, found again and closed.
/// <para>
/// Everything else about WSL is tested without one (<see cref="WslHostTests" />,
/// <see cref="WslPathsTests" />, <see cref="WslKillScriptTests" />). These are the parts that
/// only a distribution can answer: what its mount table and <c>ps</c> really say, and whether a
/// program on the host can be started, handed a file, and ended. They run in the <c>wsl</c> job,
/// under WSL 1 and WSL 2, and for anyone running the suite inside a distribution.
/// </para>
/// <para>
/// No diff tool is started. Programs every host has stand in: <c>cmd.exe</c> to read a file,
/// and Windows PowerShell as the tool that is handed two files and then waits.
/// </para>
/// </summary>
[NotInParallel]
[RequiresWsl]
public class WslLiveTests :
    IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), $"WslLiveTests_{Guid.NewGuid():N}");

    public WslLiveTests() =>
        Directory.CreateDirectory(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    static WslHost Host =>
        WslInterop.Host ?? throw new("Inside WSL, but the host could not be read.");

    /// <summary>
    /// <c>cmd.exe</c> as this process sees it, found the way a tool is: by a Windows directory
    /// written with one of the host's variables.
    /// </summary>
    static string Cmd()
    {
        var settings = new OsSettings("cmd.exe", new((_, _) => "", (_, _) => ""), @"%SystemRoot%\System32\");
        if (!OsSettingsResolver.TryFindOnHost(Host, settings, out var path))
        {
            throw new(@"cmd.exe was not found in %SystemRoot%\System32.");
        }

        return path;
    }

    [Test]
    public async Task TheHostIsReadAndAProgramFoundOnIt()
    {
        var cmd = Cmd();

        await Assert.That(File.Exists(cmd)).IsTrue();
        await Assert.That(WslInterop.IsWindowsProgram(cmd)).IsTrue();
        await Assert.That(Host.TryFindPowerShell(out _)).IsTrue();
    }

    /// <summary>
    /// The translation is only right if a program on the host can open the file by it. One file
    /// in the distribution, which the host reaches as a share, and one on a drive, where the
    /// tests themselves are when the checkout is a Windows one.
    /// </summary>
    [Test]
    public async Task AWindowsProgramReadsAFileByItsTranslatedPath()
    {
        var cmd = Cmd();
        var inDistribution = Path.Combine(directory, "in the distribution.txt");
        await File.WriteAllTextAsync(inDistribution, "from the distribution");

        await Assert.That(Type(cmd, inDistribution)).IsEqualTo("from the distribution");

        var onDrive = Path.Combine(Path.GetDirectoryName(cmd)!, "drivers", "etc", "hosts");
        await Assert.That(Host.Paths.IsOnHost(onDrive)).IsTrue();
        await Assert.That(Type(cmd, onDrive)).IsNotNull();
    }

    /// <summary>
    /// What <c>ps</c> lists for the stand-in is WSL's own program with the tool's command line
    /// behind it. The tool is found by the command it was started with, and ended on the host,
    /// which takes the stand-in with it.
    /// </summary>
    [Test]
    public async Task AStartedToolIsFoundByItsCommandAndEndedOnTheHost()
    {
        var temp = Path.Combine(directory, "Tests.Method.received.txt");
        var target = Path.Combine(directory, "Tests.Method.verified.txt");
        await File.WriteAllTextAsync(temp, "received");
        await File.WriteAllTextAsync(target, "verified");
        var tool = StandIn();
        tool.CommandAndArguments(temp, target, out var arguments, out var command);

        var processId = DiffRunner.LaunchProcess(tool, arguments);
        try
        {
            ProcessCleanup.Track(command, processId);
            var listed = await Listed(processId);

            await Assert.That(listed).IsEqualTo(command.Replace("\"", ""));
            await Assert.That(ProcessCleanup.IsRunning(command)).IsTrue();

            await Assert.That(WslInterop.Kill(tool, temp, target)).IsTrue();
            await Assert.That(await Gone(processId)).IsTrue();
        }
        finally
        {
            LinuxOsxProcess.TryTerminateProcess(processId);
        }
    }

    /// <summary>
    /// A pair nothing was opened for costs nothing on the host: with no stand-in to find, the
    /// host is not asked.
    /// </summary>
    [Test]
    public async Task APairWithNoToolOpenIsNotLookedForOnTheHost()
    {
        var temp = Path.Combine(directory, "Tests.Other.received.txt");
        var target = Path.Combine(directory, "Tests.Other.verified.txt");

        await Assert.That(WslInterop.Kill(StandIn(), temp, target)).IsFalse();
    }

    /// <summary>
    /// A Windows PowerShell that carries both paths on its command line, as a comment, and then
    /// waits for twenty seconds saying nothing: what a diff tool looks like to everything on this
    /// side.
    /// <para>
    /// One process, as a diff tool is. A <c>cmd.exe</c> running <c>ping</c> to pass the time was
    /// tried first, and WSL's stand-in for it stayed until the <c>ping</c> it had started was
    /// done, long after the <c>cmd.exe</c> had been ended.
    /// </para>
    /// </summary>
    static ResolvedTool StandIn()
    {
        static string Arguments(string temp, string target) =>
            $"-NoProfile -NonInteractive -Command \"Start-Sleep 20 # {temp} {target}\"";

        if (!Host.TryFindPowerShell(out var powerShell))
        {
            throw new("Windows PowerShell was not found on the host.");
        }

        return new(
            "WslLiveTestsStandIn",
            powerShell,
            new(Arguments, Arguments),
            isMdi: false,
            autoRefresh: false,
            binaryExtensions: [],
            requiresTarget: false,
            supportsText: true,
            useShellExecute: false);
    }

    /// <summary>
    /// What <c>cmd.exe</c> on the host prints for a file given by its translated path.
    /// </summary>
    static string? Type(string cmd, string file) =>
        WslHost.Run(cmd, $"/c type \"{WslInterop.ToWindows(file)}\"", Path.GetDirectoryName(cmd)!, Encoding.UTF8)?.Trim();

    /// <summary>
    /// The command <c>ps</c> has for a process, once it is there to be listed.
    /// </summary>
    static async Task<string?> Listed(int processId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var found = ProcessCleanup.FindAll().Where(_ => _.Process == processId).ToList();
            if (found.Count == 1)
            {
                return found[0].Command;
            }

            await Task.Delay(100);
        }

        return null;
    }

    static async Task<bool> Gone(int processId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (ProcessCleanup.FindAll().All(_ => _.Process != processId))
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }
}
#endif
