static partial class Implementation
{
    public static Definition MSWord()
    {
        static string LeftArguments(string temp, string target) =>
            $"\"{target}\" \"{temp}\"";

        static string RightArguments(string temp, string target) =>
            $"\"{temp}\" \"{target}\"";

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
                    "C:\\Code\\DiffEngine\\src\\DiffEngineWord\\bin\\Debug\\net10.0-windows\\diffengine-word.exe",
                    new(
                        LeftArguments,
                        RightArguments))),
            Notes: """
                   * Uses Microsoft Word's CompareSideBySideWith feature
                   * Requires Microsoft Word to be installed
                   * Requires the diffengine-word tool: `dotnet tool install -g DiffEngineWord`
                   """);
    }
}
