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
/// Deliberately not a file system watcher. The stat is what the attached path already pays, and
/// it needs no handle per directory and no debounce. What it costs is kept small instead: a pass
/// looks at the entry on screen and at <see cref="Budget"/> of the others, taking those in turn,
/// and the passes come a second apart while the window is hidden. A thousand pending pairs, which
/// one serializer setting can produce, were two thousand stats five times a second for as long as
/// the viewer ran, and behind a tray that is days.
/// </para>
/// </summary>
sealed class TrackedWatch(SessionHost host, DocumentPlugin? documents = null)
{
    /// <summary>
    /// The same cadence an attached viewer reads at, so a re-run that rewrites a received file
    /// reaches the pane at the same speed whichever process is holding it.
    /// </summary>
    public static TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Whether the window is hidden, set by the render loop as it hides and shows it. Nobody is
    /// reading a hidden window's rows, so its files are looked at as often as an attached viewer
    /// lists its owner while hidden. Not stopped, because an entry whose received file has gone
    /// is still pending for as long as it is in the queue, and the queue is what gets staged.
    /// </summary>
    public bool Hidden { get; set; }

    public static TimeSpan HiddenInterval { get; set; } = OwnerLink.HiddenInterval;

    /// <summary>
    /// How many pending files one pass looks at, besides the one on screen. A queue of no more
    /// than this is looked at whole every pass, as every queue used to be. A longer one is looked
    /// at this many at a time, so a row that is not on screen follows its file within a few
    /// seconds rather than within one pass.
    /// </summary>
    public int Budget { get; init; } = 100;

    // Where in the queue the next pass takes up. A position rather than a key, so an entry that
    // leaves moves it by one, which costs some entry a pass and no more.
    int next;

    public void Run(Cancel cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            cancel.WaitHandle.WaitOne(Hidden ? HiddenInterval : Interval);
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
    /// <para>
    /// What it found is handed on with the entries it found it about, and applied only to those
    /// same entries. The stat runs outside the host's lock, and a re-run can stage its pair again
    /// under the same key in between: dropped by key, the new pair went with the old one's missing
    /// file, and stayed gone until the test failed again.
    /// </para>
    /// <para>
    /// The entry on screen every pass, since it is the one being read, and then the others from
    /// where the last pass left off, until <see cref="Budget"/> of them have been looked at or
    /// the queue has been gone round once.
    /// </para>
    /// </summary>
    public void Pump()
    {
        var gone = new List<QueueEntry>();
        var changed = new List<(QueueEntry Seen, QueueEntry Fresh)>();
        // One read, so the entry on screen and the queue it is in are of the same moment
        var state = host.State;
        var queue = state.Queue;
        var current = state.Current;
        if (current is not null)
        {
            Look(current, gone, changed);
        }

        if (next >= queue.Count)
        {
            next = 0;
        }

        var looked = 0;
        for (var visited = 0; visited < queue.Count && looked < Budget; visited++)
        {
            var entry = queue[next];
            next = (next + 1) % queue.Count;
            if (!ReferenceEquals(entry, current) &&
                Look(entry, gone, changed))
            {
                looked++;
            }
        }

        if (gone.Count == 0 &&
            changed.Count == 0)
        {
            return;
        }

        host.Mutate(_ => ViewerSession.Refresh(_, gone, changed));
    }

    /// <summary>
    /// Whether the entry is one with files to look at, which is what a pass has a budget of. A
    /// snapshot has none: it is in the queue as the patch it arrived as.
    /// </summary>
    bool Look(QueueEntry entry, List<QueueEntry> gone, List<(QueueEntry Seen, QueueEntry Fresh)> changed)
    {
        if (entry.Kind == QueueEntryKind.Move)
        {
            Move(entry, gone, changed);
            return true;
        }

        if (entry.Kind == QueueEntryKind.Delete)
        {
            Delete(entry, gone, changed);
            return true;
        }

        return false;
    }

    readonly ReadRetry retry = new();

    void Move(QueueEntry entry, List<QueueEntry> gone, List<(QueueEntry Seen, QueueEntry Fresh)> changed)
    {
        var temp = entry.LeftFile!;
        var target = entry.TargetFile!;
        if (FileSide.StampOf(temp) is not { } tempStamp)
        {
            // The received file is what the pair exists for, so its absence ends the entry. A
            // target that is not there is not the same thing at all: a brand new snapshot never
            // has one, and an entry offering to create it is the whole point.
            gone.Add(entry);
            return;
        }

        if (entry.LeftStamp == tempStamp &&
            entry.RightStamp == FileSide.StampOf(target))
        {
            return;
        }

        Changed(
            entry,
            () => QueueEntry.ForMove(
                entry.Key,
                entry.Name,
                entry.Solution,
                temp,
                target,
                FileSide.Read(temp, documents),
                FileSide.Read(target, documents)),
            changed);
    }

    void Delete(QueueEntry entry, List<QueueEntry> gone, List<(QueueEntry Seen, QueueEntry Fresh)> changed)
    {
        var file = entry.LeftFile!;
        if (FileSide.StampOf(file) is not { } stamp)
        {
            // Already gone, so there is nothing left to offer to delete.
            gone.Add(entry);
            return;
        }

        if (entry.LeftStamp == stamp)
        {
            return;
        }

        Changed(
            entry,
            () => QueueEntry.ForDelete(entry.Key, entry.Name, entry.Solution, file, FileSide.Read(file, documents)),
            changed);
    }

    /// <summary>
    /// Re-read, unless a read of it failed a moment ago (<see cref="ReadRetry" />). Stamped again
    /// after the read rather than trusted from before it, because a file that exists but cannot be
    /// opened stamps and does not read: it comes back with no stamp at all, the same as the entry
    /// already held, which is kept rather than replaced by an identical one.
    /// </summary>
    void Changed(QueueEntry entry, Func<QueueEntry> read, List<(QueueEntry Seen, QueueEntry Fresh)> changed)
    {
        if (retry.Waiting(entry.Key))
        {
            return;
        }

        var fresh = read();
        retry.Read(fresh);
        if (fresh.LeftStamp == entry.LeftStamp &&
            fresh.RightStamp == entry.RightStamp)
        {
            return;
        }

        changed.Add((entry, fresh));
    }
}
