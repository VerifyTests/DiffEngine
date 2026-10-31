public class DesktopPurgeTests :
    IDisposable
{
    [Test]
    public async Task DeletesWhatItFindsOnceConfirmed()
    {
        var kept = Write("Sample.cs");
        Write("Sample.Test.verified.txt");
        Write(Path.Combine("nested", "Sample.Test.received.png"));
        string? asked = null;

        DesktopPurge.Run(() => directory, _ =>
        {
            asked = _;
            return true;
        }, told.Add);

        await Assert.That(Remaining()).IsEquivalentTo([kept]);
        await Assert.That(asked!).StartsWith($"Files found: 2, in {directory}.");
        await Assert.That(told).IsEquivalentTo([$"Deleted 2 files from {directory}."]);
    }

    [Test]
    public async Task DeletesNothingWhenNotConfirmed()
    {
        var verified = Write("Sample.Test.verified.txt");

        DesktopPurge.Run(() => directory, _ => false, told.Add);

        await Assert.That(Remaining()).IsEquivalentTo([verified]);
        await Assert.That(told).IsEmpty();
    }

    [Test]
    public async Task AsksNothingMoreWhenNoFolderIsChosen()
    {
        Write("Sample.Test.verified.txt");
        var asked = false;

        DesktopPurge.Run(() => null, _ => asked = true, told.Add);

        await Assert.That(asked).IsFalse();
        await Assert.That(Remaining()).HasSingleItem();
        await Assert.That(told).IsEmpty();
    }

    [Test]
    public async Task SaysSoWhenThereIsNothingToPurge()
    {
        Write("Sample.cs");
        var asked = false;

        DesktopPurge.Run(() => directory, _ => asked = true, told.Add);

        await Assert.That(asked).IsFalse();
        await Assert.That(told).IsEquivalentTo([$"No *.verified.* or *.received.* files found in {directory}"]);
    }

    /// <summary>
    /// Passed over and counted, and the rest still deleted. Made undeletable the way each platform
    /// refuses a delete for: a read only file on Windows, and on Unix a folder that cannot be
    /// written, since there a file's own mode says nothing about removing it.
    /// </summary>
    [Test]
    public async Task CountsAFileThatCouldNotBeDeleted()
    {
        Write("Sample.Test.verified.txt");
        var locked = Write(Path.Combine("locked", "Sample.Test.received.txt"));
        var lockedDirectory = Path.GetDirectoryName(locked)!;
        Refuse(locked, lockedDirectory, true);
        try
        {
            DesktopPurge.Run(() => directory, _ => true, told.Add);
        }
        finally
        {
            Refuse(locked, lockedDirectory, false);
        }

        await Assert.That(Remaining()).IsEquivalentTo([locked]);
        await Assert.That(told).HasSingleItem();
        await Assert.That(told[0]).StartsWith($"Deleted 1 of 2 files from {directory}. 1 could not be deleted, the first being {locked}: ");
    }

    [Test]
    public async Task ThereIsNoPurgeWithNothingToAskWith() =>
        // As a bool, since an Action is taken as the thing to run and assert on
        await Assert.That(DesktopPurge.For(null, told.Add) is null).IsTrue();

    static void Refuse(string file, string fileDirectory, bool refuse)
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(file, refuse ? FileAttributes.ReadOnly : FileAttributes.Normal);
            return;
        }

        var mode = UnixFileMode.UserRead | UnixFileMode.UserExecute;
        if (!refuse)
        {
            mode |= UnixFileMode.UserWrite;
        }

        File.SetUnixFileMode(fileDirectory, mode);
    }

    string Write(string name)
    {
        var path = Path.Combine(directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "content");
        return path;
    }

    string[] Remaining() =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories);

    string directory = Directory.CreateTempSubdirectory("DesktopPurgeTests").FullName;
    List<string> told = [];

    public void Dispose() =>
        Directory.Delete(directory, true);
}
