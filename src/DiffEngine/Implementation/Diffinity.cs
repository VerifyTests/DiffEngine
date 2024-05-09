static partial class Implementation
{
    public static Definition Diffinity() =>
        new(
            Tool: DiffTool.Diffinity,
            Url: "https://truehumandesign.se/s_diffinity.php",
            AutoRefresh: true,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free with option to donate",
            BinaryExtensions: [".svg"],
            OsSupport: new(
                Windows: new(
                    "Diffinity.exe",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\" -forceNewInstance",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\" -forceNewInstance"),
                    @"%ProgramFiles%\Diffinity\"))
           );
}