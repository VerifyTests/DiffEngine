﻿static partial class Implementation
{
    public static Definition Diffinity()
    {
        static string LeftArguments(string temp, string target)
        {
            return $"\"{target}\" \"{temp}\"";
        }

        static string RightArguments(string temp, string target)
        {
            return $"\"{temp}\" \"{target}\"";
        }

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
                "Diffinity.exe",
                LeftArguments,
                RightArguments,
                @"%ProgramFiles%\Diffinity\",
                @"%UserProfile%\scoop\apps\diffinity\current\"),
            notes: @"
 * Disable single instance:
   \ Preferences \ Tabs \ uncheck `Use single instance and open new diffs in tabs`.");
    }
}