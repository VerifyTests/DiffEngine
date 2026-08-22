namespace DiffEngine;

/// <summary>
/// Keys for the tray's tracked moves and deletes when they ride the inline listing. Prefixed so
/// they cannot collide with an inline key — a folded path plus "|" plus a line number, where a
/// Windows path cannot put a colon at index four — and so only the tray has to know which
/// collection a key belongs to. Every other process echoes keys back opaquely.
/// <para>
/// Folded through <see cref="InlineKey.FoldPath" />, which folds only where the file system does.
/// Lower-casing unconditionally made two Linux files differing only in case - which is two files,
/// not one - share a key, and the viewer's enqueue drops the earlier of two entries with the same
/// key. A parameterised test producing value=a and value=A lost one of them.
/// </para>
/// </summary>
static class TrackedKeys
{
    public const string MovePrefix = "move:";

    public const string DeletePrefix = "delete:";

    public static string ForMove(string temp) =>
        MovePrefix + InlineKey.FoldPath(temp);

    public static string ForDelete(string file) =>
        DeletePrefix + InlineKey.FoldPath(file);

    public static bool IsTracked(string key) =>
        key.StartsWith(MovePrefix, StringComparison.Ordinal) ||
        key.StartsWith(DeletePrefix, StringComparison.Ordinal);

    public static bool TryStrip(string key, string prefix, out string path)
    {
        if (key.StartsWith(prefix, StringComparison.Ordinal))
        {
            path = key.Substring(prefix.Length);
            return true;
        }

        path = "";
        return false;
    }
}
