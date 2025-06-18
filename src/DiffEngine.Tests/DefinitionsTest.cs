public class DefinitionsTest(ITestOutputHelper output) :
    XunitContextBase(output)
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

    [Fact]
    public void EnvironmentVariablesShouldBeUnique()
    {
        static void FindDuplicates(Func<OsSupport, OsSettings?> selectOs)
        {
            var findDuplicates = Definitions.Tools
                .Select(_ => _.OsSupport)
                .Select(selectOs)
                .Where(_ => _ is not null)
                .GroupBy(_ => _);
            foreach (var group in findDuplicates)
            {
                Assert.Single(group);
            }
        }

        FindDuplicates(_ => _.Windows);
        FindDuplicates(_ => _.Osx);
        FindDuplicates(_ => _.Linux);
    }

    static void AddToolLink(TextWriter writer, Definition tool)
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

        writer.WriteLine(
            """

            ## Non-MDI tools

            Non-MDI tools are preferred since it allows [DiffEngineTray](tray.md) to track and close diffs.

            """);

        foreach (var tool in Definitions.Tools
                     .Where(_ => !_.IsMdi)
                     .OrderBy(_ => _.Tool.ToString()))
        {
            AddTool(writer, tool);
        }

        writer.WriteLine(
            """

            ## MDI tools

            """);
        foreach (var tool in Definitions.Tools
                     .Where(_ => _.IsMdi)
                     .OrderBy(_ => _.Tool.ToString()))
        {
            AddTool(writer, tool);
        }
    }

    static void AddTool(StreamWriter writer, Definition tool)
    {
        writer.WriteLine(
            $"""

             ### [{tool.Tool}]({tool.Url})

               * Cost: {tool.Cost}
               * Is MDI: {tool.IsMdi}
               * Supports auto-refresh: {tool.AutoRefresh}
               * Supports text files: {tool.SupportsText}
               * Use shell execute: {tool.UseShellExecute}
               * Environment variable for custom install location: `DiffEngine_{tool.Tool}`
             """);

        if (tool.BinaryExtensions.Length != 0)
        {
            writer.WriteLine($"  * Supported binaries: {string.Join(", ", tool.BinaryExtensions.OrderBy(_ => _))}");
        }

        if (tool.Notes != null)
        {
            writer.WriteLine(
                $"""

                 #### Notes:

                 {tool.Notes}
                 """);
        }

        var (windows, linux, osx) = tool.OsSupport;
        if (windows != null)
        {
            writer.WriteLine(
                """

                #### Windows settings:

                """);
            WriteArguments(writer, windows.LaunchArguments);
            WritePaths(windows.ExeName, windows.PathCommandName, writer, OsSettingsResolver.ExpandProgramFiles(windows.SearchDirectories).ToList());
        }

        if (osx != null)
        {
            writer.WriteLine(
                """

                #### OSX settings:

                """);
            WriteArguments(writer, osx.LaunchArguments);
            WritePaths(osx.ExeName, osx.PathCommandName, writer, osx.SearchDirectories);
        }

        if (linux != null)
        {
            writer.WriteLine(
                """

                #### Linux settings:

                """);
            WriteArguments(writer, linux.LaunchArguments);
            WritePaths(linux.ExeName, linux.PathCommandName, writer, linux.SearchDirectories);
        }
    }

    static void WriteArguments(StreamWriter writer, LaunchArguments arguments)
    {
        var leftText = arguments.Left("tempFile.txt", "targetFile.txt");
        var rightText = arguments.Right("tempFile.txt", "targetFile.txt");
        var leftBinary = arguments.Left("tempFile.png", "targetFile.png");
        var rightBinary = arguments.Right("tempFile.png", "targetFile.png");
        if (leftText.Replace(".txt", "") == leftBinary.Replace(".png", ""))
        {
            writer.WriteLine(
                $"""
                   * Example target on left arguments:
                    `{leftText}`
                   * Example target on right arguments:
                    `{rightText}`
                 """);
        }
        else
        {
            writer.WriteLine(
                $"""
                   * Example target on left arguments for text:
                    `{leftText}`
                   * Example target on right arguments for text:
                    `{rightText}`
                   * Example target on left arguments for binary:
                    `{leftBinary}`
                   * Example target on right arguments for binary:
                    `{rightBinary}`
                 """);
        }
    }

    static void WritePaths(string exeName, string pathCommandName, TextWriter writer, IReadOnlyCollection<string> paths)
    {
        writer.WriteLine("  * Scanned paths:");

        foreach (var path in paths)
        {
            writer.WriteLine($"    * `{path}{exeName}`");
        }

        writer.WriteLine($"    * `%PATH%{pathCommandName}`");
    }
}