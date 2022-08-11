static partial class Implementation
{
    public static Definition TkDiff() =>
        new(
            Tool: DiffTool.TkDiff,
            Url: "https://sourceforge.net/projects/tkdiff/",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: Array.Empty<string>(),
            OsSupport: new(
                Osx: new(
                    "tkdiff",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                    "/Applications/TkDiff.app/Contents/MacOS/")));
}