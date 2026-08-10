/// <summary>
/// The queue belongs to a viewer that bound the port before this tray started, so every call is a
/// short loopback round trip and the tray is a remote control.
/// <para>
/// All of them use ViewerClient.ShortTimeout. These run from the 2 second scan timer and from the
/// menu opening, so a slow exchange must not outlast the timer period or block the UI.
/// </para>
/// <para>
/// A refused connection means the viewer has gone, which is the same as nothing pending. The queue
/// went with it, and this tray does not take ownership: it was decided at startup.
/// </para>
/// </summary>
class RemoteInlineHost : IInlineHost
{
    public IReadOnlyList<PendingSnapshot> List()
    {
        if (!Exchange(new(ViewerVerb.List), out var response) ||
            !response.Ok)
        {
            return [];
        }

        return response.Items
            .Select(_ => new PendingSnapshot(_.Key, _.Name, _.Status))
            .ToList();
    }

    /// <summary>
    /// Applied or failed only. The wire carries <c>ok</c> and a message, not an apply status, so a
    /// stale patch reads as applied here. It costs nothing: the owner is a viewer, and it is
    /// showing that message in its own footer.
    /// </summary>
    public AcceptOutcome Accept(PendingSnapshot snapshot, out string? message) =>
        Send(ViewerVerb.Accept, snapshot.Key, out message)
            ? AcceptOutcome.Applied
            : AcceptOutcome.Failed;

    public bool Discard(PendingSnapshot snapshot, out string? message) =>
        Send(ViewerVerb.Discard, snapshot.Key, out message);

    public bool AcceptAll(out string? message) =>
        Send(ViewerVerb.AcceptAll, null, out message);

    public void DiscardAll() =>
        Send(ViewerVerb.DiscardAll, null, out _);

    public void Focus(PendingSnapshot snapshot) =>
        Send(ViewerVerb.Focus, snapshot.Key, out _);

    public void Close() =>
        Send(ViewerVerb.Quit, null, out _);

    static bool Send(ViewerVerb verb, string? key, out string? message)
    {
        message = null;
        if (!Exchange(new(verb, key), out var response))
        {
            message = "The snapshot viewer is not running.";
            return false;
        }

        message = response.Message is { Length: > 0 } text ? text : null;
        return response.Ok;
    }

    static bool Exchange(ViewerMessage message, [NotNullWhen(true)] out ViewerResponse? response) =>
        ViewerClient.TrySend(message, out response, wait: ViewerClient.ShortTimeout);
}
