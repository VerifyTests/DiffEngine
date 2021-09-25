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
    public string Cost { get; }
    public string? Notes { get; }
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
        string cost,
        OsSettings? windows = null,
        OsSettings? linux = null,
        OsSettings? osx = null,
        string? notes = null)
    {
        Tool = name;
        Url = url;
        AutoRefresh = autoRefresh;
        IsMdi = isMdi;
        BinaryExtensions = binaryExtensions;
        Cost = cost;
        Notes = notes;
        SupportsText = supportsText;
        RequiresTarget = requiresTarget;
        Windows = windows;
        Linux = linux;
        Osx = osx;
    }
}