static partial class Implementation
{
    public static Definition VisualStudioCode()
    {
        static string TargetLeftArguments(string temp, string target)
        {
            return $"--diff \"{target}\" \"{temp}\"";
        }

        static string TargetRightArguments(string temp, string target)
        {
            return $"--diff \"{temp}\" \"{target}\"";
        }

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
                TargetLeftArguments,
                TargetRightArguments,
                @"%LocalAppData%\Programs\Microsoft VS Code\",
                @"%ProgramFiles%\Microsoft VS Code\",
                @"%UserProfile%\scoop\apps\vscode\current\"),
            linux: new(
                "code",
                TargetLeftArguments,
                TargetRightArguments,
                "/usr/local/bin/",
                "/usr/bin/"),
            osx: new(
                "code",
                TargetLeftArguments,
                TargetRightArguments,
                "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/"),
            notes: @"
 * [Command line reference](https://code.visualstudio.com/docs/editor/command-line)");
    }
}