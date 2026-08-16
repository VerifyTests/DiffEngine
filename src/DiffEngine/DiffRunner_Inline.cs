namespace DiffEngine;

public enum InlineResult
{
    /// <summary>
    /// Handed to the viewer, either by forwarding to a running one or by launching it.
    /// </summary>
    Queued,

    /// <summary>
    /// <see cref="DiffRunner.Disabled"/>, which also covers build servers, continuous testing and
    /// AI CLIs.
    /// </summary>
    Disabled,

    /// <summary>
    /// No DiffEngineViewer could be resolved. Callers that want a fallback should use it here.
    /// </summary>
    NoViewerFound
}

public static partial class DiffRunner
{
    /// <summary>
    /// Set <c>DiffEngine_InlineViewer</c> to false to stop inline snapshots opening a window.
    /// </summary>
    public const string InlineViewerVariable = "DiffEngine_InlineViewer";

    /// <summary>
    /// Sends a pending inline snapshot to DiffEngineViewer for review.
    /// <para>
    /// Takes the patch itself rather than a file, so nothing is written to disk: an already
    /// running viewer receives it over a loopback socket, and a newly launched one receives it on
    /// stdin.
    /// </para>
    /// <para>
    /// Async only, deliberately. A synchronous overload would have to block on the socket read,
    /// and a parallel run calls this once per failing snapshot, which would tie up a thread pool
    /// thread per call.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <see cref="InlinePatchMode.Remove"/>, which has nothing for a user to review. Apply it with
    /// <see cref="InlineApplier"/> instead.
    /// </exception>
    public static async Task<InlineResult> AddInlineAsync(InlinePatch patch, Cancel cancel = default)
    {
        if (patch.Mode == InlinePatchMode.Remove)
        {
            throw new ArgumentException($"{InlinePatchMode.Remove} patches are not reviewable. Use InlineApplier.", nameof(patch));
        }

        var check = CheckInline();
        if (check != InlineResult.Queued)
        {
            return check;
        }

        // Stamped here and nowhere else: the one place both the socket and stdin-launch paths
        // share, and always the sending process, so a re-parsed patch keeps its birth framework.
        // Onto the payload rather than onto the patch, which belongs to the caller and may be
        // held or sent again
        var payload = InlinePatchFile.Build(patch, patch.Framework ?? RuntimeMoniker.Current);
        if (await ViewerClient.TrySendAsync(new(ViewerVerb.Inline, Body: payload), cancel))
        {
            return InlineResult.Queued;
        }

        var launched = await ViewerLauncher.LaunchAsync(patch, payload, cancel);
        return launched ? InlineResult.Queued : InlineResult.NoViewerFound;
    }

    /// <summary>
    /// Drops a pending inline snapshot from the viewer's queue, for when a previously failing test
    /// starts passing. Does nothing when no viewer is running.
    /// <para>
    /// Carries this process's framework so a multi-targeted run only settles its own variant of a
    /// conflicted entry; the other framework's differing content stays pending.
    /// </para>
    /// </summary>
    public static void SettleInline(string sourceFile, int line)
    {
        if (Disabled)
        {
            return;
        }

        ViewerClient.TrySend(new(ViewerVerb.Settle, InlineKey.For(sourceFile, line), RuntimeMoniker.Current));
    }

    static InlineResult CheckInline()
    {
        if (Disabled)
        {
            return InlineResult.Disabled;
        }

        var value = Environment.GetEnvironmentVariable(InlineViewerVariable);
        if (value != null &&
            bool.TryParse(value, out var enabled) &&
            !enabled)
        {
            return InlineResult.NoViewerFound;
        }

        return InlineResult.Queued;
    }
}
