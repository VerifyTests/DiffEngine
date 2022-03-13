static partial class Implementation
{
    public static Definition ExamDiff()
    {
        static string TargetLeftArguments(string temp, string target)
        {
            var tempTitle = Path.GetFileName(temp);
            var targetTitle = Path.GetFileName(target);
            return $"\"{target}\" \"{temp}\" /nh /diffonly /dn1:{targetTitle} /dn2:{tempTitle}";
        }

        static string TargetRightArguments(string temp, string target)
        {
            var tempTitle = Path.GetFileName(temp);
            var targetTitle = Path.GetFileName(target);
            return $"\"{temp}\" \"{target}\" /nh /diffonly /dn1:{tempTitle} /dn2:{targetTitle}";
        }

        return new(
            name: DiffTool.ExamDiff,
            url: "https://www.prestosoft.com/edp_examdiffpro.asp",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Paid",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                TargetLeftArguments,
                TargetRightArguments,
                @"%ProgramFiles%\ExamDiff Pro\ExamDiff.exe"),
            notes: @"
 * [Command line reference](https://www.prestosoft.com/ps.asp?page=htmlhelp/edp/command_line_options)
 * `/nh`: do not add files or directories to comparison history
 * `/diffonly`: diff-only merge mode: hide the merge pane");
    }
}