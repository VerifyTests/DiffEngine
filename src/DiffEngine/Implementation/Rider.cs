using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition Rider()
    {
        static string Arguments(string temp, string target) =>
            $"diff \"{temp}\" \"{target}\"";

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
                Arguments,
                @"%LOCALAPPDATA%\JetBrains\Installations\Rider*\bin\rider64.exe",
                @"%ProgramFiles%\JetBrains\JetBrains Rider *\bin\rider64.exe",
                @"%JetBrains Rider%\rider64.exe",
                @"%LOCALAPPDATA%\JetBrains\Toolbox\apps\Rider\*\*\bin\rider64.exe"),
            osx: new(
                Arguments,
                "%HOME%/Library/Application Support/JetBrains/Toolbox/apps/Rider/*/*/Rider EAP.app/Contents/MacOS/rider",
                "%HOME%/Library/Application Support/JetBrains/Toolbox/apps/Rider/*/*/Rider.app/Contents/MacOS/rider",
                "/Applications/Rider EAP.app/Contents/MacOS/rider",
                "/Applications/Rider.app/Contents/MacOS/rider"),
            linux: new(
                Arguments,
                "%HOME%/.local/share/JetBrains/Toolbox/apps/Rider/*/*/bin/rider.sh",
                "/opt/jetbrains/rider/bin/rider.sh",
                "/usr/share/rider/bin/rider.sh"),
            notes: @"
 * https://www.jetbrains.com/help/rider/Command_Line_Differences_Viewer.html");
    }
}