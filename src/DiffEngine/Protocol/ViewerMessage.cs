namespace DiffEngine;

/// <summary>
/// A request to whoever owns the inline queue. <paramref name="Key"/> identifies a queue entry for
/// the verbs that act on one; <paramref name="Body"/> carries an <see cref="InlinePatchFile"/>
/// payload for <see cref="ViewerVerb.Inline"/>.
/// </summary>
record ViewerMessage(ViewerVerb Verb, string? Key = null, string? Body = null)
{
    public string Build()
    {
        var builder = new StringBuilder($"version: {ViewerPayload.Version}\n");
        builder.Append($"verb: {Verb.ToString().ToLowerInvariant()}\n");
        ViewerPayload.Append(builder, "key", Key);
        ViewerPayload.Append(builder, "body", Body);
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

        message = new(verb.Value, key, body);
        return true;
    }
}
