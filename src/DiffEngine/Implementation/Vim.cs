using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition Vim()
    {
        string Arguments(string temp, string target)
        {
            return $"-d \"{temp}\" \"{target}\" -c \"setl autoread | setl nobackup | set noswapfile\"";
        }

        return new Definition(
            name: DiffTool.Vim,
            url: "https://www.vim.org/",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            binaryExtensions: Array.Empty<string>(),
            windows: new OsSettings(Arguments, @"%ProgramFiles%\Vim\*\vim.exe"),
            osx: new OsSettings(Arguments, @"/Applications/MacVim.app/Contents/bin/mvim"),
            notes: @"
 * [Options](http://vimdoc.sourceforge.net/htmldoc/options.html)
 * [Vim help files](https://vimhelp.org/)
 * [autoread](http://vimdoc.sourceforge.net/htmldoc/options.html#'autoread')
 * [nobackup](http://vimdoc.sourceforge.net/htmldoc/options.html#'backup')
 * [noswapfile](http://vimdoc.sourceforge.net/htmldoc/options.html#'swapfile')");
    }
}