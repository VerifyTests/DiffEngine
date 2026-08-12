extern alias engine;

using System.ComponentModel;
using EngineRunner = engine::DiffEngine.DiffRunner;

/// <summary>
/// Points DiffEngine at the head built from this repo, and waits for the person driving it.
/// <para>
/// <see cref="Close"/> ends the process afterwards, because a dismissed window is not an exited
/// one: with DiffEngineTray running the viewer hides so the tray can reopen it. A survivor would
/// answer the next run — the viewer is single instance — and the person driving would review the
/// build before last with nothing saying so.
/// </para>
/// <para>
/// If a run is killed before it can clean up, or a viewer was started by hand, end it before
/// launching another. Only the head from this working tree counts; a viewer open on other work is
/// left alone.
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
    /// <para>
    /// Any viewer still running is ended first. The head itself is always current: DiffEngine
    /// references all three heads to order the build, and this project references DiffEngine, so
    /// the command that runs one of these tests rebuilds the head on the way. A leftover process
    /// is the only way to end up reviewing an older build, and it is not one the binary can be
    /// checked for.
    /// </para>
    /// </summary>
    public static void Enable()
    {
        var executable = Executable();
        if (!File.Exists(executable))
        {
            throw new($"Build the viewer head first. Not found: {executable}");
        }

        // A leftover from a run that was killed before it could clean up, or one started by hand.
        // Nothing else runs this executable, so whatever is up is spent, and being single instance
        // it would answer this run rather than let it start its own — showing its own window, from
        // whenever it was built.
        Close();

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

        var dismissed = await Until(() => !Showing(), DateTime.UtcNow + patience);
        Close();
        if (!dismissed)
        {
            throw new($"The viewer was not dismissed within {patience.TotalMinutes} minutes.");
        }
    }

    /// <summary>
    /// Ends the process this run started.
    /// <para>
    /// A dismissed window is not an exited process: with DiffEngineTray running the viewer hides so
    /// the tray can reopen it, which is right for a user and wrong for a test. The viewer is single
    /// instance, so a survivor answers the next run — it takes that run's patches, shows its own
    /// already running window, and the person driving reviews the build before last without
    /// anything saying so.
    /// </para>
    /// </summary>
    public static void Close()
    {
        foreach (var process in Running())
        {
            try
            {
                process.Kill();
                process.WaitForExit(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
                when (exception is Win32Exception or InvalidOperationException)
            {
                // Already gone, which is the goal state.
            }
            finally
            {
                process.Dispose();
            }
        }
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

    /// <summary>
    /// Only the head built from this working tree, matched by the executable it is running. A
    /// viewer the user has open on their own work is then neither mistaken for this test's window
    /// nor killed when the test finishes.
    /// </summary>
    static List<Process> Running()
    {
        var executable = Executable();
        var ours = new List<Process>();
        foreach (var process in Process.GetProcessesByName("DiffEngineViewer"))
        {
            if (IsOurs(process, executable))
            {
                ours.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }

        return ours;
    }

    static bool IsOurs(Process process, string executable)
    {
        try
        {
            return string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
            when (exception is Win32Exception or InvalidOperationException)
        {
            // Exited between listing and asking, or running as something this process cannot open.
            // Either way it is not one this test launched.
            return false;
        }
    }
}
