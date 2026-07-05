static class FileLockUtils
{
    public static Process StartFileLockProcess(string path)
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

    public static bool IsFileLocked(string path)
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

    public static void Cleanup(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill();
            process.WaitForExit(5000);
        }

        process.Dispose();
    }
}