class Tracker :
    IAsyncDisposable
{
    Action active;
    Action inactive;
    LockedFilesResolver? lockedFilesResolver;
    Action<TrackedMove>? acceptFailed;
    Action<string>? inlineFailed;
    ConcurrentDictionary<string, TrackedMove> moves = new(StringComparer.OrdinalIgnoreCase);
    ConcurrentDictionary<string, TrackedDelete> deletes = new(StringComparer.OrdinalIgnoreCase);
    IInlineHost inline;
    // The last listing seen, used for the icon state. Free when this tray owns the queue, and a
    // loopback round trip when a viewer does, which is why the menu re-reads live when it opens.
    IReadOnlyList<PendingSnapshot> snapshots = [];
    AsyncTimer timer;
    int lastScanCount;

    public Tracker(Action active, Action inactive, LockedFilesResolver? lockedFilesResolver = null, Action<TrackedMove>? acceptFailed = null, Action<string>? inlineFailed = null, IInlineHost? inline = null)
    {
        this.active = active;
        this.inactive = inactive;
        this.lockedFilesResolver = lockedFilesResolver;
        this.acceptFailed = acceptFailed;
        this.inlineFailed = inlineFailed;
        this.inline = inline ?? new RemoteInlineHost();
        timer = new(
            ScanFiles,
            TimeSpan.FromSeconds(2),
            exception =>
            {
                ExceptionHandler.Handle("Failed to scan files", exception);
            });
    }

    Task ScanFiles(Cancel cancel)
    {
        foreach (var delete in deletes.ToList()
                     .Where(delete => !File.Exists(delete.Value.File)))
        {
            deletes.TryRemove(delete.Key, out _);
        }

        // A passing re-run sends a settle message, and whoever owns the queue drops the entry
        // then, so there is nothing to expire here. Just refresh the listing that drives the icon.
        snapshots = inline.List();

        var newCount = moves.Count + deletes.Count + snapshots.Count;
        if (lastScanCount != newCount)
        {
            ToggleActive();
        }

        lastScanCount = newCount;
        return Task.WhenAll(moves.Select(HandleScanMove));
    }

    async Task HandleScanMove(KeyValuePair<string, TrackedMove> pair)
    {
        void RemoveAndKill(TrackedMove tacked)
        {
            if (moves.TryRemove(tacked.Temp, out var removed))
            {
                KillProcesses(removed);
            }
        }

        var move = pair.Value;
        if (!File.Exists(move.Temp))
        {
            RemoveAndKill(pair.Value);
            return;
        }

        if (!File.Exists(move.Target))
        {
            return;
        }
        try
        {
            if (!await FileComparer.FilesAreEqual(move.Temp, move.Target))
            {
                return;
            }
        }
        catch (IOException)
        {
            // File is missing, or locked by a diff tool or a running test.
            // Skip this scan round
            return;
        }

        RemoveAndKill(pair.Value);
    }

    void ToggleActive()
    {
        if (TrackingAny)
        {
            active();
        }
        else
        {
            inactive();
        }
    }

    public bool TrackingAny =>
        !moves.IsEmpty ||
        !deletes.IsEmpty ||
        snapshots.Count > 0;

    public TrackedMove AddMove(
        string temp,
        string target,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId)
    {
        var exeFile = Path.GetFileName(exe);
        var targetFile = Path.GetFileName(target);
        return moves.AddOrUpdate(
            temp,
            addValueFactory: temp =>
            {
                Process? process = null;
                if (processId != null)
                {
                    ProcessEx.TryGet(processId.Value, out process);
                }

                var move = BuildTrackedMove(temp, exe, arguments, canKill, target, process);

                if (exeFile == null)
                {
                    Log.Information("MoveAdded. Target:{target}, CanKill:{canKill}, Process:{process}", targetFile, move.CanKill, processId);
                }
                else
                {
                    Log.Information("MoveAdded. Target:{target}, CanKill:{canKill}, Process:{process}, Command:{command}", targetFile, move.CanKill, processId!, $"{exeFile} {arguments}");
                }

                return move;
            },
            updateValueFactory: (temp, existing) =>
            {
                Process? process;
                if (processId == null)
                {
                    process = existing.Process;
                }
                else
                {
                    existing.Process?.Dispose();
                    ProcessEx.TryGet(processId.Value, out process);
                }

                var move = BuildTrackedMove(temp, exe, arguments, canKill, target, process);

                if (exeFile == null)
                {
                    Log.Information("MoveUpdated. Target:{target}, CanKill:{canKill}, Process:{process}", targetFile, move.CanKill, processId);
                }
                else
                {
                    Log.Information("MoveUpdated. Target:{target}, CanKill:{canKill}, Process:{process}, Command:{command}", targetFile, move.CanKill, processId!, $"{exeFile} {arguments}");
                }

                return move;
            });
    }

    static TrackedMove BuildTrackedMove(string temp, string? exe, string? arguments, bool? canKill, string target, Process? process)
    {
        var solution = SolutionDirectoryFinder.Find(target);
        var extension = Path.GetExtension(target).TrimStart('.');
        var killLockingProcess = false;
        if (exe == null)
        {
            if (DiffTools.TryFindByExtension(extension, out var tool))
            {
                arguments = tool.GetArguments(temp, target);
                exe = tool.ExePath;
                canKill = !tool.IsMdi;
                killLockingProcess = tool.KillLockingProcess;
            }
        }
        else if (canKill == null)
        {
            if (DiffTools.TryFindByPath(exe, out var tool))
            {
                canKill = !tool.IsMdi;
                killLockingProcess = tool.KillLockingProcess;
            }
            else
            {
                canKill = false;
            }
        }
        else
        {
            if (DiffTools.TryFindByPath(exe, out var tool))
            {
                killLockingProcess = tool.KillLockingProcess;
            }
        }

        return new(temp, target, exe, arguments, canKill.GetValueOrDefault(false), process, solution, extension, killLockingProcess);
    }

    /// <summary>
    /// Applies the snapshot wherever the queue lives: here when this tray owns it, and in the
    /// viewer when one bound the port first.
    /// <para>
    /// On a worker, because this is called from a menu click and applying can wait ten seconds on
    /// InlineApplier's cross process mutex. The task is returned for tests; the menu discards it,
    /// and the balloon channel carries any failure back.
    /// </para>
    /// </summary>
    public Task Accept(PendingSnapshot snapshot) =>
        Task.Run(() =>
        {
            try
            {
                var outcome = inline.Accept(snapshot, out var message);
                switch (outcome)
                {
                    case AcceptOutcome.Applied:
                        Log.Information("Inline snapshot accepted for `{Name}`. {Message}", snapshot.Name, message);
                        break;
                    case AcceptOutcome.Stale:
                        // Gone, but not accepted. Told rather than logged, because the snapshot
                        // vanishing from the menu otherwise reads as success.
                        Log.Warning("Inline snapshot stale for `{Name}`: {Message}", snapshot.Name, message);
                        inlineFailed?.Invoke($"Could not accept the snapshot for '{snapshot.Name}'. {message}");
                        break;
                    default:
                        Log.Warning("Inline snapshot accept failed for `{Name}`: {Message}", snapshot.Name, message);
                        inlineFailed?.Invoke($"Could not accept the snapshot for '{snapshot.Name}'. {message}");
                        break;
                }

                Refresh();
            }
            catch (Exception exception)
            {
                ExceptionHandler.Handle($"Failed to accept the snapshot for '{snapshot.Name}'", exception);
            }
        });

    public void Discard(PendingSnapshot snapshot)
    {
        if (!inline.Discard(snapshot, out var message))
        {
            inlineFailed?.Invoke($"Could not discard the snapshot for '{snapshot.Name}'. {message}");
        }

        Refresh();
    }

    public Task AcceptAllSnapshots()
    {
        // Live read, not the scan cache: this can be called before the first scan, and acting on
        // a stale empty cache would silently do nothing.
        if (Snapshots.Count == 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            try
            {
                if (!inline.AcceptAll(out var message))
                {
                    inlineFailed?.Invoke($"Could not accept the pending snapshots. {message}");
                }

                Refresh();
            }
            catch (Exception exception)
            {
                ExceptionHandler.Handle("Failed to accept the pending snapshots", exception);
            }
        });
    }

    /// <summary>
    /// Bring the window forward on this snapshot, starting one when this tray owns the queue and
    /// nothing is displaying it.
    /// </summary>
    public void Focus(PendingSnapshot snapshot) =>
        inline.Focus(snapshot);

    public void CloseViewer() =>
        inline.Close();

    public void Refresh()
    {
        snapshots = inline.List();
        ToggleActive();
    }

    public TrackedDelete AddDelete(string file) =>
        deletes.AddOrUpdate(
            file,
            addValueFactory: key =>
            {
                Log.Information("DeleteAdded. File:{file}", file);
                var solution = SolutionDirectoryFinder.Find(key);
                return new(key, solution);
            },
            updateValueFactory: (_, existing) =>
            {
                Log.Information("DeleteUpdated. File:{file}", file);
                return existing;
            });

    public void Accept(TrackedDelete delete)
    {
        if (deletes.TryRemove(delete.File, out var removed))
        {
            File.Delete(removed.File);
        }
    }

    public void Accept(IEnumerable<TrackedDelete> toAccept)
    {
        foreach (var delete in toAccept)
        {
            if (deletes.TryRemove(delete.File, out var removed))
            {
                File.Delete(removed.File);
            }
        }
    }

    public void Accept(IEnumerable<TrackedMove> toAccept) =>
        AcceptMoves(toAccept);

    public void Accept(TrackedMove move) =>
        AcceptMoves([move]);

    class AcceptBatch
    {
        public bool KillWithoutPrompt;
        public bool AcceptAllPending;
    }

    void AcceptMoves(IEnumerable<TrackedMove> toAccept)
    {
        var batch = new AcceptBatch();
        foreach (var move in toAccept)
        {
            AcceptMove(move, batch);
        }

        if (batch.AcceptAllPending)
        {
            foreach (var move in moves.Values)
            {
                AcceptMove(move, batch);
            }
        }
    }

    void AcceptMove(TrackedMove move, AcceptBatch batch)
    {
        if (!moves.TryRemove(move.Temp, out var removed))
        {
            return;
        }

        if (!InnerMove(removed, batch))
        {
            // Keep the move pending so accepting can be retried
            moves.TryAdd(removed.Temp, removed);
        }
    }

    public void Discard(TrackedMove move)
    {
        if (moves.TryRemove(move.Temp, out var removed))
        {
            InnerDiscard(removed);
        }
    }

    const int acceptAttempts = 8;
    static readonly TimeSpan acceptRetryDelay = TimeSpan.FromMilliseconds(400);

    // Returns false when the move should be kept pending
    bool InnerMove(TrackedMove move, AcceptBatch batch)
    {
        KillProcesses(move);

        // A single move attempt and a single lock query are both racy:
        // * A killed diff tool releases its file handles asynchronously, and Job
        //   Objects reap child processes (eg diffword's WINWORD) a beat after the
        //   direct kill, so the first move attempt can fail while the locks are
        //   already on their way out.
        // * A diff tool killed mid-startup can leave an orphaned child that only
        //   opens (and locks) the files after the kill, so a lock query can find
        //   nothing even though the move keeps failing.
        // So retry both for a few seconds before giving up, and never treat an
        // unexplained failure as success.
        var killApproved = false;
        for (var attempt = 0; attempt < acceptAttempts; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(acceptRetryDelay);
            }

            if (!File.Exists(move.Temp))
            {
                // Nothing left to move. Drop the move since it is likely a
                // running test deleted or is re-writing the file, and the result
                // will re-add the tracked item
                return true;
            }

            if (FileEx.SafeMove(move.Temp, move.Target))
            {
                DeleteTempDirectory(move);
                return true;
            }

            var locked = FindLockedFiles(move);
            if (locked == null)
            {
                // No lock visible (yet). The holder may be mid-death or mid-startup
                continue;
            }

            Log.Information(
                "Files for `{Name}` are locked by {Processes}",
                move.Name,
                locked.ProcessNames);

            if (!killApproved &&
                !ShouldKill(move, locked, batch))
            {
                // The user chose to keep the locking processes. Keep the move
                // pending without further retries
                return false;
            }

            // Remember the approval so re-surfacing lockers dont re-prompt
            killApproved = true;
            FileLockKiller.Kill(locked.Processes);
            // Killed processes release their handles asynchronously; the next
            // attempt re-tries the move
        }

        Log.Warning("Could not accept `{Name}`: the move keeps failing. Kept pending", move.Name);
        acceptFailed?.Invoke(move);
        return false;
    }

    bool ShouldKill(TrackedMove move, LockedFiles locked, AcceptBatch batch)
    {
        if (move.KillLockingProcess ||
            batch.KillWithoutPrompt)
        {
            return true;
        }

        if (lockedFilesResolver == null)
        {
            return false;
        }

        switch (lockedFilesResolver(move, locked))
        {
            case LockedFilesResponse.Kill:
                return true;
            case LockedFilesResponse.KillAndAcceptAllPending:
                batch.KillWithoutPrompt = true;
                batch.AcceptAllPending = true;
                return true;
            default:
                return false;
        }
    }

    static LockedFiles? FindLockedFiles(TrackedMove move)
    {
        var files = new List<string>();
        var processes = new List<LockingProcess>();

        void AddLockers(string file)
        {
            var lockers = FileLockKiller.GetLockingProcesses(file);
            if (lockers.Count == 0)
            {
                return;
            }

            files.Add(file);
            foreach (var locker in lockers)
            {
                if (processes.TrueForAll(_ => _.ProcessId != locker.ProcessId))
                {
                    processes.Add(locker);
                }
            }
        }

        AddLockers(move.Temp);
        AddLockers(move.Target);

        if (files.Count == 0)
        {
            return null;
        }

        return new(files, processes);
    }

    static void DeleteTempDirectory(TrackedMove move)
    {
        var directory = Path.GetDirectoryName(move.Temp)!;
        FileEx.SafeDeleteDirectory(directory);
    }

    static void InnerDiscard(TrackedMove move)
    {
        KillProcesses(move);

        if (!FileEx.SafeDeleteFile(move.Temp))
        {
            return;
        }

        var directory = Path.GetDirectoryName(move.Temp)!;
        FileEx.SafeDeleteDirectory(directory);
    }

    static void KillProcesses(TrackedMove move)
    {
        if (!move.CanKill)
        {
            Log.Information("Did not kill for `{Name}` since CanKill=false", move.Name);
            return;
        }

        if (move.Process == null)
        {
            Log.Information("No processes to kill for `{Name}`", move.Name);
            return;
        }

        move.Process.KillAndDispose();
    }

    public void Clear()
    {
        deletes.Clear();

        foreach (var move in moves.Values)
        {
            KillProcesses(move);
        }

        moves.Clear();

        // The menu counts snapshots in "Discard (n)", so discarding has to include them. Clearing
        // only the cache used to make the button lie twice over: it discarded fewer things than it
        // said, and the ones it skipped came back on the next scan two seconds later.
        inline.DiscardAll();
        snapshots = [];
    }

    public void AcceptOpen()
    {
        AcceptAllDeletes();

        AcceptMoves(
            moves.Values
                .Where(_ => _.Process is { HasExited: false })
                .ToList());

        // Every pending snapshot is open by definition: the viewer only stays running while it
        // has something to show.
        AcceptAllSnapshots();
    }

    public void AcceptAll()
    {
        AcceptAllDeletes();

        AcceptMoves(moves.Values);

        AcceptAllSnapshots();
    }

    void AcceptAllDeletes()
    {
        foreach (var delete in deletes.Values)
        {
            File.Delete(delete.File);
        }

        deletes.Clear();
    }

    public ICollection<TrackedDelete> Deletes => deletes.Values;

    public ICollection<TrackedMove> Moves => moves.Values;

    /// <summary>
    /// Read live rather than from the scan cache, so the menu shows the viewer's current queue at
    /// the moment it opens.
    /// </summary>
    public IReadOnlyList<PendingSnapshot> Snapshots
    {
        get
        {
            snapshots = inline.List();
            return snapshots;
        }
    }

    public ValueTask DisposeAsync()
    {
        Clear();
        return timer.DisposeAsync();
    }
}