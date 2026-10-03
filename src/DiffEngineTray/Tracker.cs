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
    // The last listing seen, used for the icon state and for the menu. Free when this tray owns
    // the queue, and a loopback round trip when a viewer does, which is why nothing re-reads it
    // from a click.
    IReadOnlyList<PendingSnapshot> snapshots = [];
    AsyncTimer timer;
    int lastScanCount;

    public Tracker(Action active, Action inactive, LockedFilesResolver? lockedFilesResolver = null, Action<TrackedMove>? acceptFailed = null, Action<string>? inlineFailed = null, IInlineHost? inline = null, Action<string>? scanFailing = null)
    {
        this.active = active;
        this.inactive = inactive;
        this.lockedFilesResolver = lockedFilesResolver;
        this.acceptFailed = acceptFailed;
        this.inlineFailed = inlineFailed;
        this.scanFailing = scanFailing;
        this.inline = inline ?? new RemoteInlineHost();
        timer = new(Scan, TimeSpan.FromSeconds(2));

        // Seeded rather than left empty until the first scan two seconds later. The menu reads
        // this cache now, so without it a tray that has just started shows none of what a viewer
        // already had queued - and the icon stays dark for the same two seconds.
        Refresh();
    }

    /// <summary>
    /// How many scans have to fail one after the other before anybody is told. One that fails
    /// alone is a file that went between two lines of it, and is put right by the next.
    /// </summary>
    internal const int ScanFailuresBeforeTelling = 3;

    Action<string>? scanFailing;
    int failedScans;

    /// <summary>
    /// One scan, and what is made of it failing.
    /// <para>
    /// Logged, every time. This is the timer's own thread, and the handler everything else uses
    /// follows the log with a modal box: no scan ran again until somebody answered it, and a scan
    /// is nothing anybody asked for, so the box arrived out of nowhere.
    /// </para>
    /// <para>
    /// The log alone left a tray whose every scan failed looking like one with nothing wrong: the
    /// icon and the menu stopped following the files, and nothing said so. So a run of failures
    /// is said once, in a balloon, when it has gone on long enough not to be a passing one. Once
    /// for the run, rather than for each scan in it, which would be a balloon every two seconds
    /// for as long as the cause stood. A scan that works ends the run, and the next run is told
    /// afresh.
    /// </para>
    /// </summary>
    internal async Task Scan(Cancel cancel)
    {
        try
        {
            await ScanFiles(cancel);
            Interlocked.Exchange(ref failedScans, 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to scan files");
            // Counted across threads, since a test runs scans beside the timer's
            if (Interlocked.Increment(ref failedScans) == ScanFailuresBeforeTelling)
            {
                scanFailing?.Invoke(ScanFailingMessage(exception));
            }
        }
    }

    internal static string ScanFailingMessage(Exception exception) =>
        $"The pending files could not be checked, {ScanFailuresBeforeTelling} times running, so the menu and the icon may be behind what is on disk. {exception.Message} Every failure is in the log: 'Open logs' in the menu.";

    internal Task ScanFiles(Cancel cancel)
    {
        foreach (var delete in deletes.ToList()
                     .Where(delete => !File.Exists(delete.Value.File)))
        {
            deletes.TryRemove(delete.Key, out _);
        }

        // What a pair was last found to be is worth keeping only while the pair is tracked. Here,
        // once a scan, rather than wherever a move leaves: there are a score of such places, and
        // a scan that is still comparing a move as it leaves writes its entry after any of them
        // had run. Left alone, a tray that stays up for weeks kept an entry for every received
        // file it had ever found different.
        foreach (var temp in differing.Keys)
        {
            if (!moves.ContainsKey(temp))
            {
                differing.TryRemove(temp, out _);
            }
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

    internal async Task HandleScanMove(KeyValuePair<string, TrackedMove> pair)
    {
        // The move this scan looked at, and no other. Everything below is about that one, and a
        // re-run can replace it while the two files are being compared: taken out by key alone,
        // the move that went was the fresh one, which nothing had found equal to anything, and the
        // tool just opened for it was ended. A move that was replaced is left to the next scan.
        void RemoveAndKill()
        {
            if (moves.TryRemove(pair))
            {
                KillProcesses(pair.Value);
                Release(pair.Value);
            }
        }

        var move = pair.Value;
        if (!File.Exists(move.Temp))
        {
            RemoveAndKill();
            return;
        }

        if (!File.Exists(move.Target))
        {
            return;
        }
        // A pair found different, and untouched since, is still different. The scan runs every two
        // seconds and read both files through again each time, which for a few large same-size
        // pairs - bitmaps, fixed-size data - was hundreds of megabytes a scan for as long as they
        // stayed pending.
        var stamp = Stamp(move);
        if (stamp is not null &&
            differing.TryGetValue(move.Temp, out var known) &&
            known == stamp)
        {
            return;
        }

        try
        {
            if (!await FileComparer.FilesAreEqual(move.Temp, move.Target))
            {
                if (stamp is not null)
                {
                    differing[move.Temp] = stamp.Value;
                }

                return;
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // File is missing, locked by a diff tool or a running test, or not this account's to
            // read. Skip this scan round
            return;
        }

        RemoveAndKill();
    }

    readonly ConcurrentDictionary<string, (long, DateTime, long, DateTime)> differing = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How many pairs are remembered as found different, for the tests.
    /// </summary>
    internal int KnownDiffering => differing.Count;

    static (long, DateTime, long, DateTime)? Stamp(TrackedMove move)
    {
        try
        {
            var temp = new FileInfo(move.Temp);
            var target = new FileInfo(move.Target);
            return (temp.Length, temp.LastWriteTimeUtc, target.Length, target.LastWriteTimeUtc);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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

        // A move onto a file is a run that verified against it, so a delete an earlier run raised
        // for that file no longer describes a stale one. DiffRunner.SettleDelete says the same
        // thing, but not from a library that predates it, not while a viewer holds the queue the
        // settle is sent to, and not while that port is remembered as unowned. The delete stayed,
        // and "Accept all" moved the received file into place and then deleted it
        if (deletes.TryRemove(target, out _))
        {
            Log.Information("DeleteWithdrawn. A move now targets the file. File:{file}", target);
        }

        return moves.AddOrUpdate(
            temp,
            addValueFactory: temp =>
            {
                Process? process = null;
                if (processId != null)
                {
                    ProcessEx.TryGetTool(processId.Value, exe, out process);
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
                    // Taken off the move it is disposed on, so nothing still holding that move -
                    // a menu built before this update - reaches a disposed process through it
                    existing.Process?.Dispose();
                    existing.Process = null;
                    // Against the tool the pair was tracked with when this move names none, as
                    // Retarget keeps that one
                    ProcessEx.TryGetTool(processId.Value, exe ?? existing.Exe, out process);
                }

                var move = exe == null
                    ? Retarget(existing, target, process)
                    : BuildTrackedMove(temp, exe, arguments, canKill, target, process);

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

    /// <summary>
    /// A move that arrives for a pair already tracked and names no tool: what it says is the
    /// target, and everything recorded about the tool stays as it was.
    /// <para>
    /// A move with no tool is one over the viewer port, which carries the two paths and nothing
    /// else, or one from a run that launched nothing. Neither says anything about the tool the
    /// pair was first tracked with, and filling the gap from the extension, as a move seen for the
    /// first time has to, replaced that tool with this tray's own choice. "Open diff tool" on a
    /// viewer pair is how it happened by hand: the viewer it starts cannot bind the port and
    /// forwards the pair here as a Diff. The pair then read as another tool's with no window, so
    /// "Accept open" passed over it while it was on screen, and it had become killable.
    /// </para>
    /// </summary>
    static TrackedMove Retarget(TrackedMove existing, string target, Process? process) =>
        new(
            existing.Temp,
            target,
            existing.Exe,
            existing.Arguments,
            existing.CanKill,
            process,
            SolutionDirectoryFinder.Find(target),
            Path.GetExtension(target).TrimStart('.'),
            existing.KillLockingProcess,
            existing.IsViewer);

    static TrackedMove BuildTrackedMove(string temp, string? exe, string? arguments, bool? canKill, string target, Process? process)
    {
        var solution = SolutionDirectoryFinder.Find(target);
        var extension = Path.GetExtension(target).TrimStart('.');
        var killLockingProcess = false;
        if (exe == null)
        {
            if (DiffTools.TryFindByExtension(extension, out var tool))
            {
                // Through DiffEngine's own answer rather than straight off the definition, because
                // the viewer's declared arguments still name two paths and running those opens a
                // window of its own for a pair whose queue is already on screen.
                (arguments, var killable) = PendingFiles.RelaunchFor(tool, temp, target);
                canKill = killable;
                exe = tool.ExePath;
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

        // Off the resolved executable rather than the resolved tool, because the sender's viewer
        // and this tray's are different copies at different paths, so the path lookup above finds
        // nothing for the one case that matters most here.
        return new(
            temp,
            target,
            exe,
            arguments,
            canKill.GetValueOrDefault(false),
            process,
            solution,
            extension,
            killLockingProcess,
            PendingFiles.IsViewerExe(exe));
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
                if (!TryAcceptOne(snapshot, out var message))
                {
                    inlineFailed?.Invoke(CouldNotAccept(snapshot.Name, message));
                }

                Refresh();
            }
            catch (Exception exception)
            {
                ExceptionHandler.Handle($"Failed to accept the snapshot for '{snapshot.Name}'", exception);
            }
        });

    /// <summary>
    /// One accept, and what it meant. Both the single and the bulk path have to agree on this, and
    /// they used to reach it through a switch each - which is how they came to disagree about
    /// Unknown, one skipping it and the other calling it a failed accept.
    /// <para>
    /// False is a failure the caller reports its own way: named, for a click on one snapshot, and
    /// counted, for a click that swept a group.
    /// </para>
    /// </summary>
    bool TryAcceptOne(PendingSnapshot snapshot, out string? message)
    {
        var outcome = inline.Accept(snapshot, out message);
        switch (outcome)
        {
            case AcceptOutcome.Applied:
                Log.Information("Inline snapshot accepted for `{Name}`. {Message}", snapshot.Name, message);
                return true;
            case AcceptOutcome.Unknown:
                // No entry left to accept: it settled, or another surface got to it first. The
                // menu is built from the last scan, so an item outliving its entry is ordinary
                // rather than a failure, and saying so names a snapshot already in the source
                Log.Information("Inline snapshot for `{Name}` was no longer pending.", snapshot.Name);
                return true;
            case AcceptOutcome.Stale:
                // Gone, but not accepted. Reported rather than logged, because the snapshot
                // vanishing from the menu otherwise reads as success.
                Log.Warning("Inline snapshot stale for `{Name}`: {Message}", snapshot.Name, message);
                return false;
            default:
                Log.Warning("Inline snapshot accept failed for `{Name}`: {Message}", snapshot.Name, message);
                return false;
        }
    }

    // The owner does not always have something to add, and a balloon ending in a bare full stop
    // and a space reads as a message that went missing
    static string CouldNotAccept(string name, string? message)
    {
        if (message is { Length: > 0 })
        {
            return $"Could not accept the snapshot for '{name}'. {message}";
        }

        return $"Could not accept the snapshot for '{name}'.";
    }

    /// <summary>
    /// On a worker, matching <see cref="Accept(PendingSnapshot)"/> and for the same reason. Against
    /// a queue another process owns this is two socket round trips - the discard, then the listing
    /// <see cref="Refresh"/> reads - and it was running both on the thread that had just handled
    /// the menu click, which is the one drawing everything.
    /// </summary>
    public Task Discard(PendingSnapshot snapshot) =>
        Task.Run(() =>
        {
            try
            {
                if (!inline.Discard(snapshot, out var message))
                {
                    inlineFailed?.Invoke($"Could not discard the snapshot for '{snapshot.Name}'. {message}");
                }

                Refresh();
            }
            catch (Exception exception)
            {
                ExceptionHandler.Handle($"Failed to discard the snapshot for '{snapshot.Name}'", exception);
            }
        });

    public Task AcceptAllSnapshots() =>
        Task.Run(() =>
        {
            try
            {
                SweepSnapshots(out var failure);
                if (failure is not null)
                {
                    inlineFailed?.Invoke(failure);
                }

                Refresh();
            }
            catch (Exception exception)
            {
                ExceptionHandler.Handle("Failed to accept the pending snapshots", exception);
            }
        });

    /// <summary>
    /// The second half of an accept-all: the snapshots, then the deletes, on a worker for the
    /// reason <see cref="Accept(PendingSnapshot)"/> gives.
    /// <para>
    /// Deletes after the snapshots, and not at all when one of those was not written. A snapshot
    /// moving inline arrives as a patch plus a delete of the verified file it replaces, and nothing
    /// ties the two together. Deleting first, which is what this used to do, removed that file
    /// before finding out the patch would be refused, so the snapshot was in neither place: not in
    /// the source, and not on disk. The viewer's own accept-all has always held its deletes this
    /// way, for the same reason.
    /// </para>
    /// </summary>
    /// <param name="pending">
    /// The deletes that were pending when the accept-all began, which are the only ones it carries
    /// out: see <see cref="AcceptOpen"/>.
    /// </param>
    /// <param name="written">
    /// The files the moves accepted ahead of this were moved onto, which no delete here may remove.
    /// </param>
    Task AcceptSnapshotsThenDeletes(List<TrackedDelete> pending, HashSet<string> written) =>
        Task.Run(() =>
        {
            try
            {
                if (!SweepSnapshots(out var failure))
                {
                    // Said, as the deletes held for a snapshot are below. Only the log knew, so
                    // the menu went on listing a delete "Accept all" had just been pressed over
                    // with nothing to say it had been left on purpose
                    var kept = AcceptDeletes(pending, written);
                    if (kept.Count > 0)
                    {
                        failure = failure is null ? DeletesKept(kept) : $"{failure} {DeletesKept(kept)}";
                    }
                }
                else if (pending.Any(_ => deletes.ContainsKey(_.File)))
                {
                    failure = failure is null ? DeletesHeld : $"{failure} {DeletesHeld}";
                }

                if (failure is not null)
                {
                    inlineFailed?.Invoke(failure);
                }

                Refresh();
            }
            catch (Exception exception)
            {
                ExceptionHandler.Handle("Failed to accept the pending snapshots", exception);
            }
        });

    /// <summary>
    /// What a user is told about the deletes an accept-all left pending, from either surface.
    /// </summary>
    public const string DeletesHeld = "Pending deletes were kept, since a snapshot in this batch was not written and a file being deleted may be the only copy of it left. Accept them on their own to delete them anyway.";

    /// <summary>
    /// Accepts every pending snapshot, and returns whether one it tried was not written, or the
    /// queue could not be asked whether it holds any.
    /// </summary>
    /// <param name="failure">What to tell the user, when something is still pending afterwards.</param>
    bool SweepSnapshots(out string? failure)
    {
        failure = null;
        // Live read, not the scan cache: this can be called before the first scan, and acting on
        // a stale empty cache would silently do nothing. Inside the worker rather than in front of
        // it, because the caller is a menu click or a hot key and the read is a round trip
        // whenever a viewer owns the queue.
        if (!inline.TryList(out var pending))
        {
            // Something holds the queue and did not say what is in it, which is not the queue
            // being empty. Reported the way a snapshot that was not written is, because the same
            // thing waits on the answer: a patch held over there may be the one a delete here
            // belongs to, and reading silence as nothing pending carried that delete out
            failure = "Could not accept the pending snapshots. The snapshot viewer did not answer.";
            return true;
        }

        if (pending.Count == 0)
        {
            return false;
        }

        if (!inline.AcceptAll(out var message, out var refused))
        {
            failure = $"Could not accept the pending snapshots. {message}";
        }

        return refused;
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
                    if (!TryAcceptOne(snapshot, out var message))
                    {
                        failures.Add(message ?? snapshot.Name);
                    }
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
                // Raised again, so by a run that looked at the file as it is now. Whatever a move
                // wrote there since the delete was first raised, this is the later statement
                existing.Written = false;
                return existing;
            });

    /// <summary>
    /// Why an accept-all leaves a delete pending, where it would: null when it would carry it
    /// out. For the menu and the debug view, which say it beside the delete, and for the sweeps,
    /// which act on it.
    /// </summary>
    public string? HeldReason(TrackedDelete delete)
    {
        if (delete.Written)
        {
            return WroteItsFile;
        }

        if (moves.Values.Any(_ => string.Equals(_.Target, delete.File, StringComparison.OrdinalIgnoreCase)))
        {
            return AwaitsItsFile;
        }

        return null;
    }

    /// <summary>
    /// A move was accepted onto the file while its delete was pending.
    /// <para>
    /// <see cref="AddMove"/> withdraws the delete a move finds waiting for its target, so the two
    /// are only pending together when the delete arrived second, and which of them is the stale
    /// one is then not knowable from here. The move is the snapshot arriving and the delete is the
    /// last copy leaving, so the move goes ahead and the delete waits to be accepted on its own.
    /// </para>
    /// <para>
    /// Remembered on the delete, rather than only for the sweep that wrote the file. The sweep
    /// held the delete and forgot why as it ended, so the delete sat in the menu looking like any
    /// other, and a second "Accept all" deleted the snapshot the first had just accepted. The same
    /// went for a move accepted on its own and an accept-all after it.
    /// </para>
    /// </summary>
    public const string WroteItsFile = "Kept by 'Accept all': a move was accepted onto this file after the delete was raised, so deleting it would remove what was just accepted. Accept the delete on its own to delete the file anyway, or run the tests again.";

    /// <summary>
    /// A move still pending is going to write the file. Not remembered: it is true for as long as
    /// the move is there, and stops being true if the move is discarded.
    /// </summary>
    public const string AwaitsItsFile = "Kept by 'Accept all': a pending move is still to be accepted onto this file. Accept the delete on its own to delete the file anyway.";

    void MarkWritten(string target)
    {
        if (deletes.TryGetValue(target, out var delete))
        {
            delete.Written = true;
        }
    }

    /// <summary>
    /// Through <see cref="AcceptTracked(TrackedDelete)"/>, which is what the wire path has always
    /// used: it catches, re-tracks so the delete can be retried, and reports why.
    /// <para>
    /// These called File.Delete straight, so a read-only or open verified file threw out of a menu
    /// click or a hot key - onto the UI thread, where nothing hooks Application.ThreadException -
    /// and the entry was already untracked by then, so the pending delete was lost with it.
    /// </para>
    /// </summary>
    public void Accept(TrackedDelete delete)
    {
        var (ok, message) = AcceptTracked(delete);
        if (!ok &&
            message != null)
        {
            Log.Error("{Message}", message);
        }
    }

    public void Accept(IEnumerable<TrackedDelete> toAccept)
    {
        foreach (var delete in toAccept.ToList())
        {
            Accept(delete);
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

        // The files this batch moved a received file onto, for the deletes swept after it: see
        // WrittenOrAwaited.
        public readonly HashSet<string> Written = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <returns>The files a received file was moved onto.</returns>
    HashSet<string> AcceptMoves(IEnumerable<TrackedMove> toAccept)
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

        return batch.Written;
    }

    void AcceptMove(TrackedMove move, AcceptBatch batch)
    {
        if (!moves.TryRemove(move.Temp, out var removed))
        {
            return;
        }

        if (InnerMove(removed, batch))
        {
            Release(removed);
            return;
        }

        // Keep the move pending so accepting can be retried
        Restore(removed);
    }

    /// <summary>
    /// Puts back a move that was taken out to be accepted and could not be. One that arrived for
    /// the same received file meanwhile stands, and this one has then left for good.
    /// </summary>
    void Restore(TrackedMove removed)
    {
        if (!moves.TryAdd(removed.Temp, removed))
        {
            Release(removed);
        }

        Interlocked.Increment(ref restores);
    }

    /// <summary>
    /// How many times something taken out to be accepted has been put back, which is the one
    /// change to what is tracked that <see cref="ITrackedFiles.Version"/> cannot see by looking:
    /// the same object in the same place as before it left.
    /// </summary>
    long restores;

    /// <summary>
    /// Lets go of the process a move was tracked with, without ending it, once the move has left
    /// for good.
    /// <para>
    /// <see cref="ProcessEx.TryGet"/> holds a handle on every process a move
    /// names, and DiffRunner names one for an MDI tool too. Only <see cref="KillProcesses"/>
    /// disposed any, and it passes over a move that cannot be killed, so each of those kept a
    /// handle, and with it a process id Windows could not hand out again, until a finaliser ran.
    /// </para>
    /// </summary>
    static void Release(TrackedMove move)
    {
        move.Process?.Dispose();
        move.Process = null;
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
                batch.Written.Add(move.Target);
                MarkWritten(move.Target);
                DeleteTempDirectory(move);
                return true;
            }

            // Nothing waiting will change these, and every retry is another 400ms of a frozen
            // menu - an accept-all in a read-only workspace sat through all eight for every move
            if (CannotEverMove(move))
            {
                Log.Warning("Could not accept `{Name}`: the target is read-only or its directory is missing. Kept pending", move.Name);
                acceptFailed?.Invoke(move);
                return false;
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

    static bool CannotEverMove(TrackedMove move)
    {
        try
        {
            var directory = Path.GetDirectoryName(move.Target);
            if (directory is not null &&
                !Directory.Exists(directory))
            {
                return true;
            }

            return File.Exists(move.Target) &&
                   new FileInfo(move.Target).IsReadOnly;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    bool ShouldKill(TrackedMove move, LockedFiles locked, AcceptBatch batch)
    {
        // The user's standing answer, read here rather than only by the resolver: an accept that
        // arrives from the viewer never prompts, so it never reached the resolver, and was refused
        // as locked with "accept from the tray menu" - where the same accept killed without asking.
        if (move.KillLockingProcess ||
            batch.KillWithoutPrompt ||
            LockedFilesHandler.AlwaysKill)
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
        Release(move);

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

        // The move can come back: a locked target, the user picking Ignore, or the retries running
        // out all re-add this same object. Leaving a disposed Process on it made the Accept-open
        // hot key and "Open diff tool" throw "No process is associated with this object" on the UI
        // thread, where nothing catches it
        move.Process = null;
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
    /// <para>
    /// All of it on a worker, as an accept-all's second half is. The snapshots for the reason
    /// <see cref="Discard(PendingSnapshot)"/> gives: a queue a viewer owns is asked over a socket,
    /// and one slow to answer held the thread drawing everything for as long as that took. The
    /// tracked files because discarding a move ends its diff tool and waits up to half a second
    /// for each to go, which for a screen full of them was seconds of a tray that drew nothing.
    /// The files first, so a queue that is slow to answer holds up nothing but itself. The menu
    /// and the hot key discard the task; tests await it.
    /// </para>
    /// </summary>
    public Task Clear() =>
        Task.Run(() =>
        {
            try
            {
                DiscardFiles();

                // Only forget the cached snapshots when the owner actually discarded them. It used
                // to be cleared regardless, so a discard the owner never received still emptied the
                // menu - and everything came back on the next scan two seconds later
                if (inline.DiscardAll(out var message))
                {
                    snapshots = [];
                }
                else
                {
                    // Said, and not only logged: the snapshots are still in the menu, under the
                    // button that was just pressed to be rid of them
                    var failure = CouldNotDiscard(message);
                    Log.Error("{Message}", failure);
                    inlineFailed?.Invoke(failure);
                }

                // Nothing waits for the next scan to say so: the files went above, whatever the
                // queue answered
                ToggleActive();
            }
            catch (Exception exception)
            {
                ExceptionHandler.Handle("Failed to discard everything pending", exception);
            }
        });

    /// <summary>
    /// What a user is told when "Discard (n)" could not discard the snapshots, which stay pending.
    /// </summary>
    internal static string CouldNotDiscard(string? message)
    {
        if (message is { Length: > 0 })
        {
            return $"Could not discard the pending snapshots. {message}";
        }

        return "Could not discard the pending snapshots.";
    }

    /// <summary>
    /// Every tracked move and delete discarded, for <see cref="Clear"/> and for the wire. Virtual
    /// so a test can see which thread it was asked of.
    /// </summary>
    protected virtual int DiscardFiles()
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

    /// <summary>
    /// The moves here, on the calling thread, because a locked one can prompt. The returned task
    /// covers the rest - the snapshots, then the deletes, which have to wait for them - and runs
    /// on a worker for the reason <see cref="Accept(PendingSnapshot)"/> gives. The menu and the
    /// hot keys discard it; tests await it so what the other surface should now be showing is
    /// settled rather than in flight.
    /// <para>
    /// The deletes are listed here, before anything is accepted, and only those are carried out. A
    /// snapshot moving inline can land while a batch is applying. Whoever owns the queue took its
    /// snapshots as the sweep began, so that patch is not in the batch, and the deletes were read
    /// when their turn came, so its delete was: the verified file went while the patch replacing
    /// it was only pending. Ahead of the snapshots rather than beside them, because Verify raises
    /// the delete and then queues the patch, so a delete listed this early has a patch that was
    /// there to be taken unless the batch began between the two.
    /// </para>
    /// </summary>
    public Task AcceptOpen()
    {
        var pending = deletes.Values.ToList();
        var written = AcceptMoves(
            moves.Values
                .Where(_ => _.IsOpen)
                .ToList());

        // Every pending snapshot is open by definition: the viewer only stays running while it
        // has something to show.
        return AcceptSnapshotsThenDeletes(pending, written);
    }

    /// <inheritdoc cref="AcceptOpen"/>
    public Task AcceptAll()
    {
        var pending = deletes.Values.ToList();
        var written = AcceptMoves(moves.Values);

        return AcceptSnapshotsThenDeletes(pending, written);
    }

    /// <returns>
    /// The deletes left pending because of a move onto their file, for the caller to say so.
    /// </returns>
    List<TrackedDelete> AcceptDeletes(List<TrackedDelete> pending, HashSet<string> written)
    {
        var kept = new List<TrackedDelete>();
        // One at a time, and no Clear afterwards: a delete that fails re-tracks itself, and
        // clearing would throw that away. Unguarded, the first bad one also took the rest of the
        // sweep with it, so "Accept all" stopped at the first read-only file
        foreach (var delete in pending)
        {
            // Settled, withdrawn or accepted on its own since the batch began
            if (!deletes.ContainsKey(delete.File))
            {
                continue;
            }

            if (WrittenOrAwaited(delete, written))
            {
                Log.Information("Kept the pending delete of `{Name}`: a move wrote that file, or is still pending onto it", delete.Name);
                kept.Add(delete);
                continue;
            }

            Accept(delete);
        }

        return kept;
    }

    /// <summary>
    /// What a user is told about deletes an accept-all kept because of a move onto their file.
    /// The menu says the same beside each of them, for whoever missed the balloon.
    /// </summary>
    internal static string DeletesKept(IReadOnlyList<TrackedDelete> kept)
    {
        var which = kept.Count == 1
            ? $"The pending delete of '{kept[0].Name}' was kept"
            : $"{kept.Count} pending deletes were kept";
        return $"{which}, since a move was accepted onto the same file, or is still to be, and deleting it would remove what the move put there. Accept a delete on its own to delete its file anyway.";
    }

    /// <summary>
    /// Whether a sweep must leave this delete pending, because the file it would remove is one a
    /// move in the same sweep has just written or one a move still pending is going to write.
    /// <para>
    /// Carried out, it removed the received file a moment after that file had been moved into
    /// place, and neither was left: see <see cref="WroteItsFile"/>, which is also why a delete
    /// whose file an earlier accept wrote is held by every sweep after it.
    /// </para>
    /// <para>
    /// By what was written rather than by what was swept. A move whose received file has gone is
    /// dropped without writing anything, and the delete beside that one is the newer statement.
    /// </para>
    /// </summary>
    bool WrittenOrAwaited(TrackedDelete delete, HashSet<string> written) =>
        written.Contains(delete.File) ||
        HeldReason(delete) is not null;

    public ICollection<TrackedDelete> Deletes => deletes.Values;

    public ICollection<TrackedMove> Moves => moves.Values;

    /// <summary>
    /// The move for a received file as it is now, or null once it has gone.
    /// </summary>
    public TrackedMove? FindMove(string temp) =>
        moves.GetValueOrDefault(temp);

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

    readonly Lock versionGate = new();
    readonly List<object> versioned = [];
    long versionedRestores;
    long version;

    /// <summary>
    /// By which objects are tracked, compared with the ones tracked the last time this was asked.
    /// <para>
    /// Everything a listing carries of a move or a delete is fixed when the object is made, and a
    /// change to either is another object in its place, so the same objects are the same listing.
    /// Walking the two dictionaries and comparing references allocates nothing but the walk, where
    /// describing every entry to hash the descriptions was a megabyte for a couple of hundred of
    /// them. A dictionary nothing has touched is walked in the same order each time. One that was
    /// touched and put back as it was may not be, which reads as a change and costs a listing.
    /// </para>
    /// <para>
    /// Rather than a count of changes, kept wherever the dictionaries are written: there are a
    /// score of such places, and one missed is a viewer that goes on showing a file that left.
    /// </para>
    /// <para>
    /// Except for the one change that leaves the same objects behind it. An accept takes its move
    /// out for as long as the move takes, seconds when a file is locked, and puts the same object
    /// back when it could not be carried out. The owner takes its tag and then builds its listing,
    /// so a listing built in that gap goes out without the move, under a tag taken while it was
    /// there, and once the move was back nothing here looked different from when the tag was
    /// taken: the viewer was told "unchanged" about a listing missing a pending file. Those are
    /// two places, <see cref="Restore"/> and the delete that could not be deleted, and they are
    /// counted.
    /// </para>
    /// </summary>
    long ITrackedFiles.Version()
    {
        lock (versionGate)
        {
            var restored = Interlocked.Read(ref restores);
            if (restored == versionedRestores &&
                Unchanged())
            {
                return version;
            }

            versionedRestores = restored;
            versioned.Clear();
            foreach (var move in moves)
            {
                versioned.Add(move.Value);
            }

            foreach (var delete in deletes)
            {
                versioned.Add(delete.Value);
            }

            return ++version;
        }
    }

    bool Unchanged()
    {
        var index = 0;
        foreach (var move in moves)
        {
            if (index == versioned.Count ||
                !ReferenceEquals(versioned[index], move.Value))
            {
                return false;
            }

            index++;
        }

        foreach (var delete in deletes)
        {
            if (index == versioned.Count ||
                !ReferenceEquals(versioned[index], delete.Value))
            {
                return false;
            }

            index++;
        }

        return index == versioned.Count;
    }

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

    bool ITrackedFiles.Untrack(string key)
    {
        if (TrackedKeys.TryStrip(key, TrackedKeys.MovePrefix, out var temp))
        {
            if (!moves.TryRemove(temp, out var removed))
            {
                return false;
            }

            Release(removed);
            return true;
        }

        return TrackedKeys.TryStrip(key, TrackedKeys.DeletePrefix, out var file) &&
               deletes.TryRemove(file, out _);
    }

    (bool ok, string? message) ITrackedFiles.Accept(string key)
    {
        if (TrackedKeys.TryStrip(key, TrackedKeys.MovePrefix, out var temp))
        {
            if (moves.TryGetValue(temp, out var move))
            {
                return AcceptWithoutPrompting(move);
            }
        }
        else if (TrackedKeys.TryStrip(key, TrackedKeys.DeletePrefix, out var file))
        {
            if (deletes.TryGetValue(file, out var delete))
            {
                return AcceptTracked(delete);
            }
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

    (int accepted, int kept) ITrackedFiles.AcceptAll(IReadOnlyCollection<string> deleteKeys, bool holdDeletes, Action? advanced)
    {
        var accepted = 0;
        var kept = 0;
        // One batch for the whole sweep, so the deletes below know what its moves wrote
        var batch = new AcceptBatch
        {
            NeverPrompt = true
        };
        foreach (var move in moves.Values.ToList())
        {
            if (AcceptWithoutPrompting(move, batch).ok)
            {
                accepted++;
            }
            else
            {
                kept++;
            }

            advanced?.Invoke();
        }

        foreach (var key in deleteKeys)
        {
            // Settled, withdrawn or accepted on its own since the batch began. Nothing to carry
            // out, and one fewer to wait for
            if (!TrackedKeys.TryStrip(key, TrackedKeys.DeletePrefix, out var file) ||
                !deletes.TryGetValue(file, out var delete))
            {
                advanced?.Invoke();
                continue;
            }

            // Held rather than tried, and left tracked, so it can still be accepted on its own by
            // anyone who knows the file is redundant
            if (!holdDeletes &&
                !WrittenOrAwaited(delete, batch.Written) &&
                AcceptTracked(delete).ok)
            {
                accepted++;
            }
            else
            {
                kept++;
            }

            advanced?.Invoke();
        }

        return (accepted, kept);
    }

    int ITrackedFiles.DiscardAll() =>
        DiscardFiles();

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
            Interlocked.Increment(ref restores);
            return (false, $"Could not delete {removed.Name}. {exception.Message}");
        }

        return (true, $"Deleted {removed.Name}");
    }

    (bool ok, string? message) AcceptWithoutPrompting(TrackedMove move) =>
        AcceptWithoutPrompting(
            move,
            new()
            {
                NeverPrompt = true
            });

    (bool ok, string? message) AcceptWithoutPrompting(TrackedMove move, AcceptBatch batch)
    {
        if (!moves.TryRemove(move.Temp, out var removed))
        {
            return (false, null);
        }

        if (InnerMove(removed, batch))
        {
            Release(removed);
            return (true, $"Accepted {removed.Name}");
        }

        Restore(removed);
        return (false, $"Files for '{removed.Name}' are locked. Accept from the tray menu to resolve.");
    }

    /// <summary>
    /// The last listing seen, rather than a fresh one.
    /// <para>
    /// This is what the menu is built from, and building it runs on the UI thread inside
    /// <c>ContextMenuStrip.Opening</c>. Reading live there put a loopback round trip between the
    /// right click and the menu whenever a viewer owned the queue. Worse, a connection to a port
    /// nothing is listening on is only refused at once on some machines - where the SYN is dropped
    /// instead, an owner that had exited cost the whole of
    /// <see cref="ViewerClient.ShortTimeout"/>, so every menu open took half a second for the rest
    /// of the tray's life.
    /// </para>
    /// <para>
    /// Nothing is lost where the queue is held here: <see cref="OwnedInlineHost.Changed"/> runs
    /// <see cref="Refresh"/> on every mutation, and the tray's own accepts and discards refresh
    /// too, so the cache is the live queue. Where a viewer holds it, the listing is at most one
    /// scan old - which is what <see cref="TrackingAny"/> and the icon have always shown.
    /// </para>
    /// </summary>
    public IReadOnlyList<PendingSnapshot> Snapshots => snapshots;

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
            Release(move);
        }

        moves.Clear();
        deletes.Clear();
        snapshots = [];
        return timer.DisposeAsync();
    }
}
