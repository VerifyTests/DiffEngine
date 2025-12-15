static partial class Implementation
{
    public static Definition MSWord()
    {
        // Word's Compare feature is invoked via PowerShell COM automation
        // The comparison opens the target, then compares against temp, producing a tracked-changes result
        static string LeftArguments(string temp, string target) =>
            $"-NoProfile -Command \"$w = New-Object -ComObject Word.Application; $w.Visible = $true; $d = $w.Documents.Open('{target.Replace("'", "''")}'); $d.Compare('{temp.Replace("'", "''")}')\"";

        static string RightArguments(string temp, string target) =>
            $"-NoProfile -Command \"$w = New-Object -ComObject Word.Application; $w.Visible = $true; $d = $w.Documents.Open('{temp.Replace("'", "''")}'); $d.Compare('{target.Replace("'", "''")}')\"";

        return new(
            Tool: DiffTool.MSWord,
            Url: "https://support.microsoft.com/en-us/office/compare-and-merge-two-versions-of-a-document-f5059749-a797-4db7-a8fb-b3b27eb8b87e",
            AutoRefresh: false,
            IsMdi: true,
            SupportsText: false,
            RequiresTarget: true,
            Cost: "Paid (Microsoft 365 or standalone)",
            BinaryExtensions:
            [
                ".docx",
                ".docm",
                ".doc",
                ".rtf"
            ],
            OsSupport: new(
                Windows: new(
                    "powershell.exe",
                    new(
                        LeftArguments,
                        RightArguments),
                    @"%SystemRoot%\System32\WindowsPowerShell\v1.0\")),
            Notes: """
                   * Uses Microsoft Word's built-in Compare feature via COM automation
                   * Requires Microsoft Word to be installed
                   * Opens a new document showing differences with tracked changes
                   * [Compare documents](https://support.microsoft.com/en-us/office/compare-and-merge-two-versions-of-a-document-f5059749-a797-4db7-a8fb-b3b27eb8b87e)
                   """);
    }
}
