#if DEBUG
/// <summary>
/// The window <see cref="DebugReport"/> is shown in. Built over a fixed report rather than a
/// Tracker, so what is rendered does not depend on the machine it renders on.
/// </summary>
public class DebugFormTests
{
    [Test]
    public async Task Default()
    {
        using var form = new DebugForm(() => report);
        await Verify(form);
    }

    // ReplaceLineEndings because a multiline TextBox renders a lone \n as no break at all, and
    // this file is checked out with LF. The real report gets CRLF from AppendLine.
    static string report =
        """
        DiffEngineTray 20.0.0
        Captured: 2024-10-01 13:45:30
        Inline queue: owned by this tray on port 3493
        Tracking: True

        Deletes (1)
        -----------
        [1] Extra.verified.txt
            File:               C:\repo\tests\Extra.verified.txt (missing)
            Group:              DiffEngine

        Moves (1)
        ---------
        [1] Sample.Test
            Temp:               C:\repo\tests\Sample.Test.received.txt (exists)
            Target:             C:\repo\tests\Sample.Test.verified.txt (exists)
            Extension:          txt
            Group:              DiffEngine
            Exe:                C:\tools\diff.exe
            Arguments:          "C:\repo\tests\Sample.Test.received.txt" "C:\repo\tests\Sample.Test.verified.txt"
            CanKill:            True
            KillLockingProcess: False
            Process:            4312 (running)

        Snapshots (1)
        -------------
        [1] Sample.cs:12
            Key:                c:\repo\tests\sample.cs|12
            Source:             c:\repo\tests\sample.cs (exists)
            Group:              DiffEngine
            Status:             <null>
            SourceFile:         C:\repo\tests\Sample.cs
            LineHint:           12
            Mode:               Set
            OriginalExpression: "old text"
            NewContent:
                line one
                line two
        """.ReplaceLineEndings();
}
#endif
