#if NET7_0
public class DiffRunnerTests :
    XunitContextBase
{
    static ResolvedTool tool;
    string file2;
    string file1;
    string command;

    [Fact(Skip = "Explicit")]
    public async Task MaxInstancesToLaunch()
    {
        DiffRunner.MaxInstancesToLaunch(1);
        try
        {
            await Task.Delay(500);
            ProcessCleanup.Refresh();
            var result = DiffRunner.Launch(file1, "fake.txt");
            await Task.Delay(300);
            Assert.Equal(LaunchResult.StartedNewInstance, result);
            ProcessCleanup.Refresh();
            result = DiffRunner.Launch(file2, "fake.txt");
            Assert.Equal(LaunchResult.TooManyRunningDiffTools, result);
            ProcessCleanup.Refresh();
            DiffRunner.Kill(file1, "fake.txt");
            DiffRunner.Kill(file2, "fake.txt");
        }
        finally
        {
            DiffRunner.MaxInstancesToLaunch(5);
        }
    }

    [Fact(Skip = "Explicit")]
    public async Task MaxInstancesToLaunchAsync()
    {
        DiffRunner.MaxInstancesToLaunch(1);
        try
        {
            await Task.Delay(500);
            ProcessCleanup.Refresh();
            var result = await DiffRunner.LaunchAsync(file1, "fake.txt");
            await Task.Delay(300);
            Assert.Equal(LaunchResult.StartedNewInstance, result);
            ProcessCleanup.Refresh();
            result = await DiffRunner.LaunchAsync(file2, "fake.txt");
            Assert.Equal(LaunchResult.TooManyRunningDiffTools, result);
            ProcessCleanup.Refresh();
            DiffRunner.Kill(file1, "fake.txt");
            DiffRunner.Kill(file2, "fake.txt");
        }
        finally
        {
            DiffRunner.MaxInstancesToLaunch(5);
        }
    }

    async Task Launch()
    {
        var targetFile = "";
        var tempFile = "";

        #region DiffRunnerLaunch

        await DiffRunner.LaunchAsync(tempFile, targetFile);

        #endregion
    }

    [Fact(Skip = "Explicit")]
    public async Task KillAsync()
    {
        await DiffRunner.LaunchAsync(file1, file2);
        ProcessCleanup.Refresh();

        #region DiffRunnerKill

        DiffRunner.Kill(file1, file2);

        #endregion
    }

    [Fact]
    public void LaunchAndKillDisabled()
    {
        DiffRunner.Disabled = true;
        try
        {
            Assert.False(IsRunning());
            Assert.False(ProcessCleanup.IsRunning(command));
            var result = DiffRunner.Launch(file1, file2);
            Assert.Equal(LaunchResult.Disabled, result);
            Thread.Sleep(500);
            ProcessCleanup.Refresh();
            Assert.False(IsRunning());
            Assert.False(ProcessCleanup.IsRunning(command));
            DiffRunner.Kill(file1, file2);
            Thread.Sleep(500);
            ProcessCleanup.Refresh();
            Assert.False(IsRunning());
            Assert.False(ProcessCleanup.IsRunning(command));
        }
        finally
        {
            DiffRunner.Disabled = false;
        }
    }

    [Fact]
    public async Task LaunchAndKillDisabledAsync()
    {
        DiffRunner.Disabled = true;
        try
        {
            Assert.False(IsRunning());
            Assert.False(ProcessCleanup.IsRunning(command));
            var result = await DiffRunner.LaunchAsync(file1, file2);
            Assert.Equal(LaunchResult.Disabled, result);
            Thread.Sleep(500);
            ProcessCleanup.Refresh();
            Assert.False(IsRunning());
            Assert.False(ProcessCleanup.IsRunning(command));
            DiffRunner.Kill(file1, file2);
            Thread.Sleep(500);
            ProcessCleanup.Refresh();
            Assert.False(IsRunning());
            Assert.False(ProcessCleanup.IsRunning(command));
        }
        finally
        {
            DiffRunner.Disabled = false;
        }
    }

    [Fact]
    public void LaunchAndKill()
    {
        Assert.False(IsRunning());
        Assert.False(ProcessCleanup.IsRunning(command));
        var result = DiffRunner.Launch(file1, file2);
        Assert.Equal(LaunchResult.StartedNewInstance, result);
        Thread.Sleep(500);
        ProcessCleanup.Refresh();
        Assert.True(IsRunning());
        Assert.True(ProcessCleanup.IsRunning(command));
        DiffRunner.Kill(file1, file2);
        Thread.Sleep(500);
        ProcessCleanup.Refresh();
        Assert.False(IsRunning());
        Assert.False(ProcessCleanup.IsRunning(command));
    }

    [Fact]
    public async Task LaunchAndKillAsync()
    {
        Assert.False(IsRunning());
        Assert.False(ProcessCleanup.IsRunning(command));
        var result = await DiffRunner.LaunchAsync(file1, file2);
        Assert.Equal(LaunchResult.StartedNewInstance, result);
        Thread.Sleep(500);
        ProcessCleanup.Refresh();
        Assert.True(IsRunning());
        Assert.True(ProcessCleanup.IsRunning(command));
        DiffRunner.Kill(file1, file2);
        Thread.Sleep(500);
        ProcessCleanup.Refresh();
        Assert.False(IsRunning());
        Assert.False(ProcessCleanup.IsRunning(command));
    }

    static bool IsRunning() =>
        ProcessCleanup
            .FindAll()
            .Any(_ => _.Command.Contains("FakeDiffTool"));

    public DiffRunnerTests(ITestOutputHelper output) :
        base(output)
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
            requiresTarget: true,
            launchArguments: new(
                Left: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\"",
                Right: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\""),
            exePath: FakeDiffTool.Exe,
            binaryExtensions: new[]
            {
                "knownBin"
            })!;
}
#endif