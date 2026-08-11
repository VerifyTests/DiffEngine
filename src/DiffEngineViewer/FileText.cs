/// <summary>
/// Freshness token for a file a move or delete entry renders: enough to tell "unchanged since the
/// last pump" without reading the content again.
/// </summary>
readonly record struct FileStamp(long WriteTicksUtc, long Length);

/// <summary>
/// One guarded read of a file a tracked move or delete points at. Never throws: the poller that
/// materializes these must survive a file vanishing mid-pump — a throw there closes the window as
/// "owner gone" — so a missing or locked file degrades to empty text plus a warning.
/// </summary>
readonly record struct FileText(string Text, FileStamp? Stamp, string? Warning)
{
    public static FileText Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                // Normal for a move: a brand new snapshot has no verified file yet.
                return new("", null, null);
            }

            return new(
                File.ReadAllText(path),
                new FileStamp(info.LastWriteTimeUtc.Ticks, info.Length),
                null);
        }
        catch (Exception exception)
        {
            return new("", null, $"Could not read {path}. {exception.Message}");
        }
    }

    public static FileStamp? StampOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            return new FileStamp(info.LastWriteTimeUtc.Ticks, info.Length);
        }
        catch
        {
            return null;
        }
    }
}
