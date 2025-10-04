static partial class Implementation
{
    public static Definition VisualStudioCode()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"--diff \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"--diff \"{temp}\" \"{target}\"");
        return new(
            Tool: DiffTool.VisualStudioCode,
            Url: "https://code.visualstudio.com",
            AutoRefresh: true,
            IsMdi: true,
            SupportsText: true,
            UseShellExecute: false,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions:
            [
                ".svg",
                ".bin"
            ],
            OsSupport: new(
                Windows: new(
                    "code.cmd",
                    launchArguments,
                    @"%LocalAppData%\Programs\Microsoft VS Code\bin\",
                    @"%ProgramFiles%\Microsoft VS Code\bin\"),
                Linux: new(
                    "code",
                    launchArguments),
                Osx: new(
                    "code",
                    launchArguments,
                    "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/")),
            Notes: " * [Command line reference](https://code.visualstudio.com/docs/editor/command-line)");
    }
}