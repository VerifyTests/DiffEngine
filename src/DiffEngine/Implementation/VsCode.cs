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
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: Array.Empty<string>(),
            OsSupport: new(
                Windows: new(
                    "code.exe",
                    launchArguments,
                    @"%LocalAppData%\Programs\Microsoft VS Code\",
                    @"%ProgramFiles%\Microsoft VS Code\"),
                Linux: new(
                    "code",
                    launchArguments),
                Osx: new(
                    "code",
                    launchArguments,
                    "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/")),
                Notes: @" * [Command line reference](https://code.visualstudio.com/docs/editor/command-line)");
    }
}