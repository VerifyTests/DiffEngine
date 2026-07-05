class Tracker :
    IAsyncDisposable
{
    Action active;
    Action inactive;
    LockedFilesResolver? lockedFilesResolver;
    ConcurrentDictionary<string, TrackedMove> moves = new(StringComparer.OrdinalIgnoreCase);
    ConcurrentDictionary<string, TrackedDelete> deletes = new(StringComparer.OrdinalIgnoreCase);
    AsyncTimer timer;
    int lastScanCount;

    public Tracker(Action active, Action inactive, LockedFilesResolver? lockedFilesResolver = null)
    {
        this.active = active;
        this.inactive = inactive;
        this.lockedFilesResolver = lockedFilesResolver;
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

        var newCount = moves.Count + deletes.Count;
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
        catch (FileNotFoundException)
        {
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
        !deletes.IsEmpty;

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

    // Returns false when the move should be kept pending
    bool InnerMove(TrackedMove move, AcceptBatch batch)
    {
        KillProcesses(move);

        if (FileEx.SafeMove(move.Temp, move.Target))
        {
            DeleteTempDirectory(move);
            return true;
        }

        var locked = FindLockedFiles(move);
        if (locked == null)
        {
            // Not caused by a file lock. Drop the move since it is likely a
            // running test is reading or writing to the files, and the result
            // will re-add the tracked item
            return true;
        }

        Log.Information(
            "Files for `{Name}` are locked by {Processes}",
            move.Name,
            locked.ProcessNames);

        if (!ShouldKill(move, locked, batch))
        {
            return false;
        }

        FileLockKiller.Kill(locked.Processes);

        if (FileEx.SafeMove(move.Temp, move.Target))
        {
            DeleteTempDirectory(move);
            return true;
        }

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
    }

    public void AcceptOpen()
    {
        AcceptAllDeletes();

        AcceptMoves(
            moves.Values
                .Where(_ => _.Process is { HasExited: false })
                .ToList());
    }

    public void AcceptAll()
    {
        AcceptAllDeletes();

        AcceptMoves(moves.Values);
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

    public ValueTask DisposeAsync()
    {
        Clear();
        return timer.DisposeAsync();
    }
}