namespace DiffEngine;

public record ResolvedTool(
    string Name,
    DiffTool? Tool,
    string ExePath,
    LaunchArguments LaunchArguments,
    bool IsMdi,
    bool AutoRefresh,
    IReadOnlyCollection<string> BinaryExtensions,
    bool RequiresTarget,
    bool SupportsText)
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

    public ResolvedTool(string name, string exePath, LaunchArguments launchArguments, bool isMdi, bool autoRefresh, IReadOnlyCollection<string> binaryExtensions, bool requiresTarget, bool supportsText) :
        this(name, null, exePath, launchArguments, isMdi, autoRefresh, binaryExtensions, requiresTarget, supportsText)
    {
        Guard.FileExists(exePath, nameof(exePath));
        Guard.AgainstEmpty(name, nameof(name));
    }
}