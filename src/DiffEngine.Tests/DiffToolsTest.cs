using System;
using System.IO;
using System.Runtime.InteropServices;
using DiffEngine;
using Xunit;
using Xunit.Abstractions;

public class DiffToolsTest :
    XunitContextBase
{
    [Fact]
    public void MaxInstancesToLaunch()
    {
        #region MaxInstancesToLaunch

        DiffRunner.MaxInstancesToLaunch(10);

        #endregion
    }

    void AddCustomTool(string diffToolPath)
    {
        #region AddCustomTool
        DiffTools.TryAddCustomTool(
            name: "MyCustomDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            buildArguments: (tempFile, targetFile) =>
            {
                return $"\"{tempFile}\" \"{targetFile}\"";
            },
            exePath: diffToolPath,
            binaryExtensions: new[] {"jpg"});
        #endregion

        #region AddCustomToolExpanded
        DiffTools.TryAddCustomTool(
            name: "MyCustomDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            buildArguments: (tempFile, targetFile) =>
            {
                return $"\"{tempFile}\" \"{targetFile}\"";
            },
            exePath: Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\MyTool\MyTool.exe"),
            binaryExtensions: new[] {"jpg"});
        #endregion
    }

    //[Fact]
    //public void LaunchImageDiff()
    //{
    //    foreach (var tool in DiffTools.Resolved)
    //    {
    //        DiffRunner.Launch(tool,
    //            Path.Combine(SourceDirectory, "input.file1.png"),
    //            Path.Combine(SourceDirectory, "input.file2.png"));
    //    }
    //}

    //[Fact]
    //public void LaunchTextDiff()
    //{
    //    foreach (var tool in DiffTools.Resolved)
    //    {
    //        DiffRunner.Launch(tool,
    //            Path.Combine(SourceDirectory, "input.file1.txt"),
    //            Path.Combine(SourceDirectory, "input.file2.txt"));
    //    }
    //}

#if DEBUG
    [Fact]
    public void TryFind()
    {
        Assert.True(DiffTools.TryFind("txt", out var resolved));
        Assert.NotNull(resolved);

        Assert.True(DiffTools.TryFind(DiffTool.VisualStudio, out resolved));
        Assert.NotNull(resolved);

        Assert.False(DiffTools.TryFind("notFound", out resolved));
        Assert.Null(resolved);

        Assert.False(DiffTools.TryFind(DiffTool.Kaleidoscope, out resolved));
        Assert.Null(resolved);
    }
#endif

    public DiffToolsTest(ITestOutputHelper output) :
        base(output)
    {
        DiffTools.Reset();
    }
}