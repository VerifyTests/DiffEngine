static partial class Implementation
{
    public static Definition Vim()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"-d \"{target}\" \"{temp}\" -c \"setl autoread | setl nobackup | set noswapfile\"",
            Right: (temp, target) => $"-d \"{temp}\" \"{target}\" -c \"setl autoread | setl nobackup | set noswapfile\"");

        return new(
            Tool: DiffTool.Vim,
            Url: "https://www.vim.org/",
            AutoRefresh: true,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free with option to donate",
            BinaryExtensions: Array.Empty<string>(),
            OsSupport: new(
                Windows: new(
                    "vim.exe",
                    launchArguments,
                    @"%ProgramFiles%\Vim\*\"),
                Osx: new(
                    "mvim",
                    launchArguments,
                    "/Applications/MacVim.app/Contents/bin/")),
            Notes: @"
 * [Options](http://vimdoc.sourceforge.net/htmldoc/options.html)
 * [Vim help files](https://vimhelp.org/)
 * [autoread](http://vimdoc.sourceforge.net/htmldoc/options.html#'autoread')
 * [nobackup](http://vimdoc.sourceforge.net/htmldoc/options.html#'backup')
 * [noswapfile](http://vimdoc.sourceforge.net/htmldoc/options.html#'swapfile')");
    }
}