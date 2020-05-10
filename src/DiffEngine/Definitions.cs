using System.Collections.Generic;

static class Definitions
{
    internal static List<Definition> Tools()
    {
        return new List<Definition>
        {
            Implementation.BeyondCompare(),
            Implementation.P4Merge(),
            Implementation.AraxisMerge(),
            Implementation.Meld(),
            Implementation.SublimeMerge(),
            Implementation.Kaleidoscope(),
            Implementation.DeltaWalker(),
            Implementation.CodeCompare(),
            Implementation.WinMerge(),
            Implementation.DiffMerge(),
            Implementation.TortoiseMerge(),
            Implementation.TortoiseGitMerge(),
            Implementation.TortoiseIDiff(),
            Implementation.KDiff3(),
            Implementation.TkDiff(),
            Implementation.Guiffy(),
            Implementation.VsCode(),
            Implementation.VisualStudio(),
            Implementation.Rider()
        };
    }
}