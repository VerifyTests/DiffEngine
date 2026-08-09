record ViewerResponseItem(string Key, string Name, string? Status);

/// <summary>
/// The reply the viewer writes before closing the connection. Only <see cref="ViewerVerb.List"/>
/// populates <paramref name="Items"/>; the rest report an outcome the tray can show in a balloon.
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
        var builder = new StringBuilder($"version: {Payload.Version}\n");
        builder.Append($"status: {(Ok ? "ok" : "error")}\n");
        Payload.Append(builder, "message", Message);
        foreach (var item in Items)
        {
            var status = item.Status is null ? "" : Payload.Encode(item.Status);
            builder.Append($"item: {Payload.Encode(item.Key)}|{Payload.Encode(item.Name)}|{status}\n");
        }

        return builder.ToString();
    }

    public static bool TryParse(string text, [NotNullWhen(true)] out ViewerResponse? response)
    {
        response = null;
        if (!Payload.TryReadLines(text, out var lines) ||
            !Payload.HasVersion(lines))
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
                    if (!Payload.TryDecode(value, out message))
                    {
                        return false;
                    }

                    continue;
                case "item":
                    if (!TryParseItem(value, out var item))
                    {
                        return false;
                    }

                    items.Add(item);
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

    static bool TryParseItem(string value, [NotNullWhen(true)] out ViewerResponseItem? item)
    {
        item = null;
        var parts = value.Split('|');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!Payload.TryDecode(parts[0], out var key) ||
            !Payload.TryDecode(parts[1], out var name) ||
            !Payload.TryDecode(parts[2], out var status))
        {
            return false;
        }

        item = new(key, name, status.Length == 0 ? null : status);
        return true;
    }
}
