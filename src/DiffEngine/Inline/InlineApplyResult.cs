namespace DiffEngine;

public enum InlineApplyStatus
{
    /// <summary>
    /// The source file was modified.
    /// </summary>
    Applied,

    /// <summary>
    /// The source file already contains the new content at the call site. No write performed.
    /// </summary>
    AlreadyApplied,

    /// <summary>
    /// The call site could not be located; the source has changed since the patch was created. No write performed.
    /// </summary>
    NotFound,

    /// <summary>
    /// IO, locking, or validation failure. See <see cref="InlineApplyResult.Message"/>.
    /// </summary>
    Failed
}

public sealed class InlineApplyResult
{
    InlineApplyResult(InlineApplyStatus status, string? message, Exception? exception)
    {
        Status = status;
        Message = message;
        Exception = exception;
    }

    public InlineApplyStatus Status { get; }
    public string? Message { get; }
    public Exception? Exception { get; }

    /// <summary>
    /// The first line the edit moved, 1 based, in the source as it was before this patch, and
    /// <see cref="MovedBy"/> is how many lines it and every line after it moved down by, or up
    /// by when negative. Zero and zero for an edit that left every line where it was, and for
    /// anything that is not <see cref="InlineApplyStatus.Applied"/>.
    /// <para>
    /// A snapshot is several lines of source, so accepting one moves every call site under it in
    /// the file, and every other patch for that file was recorded against the lines as they
    /// were. Whoever holds such patches has this to bring them along (<see cref="Rebase"/>),
    /// where they otherwise stay wrong until a run reports them again.
    /// </para>
    /// </summary>
    public int MovedFrom { get; }

    /// <inheritdoc cref="MovedFrom"/>
    public int MovedBy { get; }

    InlineApplyResult(int movedFrom, int movedBy) :
        this(InlineApplyStatus.Applied, null, null)
    {
        MovedFrom = movedFrom;
        MovedBy = movedBy;
    }

    /// <summary>
    /// Where a line of the source as it was before this patch is once the patch has been applied.
    /// </summary>
    public int Rebase(int line) =>
        MovedBy != 0 && line >= MovedFrom
            ? Math.Max(line + MovedBy, 1)
            : line;

    /// <summary>
    /// Applied, by an edit that moved the lines from <paramref name="from"/> on by
    /// <paramref name="by"/>.
    /// </summary>
    internal static InlineApplyResult AppliedMoving(int from, int by) =>
        by == 0 ? Applied : new(from, by);

    public static readonly InlineApplyResult Applied = new(InlineApplyStatus.Applied, null, null);
    public static readonly InlineApplyResult AlreadyApplied = new(InlineApplyStatus.AlreadyApplied, null, null);

    public static InlineApplyResult NotFound(string message) =>
        new(InlineApplyStatus.NotFound, message, null);

    public static InlineApplyResult Failed(string message, Exception? exception = null) =>
        new(InlineApplyStatus.Failed, message, exception);
}
