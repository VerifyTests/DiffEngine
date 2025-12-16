static partial class Implementation
{
    public static Definition MsWordDiff()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"\"{target}\" \"{temp}\"",
            Right: (temp, target) => $"\"{temp}\" \"{target}\"");

        return new(
            Tool: DiffTool.MsWordDiff,
            Url: "https://github.com/SimonCropp/MsWordDiff",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: false,
            UseShellExecute: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions:
            [
                ".docx",
                ".doc"
            ],
            OsSupport: new(
                Windows: new(
                    "diffword.exe",
                    launchArguments,
                    @"%USERPROFILE%\.dotnet\tools\")),
            Notes: """
                 * Install via `dotnet tool install -g MsWordDiff`
                 * Requires Microsoft Word to be installed
                 * Uses Word's built-in document comparison feature
                """);
    }
}
