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
    /// in turn. A patch is applied to what the ones before it left of its file, so the second of
    /// two for one call site finds the first one's literal there, and a call site that an earlier
    /// one moved is found where it now is.
    /// </para>
    /// <para>
    /// What differs is when the file is written: once, after the last of its patches, through the
    /// same temporary and the same swap, with the file's lock held from the read to the write. One
    /// at a time, the whole file is read, lexed and written again for every patch, and the write
    /// is where the time goes. A file that has just been written is scanned by whatever watches
    /// the drive before the next thing can open it, and for five hundred snapshots in one ten
    /// thousand line file that came to half a minute. The lexing is still once per patch, since
    /// each starts from different source.
    /// </para>
    /// <para>
    /// So a write that fails fails every patch it was carrying, and each says so. The patches
    /// after the first of those say so too, whatever they were judged to be, because what they
    /// were judged against was never written. The file is left as it was.
    /// </para>
    /// </summary>
    public static IReadOnlyList<InlineApplyResult> ApplyAll(IReadOnlyList<InlinePatch> patches) =>
        ApplyAll(patches, Swap);

    /// <param name="patches">The patches, in the order they are to be applied.</param>
    /// <param name="replace">
    /// The swap. Supplied by the tests, which count how many there were and make one fail.
    /// </param>
    internal static IReadOnlyList<InlineApplyResult> ApplyAll(IReadOnlyList<InlinePatch> patches, Action<string, string> replace)
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
            var applied = Run(fullPath, indexes.Select(_ => patches[_]).ToList(), write: true, anchorOnly: false, replace);
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
    static InlineApplyResult[] Run(string fullPath, IReadOnlyList<InlinePatch> patches, bool write, bool anchorOnly, Action<string, string> replace)
    {
        var normalizedPath = fullPath.ToLowerInvariant();
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

                return LockedApply(fullPath, patches, write, anchorOnly, replace);
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

    static InlineApplyResult[] LockedApply(string fullPath, IReadOnlyList<InlinePatch> patches, bool write, bool anchorOnly, Action<string, string> replace)
    {
        // Asked here rather than before the lock, because the swap at the end of this method takes
        // the path away for the instant it takes to rename over it. Asked outside, an applier
        // waiting its turn on a file another one was finishing with saw the file as missing and
        // reported it, which is neither true nor the sort of thing a retry was going to fix
        if (!File.Exists(fullPath))
        {
            return All(patches, InlineApplyResult.Failed($"Source file does not exist: {fullPath}"));
        }

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
        var results = new InlineApplyResult[patches.Count];
        // The first patch whose edit the write below has to carry, or -1 while there is none
        var firstToWrite = -1;
        for (var index = 0; index < results.Length; index++)
        {
            var patch = patches[index];
            PatchStatus status;
            string newSource;
            string failReason;
            try
            {
                status = InlinePatcher.TryApply(
                    language,
                    source,
                    patch.LineHint,
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
                results[index] = InlineApplyResult.Failed($"Failed to patch: {fullPath}", exception);
                continue;
            }

            switch (status)
            {
                case PatchStatus.AlreadyApplied:
                    results[index] = InlineApplyResult.AlreadyApplied;
                    continue;
                case PatchStatus.NotFound:
                    results[index] = InlineApplyResult.NotFound(failReason);
                    continue;
            }

            // Every reason a patch can be refused for has been asked by this point and none of
            // them held. All that remains is the write, which is the one step a dry run may not
            // take
            results[index] = InlineApplyResult.Applied;
            if (!write)
            {
                continue;
            }

            // The next patch is applied to this one's result, as it would have been to the file
            // this one had written
            source = newSource;
            if (firstToWrite < 0)
            {
                firstToWrite = index;
            }
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
            // Nothing from the first edit on reached the file, and every answer after it was about
            // source that held that edit: one already applied only because an earlier patch here
            // had written the same literal, one not found only because an earlier patch had taken
            // its anchor. So they all report the write, and an entry that reports a failure is
            // kept for another try
            var failed = InlineApplyResult.Failed($"Failed to write: {fullPath}", exception);
            for (var index = firstToWrite; index < results.Length; index++)
            {
                results[index] = failed;
            }
        }

        return results;
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
