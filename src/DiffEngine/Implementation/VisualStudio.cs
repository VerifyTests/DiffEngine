static partial class Implementation
{
    public static Definition VisualStudio()
    {
        static string LeftArguments(string temp, string target)
        {
            var tempTitle = Path.GetFileName(temp);
            var targetTitle = Path.GetFileName(target);
            return $"/diff \"{target}\" \"{temp}\" \"{targetTitle}\" \"{tempTitle}\"";
        }

        static string RightArguments(string temp, string target)
        {
            var tempTitle = Path.GetFileName(temp);
            var targetTitle = Path.GetFileName(target);
            return $"/diff \"{temp}\" \"{target}\" \"{tempTitle}\" \"{targetTitle}\"";
        }

        return new(
            Tool: DiffTool.VisualStudio,
            Url: "https://docs.microsoft.com/en-us/visualstudio/ide/reference/diff",
            AutoRefresh: true,
            IsMdi: true,
            SupportsText: true,
            UseShellExecute: true,
            RequiresTarget: true,
            Cost: "Paid and free options",
            BinaryExtensions: [".svg"],
            OsSupport: new(
                Windows: new(
                    "devenv.exe", new(
                        LeftArguments,
                        RightArguments),
                    @"%ProgramFiles%\Microsoft Visual Studio\*\Preview\Common7\IDE\",
                    @"%ProgramFiles%\Microsoft Visual Studio\*\Community\Common7\IDE\",
                    @"%ProgramFiles%\Microsoft Visual Studio\*\Professional\Common7\IDE\",
                    @"%ProgramFiles%\Microsoft Visual Studio\*\Enterprise\Common7\IDE\")));
    }
}