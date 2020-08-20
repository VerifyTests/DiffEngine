using System.Diagnostics;
using DiffEngine;

[DebuggerDisplay("{Tool}, Refresh={AutoRefresh}, Mdi={IsMdi}, RequiresTarget={RequiresTarget}, SupportsText={SupportsText}")]
class Definition
{
    public DiffTool Tool { get; }
    public string Url { get; }
    public bool AutoRefresh { get; }
    public bool IsMdi { get; }
    public OsSettings? Windows { get; }
    public OsSettings? Linux { get; }
    public OsSettings? Osx { get; }
    public string[] BinaryExtensions { get; }
    public string? Notes { get; }
    public bool ShellExecute { get; }
    public bool SupportsText { get; }
    public bool RequiresTarget { get; }

    public Definition(
        DiffTool name,
        string url,
        bool autoRefresh,
        bool isMdi,
        bool supportsText,
        bool requiresTarget,
        string[] binaryExtensions,
        OsSettings? windows = null,
        OsSettings? linux = null,
        OsSettings? osx = null,
        string? notes = null,
        bool shellExecute = true)
    {
        Tool = name;
        Url = url;
        AutoRefresh = autoRefresh;
        IsMdi = isMdi;
        BinaryExtensions = binaryExtensions;
        Notes = notes;
        ShellExecute = shellExecute;
        SupportsText = supportsText;
        RequiresTarget = requiresTarget;
        Windows = windows;
        Linux = linux;
        Osx = osx;
    }
}