static partial class Implementation
{
    public static Definition Diffinity() =>
        new(
            Tool: DiffTool.Diffinity,
            Url: "https://truehumandesign.se/s_diffinity.php",
            AutoRefresh: true,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free with option to donate",
            BinaryExtensions: [],
            OsSupport: new(
                Windows: new(
                    "Diffinity.exe",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                    @"%ProgramFiles%\Diffinity\")),
            Notes: """
                 * Disable single instance:
                   \ Preferences \ Tabs \ uncheck `Use single instance and open new diffs in tabs`.
                """);
}