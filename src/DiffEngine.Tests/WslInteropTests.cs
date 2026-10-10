public class WslInteropTests
{
    /// <summary>
    /// A Windows tool started from inside WSL is listed by <c>ps</c> as WSL's own program, given
    /// the tool's path twice and then its arguments. A running tool is found by the command it
    /// was started with, so what <c>ps</c> says is brought back to that. These are lines as an
    /// Ubuntu distribution listed them, less the process id.
    /// </summary>
    [Test]
    [Arguments(
        @"/init /mnt/c/Program Files/WinMerge/WinMergeU.exe /mnt/c/Program Files/WinMerge/WinMergeU.exe /u /wl /e \\wsl.localhost\Ubuntu\home\simon\a.received.txt \\wsl.localhost\Ubuntu\home\simon\a.verified.txt",
        @"/mnt/c/Program Files/WinMerge/WinMergeU.exe /u /wl /e \\wsl.localhost\Ubuntu\home\simon\a.received.txt \\wsl.localhost\Ubuntu\home\simon\a.verified.txt")]
    [Arguments(
        @"/init /mnt/c/Program Files/Beyond Compare 5/BCompare.exe /mnt/c/Program Files/Beyond Compare 5/BCompare.exe /solo /leftreadonly /nobackups C:\code\a b.received.txt C:\code\a b.verified.txt",
        @"/mnt/c/Program Files/Beyond Compare 5/BCompare.exe /solo /leftreadonly /nobackups C:\code\a b.received.txt C:\code\a b.verified.txt")]
    [Arguments(
        "/init /mnt/c/Windows/System32/PING.EXE /mnt/c/Windows/System32/PING.EXE -n 120 127.0.0.1",
        "/mnt/c/Windows/System32/PING.EXE -n 120 127.0.0.1")]
    [Arguments(
        "/init /mnt/c/Tools/tool.exe /mnt/c/Tools/tool.exe",
        "/mnt/c/Tools/tool.exe")]
    public async Task TheProxyIsTakenOffAWindowsToolsCommand(string listed, string command) =>
        await Assert.That(WslInterop.StripProxy(listed)).IsEqualTo(command);

    /// <summary>
    /// Everything else <c>ps</c> lists is left as it is: a native tool, WSL's own processes, and
    /// anything that only begins the same way.
    /// </summary>
    [Test]
    [Arguments("/usr/bin/meld /home/simon/a.received.txt /home/simon/a.verified.txt")]
    [Arguments("/init")]
    [Arguments("/init ")]
    [Arguments("/init --debug")]
    [Arguments("/init /mnt/c/Tools/tool.exe /mnt/c/Tools/other.exe left right")]
    [Arguments("/init /mnt/c/Tools/tool /mnt/c/Tools/tool.exe left")]
    [Arguments("/initial /a /a")]
    public async Task AnythingElseIsLeftAlone(string listed) =>
        await Assert.That(WslInterop.StripProxy(listed)).IsEqualTo(listed);

    /// <summary>
    /// A value is never text in the script that ends a tool on the host, so a quote in a path
    /// cannot end a string early. It is not only the straight quote that PowerShell takes for
    /// one. <see cref="WslKillScriptTests" /> runs the script.
    /// </summary>
    [Test]
    public async Task NoValueIsWrittenIntoTheKillScriptAsText()
    {
        var script = WslInterop.KillScript("to'ol.exe", @"C:\it's\a’b.received.txt", @"C:\it's\a’b.verified.txt");

        await Assert.That(script).DoesNotContain("to'ol");
        await Assert.That(script).DoesNotContain("it's");
        await Assert.That(script).DoesNotContain("’");
    }

    /// <summary>
    /// The viewer is spoken to over a loopback port the host does not share with a
    /// distribution, and the two editors run in a terminal a test process does not have. Every
    /// other tool is offered, and so is one of the caller's own, which has no
    /// <see cref="DiffTool" />.
    /// </summary>
    [Test]
    public async Task EveryToolButTheViewerAndTheTerminalEditorsIsOffered()
    {
        var withheld = Definitions.Tools
            .Select(_ => _.Tool)
            .Where(_ => !WslInterop.Offers(_))
            .ToList();

        await Assert.That(withheld).IsEquivalentTo([DiffTool.DiffEngineViewer, DiffTool.Vim, DiffTool.Neovim]);
        await Assert.That(WslInterop.Offers(null)).IsTrue();
    }

    /// <summary>
    /// A definition whose Windows executable is a script cannot be started from a distribution,
    /// so it has to be reachable some other way. Which ones are is written down here, so adding
    /// another is a decision rather than a tool that quietly never resolves inside WSL.
    /// </summary>
    [Test]
    public async Task TheToolsDeclaredAsScriptsAreTheOnesKnownAbout()
    {
        var scripts = Definitions.Tools
            .Where(_ => _.OsSupport.Windows is { } windows &&
                        !windows.ExeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Select(_ => _.Tool)
            .ToList();

        // Found by the launcher its Linux definition names, which WSL puts on the PATH
        await Assert.That(scripts).IsEquivalentTo([DiffTool.VisualStudioCode]);
    }
}
