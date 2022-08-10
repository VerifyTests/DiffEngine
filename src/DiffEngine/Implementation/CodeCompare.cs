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
                (temp, target) => $"\"{target}\" \"{temp}\"",
                (temp, target) => $"\"{temp}\" \"{target}\"",
                @"%ProgramFiles%\Devart\Code Compare\"),
            notes: @"
 * [Command line reference](https://docs.devart.com/code-compare/using-command-line/comparing-via-command-line.html)");
}