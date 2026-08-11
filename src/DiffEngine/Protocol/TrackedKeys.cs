namespace DiffEngine;

/// <summary>
/// Keys for the tray's tracked moves and deletes when they ride the inline listing. Prefixed so
/// they cannot collide with an inline key — a lower-cased path plus "|" plus a line number, where
/// a Windows path cannot put a colon at index four — and so only the tray has to know which
/// collection a key belongs to. Every other process echoes keys back opaquely.
/// </summary>
static class TrackedKeys
{
    public const string MovePrefix = "move:";

    public const string DeletePrefix = "delete:";

    public static string ForMove(string temp) =>
        MovePrefix + temp.ToLowerInvariant();

    public static string ForDelete(string file) =>
        DeletePrefix + file.ToLowerInvariant();

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
