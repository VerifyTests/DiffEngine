using DiffEngine;

class ToolDefinition
{
    public DiffTool Tool { get; }
    public string Url { get; }
    public bool SupportsAutoRefresh { get; }
    public bool IsMdi { get; }
    public BuildArguments? WindowsArguments { get; }
    public BuildArguments? LinuxArguments { get; }
    public BuildArguments? OsxArguments { get; }
    public string[] BinaryExtensions { get; }
    public string[] WindowsExePaths { get; }
    public string[] LinuxExePaths { get; }
    public string[] OsxExePaths { get; }
    public string? Notes { get; }
    public bool SupportsText { get; }
    public bool RequiresTarget { get; }

    public ToolDefinition(
        DiffTool name,
        string url,
        bool supportsAutoRefresh,
        bool isMdi,
        bool supportsText,
        bool requiresTarget,
        BuildArguments? windowsArguments,
        BuildArguments? linuxArguments,
        BuildArguments? osxArguments,
        string[] windowsExePaths,
        string[] binaryExtensions,
        string[] linuxExePaths,
        string[] osxExePaths,
        string? notes = null)
    {
        Tool = name;
        Url = url;
        SupportsAutoRefresh = supportsAutoRefresh;
        IsMdi = isMdi;
        WindowsArguments = windowsArguments;
        LinuxArguments = linuxArguments;
        OsxArguments = osxArguments;
        BinaryExtensions = binaryExtensions;
        WindowsExePaths = windowsExePaths;
        LinuxExePaths = linuxExePaths;
        OsxExePaths = osxExePaths;
        Notes = notes;
        SupportsText = supportsText;
        RequiresTarget = requiresTarget;
    }
}