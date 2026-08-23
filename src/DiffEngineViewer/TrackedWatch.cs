/// <summary>
/// Keeps the tracked files a viewer owns in step with the disk: a stat per entry per pass,
/// dropping the entries whose received file has gone and re-reading the ones that changed.
/// <para>
/// The owned counterpart of <see cref="OwnerLink" />'s read seam, which has always done this for a
/// queue held elsewhere. An owned queue is only ever pushed to - a socket message or a launch
/// argument puts an entry in it and nothing ever revisits it - so its rows described the moment
/// they arrived and nothing after. That was survivable while an owning viewer only held files with
/// no tray running; it stopped being survivable when every failing pair started arriving this way.
/// </para>
/// <para>
/// Its own thread, like <see cref="OwnerLink" />'s and for the same reason: re-reading a queue of
/// image snapshots is not work to do between two frames.
/// </para>
/// <para>
/// Deliberately not a file system watcher. The stat is what the attached path already pays, it
/// needs no handle per directory and no debounce, and a queue is small enough that the difference
/// is not measurable.
/// </para>
/// </summary>
sealed class TrackedWatch(SessionHost host)
{
    /// <summary>
    /// The same cadence an attached viewer reads at, so a re-run that rewrites a received file
    /// reaches the pane at the same speed whichever process is holding it.
    /// </summary>
    public static TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(200);

    public void Run(Cancel cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            cancel.WaitHandle.WaitOne(Interval);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            try
            {
                Pump();
            }
            catch (Exception exception)
            {
                // Nothing below is expected to throw - both reads swallow their own IO failures -
                // but this runs on a task nothing awaits, so an unobserved fault here would leave
                // a live window quietly no longer following its files. Said out loud, and the
                // queue stays usable, which is why this does not exit the way a lost owner does.
                host.Mutate(_ => _ with
                {
                    Message = $"Could not re-read the pending files: {exception.Message}"
                });
                return;
            }
        }
    }

    /// <summary>
    /// One pass. Public for the tests, which drive it directly rather than waiting on a thread.
    /// </summary>
    public void Pump()
    {
        var gone = new List<string>();
        var changed = new List<QueueEntry>();
        foreach (var entry in host.State.Queue)
        {
            if (entry.Kind == QueueEntryKind.Move)
            {
                Move(entry, gone, changed);
                continue;
            }

            if (entry.Kind == QueueEntryKind.Delete)
            {
                Delete(entry, gone, changed);
            }
        }

        if (gone.Count == 0 &&
            changed.Count == 0)
        {
            return;
        }

        host.Mutate(_ => ViewerSession.Refresh(_, gone, changed));
    }

    static void Move(QueueEntry entry, List<string> gone, List<QueueEntry> changed)
    {
        var temp = entry.LeftFile!;
        var target = entry.TargetFile!;
        if (FileSide.StampOf(temp) is not { } tempStamp)
        {
            // The received file is what the pair exists for, so its absence ends the entry. A
            // target that is not there is not the same thing at all: a brand new snapshot never
            // has one, and an entry offering to create it is the whole point.
            gone.Add(entry.Key);
            return;
        }

        if (entry.LeftStamp == tempStamp &&
            entry.RightStamp == FileSide.StampOf(target))
        {
            return;
        }

        Changed(
            entry,
            QueueEntry.ForMove(
                entry.Key,
                entry.Name,
                entry.Solution,
                temp,
                target,
                FileSide.Read(temp),
                FileSide.Read(target)),
            changed);
    }

    static void Delete(QueueEntry entry, List<string> gone, List<QueueEntry> changed)
    {
        var file = entry.LeftFile!;
        if (FileSide.StampOf(file) is not { } stamp)
        {
            // Already gone, so there is nothing left to offer to delete.
            gone.Add(entry.Key);
            return;
        }

        if (entry.LeftStamp == stamp)
        {
            return;
        }

        Changed(
            entry,
            QueueEntry.ForDelete(entry.Key, entry.Name, entry.Solution, file, FileSide.Read(file)),
            changed);
    }

    /// <summary>
    /// Stamped again after the read rather than trusted from before it, because a file that exists
    /// but cannot be opened stamps and does not read: it comes back with no stamp at all, which
    /// differs from the stat every time and would rebuild and re-diff the entry on every pass for
    /// as long as whatever holds the file holds it.
    /// </summary>
    static void Changed(QueueEntry entry, QueueEntry fresh, List<QueueEntry> changed)
    {
        if (fresh.LeftStamp == entry.LeftStamp &&
            fresh.RightStamp == entry.RightStamp)
        {
            return;
        }

        changed.Add(fresh);
    }
}
