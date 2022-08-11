﻿static partial class Implementation
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
            name: DiffTool.VisualStudio,
            url: "https://docs.microsoft.com/en-us/visualstudio/ide/reference/diff",
            autoRefresh: true,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            cost: "Paid and free options",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "devenv.exe",
                LeftArguments,
                RightArguments,
                @"%ProgramFiles%\Microsoft Visual Studio\2022\Preview\Common7\IDE\",
                @"%ProgramFiles%\Microsoft Visual Studio\2022\Community\Common7\IDE\",
                @"%ProgramFiles%\Microsoft Visual Studio\2022\Professional\Common7\IDE\",
                @"%ProgramFiles%\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\"));
    }
}