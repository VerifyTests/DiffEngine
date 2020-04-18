using System;
using System.IO;
using System.Linq;
using DiffEngine;
using Xunit;
using Xunit.Abstractions;

public class DiffToolsTest :
    XunitContextBase
{
    //[Fact]
    //public void IsDetected()
    //{
    //    Assert.True(DiffTools.IsDetectedFor(DiffTool.Rider, "txt"));
    //}

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
        DiffTools.AddCustomTool(
            supportsAutoRefresh: true,
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
    }

    [Fact]
    public void ParseEnvironmentVariable()
    {
        var diffTools = DiffTools.ParseEnvironmentVariable("VisualStudio,Meld").ToList();
        Assert.Equal(DiffTool.VisualStudio, diffTools[0]);
        Assert.Equal(DiffTool.Meld, diffTools[1]);
    }

    [Fact]
    public void BadEnvironmentVariable()
    {
        var exception = Assert.Throws<Exception>(() => DiffTools.ParseEnvironmentVariable("Foo").ToList());
        Assert.Equal("Unable to parse tool from `DiffEngine.DiffToolOrder` environment variable: Foo", exception.Message);
    }

    [Fact]
    public void WriteDiffToolsList()
    {
        var md = Path.Combine(SourceDirectory, "diffToolList.include.md");
        File.Delete(md);
        using var writer = File.CreateText(md);
        var tools = DiffTools
            .Tools();

        foreach (var tool in tools
            .OrderBy(x => x.Name.ToString()))
        {
            writer.WriteLine($@" * [{tool.Name}](/docs/diff-tool.md#{tool.Name.ToString().ToLower()})");
        }
    }

    [Fact]
    public void WriteFoundTools()
    {
        var md = Path.Combine(SourceDirectory, "diffTools.include.md");
        File.Delete(md);
        using var writer = File.CreateText(md);
        var tools = DiffTools
            .Tools();

        foreach (var tool in tools
            .OrderBy(x => x.Name.ToString()))
        {
            writer.WriteLine($@"
## [{tool.Name}]({tool.Url})");

            writer.WriteLine($@"
  * Is MDI: {tool.IsMdi}
  * Supports auto-refresh: {tool.SupportsAutoRefresh}
  * Supports text files: {tool.SupportsText}");

            if (tool.BinaryExtensions.Any())
            {
                writer.WriteLine(@" * Supported binaries: " + string.Join(", ", tool.BinaryExtensions));
            }

            if (tool.Notes != null)
            {
                writer.WriteLine(@"
### Notes:
");
                writer.WriteLine(tool.Notes);
            }

            if (tool.WindowsExePaths.Any())
            {
                writer.WriteLine(@"
### Windows settings:
");
                WriteArguments(writer, tool.BuildWindowsArguments!);
                WritePaths(writer, tool.WindowsExePaths);
            }

            if (tool.OsxExePaths.Any())
            {
                writer.WriteLine(@"
### OSX settings:
");
                WriteArguments(writer, tool.BuildOsxArguments!);
                WritePaths(writer, tool.OsxExePaths);
            }

            if (tool.LinuxExePaths.Any())
            {
                writer.WriteLine(@"
### Linux settings:
");
                WriteArguments(writer, tool.BuildLinuxArguments!);
                WritePaths(writer, tool.LinuxExePaths);
            }
        }
    }

    static void WriteArguments(StreamWriter writer, BuildArguments buildArguments)
    {
        var argumentsWithTarget = buildArguments("tempFile", "targetFile");
        writer.WriteLine($@"
 * Example arguments: `{argumentsWithTarget}`");
    }

    static void WritePaths(TextWriter writer, string[] paths)
    {
        if (paths.Length > 1)
        {
            writer.WriteLine(@" * Scanned paths:
");
            foreach (var path in paths)
            {
                writer.WriteLine($@"   * `{path}`");
            }
        }
        else
        {
            writer.WriteLine($@" * Scanned path: `{paths.Single()}`");
        }
    }

    [Fact]
    public void WriteDefaultDiffToolOrder()
    {
        var md = Path.Combine(SourceDirectory, "defaultDiffToolOrder.include.md");
        File.Delete(md);
        using var writer = File.CreateText(md);

        foreach (var tool in DiffTools.Tools())
        {
            writer.WriteLine($@" * {tool.Name}");
        }
    }

    //[Fact]
    //public void LaunchImageDiff()
    //{
    //    foreach (var tool in DiffTools.ResolvedDiffTools)
    //    {
    //        DiffRunner.Launch(tool,
    //            Path.Combine(SourceDirectory, "input.file1.png"),
    //            Path.Combine(SourceDirectory, "input.file2.png"));
    //    }
    //}

    //[Fact]
    //public void LaunchTextDiff()
    //{
    //    foreach (var tool in DiffTools.ResolvedDiffTools)
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

        Assert.True(DiffTools.TryFind(DiffTool.VisualStudio, "txt", out resolved));
        Assert.NotNull(resolved);

        Assert.False(DiffTools.TryFind("notFound", out resolved));
        Assert.Null(resolved);

        Assert.False(DiffTools.TryFind(DiffTool.VisualStudio, "notFound", out resolved));
        Assert.Null(resolved);

        Assert.False(DiffTools.TryFind(DiffTool.Kaleidoscope, "txt", out resolved));
        Assert.Null(resolved);
    }
#endif

    public DiffToolsTest(ITestOutputHelper output) :
        base(output)
    {
    }
}