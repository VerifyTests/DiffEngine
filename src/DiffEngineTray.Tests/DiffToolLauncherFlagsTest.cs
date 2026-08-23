/// <summary>
/// The start flags the tray relaunches a tracked move with, which have to be the tool's own rather
/// than a fixed pair.
/// <para>
/// They were fixed at ShellExecute with no CreateNoWindow, so a console subsystem tool - the
/// bundled viewer is one - came up from "Open diff tool" with a console attached, while
/// DiffEngine's own launch of the very same tool did not. The window that console puts on screen
/// is zero sized and never activates, so nobody saw it; what it left behind was a conhost process
/// per relaunch and two launch paths that disagreed.
/// </para>
/// </summary>
public class DiffToolLauncherFlagsTest :
    IDisposable
{
    [Test]
    public async Task AToolsOwnFlagsAreUsed()
    {
        var registered = DiffTools.AddTool(
            name: "FakeConsoleTool",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: false,
            useShellExecute: false,
            launchArguments: new(
                Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                Right: (temp, target) => $"\"{temp}\" \"{target}\""),
            exePath: exe,
            binaryExtensions: [],
            createNoWindow: true);

        await Assert.That(registered).IsNotNull();
        await Assert.That(DiffToolLauncher.FlagsFor(exe)).IsEqualTo((false, true));
    }

    /// <summary>
    /// The path a move carries is the sending process's. For the bundled viewer that is inside
    /// that project's package folder, which this process has never looked in, so the exact path
    /// misses and the executable's name is what is left to go on.
    /// </summary>
    [Test]
    public async Task TheSameToolAtAnotherPathIsStillThatTool()
    {
        DiffTools.AddTool(
            name: "FakeConsoleTool",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: false,
            useShellExecute: false,
            launchArguments: new(
                Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                Right: (temp, target) => $"\"{temp}\" \"{target}\""),
            exePath: exe,
            binaryExtensions: [],
            createNoWindow: true);

        var elsewhere = Path.Combine(@"c:\somewhere\else", Path.GetFileName(exe));

        await Assert.That(DiffToolLauncher.FlagsFor(elsewhere)).IsEqualTo((false, true));
    }

    /// <summary>
    /// Nothing resolves for a tool that is no longer installed, or an exe a payload named that
    /// never was one. That keeps what this has always done, and ShellExecute is the safe end of
    /// it: without it the launched tool inherits the launching process's handles.
    /// </summary>
    [Test]
    public async Task AnUnknownExeKeepsTheOldPair() =>
        await Assert.That(DiffToolLauncher.FlagsFor(@"c:\nothing\here.exe")).IsEqualTo((true, false));

    readonly string exe = Environment.ProcessPath!;

    public void Dispose() =>
        // Registered into the static lookup, so the rest of the run has to get it back.
        DiffTools.Reset();
}
