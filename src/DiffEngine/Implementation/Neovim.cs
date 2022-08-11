static partial class Implementation
{
    public static Definition Neovim()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"-d \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"-d \"{temp}\" \"{target}\"");

        return new(
            name: DiffTool.Neovim,
            url: "https://neovim.io/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free with option to sponsor",
            binaryExtensions: Array.Empty<string>(),
            windows: new("nvim.exe", launchArguments),
            linux: new("nvim", launchArguments),
            osx: new("nvim", launchArguments));
    }
}