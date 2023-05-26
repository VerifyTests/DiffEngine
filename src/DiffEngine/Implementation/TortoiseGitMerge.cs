static partial class Implementation
{
    public static Definition TortoiseGitMerge()
    {
        var environmentVariable = $"${DefaultEnvironmentVariablePrefix}_{nameof(DiffTool.TortoiseGitMerge)}";
        return new(
            Tool: DiffTool.TortoiseGitMerge,
            Url: "https://tortoisegit.org/docs/tortoisegitmerge/",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: Array.Empty<string>(),
            OsSupport: new(
                Windows: new(
                    environmentVariable,
                    "TortoiseGitMerge.exe",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                    @"%ProgramFiles%\TortoiseGit\bin\")));
    }
}