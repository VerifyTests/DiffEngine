namespace DiffEngine;

static partial class Implementation
{
    public static Definition CodeCompare()
    {
        // Left to itself Code Compare picks where a comparison opens: the Visual Studio a file
        // belongs to a project of, a running Code Compare, or any running Visual Studio, and with
        // more than one of them open it stops and asks which. A test run has the verified file in
        // a project, so the diff would go into Visual Studio, which is a tool of its own here
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"/environment=standalone \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"/environment=standalone \"{temp}\" \"{target}\"");

        return new(
            Tool: DiffTool.CodeCompare,
            Url: "https://www.devart.com/codecompare/",
            AutoRefresh: false,
            // A comparison opens as a tab in whichever Code Compare is already running
            IsMdi: true,
            SupportsText: true,
            UseShellExecute: true,
            RequiresTarget: true,
            Cost: "Free and Paid",
            BinaryExtensions: [],
            OsSupport: new(
                Windows: new(
                    "CodeCompare.exe",
                    launchArguments,
                    @"%ProgramFiles%\Devart\Code Compare\")),
            Notes: """
                 * [Command line reference](https://docs.devart.com/code-compare/using-command-line/comparing-via-command-line.html)
                 * Launched with `/environment=standalone`, so a diff always opens in Code Compare rather than in a Visual Studio with the Code Compare extension, and never stops to ask which. [Environment on comparison opening](https://docs.devart.com/code-compare/using-command-line/choosing-environment-on-comparison-opening.html)
                """);
    }
}
