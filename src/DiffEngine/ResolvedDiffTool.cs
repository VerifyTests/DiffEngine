using System.Collections.Generic;
using DiffEngine;

class ResolvedDiffTool
{
    public DiffTool? Tool { get; }
    public string ExePath { get; }
    public BuildArguments BuildArguments { get; }
    public bool IsMdi { get; }
    public bool SupportsAutoRefresh { get; }
    public IReadOnlyList<string> BinaryExtensions { get; }
    public bool RequiresTarget { get; }

    public string BuildCommand(string tempFile, string targetFile, bool targetExists)
    {
        return $"\"{ExePath}\" {BuildArguments(tempFile, targetFile, targetExists)}";
    }

    public ResolvedDiffTool(
        DiffTool? tool,
        string exePath,
        BuildArguments buildArguments,
        bool isMdi,
        bool supportsAutoRefresh,
        string[] binaryExtensions,
        bool requiresTarget)
    {
        Tool = tool;
        ExePath = exePath;
        BuildArguments = buildArguments;
        IsMdi = isMdi;
        SupportsAutoRefresh = supportsAutoRefresh;
        BinaryExtensions = binaryExtensions;
        RequiresTarget = requiresTarget;
    }
}