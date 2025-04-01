static partial class Implementation
{
    public static Definition TortoiseMerge() =>
        new(
            Tool: DiffTool.TortoiseMerge,
            Url: "https://tortoisesvn.net/TortoiseMerge.html",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            CreateNoWindow: false,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: [".svg"],
            OsSupport: new(
                Windows: new(
                    "TortoiseMerge.exe",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                    @"%ProgramFiles%\TortoiseSVN\bin\")));
}