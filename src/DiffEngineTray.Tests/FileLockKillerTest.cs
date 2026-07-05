public class FileLockKillerTest
{
    [Test]
    public async Task GetLockingProcesses_WhenFileNotLocked_ReturnsEmpty()
    {
        var file = Path.Combine(Path.GetTempPath(), $"FileLockKillerTest_{Guid.NewGuid()}.txt");
        try
        {
            await File.WriteAllTextAsync(file, "content");
            var result = FileLockKiller.GetLockingProcesses(file);
            await Assert.That(result).IsEmpty();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task GetLockingProcesses_WhenFileLocked_ReturnsProcess()
    {
        var file = Path.Combine(Path.GetTempPath(), $"FileLockKillerTest_{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(file, "content");

        var lockProcess = FileLockUtils.StartFileLockProcess(file);

        try
        {
            await Assert.That(FileLockUtils.IsFileLocked(file)).IsTrue();

            var result = FileLockKiller.GetLockingProcesses(file);

            await Assert.That(result.Select(_ => _.ProcessId)).Contains(lockProcess.Id);
        }
        finally
        {
            FileLockUtils.Cleanup(lockProcess);
            File.Delete(file);
        }
    }

    [Test]
    public async Task KillLockingProcesses_WhenFileNotLocked_ReturnsFalse()
    {
        var file = Path.Combine(Path.GetTempPath(), $"FileLockKillerTest_{Guid.NewGuid()}.txt");
        try
        {
            await File.WriteAllTextAsync(file, "content");
            var result = FileLockKiller.KillLockingProcesses(file);
            await Assert.That(result).IsFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task KillLockingProcesses_WhenFileDoesNotExist_ReturnsFalse()
    {
        var result = FileLockKiller.KillLockingProcesses(
            Path.Combine(Path.GetTempPath(), $"FileLockKillerTest_{Guid.NewGuid()}.txt"));
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task KillLockingProcesses_WhenFileLocked_KillsProcess()
    {
        var file = Path.Combine(Path.GetTempPath(), $"FileLockKillerTest_{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(file, "content");

        var lockProcess = FileLockUtils.StartFileLockProcess(file);

        try
        {
            await Assert.That(FileLockUtils.IsFileLocked(file)).IsTrue();

            var result = FileLockKiller.KillLockingProcesses(file);

            await Assert.That(result).IsTrue();

            var exited = lockProcess.WaitForExit(5000);
            await Assert.That(exited).IsTrue();
        }
        finally
        {
            FileLockUtils.Cleanup(lockProcess);
            File.Delete(file);
        }
    }

    [Test]
    public async Task MoveSucceedsAfterKillingLockingProcess()
    {
        var file = Path.Combine(Path.GetTempPath(), $"FileLockKillerTest_{Guid.NewGuid()}.txt");
        var tempFile = Path.Combine(Path.GetTempPath(), $"FileLockKillerTest_{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(file, "content");
        await File.WriteAllTextAsync(tempFile, "new content");

        var lockProcess = FileLockUtils.StartFileLockProcess(file);

        try
        {
            await Assert.That(FileLockUtils.IsFileLocked(file)).IsTrue();
            await Assert.That(FileEx.SafeMove(tempFile, file)).IsFalse();

            FileLockKiller.KillLockingProcesses(file);

            await Assert.That(FileEx.SafeMove(tempFile, file)).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(file)).IsEqualTo("new content");
        }
        finally
        {
            FileLockUtils.Cleanup(lockProcess);
            File.Delete(file);
            File.Delete(tempFile);
        }
    }
}