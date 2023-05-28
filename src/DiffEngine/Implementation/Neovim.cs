static partial class Implementation
{
    public static Definition Neovim()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"-d \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"-d \"{temp}\" \"{target}\"");

        var environmentVariable = $"${DefaultEnvironmentVariablePrefix}_{nameof(DiffTool.Neovim)}";
        return new(
            Tool: DiffTool.Neovim,
            Url: "https://neovim.io/",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free with option to sponsor",
            BinaryExtensions: Array.Empty<string>(),
            OsSupport: new(
                Windows: new(
                    environmentVariable,
                    "nvim.exe",
                    launchArguments),
                Linux: new(
                    environmentVariable,
                    "nvim",
                    launchArguments),
                Osx: new(
                    environmentVariable,
                    "nvim",
                    launchArguments)),
            Notes: " * https://neovim.io/doc/user/diff.html");
    }
}