namespace DiffEngine;

/// <summary>
/// Applies an <see cref="InlinePatch"/> to a source file, preserving the file's
/// encoding, BOM and line endings. Owns all locking (cross process and in process);
/// callers must not add their own.
/// <para>
/// The language is read off the file's extension (see <see cref="SourceLanguage.ForFile"/>), so a
/// patch says which file it edits and nothing has to say which language that file is in.
/// </para>
/// </summary>
public static class InlineApplier
{
    static ConcurrentDictionary<string, object> gates = new(StringComparer.OrdinalIgnoreCase);

    public static InlineApplyResult Apply(InlinePatch patch) =>
        Run(patch, write: true);

    /// <summary>
    /// Applies several patches, reading and writing each source file once however many of them
    /// are for it. The results are in the order the patches were given, whichever files they
    /// name.
    /// <para>
    /// Each outcome is the one <see cref="Apply"/> would have reported had it been called on them
    /// in turn, each asked about the line the edits before it had moved its call site to. A patch
    /// is applied to what the ones before it left of its file, so the second of two for one call
    /// site finds the first one's literal there, and a call site that an earlier one moved is
    /// looked for where it now is: every patch of a batch was recorded against the file as it
    /// was read, and the line is all an Append has to go by.
    /// </para>
    /// <para>
    /// An applied patch says which lines it moved (<see cref="InlineApplyResult.MovedFrom"/>),
    /// counted in the file as the patches before it left it, so the results of a file taken in
    /// order bring any other line of that file along.
    /// </para>
    /// <para>
    /// What differs is when the file is written: once, after the last of its patches, through the
    /// same temporary and the same swap, with the file's lock held from the read to the write. One
    /// at a time, the whole file is read, lexed and written again for every patch, and the write
    /// is where the time goes. A file that has just been written is scanned by whatever watches
    /// the drive before the next thing can open it, and for five hundred snapshots in one ten
    /// thousand line file that came to half a minute. The lexing is once a file too, and around
    /// each edit after that (<see cref="PatchInTurn"/>).
    /// </para>
    /// <para>
    /// So a write that fails fails every patch it was carrying, and each says so. A patch after
    /// the first of those that made no edit was judged against source that was never written, so
    /// it is asked again of the file as it was read, and says the write failed only where it would
    /// have had to edit that. The file is left as it was.
    /// </para>
    /// </summary>
    public static IReadOnlyList<InlineApplyResult> ApplyAll(IReadOnlyList<InlinePatch> patches) =>
        ApplyAll(patches, Swap);

    /// <summary>
    /// <see cref="ApplyAll(IReadOnlyList{InlinePatch})"/> for a caller whose patches can stop
    /// being wanted while they wait: a queue owner's bulk accept, whose queue goes on being
    /// settled and discarded from while a file is read, patched and written.
    /// <para>
    /// A patch is handed over when its file's turn comes and written at the end of it, and the
    /// wait between is the file's lock, up to ten seconds of it. A snapshot discarded in that
    /// time, or settled by a test that started passing, was written with the rest of its file:
    /// the reviewer threw it away and found it in the source. So once a file is patched in
    /// memory, and before its one write, each patch that edited is asked about, by its position
    /// in <paramref name="patches"/>. One that is no longer wanted is not in what is written.
    /// </para>
    /// <para>
    /// Not by taking its edit back out, since the patches after it were applied to source that
    /// held it: the file is patched again from what was read, without it. So every outcome is
    /// what it would have been had that patch never been handed over, the lines each edit moved
    /// included, and is true of the file that is written. The patch itself is
    /// <see cref="InlineApplyStatus.Withdrawn"/>.
    /// </para>
    /// <para>
    /// The question is asked with the file's lock and mutex held, on the thread that applies. It
    /// must not wait on anything that can be waiting to apply to the same file.
    /// </para>
    /// </summary>
    /// <param name="patches">The patches, in the order they are to be applied.</param>
    /// <param name="wanted">
    /// Whether the patch at a position is still to be written. Asked only of a patch that would
    /// edit its file, and at most once.
    /// </param>
    internal static IReadOnlyList<InlineApplyResult> ApplyAll(IReadOnlyList<InlinePatch> patches, Func<int, bool> wanted) =>
        ApplyAll(patches, Swap, wanted);

    /// <param name="patches">The patches, in the order they are to be applied.</param>
    /// <param name="replace">
    /// The swap. Supplied by the tests, which count how many there were and make one fail.
    /// </param>
    /// <param name="wanted">
    /// Whether the patch at a position is still to be written, or null for a caller whose patches
    /// are all wanted.
    /// </param>
    internal static IReadOnlyList<InlineApplyResult> ApplyAll(IReadOnlyList<InlinePatch> patches, Action<string, string> replace, Func<int, bool>? wanted = null)
    {
        var results = new InlineApplyResult[patches.Count];
        // Each file's patches in the order they were given, and the files in the order they were
        // first named
        var files = new List<(string FullPath, List<int> Indexes)>();
        var known = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < patches.Count; index++)
        {
            if (!TryResolve(patches[index], out var fullPath, out var invalid))
            {
                results[index] = invalid;
                continue;
            }

            // Folded as the queue folds a path, so two spellings of a file the file system takes
            // for one are one read and one write here as well
            var key = InlineKey.FoldPath(fullPath);
            if (!known.TryGetValue(key, out var file))
            {
                file = files.Count;
                known.Add(key, file);
                files.Add((fullPath, []));
            }

            files[file].Indexes.Add(index);
        }

        foreach (var (fullPath, indexes) in files)
        {
            // Asked by where a patch is in the file's own list, and answered by where it was in
            // the caller's
            Func<int, bool>? fileWanted = wanted is null ? null : _ => wanted(indexes[_]);
            var applied = Run(fullPath, indexes.Select(_ => patches[_]).ToList(), write: true, anchorOnly: false, replace, fileWanted);
            for (var position = 0; position < indexes.Count; position++)
            {
                results[indexes[position]] = applied[position];
            }
        }

        return results;
    }

    /// <summary>
    /// What <see cref="Apply"/> would report, with nothing written.
    /// <para>
    /// For a producer deciding whether a snapshot can live inline at all. Some call sites cannot
    /// host one - the entry point is reached through a helper of the caller's own, so there is no
    /// SettingsTask to chain onto - and a producer that goes ahead regardless declares the
    /// verification inline, has the append refused at accept time, and by then has already had the
    /// verified file deleted as redundant. Asking first keeps that verification on files.
    /// </para>
    /// <para>
    /// An answer about the file as it is now. The source can still change between this and the
    /// accept, so <see cref="Apply"/> is no less able to refuse; what this rules out is the case
    /// that was never going to work rather than the one that stopped working.
    /// </para>
    /// </summary>
    public static InlineApplyResult CanApply(InlinePatch patch) =>
        Run(patch, write: false);

    /// <summary>
    /// Whether an <see cref="InlinePatchMode.Append"/> has a call site to hang a Snapshot call off
    /// at all, which is the half of <see cref="CanApply"/> a producer can act on before the run is
    /// over. Applied for yes, NotFound for no, and Failed where the source could not be read.
    /// <para>
    /// Apart from CanApply in one way, and only for Append: a call that already has a Snapshot call
    /// chained onto it holding other content answers yes here and is refused there. Both are right.
    /// An accept has nowhere to put the literal it is carrying and says to re-run; a producer
    /// asking whether this call site can host an inline snapshot has its answer, and taking the
    /// verification off inline because another process got there first would be the wrong lesson
    /// to draw. Where the chained call holds this same content there is nothing to tell apart and
    /// both say yes, CanApply as AlreadyApplied.
    /// </para>
    /// </summary>
    public static InlineApplyResult CanAnchor(InlinePatch patch) =>
        Run(patch, write: false, anchorOnly: true);

    static InlineApplyResult Run(InlinePatch patch, bool write, bool anchorOnly = false)
    {
        if (!TryResolve(patch, out var fullPath, out var invalid))
        {
            return invalid;
        }

        return Run(fullPath, [patch], write, anchorOnly, Swap)[0];
    }

    /// <summary>
    /// The file a patch is for, or what is wrong with the patch when it does not name one.
    /// </summary>
    static bool TryResolve(InlinePatch patch, out string fullPath, [NotNullWhen(false)] out InlineApplyResult? invalid)
    {
        fullPath = "";
        invalid = null;
        if (string.IsNullOrWhiteSpace(patch.SourceFile))
        {
            invalid = InlineApplyResult.Failed("InlinePatch.SourceFile is empty");
            return false;
        }

        if (patch.LineHint < 1)
        {
            invalid = InlineApplyResult.Failed($"InlinePatch.LineHint must be 1 or greater. Value: {patch.LineHint}");
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(patch.SourceFile);
        }
        catch (Exception exception)
        {
            invalid = InlineApplyResult.Failed($"Invalid InlinePatch.SourceFile: {patch.SourceFile}", exception);
            return false;
        }

        // Followed before anything else, so the lock, the mutex, the read and the swap all name
        // the file that actually holds the source
        fullPath = ResolveLink(fullPath);
        return true;
    }

    /// <summary>
    /// The patches of one file, applied in order with the file's locks held throughout, and an
    /// outcome for each. A failure that is about the file rather than about a patch - a lock that
    /// could not be taken, a file that could not be read - is every patch's outcome.
    /// </summary>
    static InlineApplyResult[] Run(
        string fullPath,
        IReadOnlyList<InlinePatch> patches,
        bool write,
        bool anchorOnly,
        Action<string, string> replace,
        Func<int, bool>? wanted = null)
    {
        var normalizedPath = fullPath.ToLowerInvariant();
        // A dry run of a file an earlier one read, and nothing has written since, is answered
        // from what was read then, with no lock: there is nothing here for one to protect
        if (!write &&
            Probed.Find(normalizedPath, fullPath) is { } lexed)
        {
            return Judge(lexed, patches, anchorOnly, fullPath);
        }

        lock (gates.GetOrAdd(normalizedPath, static _ => new()))
        {
            // Answered rather than thrown, as everything else here is. A mutex this process may
            // not open - one an elevated applier created for the same file - threw out of Apply,
            // and a viewer's single or group accept let that unwind its loop: the queue was staged
            // and the window vanished mid review.
            Mutex opened;
            try
            {
                opened = OpenMutex(MutexName(normalizedPath));
            }
            catch (Exception exception)
            {
                return All(patches, InlineApplyResult.Failed($"Could not open the inline patch mutex for: {fullPath}", exception));
            }

            using var mutex = opened;
            var owned = false;
            try
            {
                try
                {
                    owned = mutex.WaitOne(TimeSpan.FromSeconds(10));
                }
                catch (AbandonedMutexException)
                {
                    owned = true;
                }

                if (!owned)
                {
                    return All(patches, InlineApplyResult.Failed($"Timed out waiting for the inline patch mutex for: {fullPath}"));
                }

                return LockedApply(fullPath, normalizedPath, patches, write, anchorOnly, replace, wanted);
            }
            finally
            {
                if (owned)
                {
                    mutex.ReleaseMutex();
                }
            }
        }
    }

    static InlineApplyResult[] All(IReadOnlyList<InlinePatch> patches, InlineApplyResult result)
    {
        var results = new InlineApplyResult[patches.Count];
        for (var index = 0; index < results.Length; index++)
        {
            results[index] = result;
        }

        return results;
    }

    static InlineApplyResult[] LockedApply(
        string fullPath,
        string normalizedPath,
        IReadOnlyList<InlinePatch> patches,
        bool write,
        bool anchorOnly,
        Action<string, string> replace,
        Func<int, bool>? wanted)
    {
        // Asked here rather than before the lock, because the swap at the end of this method takes
        // the path away for the instant it takes to rename over it. Asked outside, an applier
        // waiting its turn on a file another one was finishing with saw the file as missing and
        // reported it, which is neither true nor the sort of thing a retry was going to fix
        if (!File.Exists(fullPath))
        {
            return All(patches, InlineApplyResult.Failed($"Source file does not exist: {fullPath}"));
        }

        // What the file looked like before it was read, for a dry run to be kept under
        var stamp = write ? null : Probed.Stamp(fullPath);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception exception)
        {
            return All(patches, InlineApplyResult.Failed($"Failed to read: {fullPath}", exception));
        }

        var (encoding, bomLength) = DetectEncoding(bytes);
        string source;
        try
        {
            source = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        }
        catch (DecoderFallbackException exception)
        {
            return All(
                patches,
                InlineApplyResult.Failed(
                    $"Could not decode as {encoding.WebName}: {fullPath}. Every byte that failed to decode would be replaced on write, so the file is left alone. Convert it to UTF-8 and re-run the test.",
                    exception));
        }
        catch (Exception exception)
        {
            return All(patches, InlineApplyResult.Failed($"Failed to decode: {fullPath}", exception));
        }

        var language = SourceLanguage.ForFile(fullPath);
        if (!write)
        {
            SourceScan lexed;
            try
            {
                lexed = language.Scan(source);
            }
            catch (Exception exception)
            {
                return All(patches, InlineApplyResult.Failed($"Failed to patch: {fullPath}", exception));
            }

            var judged = Judge(lexed, patches, anchorOnly, fullPath);
            if (stamp is null ||
                !Probed.Keep(normalizedPath, fullPath, stamp.Value, lexed))
            {
                lexed.Dispose();
            }

            return judged;
        }

        var results = new InlineApplyResult[patches.Count];
        var read = source;
        var firstToWrite = PatchInTurn(language, ref source, patches, fullPath, results);
        if (wanted is not null &&
            firstToWrite >= 0)
        {
            // As late as there is: everything from here to the write is this thread's own work
            firstToWrite = WithoutTheUnwanted(language, read, ref source, patches, fullPath, results, wanted, firstToWrite);
        }

        if (firstToWrite < 0)
        {
            return results;
        }

        try
        {
            var content = encoding.GetBytes(source);
            byte[] output;
            if (bomLength > 0)
            {
                var preamble = encoding.GetPreamble();
                output = new byte[preamble.Length + content.Length];
                Buffer.BlockCopy(preamble, 0, output, 0, preamble.Length);
                Buffer.BlockCopy(content, 0, output, preamble.Length, content.Length);
            }
            else
            {
                output = content;
            }

            WriteThroughTemporary(fullPath, output, replace);
        }
        catch (Exception exception)
        {
            AfterAFailedWrite(
                language,
                read,
                patches,
                fullPath,
                results,
                firstToWrite,
                InlineApplyResult.Failed($"Failed to write: {fullPath}", exception));
        }

        return results;
    }

    /// <summary>
    /// What each patch from the first edit on is told when the one write failed.
    /// <para>
    /// Nothing from that edit on reached the file, and every answer after it was about source
    /// that held it: one already applied only because an earlier patch here had written the same
    /// literal, one not found only because an earlier patch had taken its anchor. So a patch that
    /// edited reports the write, and an entry that reports a failure is kept for another try.
    /// </para>
    /// <para>
    /// One that made no edit is asked again, of the file as it was read, which is the file as it
    /// still is. Already applied there, or not found there, is true whatever became of the write
    /// and is what it is told: they all used to report the write, so a snapshot that was in the
    /// source all along stayed queued as a failure, and one whose call site had gone was not
    /// told to re-run. Where it would have had to edit that file, it needed the write as much as
    /// the others, and reports it.
    /// </para>
    /// </summary>
    static void AfterAFailedWrite(
        SourceLanguage language,
        string read,
        IReadOnlyList<InlinePatch> patches,
        string fullPath,
        InlineApplyResult[] results,
        int firstToWrite,
        InlineApplyResult failed)
    {
        SourceScan? scan = null;
        try
        {
            for (var index = firstToWrite; index < results.Length; index++)
            {
                // Taken back before the write was tried, and no more in the file for its failing
                if (results[index].Status == InlineApplyStatus.Withdrawn)
                {
                    continue;
                }

                if (results[index].Status == InlineApplyStatus.Applied)
                {
                    results[index] = failed;
                    continue;
                }

                scan ??= language.Scan(read);
                var asRead = Judge(scan, patches[index], false, fullPath, out _);
                results[index] = asRead.Status == InlineApplyStatus.Applied ? failed : asRead;
            }
        }
        catch (Exception)
        {
            // The source could not be lexed again, which leaves the write as all there is to say
            for (var index = firstToWrite; index < results.Length; index++)
            {
                results[index] = failed;
            }
        }
        finally
        {
            scan?.Dispose();
        }
    }

    /// <summary>
    /// Asks, of each patch whose edit the write is about to carry, whether it is still wanted,
    /// and leaves the source and the outcomes as they would be had the ones that are not never
    /// been handed over. Returns the first patch whose edit a write has still to carry, or -1
    /// when none is left.
    /// <para>
    /// The patches are applied again, from the source as it was read, without the unwanted ones.
    /// An edit cannot be taken back out on its own: each patch after it was judged against source
    /// that held it, at a line it had moved, and may have been already applied only because of
    /// it, or not found only because it had taken the anchor. Applied again, each is told what is
    /// true of the file that will be written, and says which lines it moved in that file.
    /// </para>
    /// <para>
    /// Which can make an edit of a patch that had made none, the second of two for one call site
    /// when the first is taken back. That one has not been asked about, so it is asked, and the
    /// file patched once more if it is unwanted too. Each patch is asked once, so this ends.
    /// </para>
    /// </summary>
    /// <param name="language">The language the source is in.</param>
    /// <param name="read">The source as it was read.</param>
    /// <param name="source">The patched source, replaced when a patch is taken back out.</param>
    /// <param name="patches">The patches, in the order they are to be applied.</param>
    /// <param name="fullPath">The file, for a failure to name.</param>
    /// <param name="results">The outcomes, replaced when a patch is taken back out.</param>
    /// <param name="wanted">Whether the patch at a position is still to be written.</param>
    /// <param name="firstToWrite">The first patch whose edit the write has to carry.</param>
    static int WithoutTheUnwanted(
        SourceLanguage language,
        string read,
        ref string source,
        IReadOnlyList<InlinePatch> patches,
        string fullPath,
        InlineApplyResult[] results,
        Func<int, bool> wanted,
        int firstToWrite)
    {
        var asked = new bool[results.Length];
        var withdrawn = new bool[results.Length];
        while (firstToWrite >= 0)
        {
            var found = false;
            for (var index = firstToWrite; index < results.Length; index++)
            {
                if (asked[index] ||
                    results[index].Status != InlineApplyStatus.Applied)
                {
                    continue;
                }

                asked[index] = true;
                if (!IsWanted(wanted, index))
                {
                    withdrawn[index] = true;
                    found = true;
                }
            }

            if (!found)
            {
                break;
            }

            var kept = new List<int>(results.Length);
            for (var index = 0; index < results.Length; index++)
            {
                if (withdrawn[index])
                {
                    results[index] = InlineApplyResult.Withdrawn;
                }
                else
                {
                    kept.Add(index);
                }
            }

            var keptPatches = new InlinePatch[kept.Count];
            for (var position = 0; position < keptPatches.Length; position++)
            {
                keptPatches[position] = patches[kept[position]];
            }

            var keptResults = new InlineApplyResult[kept.Count];
            source = read;
            var first = PatchInTurn(language, ref source, keptPatches, fullPath, keptResults);
            for (var position = 0; position < keptResults.Length; position++)
            {
                results[kept[position]] = keptResults[position];
            }

            firstToWrite = first < 0 ? -1 : kept[first];
        }

        return firstToWrite;
    }

    /// <summary>
    /// A question that throws is taken as yes. The patch was handed over to be written, and
    /// nothing has said otherwise; thrown on from here, the outcomes of the patches beside it
    /// would be lost with it.
    /// </summary>
    static bool IsWanted(Func<int, bool> wanted, int index)
    {
        try
        {
            return wanted(index);
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// Applies the patches of one file to its source in memory, each to what the one before it
    /// left, and says what became of each. Returns the first patch whose edit a write has to
    /// carry, or -1 when none made one.
    /// <para>
    /// The source is lexed once, for the first patch, and the scan is carried from each patch
    /// that edits to the next (<see cref="SourceScan.Edited"/>), which lexes only around the
    /// edit. Lexed whole for every patch, five hundred snapshots in a 600 KB file were most of a
    /// second of patching around their one write. What each patch is told is what it would have
    /// been told over a scan of the whole text, since the scan it is given is that scan.
    /// </para>
    /// </summary>
    /// <param name="language">The language the source is in.</param>
    /// <param name="source">The source, replaced by each patch that edits.</param>
    /// <param name="patches">The patches, in the order they are to be applied.</param>
    /// <param name="fullPath">The file, for a failure to name.</param>
    /// <param name="results">Filled with an outcome for each patch.</param>
    internal static int PatchInTurn(
        SourceLanguage language,
        ref string source,
        IReadOnlyList<InlinePatch> patches,
        string fullPath,
        InlineApplyResult[] results)
    {
        // The first patch whose edit the write has to carry, or -1 while there is none
        var firstToWrite = -1;
        SourceScan? scan = null;
        try
        {
            for (var index = 0; index < results.Length; index++)
            {
                try
                {
                    scan ??= language.Scan(source);
                }
                catch (Exception exception)
                {
                    results[index] = InlineApplyResult.Failed($"Failed to patch: {fullPath}", exception);
                    continue;
                }

                // Every patch here was recorded against the file as it was read, and each edit
                // above a call site moves it. So a patch is asked about the line its own is on
                // now. Asked about the recorded one, the second snapshot of a file was looked
                // for by a line that had just stopped being its, and an Append whose line names
                // no call is refused where its member has more than one to choose from
                var line = patches[index].LineHint;
                for (var earlier = firstToWrite; earlier >= 0 && earlier < index; earlier++)
                {
                    line = results[earlier].Rebase(line);
                }

                results[index] = Judge(scan, patches[index], false, fullPath, out var newSource, line);
                if (results[index].Status != InlineApplyStatus.Applied)
                {
                    continue;
                }

                var (from, by) = scan.LinesMoved(newSource);
                results[index] = InlineApplyResult.AppliedMoving(from, by);

                // The next patch is applied to this one's result, as it would have been to the
                // file this one had written
                source = newSource;
                if (firstToWrite < 0)
                {
                    firstToWrite = index;
                }

                var previous = scan;
                scan = null;
                try
                {
                    if (index + 1 < results.Length)
                    {
                        scan = previous.Edited(newSource);
                    }
                }
                catch (Exception)
                {
                    // Left for the next patch to lex whole, which is what it used to do. A scan
                    // that could not be carried is no reason to fail a patch that has applied
                }
                finally
                {
                    previous.Dispose();
                }
            }
        }
        finally
        {
            scan?.Dispose();
        }

        return firstToWrite;
    }

    /// <summary>
    /// What the patcher makes of one patch, over source that has been lexed, as the outcome a
    /// caller is given. <paramref name="newSource"/> is the edited source where the outcome is
    /// Applied, which is all a dry run wanted to know and what a write has still to carry out.
    /// </summary>
    /// <param name="scan">The source, lexed.</param>
    /// <param name="patch">The patch asked about.</param>
    /// <param name="anchorOnly">Whether only a call site to hang a Snapshot call off is asked for.</param>
    /// <param name="fullPath">The file, for a failure to name.</param>
    /// <param name="newSource">The edited source, where the outcome is Applied.</param>
    /// <param name="line">
    /// The line the call site is taken to be on in this source, where edits made since the patch
    /// was recorded have moved it. The patch's own when not given.
    /// </param>
    static InlineApplyResult Judge(SourceScan scan, InlinePatch patch, bool anchorOnly, string fullPath, out string newSource, int? line = null)
    {
        PatchStatus status;
        string failReason;
        try
        {
            status = InlinePatcher.TryApply(
                scan,
                line ?? patch.LineHint,
                patch.Mode,
                patch.OriginalExpression,
                patch.OriginalValue,
                patch.MemberName,
                patch.EntryPoints,
                anchorOnly,
                SourceLanguage.NormalizeNewlines(patch.NewContent),
                out newSource,
                out failReason);
        }
        catch (Exception exception)
        {
            // A patcher defect on some shape of source, reported against the file it met it in
            // rather than thrown at whichever surface was accepting
            newSource = "";
            return InlineApplyResult.Failed($"Failed to patch: {fullPath}", exception);
        }

        // Every reason a patch can be refused for has been asked by this point. For one that
        // none of them held for, all that remains is the write, which is the one step a dry run
        // may not take
        return status switch
        {
            PatchStatus.AlreadyApplied => InlineApplyResult.AlreadyApplied,
            PatchStatus.NotFound => InlineApplyResult.NotFound(failReason),
            _ => InlineApplyResult.Applied
        };
    }

    /// <summary>
    /// A dry run: every patch asked of the same source, which none of them changes.
    /// </summary>
    static InlineApplyResult[] Judge(SourceScan scan, IReadOnlyList<InlinePatch> patches, bool anchorOnly, string fullPath)
    {
        var results = new InlineApplyResult[patches.Count];
        for (var index = 0; index < results.Length; index++)
        {
            results[index] = Judge(scan, patches[index], anchorOnly, fullPath, out _);
        }

        return results;
    }

    /// <summary>
    /// The source files a dry run has read, lexed, for the next dry run of the same file.
    /// <para>
    /// A test run asks <see cref="CanAnchor"/> once for each call site it has not seen before,
    /// and each asking read, decoded and lexed the whole file: 0.7 s for the five hundred call
    /// sites of a 600 KB file, to be told five hundred things about the same text. Nothing is
    /// written by a dry run, so what one read stands for as long as the file does, and the file's
    /// length and write time say whether it has.
    /// </para>
    /// <para>
    /// A write time says nothing about a second write inside the same tick of the file system's
    /// clock, which is two seconds on some. So only a file that had been left alone for longer
    /// than that when it was read is kept: any write after the read then has a later time. A
    /// file being edited is read each time, as it was.
    /// </para>
    /// <para>
    /// A few files, since a run goes through its source files a class at a time and tests run
    /// side by side. A scan that is dropped is left to the collector and not disposed, because
    /// another thread may be part way through asking it something.
    /// </para>
    /// </summary>
    static class Probed
    {
        const int capacity = 4;

        static readonly TimeSpan settled = TimeSpan.FromSeconds(3);

        static readonly List<Entry> entries = [];

        static long uses;

        sealed class Entry(string path, (long Length, DateTime Written) stamp, SourceScan scan)
        {
            public string Path => path;
            public (long Length, DateTime Written) Stamp => stamp;
            public SourceScan Scan => scan;
            public long Used;
        }

        /// <summary>
        /// What tells one state of a file from the next, or null for a file that cannot be asked.
        /// </summary>
        public static (long Length, DateTime Written)? Stamp(string fullPath)
        {
            try
            {
                var info = new FileInfo(fullPath);
                if (!info.Exists)
                {
                    return null;
                }

                return (info.Length, info.LastWriteTimeUtc);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// The scan kept for a file, where the file is still as it was when that was read.
        /// </summary>
        public static SourceScan? Find(string normalizedPath, string fullPath)
        {
            Entry? entry;
            lock (entries)
            {
                entry = entries.Find(_ => _.Path == normalizedPath);
            }

            if (entry is null)
            {
                return null;
            }

            // Asked outside the lock, since it is the file system that answers
            var stamp = Stamp(fullPath);
            lock (entries)
            {
                if (stamp is null ||
                    stamp.Value != entry.Stamp)
                {
                    entries.Remove(entry);
                    return null;
                }

                entry.Used = ++uses;
                return entry.Scan;
            }
        }

        /// <summary>
        /// Keeps a scan of a file that was read while it looked like <paramref name="stamp"/>.
        /// False when it is not kept, and is still the caller's to dispose.
        /// </summary>
        public static bool Keep(string normalizedPath, string fullPath, (long Length, DateTime Written) stamp, SourceScan scan)
        {
            // Written too recently to tell a later write by its time, written since it was read,
            // or dated in the future, which the same test refuses
            if (DateTime.UtcNow - stamp.Written < settled ||
                Stamp(fullPath) is not { } after ||
                after != stamp)
            {
                return false;
            }

            // Asked for here, so nothing about the scan changes once other threads can see it
            _ = scan.Eol;
            _ = scan.IndentUnit;
            lock (entries)
            {
                entries.RemoveAll(_ => _.Path == normalizedPath);
                if (entries.Count >= capacity)
                {
                    var oldest = 0;
                    for (var index = 1; index < entries.Count; index++)
                    {
                        if (entries[index].Used < entries[oldest].Used)
                        {
                            oldest = index;
                        }
                    }

                    entries.RemoveAt(oldest);
                }

                entries.Add(new(normalizedPath, stamp, scan) { Used = ++uses });
            }

            return true;
        }
    }

    /// <summary>
    /// Writes the patched source through a temporary file beside it and swaps that in, so the file
    /// on disk is either what it was or what the patch made it and never half of either.
    /// <para>
    /// Writing in place truncates the file first and fills it back in, which leaves a window where
    /// a killed process or a full disk costs the caller the rest of their source file. The mutex
    /// above keeps two appliers apart but says nothing about a process that stops partway. Every
    /// other care taken here - strict decoders, the BOM round trip, refusing a file that will not
    /// decode - is because the whole file is rewritten rather than the patched span, and the write
    /// itself was the step that could still lose it.
    /// </para>
    /// <para>
    /// The temporary is a sibling so the swap stays on one volume, where it is a rename rather
    /// than a copy. Replace rather than a move that overwrites, because it keeps the attributes
    /// the destination already had, and because the overwriting move does not exist on every
    /// framework this targets.
    /// </para>
    /// </summary>
    /// <summary>
    /// The file a symlinked source points at, which is the file to patch.
    /// <para>
    /// The whole file is rewritten through a temporary and swapped in, and on Linux and macOS that
    /// swap is a rename: it replaces the link itself with a regular file, leaving the target still
    /// holding the old literal and the link no longer a link. Following it first puts the patch on
    /// the real file, and gives two links to one file the same lock into the bargain.
    /// </para>
    /// <para>
    /// The final target rather than one hop, since a chain has the same problem, and the path as
    /// it stands when nothing resolves: a broken link is a file that cannot be read, which the
    /// read reports better than this could.
    /// </para>
    /// </summary>
    static string ResolveLink(string path)
    {
#if NET6_0_OR_GREATER
        try
        {
            return File.ResolveLinkTarget(path, true)?.FullName ?? path;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return path;
        }
#else
#pragma warning disable IDE0022
        return path;
#pragma warning restore IDE0022
#endif
    }

#if NET7_0_OR_GREATER
    /// <summary>
    /// The destination's Unix permissions onto the temporary, because the swap is a rename and the
    /// file that survives it is the temporary - created with this process's umask. A source file
    /// that was executable, or group writable, or anything else out of the ordinary, came back as
    /// whatever the umask happened to say. Windows keeps the destination's ACLs across a Replace,
    /// so there is nothing to carry there.
    /// <para>
    /// Only exists from net7, which is where SetUnixFileMode arrives. Below that there is nothing
    /// to carry on any OS, so the call site is compiled out with it rather than calling an empty
    /// method.
    /// </para>
    /// </summary>
    static void CopyMode(string destination, string temporary)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(temporary, File.GetUnixFileMode(destination));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort. The content is the point, and a mode that could not be read or set is
            // not worth failing a patch that otherwise applied.
        }
    }
#endif

    // What replace is for every caller but a test
    static void Swap(string temporary, string destination) =>
        File.Replace(temporary, destination, null);

    /// <param name="fullPath">The source file, which the patched bytes replace.</param>
    /// <param name="output">The whole patched file, preamble included.</param>
    /// <param name="replace">
    /// The swap. Supplied by the tests, because the failure it has to survive is one ReplaceFile
    /// produces on its own schedule - an antivirus or sync client holding the file it was just
    /// handed - and cannot be arranged on demand.
    /// </param>
    internal static void WriteThroughTemporary(string fullPath, byte[] output, Action<string, string> replace)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        // Named after the file it replaces, so anything left by a process that died between the
        // write and the swap says what it was for. The extension keeps it out of a *.cs glob
        var temporary = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, output);
#if NET7_0_OR_GREATER
            CopyMode(fullPath, temporary);
#endif
            try
            {
                replace(temporary, fullPath);
            }
            catch (Exception exception)
                when (!File.Exists(fullPath) &&
                      File.Exists(temporary))
            {
                // ReplaceFile can fail after it has already taken the destination away: with no
                // backup name, ERROR_UNABLE_TO_MOVE_REPLACEMENT means the original no longer
                // exists and the replacement is still under its temporary name. The temporary is
                // then the only copy of the source anywhere, and the finally below used to delete
                // it. It is the whole patched file, so finishing the swap by hand is the write
                // having happened.
                MoveIntoPlace(temporary, fullPath, exception);
            }
        }
        finally
        {
            // Replace consumed it. Anything still there is this method's litter, and failing an
            // applied patch over a temporary file that could not be deleted helps nobody - unless
            // the destination is gone, when it is the source file and is left for the reader of
            // the failure to find
            try
            {
                if (File.Exists(temporary) &&
                    File.Exists(fullPath))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    static void MoveIntoPlace(string temporary, string fullPath, Exception replaceFailure)
    {
        try
        {
            File.Move(temporary, fullPath);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // Named, because this is the one failure a person has to act on: the file they edit is
            // not where it was, and this is where it went
            throw new IOException(
                $"Replacing {fullPath} failed after the original had been removed, and the patched source could not be moved back into place. It is in {temporary}.",
                new AggregateException(replaceFailure, exception));
        }
    }

    /// <summary>
    /// The encoding to read and write the file with. Every one of them throws rather than
    /// substituting: the applier rewrites the whole file, not just the patched span, so a
    /// replacement character for an undecodable byte is not a local defect but a file wide one.
    /// A source file that is not what its BOM says, or is not UTF-8 when it has no BOM, has to
    /// fail loudly and stay as it was.
    /// </summary>
    static (Encoding encoding, int bomLength) DetectEncoding(byte[] bytes)
    {
        if (bytes is [0xFF, 0xFE, 0x00, 0x00, ..])
        {
            return (new UTF32Encoding(false, true, true), 4);
        }

        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            return (new UTF8Encoding(true, true), 3);
        }

        if (bytes is [0xFF, 0xFE, ..])
        {
            return (new UnicodeEncoding(false, true, true), 2);
        }

        if (bytes is [0xFE, 0xFF, ..])
        {
            return (new UnicodeEncoding(true, true, true), 2);
        }

        return (new UTF8Encoding(false, true), 0);
    }

    /// <summary>
    /// Machine wide off Windows. A name with no prefix is session scoped, and on Linux and macOS a
    /// session is a POSIX session - every terminal has its own - so an IDE applying a staged patch
    /// and a viewer started from a terminal's test run each held a mutex of their own, both
    /// rewrote the file, and one literal was lost. On Windows the session is the logon session,
    /// which every process involved already shares. Falls back to the session scoped name where
    /// the global namespace cannot be used.
    /// </summary>
    static Mutex OpenMutex(string name)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new(false, name);
        }

        try
        {
            return new(false, $@"Global\{name}");
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or IOException)
        {
            return new(false, name);
        }
    }

    static string MutexName(string normalizedPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        var builder = new StringBuilder("DiffEngineInline_");
        foreach (var b in hash)
        {
            builder.Append(b.ToString("X2"));
        }

        return builder.ToString();
    }
}
