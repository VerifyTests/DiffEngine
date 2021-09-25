using DiffEngine;

static partial class Implementation
{
    public static Definition Diffinity()
    {
        static string TargetLeftArguments(string temp, string target) 
            => $"\"{target}\" \"{temp}\"";

        static string TargetRightArguments(string temp, string target) 
        => $"\"{temp}\" \"{target}\"";

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
                TargetLeftArguments, 
                TargetRightArguments,
                @"%ProgramFiles%\Diffinity\Diffinity.exe",
                @"%UserProfile%\scoop\apps\diffinity\current\Diffinity.exe"),
        notes: @"
 * Disable single instance:
   \ Preferences \ Tabs \ uncheck `Use single instance and open new diffs in tabs`.");
    }

}