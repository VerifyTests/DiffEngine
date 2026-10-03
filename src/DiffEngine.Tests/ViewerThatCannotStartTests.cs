// DiffEngineTray is the obsolete public shim, but its IsRunning is still where the tray check
// lives, and this test has to hold it down.
#pragma warning disable CS0618

/// <summary>
/// A viewer that resolves and cannot take the launch, from the public entry point down: the real
/// resolution, the real ShellExecute launch and the real probe of the port, with only the
/// executable stood in for.
/// <para>
/// The copy that resolves is not always the one this library was built beside. A globally
/// installed tool and the copy shipped with a tray are both looked for ahead of the bundled one,
/// and either can be older than the arguments it is about to be given: <c>--payload</c> first
/// shipped in 20.5.0, and a viewer from before it exits with 2 on seeing it. The launch was
/// reported as made all the same, so the snapshot was called queued, in a queue no process held,
/// and a caller told that stages nothing.
/// </para>
/// <para>
/// FakeDiffTool copied to the viewer's name is that older copy: it exits with 2 on
/// <c>--payload</c>. Windows only, as everything else that starts FakeDiffTool is.
/// </para>
/// </summary>
[NotInParallel]
[RunOn(TUnit.Core.Enums.OS.Windows)]
public class ViewerThatCannotStartTests
{
    [Test]
    public async Task AnInlineSnapshotIsNotCalledQueued()
    {
        using var viewer = new OlderViewer();
        var patch = new InlinePatch(Path.Combine(viewer.Folder, "Tests.cs"), 42, "\"old\"", "new")
        {
            TestName = "Tests.Method"
        };
        var before = PayloadFiles();

        var result = await DiffRunner.AddInlineAsync(patch);

        await Assert.That(result).IsEqualTo(InlineResult.NoViewerFound);
        // The viewer reads its payload file and deletes it, and this one never got that far, so
        // the file is the launcher's to take back
        await Assert.That(await LeftBehind(before)).IsEmpty();
    }

    /// <summary>
    /// The payload files that are there now and were not before, once any that are someone
    /// else's have had time to go. The temp folder is shared, and this test runs in the net48
    /// process and the net10.0 one at the same time: each saw the other's file in the moment
    /// between it being written and taken back, and failed for it. One this launch left behind
    /// stays, however long it is waited for.
    /// </summary>
    static async Task<List<string>> LeftBehind(List<string> before)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            var left = PayloadFiles().Except(before).ToList();
            if (left.Count == 0 ||
                waited.Elapsed > TimeSpan.FromSeconds(40))
            {
                return left;
            }

            await Task.Delay(100);
        }
    }

    static List<string> PayloadFiles() =>
        Directory.GetFiles(Path.GetTempPath(), "DiffEngineViewer_*.inlinepatch").ToList();

    /// <summary>
    /// The stand-in, resolved the way a real copy is - through the variable that overrides where
    /// the viewer is looked for - with no tray and nothing on the port, so the launch is the only
    /// way left to hand the patch over.
    /// </summary>
    sealed class OlderViewer :
        IDisposable
    {
        const string variable = "DiffEngine_DiffEngineViewer";
        readonly string? previousPort;
        readonly bool previousRunning;
        readonly bool previousDisabled;
        readonly TimeSpan previousBindWait;

        public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"DiffEngine.OlderViewer.{Guid.NewGuid():N}");

        public OlderViewer()
        {
            // The apphost under the viewer's name. Everything it loads keeps its own, since the
            // apphost looks for the assembly it was built for rather than one named after itself
            Directory.CreateDirectory(Folder);
            var apphost = Path.GetFileName(FakeDiffTool.Exe);
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(FakeDiffTool.Exe)!))
            {
                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(Folder, name == apphost ? "DiffEngineViewer.exe" : name));
            }

            if (!ViewerServer.TryBind(0, out var bound))
            {
                throw new("Could not bind an ephemeral port.");
            }

            var port = bound.Port;
            bound.Dispose();
            previousPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
            previousRunning = DiffEngineTray.IsRunning;
            previousDisabled = DiffRunner.Disabled;
            previousBindWait = ViewerLaunchGate.BindWait;
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, port.ToString());
            DiffEngineTray.IsRunning = false;
            DiffRunner.Disabled = false;
            // Well past how long a freshly copied executable can take to start with a virus
            // scanner reading it first. The stand-in is noticed when it exits, not when this ends
            ViewerLaunchGate.BindWait = TimeSpan.FromSeconds(30);
            ViewerClient.ForgetUnowned();
            MaxInstance.ResetCount();

            try
            {
                Environment.SetEnvironmentVariable(variable, Folder);
                DiffTools.UseOrder(DiffTool.DiffEngineViewer);
                // Checked before anything is launched, because the alternative to the stand-in is
                // whichever real viewer this machine has installed
                if (!DiffTools.TryFindByName(DiffTool.DiffEngineViewer, out var tool) ||
                    !tool.ExePath.StartsWith(Folder, StringComparison.OrdinalIgnoreCase))
                {
                    throw new($"The stand-in viewer did not resolve. Resolved: {tool?.ExePath}");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(variable, null);
            DiffTools.Reset();
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousPort);
            DiffEngineTray.IsRunning = previousRunning;
            DiffRunner.Disabled = previousDisabled;
            ViewerLaunchGate.BindWait = previousBindWait;
            ViewerClient.ForgetUnowned();
            MaxInstance.ResetCount();
            try
            {
                Directory.Delete(Folder, true);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // A stand-in that is still on its way out holds its own files for a moment
            }
        }
    }
}
