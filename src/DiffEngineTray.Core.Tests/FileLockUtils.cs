static class FileLockUtils
{
    /// <param name="path">The file the process holds open, with nothing else let in.</param>
    /// <param name="shows">
    /// A received file to name on the command line, for a process standing in for a diff tool
    /// showing that pair: the tray tracks a process as a move's tool only when it was started
    /// with the move's received file.
    /// </param>
    public static Process StartFileLockProcess(string path, string? shows = null)
    {
        var script = $"$f = [System.IO.File]::Open('{path.Replace("'", "''")}', 'Open', 'ReadWrite', 'None'); [Console]::WriteLine('locked'); Start-Sleep -Seconds 60";
        if (shows is not null)
        {
            // A comment to the script, and the last thing inside the quotes the command is passed
            // in, so the path ends at a quote as it does where a diff tool is handed one
            script = $"{script} # {shows}";
        }

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