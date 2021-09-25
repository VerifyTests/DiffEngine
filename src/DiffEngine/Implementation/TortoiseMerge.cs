using DiffEngine;

static partial class Implementation
{
    public static Definition TortoiseMerge()
    {
        return new(
            name: DiffTool.TortoiseMerge,
            url: "https://tortoisesvn.net/TortoiseMerge.html",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                (temp, target) => $"\"{target}\" \"{temp}\"",
                (temp, target) => $"\"{temp}\" \"{target}\"",
                @"%ProgramFiles%\TortoiseSVN\bin\TortoiseMerge.exe"));
    }
}