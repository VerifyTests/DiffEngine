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
    [RunOn(TUnit.Core.Enums.OS.Linux | TUnit.Core.Enums.OS.MacOs)]
    public async Task A_directory_that_cannot_be_removed_does_not_fail_the_move()
    {
        // The received file is two deep, and it is the middle directory that cannot be removed.
        // Everything the move itself touches - taking the file out of "received", and writing it
        // into a directory of its own - stays permitted, so the only thing denied is the tidy-up
        var locked = Path.Combine(root, "locked");
        var received = Path.Combine(locked, "received");
        Directory.CreateDirectory(received);
        var temp = Path.Combine(received, "sample.received.txt");
        await File.WriteAllTextAsync(temp, "the snapshot");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        var verified = Path.Combine(target, "sample.verified.txt");
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        ViewerActions.Real.MoveFile(temp, verified);

        await Assert.That(File.Exists(verified)).IsTrue();
        // Still there, which is the point: it could not be removed and that did not matter
        await Assert.That(Directory.Exists(received)).IsTrue();
    }

    [Test]
    public async Task An_emptied_directory_is_removed()
    {
        var received = Path.Combine(root, "received");
        Directory.CreateDirectory(received);
        var temp = Path.Combine(received, "sample.received.txt");
        await File.WriteAllTextAsync(temp, "the snapshot");
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
        await File.WriteAllTextAsync(temp, "the snapshot");
        await File.WriteAllTextAsync(Path.Combine(received, "other.received.txt"), "another");
        var target = Path.Combine(root, "sample.verified.txt");

        ViewerActions.Real.MoveFile(temp, target);

        await Assert.That(Directory.Exists(received)).IsTrue();
    }

    public MoveSweepTests()
    {
        root = Path.Combine(Path.GetTempPath(), $"MoveSweepTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        // Whatever the test denied itself, given back, or the tree cannot be removed here either
        if (!OperatingSystem.IsWindows())
        {
            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Directory.Delete(root, true);
    }

    readonly string root;
}
