static partial class Implementation
{
    public static Definition Diffinity() =>
        new(
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
                new(
                    Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                    Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                @"%ProgramFiles%\Diffinity\"),
            notes: @"
 * Disable single instance:
   \ Preferences \ Tabs \ uncheck `Use single instance and open new diffs in tabs`.");
}