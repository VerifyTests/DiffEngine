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

    public static readonly InlineApplyResult Applied = new(InlineApplyStatus.Applied, null, null);
    public static readonly InlineApplyResult AlreadyApplied = new(InlineApplyStatus.AlreadyApplied, null, null);

    public static InlineApplyResult NotFound(string message) =>
        new(InlineApplyStatus.NotFound, message, null);

    public static InlineApplyResult Failed(string message, Exception? exception = null) =>
        new(InlineApplyStatus.Failed, message, exception);
}
