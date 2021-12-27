using DiffEngine;

static partial class Implementation
{
    public static Definition Vim()
    {
        static string TargetLeftArguments(string temp, string target) =>
            $"-d \"{target}\" \"{temp}\" -c \"setl autoread | setl nobackup | set noswapfile\"";
        static string TargetRightArguments(string temp, string target) =>
            $"-d \"{temp}\" \"{target}\" -c \"setl autoread | setl nobackup | set noswapfile\"";

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
                TargetLeftArguments,
                TargetRightArguments,
                @"%ProgramFiles%\Vim\*\vim.exe"),
            osx: new(
                TargetLeftArguments,
                TargetRightArguments,
                "/Applications/MacVim.app/Contents/bin/mvim"),
            notes: @"
 * [Options](http://vimdoc.sourceforge.net/htmldoc/options.html)
 * [Vim help files](https://vimhelp.org/)
 * [autoread](http://vimdoc.sourceforge.net/htmldoc/options.html#'autoread')
 * [nobackup](http://vimdoc.sourceforge.net/htmldoc/options.html#'backup')
 * [noswapfile](http://vimdoc.sourceforge.net/htmldoc/options.html#'swapfile')");
    }
}