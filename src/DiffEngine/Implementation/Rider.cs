static partial class Implementation
{
    public static Definition Rider()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"diff \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"diff \"{temp}\" \"{target}\"");

        return new(
            name: DiffTool.Rider,
            url: "https://www.jetbrains.com/rider/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Paid with free option for OSS",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "rider64.exe",
                launchArguments,
                @"%LOCALAPPDATA%\JetBrains\Installations\Rider*\bin\",
                @"%ProgramFiles%\JetBrains\JetBrains Rider *\bin\",
                @"%JetBrains Rider%\",
                @"%LOCALAPPDATA%\JetBrains\Toolbox\apps\Rider\*\*\bin\"),
            osx: new(
                "rider",
                launchArguments,
                "%HOME%/Library/Application Support/JetBrains/Toolbox/apps/Rider/*/*/Rider EAP.app/Contents/MacOS/",
                "%HOME%/Library/Application Support/JetBrains/Toolbox/apps/Rider/*/*/Rider.app/Contents/MacOS/",
                "/Applications/Rider EAP.app/Contents/MacOS/",
                "/Applications/Rider.app/Contents/MacOS/"),
            linux: new(
                "rider.sh",
                launchArguments,
                "%HOME%/.local/share/JetBrains/Toolbox/apps/Rider/*/*/bin/",
                "/opt/jetbrains/rider/bin/",
                "/usr/share/rider/bin/"),
            notes: @"
 * https://www.jetbrains.com/help/rider/Command_Line_Differences_Viewer.html");
    }
}