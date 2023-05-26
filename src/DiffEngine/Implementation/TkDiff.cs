static partial class Implementation
{
    public static Definition TkDiff()
    {
        var environmentVariable = $"${DefaultEnvironmentVariablePrefix}_{nameof(DiffTool.TkDiff)}";
        return new(
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
                    environmentVariable,
                    "tkdiff",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                    "/Applications/TkDiff.app/Contents/MacOS/")));
    }
}