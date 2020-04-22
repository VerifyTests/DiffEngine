using System.IO;
using System.Linq;
using DiffEngine;
using Xunit;
using Xunit.Abstractions;

public class DefinitionsTest :
    XunitContextBase
{
    [Fact]
    public void WriteList()
    {
        var md = Path.Combine(SourceDirectory, "diffToolList.include.md");
        File.Delete(md);
        using var writer = File.CreateText(md);
        var tools = Definitions
            .Tools();

        foreach (var tool in tools
            .OrderBy(x => x.Tool.ToString()))
        {
            writer.WriteLine($@" * [{tool.Tool}](/docs/diff-tool.md#{tool.Tool.ToString().ToLower()})");
        }
    }

    [Fact]
    public void WriteDefaultOrder()
    {
        var md = Path.Combine(SourceDirectory, "defaultOrder.include.md");
        File.Delete(md);
        using var writer = File.CreateText(md);

        foreach (var tool in Definitions.Tools())
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
        var tools = Definitions
            .Tools();

        foreach (var tool in tools
            .OrderBy(x => x.Tool.ToString()))
        {
            writer.WriteLine($@"
## [{tool.Tool}]({tool.Url})");

            writer.WriteLine($@"
  * Is MDI: {tool.IsMdi}
  * Supports auto-refresh: {tool.AutoRefresh}
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

            if (tool.WindowsPaths.Any())
            {
                writer.WriteLine(@"
### Windows settings:
");
                WriteArguments(writer, tool.WindowsArguments!);
                WritePaths(writer, tool.WindowsPaths);
            }

            if (tool.OsxPaths.Any())
            {
                writer.WriteLine(@"
### OSX settings:
");
                WriteArguments(writer, tool.OsxArguments!);
                WritePaths(writer, tool.OsxPaths);
            }

            if (tool.LinuxPaths.Any())
            {
                writer.WriteLine(@"
### Linux settings:
");
                WriteArguments(writer, tool.LinuxArguments!);
                WritePaths(writer, tool.LinuxPaths);
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


    public DefinitionsTest(ITestOutputHelper output) :
        base(output)
    {
    }
}