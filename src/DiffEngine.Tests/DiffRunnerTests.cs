using System.IO;
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

    public DiffRunnerTests(ITestOutputHelper output) :
        base(output)
    {
    }
}