using System.Collections.Generic;
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

        foreach (var tool in Definitions.Tools
            .OrderBy(x => x.Tool.ToString()))
        {
            AddToolLink(writer, tool);
        }
    }

    static void AddToolLink(StreamWriter writer, Definition tool)
    {
        writer.WriteLine($" * [{tool.Tool}](/docs/diff-tool.md#{tool.Tool.ToString().ToLower()})");
    }

    [Fact]
    public void WriteDefaultOrder()
    {
        var md = Path.Combine(SourceDirectory, "defaultOrder.include.md");
        File.Delete(md);
        using var writer = File.CreateText(md);

        foreach (var tool in Definitions.Tools)
        {
            AddToolLink(writer, tool);
        }
    }

    [Fact]
    public void WriteFoundTools()
    {
        var md = Path.Combine(SourceDirectory, "diffTools.include.md");
        File.Delete(md);
        using var writer = File.CreateText(md);

        writer.WriteLine($@"

## Non-MDI tools

Non-MDI tools are prefered since it allows [DiffEngineTray](tray.md) to track and close diffs.

");

        foreach (var tool in Definitions.Tools
            .Where(x => !x.IsMdi)
            .OrderBy(x => x.Tool.ToString()))
        {
            AddTool(writer, tool);
        }

        writer.WriteLine($@"

## MDI tools

");
        foreach (var tool in Definitions.Tools
            .Where(x => x.IsMdi)
            .OrderBy(x => x.Tool.ToString()))
        {
            AddTool(writer, tool);
        }
    }

    static void AddTool(StreamWriter writer, Definition tool)
    {
        writer.WriteLine($@"

### [{tool.Tool}]({tool.Url})");

        writer.WriteLine($@"
  * Supports auto-refresh: {tool.AutoRefresh}
  * Supports text files: {tool.SupportsText}");

        if (tool.BinaryExtensions.Any())
        {
            writer.WriteLine("  * Supported binaries: " + string.Join(", ", tool.BinaryExtensions));
        }

        if (tool.Notes != null)
        {
            writer.WriteLine(@"
#### Notes:");
            writer.WriteLine(tool.Notes);
        }

        if (tool.Windows != null)
        {
            writer.WriteLine(@"
#### Windows settings:");
            WriteArguments(writer, tool.Windows.Arguments!);
            WritePaths(writer, OsSettingsResolver.ExpandProgramFiles(tool.Windows.ExePaths).ToList());
        }

        if (tool.Osx != null)
        {
            writer.WriteLine(@"
#### OSX settings:");
            WriteArguments(writer, tool.Osx.Arguments);
            WritePaths(writer, tool.Osx.ExePaths);
        }

        if (tool.Linux != null)
        {
            writer.WriteLine(@"
#### Linux settings:");
            WriteArguments(writer, tool.Linux.Arguments);
            WritePaths(writer, tool.Linux.ExePaths);
        }
    }

    static void WriteArguments(StreamWriter writer, BuildArguments buildArguments)
    {
        var argumentsWithTarget = buildArguments("tempFile", "targetFile");
        writer.WriteLine($@"
 * Example arguments: `{argumentsWithTarget}`");
    }

    static void WritePaths(TextWriter writer, IReadOnlyCollection<string> paths)
    {
        if (paths.Count > 1)
        {
            writer.WriteLine(@" * Scanned paths:
");
            foreach (var path in paths)
            {
                writer.WriteLine($"   * `{path}`");
            }
        }
        else
        {
            writer.WriteLine($" * Scanned path: `{paths.Single()}`");
        }
    }


    public DefinitionsTest(ITestOutputHelper output) :
        base(output)
    {
    }
}