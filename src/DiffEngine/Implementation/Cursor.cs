static partial class Implementation
{
    public static Definition Cursor()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"--diff \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"--diff \"{temp}\" \"{target}\"");
        return new(
            Tool: DiffTool.Cursor,
            Url: "https://cursor.com",
            AutoRefresh: true,
            IsMdi: true,
            SupportsText: true,
            UseShellExecute: false,
            RequiresTarget: true,
            Cost: "Free and Paid",
            BinaryExtensions:
            [
                ".svg",
                ".bin"
            ],
            OsSupport: new(
                Windows: new(
                    "Cursor.exe",
                    launchArguments,
                    @"%ProgramFiles%\Cursor\"),
                Linux: new(
                    "cursor",
                    launchArguments),
                Osx: new(
                    "cursor",
                    launchArguments,
                    "/Applications/Cursor.app/Contents/MacOS")),
            Notes: " * [Command line reference](https://cursor.com/docs/configuration/shell)");
    }
}