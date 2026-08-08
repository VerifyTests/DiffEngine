/// <summary>
/// A request to the running viewer. <paramref name="Key"/> identifies a queue entry for the
/// verbs that act on one; <paramref name="Body"/> carries an <see cref="InlinePatchFile"/>
/// payload for <see cref="ViewerVerb.Inline"/>.
/// </summary>
record ViewerMessage(ViewerVerb Verb, string? Key = null, string? Body = null)
{
    public string Build()
    {
        var builder = new StringBuilder($"version: {Payload.Version}\n");
        builder.Append($"verb: {Verb.ToString().ToLowerInvariant()}\n");
        Payload.Append(builder, "key", Key);
        Payload.Append(builder, "body", Body);
        return builder.ToString();
    }

    public static bool TryParse(string text, [NotNullWhen(true)] out ViewerMessage? message)
    {
        message = null;
        if (!Payload.TryReadLines(text, out var lines) ||
            !Payload.HasVersion(lines))
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
                    if (!Payload.TryDecode(value, out key))
                    {
                        return false;
                    }

                    continue;
                case "body":
                    if (!Payload.TryDecode(value, out body))
                    {
                        return false;
                    }

                    continue;
                default:
                    // Unknown fields are ignored so a newer client can add one without breaking
                    // an older viewer, matching how PiperServer tolerates unknown payload types.
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
