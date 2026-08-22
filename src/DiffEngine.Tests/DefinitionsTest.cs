public class DefinitionsTest
{
    static string SourceDirectory { get; } = Path.GetDirectoryName(GetSourceFile())!;
    static string GetSourceFile([CallerFilePath] string path = "") => path;

    // the repo stores markdown as lf, so dont let Environment.NewLine leak into the generated files
    static StreamWriter CreateWriter(string path)
    {
        File.Delete(path);
        return new(path)
        {
            NewLine = "\n"
        };
    }

    [Test]
    public void WriteList()
    {
        var md = Path.Combine(SourceDirectory, "diffToolList.include.md");
        using var writer = CreateWriter(md);

        foreach (var tool in Definitions.Tools.OrderBy(_ => _.Tool.ToString()))
        {
            AddToolLink(writer, tool);
        }
    }

    [Test]
    public async Task EnvironmentVariablesShouldBeUnique()
    {
        static async Task FindDuplicates(Func<OsSupport, OsSettings?> selectOs)
        {
            var findDuplicates = Definitions.Tools
                .Select(_ => _.OsSupport)
                .Select(selectOs)
                .Where(_ => _ is not null)
                .GroupBy(_ => _);
            foreach (var group in findDuplicates)
            {
                await Assert.That(group).HasSingleItem();
            }
        }

        await FindDuplicates(_ => _.Windows);
        await FindDuplicates(_ => _.Osx);
        await FindDuplicates(_ => _.Linux);
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
            builder.Append("Windows/");
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

    /// <summary>
    /// Both files say "keep in sync", and this is what checks it. Ordered, because the order is
    /// the thing being kept in sync: the enum is the default resolution order (OrderReader hands
    /// back Enum.GetValues), while the generated docs and the remainder after a
    /// DiffEngine_ToolOrder prefix follow Definitions. IsEquivalentTo is order insensitive by
    /// default, so the two drifted apart under a green test.
    /// </summary>
    [Test]
    public async Task ToolOrderMatchesEnumOrder()
    {
        // Joined, because the collection assertions here are order insensitive and the order is
        // the whole point. A string also names the first tool that differs, rather than saying the
        // two lists have the same contents
        var definitionsOrder = string.Join(", ", Definitions.Tools.Select(_ => _.Tool));
        var enumOrder = string.Join(", ", Enum.GetValues(typeof(DiffTool)).Cast<DiffTool>());
        await Assert.That(definitionsOrder).IsEqualTo(enumOrder);
    }

    [Test]
    public void WriteDefaultOrder()
    {
        var md = Path.Combine(SourceDirectory, "defaultOrder.include.md");
        using var writer = CreateWriter(md);

        foreach (var tool in Definitions.Tools)
        {
            AddToolLink(writer, tool);
        }
    }

    [Test]
    public void WriteFoundTools()
    {
        var md = Path.Combine(SourceDirectory, "diffTools.include.md");
        using var writer = CreateWriter(md);

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
               * Create no window: {tool.CreateNoWindow}
               * Environment variable for custom install location: `DiffEngine_{tool.Tool}`
             """);

        if (tool.BinaryExtensions.Length != 0)
        {
            writer.WriteLine($"  * Supported binaries: {string.Join(", ", tool.BinaryExtensions.OrderBy(_ => _))}");
        }

        writer.WriteLine(
            $"""

             #### Tool order:

             Use [tool order](diff-tool.order.md) to prioritise {tool.Tool} over other tools.

             ```
             DiffTools.UseOrder(DiffTool.{tool.Tool});
             ```
             """);
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
                    ```
                    {leftText}
                    ```
                   * Example target on right arguments:
                    ```
                    {rightText}
                    ```
                 """);
        }
        else
        {
            writer.WriteLine(
                $"""
                   * Example target on left arguments for text:
                    ```
                    {leftText}
                    ```
                   * Example target on right arguments for text:
                    ```
                    {rightText}
                    ```
                   * Example target on left arguments for binary:
                    ```
                    {leftBinary}
                    ```
                   * Example target on right arguments for binary:
                    ```
                    {rightBinary}
                    ```
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
