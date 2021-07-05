using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition Diffinity()
    {
        return new(
            name: DiffTool.Diffinity,
            url: "https://truehumandesign.se/s_diffinity.php",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free with option to donate",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                (temp, target) => $"\"{temp}\" \"{target}\"",
                @"%ProgramFiles%\Diffinity\Diffinity.exe"),
        notes: @"
 * Disable single instance:
   \ Preferences \ Tabs \ uncheck `Use single instance and open new diffs in tabs`.");
    }
}