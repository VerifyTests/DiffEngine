namespace DiffEngine;

/// <summary>
/// One entry in a listing. <paramref name="Patch"/> is only carried by
/// <see cref="ViewerVerb.ListFull"/>, as an <see cref="InlinePatchFile"/> payload, and is what
/// lets a reader derive the diff locally instead of it crossing the wire.
/// </summary>
record ViewerResponseItem(string Key, string Name, string? Status, string? Patch = null);

/// <summary>
/// The reply the queue owner writes before closing the connection. Only the listing verbs populate
/// <paramref name="Items"/>; the rest report an outcome the tray can show in a balloon.
/// </summary>
record ViewerResponse(bool Ok, string? Message, IReadOnlyList<ViewerResponseItem> Items)
{
    public static ViewerResponse Success(string? message = null) =>
        new(true, message, []);

    public static ViewerResponse Error(string message) =>
        new(false, message, []);

    public static ViewerResponse Listing(IReadOnlyList<ViewerResponseItem> items) =>
        new(true, null, items);

    public string Build()
    {
        var builder = new StringBuilder($"version: {ViewerPayload.Version}\n");
        builder.Append($"status: {(Ok ? "ok" : "error")}\n");
        ViewerPayload.Append(builder, "message", Message);
        foreach (var item in Items)
        {
            var status = item.Status is null ? "" : ViewerPayload.Encode(item.Status);
            var head = $"{ViewerPayload.Encode(item.Key)}|{ViewerPayload.Encode(item.Name)}|{status}";
            if (item.Patch is null)
            {
                builder.Append($"item: {head}\n");
                continue;
            }

            // Its own line name rather than a fourth field on `item`, so a reader that only wants
            // a listing skips these entirely instead of failing to split them.
            builder.Append($"full: {head}|{ViewerPayload.Encode(item.Patch)}\n");
        }

        return builder.ToString();
    }

    public static bool TryParse(string text, [NotNullWhen(true)] out ViewerResponse? response)
    {
        response = null;
        if (!ViewerPayload.TryReadLines(text, out var lines) ||
            !ViewerPayload.HasVersion(lines))
        {
            return false;
        }

        bool? ok = null;
        string? message = null;
        var items = new List<ViewerResponseItem>();
        foreach (var (name, value) in lines)
        {
            switch (name)
            {
                case "status":
                    ok = value == "ok";
                    continue;
                case "message":
                    if (!ViewerPayload.TryDecode(value, out message))
                    {
                        return false;
                    }

                    continue;
                case "item":
                    if (!TryParseItem(value, false, out var item))
                    {
                        return false;
                    }

                    items.Add(item);
                    continue;
                case "full":
                    if (!TryParseItem(value, true, out var full))
                    {
                        return false;
                    }

                    items.Add(full);
                    continue;
                default:
                    continue;
            }
        }

        if (ok is null)
        {
            return false;
        }

        response = new(ok.Value, message, items);
        return true;
    }

    static bool TryParseItem(string value, bool withPatch, [NotNullWhen(true)] out ViewerResponseItem? item)
    {
        item = null;
        var parts = value.Split('|');
        if (parts.Length != (withPatch ? 4 : 3))
        {
            return false;
        }

        if (!ViewerPayload.TryDecode(parts[0], out var key) ||
            !ViewerPayload.TryDecode(parts[1], out var name) ||
            !ViewerPayload.TryDecode(parts[2], out var status))
        {
            return false;
        }

        string? patch = null;
        if (withPatch)
        {
            if (!ViewerPayload.TryDecode(parts[3], out var decoded))
            {
                return false;
            }

            patch = decoded;
        }

        item = new(key, name, status.Length == 0 ? null : status, patch);
        return true;
    }
}
