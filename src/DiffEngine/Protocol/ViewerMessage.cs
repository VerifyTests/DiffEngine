namespace DiffEngine;

/// <summary>
/// A request to whoever owns the inline queue. <paramref name="Key"/> identifies a queue entry for
/// the verbs that act on one; <paramref name="Body"/> carries an <see cref="InlinePatchFile"/>
/// payload for <see cref="ViewerVerb.Inline"/>.
/// </summary>
/// <param name="Member">
/// The member the call site sits in, on a <see cref="ViewerVerb.Settle"/>. A fallback for the key,
/// which names a line and so stops being true once an accept inserts a literal above it. Optional,
/// and read past by an owner that predates it, so an older one still settles by key alone.
/// </param>
record ViewerMessage(ViewerVerb Verb, string? Key = null, string? Body = null, string? Member = null)
{
    public string Build()
    {
        var builder = new StringBuilder($"version: {ViewerPayload.Version}\n");
        builder.Append($"verb: {Verb.ToString().ToLowerInvariant()}\n");
        ViewerPayload.Append(builder, "key", Key);
        ViewerPayload.Append(builder, "body", Body);
        ViewerPayload.Append(builder, "member", Member);
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

        message = new(verb.Value, key, body, member);
        return true;
    }
}
