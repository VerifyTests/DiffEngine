static partial class Implementation
{
    public static Definition MsExcelDiff()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"\"{target}\" \"{temp}\"",
            Right: (temp, target) => $"\"{temp}\" \"{target}\"");

        return new(
            Tool: DiffTool.MsExcelDiff,
            Url: "https://github.com/SimonCropp/MsOfficeDiff",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: false,
            RequiresTarget: true,
            BinaryExtensions:
            [
                ".xlsx",
                ".xls"
            ],
            Cost: "Free",
            OsSupport: new(
                Windows: new(
                    "diffexcel.exe",
                    launchArguments,
                    @"%USERPROFILE%\.dotnet\tools\")),
            UseShellExecute: false,
            CreateNoWindow: true,
            KillLockingProcess: true,
            Notes: """
                 * Install via `dotnet tool install -g MsExcelDiff`
                 * Requires Spreadsheet Compare (Office Professional Plus / Microsoft 365 Apps for Enterprise)
                 * Uses Microsoft's Spreadsheet Compare to show differences between workbooks
                """);
    }
}
