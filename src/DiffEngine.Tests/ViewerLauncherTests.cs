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
