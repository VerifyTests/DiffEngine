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

    /// <summary>
    /// Launch a diff tool for a file that was derived from another: a page of a document, the text
    /// read out of one, anything a snapshot library computed from a source it is also verifying.
    /// <para>
    /// <paramref name="sourceTempFile" /> is the received file of that source, exactly as it was
    /// given as <c>tempFile</c> to the launch for it. Pass it only while the source is itself
    /// pending, and launch the source first.
    /// </para>
    /// <para>
    /// When the source went to DiffEngineViewer, and the viewer is drawing it as a document, no
    /// tool is opened for this file: it is tracked, and the viewer shows it beneath the document
    /// and accepts the two together. That is a pair handed to something already on screen, so it
    /// is reported as <see cref="LaunchResult.AlreadyRunningAndSupportsRefresh" />, and costs
    /// nothing against <see cref="MaxInstancesToLaunch" />. In every other case - the source is
    /// in Word or Beyond Compare, or in no tool at all - this is
    /// <see cref="Launch(string, string, Encoding?)" />, with the source said to whoever tracks
    /// the pair.
    /// </para>
    /// <para>
    /// A name of its own rather than an overload: three strings in a row beside the overloads
    /// that take a tool read as one of those.
    /// </para>
    /// </summary>
    public static LaunchResult LaunchDerived(string tempFile, string targetFile, string sourceTempFile, Encoding? encoding)
    {
        GuardFiles(tempFile, targetFile);
        Guard.AgainstEmpty(sourceTempFile, nameof(sourceTempFile));

        return InnerLaunch(
            ([NotNullWhen(true)] out tool) =>
                DiffTools.TryFindForInputFilePath(tempFile, out tool),
            tempFile,
            targetFile,
            encoding,
            SourceOf(tempFile, sourceTempFile));
    }

    /// <inheritdoc cref="LaunchDerived(string, string, string, Encoding?)" />
    public static Task<LaunchResult> LaunchDerivedAsync(string tempFile, string targetFile, string sourceTempFile, Encoding? encoding)
    {
        GuardFiles(tempFile, targetFile);
        Guard.AgainstEmpty(sourceTempFile, nameof(sourceTempFile));

        return InnerLaunchAsync(
            ([NotNullWhen(true)] out tool) =>
                DiffTools.TryFindForInputFilePath(tempFile, out tool),
            tempFile,
            targetFile,
            encoding,
            SourceOf(tempFile, sourceTempFile));
    }

    /// <summary>
    /// <see cref="LaunchDerived(string, string, string, Encoding?)" /> for a file the caller knows
    /// to be text whatever its extension says, as <see cref="LaunchForText" /> is to
    /// <see cref="Launch(string, string, Encoding?)" />.
    /// </summary>
    public static LaunchResult LaunchDerivedForText(string tempFile, string targetFile, string sourceTempFile, Encoding? encoding)
    {
        GuardFiles(tempFile, targetFile);
        Guard.AgainstEmpty(sourceTempFile, nameof(sourceTempFile));

        return InnerLaunch(
            ([NotNullWhen(true)] out tool) =>
                DiffTools.TryFindForText(out tool),
            tempFile,
            targetFile,
            encoding,
            SourceOf(tempFile, sourceTempFile));
    }

    /// <inheritdoc cref="LaunchDerivedForText" />
    public static Task<LaunchResult> LaunchDerivedForTextAsync(string tempFile, string targetFile, string sourceTempFile, Encoding? encoding)
    {
        GuardFiles(tempFile, targetFile);
        Guard.AgainstEmpty(sourceTempFile, nameof(sourceTempFile));

        return InnerLaunchAsync(
            ([NotNullWhen(true)] out tool) =>
                DiffTools.TryFindForText(out tool),
            tempFile,
            targetFile,
            encoding,
            SourceOf(tempFile, sourceTempFile));
    }

    /// <summary>
    /// A file is not derived from itself. Said here rather than left to whoever tracks it, where
    /// an entry naming itself as its source would be hidden beneath an entry that is not there.
    /// </summary>
    static string? SourceOf(string file, string source)
    {
        if (InlineKey.SamePath(file, source))
        {
            return null;
        }

        return source;
    }

    public static void AddDelete(string file)
    {
        if (Disabled)
        {
            return;
        }

        PendingFiles.AddDelete(file);
    }

    public static Task AddDeleteAsync(string file)
    {
        if (Disabled)
        {
            return Task.CompletedTask;
        }

        return PendingFiles.AddDeleteAsync(file, Cancel.None);
    }

    /// <summary>
    /// <see cref="AddDelete" /> for a file that was derived from another, which that other no
    /// longer produces: a page a document has lost. <paramref name="sourceTempFile" /> is as on
    /// <see cref="LaunchDerived(string, string, string, Encoding?)" />, and the delete is raised
    /// after the launch for the source, never before it.
    /// <para>
    /// A viewer drawing the source shows the delete beneath it and carries it out when the source
    /// is accepted. Anything else holds it as the ordinary delete it also is.
    /// </para>
    /// </summary>
    public static void AddDerivedDelete(string file, string sourceTempFile)
    {
        Guard.AgainstEmpty(sourceTempFile, nameof(sourceTempFile));
        if (Disabled)
        {
            return;
        }

        PendingFiles.AddDelete(file, SourceOf(file, sourceTempFile));
    }

    /// <inheritdoc cref="AddDerivedDelete" />
    public static Task AddDerivedDeleteAsync(string file, string sourceTempFile)
    {
        Guard.AgainstEmpty(sourceTempFile, nameof(sourceTempFile));
        if (Disabled)
        {
            return Task.CompletedTask;
        }

        return PendingFiles.AddDeleteAsync(file, Cancel.None, SourceOf(file, sourceTempFile));
    }

    /// <summary>
    /// Withdraws a pending delete, for when the file it was raised for is in use again: a later
    /// run verified against it, so accepting the delete would remove a file that run depends on.
    /// Nothing is deleted.
    /// <para>
    /// Does nothing when no tray or viewer holds the queue - and cheaply, the way
    /// <see cref="SettleInline(string, int, string?, string?)" /> does, since a port found with nothing listening is not
    /// connected to again for a while.
    /// </para>
    /// </summary>
    public static void SettleDelete(string file)
    {
        if (Disabled)
        {
            return;
        }

        PendingFiles.SettleDelete(file);
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

    /// <param name="tryResolveTool">The tool for the pair.</param>
    /// <param name="tempFile">The received file.</param>
    /// <param name="targetFile">The file it belongs at.</param>
    /// <param name="encoding">For an empty target a tool needs written first.</param>
    /// <param name="source">
    /// The received file of the pending move this pair was derived from, or null for a pair that
    /// stands alone, which is every pair any caller but the derived launches sends.
    /// </param>
    /// <param name="tryResolveSource">
    /// The tool for <paramref name="source" />. Null resolves it from its path, the way the launch
    /// for the source itself did. Handed in by the tests, which have a stand-in for a viewer.
    /// </param>
    internal static LaunchResult InnerLaunch(
        TryResolveTool tryResolveTool,
        string tempFile,
        string targetFile,
        Encoding? encoding,
        string? source = null,
        TryResolveTool? tryResolveSource = null)
    {
        // Before the pair's own tool is resolved. A file shown beneath its source needs none, and
        // resolving one writes an empty target for a tool that requires it, which the viewer
        // would then draw the page against.
        if (source is not null &&
            DrawnWithSource(source, tryResolveSource, out var viewer) &&
            PendingFiles.AddDerived(viewer, tempFile, targetFile, source))
        {
            return LaunchResult.AlreadyRunningAndSupportsRefresh;
        }

        if (ShouldExitLaunch(tryResolveTool, targetFile, encoding, out var tool, out var result))
        {
            PendingFiles.AddMove(tempFile, targetFile, null, null, false, null, source);
            return result.Value;
        }

        // The viewer queues rather than opening a window per pair, so none of the process
        // bookkeeping below applies to it: there is no instance showing this pair to find, and no
        // window to replace. The cap still does, but only on a viewer that has to be started -
        // handing a pair to one already on screen opens nothing. ViewerLaunchGate is the only
        // place that knows which of the two is happening, so it charges MaxInstance rather than
        // this method.
        if (PendingFiles.IsViewer(tool))
        {
            return PendingFiles.AddDiff(tool, tempFile, targetFile, source);
        }

        tool.CommandAndArguments(tempFile, targetFile, out var arguments, out var command);

        var canKill = tool.CanKill;
        var replacing = false;
        if (ProcessCleanup.TryGetProcessInfo(command, out var processCommand))
        {
            if (tool.AutoRefresh)
            {
                PendingFiles.AddMove(tempFile, targetFile, tool.ExePath, arguments, canKill, processCommand.Process, source);
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
            PendingFiles.AddMove(tempFile, targetFile, tool.ExePath, arguments, canKill, null, source);
            return LaunchResult.TooManyRunningDiffTools;
        }

        var processId = LaunchProcess(tool, arguments);
        ProcessCleanup.Track(command, processId);

        PendingFiles.AddMove(tempFile, targetFile, tool.ExePath, arguments, canKill, processId, source);

        return LaunchResult.StartedNewInstance;
    }

    /// <inheritdoc cref="InnerLaunch" />
    internal static async Task<LaunchResult> InnerLaunchAsync(
        TryResolveTool tryResolveTool,
        string tempFile,
        string targetFile,
        Encoding? encoding,
        string? source = null,
        TryResolveTool? tryResolveSource = null)
    {
        // As above: a file shown beneath its source has no tool to resolve
        if (source is not null &&
            DrawnWithSource(source, tryResolveSource, out var viewer) &&
            await PendingFiles.AddDerivedAsync(viewer, tempFile, targetFile, source, Cancel.None))
        {
            return LaunchResult.AlreadyRunningAndSupportsRefresh;
        }

        if (ShouldExitLaunch(tryResolveTool, targetFile, encoding, out var tool, out var result))
        {
            await PendingFiles.AddMoveAsync(tempFile, targetFile, null, null, false, null, Cancel.None, source);
            return result.Value;
        }

        // As above: the viewer has no window of its own for this pair to reason about.
        if (PendingFiles.IsViewer(tool))
        {
            return await PendingFiles.AddDiffAsync(tool, tempFile, targetFile, Cancel.None, source);
        }

        tool.CommandAndArguments(tempFile, targetFile, out var arguments, out var command);

        var canKill = tool.CanKill;
        var replacing = false;
        if (ProcessCleanup.TryGetProcessInfo(command, out var processCommand))
        {
            if (tool.AutoRefresh)
            {
                await PendingFiles.AddMoveAsync(tempFile, targetFile, tool.ExePath, arguments, canKill, processCommand.Process, Cancel.None, source);
                return LaunchResult.AlreadyRunningAndSupportsRefresh;
            }

            replacing = KillIfNotMdi(tool, command);
        }

        // As above: a replacement is not a new instance
        if (!replacing &&
            MaxInstance.Reached())
        {
            await PendingFiles.AddMoveAsync(tempFile, targetFile, tool.ExePath, arguments, canKill, null, Cancel.None, source);
            return LaunchResult.TooManyRunningDiffTools;
        }

        var processId = LaunchProcess(tool, arguments);
        ProcessCleanup.Track(command, processId);

        await PendingFiles.AddMoveAsync(tempFile, targetFile, tool.ExePath, arguments, canKill, processId, Cancel.None, source);

        return LaunchResult.StartedNewInstance;
    }

    /// <summary>
    /// Whether the tool the source went to is a viewer drawing it as a document, and so already
    /// showing what was derived from it: see <see cref="PendingFiles.Draws" />.
    /// <para>
    /// Resolved from the source's path, as its own launch resolved it, so the two agree about
    /// where the source is. Not while launching is turned off: nothing was opened for the source
    /// then, and the pair is tracked as every pair is.
    /// </para>
    /// </summary>
    static bool DrawnWithSource(string source, TryResolveTool? tryResolveSource, [NotNullWhen(true)] out ResolvedTool? viewer)
    {
        viewer = null;
        if (Disabled ||
            !TryResolveSource(source, tryResolveSource, out var tool) ||
            !PendingFiles.Draws(tool, source))
        {
            return false;
        }

        viewer = tool;
        return true;
    }

    static bool TryResolveSource(string source, TryResolveTool? tryResolveSource, [NotNullWhen(true)] out ResolvedTool? tool)
    {
        if (tryResolveSource is null)
        {
            return DiffTools.TryFindForInputFilePath(source, out tool);
        }

        return tryResolveSource(out tool);
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

    internal static int LaunchProcess(ResolvedTool tool, string arguments)
    {
        try
        {
            // A tool declared without ShellExecute held the test run open for as long as the tool
            // was, which is the problem the comment further down records being solved for the
            // tools declared with it. On Windows it is started so that it cannot: see
            // WindowsProcess.StartInheritingNothing.
            //
            // Elsewhere it is started as declared, as every tool is. A child there takes the
            // host's standard streams however it is started, and nothing in a definition says
            // which tools could do without them: Neovim runs in the terminal and needs all three,
            // and is declared the same as the tools that open a window
            if (!tool.UseShellExecute &&
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return WindowsProcess.StartInheritingNothing(tool.ExePath, arguments);
            }

            // A Windows tool from inside WSL, which is started so that it holds nothing of the
            // host's either: its window outlives the run by as long as it is left open
            if (tool.IsWindowsProgramInWsl)
            {
                return WslInterop.Start(tool.ExePath, arguments);
            }

            var startInfo = new ProcessStartInfo(tool.ExePath, arguments)
            {
                // Given the full exe path is known we dont need UseShellExecute https://stackoverflow.com/a/5255335
                // however UseShellExecute allows the test running to not block when the difftool is launched
                // https://github.com/VerifyTests/Verify/issues/1229
                UseShellExecute = tool.UseShellExecute,
                CreateNoWindow = tool.CreateNoWindow
            };
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
    /// every diff in one window, so there is nothing to close and nothing being replaced. A
    /// Windows tool started from WSL cannot be closed from here, so its window is left and the
    /// pair gets another beside it.
    /// </summary>
    static bool KillIfNotMdi(ResolvedTool tool, string command)
    {
        if (!tool.CanKill)
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

    internal delegate bool TryResolveTool([NotNullWhen(true)] out ResolvedTool? resolved);
}
