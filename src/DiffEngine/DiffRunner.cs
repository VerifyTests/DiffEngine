#pragma warning disable CS0618 // Type or member is obsolete
namespace DiffEngine;

/// <summary>
/// Manages diff tools processes.
/// </summary>
public static partial class DiffRunner
{
    /// <summary>
    /// Whether launching a diff tool is turned off, for this process or for this async context.
    /// <para>
    /// Read rather than captured, so that the overrides feeding it - <see cref="BuildServerDetector.Detected" />
    /// and <see cref="AiCliDetector.Detected" />, both of which a test host sets after it has
    /// loaded - are honoured whenever they are set. Captured once at type initialisation, they
    /// were inert the moment anything had touched this class, and an AsyncLocal override could
    /// never have reached a value read into a static anyway.
    /// </para>
    /// <para>
    /// Setting it pins it, and nothing is read from the environment after that.
    /// </para>
    /// </summary>
    public static bool Disabled
    {
        get => disabled ?? DisabledChecker.IsDisable();
        set => disabled = value;
    }

    static bool? disabled;

    /// <summary>
    /// Forgets an explicit <see cref="Disabled" />, so it is read from the environment again. For
    /// tests, which is where anything sets it and then wants the detectors back.
    /// </summary>
    internal static void ResetDisabled() =>
        disabled = null;

    /// <summary>
    /// Whether pending moves and deletes are sent to DiffEngineTray.
    /// <para>
    /// Independent of <see cref="Disabled" />, because the two answer different questions and a
    /// test suite driving a library that stages snapshots needs them apart. Disabling diff turns
    /// off the launch, and in Verify it also turns off the inline staging that a suite testing
    /// that staging exists to produce. This turns off only the tracking, so a machine with a tray
    /// running does not collect a pending move per snapshot a test run happened to produce -
    /// pointing at a throwaway directory, and offering an accept that would write to it.
    /// </para>
    /// <para>
    /// A move with no tray falls through to the inline queue owner, and goes nowhere when nothing
    /// owns it. Pair this with a <c>DiffEngine_ViewerPort</c> nothing is listening on to detach
    /// from both, which is what <c>DiffEngine.Tests</c> does.
    /// </para>
    /// <para>
    /// Read from <c>DiffEngine_TrayDisabled</c> until set, then pinned, exactly as
    /// <see cref="Disabled" /> is.
    /// </para>
    /// </summary>
    public static bool TrayDisabled
    {
        get => trayDisabled ?? TrayDisabledChecker.IsDisabled();
        set => trayDisabled = value;
    }

    static bool? trayDisabled;

    /// <summary>
    /// Forgets an explicit <see cref="TrayDisabled" />, so it is read from the environment again.
    /// For tests, which is where anything sets it and then wants the ambient value back.
    /// </summary>
    internal static void ResetTrayDisabled() =>
        trayDisabled = null;

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

        // The viewer queues rather than opening a window per pair, so none of the process
        // bookkeeping below applies to it: there is no instance showing this pair to find, no
        // window to replace, and no slot to spend on a window that already exists.
        if (PendingFiles.IsViewer(tool))
        {
            return PendingFiles.AddDiff(tool, tempFile, targetFile);
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

        // As above: the viewer has no window of its own for this pair to reason about.
        if (PendingFiles.IsViewer(tool))
        {
            return await PendingFiles.AddDiffAsync(tool, tempFile, targetFile, Cancel.None);
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
