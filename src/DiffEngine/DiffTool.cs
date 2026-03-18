namespace DiffEngine;

// The order of this enum determines the default tool priority in DiffTools.Resolved.
// Keep this order in sync with Definitions.Tools.
public enum DiffTool
{
    MsWordDiff,
    MsExcelDiff,
    BeyondCompare,
    P4Merge,
    Kaleidoscope,
    DeltaWalker,
    WinMerge,
    TortoiseMerge,
    TortoiseGitMerge,
    TortoiseGitIDiff,
    TortoiseIDiff,
    KDiff3,
    TkDiff,
    Guiffy,
    ExamDiff,
    Diffinity,
    Rider,
    Vim,
    Neovim,
    AraxisMerge,
    Meld,
    SublimeMerge,
    VisualStudioCode,
    VisualStudio,
    Cursor,
}