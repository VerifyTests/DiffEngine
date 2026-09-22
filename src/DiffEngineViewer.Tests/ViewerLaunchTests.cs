extern alias engine;
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
/// single instance, and rebuild first — a lingering instance would otherwise answer instead, which
/// <see cref="ManualViewer"/> explains:
/// <code>
/// Get-Process DiffEngineViewer -ErrorAction SilentlyContinue | Stop-Process -Force
/// dotnet build src
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

    // Unnamed, so the queue labels each of these by its call site — which is what the expectations
    // below tell the reader to look for. The cases that are about naming set one themselves.
    static engine::DiffEngine.InlinePatch Patch(
        string source,
        int line,
        string? expression,
        string content,
        EnginePatchMode mode = EnginePatchMode.Set,
        string? framework = null) =>
        new(source, line, expression, content, mode)
        {
            TestName = null,
            Framework = framework
        };

    /// <summary>
    /// The belt to WaitForClose's braces: a case that throws before it gets there would otherwise
    /// leave a hidden viewer to answer the next run.
    /// </summary>
    [After(Class)]
    public static void Cleanup() =>
        ManualViewer.Close();

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
            Patch(source, 6, "\"old value\"", "new value"));

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
            Patch(source, 6, null, "brand new", EnginePatchMode.Append));

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
            var result = await EngineRunner.AddInlineAsync(Patch(source, 6, expression, content, mode));
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
            "A scrollbar on the right, draggable, that follows the keys and the wheel",
            "Dragging it to the bottom lands on the last page and stays there, no spring back",
            "Accept writes the whole thing as a raw string literal");

        var result = await EngineRunner.AddInlineAsync(
            Patch(source, 6, "\"old value\"", Long(changed: true)));

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

        await EngineRunner.AddInlineAsync(Patch(source, 6, "\"old value\"", "new value"));
        await ManualViewer.WaitForClose();

        await Assert.That(await File.ReadAllTextAsync(source)).IsEqualTo(before);
        Console.WriteLine("Source unchanged, as it should be after a discard.");
    }

    /// <summary>
    /// Everything the queue column gained in one window: solution headers, a test sub-group, a
    /// conflicted entry with its variant button, and a per-kind accept label is out of reach here
    /// because moves and deletes need a tray owner.
    /// <para>
    /// On its own port, deliberately: a machine running the real DiffEngineTray would otherwise
    /// receive these patches into its live queue and open its installed viewer, which is not the
    /// code in this working tree.
    /// </para>
    /// </summary>
    [Test]
    [Explicit]
    public async Task GroupedQueue()
    {
        Environment.SetEnvironmentVariable("DiffEngine_ViewerPort", "3499");
        try
        {
            var root = ManualViewer.TempDirectory();
            var solutionA = root.CreateSubdirectory("SolutionA");
            await File.WriteAllTextAsync(Path.Combine(solutionA.FullName, "SolutionA.slnx"), "");
            var solutionB = root.CreateSubdirectory("SolutionB");
            await File.WriteAllTextAsync(Path.Combine(solutionB.FullName, "SolutionB.slnx"), "");

            var grouped = WriteSource(solutionA, "ATests.cs", "\"old value\"");
            var single = WriteSource(solutionA, "OtherTests.cs", "\"other value\"");
            var conflicted = WriteSource(solutionB, "BTests.cs", "\"framework value\"");

            ManualViewer.Expect(
                "Grouped queue with a conflict",
                "SolutionA (3) and SolutionB (1) headers, dimmed, with the entries indented",
                "Compare handles nulls (2) sub-header over the two ATests.cs call sites",
                "Order is stable labels the OtherTests.cs entry by test name",
                "The BTests.cs entry is marked *, its pane header says received (net8.0)",
                "The Variant 1/2: net8.0 button (or v) flips to net9.0 and back",
                "Right-clicking BTests.cs selects it and offers Accept, Show next variant, Discard, Open source file",
                "Right-clicking the SolutionA header offers Accept all in SolutionA and Discard all in SolutionA, sweeping only that solution",
                "On Linux the viewer draws the menu, and any other click or key closes it",
                "On Windows and macOS it is the real OS one: arrows and Enter drive it, Escape closes it WITHOUT closing the window, and it flips rather than clips near the edge of a screen",
                "On macOS there is a menu bar, Quit hides rather than kills when the tray is running, and Close and Minimize work",
                "Clicking the SolutionA header folds it, its marker flips - to +, and its count stays",
                "The selection moves to a visible entry rather than disappearing under the fold",
                "Tab steps over the folded entries, and Accept all still takes them",
                "Hovering an entry shows its full path, and the frameworks when marked *",
                "Hovering a header, or a row whose label already says everything, shows no tooltip",
                "Accepting the conflicted entry writes the variant on screen",
                "Accept all skips the conflict and says 1 conflict needs review");

            foreach (var patch in new[]
                     {
                         new(grouped, 6, "\"old value\"", "first new")
                         {
                             TestName = "Compare handles nulls"
                         },
                         new(grouped, 7, "\"old value\"", "second new")
                         {
                             TestName = "Compare handles nulls"
                         },
                         new(single, 6, "\"other value\"", "other new")
                         {
                             TestName = "Order is stable"
                         },
                         Patch(conflicted, 6, "\"framework value\"", "eight", framework: "net8.0"),
                         Patch(conflicted, 6, "\"framework value\"", "nine", framework: "net9.0")
                     })
            {
                var result = await EngineRunner.AddInlineAsync(patch);
                await Assert.That(result).IsEqualTo(EngineResult.Queued);
            }

            await ManualViewer.WaitForClose();
            Report(grouped, single, conflicted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_ViewerPort", null);
        }
    }

    /// <summary>
    /// Enough pending snapshots that accepting them takes a while, for watching an accept-all go:
    /// the status line counting up, entries leaving as they land, and the window answering the whole
    /// time. Two solutions of ten classes, twenty five snapshots a class, which is the shape a run
    /// that changed something widely used leaves behind. Every accept rewrites a file holding two
    /// dozen others, and the multi line literal it writes moves every call site below it, so the
    /// rest of the class is found by what it says rather than by the line it was reported on.
    /// <para>
    /// A fast disk accepts all of that in a second or two, which is not long enough to watch or to
    /// try anything while it runs. So the class halfway down the batch is held the way another
    /// process part way through writing it would hold it - an IDE accepting its own snapshot - and
    /// the batch stops there for a few seconds. That is also the case the window used to freeze in.
    /// </para>
    /// <para>
    /// Three conflicts ride along. Accept-all leaves those for review, so the window is still open
    /// once the batch has finished, and what it says about the batch can be read.
    /// </para>
    /// <para>
    /// On its own port, for the reason <see cref="GroupedQueue"/> gives.
    /// </para>
    /// </summary>
    [Test]
    [Explicit]
    public async Task AcceptAllOverALongQueue()
    {
        const int classes = 10;
        const int snapshots = 25;
        const int conflicts = 3;
        Environment.SetEnvironmentVariable("DiffEngine_ViewerPort", "3499");
        try
        {
            var root = ManualViewer.TempDirectory();
            var solutionA = root.CreateSubdirectory("SolutionA");
            await File.WriteAllTextAsync(Path.Combine(solutionA.FullName, "SolutionA.slnx"), "");
            var solutionB = root.CreateSubdirectory("SolutionB");
            await File.WriteAllTextAsync(Path.Combine(solutionB.FullName, "SolutionB.slnx"), "");

            var sources = new List<string>();
            var patches = new List<engine::DiffEngine.InlinePatch>();
            foreach (var solution in new[] { solutionA, solutionB })
            {
                for (var index = 1; index <= classes; index++)
                {
                    var (source, lines) = WriteClass(solution, $"{solution.Name[^1]}{index:D2}Tests.cs", snapshots);
                    sources.Add(source);
                    for (var method = 1; method <= snapshots; method++)
                    {
                        patches.Add(
                            new(source, lines[method - 1], $"\"old {method}\"", Snapshot(method))
                            {
                                TestName = null,
                                MemberName = $"Case{method}"
                            });
                    }
                }
            }

            var (conflicted, conflictedLines) = WriteClass(solutionB, "ConflictedTests.cs", conflicts);
            for (var method = 1; method <= conflicts; method++)
            {
                var line = conflictedLines[method - 1];
                patches.Add(Patch(conflicted, line, $"\"old {method}\"", $"eight {method}", framework: "net8.0"));
                patches.Add(Patch(conflicted, line, $"\"old {method}\"", $"nine {method}", framework: "net9.0"));
            }

            var total = sources.Count * snapshots;
            ManualViewer.Expect(
                "Accept all over a long queue",
                $"Pending ({total + conflicts}), grouped under SolutionA and SolutionB headers - the list follows the last arrival to the bottom, so scroll it up to see them",
                "Press Accept all, or Shift+A",
                $"The status line counts up - Accepting 1 of {total}, 2 of {total} - and entries leave the list as they land",
                "Accept, Discard and Accept all are disabled until it finishes, and a, d and Shift+A do nothing",
                "About halfway the count stops for six seconds, on a class this test is holding the way another process writing it would",
                "The window keeps answering the whole time, stop included: scroll, Tab through the entries, click one, fold a header",
                "The count carries on once the class is let go",
                $"When it finishes the {conflicts} ConflictedTests.cs entries are left, and the status line says Accepted {total}, {conflicts} conflicts need review");

            foreach (var patch in patches)
            {
                var result = await EngineRunner.AddInlineAsync(patch);
                await Assert.That(result).IsEqualTo(EngineResult.Queued);
            }

            // The batch goes in the order the queue lists, so the class listed halfway down is the
            // one it reaches halfway through
            if (!ViewerClient.TrySend(new(ViewerVerb.List), out var listing, 3499))
            {
                throw new("The viewer did not list its queue.");
            }

            var halfway = listing.Items[listing.Items.Count / 2].Name.Split(':')[0];
            using var cancel = new CancelSource();
            var holding = HoldOnceReached(sources.Single(_ => Path.GetFileName(_) == halfway), 3499, cancel.Token);

            await ManualViewer.WaitForClose();
            await cancel.CancelAsync();
            await holding;

            var left = sources.Sum(_ => Unaccepted(_));
            Console.WriteLine();
            Console.WriteLine($"{total - left} of {total} call sites rewritten, and {Unaccepted(conflicted)} of {conflicts} conflicted ones left alone.");
            // One class in full, for what the accepts wrote. The other nineteen are the same shape.
            Report(sources[0]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_ViewerPort", null);
        }
    }

    /// <summary>
    /// Holds <paramref name="source"/> the way another process applying a patch to it would, by
    /// taking the cross process mutex InlineApplier waits on, so an accept-all stops when it gets
    /// there.
    /// <para>
    /// Taken only once a batch has started, so someone who accepts one at a time instead is never
    /// held up by it. Let go six seconds after the count stops moving, well inside the ten seconds
    /// InlineApplier waits before giving up, so the held entry still lands.
    /// </para>
    /// <para>
    /// A thread of its own, because a mutex has to be released by the thread that took it.
    /// </para>
    /// </summary>
    static Task HoldOnceReached(string source, int port, Cancel cancel) =>
        Task.Factory.StartNew(
            () =>
            {
                AcceptProgress? Progress() =>
                    ViewerClient.TrySend(new(ViewerVerb.List), out var response, port) ? response.Progress : null;

                while (Progress() is null)
                {
                    if (cancel.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50)))
                    {
                        return;
                    }
                }

                using var mutex = new Mutex(false, PatchMutex(source));
                if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    return;
                }

                try
                {
                    // Stopped, rather than between two entries, once the count has stood still for
                    // longer than an apply takes
                    var previous = Progress();
                    var since = DateTime.UtcNow;
                    while (previous is not null &&
                           DateTime.UtcNow - since < TimeSpan.FromMilliseconds(300))
                    {
                        if (cancel.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50)))
                        {
                            return;
                        }

                        var current = Progress();
                        if (current != previous)
                        {
                            previous = current;
                            since = DateTime.UtcNow;
                        }
                    }

                    // A batch that finished instead got past this class before it was taken
                    if (previous is not null)
                    {
                        cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(6));
                    }
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            },
            // Watched inside rather than handed over: a token here cancels the scheduling, and a
            // task that never ran would fault the await after the window closes
            Cancel.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    /// <summary>
    /// The name InlineApplier gives the mutex it waits on for a file. Worked out the same way here
    /// rather than shared, because nothing but this test needs it; if the two ever part, the
    /// batch simply does not stop, and the rest of the test still stands.
    /// </summary>
    static string PatchMutex(string source)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(source).ToLowerInvariant()));
        return $"DiffEngineInline_{Convert.ToHexString(hash)}";
    }

    /// <summary>
    /// A class of <paramref name="methods"/> tests, each verifying and holding a snapshot, in the
    /// shape <see cref="WriteSource"/> writes one of. Returns the line of each verify call, which is
    /// what a patch reports.
    /// </summary>
    static (string Path, IReadOnlyList<int> Lines) WriteClass(DirectoryInfo directory, string name, int methods)
    {
        var builder = new StringBuilder("public class Sample\n{\n");
        var lines = new List<int>();
        for (var method = 1; method <= methods; method++)
        {
            // The class opens on two lines, each method takes seven, and the verify call is the
            // fourth of them
            lines.Add(2 + (method - 1) * 7 + 4);
            builder.Append("    [Test]\n");
            builder.Append($"    public Task Case{method}()\n");
            builder.Append("    {\n");
            builder.Append("        Verify(Build())\n");
            builder.Append($"            .Snapshot(\"old {method}\");\n");
            builder.Append("    }\n");
            builder.Append('\n');
        }

        builder.Append("    static string Build() =>\n");
        builder.Append("        \"content\";\n");
        builder.Append("}\n");
        return (Write(directory, name, builder.ToString()), lines);
    }

    /// <summary>
    /// Several lines, so accepting one writes a raw string literal and moves what follows it.
    /// </summary>
    static string Snapshot(int method) =>
        $$"""
          {
            Case: {{method}},
            Value: new
          }
          """;

    static int Unaccepted(string source)
    {
        var text = File.ReadAllText(source);
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(".Snapshot(\"old ", index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index++;
        }

        return count;
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
