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
