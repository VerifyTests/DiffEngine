static partial class Implementation
{
    public static Definition Neovim()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"-d \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"-d \"{temp}\" \"{target}\"");

        return new(
            Tool: DiffTool.Neovim,
            Url: "https://neovim.io/",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            UseShellExecute: true,
            RequiresTarget: true,
            Cost: "Free with option to sponsor",
            BinaryExtensions: [".svg"],
            OsSupport: new(
                Windows: new(
                    "nvim.exe",
                    launchArguments,
                    // %ProgramFiles%, not the literal path: expansion is what picks up
                    // ProgramW6432 and the x86 variant, and what makes this work on a
                    // machine whose Windows is not on C:
                    searchDirectory: @"%ProgramFiles%\Neovim\bin\"),
                Linux: new(
                    "nvim",
                    launchArguments),
                Osx: new(
                    "nvim",
                    launchArguments)),
            Notes: " * https://neovim.io/doc/user/diff.html");
    }
}