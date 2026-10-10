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

    /// <summary>
    /// Whether this is a Windows program being run from inside WSL: see <see cref="WslInterop" />.
    /// </summary>
    internal bool IsWindowsProgramInWsl =>
        WslInterop.IsWindowsProgram(ExePath);

    /// <summary>
    /// Whether the window opened for a pair can be closed by ending the process started for it.
    /// Not for an MDI tool, whose one window holds every pair, and not for a Windows tool started
    /// from WSL, where that process is not the tool.
    /// </summary>
    internal bool CanKill =>
        !IsMdi &&
        !IsWindowsProgramInWsl;

    public string GetArguments(string tempFile, string targetFile)
    {
        // A program on the host is handed both files by the paths the host knows them by
        if (IsWindowsProgramInWsl)
        {
            tempFile = WslInterop.ToWindows(tempFile);
            targetFile = WslInterop.ToWindows(targetFile);
        }

        if (TargetPosition.TargetOnLeft)
        {
            return LaunchArguments.Left(tempFile, targetFile);
        }

        return LaunchArguments.Right(tempFile, targetFile);
    }

    public ResolvedTool(string name, string exePath, LaunchArguments launchArguments, bool isMdi, bool autoRefresh, IReadOnlyCollection<string> binaryExtensions, bool requiresTarget, bool supportsText, bool useShellExecute, bool createNoWindow = false, bool killLockingProcess = false) :
        this(name, null, exePath, launchArguments, isMdi, autoRefresh, binaryExtensions, requiresTarget, supportsText, useShellExecute, createNoWindow, killLockingProcess)
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
        bool createNoWindow = false,
        bool killLockingProcess = false)
    {
        Guard.FileExists(exePath, nameof(exePath));
        Guard.AgainstEmpty(name, nameof(name));
        Name = name;
        Tool = tool;
        ExePath = exePath;
        LaunchArguments = launchArguments;
        IsMdi = isMdi;
        AutoRefresh = autoRefresh;
        foreach (var extension in binaryExtensions)
        {
            if (!extension.StartsWith('.'))
            {
                throw new(
                    $"""
                     Extensions must begin with a period.
                     {string.Join(Environment.NewLine, binaryExtensions)}
                     """);
            }
        }

        // Case insensitive for the same reason ExtensionLookup is: a binary extension is compared
        // against whatever casing the file system produced
        BinaryExtensions = binaryExtensions.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        RequiresTarget = requiresTarget;
        SupportsText = supportsText;
        UseShellExecute = useShellExecute;
        CreateNoWindow = createNoWindow;
        KillLockingProcess = killLockingProcess;
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
    public bool KillLockingProcess { get; init; }
}