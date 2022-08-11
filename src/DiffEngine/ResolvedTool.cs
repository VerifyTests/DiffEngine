namespace DiffEngine;

[DebuggerDisplay("{Name} {ExePath}, Refresh={AutoRefresh}, Mdi={IsMdi}, RequiresTarget={RequiresTarget}, SupportsText={SupportsText}")]
public class ResolvedTool
{
    public string Name { get; }
    public DiffTool? Tool { get; }
    public string ExePath { get; }
    public BuildArguments RightArguments { get; }
    public BuildArguments LeftArguments { get; }
    public bool IsMdi { get; }
    public bool AutoRefresh { get; }
    public IReadOnlyList<string> BinaryExtensions { get; }
    public bool RequiresTarget { get; }
    public bool SupportsText { get; }

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
            return LeftArguments(tempFile, targetFile);
        }

        return RightArguments(tempFile, targetFile);
    }

    internal ResolvedTool(
        string name,
        DiffTool? tool,
        string exePath,
        BuildArguments rightArguments,
        BuildArguments leftArguments,
        bool isMdi,
        bool autoRefresh,
        IReadOnlyList<string> binaryExtensions,
        bool requiresTarget,
        bool supportsText)
    {
        Name = name;
        Tool = tool;
        ExePath = exePath;
        RightArguments = rightArguments;
        LeftArguments = leftArguments;
        IsMdi = isMdi;
        AutoRefresh = autoRefresh;
        BinaryExtensions = binaryExtensions;
        RequiresTarget = requiresTarget;
        SupportsText = supportsText;
    }

    public ResolvedTool(
        string name,
        string exePath,
        BuildArguments rightArguments,
        BuildArguments leftArguments,
        bool isMdi,
        bool autoRefresh,
        string[] binaryExtensions,
        bool requiresTarget,
        bool supportsText)
    {
        Guard.FileExists(exePath, nameof(exePath));
        Guard.AgainstEmpty(name, nameof(name));
        Name = name;
        ExePath = exePath;
        RightArguments = rightArguments;
        LeftArguments = leftArguments;
        IsMdi = isMdi;
        AutoRefresh = autoRefresh;
        BinaryExtensions = binaryExtensions;
        RequiresTarget = requiresTarget;
        SupportsText = supportsText;
    }
}