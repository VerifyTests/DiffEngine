using System.IO;
using System.Linq;
using DiffEngine;
using Xunit;
using Xunit.Abstractions;

public class ToolDefinitionsTest :
    XunitContextBase
{
    [Fact]
    public void WriteList()
    {
        var md = Path.Combine(SourceDirectory, "diffToolList.include.md");
        File.Delete(md);
        using var writer = File.CreateText(md);
        var tools = ToolDefinitions
            .Tools();

        foreach (var tool in tools
            .OrderBy(x => x.Tool.ToString()))
        {
            writer.WriteLine($@" * [{tool.Tool}](/docs/diff-tool.md#{tool.Tool.ToString().ToLower()})");
        }
    }

    [Fact]
    public void WriteDefaultDiffToolOrder()
    {
        var md = Path.Combine(SourceDirectory, "defaultDiffToolOrder.include.md");
        File.Delete(md);
        using var writer = File.CreateText(md);

        foreach (var tool in ToolDefinitions.Tools())
        {
            writer.WriteLine($@" * {tool.Tool}");
        }
    }

    [Fact]
    public void WriteFoundTools()
    {
        var md = Path.Combine(SourceDirectory, "diffTools.include.md");
        File.Delete(md);
        using var writer = File.CreateText(md);
        var tools = ToolDefinitions
            .Tools();

        foreach (var tool in tools
            .OrderBy(x => x.Tool.ToString()))
        {
            writer.WriteLine($@"
## [{tool.Tool}]({tool.Url})");

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
                WriteArguments(writer, tool.WindowsArguments!);
                WritePaths(writer, tool.WindowsExePaths);
            }

            if (tool.OsxExePaths.Any())
            {
                writer.WriteLine(@"
### OSX settings:
");
                WriteArguments(writer, tool.OsxArguments!);
                WritePaths(writer, tool.OsxExePaths);
            }

            if (tool.LinuxExePaths.Any())
            {
                writer.WriteLine(@"
### Linux settings:
");
                WriteArguments(writer, tool.LinuxArguments!);
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


    public ToolDefinitionsTest(ITestOutputHelper output) :
        base(output)
    {
    }
}