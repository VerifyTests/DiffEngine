static partial class Implementation
{
    public static Definition VisualStudioCode()
    {
        static string LeftArguments(string temp, string target)
        {
            return $"--diff \"{target}\" \"{temp}\"";
        }

        static string RightArguments(string temp, string target)
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
                LeftArguments,
                RightArguments,
                @"%LocalAppData%\Programs\Microsoft VS Code\",
                @"%ProgramFiles%\Microsoft VS Code\",
                @"%UserProfile%\scoop\apps\vscode\current\"),
            linux: new(
                "code",
                LeftArguments,
                RightArguments),
            osx: new(
                "code",
                LeftArguments,
                RightArguments,
                "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/"),
            notes: @"
 * [Command line reference](https://code.visualstudio.com/docs/editor/command-line)");
    }
}