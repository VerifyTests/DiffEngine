#if NETCOREAPP3_1
using System.IO;
using System.Linq;
using System.Threading;
using DiffEngine;
using Xunit;
using Xunit.Abstractions;

public class DiffRunnerTests :
    XunitContextBase
{
    [Fact(Skip = "Explicit")]
    public void Launch()
    {
        var tempFile = Path.Combine(SourceDirectory, "DiffRunner.file1.txt");
        var targetFile = Path.Combine(SourceDirectory, "DiffRunner.file2.txt");
        #region DiffRunnerLaunch
        DiffRunner.Launch(tempFile, targetFile);
        #endregion
    }

    [Fact(Skip = "Explicit")]
    public void Kill()
    {
        var tempFile = Path.Combine(SourceDirectory, "DiffRunner.file1.txt");
        var targetFile = Path.Combine(SourceDirectory, "DiffRunner.file2.txt");
        DiffRunner.Launch(tempFile, targetFile);
        ProcessCleanup.Refresh();
        #region DiffRunnerKill
        DiffRunner.Kill(tempFile, targetFile);
        #endregion
    }

    [Fact]
    public void LaunchAndKill()
    {
        var tool = DiffTools.AddTool(
            name: "FakeDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            shellExecute: false,
            arguments: (path1, path2) => $"\"{path1}\" \"{path2}\"",
            exePath: FakeDiffTool.Exe,
            binaryExtensions: new[] {"knownBin"});
        var tempFile = Path.Combine(SourceDirectory, "DiffRunner.file1.txt");
        var targetFile = Path.Combine(SourceDirectory, "DiffRunner.file2.txt");
        var command = tool!.BuildCommand(tempFile, targetFile);
        Assert.False(IsRunning());
        Assert.False(ProcessCleanup.IsRunning(command));
        var result = DiffRunner.Launch(tempFile, targetFile);
        Assert.Equal(LaunchResult.StartedNewInstance, result);
        Thread.Sleep(500);
        ProcessCleanup.Refresh();
        Assert.True(IsRunning());
        Assert.True(ProcessCleanup.IsRunning(command));
        DiffRunner.Kill(tempFile, targetFile);
        Thread.Sleep(500);
        ProcessCleanup.Refresh();
        Assert.False(IsRunning());
        Assert.False(ProcessCleanup.IsRunning(command));
    }

    static bool IsRunning()
    {
        return ProcessCleanup
            .FindAll()
            .Any(x => x.Command.Contains("FakeDiffTool"));
    }

    public DiffRunnerTests(ITestOutputHelper output) :
        base(output)
    {
    }
}
#endif