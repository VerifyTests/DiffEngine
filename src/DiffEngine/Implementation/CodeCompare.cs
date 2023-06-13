static partial class Implementation
{
    public static Definition CodeCompare() =>
        new(
            Tool: DiffTool.CodeCompare,
            Url: "https://www.devart.com/codecompare/",
            AutoRefresh: false,
            IsMdi: true,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Paid",
            BinaryExtensions: Array.Empty<string>(),
            OsSupport: new(
                Windows: new(
                    "CodeCompare.exe",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                    @"%ProgramFiles%\Devart\Code Compare\")),
            Notes: " * [Command line reference](https://docs.devart.com/code-compare/using-command-line/comparing-via-command-line.html)");
}