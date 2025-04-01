static partial class Implementation
{
    public static Definition TortoiseGitMerge() =>
        new(
            Tool: DiffTool.TortoiseGitMerge,
            Url: "https://tortoisegit.org/docs/tortoisegitmerge/",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            UseShellExecute: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: [".svg"],
            OsSupport: new(
                Windows: new(
                    "TortoiseGitMerge.exe",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                    @"%ProgramFiles%\TortoiseGit\bin\")));
}