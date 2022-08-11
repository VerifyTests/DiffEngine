record Definition(
    DiffTool Tool,
    string Url,
    bool AutoRefresh,
    bool IsMdi,
    bool SupportsText,
    bool RequiresTarget,
    string[] BinaryExtensions,
    string Cost,
    OsSettings? Windows = null,
    OsSettings? Linux = null,
    OsSettings? Osx = null,
    string? Notes = null);