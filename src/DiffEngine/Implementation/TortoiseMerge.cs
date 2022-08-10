static partial class Implementation
{
    public static Definition TortoiseMerge() =>
        new(
            name: DiffTool.TortoiseMerge,
            url: "https://tortoisesvn.net/TortoiseMerge.html",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "TortoiseMerge.exe",
                (temp, target) => $"\"{target}\" \"{temp}\"",
                (temp, target) => $"\"{temp}\" \"{target}\"",
                @"%ProgramFiles%\TortoiseSVN\bin"));
}