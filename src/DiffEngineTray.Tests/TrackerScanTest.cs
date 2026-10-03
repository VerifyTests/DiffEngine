/// <summary>
/// The two second scan, which drops a move whose received file has gone or has come to equal its
/// target, and ends that move's diff tool.
/// </summary>
[NotInParallel]
public class TrackerScanTest :
    IDisposable
{
    /// <summary>
    /// The scan decides about the move it read and then compares two files, which takes as long
    /// as they are large. A re-run that lands in between replaces the move, and the removal was by
    /// key alone: it took the fresh move, which nothing had found equal to anything, and ended the
    /// tool just opened for it.
    /// </summary>
    [Test]
    public async Task AMoveReplacedWhileItWasBeingComparedIsKept()
    {
        await using var tracker = new RecordingTracker();
        File.WriteAllText(temp, "same");
        File.WriteAllText(target, "same");
        File.WriteAllText(other, "different");
        // What the scan read, and then what a re-run made of it before the scan had finished
        var scanned = tracker.AddMove(temp, target, "theExe", "theArguments", true, null);
        var fresh = tracker.AddMove(temp, other, "theExe", "theArguments", true, null);

        await tracker.HandleScanMove(new(temp, scanned));

        await Assert.That(tracker.FindMove(temp)).IsSameReferenceAs(fresh);
    }

    [Test]
    public async Task AMoveThatCameToEqualItsTargetIsDropped()
    {
        await using var tracker = new RecordingTracker();
        File.WriteAllText(temp, "same");
        File.WriteAllText(target, "same");
        var scanned = tracker.AddMove(temp, target, "theExe", "theArguments", true, null);

        await tracker.HandleScanMove(new(temp, scanned));

        await Assert.That(tracker.Moves).IsEmpty();
    }

    /// <summary>
    /// A file that may not be read is no more a failure of the scan than one that is locked: the
    /// pair cannot be compared this round. Only the locked one was caught, so this one failed the
    /// whole scan, every two seconds, for as long as the move stayed pending.
    /// </summary>
    [Test]
    public async Task ATargetThatMayNotBeReadIsPassedOver()
    {
        await using var tracker = new RecordingTracker();
        File.WriteAllText(temp, "same");
        File.WriteAllText(target, "same");
        var scanned = tracker.AddMove(temp, target, "theExe", "theArguments", true, null);

        var info = new FileInfo(target);
        var security = info.GetAccessControl();
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ReadData,
            AccessControlType.Deny);
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        try
        {
            await tracker.HandleScanMove(new(temp, scanned));

            await Assert.That(tracker.FindMove(temp)).IsSameReferenceAs(scanned);
        }
        finally
        {
            security.RemoveAccessRule(rule);
            info.SetAccessControl(security);
        }
    }

    /// <summary>
    /// A scan that fails for a reason nobody foresaw is logged and the next one runs. It used to
    /// ask whether to open an issue, in a modal box put up from the timer's thread, and no scan
    /// ran again until somebody answered it.
    /// </summary>
    [Test]
    public async Task AScanThatFailsIsFollowedByTheNextWithNobodyAsked()
    {
        var listed = 0;
        var host = new StubInlineHost
        {
            Listed = () =>
            {
                // The first is the tracker starting, and the rest are scans
                if (Interlocked.Increment(ref listed) > 1)
                {
                    throw new InvalidOperationException("TheScanFailure");
                }
            }
        };
        await using var tracker = new RecordingTracker(inline: host);

        var timeout = Stopwatch.StartNew();
        while (Volatile.Read(ref listed) < 3 &&
               timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(100);
        }

        await Assert.That(Volatile.Read(ref listed)).IsGreaterThanOrEqualTo(3);
        await Assert.That(ModuleInitializer.IssuesAsked.Where(_ => _.Contains("Failed to scan files"))).IsEmpty();
    }

    /// <summary>
    /// Logged and nothing more, a tray whose every scan failed looked like one with nothing wrong
    /// while its menu and icon stopped following the files. A run of failures is said once, in a
    /// balloon, and not again for each scan in it: that would be one every two seconds.
    /// <para>
    /// The scans here are run by the test, beside the tracker's own two second timer over the same
    /// failing queue, whose scans count towards the same run. So what is asserted holds however
    /// many of those land in between.
    /// </para>
    /// </summary>
    [Test]
    public async Task AScanThatKeepsFailingIsSaidOnce()
    {
        var listed = 0;
        var told = new ConcurrentQueue<string>();
        var host = new StubInlineHost
        {
            Listed = () =>
            {
                // The first is the tracker starting, and the rest are scans
                if (Interlocked.Increment(ref listed) > 1)
                {
                    throw new InvalidOperationException("TheScanFailure");
                }
            }
        };
        await using var tracker = new RecordingTracker(inline: host, scanFailing: told.Enqueue);

        for (var scan = 0; scan < Tracker.ScanFailuresBeforeTelling * 4; scan++)
        {
            await tracker.Scan(Cancel.None);
        }

        await Assert.That(told).HasSingleItem();
        await Assert.That(told.Single()).Contains("TheScanFailure");
        await Assert.That(ModuleInitializer.IssuesAsked.Where(_ => _.Contains("Failed to scan files"))).IsEmpty();
    }

    /// <summary>
    /// A scan that works ends the run, so the next run of failures is news again.
    /// </summary>
    [Test]
    public async Task AScanThatFailsAgainAfterWorkingIsSaidAgain()
    {
        var failing = false;
        var told = new ConcurrentQueue<string>();
        var host = new StubInlineHost
        {
            Listed = () =>
            {
                if (Volatile.Read(ref failing))
                {
                    throw new InvalidOperationException("TheScanFailure");
                }
            }
        };
        await using var tracker = new RecordingTracker(inline: host, scanFailing: told.Enqueue);

        async Task ScanThrice()
        {
            for (var scan = 0; scan < Tracker.ScanFailuresBeforeTelling; scan++)
            {
                await tracker.Scan(Cancel.None);
            }
        }

        Volatile.Write(ref failing, true);
        await ScanThrice();
        await Assert.That(told.Count).IsEqualTo(1);

        Volatile.Write(ref failing, false);
        await tracker.Scan(Cancel.None);

        Volatile.Write(ref failing, true);
        await ScanThrice();
        await Assert.That(told.Count).IsEqualTo(2);
    }

    /// <summary>
    /// One scan failing is a file that went between two lines of it, and nobody is told.
    /// </summary>
    [Test]
    public async Task ASingleFailedScanIsNotSaid()
    {
        var failing = true;
        var told = new ConcurrentQueue<string>();
        var listed = 0;
        var host = new StubInlineHost
        {
            Listed = () =>
            {
                // The tracker starting, which is not a scan, and then the one scan that fails
                if (Interlocked.Increment(ref listed) > 1 &&
                    Volatile.Read(ref failing))
                {
                    throw new InvalidOperationException("TheScanFailure");
                }
            }
        };
        await using var tracker = new RecordingTracker(inline: host, scanFailing: told.Enqueue);

        await tracker.Scan(Cancel.None);
        Volatile.Write(ref failing, false);
        await tracker.Scan(Cancel.None);

        await Assert.That(told).IsEmpty();
    }

    public TrackerScanTest()
    {
        directory = Path.Combine(Path.GetTempPath(), "DiffEngineTray.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        temp = Path.Combine(directory, "file.received.txt");
        target = Path.Combine(directory, "file.verified.txt");
        other = Path.Combine(directory, "other.verified.txt");
    }

    public void Dispose() =>
        Directory.Delete(directory, true);

    readonly string directory;
    readonly string temp;
    readonly string target;
    readonly string other;
}
