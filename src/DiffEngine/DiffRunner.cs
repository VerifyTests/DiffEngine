namespace DiffEngine;

/// <summary>
/// Manages diff tools processes.
/// </summary>
public static partial class DiffRunner
{
    public static bool Disabled { get; set; } = DisabledChecker.IsDisable();

    public static void MaxInstancesToLaunch(int value) =>
        MaxInstance.SetForAppDomain(value);

    public static LaunchResult Launch(DiffTool tool, string tempFile, string targetFile, Encoding? encoding = null)
    {
        GuardFiles(tempFile, targetFile);

        return InnerLaunch(
            ([NotNullWhen(true)] out ResolvedTool? resolved) => DiffTools.TryFindByName(tool, out resolved),
            tempFile,
            targetFile,
            encoding);
    }

    public static Task<LaunchResult> LaunchAsync(DiffTool tool, string tempFile, string targetFile, Encoding? encoding = null)
    {
        GuardFiles(tempFile, targetFile);

        return InnerLaunchAsync(
            ([NotNullWhen(true)] out ResolvedTool? resolved) => DiffTools.TryFindByName(tool, out resolved),
            tempFile,
            targetFile,
            encoding);
    }

    /// <summary>
    /// Launch a diff tool for the given paths.
    /// </summary>
    public static LaunchResult Launch(string tempFile, string targetFile, Encoding? encoding = null)
    {
        GuardFiles(tempFile, targetFile);

        return InnerLaunch(
            ([NotNullWhen(true)] out ResolvedTool? tool) =>
            {
                var extension = Path.GetExtension(tempFile);
                return DiffTools.TryFindByExtension(extension, out tool);
            },
            tempFile,
            targetFile,
            encoding);
    }

    /// <summary>
    /// Launch a diff tool for the given paths.
    /// </summary>
    public static Task<LaunchResult> LaunchAsync(string tempFile, string targetFile, Encoding? encoding = null)
    {
        GuardFiles(tempFile, targetFile);

        return InnerLaunchAsync(
            ([NotNullWhen(true)] out ResolvedTool? tool) =>
                DiffTools.TryFindForInputFilePath(tempFile, out tool),
            tempFile,
            targetFile,
            encoding);
    }
    /// <summary>
    /// Launch a diff tool for the given paths.
    /// </summary>
    public static Task<LaunchResult> LaunchForTextAsync(string tempFile, string targetFile, Encoding? encoding = null)
    {
        GuardFiles(tempFile, targetFile);

        return InnerLaunchAsync(
            ([NotNullWhen(true)] out ResolvedTool? tool) =>
                DiffTools.TryFindForText(out tool),
            tempFile,
            targetFile,
            encoding);
    }

    /// <summary>
    /// Launch a diff tool for the given paths.
    /// </summary>
    public static LaunchResult LaunchForText(string tempFile, string targetFile, Encoding? encoding = null)
    {
        GuardFiles(tempFile, targetFile);

        return InnerLaunch(
            ([NotNullWhen(true)] out ResolvedTool? tool) =>
                DiffTools.TryFindForText(out tool),
            tempFile,
            targetFile,
            encoding);
    }

    public static LaunchResult Launch(ResolvedTool tool, string tempFile, string targetFile, Encoding? encoding = null)
    {
        GuardFiles(tempFile, targetFile);

        return InnerLaunch(
            ([NotNullWhen(true)] out ResolvedTool? resolvedTool) =>
            {
                resolvedTool = tool;
                return true;
            },
            tempFile,
            targetFile,
            encoding);
    }

    public static Task<LaunchResult> LaunchAsync(ResolvedTool tool, string tempFile, string targetFile, Encoding? encoding = null)
    {
        GuardFiles(tempFile, targetFile);

        return InnerLaunchAsync(
            ([NotNullWhen(true)] out ResolvedTool? resolvedTool) =>
            {
                resolvedTool = tool;
                return true;
            },
            tempFile,
            targetFile,
            encoding);
    }

    static LaunchResult InnerLaunch(TryResolveTool tryResolveTool, string tempFile, string targetFile, Encoding? encoding)
    {
        if (ShouldExitLaunch(tryResolveTool, targetFile, encoding, out var tool, out var result))
        {
            DiffEngineTray.AddMove(tempFile, targetFile, null, null, false, null);
            return result.Value;
        }

        tool.CommandAndArguments(tempFile, targetFile, out var arguments, out var command);

        if (ProcessCleanup.TryGetProcessInfo(command, out var processCommand))
        {
            if (tool.AutoRefresh)
            {
                DiffEngineTray.AddMove(tempFile, targetFile, tool.ExePath, arguments, tool.IsMdi, processCommand.Process);
                return LaunchResult.AlreadyRunningAndSupportsRefresh;
            }

            KillIfNotMdi(tool, command);
        }

        if (MaxInstance.Reached())
        {
            DiffEngineTray.AddMove(tempFile, targetFile, tool.ExePath, arguments, tool.IsMdi, null);
            return LaunchResult.TooManyRunningDiffTools;
        }

        var processId = LaunchProcess(tool, arguments);

        DiffEngineTray.AddMove(tempFile, targetFile, tool.ExePath, arguments, !tool.IsMdi, processId);

        return LaunchResult.StartedNewInstance;
    }

    static async Task<LaunchResult> InnerLaunchAsync(TryResolveTool tryResolveTool, string tempFile, string targetFile, Encoding? encoding)
    {
        if (ShouldExitLaunch(tryResolveTool, targetFile, encoding, out var tool, out var result))
        {
            await DiffEngineTray.AddMoveAsync(tempFile, targetFile, null, null, false, null);
            return result.Value;
        }

        tool.CommandAndArguments(tempFile, targetFile, out var arguments, out var command);

        var canKill = !tool.IsMdi;
        if (ProcessCleanup.TryGetProcessInfo(command, out var processCommand))
        {
            if (tool.AutoRefresh)
            {
                await DiffEngineTray.AddMoveAsync(tempFile, targetFile, tool.ExePath, arguments, canKill, processCommand.Process);
                return LaunchResult.AlreadyRunningAndSupportsRefresh;
            }

            KillIfNotMdi(tool, command);
        }

        if (MaxInstance.Reached())
        {
            await DiffEngineTray.AddMoveAsync(tempFile, targetFile, tool.ExePath, arguments, canKill, null);
            return LaunchResult.TooManyRunningDiffTools;
        }

        var processId = LaunchProcess(tool, arguments);

        await DiffEngineTray.AddMoveAsync(tempFile, targetFile, tool.ExePath, arguments, canKill, processId);

        return LaunchResult.StartedNewInstance;
    }

    static bool ShouldExitLaunch(
        TryResolveTool tryResolveTool,
        string targetFile,
        Encoding? encoding,
        [NotNullWhen(false)] out ResolvedTool? tool,
        [NotNullWhen(true)] out LaunchResult? result)
    {
        if (Disabled)
        {
            result = LaunchResult.Disabled;
            tool = null;
            return true;
        }

        if (!tryResolveTool(out tool))
        {
            result = LaunchResult.NoDiffToolFound;
            return true;
        }

        if (!TryCreate(tool, targetFile, encoding))
        {
            result = LaunchResult.NoEmptyFileForExtension;
            return true;
        }

        result = null;
        return false;
    }

    static bool TryCreate(ResolvedTool tool, string targetFile, Encoding? encoding)
    {
        var targetExists = File.Exists(targetFile);
        if (tool.RequiresTarget && !targetExists)
        {
            if (!AllFiles.TryCreateFile(targetFile, useEmptyStringForTextFiles: true, encoding))
            {
                return false;
            }
        }

        return true;
    }

    static int LaunchProcess(ResolvedTool tool, string arguments)
    {
        var startInfo = new ProcessStartInfo(tool.ExePath, arguments)
        {
            // Given the full exe path is known we dont need UseShellExecute https://stackoverflow.com/a/5255335
            // however UseShellExecute allows the test running to not block when the difftool is launched
            // https://github.com/VerifyTests/Verify/issues/1229
            UseShellExecute = true
        };
        try
        {
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                return process.Id;
            }

            throw new(
                $"""
                 Failed to launch diff tool.
                 {tool.ExePath} {arguments}
                 """);
        }
        catch (Exception exception)
        {
            throw new(
                $"""
                 Failed to launch diff tool.
                 {tool.ExePath} {arguments}
                 """,
                exception);
        }
    }

    static void KillIfNotMdi(ResolvedTool tool, string command)
    {
        if (!tool.IsMdi)
        {
            ProcessCleanup.Kill(command);
        }
    }

    static void GuardFiles(string tempFile, string targetFile)
    {
        Guard.FileExists(tempFile, nameof(tempFile));
        Guard.AgainstEmpty(targetFile, nameof(targetFile));
    }

    delegate bool TryResolveTool([NotNullWhen(true)] out ResolvedTool? resolved);
}