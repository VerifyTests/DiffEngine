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
}
