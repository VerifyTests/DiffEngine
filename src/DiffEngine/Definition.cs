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
    bool UseShellExecute = true,
    bool CreateNoWindow = false,
    bool KillLockingProcess = false,
    string? Notes = null);