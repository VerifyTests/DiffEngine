/// <summary>
/// The tracker with the resolver the Windows head hands it, which is the head's: the tracker's
/// own tests are the core's, and run on every platform.
/// </summary>
public class LockedFilesHandlerTest :
    IDisposable
{
    string tempDirectory;
    string temp;
    string target;

    public LockedFilesHandlerTest()
    {
        // The move's temp sits in its own directory, the way DiffEngine stages received files,
        // because accepting a move deletes that directory.
        tempDirectory = Path.Combine(Path.GetTempPath(), $"TrackedFilesTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        temp = Path.Combine(tempDirectory, "Sample.Test.received.txt");
        target = Path.Combine(Path.GetTempPath(), $"TrackedFilesTest_{Guid.NewGuid():N}.verified.txt");
    }

    public void Dispose()
    {
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }

        if (File.Exists(target))
        {
            File.Delete(target);
        }

        FileEx.SafeDeleteDirectory(tempDirectory);
    }

    /// <summary>
    /// With "Always kill locking processes" on, the menu accepts a locked move by killing
    /// the locker without asking. The same accept arriving from the viewer is refused as locked,
    /// because the preference lives inside the resolver and a wire accept never consults it.
    /// </summary>
    [Test]
    public async Task AlwaysKillAppliesToAnAcceptArrivingOverTheSocket()
    {
        var previous = LockedFilesPolicy.AlwaysKill;
        // The stored preference, which Program loads into this at startup
        LockedFilesPolicy.AlwaysKill = true;
        try
        {
            await File.WriteAllTextAsync(temp, "new");
            await File.WriteAllTextAsync(target, "old");
            // The resolver Program passes
            await using var tracker = new RecordingTracker(LockedFilesHandler.Resolve);
            var locker = FileLockUtils.StartFileLockProcess(target);
            try
            {
                tracker.AddMove(temp, target, "theExe", "theArguments", false, null);

                // What OwnedInlineHost does with an accept sent by a viewer displaying this queue
                var (ok, message) = ((ITrackedFiles) tracker).Accept(TrackedKeys.ForMove(temp));

                await Assert.That(message).IsNotEqualTo($"Files for '{Path.GetFileNameWithoutExtension(target)}' are locked. Accept from the tray menu to resolve.");
                await Assert.That(ok).IsTrue();
            }
            finally
            {
                FileLockUtils.Cleanup(locker);
            }

            await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("new");
        }
        finally
        {
            LockedFilesPolicy.AlwaysKill = previous;
        }
    }
}
