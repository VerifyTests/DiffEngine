static partial class Implementation
{
    public static Definition Vim()
    {
        static string LeftArguments(string temp, string target)
        {
            return $"-d \"{target}\" \"{temp}\" -c \"setl autoread | setl nobackup | set noswapfile\"";
        }

        static string RightArguments(string temp, string target)
        {
            return $"-d \"{temp}\" \"{target}\" -c \"setl autoread | setl nobackup | set noswapfile\"";
        }

        return new(
            name: DiffTool.Vim,
            url: "https://www.vim.org/",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free with option to donate",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "vim.exe",
                LeftArguments,
                RightArguments,
                @"%ProgramFiles%\Vim\*\"),
            osx: new(
                "mvim",
                LeftArguments,
                RightArguments,
                "/Applications/MacVim.app/Contents/bin/"),
            notes: @"
 * [Options](http://vimdoc.sourceforge.net/htmldoc/options.html)
 * [Vim help files](https://vimhelp.org/)
 * [autoread](http://vimdoc.sourceforge.net/htmldoc/options.html#'autoread')
 * [nobackup](http://vimdoc.sourceforge.net/htmldoc/options.html#'backup')
 * [noswapfile](http://vimdoc.sourceforge.net/htmldoc/options.html#'swapfile')");
    }
}