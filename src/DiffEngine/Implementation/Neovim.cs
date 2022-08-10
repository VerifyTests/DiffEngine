static partial class Implementation
{
    public static Definition Neovim()
    {
        static string LeftArguments(string temp, string target)
        {
            return $"-d \"{target}\" \"{temp}\"";
        }

        static string RightArguments(string temp, string target)
        {
            return $"-d \"{temp}\" \"{target}\"";
        }

        return new(
            name: DiffTool.Neovim,
            url: "https://neovim.io/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free with option to sponsor",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "nvim.exe",
                LeftArguments,
                RightArguments),
            linux: new(
                "nvim",
                LeftArguments,
                RightArguments),
            osx: new(
                "nvim",
                LeftArguments,
                RightArguments));
    }
}