namespace DiffEngine;

public static class Definitions
{
    public static IReadOnlyCollection<Definition> Tools { get; }

    static Definitions() =>
        // Order determines default tool priority. Keep in sync with the DiffTool enum.
        Tools =
        [
            Implementation.MsWordDiff(),
            Implementation.MsExcelDiff(),
            Implementation.BeyondCompare(),
            Implementation.P4Merge(),
            Implementation.Kaleidoscope(),
            Implementation.DeltaWalker(),
            Implementation.WinMerge(),
            Implementation.TortoiseMerge(),
            Implementation.TortoiseGitMerge(),
            Implementation.TortoiseGitIDiff(),
            Implementation.TortoiseIDiff(),
            Implementation.KDiff3(),
            Implementation.TkDiff(),
            Implementation.Guiffy(),
            Implementation.ExamDiff(),
            Implementation.Diffinity(),
            Implementation.Rider(),
            Implementation.Vim(),
            Implementation.Neovim(),
            Implementation.AraxisMerge(),
            Implementation.Meld(),
            Implementation.SublimeMerge(),
            Implementation.VisualStudioCode(),
            Implementation.VisualStudio(),
            Implementation.Cursor(),
            Implementation.DiffEngineViewer(),
        ];
}
