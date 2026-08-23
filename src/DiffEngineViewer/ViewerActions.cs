/// <summary>
/// The only IO <see cref="ViewerSession"/> performs, injected so the state machine stays pure
/// and every accept path is testable without touching disk.
/// </summary>
record ViewerActions(
    Func<InlinePatch, InlineApplyResult> ApplyInline,
    Action<string, string> CopyFile,
    Action<string> Reveal)
{
    /// <summary>
    /// Accepting a tracked move: the received file over the target.
    /// <para>
    /// Init rather than positional, and throwing by default, because only a viewer that owns the
    /// queue ever reaches it — one displaying someone else's forwards the key instead, and file
    /// mode has no tracked entries at all. A caller that turns out to need it and did not supply
    /// one fails loudly, the same bargain <see cref="None"/> makes for the rest.
    /// </para>
    /// </summary>
    public Action<string, string> MoveFile { get; init; } = Missing;

    /// <summary>
    /// Accepting a tracked delete, and discarding a tracked move — the two cases where a pending
    /// file is the thing that goes.
    /// </summary>
    public Action<string> DeleteFile { get; init; } = Missing;

    public static readonly ViewerActions Real = new(
        InlineApplier.Apply,
        static (source, destination) => File.Copy(source, destination, true),
        RevealFile.Show)
    {
        MoveFile = Move,
        DeleteFile = File.Delete
    };

    /// <summary>
    /// Refuses everything. Held by the view only <c>Apply</c> overload, so a command that turns
    /// out to need IO fails loudly rather than quietly doing nothing.
    /// </summary>
    public static readonly ViewerActions None = new(
        static _ => throw new(missing),
        static (_, _) => throw new(missing),
        static _ => throw new(missing));

    const string missing = "This command was applied as view only, but needs real actions.";

    static void Missing(string file) =>
        throw new(missing);

    static void Missing(string temp, string target) =>
        throw new(missing);

    /// <summary>
    /// Then the directory the received file sat in, when nothing is left in it. DiffEngine stages
    /// received files in their own directory for some flows, and the tray has always swept it, so
    /// accepting here leaves behind what accepting there leaves behind.
    /// </summary>
    static void Move(string temp, string target)
    {
        File.Move(temp, target, true);
        Sweep(Path.GetDirectoryName(temp));
    }

    /// <summary>
    /// The directory the received file sat in, if it is now empty.
    /// <para>
    /// Nothing in here can fail the move. It is already done, and the caller reads a throw as the
    /// move having failed: the entry goes back on the queue, and the retry fails with file not
    /// found on a temp file that is no longer there. Only <see cref="IOException" /> was covered,
    /// so a directory whose parent will not have it removed - or whose permissions are not this
    /// process's to change - reported a move that had succeeded as a failure.
    /// </para>
    /// </summary>
    static void Sweep(string? directory)
    {
        if (directory is null)
        {
            return;
        }

        try
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                return;
            }

            Directory.Delete(directory);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // Raced by something writing into it, or not ours to remove.
        }
    }
}
