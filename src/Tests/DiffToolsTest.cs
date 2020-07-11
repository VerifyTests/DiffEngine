using System.IO;
using System.Linq;
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

    [Fact]
    public void AddTool()
    {
        string diffToolPath = FakeDiffTool.Exe;
        #region AddTool
        var resolvedTool = DiffTools.AddTool(
            name: "MyCustomDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            shellExecute: false,
            arguments: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\"",
            exePath: diffToolPath,
            binaryExtensions: new[] {"jpg"})!;
        #endregion
        Assert.Equal(resolvedTool.Name, DiffTools.Resolved.First().Name);
        Assert.True(DiffTools.TryFind("jpg", out var forExtension));
        Assert.Equal(resolvedTool.Name, forExtension!.Name);
    }

    [Fact]
    public void OrderShouldNotMessWithAddTool()
    {
        string diffToolPath = FakeDiffTool.Exe;
        var resolvedTool = DiffTools.AddTool(
            name: "MyCustomDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            shellExecute: false,
            arguments: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\"",
            exePath: diffToolPath,
            binaryExtensions: Enumerable.Empty<string>())!;
        DiffTools.UseOrder(DiffTool.VisualStudio, DiffTool.AraxisMerge);
        Assert.Equal(resolvedTool.Name, DiffTools.Resolved.First().Name);
        Assert.True(DiffTools.TryFind("txt", out var forExtension));
        Assert.Equal(resolvedTool.Name, forExtension!.Name);
    }

#if DEBUG
    [Fact]
    public void AddToolBasedOn()
    {
        #region AddToolBasedOn
        var resolvedTool = DiffTools.AddToolBasedOn(
            DiffTool.VisualStudio,
            name: "MyCustomDiffTool",
            arguments: (temp, target) => $"\"custom args {temp}\" \"{target}\"");
        #endregion
        Assert.Equal(resolvedTool, DiffTools.Resolved.First());
        Assert.True(DiffTools.TryFind("txt", out var forExtension));
        Assert.Equal(resolvedTool, forExtension);
        Assert.Equal("\"custom args foo\" \"bar\"", resolvedTool!.Arguments("foo", "bar"));
    }
#endif
    void AddToolAndLaunch()
    {
        #region AddToolAndLaunch
        var resolvedTool = DiffTools.AddToolBasedOn(
            DiffTool.VisualStudio,
            name: "MyCustomDiffTool",
            arguments: (temp, target) => $"\"custom args {temp}\" \"{target}\"");

        DiffRunner.Launch(resolvedTool!, "PathToTempFile", "PathToTargetFile");
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
    public void ChangeOrder()
    {
        #region UseOrder
        DiffTools.UseOrder(DiffTool.VisualStudio, DiffTool.AraxisMerge);
        #endregion
        Assert.Equal(DiffTool.VisualStudio, DiffTools.Resolved.First().Tool);
    }

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