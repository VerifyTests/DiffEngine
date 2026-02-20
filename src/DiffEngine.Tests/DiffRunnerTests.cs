#if NET10_0
[NotInParallel]
public class DiffRunnerTests
{
    static string SourceDirectory { get; } = Path.GetDirectoryName(GetSourceFile())!;
    static string GetSourceFile([CallerFilePath] string path = "") => path;

    static ResolvedTool tool;
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
            await Assert.That(IsRunning()).IsFalse();
            await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
            var result = DiffRunner.Launch(file1, file2);
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
        await Assert.That(IsRunning()).IsFalse();
        await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
        var result = DiffRunner.Launch(file1, file2);
        await Assert.That(result).IsEqualTo(LaunchResult.StartedNewInstance);
        Thread.Sleep(500);
        ProcessCleanup.Refresh();
        await Assert.That(IsRunning()).IsTrue();
        await Assert.That(ProcessCleanup.IsRunning(command)).IsTrue();
        DiffRunner.Kill(file1, file2);
        Thread.Sleep(500);
        ProcessCleanup.Refresh();
        await Assert.That(IsRunning()).IsFalse();
        await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
    }

    [Test]
    public async Task LaunchAndKillAsync()
    {
        await Assert.That(IsRunning()).IsFalse();
        await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
        var result = await DiffRunner.LaunchAsync(file1, file2);
        await Assert.That(result).IsEqualTo(LaunchResult.StartedNewInstance);
        Thread.Sleep(500);
        ProcessCleanup.Refresh();
        await Assert.That(IsRunning()).IsTrue();
        await Assert.That(ProcessCleanup.IsRunning(command)).IsTrue();
        DiffRunner.Kill(file1, file2);
        Thread.Sleep(500);
        ProcessCleanup.Refresh();
        await Assert.That(IsRunning()).IsFalse();
        await Assert.That(ProcessCleanup.IsRunning(command)).IsFalse();
    }

    static bool IsRunning() =>
        ProcessCleanup
            .FindAll()
            .Any(_ => _.Command.Contains("FakeDiffTool"));

    public DiffRunnerTests()
    {
        file1 = Path.Combine(SourceDirectory, "DiffRunner.file1.txt");
        file2 = Path.Combine(SourceDirectory, "DiffRunner.file2.txt");
        command = tool.BuildCommand(file1, file2);
    }

    static DiffRunnerTests() =>
        tool = DiffTools.AddTool(
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
