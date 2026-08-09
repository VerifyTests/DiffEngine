/// <summary>
/// The tray's half of the viewer protocol. Every call is a short loopback round trip, and a
/// refused connection simply means no viewer is running, which is the same as nothing pending.
/// <para>
/// All of them use ViewerClient.ShortTimeout. These run from the 2 second scan timer and from the
/// menu opening, so a slow exchange must not outlast the timer period or block the UI.
/// </para>
/// </summary>
static class InlineViewerProxy
{
    public static IReadOnlyList<PendingSnapshot> List()
    {
        if (!ViewerClient.TryExchange(ViewerPayload.Build("list"), ViewerClient.ShortTimeout, out var response))
        {
            return [];
        }

        var items = new List<PendingSnapshot>();
        foreach (var line in response.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith("item: ", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryReadItem(trimmed["item: ".Length..], out var item))
            {
                items.Add(item);
            }
        }

        return items;
    }

    static bool TryReadItem(string value, [NotNullWhen(true)] out PendingSnapshot? item)
    {
        item = null;
        var parts = value.Split('|');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!ViewerPayload.TryDecode(parts[0], out var key) ||
            !ViewerPayload.TryDecode(parts[1], out var name) ||
            !ViewerPayload.TryDecode(parts[2], out var status))
        {
            return false;
        }

        item = new(key, name, status.Length == 0 ? null : status);
        return true;
    }

    public static bool Accept(PendingSnapshot snapshot, out string? message) =>
        Send("accept", snapshot.Key, out message);

    public static bool Discard(PendingSnapshot snapshot, out string? message) =>
        Send("discard", snapshot.Key, out message);

    public static bool AcceptAll(out string? message) =>
        Send("acceptall", null, out message);

    public static void Focus(PendingSnapshot snapshot) =>
        Send("focus", snapshot.Key, out _);

    public static void Quit() =>
        Send("quit", null, out _);

    static bool Send(string verb, string? key, out string? message)
    {
        message = null;
        if (!ViewerClient.TryExchange(ViewerPayload.Build(verb, key), ViewerClient.ShortTimeout, out var response))
        {
            message = "The snapshot viewer is not running.";
            return false;
        }

        message = ReadMessage(response);
        return response.Contains("status: ok");
    }

    static string? ReadMessage(string response)
    {
        foreach (var line in response.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith("message: ", StringComparison.Ordinal))
            {
                continue;
            }

            if (ViewerPayload.TryDecode(trimmed["message: ".Length..], out var message) &&
                message.Length > 0)
            {
                return message;
            }
        }

        return null;
    }
}
