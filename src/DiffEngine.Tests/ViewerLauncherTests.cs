/// <summary>
/// A viewer started with nowhere to draw bound the port, failed to open its window and exited, and
/// the bind read to its launcher as a viewer that had taken the snapshot. On Linux with no display
/// none is started, so the caller hears that no viewer was found and keeps what it sent.
/// </summary>
public class ViewerLauncherTests
{
    [Test]
    public async Task LinuxWithNoDisplayStartsNoViewer() =>
        await Assert.That(ViewerLauncher.HasDisplay(linux: true, Variables())).IsFalse();

    [Test]
    public async Task LinuxWithAnXDisplayStartsOne() =>
        await Assert.That(ViewerLauncher.HasDisplay(linux: true, Variables(("DISPLAY", ":0")))).IsTrue();

    [Test]
    public async Task LinuxWithAWaylandDisplayStartsOne() =>
        await Assert.That(ViewerLauncher.HasDisplay(linux: true, Variables(("WAYLAND_DISPLAY", "wayland-0")))).IsTrue();

    /// <summary>
    /// Set but empty is how a shell unsets a variable it cannot remove, and names no display.
    /// </summary>
    [Test]
    public async Task AnEmptyDisplayIsNone() =>
        await Assert.That(ViewerLauncher.HasDisplay(linux: true, Variables(("DISPLAY", "")))).IsFalse();

    [Test]
    public async Task OtherPlatformsAreTakenToHaveADesktop() =>
        await Assert.That(ViewerLauncher.HasDisplay(linux: false, Variables())).IsTrue();

    /// <summary>
    /// With no tray, <c>dotnet test</c> did not return until the viewer it launched was closed: the
    /// viewer inherited the test host's handles, the pipe dotnet test reads the host's output from
    /// among them, and dotnet test reads it until every writer has closed it. On Windows a process
    /// started without ShellExecute inherits every inheritable handle whatever is redirected, so
    /// ShellExecute, which inherits none, is the whole of the fix there.
    /// </summary>
    [Test]
    public async Task OnWindowsTheViewerInheritsNothing()
    {
        var info = ViewerLauncher.StartInfo("DiffEngineViewer.exe", "--attach", windows: true);

        await Assert.That(info.UseShellExecute).IsTrue();
        await Assert.That(info.RedirectStandardInput).IsFalse();
    }

    /// <summary>
    /// Elsewhere .NET opens every descriptor close-on-exec, so the three standard streams are all
    /// a child can inherit, and all three are given pipes of their own.
    /// </summary>
    [Test]
    public async Task ElsewhereTheViewerHasStandardStreamsOfItsOwn()
    {
        var info = ViewerLauncher.StartInfo("DiffEngineViewer", "--attach", windows: false);

        await Assert.That(info.UseShellExecute).IsFalse();
        await Assert.That(info.RedirectStandardInput).IsTrue();
        await Assert.That(info.RedirectStandardOutput).IsTrue();
        await Assert.That(info.RedirectStandardError).IsTrue();
    }

    /// <summary>
    /// A viewer took its working directory from the test host, which is usually the test project's
    /// output folder, and held it for as long as it lived. It is started in its own folder, which
    /// it holds by running from it whatever its working directory is.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task TheViewerStartsInItsOwnFolder(bool windows)
    {
        var directory = Path.Combine(Path.GetTempPath(), "viewer folder");

        var info = ViewerLauncher.StartInfo(Path.Combine(directory, "DiffEngineViewer.exe"), "--attach", windows);

        await Assert.That(info.WorkingDirectory).IsEqualTo(directory);
    }

    /// <summary>
    /// The same thing as Windows sees it, which is where it mattered: the directory the host was in
    /// when it started a viewer could not be deleted until that viewer exited, so
    /// <c>git clean -xdf</c> failed behind a viewer nobody could see.
    /// <para>
    /// The one test that moves this process's current directory, so it runs alone and puts it back
    /// before anything else is asked.
    /// </para>
    /// </summary>
    [Test]
    [NotInParallel]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task ARunningViewerDoesNotHoldTheHostsDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DiffEngine.HostDirectory.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var previous = Environment.CurrentDirectory;
        Process? viewer;
        Environment.CurrentDirectory = directory;
        try
        {
            // FakeDiffTool stands in for the viewer: no window, and gone by itself in five seconds
            viewer = ViewerLauncher.Start(FakeDiffTool.Exe, "");
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }

        try
        {
            await Assert.That(viewer).IsNotNull();
            // Still running, or the delete below says nothing about what a running one holds
            await Assert.That(viewer!.HasExited).IsFalse();
            await Assert.That(() => Directory.Delete(directory)).ThrowsNothing();
        }
        finally
        {
            if (viewer is not null)
            {
                using (viewer)
                {
                    try
                    {
                        viewer.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // Already gone, which is all the kill was for
                    }

                    viewer.WaitForExit(5000);
                }
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    /// <summary>
    /// A relative path meant relative to the host's directory, which is no longer where the viewer
    /// starts.
    /// </summary>
    [Test]
    public async Task ARelativePathIsHandedOverRooted()
    {
        var rooted = ViewerLauncher.Rooted("Sample.received.txt");

        // Not compared with a path built from the current directory here, which the test above
        // moves for a moment
        await Assert.That(Path.IsPathRooted(rooted)).IsTrue();
        await Assert.That(Path.GetFileName(rooted)).IsEqualTo("Sample.received.txt");
    }

    /// <summary>
    /// And one that is already rooted goes over as the caller spelt it, tidied or not: the row is
    /// settled later by a key built from that spelling.
    /// </summary>
    [Test]
    public async Task ARootedPathIsHandedOverAsGiven()
    {
        var path = Path.Combine(Path.GetTempPath(), "one", "..", "Sample.received.txt");

        await Assert.That(ViewerLauncher.Rooted(path)).IsEqualTo(path);
    }

    /// <summary>
    /// The patch goes in a file named on the command line, because a launch that redirects stdin
    /// cannot use ShellExecute.
    /// </summary>
    [Test]
    public async Task AnInlineLaunchNamesItsPayloadFile()
    {
        var patch = new InlinePatch(Path.Combine("dir with space", "Tests.cs"), 42, "\"old\"", "new")
        {
            TestName = "Tests.Method"
        };

        var arguments = ViewerLauncher.PayloadArguments(patch, Path.Combine("temp dir", "patch.inlinepatch"));

        await Assert.That(arguments).IsEqualTo(
            $"--inline --source \"{Path.Combine("dir with space", "Tests.cs")}\" --line 42 --payload \"{Path.Combine("temp dir", "patch.inlinepatch")}\"");
    }

    static Func<string, string?> Variables(params (string Name, string Value)[] set)
    {
        var variables = set.ToDictionary(_ => _.Name, _ => _.Value);

        string? Read(string name)
        {
            if (variables.TryGetValue(name, out var value))
            {
                return value;
            }

            return null;
        }

        return Read;
    }
}
