using System;
using System.IO;
using DiffEngine;

static partial class Implementation
{
    public static Definition ExamDiff()
    {
        return new Definition(
            name: DiffTool.ExamDiff ,
            url: "https://www.prestosoft.com/edp_examdiffpro.asp",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            binaryExtensions: Array.Empty<string>(),
            windows: new OsSettings(
                (temp, target) =>
                {
                    var leftTitle = Path.GetFileName(temp);
                    var rightTitle = Path.GetFileName(target);
                    return $"\"{temp}\" \"{target}\" /nh /diffonly /dn1:{leftTitle} /dn2:{rightTitle}";
                },
                @"%ProgramFiles%\ExamDiff Pro\ExamDiff.exe"),
            notes: @"
 * [Command line reference](https://www.prestosoft.com/ps.asp?page=htmlhelp/edp/command_line_options)
 * `/nh`: do not add files or directories to comparison history
 * `/diffonly`: diff-only merge mode: hide the merge pane");
    }
}