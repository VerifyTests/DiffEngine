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
    /// Case folded, because Windows paths reach here from different sources with different casing
    /// and the same call site must produce one entry.
    /// </summary>
    public static string For(string sourceFile, int line) =>
        $"{sourceFile.ToLowerInvariant()}|{line}";
}
