namespace DiffEngine;

/// <summary>
/// A request to whoever owns the inline queue.
/// </summary>
/// <param name="Verb">What the owner is being asked to do.</param>
/// <param name="Key">
/// Identifies a queue entry, for the verbs that act on one.
/// </param>
/// <param name="Body">
/// An <see cref="InlinePatchFile"/> payload, for <see cref="ViewerVerb.Inline"/>.
/// </param>
/// <param name="Member">
/// The member the call site sits in, on a <see cref="ViewerVerb.Settle"/>. A fallback for the key,
/// which names a line and so stops being true once an accept inserts a literal above it. Optional,
/// and read past by an owner that predates it, so an older one still settles by key alone.
/// </param>
/// <param name="Value">
/// What the passing call's expected argument holds, on a <see cref="ViewerVerb.Settle"/>. Narrows
/// the <paramref name="Member"/> fallback to an entry that value settles: see
/// <see cref="InlinePatch.IsSettledBy"/>. Optional, and read past by an owner that predates it.
/// </param>
record ViewerMessage(ViewerVerb Verb, string? Key = null, string? Body = null, string? Member = null, string? Value = null)
{
    /// <summary>
    /// The <see cref="Body"/> of a <see cref="ViewerVerb.Focus"/> sent for an entry that has just
    /// joined the queue: raise the window, and leave the selection on whatever is being read. An
    /// owner that predates it reads past the body and selects the entry, as every focus used to.
    /// </summary>
    public const string Arrived = "arrived";

    /// <summary>
    /// The received file of the pending move this file was derived from, on a
    /// <see cref="ViewerVerb.Move"/>, a <see cref="ViewerVerb.Diff"/> or a
    /// <see cref="ViewerVerb.Delete"/>: a page of a document, say, whose document is itself
    /// pending. The path rather than a key, since it is what the sender has, and the owner keys
    /// it the way it keys that move.
    /// <para>
    /// An init property rather than a sixth positional one, so nothing that builds a message by
    /// position has to say it does not have one. Optional, and read past by an owner that predates
    /// it, which then tracks the file as it tracks any other.
    /// </para>
    /// </summary>
    public string? Source { get; init; }

    public string Build()
    {
        var builder = new StringBuilder($"version: {ViewerPayload.Version}\n");
        builder.Append($"verb: {Verb.ToString().ToLowerInvariant()}\n");
        ViewerPayload.Append(builder, "key", Key);
        ViewerPayload.Append(builder, "body", Body);
        ViewerPayload.Append(builder, "member", Member);
        ViewerPayload.Append(builder, "value", Value);
        ViewerPayload.Append(builder, "source", Source);
        return builder.ToString();
    }

    public static bool TryParse(string text, [NotNullWhen(true)] out ViewerMessage? message)
    {
        message = null;
        if (!ViewerPayload.TryReadLines(text, out var lines) ||
            !ViewerPayload.HasVersion(lines))
        {
            return false;
        }

        ViewerVerb? verb = null;
        string? key = null;
        string? body = null;
        string? member = null;
        string? settledBy = null;
        string? source = null;
        foreach (var (name, value) in lines)
        {
            switch (name)
            {
                case "version":
                    continue;
                case "verb":
                    if (!Enum.TryParse<ViewerVerb>(value, true, out var parsed))
                    {
                        return false;
                    }

                    verb = parsed;
                    continue;
                case "key":
                    if (!ViewerPayload.TryDecode(value, out key))
                    {
                        return false;
                    }

                    continue;
                case "body":
                    if (!ViewerPayload.TryDecode(value, out body))
                    {
                        return false;
                    }

                    continue;
                case "member":
                    if (!ViewerPayload.TryDecode(value, out member))
                    {
                        return false;
                    }

                    continue;
                case "value":
                    if (!ViewerPayload.TryDecode(value, out settledBy))
                    {
                        return false;
                    }

                    continue;
                case "source":
                    if (!ViewerPayload.TryDecode(value, out source))
                    {
                        return false;
                    }

                    continue;
                default:
                    // Unknown fields are ignored so a newer client can add one without breaking
                    // an older owner, matching how PiperServer tolerates unknown payload types.
                    continue;
            }
        }

        if (verb is null)
        {
            return false;
        }

        message = new(verb.Value, key, body, member, settledBy)
        {
            // An empty one names nothing, so it is none
            Source = string.IsNullOrEmpty(source) ? null : source
        };
        return true;
    }
}
