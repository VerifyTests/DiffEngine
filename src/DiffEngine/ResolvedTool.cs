using System.Collections.Frozen;

namespace DiffEngine;

public record ResolvedTool
{
    internal void CommandAndArguments(string tempFile, string targetFile, out string arguments, out string command)
    {
        arguments = GetArguments(tempFile, targetFile);
        command = $"\"{ExePath}\" {arguments}";
    }

    public string BuildCommand(string tempFile, string targetFile) =>
        $"\"{ExePath}\" {GetArguments(tempFile, targetFile)}";

    public string GetArguments(string tempFile, string targetFile)
    {
        if (TargetPosition.TargetOnLeft)
        {
            return LaunchArguments.Left(tempFile, targetFile);
        }

        return LaunchArguments.Right(tempFile, targetFile);
    }

    public ResolvedTool(string name, string exePath, LaunchArguments launchArguments, bool isMdi, bool autoRefresh, IReadOnlyCollection<string> binaryExtensions, bool requiresTarget, bool supportsText, bool useShellExecute, bool createNoWindow = false) :
        this(name, null, exePath, launchArguments, isMdi, autoRefresh, binaryExtensions, requiresTarget, supportsText, useShellExecute, createNoWindow)
    {
    }

    public ResolvedTool(
        string name,
        DiffTool? tool,
        string exePath,
        LaunchArguments launchArguments,
        bool isMdi,
        bool autoRefresh,
        IReadOnlyCollection<string> binaryExtensions,
        bool requiresTarget,
        bool supportsText,
        bool useShellExecute,
        bool createNoWindow = false)
    {
        Guard.FileExists(exePath, nameof(exePath));
        Guard.AgainstEmpty(name, nameof(name));
        Name = name;
        Tool = tool;
        ExePath = exePath;
        LaunchArguments = launchArguments;
        IsMdi = isMdi;
        AutoRefresh = autoRefresh;
        BinaryExtensions = binaryExtensions.ToFrozenSet();
        if (binaryExtensions.Any(_ => !_.StartsWith('.')))
        {
            throw new(
                $"""
                 Extensions must begin with a period.
                 {string.Join(Environment.NewLine, binaryExtensions)}
                 """);
        }

        RequiresTarget = requiresTarget;
        SupportsText = supportsText;
        UseShellExecute = useShellExecute;
        CreateNoWindow = createNoWindow;
    }

    public string Name { get; init; }
    public DiffTool? Tool { get; init; }
    public string ExePath { get; init; }
    public LaunchArguments LaunchArguments { get; init; }
    public bool IsMdi { get; init; }
    public bool AutoRefresh { get; init; }
    public FrozenSet<string> BinaryExtensions { get; init; }
    public bool RequiresTarget { get; init; }
    public bool SupportsText { get; init; }
    public bool UseShellExecute { get; init; }
    public bool CreateNoWindow { get; init; }
}