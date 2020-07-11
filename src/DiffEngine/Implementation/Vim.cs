using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition Vim()
    {
        string Arguments(string temp, string target)
        {
            return $"-d \"{temp}\" \"{target}\" -c \"setl autoread | setl nobackup\"";
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
            notes: @"
 * [Options](http://vimdoc.sourceforge.net/htmldoc/options.html)
 * `autoread`: When a file has been detected to have been changed outside of Vim and
	it has not been changed inside of Vim, automatically read it again");
    }
}