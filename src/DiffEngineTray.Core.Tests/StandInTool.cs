/// <summary>
/// A process standing in for a diff tool showing a pair: started with the pair's received file on
/// its command line, as every tool DiffEngine starts is, and running until it is ended or a minute
/// has passed. Windows PowerShell on Windows and bash elsewhere, since each is on every machine the
/// tests run on.
/// </summary>
static class StandInTool
{
    /// <summary>
    /// The executable the stand-in runs, as a sender would name it.
    /// </summary>
    public static string Image { get; } =
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe")
            : "/bin/bash";

    /// <param name="shows">
    /// The received file to name on the command line, or null for a copy of the tool showing
    /// something else.
    /// </param>
    public static Process Start(string? shows)
    {
        var info = OperatingSystem.IsWindows() ? PowerShell(shows) : Bash(shows);
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        var process = Process.Start(info)!;

        var line = process.StandardOutput.ReadLine();
        if (line != "ready")
        {
            throw new InvalidOperationException($"Expected 'ready' but got '{line}'");
        }

        return process;
    }

    static ProcessStartInfo PowerShell(string? shows)
    {
        var script = "[Console]::WriteLine('ready'); Start-Sleep -Seconds 60";
        if (shows is not null)
        {
            // A comment to the script, and the last thing inside the quotes the command is passed
            // in, so the path ends at a quote as it does where a diff tool is handed one
            script = $"{script} # {shows}";
        }

        return new(Image, $"-NoProfile -Command \"{script}\"");
    }

    static ProcessStartInfo Bash(string? shows)
    {
        // Waits on its input, which nothing writes to, rather than on a sleep: a sleep is a
        // process of its own, which ending this one would leave running. The input closes with
        // the test process, so a run that dies leaves nothing behind either
        var info = new ProcessStartInfo(Image)
        {
            ArgumentList =
            {
                "-c",
                "echo ready; read -t 60; exit 0",
                "tool"
            },
            RedirectStandardInput = true
        };
        if (shows is not null)
        {
            info.ArgumentList.Add(shows);
        }

        return info;
    }
}
