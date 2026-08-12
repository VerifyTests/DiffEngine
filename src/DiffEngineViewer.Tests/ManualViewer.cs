extern alias engine;

using EngineRunner = engine::DiffEngine.DiffRunner;

/// <summary>
/// Points DiffEngine at the head built from this repo, and waits for the person driving it.
/// <para>
/// Kill any running DiffEngineViewer before starting one of these, or you will be reviewing a
/// stale build without knowing it. The viewer is single instance, and with DiffEngineTray running
/// it hides rather than exits when its window closes — so an instance from an earlier build stays
/// alive in the background, and the next launch hands its patches to that one and gets out of the
/// way. The window that appears is then the old binary, showing none of the changes just made.
/// It is a quiet failure: everything looks like it worked.
/// </para>
/// <code>
/// Get-Process DiffEngineViewer -ErrorAction SilentlyContinue | Stop-Process -Force
/// </code>
/// </summary>
static class ManualViewer
{
    /// <summary>
    /// Long enough to read a diff and think about it, short enough that a forgotten window does
    /// not wedge a test run forever.
    /// </summary>
    static readonly TimeSpan patience = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Points the resolver at the head built from this working tree.
    /// <para>
    /// From the module initializer, because DiffTools resolves and caches on first use: set any
    /// later and it is ignored. Unconditional, since nothing else in this assembly launches a
    /// viewer, but only when the head is actually there. OsSettingsResolver throws when the
    /// variable is set and cannot be resolved, which would take the whole assembly down for
    /// anyone who has not built it.
    /// </para>
    /// </summary>
    public static void Register()
    {
        var executable = Executable();
        if (File.Exists(executable))
        {
            Environment.SetEnvironmentVariable(EnvironmentVariable, executable);
        }
    }

    /// <summary>
    /// A test run counts as disabled, and these are the one case that wants a window. Per class
    /// rather than process wide, so an ordinary run cannot pop one open.
    /// </summary>
    public static void Enable()
    {
        if (!File.Exists(Executable()))
        {
            throw new($"Build the viewer head first. Not found: {Executable()}");
        }

        EngineRunner.Disabled = false;
    }

    const string EnvironmentVariable = "DiffEngine_DiffEngineViewer";

    /// <summary>
    /// The head for this OS, from the same configuration and framework this test assembly was
    /// built into, so it is always the code sitting in the working tree.
    /// </summary>
    static string Executable()
    {
        var head = OperatingSystem.IsWindows() ? "DiffEngineViewer.Windows" :
            OperatingSystem.IsMacOS() ? "DiffEngineViewer.Mac" :
            "DiffEngineViewer.Linux";
        var name = OperatingSystem.IsWindows() ? "DiffEngineViewer.exe" : "DiffEngineViewer";

        // bin/{configuration}/{tfm} under this test project, reused for the head.
        var thisBin = new DirectoryInfo(AppContext.BaseDirectory);
        var tfm = thisBin.Name;
        var configuration = thisBin.Parent!.Name;
        var source = thisBin.Parent!.Parent!.Parent!.Parent!;

        return Path.Combine(source.FullName, head, "bin", configuration, tfm, name);
    }

    public static DirectoryInfo TempDirectory()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"deview-manual-{Guid.NewGuid():N}"));
        Console.WriteLine($"Working in {directory.FullName}");
        return directory;
    }

    public static void Expect(string scenario, params string[] checks)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {scenario} ===");
        foreach (var check in checks)
        {
            Console.WriteLine($"  [ ] {check}");
        }

        Console.WriteLine(
            TrayDetector.IsRunning()
                ? "  Close the window when done. DiffEngineTray is running, so closing hides it."
                : "  Close the window when done.");
        Console.WriteLine();
    }

    /// <summary>
    /// Waits for a window to appear, then for the person to dismiss it.
    /// <para>
    /// Dismissing is not the same as exiting. With DiffEngineTray running, closing hides the
    /// window so the tray can reopen it, and the process stays alive; without one, closing exits.
    /// Both are correct, so both count, and the only difference visible from out here is that a
    /// hidden window reports no handle.
    /// </para>
    /// <para>
    /// Polled rather than waited on a handle, because the process that ends up owning the window
    /// is not always the one that was launched: a second instance forwards its patch and exits.
    /// </para>
    /// </summary>
    public static async Task WaitForClose()
    {
        // Short, because a window that has not appeared in this long is not coming, and waiting
        // out the full patience to say so wastes the run.
        if (!await Until(() => Showing(), DateTime.UtcNow + TimeSpan.FromSeconds(30)))
        {
            throw new("The viewer never showed a window within 30 seconds.");
        }

        if (await Until(() => !Showing(), DateTime.UtcNow + patience))
        {
            return;
        }

        foreach (var process in Running())
        {
            process.Kill();
        }

        throw new($"The viewer was not dismissed within {patience.TotalMinutes} minutes.");
    }

    static async Task<bool> Until(Func<bool> condition, DateTime deadline)
    {
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return false;
    }

    static bool Showing()
    {
        foreach (var process in Running())
        {
            // Cached on first read, so without this the handle never appears to change.
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return true;
            }
        }

        return false;
    }

    static Process[] Running() =>
        Process.GetProcessesByName("DiffEngineViewer");
}
