#if NET10_0
[NotInParallel]
// Needs the Windows only FakeDiffTool.exe, or the Win32 process APIs.
[RunOn(TUnit.Core.Enums.OS.Windows)]
public class DiffRunnerTests
{
    static string SourceDirectory { get; } = Path.GetDirectoryName(GetSourceFile())!;
    static string GetSourceFile([CallerFilePath] string path = "") => path;

    // Launching registers a real pending move of file1 over file2 with whatever owns the queue on
    // this machine, usually the developer's tray. Accepting it moves file1, and discarding it
    // deletes file1 and then its directory. Run against the source directory that quietly destroys
    // the checked in fixtures, so the tests get copies instead.
    // One directory for the whole class, because IsRunning matches the exact command string and
    // WaitForRunning is also used at test start to wait out the previous test's kill, both of which
    // want the same paths across tests. Fresh per run, so a FakeDiffTool left behind by a crashed
    // earlier run cannot match this run's command.
    static string TempDirectory { get; } = Path.Combine(
        Path.GetTempPath(),
        "DiffEngine.DiffRunnerTests",
        Guid.NewGuid().ToString("N"));

    [After(Class)]
    public static void DeleteTempDirectory()
    {
        if (Directory.Exists(TempDirectory))
        {
            Directory.Delete(TempDirectory, true);
        }
    }

    static ResolvedTool? tool;
    string file2;
    string file1;
    string command;

    [Test]
    [Skip("Explicit")]
    public async Task MaxInstancesToLaunch()
    {
        DiffRunner.MaxInstancesToLaunch(1);
        try
        {
            await Task.Delay(500);
            ProcessCleanup.Refresh();
            var result = await DiffRunner.LaunchAsync(file1, "fake.txt");
            await Task.Delay(300);
            await Assert.That(result).IsEqualTo(LaunchResult.StartedNewInstance);
            ProcessCleanup.Refresh();
            result = await DiffRunner.LaunchAsync(file2, "fake.txt");
            await Assert.That(result).IsEqualTo(LaunchResult.TooManyRunningDiffTools);
            ProcessCleanup.Refresh();
            DiffRunner.Kill(file1, "fake.txt");
            DiffRunner.Kill(file2, "fake.txt");
        }
        finally
        {
            DiffRunner.MaxInstancesToLaunch(5);
        }
    }

    [Test]
    [Skip("Explicit")]
    public async Task MaxInstancesToLaunchAsync()
    {
        DiffRunner.MaxInstancesToLaunch(1);
        try
        {
            await Task.Delay(500);
            ProcessCleanup.Refresh();
            var result = await DiffRunner.LaunchAsync(file1, "fake.txt");
            await Task.Delay(300);
            await Assert.That(result).IsEqualTo(LaunchResult.StartedNewInstance);
            ProcessCleanup.Refresh();
            result = await DiffRunner.LaunchAsync(file2, "fake.txt");
            await Assert.That(result).IsEqualTo(LaunchResult.TooManyRunningDiffTools);
            ProcessCleanup.Refresh();
            DiffRunner.Kill(file1, "fake.txt");
            DiffRunner.Kill(file2, "fake.txt");
        }
        finally
        {
            DiffRunner.MaxInstancesToLaunch(5);
        }
    }

    static async Task Launch()
    {
        var targetFile = "";
        var tempFile = "";

        #region DiffRunnerLaunch

        await DiffRunner.LaunchAsync(tempFile, targetFile);

        #endregion
    }

    [Test]
    [Skip("Explicit")]
    public async Task KillAsync()
    {
        await DiffRunner.LaunchAsync(file1, file2);
        ProcessCleanup.Refresh();

        #region DiffRunnerKill

        DiffRunner.Kill(file1, file2);

        #endregion
    }

    [Test]
    public async Task LaunchAndKillDisabled()
    {
        DiffRunner.Disabled = true;
        try
        {
            await WaitForRunning(false);
            await Assert.That(IsRunning()).IsFalse();
            await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
            var result = await DiffRunner.LaunchAsync(file1, file2);
            await Assert.That(result).IsEqualTo(LaunchResult.Disabled);
            Thread.Sleep(500);
            ProcessCleanup.Refresh();
            await Assert.That(IsRunning()).IsFalse();
            await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
            DiffRunner.Kill(file1, file2);
            Thread.Sleep(500);
            ProcessCleanup.Refresh();
            await Assert.That(IsRunning()).IsFalse();
            await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
        }
        finally
        {
            DiffRunner.Disabled = false;
        }
    }

    [Test]
    public async Task LaunchAndKillDisabledAsync()
    {
        DiffRunner.Disabled = true;
        try
        {
            await WaitForRunning(false);
            await Assert.That(IsRunning()).IsFalse();
            await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
            var result = await DiffRunner.LaunchAsync(file1, file2);
            await Assert.That(result).IsEqualTo(LaunchResult.Disabled);
            Thread.Sleep(500);
            ProcessCleanup.Refresh();
            await Assert.That(IsRunning()).IsFalse();
            await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
            DiffRunner.Kill(file1, file2);
            Thread.Sleep(500);
            ProcessCleanup.Refresh();
            await Assert.That(IsRunning()).IsFalse();
            await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
        }
        finally
        {
            DiffRunner.Disabled = false;
        }
    }

    [Test]
    public async Task LaunchAndKill()
    {
        await WaitForRunning(false);
        await Assert.That(IsRunning()).IsFalse();
        await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
        var result = await DiffRunner.LaunchAsync(file1, file2);
        await Assert.That(result).IsEqualTo(LaunchResult.StartedNewInstance);
        await WaitForRunning(true);
        await Assert.That(IsRunning()).IsTrue();
        await Assert.That(ProcessCleanup.IsRunning(command)).IsTrue();
        DiffRunner.Kill(file1, file2);
        await WaitForRunning(false);
        await Assert.That(IsRunning()).IsFalse();
        await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
    }

    [Test]
    public async Task LaunchAndKillAsync()
    {
        await WaitForRunning(false);
        await Assert.That(IsRunning()).IsFalse();
        await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
        var result = await DiffRunner.LaunchAsync(file1, file2);
        await Assert.That(result).IsEqualTo(LaunchResult.StartedNewInstance);
        await WaitForRunning(true);
        await Assert.That(IsRunning()).IsTrue();
        await Assert.That(ProcessCleanup.IsRunning(command)).IsTrue();
        DiffRunner.Kill(file1, file2);
        await WaitForRunning(false);
        await Assert.That(IsRunning()).IsFalse();
        await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
    }

    // Match this test's exact command, not any FakeDiffTool: DiffEngineTray.Tests
    // runs concurrently in the same CI job and launches its own FakeDiffTool
    // instances, which a machine-wide substring scan would see.
    bool IsRunning()
    {
        var expected = command;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            expected = expected.Replace("\"", "");
        }

        return ProcessCleanup
            .FindAll()
            .Any(_ => _.Command == expected);
    }

    // Process spawn and kill are asynchronous, so poll instead of guessing with a
    // fixed sleep. Also used at test start: the previous test's kill may still be
    // completing when the next test begins.
    // Both views must agree before returning: IsRunning() scans processes fresh,
    // while ProcessCleanup.IsRunning(command) reads the cached list from the last
    // Refresh(). A process dying between the two reads would otherwise leave the
    // cache stale and fail the cached assert.
    async Task WaitForRunning(bool expected)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            ProcessCleanup.Refresh();
            if (ProcessCleanup.IsRunning(command) == expected &&
                IsRunning() == expected)
            {
                return;
            }

            await Task.Delay(250);
        }
    }

    public DiffRunnerTests()
    {
        file1 = CopyFixture("DiffRunner.file1.txt");
        file2 = CopyFixture("DiffRunner.file2.txt");
        command = Tool.BuildCommand(file1, file2);
    }

    // Per test rather than per class: a discard deletes the temp file and its directory, so the
    // copies have to be put back for the test that follows.
    static string CopyFixture(string name)
    {
        Directory.CreateDirectory(TempDirectory);
        var target = Path.Combine(TempDirectory, name);
        File.Copy(Path.Combine(SourceDirectory, name), target, true);
        return target;
    }

    // Resolved on first use rather than in a type initializer. [After(Class)] runs even when every
    // test here is skipped for the OS, so initializing the type has to be safe everywhere, and this
    // reaches FakeDiffTool, which is only built for Windows and macOS.
    static ResolvedTool Tool =>
        tool ??= DiffTools.AddTool(
            name: "FakeDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            useShellExecute: true,
            requiresTarget: true,
            launchArguments: new(
                Left: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\"",
                Right: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\""),
            exePath: FakeDiffTool.Exe,
            binaryExtensions: [".knownBin"])!;
}
#endif
