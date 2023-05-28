static partial class Implementation
{
    public static Definition TortoiseGitIDiff()
    {
        var environmentVariable = $"${DefaultEnvironmentVariablePrefix}_{nameof(DiffTool.TortoiseGitIDiff)}";
        return new(
            Tool: DiffTool.TortoiseGitIDiff,
            Url: "https://tortoisegit.org/docs/tortoisegitmerge/",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: false,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: new[]
            {
                "bmp",
                "gif",
                "ico",
                "jpg",
                "jpeg",
                "png",
                "tif",
                "tiff"
            },
            OsSupport: new(
                Windows: new(
                    environmentVariable,
                    "TortoiseGitIDiff.exe",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                    @"%ProgramFiles%\TortoiseGit\bin\")));
    }
}