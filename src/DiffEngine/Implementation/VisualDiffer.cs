namespace DiffEngine;

static partial class Implementation
{
    public static Definition VisualDiffer() =>
        new(
            Tool: DiffTool.VisualDiffer,
            Url: "https://visualdiffer.com",
            AutoRefresh: true,
            IsMdi: true,
            SupportsText: true,
            UseShellExecute: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions:
            [
                ".doc",
                ".docx",
                ".pdf"
            ],
            OsSupport: new(
                Osx: new(
                    "visdiff",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""),
                    "/Applications/VisualDiffer.app/Contents/Helpers/")));
}
