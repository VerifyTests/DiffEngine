static partial class Implementation
{
    public static Definition ExamDiff()
    {
        static string LeftArguments(string temp, string target)
        {
            var tempTitle = Path.GetFileName(temp);
            var targetTitle = Path.GetFileName(target);
            // Quoted, like the paths beside them and like WinMerge and VisualStudio do with
            // the same titles. A snapshot name with a space in it - which a parameterised
            // test produces routinely - otherwise splits into extra positional arguments
            return $"\"{target}\" \"{temp}\" /nh /diffonly /dn1:\"{targetTitle}\" /dn2:\"{tempTitle}\"";
        }

        static string RightArguments(string temp, string target)
        {
            var tempTitle = Path.GetFileName(temp);
            var targetTitle = Path.GetFileName(target);
            return $"\"{temp}\" \"{target}\" /nh /diffonly /dn1:\"{tempTitle}\" /dn2:\"{targetTitle}\"";
        }

        return new(
            Tool: DiffTool.ExamDiff,
            Url: "https://www.prestosoft.com/edp_examdiffpro.asp",
            AutoRefresh: true,
            IsMdi: false,
            SupportsText: true,
            UseShellExecute: true,
            RequiresTarget: true,
            Cost: "Paid",
            BinaryExtensions:
            [
                ".bin",
                ".svg"
            ],
            OsSupport: new(
                Windows: new(
                    "ExamDiff.exe",
                    new(LeftArguments, RightArguments),
                    @"%ProgramFiles%\ExamDiff Pro*\")),
            Notes: """
                 * [Command line reference](https://www.prestosoft.com/ps.asp?page=htmlhelp/edp/command_line_options)
                 * `/nh`: do not add files or directories to comparison history
                 * `/diffonly`: diff-only merge mode: hide the merge pane
                """);
    }
}