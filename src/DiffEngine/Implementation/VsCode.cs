static partial class Implementation
{
    public static Definition VisualStudioCode()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"--diff \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"--diff \"{temp}\" \"{target}\"");
        var environmentVariable = $"${DefaultEnvironmentVariablePrefix}_{nameof(DiffTool.VisualStudioCode)}";
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
                    environmentVariable,
                    "code.exe",
                    launchArguments,
                    @"%LocalAppData%\Programs\Microsoft VS Code\",
                    @"%ProgramFiles%\Microsoft VS Code\"),
                Linux: new(
                    environmentVariable,
                    "code",
                    launchArguments),
                Osx: new(
                    environmentVariable,
                    "code",
                    launchArguments,
                    "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/")),
                Notes: @" * [Command line reference](https://code.visualstudio.com/docs/editor/command-line)");
    }
}