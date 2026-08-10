/// <summary>
/// The only IO <see cref="ViewerSession"/> performs, injected so the state machine stays pure
/// and every accept path is testable without touching disk.
/// </summary>
record ViewerActions(
    Func<InlinePatch, InlineApplyResult> ApplyInline,
    Action<string, string> CopyFile)
{
    public static readonly ViewerActions Real = new(
        InlineApplier.Apply,
        static (source, destination) => File.Copy(source, destination, true));

    /// <summary>
    /// Refuses everything. Held by the view only <c>Apply</c> overload, so a command that turns
    /// out to need IO fails loudly rather than quietly doing nothing.
    /// </summary>
    public static readonly ViewerActions None = new(
        static _ => throw new("This command was applied as view only, but needs real actions."),
        static (_, _) => throw new("This command was applied as view only, but needs real actions."));
}
