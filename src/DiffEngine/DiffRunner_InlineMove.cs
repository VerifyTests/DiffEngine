namespace DiffEngine;

public enum InlineMoveResult
{
    Sent,
    Disabled,
    TrayNotRunning,

    /// <summary>
    /// The running tray predates inline snapshot support.
    /// Update with: dotnet tool update -g DiffEngineTray
    /// </summary>
    TrayTooOld
}

public static partial class DiffRunner
{
    static readonly Version minInlineTrayVersion = new(20, 0, 0);

    /// <summary>
    /// Notifies the tray of a pending inline snapshot edit.
    /// </summary>
    /// <param name="tempFile">The staged received text file. Used as the tracking key and the diff left side.</param>
    /// <param name="targetFile">The .cs source file the snapshot will be spliced into.</param>
    /// <param name="patchFile">The staged patch file (see <see cref="InlinePatchFile"/>).</param>
    /// <param name="stagedVerifiedFile">Optional staged expected text file, used as the diff right side.</param>
    public static InlineMoveResult AddInlineMove(
        string tempFile,
        string targetFile,
        string patchFile,
        string? stagedVerifiedFile = null)
    {
        var check = CheckInlineMove();
        if (check != InlineMoveResult.Sent)
        {
            return check;
        }

        PiperClient.SendInlineMove(tempFile, targetFile, patchFile, stagedVerifiedFile);
        return InlineMoveResult.Sent;
    }

    /// <inheritdoc cref="AddInlineMove"/>
    public static async Task<InlineMoveResult> AddInlineMoveAsync(
        string tempFile,
        string targetFile,
        string patchFile,
        string? stagedVerifiedFile = null,
        Cancel cancel = default)
    {
        var check = CheckInlineMove();
        if (check != InlineMoveResult.Sent)
        {
            return check;
        }

        await PiperClient.SendInlineMoveAsync(tempFile, targetFile, patchFile, stagedVerifiedFile, cancel);
        return InlineMoveResult.Sent;
    }

    static InlineMoveResult CheckInlineMove()
    {
        if (Disabled)
        {
            return InlineMoveResult.Disabled;
        }

        if (!TrayDetector.IsRunning())
        {
            return InlineMoveResult.TrayNotRunning;
        }

        if (!TrayVersionFile.TryRead(out var version) ||
            version < minInlineTrayVersion)
        {
            return InlineMoveResult.TrayTooOld;
        }

        return InlineMoveResult.Sent;
    }
}
