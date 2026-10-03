namespace DiffEngine;

/// <summary>
/// Identity for a pending inline snapshot, used for replace in place, settle, accept and discard.
/// A re-run of the same test produces the same key.
/// <para>
/// Lives here because three processes address queue items by it: DiffEngine when it queues or
/// settles, the viewer when it holds the queue, and the tray when it drives one. It used to be
/// written out in each of them and held together by a test.
/// </para>
/// </summary>
public static class InlineKey
{
    /// <summary>
    /// Case folded where the file system is, because a Windows path reaches here from different
    /// sources with different casing and the same call site has to produce one entry.
    /// <para>
    /// Not where it is not. On Linux two paths differing only in case are two files, and folding
    /// them gave both one key: the second patch took over the first's entry, and settling either
    /// settled both. Every process addressing a queue is on the one machine, so they agree about
    /// which of these applies.
    /// </para>
    /// </summary>
    public static string For(string sourceFile, int line) =>
        $"{FoldPath(sourceFile)}|{line}";

    /// <summary>
    /// A path folded the way this machine's file system folds it, so anything else keyed by path
    /// agrees with <see cref="For" /> about when two paths are one file.
    /// </summary>
    internal static string FoldPath(string path) =>
        caseInsensitivePaths ? path.ToLowerInvariant() : path;

    /// <summary>
    /// Whether two paths are one file to this machine's file system, without folding either: for
    /// a caller asking it of every entry in a queue, where a folded copy of each path each time
    /// is the queue's length in strings.
    /// </summary>
    internal static bool SamePath(string left, string right) =>
        string.Equals(left, right, caseInsensitivePaths ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    static bool caseInsensitivePaths =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
}
