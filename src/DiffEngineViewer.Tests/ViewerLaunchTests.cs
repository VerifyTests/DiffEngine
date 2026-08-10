extern alias engine;

using EnginePatch = engine::DiffEngine.InlinePatch;
using EnginePatchMode = engine::DiffEngine.InlinePatchMode;
using EngineResult = engine::DiffEngine.InlineResult;
using EngineRunner = engine::DiffEngine.DiffRunner;
using EngineTool = engine::DiffEngine.DiffTool;
using EngineLaunch = engine::DiffEngine.LaunchResult;

/// <summary>
/// Launches the real viewer through the real DiffEngine entry points, for a person to confirm.
/// <para>
/// This is the only coverage the launch path has. Everything else stops at the socket:
/// EngineInlineTests drives a stand in server, and the screen tests build a Screen and never start
/// a process. Between them they never answer whether an executable can be found, whether the
/// payload survives the handoff, or whether the right thing appears.
/// </para>
/// <para>
/// Each case prints what to look for. Accepting rewrites a file in a temp directory, and the test
/// prints it afterwards, so the outcome is visible rather than taken on trust.
/// </para>
/// <para>
/// Explicit, so an ordinary run never opens a window. Run one at a time, because the viewer is
/// single instance:
/// <code>
/// dotnet test --project src/DiffEngineViewer.Tests -- --treenode-filter "/*/*/ViewerLaunchTests/InlineQueueFromSeparateLaunches"
/// </code>
/// </para>
/// </summary>
[NotInParallel]
public class ViewerLaunchTests
{
    /// <summary>
    /// Here rather than in the module initializer, so only these tests can produce a window.
    /// </summary>
    [Before(Class)]
    public static void Enable() =>
        ManualViewer.Enable();

    const string received =
        """
        the quick
        brown dog
        jumps over
        """;

    const string expected =
        """
        the quick
        brown fox
        jumps over
        """;

    [Test]
    [Explicit]
    public async Task FileDiff()
    {
        var directory = ManualViewer.TempDirectory();
        var temp = Write(directory, "Sample.received.txt", received);
        var target = Write(directory, "Sample.verified.txt", expected);

        ManualViewer.Expect(
            "Two file diff",
            "Two panes, headers Sample.received.txt and Sample.verified.txt",
            "Line 2 highlighted on both sides, dog on the left and fox on the right",
            "No pending queue column",
            "Buttons are Accept and Close");

        var result = await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, temp, target);

        await Assert.That(result).IsEqualTo(EngineLaunch.StartedNewInstance);
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// The shape a brand new snapshot takes: nothing to compare against yet.
    /// </summary>
    [Test]
    [Explicit]
    public async Task FileDiffWithNoTarget()
    {
        var directory = ManualViewer.TempDirectory();
        var temp = Write(directory, "Sample.received.txt", received);
        var target = Path.Combine(directory.FullName, "Sample.verified.txt");

        ManualViewer.Expect(
            "Two file diff, target does not exist",
            "Left pane has all three lines, marked as added",
            "Right pane is empty",
            "No crash from the missing file");

        var result = await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, temp, target);

        await Assert.That(result).IsEqualTo(EngineLaunch.StartedNewInstance);
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// Forty lines with changes at 3, 17 and 33, so scrolling and next/previous change have
    /// something to land on both inside and outside the first viewport.
    /// </summary>
    [Test]
    [Explicit]
    public async Task FileDiffLongEnoughToScroll()
    {
        var directory = ManualViewer.TempDirectory();
        var temp = Write(directory, "Long.received.txt", Long(changed: true));
        var target = Write(directory, "Long.verified.txt", Long(changed: false));

        ManualViewer.Expect(
            "Scrolling and change navigation",
            "Arrow keys, PgUp, PgDn, Home and End all scroll",
            "The status line tracks the visible range, ending in of 40",
            "n jumps forward through changes at lines 3, 17 and 33, p jumps back",
            "The mouse wheel scrolls");

        await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, temp, target);
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// The common inline case: a literal exists and the snapshot changed.
    /// </summary>
    [Test]
    [Explicit]
    public async Task InlineReplacesALiteral()
    {
        var directory = ManualViewer.TempDirectory();
        var source = WriteSource(directory, "SampleTests.cs", "\"old value\"");

        ManualViewer.Expect(
            "Inline, existing literal",
            "Title is SampleTests.cs:6, subtitle says inline",
            "Left pane shows new value, right pane shows old value",
            "Accept rewrites the literal in the source file");

        var result = await EngineRunner.AddInlineAsync(
            new(source, 6, "\"old value\"", "new value"));

        await Assert.That(result).IsEqualTo(EngineResult.Queued);
        await ManualViewer.WaitForClose();
        Report(source);
    }

    /// <summary>
    /// The other half of the inline story: no literal yet, so accepting appends the call rather
    /// than replacing an argument.
    /// </summary>
    [Test]
    [Explicit]
    public async Task InlineAppendsToACallWithNoSnapshot()
    {
        var directory = ManualViewer.TempDirectory();
        var source = WriteSource(directory, "NewTests.cs", null);

        ManualViewer.Expect(
            "Inline, new snapshot",
            "Right pane header says expected (new snapshot) and the pane is empty",
            "Left pane rows are green and marked +",
            "Accept adds a .Snapshot(...) call after the verify call");

        var result = await EngineRunner.AddInlineAsync(
            new(source, 6, null, "brand new", EnginePatchMode.Append));

        await Assert.That(result).IsEqualTo(EngineResult.Queued);
        await ManualViewer.WaitForClose();
        Report(source);
    }

    /// <summary>
    /// Three launches, one window. The first binds the port and the rest hand their patch over and
    /// exit, which is the behaviour a failing test run depends on.
    /// </summary>
    [Test]
    [Explicit]
    public async Task InlineQueueFromSeparateLaunches()
    {
        var directory = ManualViewer.TempDirectory();
        var first = WriteSource(directory, "FirstTests.cs", "\"old value\"");
        var second = WriteSource(directory, "SecondTests.cs", "\"other value\"");
        var third = WriteSource(directory, "ThirdTests.cs", null);

        ManualViewer.Expect(
            "Three pending snapshots in one window",
            "Only one window opens, not three",
            "Pending (3) column lists all three files, the first selected",
            "Clicking an entry switches panes, Tab and Shift+Tab move between them",
            "Accept all is enabled and accepts every one",
            "The window closes itself once the queue empties");

        foreach (var (source, expression, content) in new[]
                 {
                     (first, "\"old value\"", "first new"),
                     (second, "\"other value\"", "second new"),
                     (third, null, "third new")
                 })
        {
            var mode = expression is null ? EnginePatchMode.Append : EnginePatchMode.Set;
            var result = await EngineRunner.AddInlineAsync(new(source, 6, expression, content, mode));
            await Assert.That(result).IsEqualTo(EngineResult.Queued);
        }

        await ManualViewer.WaitForClose();
        Report(first, second, third);
    }

    /// <summary>
    /// A snapshot big enough that the panes scroll, which the inline path reaches differently from
    /// file mode: the content comes over stdin rather than off disk.
    /// </summary>
    [Test]
    [Explicit]
    public async Task InlineLongEnoughToScroll()
    {
        var directory = ManualViewer.TempDirectory();
        var source = WriteSource(directory, "LongTests.cs", "\"old value\"");

        ManualViewer.Expect(
            "Inline with a long snapshot",
            "Forty rows on the left, scrollable",
            "The status line ends in of 40",
            "Accept writes the whole thing as a raw string literal");

        var result = await EngineRunner.AddInlineAsync(
            new(source, 6, "\"old value\"", Long(changed: true)));

        await Assert.That(result).IsEqualTo(EngineResult.Queued);
        await ManualViewer.WaitForClose();
        Report(source);
    }

    /// <summary>
    /// Discard has to leave the file exactly as it was, which is easy to get wrong and invisible
    /// unless someone looks.
    /// </summary>
    [Test]
    [Explicit]
    public async Task InlineDiscardLeavesTheSourceAlone()
    {
        var directory = ManualViewer.TempDirectory();
        var source = WriteSource(directory, "DiscardTests.cs", "\"old value\"");
        var before = await File.ReadAllTextAsync(source);

        ManualViewer.Expect(
            "Discard",
            "Press Discard, or d",
            "The window closes because the queue is empty");

        await EngineRunner.AddInlineAsync(new(source, 6, "\"old value\"", "new value"));
        await ManualViewer.WaitForClose();

        await Assert.That(await File.ReadAllTextAsync(source)).IsEqualTo(before);
        Console.WriteLine("Source unchanged, as it should be after a discard.");
    }

    /// <summary>
    /// Line 6 is the verify call in both shapes, which is what every patch above points at.
    /// </summary>
    static string WriteSource(DirectoryInfo directory, string name, string? literal)
    {
        // Written with explicit indentation rather than a raw string, because the indentation is
        // the thing being tested: the patcher infers where to put a rewritten literal from it.
        var call = literal is null
            ? "        Verify(Build());"
            : $"        Verify(Build())\n            .Snapshot({literal});";

        return Write(
            directory,
            name,
            $$"""
              public class Sample
              {
                  [Test]
                  public Task Case()
                  {
              {{call}}
                  }

                  static string Build() =>
                      "content";
              }
              """);
    }

    static string Write(DirectoryInfo directory, string name, string content)
    {
        var path = Path.Combine(directory.FullName, name);
        File.WriteAllText(path, content);
        return path;
    }

    static string Long(bool changed)
    {
        var builder = new StringBuilder();
        for (var index = 1; index <= 40; index++)
        {
            if (index > 1)
            {
                builder.Append('\n');
            }

            builder.Append($"line {index:D2}");
            if (changed &&
                index is 3 or 17 or 33)
            {
                builder.Append(" changed");
            }
        }

        return builder.ToString();
    }

    static void Report(params string[] sources)
    {
        foreach (var source in sources)
        {
            Console.WriteLine();
            Console.WriteLine($"--- {Path.GetFileName(source)} after ---");
            Console.WriteLine(File.ReadAllText(source));
        }
    }
}
