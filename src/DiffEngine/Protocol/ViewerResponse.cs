namespace DiffEngine;

/// <summary>
/// One non-primary variant of a listed entry: a distinct content for the same call site, with the
/// origin labels that produced it and its own <see cref="InlinePatchFile"/> payload.
/// </summary>
record ViewerResponseVariant(IReadOnlyList<string> Origins, string Patch);

/// <summary>
/// One entry in a listing. <paramref name="Patch"/> is only carried by
/// <see cref="ViewerVerb.ListFull"/>, as an <see cref="InlinePatchFile"/> payload, and is what
/// lets a reader derive the diff locally instead of it crossing the wire.
/// </summary>
record ViewerResponseItem(string Key, string Name, string? Status, string? Patch = null)
{
    /// <summary>
    /// The primary variant's origin labels. Empty for an unlabeled sender.
    /// </summary>
    public IReadOnlyList<string> Origins { get; init; } = [];

    /// <summary>
    /// The non-primary variants of a conflicted entry, in order. Empty when the entry has one
    /// content, which is almost always.
    /// </summary>
    public IReadOnlyList<ViewerResponseVariant> Variants { get; init; } = [];
}

/// <summary>
/// A tracked file move riding a full listing, so a viewer displaying the tray's queue can render
/// it from the two local paths and accept or discard it by key.
/// </summary>
record ViewerResponseMove(string Key, string Name, string? Group, string Temp, string Target);

/// <summary>
/// A tracked pending delete riding a full listing.
/// </summary>
record ViewerResponseDelete(string Key, string Name, string? Group, string File);

/// <summary>
/// The reply the queue owner writes before closing the connection.
/// </summary>
/// <param name="Ok">Whether the verb was carried out.</param>
/// <param name="Message">An outcome the tray can show in a balloon.</param>
/// <param name="Items">Populated by the listing verbs, and empty for everything else.</param>
/// <param name="Window">
/// What the owner wants done to the window, for an owner that has none of its own. Answered on a
/// listing rather than pushed, so this stays one port with no discovery order.
/// </param>
/// <param name="WindowKey">
/// The entry to select while doing it, so "Open in viewer" on a tray menu item still lands on that
/// item. Selection belongs to the display, which is why it travels with the command.
/// </param>
record ViewerResponse(
    bool Ok,
    string? Message,
    IReadOnlyList<ViewerResponseItem> Items,
    WindowCommand? Window = null,
    string? WindowKey = null)
{
    /// <summary>
    /// The tray's tracked moves, on a full listing from a tray owner. A viewer that owns the
    /// queue never has any: DiffEngine only sends moves and deletes to a running tray.
    /// </summary>
    public IReadOnlyList<ViewerResponseMove> Moves { get; init; } = [];

    public IReadOnlyList<ViewerResponseDelete> Deletes { get; init; } = [];

    public static ViewerResponse Success(string? message = null) =>
        new(true, message, []);

    public static ViewerResponse Error(string message) =>
        new(false, message, []);

    public static ViewerResponse Listing(
        IReadOnlyList<ViewerResponseItem> items,
        WindowCommand? window = null,
        string? windowKey = null,
        IReadOnlyList<ViewerResponseMove>? moves = null,
        IReadOnlyList<ViewerResponseDelete>? deletes = null) =>
        new(true, null, items, window, windowKey)
        {
            Moves = moves ?? [],
            Deletes = deletes ?? []
        };

    public string Build()
    {
        var builder = new StringBuilder($"version: {ViewerPayload.Version}\n");
        builder.Append($"status: {(Ok ? "ok" : "error")}\n");
        ViewerPayload.Append(builder, "message", Message);
        if (Window is not null)
        {
            // Plain, like `verb` and `status`. Only the encoded fields can carry snapshot text.
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            builder.Append($"window: {Window.ToString()!.ToLowerInvariant()}\n");
            ViewerPayload.Append(builder, "windowKey", WindowKey);
        }

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
            builder.Append($"full: {head}|{EncodeOrigins(item.Origins)}|{ViewerPayload.Encode(item.Patch)}\n");
            foreach (var variant in item.Variants)
            {
                builder.Append($"variant: {ViewerPayload.Encode(item.Key)}|{EncodeOrigins(variant.Origins)}|{ViewerPayload.Encode(variant.Patch)}\n");
            }
        }

        foreach (var move in Moves)
        {
            var group = move.Group is null ? "" : ViewerPayload.Encode(move.Group);
            builder.Append($"move: {ViewerPayload.Encode(move.Key)}|{ViewerPayload.Encode(move.Name)}|{group}|{ViewerPayload.Encode(move.Temp)}|{ViewerPayload.Encode(move.Target)}\n");
        }

        foreach (var delete in Deletes)
        {
            var group = delete.Group is null ? "" : ViewerPayload.Encode(delete.Group);
            builder.Append($"delete: {ViewerPayload.Encode(delete.Key)}|{ViewerPayload.Encode(delete.Name)}|{group}|{ViewerPayload.Encode(delete.File)}\n");
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
        WindowCommand? window = null;
        string? windowKey = null;
        var items = new List<ViewerResponseItem>();
        var moves = new List<ViewerResponseMove>();
        var deletes = new List<ViewerResponseDelete>();
        Dictionary<string, List<ViewerResponseVariant>>? variants = null;
        foreach (var (name, value) in lines)
        {
            switch (name)
            {
                case "status":
                    ok = value == "ok";
                    continue;
                case "window":
                    if (!Enum.TryParse<WindowCommand>(value, true, out var command))
                    {
                        return false;
                    }

                    window = command;
                    continue;
                case "windowKey":
                    if (!ViewerPayload.TryDecode(value, out windowKey))
                    {
                        return false;
                    }

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
                case "variant":
                    if (!TryParseVariant(value, out var variantKey, out var variant))
                    {
                        return false;
                    }

                    variants ??= new(StringComparer.Ordinal);
                    if (!variants.TryGetValue(variantKey, out var list))
                    {
                        variants[variantKey] = list = [];
                    }

                    list.Add(variant);
                    continue;
                case "move":
                    if (!TryParseMove(value, out var move))
                    {
                        return false;
                    }

                    moves.Add(move);
                    continue;
                case "delete":
                    if (!TryParseDelete(value, out var delete))
                    {
                        return false;
                    }

                    deletes.Add(delete);
                    continue;
                default:
                    continue;
            }
        }

        if (ok is null)
        {
            return false;
        }

        // Attached after the loop rather than during it, so the parse does not depend on variant
        // lines following their entry. A variant for a key with no entry is dropped.
        if (variants is not null)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (variants.TryGetValue(items[index].Key, out var list))
                {
                    items[index] = items[index] with { Variants = list };
                }
            }
        }

        response = new(ok.Value, message, items, window, windowKey)
        {
            Moves = moves,
            Deletes = deletes
        };
        return true;
    }

    static string EncodeOrigins(IReadOnlyList<string> origins) =>
        origins.Count == 0 ? "" : ViewerPayload.Encode(string.Join(",", origins));

    static bool TryDecodeOrigins(string value, out IReadOnlyList<string> origins)
    {
        if (value.Length == 0)
        {
            origins = [];
            return true;
        }

        if (!ViewerPayload.TryDecode(value, out var joined))
        {
            origins = [];
            return false;
        }

        origins = joined.Split(',');
        return true;
    }

    static bool TryParseItem(string value, bool withPatch, [NotNullWhen(true)] out ViewerResponseItem? item)
    {
        item = null;
        var parts = value.Split('|');
        if (parts.Length != (withPatch ? 5 : 3))
        {
            return false;
        }

        if (!ViewerPayload.TryDecode(parts[0], out var key) ||
            !ViewerPayload.TryDecode(parts[1], out var name) ||
            !ViewerPayload.TryDecode(parts[2], out var status))
        {
            return false;
        }

        if (!withPatch)
        {
            item = new(key, name, status.Length == 0 ? null : status);
            return true;
        }

        if (!TryDecodeOrigins(parts[3], out var origins) ||
            !ViewerPayload.TryDecode(parts[4], out var patch))
        {
            return false;
        }

        item = new(key, name, status.Length == 0 ? null : status, patch)
        {
            Origins = origins
        };
        return true;
    }

    static bool TryParseVariant(string value, out string key, [NotNullWhen(true)] out ViewerResponseVariant? variant)
    {
        key = "";
        variant = null;
        var parts = value.Split('|');
        if (parts.Length != 3 ||
            !ViewerPayload.TryDecode(parts[0], out var decodedKey) ||
            !TryDecodeOrigins(parts[1], out var origins) ||
            !ViewerPayload.TryDecode(parts[2], out var patch))
        {
            return false;
        }

        key = decodedKey;
        variant = new(origins, patch);
        return true;
    }

    static bool TryParseMove(string value, [NotNullWhen(true)] out ViewerResponseMove? move)
    {
        move = null;
        var parts = value.Split('|');
        if (parts.Length != 5 ||
            !ViewerPayload.TryDecode(parts[0], out var key) ||
            !ViewerPayload.TryDecode(parts[1], out var name) ||
            !ViewerPayload.TryDecode(parts[2], out var group) ||
            !ViewerPayload.TryDecode(parts[3], out var temp) ||
            !ViewerPayload.TryDecode(parts[4], out var target))
        {
            return false;
        }

        move = new(key, name, group.Length == 0 ? null : group, temp, target);
        return true;
    }

    static bool TryParseDelete(string value, [NotNullWhen(true)] out ViewerResponseDelete? delete)
    {
        delete = null;
        var parts = value.Split('|');
        if (parts.Length != 4 ||
            !ViewerPayload.TryDecode(parts[0], out var key) ||
            !ViewerPayload.TryDecode(parts[1], out var name) ||
            !ViewerPayload.TryDecode(parts[2], out var group) ||
            !ViewerPayload.TryDecode(parts[3], out var file))
        {
            return false;
        }

        delete = new(key, name, group.Length == 0 ? null : group, file);
        return true;
    }
}
