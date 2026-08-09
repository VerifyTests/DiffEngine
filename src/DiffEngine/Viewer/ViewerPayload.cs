namespace DiffEngine;

/// <summary>
/// Builds messages for a running DiffEngineViewer. Used by DiffEngine to queue snapshots, and by
/// DiffEngineTray to drive the queue.
/// <para>
/// The format is duplicated here rather than shared with the viewer, because the viewer is net10
/// only while this assembly targets down to net462 and stays AOT compatible. ViewerProtocolTests
/// parses these with the viewer's own reader so the two cannot drift.
/// </para>
/// </summary>
static class ViewerPayload
{
    public const int Version = 1;

    public static string Build(string verb, string? key = null, string? body = null)
    {
        var builder = new StringBuilder($"version: {Version}\nverb: {verb}\n");
        if (key != null)
        {
            builder.Append($"key: {Encode(key)}\n");
        }

        if (body != null)
        {
            builder.Append($"body: {Encode(body)}\n");
        }

        return builder.ToString();
    }

    public static string Inline(string patchFilePayload) =>
        Build("inline", body: patchFilePayload);

    public static string Settle(string sourceFile, int line) =>
        Build("settle", Key(sourceFile, line));

    /// <summary>
    /// Must match QueueEntry.KeyForInline in the viewer.
    /// </summary>
    public static string Key(string sourceFile, int line) =>
        $"{sourceFile.ToLowerInvariant()}|{line}";

    static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    public static bool TryDecode(string value, out string decoded)
    {
        if (value.Length == 0)
        {
            decoded = "";
            return true;
        }

        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch (FormatException)
        {
            decoded = "";
            return false;
        }
    }
}
