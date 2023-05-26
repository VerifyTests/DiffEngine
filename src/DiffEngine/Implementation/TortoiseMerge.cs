static partial class Implementation
{
    public static Definition TortoiseMerge()
    {
        var environmentVariable = $"${DefaultEnvironmentVariablePrefix}_{nameof(DiffTool.TortoiseMerge)}";
        return new(
            Tool: DiffTool.TortoiseMerge,
            Url: "https://tortoisesvn.net/TortoiseMerge.html",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: Array.Empty<string>(),
            OsSupport: new(
                Windows: new(
                    environmentVariable,
                    "TortoiseMerge.exe",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                    @"%ProgramFiles%\TortoiseSVN\bin\")));
    }
}