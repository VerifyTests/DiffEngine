namespace DiffEngineTray;

static class AvaloniaUpdater
{
    public static void Run()
    {
        try
        {
            // Update in a detached shell (the tool's own files are locked while it runs),
            // then relaunch the freshly installed tool.
            var info = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("dotnet tool update diffenginetray --global --prerelease; DiffEngineTray");
            Process.Start(info);
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle("Failed to update DiffEngineTray", exception);
            return;
        }

        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
