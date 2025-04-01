static partial class Implementation
{
    public static Definition TkDiff() =>
        new(
            Tool: DiffTool.TkDiff,
            Url: "https://sourceforge.net/projects/tkdiff/",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            CreateNoWindow: false,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: [".svg"],
            OsSupport: new(
                Osx: new(
                    "tkdiff",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                    "/Applications/TkDiff.app/Contents/MacOS/")));
}