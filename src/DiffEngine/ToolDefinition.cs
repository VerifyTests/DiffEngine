using System;
using System.Runtime.InteropServices;
using DiffEngine;

class ToolDefinition
{
    public DiffTool Tool { get; }
    public string Url { get; }
    public bool SupportsAutoRefresh { get; }
    public bool IsMdi { get; }
    public BuildArguments? BuildWindowsArguments { get; }
    public BuildArguments? BuildLinuxArguments { get; }
    public BuildArguments? BuildOsxArguments { get; }

    public BuildArguments BuildArguments
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return BuildWindowsArguments!;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return BuildLinuxArguments!;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return BuildOsxArguments!;
            }

            throw new Exception($"OS not supported: {RuntimeInformation.OSDescription}");
        }
    }

    public string[] BinaryExtensions { get; }
    public string? ExePath { get; private set; }
    public bool Exists { get; private set; }
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
        BuildArguments? buildWindowsArguments,
        BuildArguments? buildLinuxArguments,
        BuildArguments? buildOsxArguments,
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
        BuildWindowsArguments = buildWindowsArguments;
        BuildLinuxArguments = buildLinuxArguments;
        BuildOsxArguments = buildOsxArguments;
        BinaryExtensions = binaryExtensions;
        WindowsExePaths = windowsExePaths;
        LinuxExePaths = linuxExePaths;
        OsxExePaths = osxExePaths;
        Notes = notes;
        SupportsText = supportsText;
        RequiresTarget = requiresTarget;

        FindExe();
    }

    public ToolDefinition(
        DiffTool name,
        string url,
        bool supportsAutoRefresh,
        bool isMdi,
        bool supportsText,
        bool requiresTarget,
        BuildArguments buildArguments,
        string[] windowsExePaths,
        string[] binaryExtensions,
        string[] linuxExePaths,
        string[] osxExePaths,
        string? notes = null) : this(name,
        url,
        supportsAutoRefresh,
        isMdi,
        supportsText,
        requiresTarget,
        buildArguments,
        buildArguments,
        buildArguments,
        windowsExePaths,
        binaryExtensions,
        linuxExePaths,
        osxExePaths,
        notes)
    {
    }

    void FindExe()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            FindExe(WindowsExePaths);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            FindExe(LinuxExePaths);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            FindExe(OsxExePaths);
            return;
        }

        throw new Exception($"OS not supported: {RuntimeInformation.OSDescription}");
    }

    void FindExe(string[] exePaths)
    {
        foreach (var exePath in exePaths)
        {
            if (WildcardFileFinder.TryFind(exePath, out var result))
            {
                ExePath = result;
                Exists = true;
                return;
            }
        }
    }
}