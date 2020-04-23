using DiffEngine;

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
    public bool SupportsText { get; }
    public bool RequiresTarget { get; }

    public Definition(
        DiffTool name,
        string url,
        bool autoRefresh,
        bool isMdi,
        bool supportsText,
        bool requiresTarget,
        OsSettings? windows,
        OsSettings? linux,
        OsSettings? osx,
        string[] binaryExtensions,
        string? notes = null)
    {
        Tool = name;
        Url = url;
        AutoRefresh = autoRefresh;
        IsMdi = isMdi;
        BinaryExtensions = binaryExtensions;
        Notes = notes;
        SupportsText = supportsText;
        RequiresTarget = requiresTarget;
        Windows = windows;
        Linux = linux;
        Osx = osx;
    }
}