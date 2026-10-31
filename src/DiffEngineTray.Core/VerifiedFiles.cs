/// <summary>
/// What "Purge verified files" finds and deletes, the same on every head: every
/// <c>*.verified.*</c> and <c>*.received.*</c> file under a folder.
/// </summary>
static class VerifiedFiles
{
    /// <summary>
    /// SearchOption.AllDirectories is the compatibility enumeration: it does not ignore what it
    /// cannot read, and it follows reparse points. A purge can be pointed at a profile folder or a
    /// drive root, either of which holds a deny-ACL junction - Application Data, $Recycle.Bin - so
    /// the scan reliably threw UnauthorizedAccessException before it had looked at a single file.
    /// </summary>
    static readonly EnumerationOptions enumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static string[] Find(string path) =>
    [
        .. Directory.GetFiles(path, "*.verified.*", enumeration),
        .. Directory.GetFiles(path, "*.received.*", enumeration)
    ];

    /// <summary>
    /// A file another process holds open with no sharing cannot be deleted on Windows, so there
    /// the processes holding it are ended and the delete tried once more. Elsewhere a lock keeps
    /// nothing out, and the restart manager that would be asked is not there.
    /// </summary>
    public static DeleteResult TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }

            return new(true, null);
        }
        catch (Exception exception)
        {
            if (OperatingSystem.IsWindows() &&
                FileLockKiller.KillLockingProcesses(file))
            {
                try
                {
                    File.Delete(file);
                    return new(true, null);
                }
                catch (Exception retryException)
                {
                    return new(false, retryException);
                }
            }

            return new(false, exception);
        }
    }

    public record DeleteResult(bool Deleted, Exception? Exception);
}
