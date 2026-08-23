/// <summary>
/// Accepting a tracked move also removes the directory the received file sat in, when nothing is
/// left in it. That is a tidy-up after the fact, and the move it follows has already happened.
/// </summary>
public class MoveSweepTests :
    IDisposable
{
    /// <summary>
    /// The caller reads a throw from here as the move having failed, so it re-tracks the entry -
    /// and the retry then fails with file not found, the temp file having been moved by the
    /// attempt that "failed". Only IOException was caught, and a directory the parent will not
    /// let go of raises UnauthorizedAccessException.
    /// </summary>
    [Test]
    [SkipOnWindows("A read-only directory raises IOException on Windows, which was always caught. Denying the removal takes a Unix permission.")]
    public async Task A_directory_that_cannot_be_removed_does_not_fail_the_move()
    {
        var received = Path.Combine(root, "received");
        Directory.CreateDirectory(received);
        var temp = Path.Combine(received, "sample.received.txt");
        File.WriteAllText(temp, "the snapshot");
        var target = Path.Combine(root, "sample.verified.txt");

        // Removing "received" is a write to the directory holding it, which this denies. Moving
        // the file out of it is a write to "received" itself, which stays allowed
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        ViewerActions.Real.MoveFile(temp, target);

        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await Assert.That(File.Exists(target)).IsTrue();
    }

    [Test]
    public async Task An_emptied_directory_is_removed()
    {
        var received = Path.Combine(root, "received");
        Directory.CreateDirectory(received);
        var temp = Path.Combine(received, "sample.received.txt");
        File.WriteAllText(temp, "the snapshot");
        var target = Path.Combine(root, "sample.verified.txt");

        ViewerActions.Real.MoveFile(temp, target);

        await Assert.That(File.Exists(target)).IsTrue();
        await Assert.That(Directory.Exists(received)).IsFalse();
    }

    /// <summary>
    /// And one still holding something is left alone.
    /// </summary>
    [Test]
    public async Task A_directory_with_anything_left_in_it_stays()
    {
        var received = Path.Combine(root, "received");
        Directory.CreateDirectory(received);
        var temp = Path.Combine(received, "sample.received.txt");
        File.WriteAllText(temp, "the snapshot");
        File.WriteAllText(Path.Combine(received, "other.received.txt"), "another");
        var target = Path.Combine(root, "sample.verified.txt");

        ViewerActions.Real.MoveFile(temp, target);

        await Assert.That(Directory.Exists(received)).IsTrue();
    }

    public MoveSweepTests()
    {
        root = Path.Combine(Path.GetTempPath(), $"MoveSweepTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(root);
    }

    public void Dispose() =>
        Directory.Delete(root, true);

    readonly string root;
}
