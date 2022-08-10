static partial class Implementation
{
    public static Definition Rider()
    {
        static string LeftArguments(string temp, string target)
        {
            return $"diff \"{target}\" \"{temp}\"";
        }

        static string RightArguments(string temp, string target)
        {
            return $"diff \"{temp}\" \"{target}\"";
        }

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
                LeftArguments,
                RightArguments,
                @"%LOCALAPPDATA%\JetBrains\Installations\Rider*\bin\",
                @"%ProgramFiles%\JetBrains\JetBrains Rider *\bin\",
                @"%JetBrains Rider%\",
                @"%LOCALAPPDATA%\JetBrains\Toolbox\apps\Rider\*\*\bin\",
                @"%UserProfile%\scoop\apps\rider\current\IDE\bin\"),
            osx: new(
                "rider",
                LeftArguments,
                RightArguments,
                "%HOME%/Library/Application Support/JetBrains/Toolbox/apps/Rider/*/*/Rider EAP.app/Contents/MacOS/",
                "%HOME%/Library/Application Support/JetBrains/Toolbox/apps/Rider/*/*/Rider.app/Contents/MacOS/",
                "/Applications/Rider EAP.app/Contents/MacOS/",
                "/Applications/Rider.app/Contents/MacOS/"),
            linux: new(
                "rider.sh",
                LeftArguments,
                RightArguments,
                "%HOME%/.local/share/JetBrains/Toolbox/apps/Rider/*/*/bin/",
                "/opt/jetbrains/rider/bin/",
                "/usr/share/rider/bin/"),
            notes: @"
 * https://www.jetbrains.com/help/rider/Command_Line_Differences_Viewer.html");
    }
}