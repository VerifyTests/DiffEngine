namespace DiffEngine;

/// <summary>
/// Projects pending entries into listing items, shared by both hosts so a conflicted entry lists
/// identically whoever owns the queue — the same argument that put one <see cref="InlineQueue"/>
/// behind both.
/// </summary>
static class ViewerListing
{
    public static List<ViewerResponseItem> Items(IEnumerable<PendingInline> entries, bool withPatches)
    {
        var items = new List<ViewerResponseItem>();
        foreach (var entry in entries)
        {
            if (withPatches)
            {
                items.Add(new(entry.Key, entry.Name, entry.Status, InlinePatchFile.Build(entry.Patch))
                {
                    Origins = entry.Variants[0].Origins,
                    Variants = entry.Variants
                        .Skip(1)
                        .Select(_ => new ViewerResponseVariant(_.Origins, InlinePatchFile.Build(_.Patch)))
                        .ToList()
                });
                continue;
            }

            // A listing without patches carries no variant lines to show the conflict, so the
            // status says it: the tray menu renders exactly this text.
            items.Add(new(
                entry.Key,
                entry.Name,
                entry.Status ?? (entry.Conflicted ? entry.ConflictStatus : null)));
        }

        return items;
    }

    /// <summary>
    /// The reverse of <see cref="Items"/>: a full listing read back into the entries it was
    /// projected from, for a process that displays or reviews a queue it does not own.
    /// <para>
    /// Here beside the projection rather than beside either reader, because the two are one format
    /// and a change to how an entry is written is a change to how it is read.
    /// </para>
    /// </summary>
    public static List<PendingInline> Pending(IEnumerable<ViewerResponseItem> items) =>
        items
            .Select(Read)
            .OfType<PendingInline>()
            .ToList();

    /// <summary>
    /// An item with no patch, or whose payload does not parse, is dropped rather than surfaced as
    /// an entry with nothing in it.
    /// </summary>
    static PendingInline? Read(ViewerResponseItem item)
    {
        if (item.Patch is null ||
            !InlinePatchFile.TryParse(item.Patch, out var patch))
        {
            return null;
        }

        var variants = new List<InlineVariant>
        {
            new(patch, item.Origins)
        };
        foreach (var variant in item.Variants)
        {
            if (InlinePatchFile.TryParse(variant.Patch, out var extra))
            {
                variants.Add(new(extra, variant.Origins));
            }
        }

        return new(variants, item.Status);
    }
}
