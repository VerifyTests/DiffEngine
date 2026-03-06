public class FileLockKillerTest
{
    [Test]
    public async Task KillLockingProcesses_WhenFileNotLocked_ReturnsFalse()
    {
        var file = Path.Combine(Path.GetTempPath(), $"FileLockKillerTest_{Guid.NewGuid()}.txt");
        try
        {
            File.WriteAllText(file, "content");
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
        File.WriteAllText(file, "content");

        var lockProcess = StartFileLockProcess(file);

        try
        {
            await Assert.That(IsFileLocked(file)).IsTrue();

            var result = FileLockKiller.KillLockingProcesses(file);

            await Assert.That(result).IsTrue();

            var exited = lockProcess.WaitForExit(5000);
            await Assert.That(exited).IsTrue();
        }
        finally
        {
            if (!lockProcess.HasExited)
            {
                lockProcess.Kill();
            }

            lockProcess.Dispose();
            File.Delete(file);
        }
    }

    [Test]
    public async Task MoveSucceedsAfterKillingLockingProcess()
    {
        var file = Path.Combine(Path.GetTempPath(), $"FileLockKillerTest_{Guid.NewGuid()}.txt");
        var tempFile = Path.Combine(Path.GetTempPath(), $"FileLockKillerTest_{Guid.NewGuid()}.txt");
        File.WriteAllText(file, "content");
        File.WriteAllText(tempFile, "new content");

        var lockProcess = StartFileLockProcess(file);

        try
        {
            await Assert.That(IsFileLocked(file)).IsTrue();
            await Assert.That(FileEx.SafeMove(tempFile, file)).IsFalse();

            FileLockKiller.KillLockingProcesses(file);

            await Assert.That(FileEx.SafeMove(tempFile, file)).IsTrue();
            await Assert.That(File.ReadAllText(file)).IsEqualTo("new content");
        }
        finally
        {
            if (!lockProcess.HasExited)
            {
                lockProcess.Kill();
            }

            lockProcess.Dispose();
            File.Delete(file);
            File.Delete(tempFile);
        }
    }

    static Process StartFileLockProcess(string path)
    {
        var script = $"$f = [System.IO.File]::Open('{path.Replace("'", "''")}', 'Open', 'ReadWrite', 'None'); [Console]::WriteLine('locked'); Start-Sleep -Seconds 60";
        var process = new Process
        {
            StartInfo = new()
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            }
        };
        process.Start();

        // Wait for the process to signal that it has acquired the lock
        var line = process.StandardOutput.ReadLine();
        if (line != "locked")
        {
            throw new InvalidOperationException($"Expected 'locked' but got '{line}'");
        }

        return process;
    }

    static bool IsFileLocked(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
