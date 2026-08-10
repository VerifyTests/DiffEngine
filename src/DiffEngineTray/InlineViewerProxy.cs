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
        if (!ViewerClient.TrySend(new(ViewerVerb.List), out var response, wait: ViewerClient.ShortTimeout) ||
            !response.Ok)
        {
            return [];
        }

        return response.Items
            .Select(_ => new PendingSnapshot(_.Key, _.Name, _.Status))
            .ToList();
    }

    public static bool Accept(PendingSnapshot snapshot, out string? message) =>
        Send(ViewerVerb.Accept, snapshot.Key, out message);

    public static bool Discard(PendingSnapshot snapshot, out string? message) =>
        Send(ViewerVerb.Discard, snapshot.Key, out message);

    public static bool AcceptAll(out string? message) =>
        Send(ViewerVerb.AcceptAll, null, out message);

    public static void Focus(PendingSnapshot snapshot) =>
        Send(ViewerVerb.Focus, snapshot.Key, out _);

    public static void Quit() =>
        Send(ViewerVerb.Quit, null, out _);

    static bool Send(ViewerVerb verb, string? key, out string? message)
    {
        message = null;
        if (!ViewerClient.TrySend(new(verb, key), out var response, wait: ViewerClient.ShortTimeout))
        {
            message = "The snapshot viewer is not running.";
            return false;
        }

        message = response.Message is { Length: > 0 } text ? text : null;
        return response.Ok;
    }
}
