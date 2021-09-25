static class Definitions
{
    public static IReadOnlyList<Definition> Tools { get; }

    static Definitions()
    {
        Tools = new List<Definition>
        {
            Implementation.BeyondCompare(),
            Implementation.P4Merge(),
            Implementation.Kaleidoscope(),
            Implementation.DeltaWalker(),
            Implementation.WinMerge(),
            Implementation.DiffMerge(),
            Implementation.TortoiseMerge(),
            Implementation.TortoiseGitMerge(),
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
            Implementation.CodeCompare(),
            Implementation.VsCode(),
            Implementation.VisualStudio(),
        };
    }
}