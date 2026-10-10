/// <summary>
/// The script that ends a Windows tool on behalf of a test process inside WSL, run here by the
/// PowerShell it is written for.
/// <para>
/// Inside WSL the script is all that reaches the host, so it is the part worth running for
/// real, and Windows is where it runs. A <c>cmd.exe</c> that names two files on its command
/// line and then waits stands in for a diff tool showing them.
/// </para>
/// </summary>
[NotInParallel]
[RunOn(TUnit.Core.Enums.OS.Windows)]
public class WslKillScriptTests
{
    [Test]
    public async Task TheProcessNamingBothFilesIsEnded()
    {
        var (temp, target) = Pair();
        using var tool = StandIn(temp, target);
        try
        {
            var ended = Run(WslInterop.KillScript("cmd.exe", temp, target));

            await Assert.That(ended).IsEqualTo(1);
            await Assert.That(tool.WaitForExit(5000)).IsTrue();
        }
        finally
        {
            End(tool);
        }
    }

    /// <summary>
    /// The image and the paths are matched as Windows and its file system match them.
    /// </summary>
    [Test]
    public async Task CaseIsIgnored()
    {
        var (temp, target) = Pair();
        using var tool = StandIn(temp, target);
        try
        {
            var ended = Run(WslInterop.KillScript("CMD.EXE", temp.ToUpperInvariant(), target.ToUpperInvariant()));

            await Assert.That(ended).IsEqualTo(1);
            await Assert.That(tool.WaitForExit(5000)).IsTrue();
        }
        finally
        {
            End(tool);
        }
    }

    /// <summary>
    /// A tool can be showing the same received file against another target, and another program
    /// altogether can have both files on its command line. Neither is the window for this pair.
    /// </summary>
    [Test]
    public async Task AProcessNamingOneFileOrOfAnotherImageIsLeft()
    {
        var (temp, target) = Pair();
        var (_, otherTarget) = Pair();
        using var tool = StandIn(temp, otherTarget);
        try
        {
            await Assert.That(Run(WslInterop.KillScript("cmd.exe", temp, target))).IsEqualTo(0);
            await Assert.That(Run(WslInterop.KillScript("notepad.exe", temp, otherTarget))).IsEqualTo(0);
            await Assert.That(tool.HasExited).IsFalse();
        }
        finally
        {
            End(tool);
        }
    }

    // A quote and a space, which are what a path is most likely to hold that a script minds
    static (string temp, string target) Pair()
    {
        var name = $@"C:\probe\it's a {Guid.NewGuid():N}";
        return ($"{name}.received.txt", $"{name}.verified.txt");
    }

    /// <summary>
    /// A <c>cmd.exe</c> with both paths on its command line, which then waits on an input that
    /// is never written to.
    /// </summary>
    static Process StandIn(string temp, string target) =>
        Process.Start(
            new ProcessStartInfo("cmd.exe", $"/k rem \"{temp}\" \"{target}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            })!;

    static void End(Process tool)
    {
        if (!tool.HasExited)
        {
            tool.Kill();
        }
    }

    static int Run(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = Process.Start(
            new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encoded}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return int.Parse(output.Trim());
    }
}
