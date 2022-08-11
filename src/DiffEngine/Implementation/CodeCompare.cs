static partial class Implementation
{
    public static Definition CodeCompare() =>
        new(
            name: DiffTool.CodeCompare,
            url: "https://www.devart.com/codecompare/",
            autoRefresh: false,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            cost: "Paid",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "CodeCompare.exe",
                new(
                    Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                    Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                @"%ProgramFiles%\Devart\Code Compare\"),
            notes: @"
 * [Command line reference](https://docs.devart.com/code-compare/using-command-line/comparing-via-command-line.html)");
}