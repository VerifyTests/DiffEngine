/// <summary>
/// Against a marker of each test's own, in a folder nothing else knows about. These used to write
/// and delete the real one, which is one file for the whole machine: a run of them removed the
/// marker of the tray running there, and two of them at once read what the other had written.
/// </summary>
public class TrayVersionFileTest :
    IDisposable
{
    string directory = Path.Combine(Path.GetTempPath(), $"TrayVersionFileTest_{Guid.NewGuid():N}");
    string path;

    // The folder is left for Write to make, as the tray's first start on a machine leaves it
    public TrayVersionFileTest() =>
        path = Path.Combine(directory, "version.txt");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task RoundTrip()
    {
        TrayVersionFile.Write(path, "20.1.3+abc123");

        var read = TrayVersionFile.TryRead(path, out var version);
        await Assert.That(read).IsTrue();
        await Assert.That(version).IsEqualTo(new(20, 1, 3));
    }

    [Test]
    public async Task PrereleaseSuffixStripped()
    {
        TrayVersionFile.Write(path, "21.0.0-beta.1");

        var read = TrayVersionFile.TryRead(path, out var version);
        await Assert.That(read).IsTrue();
        await Assert.That(version).IsEqualTo(new(21, 0, 0));
    }

    [Test]
    public async Task MissingFileFails()
    {
        var read = TrayVersionFile.TryRead(path, out _);
        await Assert.That(read).IsFalse();
    }

    [Test]
    public async Task DeleteRemovesTheMarker()
    {
        TrayVersionFile.Write(path, "20.1.3");

        TrayVersionFile.Delete(path);

        await Assert.That(File.Exists(path)).IsFalse();
        await Assert.That(TrayVersionFile.TryRead(path, out _)).IsFalse();
    }

    /// <summary>
    /// A session that ends removes the marker whether or not the tray got as far as writing it.
    /// </summary>
    [Test]
    public async Task DeleteOfAMarkerThatIsNotThereIsQuiet()
    {
        TrayVersionFile.Delete(path);

        await Assert.That(Directory.Exists(directory)).IsFalse();
    }

    [Test]
    public async Task GarbageFails()
    {
        TrayVersionFile.Write(path, "garbage");

        var read = TrayVersionFile.TryRead(path, out _);
        await Assert.That(read).IsFalse();
    }

    /// <summary>
    /// The one thing here that names the real marker, and only names it: where it is has to stay
    /// where every DiffEngine already released looks for it.
    /// </summary>
    [Test]
    public async Task TheMarkerIsWhereItHasAlwaysBeen() =>
        await Assert.That(TrayVersionFile.FilePath)
            .IsEqualTo(Path.Combine(Path.GetTempPath(), "DiffEngineTray", "version.txt"));
}
