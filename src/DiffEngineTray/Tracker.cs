class Tracker :
    IAsyncDisposable,
    ITrackedFiles
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

    /// <summary>
    /// Where the inline queue lives, for the debug view. Decided at startup by which process bound
    /// the port, and never changes after that.
    /// </summary>
    public string InlineDescription => inline.Description;

    /// <summary>
    /// The queued patches, for the debug view, when this tray owns the queue. Null when a viewer
    /// does, since then they are held over there.
    /// </summary>
    public IReadOnlyList<PendingInline>? QueuedPatches => inline.Queued();

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
    /// Accepts just these snapshots, for a group header: unlike <see cref="AcceptAllSnapshots"/>,
    /// solution A's header must not accept solution B's queue.
    /// </summary>
    public Task Accept(IEnumerable<PendingSnapshot> toAccept) =>
        Task.Run(() =>
        {
            try
            {
                var failures = new List<string>();
                foreach (var snapshot in toAccept)
                {
                    var outcome = inline.Accept(snapshot, out var message);
                    if (outcome == AcceptOutcome.Applied)
                    {
                        Log.Information("Inline snapshot accepted for `{Name}`. {Message}", snapshot.Name, message);
                        continue;
                    }

                    if (outcome == AcceptOutcome.Unknown)
                    {
                        continue;
                    }

                    Log.Warning("Inline snapshot accept failed for `{Name}`: {Message}", snapshot.Name, message);
                    failures.Add(message ?? snapshot.Name);
                }

                if (failures.Count > 0)
                {
                    inlineFailed?.Invoke(failures.Count == 1
                        ? $"Could not accept a snapshot. {failures[0]}"
                        : $"Could not accept {failures.Count} snapshots. {failures[0]}");
                }

                Refresh();
            }
            catch (Exception exception)
            {
                ExceptionHandler.Handle("Failed to accept the snapshots", exception);
            }
        });

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

        // Wire-driven accepts run on a listener thread with no user attached, so the locked-files
        // dialog must never be raised for them.
        public bool NeverPrompt;
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

        if (batch.NeverPrompt ||
            lockedFilesResolver == null)
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

    /// <summary>
    /// The menu's "Discard (n)". Everything pending goes, on every surface.
    /// <para>
    /// Through the same discard the wire uses, so the two surfaces cannot mean different things by
    /// it. Discarding a move throws its received file away — <see cref="Discard(TrackedMove)"/> has
    /// always done that, and so does a discard arriving from the viewer — and sweeping the
    /// dictionary directly left the temps behind for a button that said it had discarded them.
    /// </para>
    /// <para>
    /// The snapshots go too: the menu counts them in "Discard (n)". Clearing only the cache used to
    /// make the button lie twice over — it discarded fewer things than it said, and the ones it
    /// skipped came back on the next scan two seconds later.
    /// </para>
    /// </summary>
    public void Clear()
    {
        ((ITrackedFiles) this).DiscardAll();

        inline.DiscardAll();
        snapshots = [];
    }

    /// <summary>
    /// The returned task covers the snapshot half, which runs on a worker for the reason
    /// <see cref="Accept(PendingSnapshot)"/> gives. The menu and the hot keys discard it; tests
    /// await it so what the other surface should now be showing is settled rather than in flight.
    /// </summary>
    public Task AcceptOpen()
    {
        AcceptAllDeletes();

        AcceptMoves(
            moves.Values
                .Where(_ => _.Process is { HasExited: false })
                .ToList());

        // Every pending snapshot is open by definition: the viewer only stays running while it
        // has something to show.
        return AcceptAllSnapshots();
    }

    /// <inheritdoc cref="AcceptOpen"/>
    public Task AcceptAll()
    {
        AcceptAllDeletes();

        AcceptMoves(moves.Values);

        return AcceptAllSnapshots();
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

    IReadOnlyList<ViewerResponseMove> ITrackedFiles.Moves() =>
        moves.Values
            .Select(_ => new ViewerResponseMove(
                TrackedKeys.ForMove(_.Temp),
                $"{_.Name} ({_.Extension})",
                _.Group,
                _.Temp,
                _.Target))
            .ToList();

    IReadOnlyList<ViewerResponseDelete> ITrackedFiles.Deletes() =>
        deletes.Values
            .Select(_ => new ViewerResponseDelete(
                TrackedKeys.ForDelete(_.File),
                _.Name,
                _.Group,
                _.File))
            .ToList();

    void ITrackedFiles.AddMove(string temp, string target)
    {
        // No exe, arguments or process: the sender's diff tool details do not cross the viewer
        // port, so this is resolved from the extension exactly as a piper move with no exe is.
        AddMove(temp, target, null, null, false, null);
        Refresh();
    }

    void ITrackedFiles.AddDelete(string file)
    {
        AddDelete(file);
        Refresh();
    }

    bool ITrackedFiles.Has(string key)
    {
        if (TrackedKeys.TryStrip(key, TrackedKeys.MovePrefix, out var temp))
        {
            return moves.ContainsKey(temp);
        }

        return TrackedKeys.TryStrip(key, TrackedKeys.DeletePrefix, out var file) &&
               deletes.ContainsKey(file);
    }

    (bool ok, string? message) ITrackedFiles.Accept(string key)
    {
        if (TrackedKeys.TryStrip(key, TrackedKeys.MovePrefix, out var temp))
        {
            return moves.TryGetValue(temp, out var move)
                ? AcceptWithoutPrompting(move)
                : (false, null);
        }

        if (TrackedKeys.TryStrip(key, TrackedKeys.DeletePrefix, out var file))
        {
            return deletes.TryGetValue(file, out var delete)
                ? AcceptTracked(delete)
                : (false, null);
        }

        return (false, null);
    }

    (bool ok, string? message) ITrackedFiles.Discard(string key)
    {
        if (TrackedKeys.TryStrip(key, TrackedKeys.MovePrefix, out var temp))
        {
            if (!moves.TryRemove(temp, out var removed))
            {
                return (false, null);
            }

            InnerDiscard(removed);
            return (true, $"Discarded {removed.Name}");
        }

        if (TrackedKeys.TryStrip(key, TrackedKeys.DeletePrefix, out var file))
        {
            if (!deletes.TryRemove(file, out var removed))
            {
                return (false, null);
            }

            // Untracked only: the file stays, matching what Clear has always meant for deletes.
            // The next test run re-tracks it.
            return (true, $"Discarded {removed.Name}");
        }

        return (false, null);
    }

    (int accepted, int kept) ITrackedFiles.AcceptAll()
    {
        var accepted = 0;
        var kept = 0;
        foreach (var delete in deletes.Values.ToList())
        {
            if (AcceptTracked(delete).ok)
            {
                accepted++;
            }
            else
            {
                kept++;
            }
        }

        foreach (var move in moves.Values.ToList())
        {
            if (AcceptWithoutPrompting(move).ok)
            {
                accepted++;
            }
            else
            {
                kept++;
            }
        }

        return (accepted, kept);
    }

    int ITrackedFiles.DiscardAll()
    {
        var count = 0;
        foreach (var delete in deletes.Values.ToList())
        {
            if (deletes.TryRemove(delete.File, out _))
            {
                count++;
            }
        }

        foreach (var move in moves.Values.ToList())
        {
            if (moves.TryRemove(move.Temp, out var removed))
            {
                InnerDiscard(removed);
                count++;
            }
        }

        return count;
    }

    (bool ok, string? message) AcceptTracked(TrackedDelete delete)
    {
        if (!deletes.TryRemove(delete.File, out var removed))
        {
            return (false, null);
        }

        try
        {
            File.Delete(removed.File);
        }
        catch (Exception exception)
        {
            // Re-tracked so it can be retried, and refused so the caller shows why.
            deletes.TryAdd(removed.File, removed);
            return (false, $"Could not delete {removed.Name}. {exception.Message}");
        }

        return (true, $"Deleted {removed.Name}");
    }

    (bool ok, string? message) AcceptWithoutPrompting(TrackedMove move)
    {
        if (!moves.TryRemove(move.Temp, out var removed))
        {
            return (false, null);
        }

        if (InnerMove(removed, new()
            {
                NeverPrompt = true
            }))
        {
            return (true, $"Accepted {removed.Name}");
        }

        moves.TryAdd(removed.Temp, removed);
        return (false, $"Files for '{removed.Name}' are locked. Accept from the tray menu to resolve.");
    }

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

    /// <summary>
    /// Deliberately not <see cref="Clear"/>: exiting is not discarding. The diff tools this tray
    /// started are killed, and everything pending stays where it is — the received files on disk
    /// for the next tray to re-track, and the inline queue with whoever owns it, which outlives
    /// this process whenever that is a viewer.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        foreach (var move in moves.Values)
        {
            KillProcesses(move);
        }

        moves.Clear();
        deletes.Clear();
        snapshots = [];
        return timer.DisposeAsync();
    }
}