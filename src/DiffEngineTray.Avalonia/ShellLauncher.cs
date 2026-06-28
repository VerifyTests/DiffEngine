namespace DiffEngineTray;

// Cross-platform equivalents of the Windows ExplorerLauncher (open a directory / reveal a file).
static class ShellLauncher
{
    public static void OpenDirectory(string directory)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Start("open", directory);
            }
            else
            {
                Start("xdg-open", directory);
            }
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to open directory: {directory}", exception);
        }
    }

    public static void RevealFile(string file)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                // -R reveals (selects) the file in Finder.
                Start("open", "-R", file);
                return;
            }

            // Linux file managers have no portable "reveal" verb; open the containing directory.
            var directory = Path.GetDirectoryName(file);
            if (directory != null)
            {
                Start("xdg-open", directory);
            }
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to reveal file: {file}", exception);
        }
    }

    static void Start(string fileName, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using (Process.Start(info))
        {
        }
    }
}
