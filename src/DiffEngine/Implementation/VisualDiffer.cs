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
                        Left: (temp, target) => $"--no-warning \"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"--no-warning \"{temp}\" \"{target}\""),
                    "/Applications/VisualDiffer.app/Contents/Helpers/")),
            Notes: " * `visdiff` runs inside the macOS sandbox, see [Sandbox and Trusted Paths](https://wiki.visualdiffer.com/unixshell.html#sandbox_and_trusted_paths) for more information");
}
