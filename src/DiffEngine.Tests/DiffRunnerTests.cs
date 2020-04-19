#if NETCOREAPP3_1 && DEBUG
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        #region DiffRunnerKill
        DiffRunner.Kill(tempFile, targetFile);
        #endregion
    }

    static string diffToolPath = Path.GetFullPath(Path.Combine(AssemblyLocation.CurrentDirectory, "../../../../FakeDiffTool/bin/FakeDiffTool.exe"));

    [Fact]
    public void LaunchAndKill()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }
        DiffTools.AddCustomTool(
            supportsAutoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            buildArguments: (path1, path2) => $"\"{path1}\" \"{path2}\"",
            exePath: diffToolPath,
            binaryExtensions: new[] {"knownBin"});
        var tempFile = Path.Combine(SourceDirectory, "DiffRunner.file1.txt");
        var targetFile = Path.Combine(SourceDirectory, "DiffRunner.file2.txt");
        DiffRunner.Launch(tempFile, targetFile);
        Assert.True(IsRunning());
        DiffRunner.Kill(tempFile, targetFile);
        Assert.False(IsRunning());
    }

    static bool IsRunning()
    {
        Thread.Sleep(500);
        return ProcessCleanup.FindAll().Any(x => x.Command.Contains("FakeDiffTool"));
    }

    public DiffRunnerTests(ITestOutputHelper output) :
        base(output)
    {
    }
}
#endif