namespace DiffEngine;

public static class Definitions
{
    public static IReadOnlyCollection<Definition> Tools { get; }

    static Definitions() =>
        Tools =
        [
            Implementation.BeyondCompare(),
            Implementation.P4Merge(),
            Implementation.Kaleidoscope(),
            Implementation.DeltaWalker(),
            Implementation.WinMerge(),
            Implementation.TortoiseGitMerge(),
            Implementation.TortoiseGitIDiff(),
            Implementation.TortoiseMerge(),
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
            Implementation.Cursor(),
            Implementation.VisualStudio()
        ];
}