namespace DiffEngine;

public record Definition(
    DiffTool Tool,
    string Url,
    bool AutoRefresh,
    bool IsMdi,
    bool SupportsText,
    bool RequiresTarget,
    string[] BinaryExtensions,
    string Cost,
    OsSupport OsSupport,
    bool CreateNoWindow = false,
    string? Notes = null);