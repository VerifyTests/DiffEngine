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
        var osSupport = GetOsSupport(tool.OsSupport);
        writer.WriteLine($" * **[{tool.Tool}](/docs/diff-tool.md#{tool.Tool.ToString().ToLower()})** {osSupport} (Cost: {tool.Cost})");
    }

    static string GetOsSupport(OsSupport osSupport)
    {
        var builder = new StringBuilder();
        if (osSupport.Windows != null)
        {
            builder.Append("Win/");
        }

        if (osSupport.Osx != null)
        {
            builder.Append("OSX/");
        }

        if (osSupport.Linux != null)
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
            writer.WriteLine($"""

                              #### Notes:
                              
                              {tool.Notes}
                              """);
        }

        var osSupport = tool.OsSupport;
        var windows = osSupport.Windows;
        if (windows != null)
        {
            writer.WriteLine("""

                             #### Windows settings:

                             """);
            WriteArguments(writer, windows.LaunchArguments);
            WritePaths(windows.ExeName, writer, OsSettingsResolver.ExpandProgramFiles(windows.SearchDirectories).ToList());
        }

        var osx = osSupport.Osx;
        if (osx != null)
        {
            writer.WriteLine("""
            
                             #### OSX settings:
                             
                             """);
            WriteArguments(writer, osx.LaunchArguments);
            WritePaths(osx.ExeName, writer, osx.SearchDirectories);
        }

        var linux = osSupport.Linux;
        if (linux != null)
        {
            writer.WriteLine("""
            
                             #### Linux settings:
                             
                             """);
            WriteArguments(writer, linux.LaunchArguments);
            WritePaths(linux.ExeName, writer, linux.SearchDirectories);
        }
    }

    static void WriteArguments(StreamWriter writer, LaunchArguments launchArguments)
    {
        var left = launchArguments.Left("tempFile", "targetFile");
        var right = launchArguments.Right("tempFile", "targetFile");
        writer.WriteLine($"""
                           * Example target on left arguments: `{left}`
                           * Example target on right arguments: `{right}`
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