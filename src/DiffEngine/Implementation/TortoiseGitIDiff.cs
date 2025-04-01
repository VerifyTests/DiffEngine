static partial class Implementation
{
    public static Definition TortoiseGitIDiff() =>
        new(
            Tool: DiffTool.TortoiseGitIDiff,
            Url: "https://tortoisegit.org/docs/tortoisegitmerge/",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: false,
            UseShellExecute: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions:
            [
                ".bmp",
                ".gif",
                ".ico",
                ".jpg",
                ".jpeg",
                ".png",
                ".tif",
                ".tiff"
            ],
            OsSupport: new(
                Windows: new(
                    "TortoiseGitIDiff.exe",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                    @"%ProgramFiles%\TortoiseGit\bin\")));
}