using DiffEngine;

class Definition
{
    public DiffTool Tool { get; }
    public string Url { get; }
    public bool SupportsAutoRefresh { get; }
    public bool IsMdi { get; }
    public BuildArguments? WindowsArguments { get; }
    public BuildArguments? LinuxArguments { get; }
    public BuildArguments? OsxArguments { get; }
    public string[] BinaryExtensions { get; }
    public string[] WindowsPaths { get; }
    public string[] LinuxPaths { get; }
    public string[] OsxPaths { get; }
    public string? Notes { get; }
    public bool SupportsText { get; }
    public bool RequiresTarget { get; }

    public Definition(
        DiffTool name,
        string url,
        bool supportsAutoRefresh,
        bool isMdi,
        bool supportsText,
        bool requiresTarget,
        BuildArguments? windowsArguments,
        BuildArguments? linuxArguments,
        BuildArguments? osxArguments,
        string[] windowsPaths,
        string[] binaryExtensions,
        string[] linuxPaths,
        string[] osxPaths,
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
        WindowsPaths = windowsPaths;
        LinuxPaths = linuxPaths;
        OsxPaths = osxPaths;
        Notes = notes;
        SupportsText = supportsText;
        RequiresTarget = requiresTarget;
    }
}