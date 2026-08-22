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
    /// Nothing has the snapshot. No DiffEngineViewer could be resolved, or the owner of the queue
    /// declined the payload - which is the same thing from the caller's side, since in both cases
    /// the snapshot is pending nowhere. Callers that want a fallback should use it here.
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
        var outcome = await ViewerClient.SendAsync(new(ViewerVerb.Inline, Body: payload), cancel);
        if (outcome == SendOutcome.Accepted)
        {
            return InlineResult.Queued;
        }

        if (outcome == SendOutcome.Refused)
        {
            // An owner that is there and said no - an older one that does not understand the
            // payload, or a handler that threw. Launching a second viewer cannot change that
            // answer, and it would bind nothing, so reporting Queued would say the snapshot is
            // somewhere when it is nowhere. Report it as no viewer, which is the answer that
            // makes the caller stage the files instead
            return InlineResult.NoViewerFound;
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
    /// <param name="sourceFile">The source file holding the call site.</param>
    /// <param name="line">The line the snapshot was recorded at, which names the queue entry.</param>
    /// <param name="memberName">
    /// The member the call site sits in. Optional, and only used where the line no longer names
    /// the entry, which is what happens once an accept inserts a literal above it.
    /// </param>
    public static void SettleInline(string sourceFile, int line, string? memberName = null)
    {
        if (Disabled)
        {
            return;
        }

        ViewerClient.TrySend(
            new(ViewerVerb.Settle, InlineKey.For(sourceFile, line), RuntimeMoniker.Current, memberName));
    }

    /// <summary>
    /// Drops a pending inline snapshot for a call site that is no longer an inline snapshot at
    /// all: the verification opted out, the global switch declined it, or its literal outgrew the
    /// size limit and moved to a file.
    /// <para>
    /// Unlike <see cref="SettleInline" /> this carries no framework, because the statement is not
    /// "this framework now passes" but "there is no inline snapshot here for any of them". A
    /// per-framework settle would strip one label and leave the entry standing on the others,
    /// pending against a call site that can never produce a snapshot again.
    /// </para>
    /// </summary>
    public static void RetireInline(string sourceFile, int line, string? memberName = null)
    {
        if (Disabled)
        {
            return;
        }

        ViewerClient.TrySend(new(ViewerVerb.Settle, InlineKey.For(sourceFile, line), null, memberName));
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
