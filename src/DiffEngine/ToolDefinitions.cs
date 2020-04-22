using System.Collections.Generic;

static class ToolDefinitions
{
    internal static List<ToolDefinition> Tools()
    {
        return new List<ToolDefinition>
        {
            Implementation.BeyondCompare(),
            Implementation.P4Merge(),
            Implementation.AraxisMerge(),
            Implementation.Meld(),
            Implementation.SublimeMerge(),
            Implementation.Kaleidoscope(),
            Implementation.CodeCompare(),
            Implementation.WinMerge(),
            Implementation.DiffMerge(),
            Implementation.TortoiseMerge(),
            Implementation.TortoiseGitMerge(),
            Implementation.TortoiseIDiff(),
            Implementation.KDiff3(),
            Implementation.TkDiff(),
            Implementation.VsCode(),
            Implementation.VisualStudio(),
            Implementation.Rider()
        };
    }
}