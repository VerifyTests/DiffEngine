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
}
