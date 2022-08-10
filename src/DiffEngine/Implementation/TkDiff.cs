static partial class Implementation
{
    public static Definition TkDiff() =>
        new(
            name: DiffTool.TkDiff,
            url: "https://sourceforge.net/projects/tkdiff/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            osx: new(
                "tkdiff",
                (temp, target) => $"\"{target}\" \"{temp}\"",
                (temp, target) => $"\"{temp}\" \"{target}\"",
                "/Applications/TkDiff.app/Contents/MacOS"));
}