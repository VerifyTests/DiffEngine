static partial class Implementation
{
    public static Definition VisualStudioCode()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"--diff \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"--diff \"{temp}\" \"{target}\"");

        return new(
            name: DiffTool.VisualStudioCode,
            url: "https://code.visualstudio.com",
            autoRefresh: true,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "code.exe",
                launchArguments,
                @"%LocalAppData%\Programs\Microsoft VS Code\",
                @"%ProgramFiles%\Microsoft VS Code\"),
            linux: new(
                "code",
                launchArguments),
            osx: new(
                "code",
                launchArguments,
                "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/"),
            notes: @"
 * [Command line reference](https://code.visualstudio.com/docs/editor/command-line)");
    }
}