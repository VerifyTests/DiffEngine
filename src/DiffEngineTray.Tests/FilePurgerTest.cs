public class FilePurgerTest
{
    /// <summary>
    /// The dialog lets the user pick a profile folder or a drive root, and both hold something
    /// the scan cannot read. SearchOption.AllDirectories threw on the first one, out of a bare
    /// Thread with nothing catching it, which took the whole tray down.
    /// </summary>
    [Test]
    public async Task Find_SkipsInaccessibleDirectories()
    {
        var root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"FilePurgerTest_{Guid.NewGuid()}"));
        var denied = root.CreateSubdirectory("denied");
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "a.verified.txt"), "content");

        var security = denied.GetAccessControl();
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny);
        security.AddAccessRule(rule);
        denied.SetAccessControl(security);
        try
        {
            var found = FilePurger.Find(root.FullName);

            await Assert.That(found).HasSingleItem();
            await Assert.That(Path.GetFileName(found[0])).IsEqualTo("a.verified.txt");
        }
        finally
        {
            security.RemoveAccessRule(rule);
            denied.SetAccessControl(security);
            root.Delete(true);
        }
    }
    [Test]
    public async Task DeleteSucceeds_WhenFileNotLocked()
    {
        var file = Path.Combine(Path.GetTempPath(), $"FilePurgerTest_{Guid.NewGuid()}.verified.txt");
        await File.WriteAllTextAsync(file, "content");

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
        await File.WriteAllTextAsync(file, "content");

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
