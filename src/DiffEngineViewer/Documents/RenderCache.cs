/// <summary>
/// Where a viewer copies the documents it reads and draws their pages: one directory per content
/// hash, holding the copy and its pngs, in a temp directory of the viewer's own.
/// <para>
/// A copy, because the documents assembly opens what it is given with read sharing only, and a
/// handle held on a received file for the length of a conversion refuses the accept that moves it.
/// It is also what makes the hash a promise: the copy is checked against it as it is made, so what
/// is drawn is the bytes the side describes rather than whatever a re-run wrote since.
/// </para>
/// <para>
/// Owned through a lock file held for the viewer's life rather than through its process id, since
/// ids are reused. A directory whose lock can be taken belongs to a viewer that has gone, and the
/// next one to start clears it: on Windows the lock file goes with its handle, elsewhere the
/// advisory lock does.
/// </para>
/// </summary>
sealed class RenderCache :
    IDisposable
{
    const string prefix = "DiffEngineViewer-documents-";
    const string lockName = "owner.lock";

    readonly FileStream held;

    RenderCache(string root, FileStream held)
    {
        Root = root;
        this.held = held;
    }

    public string Root { get; }

    public static RenderCache Create()
    {
        Sweep();
        var root = Directory.CreateTempSubdirectory(prefix).FullName;
        var held = new FileStream(
            Path.Combine(root, lockName),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            1,
            FileOptions.DeleteOnClose);
        return new(root, held);
    }

    /// <summary>
    /// The directory for one document's copy and pages, made if it is not there yet.
    /// </summary>
    public string For(string hash) =>
        Directory.CreateDirectory(Path.Combine(Root, hash)).FullName;

    public IEnumerable<string> Hashes() =>
        Directory
            .EnumerateDirectories(Root)
            .Select(Path.GetFileName)
            .OfType<string>();

    public void Forget(string hash) =>
        TryDelete(Path.Combine(Root, hash));

    public void Dispose()
    {
        held.Dispose();
        TryDelete(Root);
    }

    static void Sweep()
    {
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(Path.GetTempPath(), $"{prefix}*").ToList();
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var directory in directories)
        {
            if (Abandoned(directory))
            {
                TryDelete(directory);
            }
        }
    }

    static bool Abandoned(string directory)
    {
        try
        {
            // Young enough that whoever made it may not have taken its lock yet.
            if (DateTime.UtcNow - Directory.GetCreationTimeUtc(directory) < TimeSpan.FromMinutes(1))
            {
                return false;
            }

            using var probe = new FileStream(
                Path.Combine(directory, lockName),
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return true;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // Held by a viewer still running, or another user's.
            return false;
        }
    }

    static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, true);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // A head can still have a page open for a moment; the next sweep has it.
        }
    }
}
