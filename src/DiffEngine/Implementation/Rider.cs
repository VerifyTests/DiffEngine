static partial class Implementation
{
    public static Definition Rider()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"diff \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"diff \"{temp}\" \"{target}\"");

        return new(
            Tool: DiffTool.Rider,
            Url: "https://www.jetbrains.com/rider/",
            AutoRefresh: true,
            IsMdi: false,
            SupportsText: true,
            UseShellExecute: true,
            RequiresTarget: true,
            Cost: "Paid with free option for OSS",
            BinaryExtensions: [".svg"],
            OsSupport: new(
                Windows: new(
                    "rider64.exe",
                    "rider.cmd",
                    launchArguments,
                    @"%LOCALAPPDATA%\Programs\Rider*\bin\",
                    @"%ProgramFiles%\JetBrains\JetBrains Rider *\bin\"),
                Osx: new(
                    "rider",
                    launchArguments,
                    "/Applications/Rider.app/Contents/MacOS/",
                    "/usr/local/bin/"),
                Linux: new(
                    "rider.sh",
                    launchArguments,
                    "%HOME%/.local/share/JetBrains/Toolbox/apps/rider/bin/")),
            Notes: " * https://www.jetbrains.com/help/rider/Command_Line_Differences_Viewer.html");
    }
}