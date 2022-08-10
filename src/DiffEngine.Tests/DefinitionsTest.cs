public class DefinitionsTest :
    XunitContextBase
{
    [Fact]
    public void WriteList()
    {
        var md = Path.Combine(SourceDirectory, "diffToolList.include.md");
        File.Delete(md);
        using var writer = File.CreateText(md);

        foreach (var tool in Definitions.Tools)
        {
            AddToolLink(writer, tool);
        }
    }

    static void AddToolLink(StreamWriter writer, Definition tool)
    {
        var osSupport = GetOsSupport(tool);
        writer.WriteLine($" * **[{tool.Tool}](/docs/diff-tool.md#{tool.Tool.ToString().ToLower()})** {osSupport} (Cost: {tool.Cost})");
    }

    static string GetOsSupport(Definition tool)
    {
        var builder = new StringBuilder();
        if (tool.Windows != null)
        {
            builder.Append("Win/");
        }

        if (tool.Osx != null)
        {
            builder.Append("OSX/");
        }

        if (tool.Linux != null)
        {
            builder.Append("Linux/");
        }

        builder.Length--;
        return builder.ToString();
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

        writer.WriteLine("""


                         ## Non-MDI tools
 
                         Non-MDI tools are preferred since it allows [DiffEngineTray](tray.md) to track and close diffs.
 
                         """);

        foreach (var tool in Definitions.Tools
                     .Where(x => !x.IsMdi)
                     .OrderBy(x => x.Tool.ToString()))
        {
            AddTool(writer, tool);
        }

        writer.WriteLine("""

                         ## MDI tools

                         """);
        foreach (var tool in Definitions.Tools
                     .Where(x => x.IsMdi)
                     .OrderBy(x => x.Tool.ToString()))
        {
            AddTool(writer, tool);
        }
    }

    static void AddTool(StreamWriter writer, Definition tool)
    {
        writer.WriteLine($"""
    
                         ### [{tool.Tool}]({tool.Url})
    
                         """);

        writer.WriteLine($"""
    
                           * Cost: {tool.Cost}
                           * Is MDI: {tool.IsMdi}
                           * Supports auto-refresh: {tool.AutoRefresh}
                           * Supports text files: {tool.SupportsText}
                         """);

        if (tool.BinaryExtensions.Any())
        {
            writer.WriteLine($"  * Supported binaries: {string.Join(", ", tool.BinaryExtensions)}");
        }

        if (tool.Notes != null)
        {
            writer.WriteLine("""

                             #### Notes:

                             """);
            writer.WriteLine(tool.Notes);
        }

        var windows = tool.Windows;
        if (windows != null)
        {
            writer.WriteLine("""

                             #### Windows settings:

                             """);
            WriteArguments(writer, windows);
            WritePaths(windows.ExeName, writer, OsSettingsResolver.ExpandProgramFiles(windows.SearchDirectories).ToList());
        }

        var osx = tool.Osx;
        if (osx != null)
        {
            writer.WriteLine("""
            
                             #### OSX settings:
                             
                             """);
            WriteArguments(writer, osx);
            WritePaths(osx.ExeName, writer, osx.SearchDirectories);
        }

        var linux = tool.Linux;
        if (linux != null)
        {
            writer.WriteLine("""
            
                             #### Linux settings:
                             
                             """);
            WriteArguments(writer, linux);
            WritePaths(linux.ExeName, writer, linux.SearchDirectories);
        }
    }

    static void WriteArguments(StreamWriter writer, OsSettings osSettings)
    {
        var leftArguments = osSettings.LeftArguments("tempFile", "targetFile");
        var rightArguments = osSettings.RightArguments("tempFile", "targetFile");
        writer.WriteLine($"""
                           * Example target on left arguments: `{leftArguments}`
                           * Example target on right arguments: `{rightArguments}`
                         """);
    }

    static void WritePaths(string exeName, TextWriter writer, IReadOnlyCollection<string> paths)
    {
        writer.WriteLine("""
                           * Scanned paths:  
                         """);
        writer.WriteLine($"    * `%PATH%{exeName}`");
        foreach (var path in paths)
        {
            writer.WriteLine($"    * `{path}{exeName}`");
        }
    }

    public DefinitionsTest(ITestOutputHelper output) :
        base(output)
    {
    }
}