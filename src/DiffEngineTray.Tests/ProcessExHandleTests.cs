/// <summary>
/// TryGet holds an OS handle on the process it hands back.
/// <para>
/// Process.GetProcessById holds none of its own, so Kill, HasExited and MainWindowHandle each
/// re-open the id at the moment they are called. A tracked move outlives its diff tool by design -
/// HandleScanMove keeps it while the temp file is still there - so hours later that id may belong
/// to something else, and "Accept all" or "Open diff tool" would act on whatever it is.
/// </para>
/// <para>
/// Asserted through the exit code, which is only readable if a handle was open before the process
/// ended. That is the same thing as the id being held, since Windows will not hand out an id while
/// a handle to it exists.
/// </para>
/// </summary>
[NotInParallel]
public class ProcessExHandleTests
{
    [Test]
    public async Task TheProcessIsStillReadableAfterItExits()
    {
        // Any long lived process will do; this project has no fake tool of its own
        var started = FileLockUtils.StartFileLockProcess(Path.GetTempFileName());

        try
        {
            await Assert.That(ProcessEx.TryGet(started.Id, out var tracked)).IsTrue();

            started.Kill();
            await Assert.That(started.WaitForExit(30000)).IsTrue();

            // Readable only because TryGet opened a handle while it was still running
            await Assert.That(tracked!.HasExited).IsTrue();
            await Assert.That(tracked.ExitCode).IsNotEqualTo(int.MinValue);
            tracked.Dispose();
        }
        finally
        {
            try
            {
                if (!started.HasExited)
                {
                    started.Kill();
                }
            }
            catch
            {
                // Already gone
            }

            started.Dispose();
        }
    }
}
