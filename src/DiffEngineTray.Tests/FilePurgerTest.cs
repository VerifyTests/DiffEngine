public class FilePurgerTest
{
    [Test]
    public async Task DeleteSucceeds_WhenFileNotLocked()
    {
        var file = Path.Combine(Path.GetTempPath(), $"FilePurgerTest_{Guid.NewGuid()}.verified.txt");
        File.WriteAllText(file, "content");

        var result = FilePurger.TryDeleteWithLockKill(file);

        await Assert.That(result.Deleted).IsTrue();
        await Assert.That(result.Exception).IsNull();
        await Assert.That(File.Exists(file)).IsFalse();
    }

    [Test]
    public async Task DeleteSucceeds_WhenFileDoesNotExist()
    {
        var file = Path.Combine(Path.GetTempPath(), $"FilePurgerTest_{Guid.NewGuid()}.verified.txt");

        var result = FilePurger.TryDeleteWithLockKill(file);

        await Assert.That(result.Deleted).IsTrue();
        await Assert.That(result.Exception).IsNull();
    }

    [Test]
    public async Task DeleteSucceeds_WhenFileLocked_KillsLockingProcess()
    {
        var file = Path.Combine(Path.GetTempPath(), $"FilePurgerTest_{Guid.NewGuid()}.verified.txt");
        File.WriteAllText(file, "content");

        var lockProcess = StartFileLockProcess(file);

        try
        {
            await Assert.That(IsFileLocked(file)).IsTrue();

            var result = FilePurger.TryDeleteWithLockKill(file);

            await Assert.That(result.Deleted).IsTrue();
            await Assert.That(result.Exception).IsNull();
            await Assert.That(File.Exists(file)).IsFalse();

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

            if (File.Exists(file))
            {
                File.Delete(file);
            }
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
