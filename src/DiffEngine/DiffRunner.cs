#pragma warning disable CS0618 // Type or member is obsolete
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
            ([NotNullWhen(true)] out resolved) => DiffTools.TryFindByName(tool, out resolved),
            tempFile,
            targetFile,
            encoding);
    }

    public static Task<LaunchResult> LaunchAsync(DiffTool tool, string tempFile, string targetFile, Encoding? encoding = null)
    {
        GuardFiles(tempFile, targetFile);

        return InnerLaunchAsync(
            ([NotNullWhen(true)] out resolved) => DiffTools.TryFindByName(tool, out resolved),
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
            ([NotNullWhen(true)] out tool) =>
                // The same resolution LaunchAsync uses. Asking by extension alone cannot see a
                // text file convention, so a file matched by one launched asynchronously and
                // reported NoDiffToolFound synchronously
                DiffTools.TryFindForInputFilePath(tempFile, out tool),
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
            ([NotNullWhen(true)] out tool) =>
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
            ([NotNullWhen(true)] out tool) =>
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
            ([NotNullWhen(true)] out tool) =>
                DiffTools.TryFindForText(out tool),
            tempFile,
            targetFile,
            encoding);
    }

    public static LaunchResult Launch(ResolvedTool tool, string tempFile, string targetFile, Encoding? encoding = null)
    {
        GuardFiles(tempFile, targetFile);

        return InnerLaunch(
            ([NotNullWhen(true)] out resolvedTool) =>
            {
                resolvedTool = tool;
                return true;
            },
            tempFile,
            targetFile,
            encoding);
    }

    public static void AddDelete(string file)
    {
        if (Disabled)
        {
            return;
        }

        DiffEngineTray.AddDelete(file);
    }

    public static Task AddDeleteAsync(string file)
    {
        if (Disabled)
        {
            return Task.CompletedTask;
        }

        return DiffEngineTray.AddDeleteAsync(file);
    }

    public static Task<LaunchResult> LaunchAsync(ResolvedTool tool, string tempFile, string targetFile, Encoding? encoding = null)
    {
        GuardFiles(tempFile, targetFile);

        return InnerLaunchAsync(
            ([NotNullWhen(true)] out resolvedTool) =>
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

        var canKill = !tool.IsMdi;
        var replacing = false;
        if (ProcessCleanup.TryGetProcessInfo(command, out var processCommand))
        {
            if (tool.AutoRefresh)
            {
                DiffEngineTray.AddMove(tempFile, targetFile, tool.ExePath, arguments, canKill, processCommand.Process);
                return LaunchResult.AlreadyRunningAndSupportsRefresh;
            }

            replacing = KillIfNotMdi(tool, command);
        }

        // A replacement does not raise the number of open tools, so it does not spend a slot. The
        // kill above has already happened by this point, so counting it meant a re-failing test
        // closed its own window and then declined to open another
        if (!replacing &&
            MaxInstance.Reached())
        {
            DiffEngineTray.AddMove(tempFile, targetFile, tool.ExePath, arguments, canKill, null);
            return LaunchResult.TooManyRunningDiffTools;
        }

        var processId = LaunchProcess(tool, arguments);

        DiffEngineTray.AddMove(tempFile, targetFile, tool.ExePath, arguments, canKill, processId);

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
        var replacing = false;
        if (ProcessCleanup.TryGetProcessInfo(command, out var processCommand))
        {
            if (tool.AutoRefresh)
            {
                await DiffEngineTray.AddMoveAsync(tempFile, targetFile, tool.ExePath, arguments, canKill, processCommand.Process);
                return LaunchResult.AlreadyRunningAndSupportsRefresh;
            }

            replacing = KillIfNotMdi(tool, command);
        }

        // As above: a replacement is not a new instance
        if (!replacing &&
            MaxInstance.Reached())
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
            UseShellExecute = tool.UseShellExecute,
            CreateNoWindow = tool.CreateNoWindow
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

    /// <summary>
    /// Closes the tool already showing this pair, and reports whether it did. An MDI tool hosts
    /// every diff in one window, so there is nothing to close and nothing being replaced.
    /// </summary>
    static bool KillIfNotMdi(ResolvedTool tool, string command)
    {
        if (tool.IsMdi)
        {
            return false;
        }

        ProcessCleanup.Kill(command);
        return true;
    }

    static void GuardFiles(string tempFile, string targetFile)
    {
        Guard.FileExists(tempFile, nameof(tempFile));
        Guard.AgainstEmpty(targetFile, nameof(targetFile));
    }

    delegate bool TryResolveTool([NotNullWhen(true)] out ResolvedTool? resolved);
}
