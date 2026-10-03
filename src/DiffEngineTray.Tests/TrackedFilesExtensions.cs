static class TrackedFilesExtensions
{
    /// <summary>
    /// An accept-all over everything tracked as it is called, for the tests that are about what a
    /// sweep does rather than about when its deletes were listed. The queue owner lists them as its
    /// batch begins and sweeps once its snapshots are done; nothing arrives in between here.
    /// </summary>
    public static (int accepted, int kept) AcceptAllTracked(this ITrackedFiles tracked, bool holdDeletes) =>
        tracked.AcceptAll(
            tracked
                .Deletes()
                .Select(_ => _.Key)
                .ToList(),
            holdDeletes);
}
