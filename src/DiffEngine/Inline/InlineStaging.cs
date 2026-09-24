namespace DiffEngine;

/// <summary>
/// Writes pending inline snapshots back to disk, in the staging layout Verify uses when no queue
/// owner answers a test run: the patch and its two texts under a <c>VerifyInline</c> directory in
/// the source project's <c>obj</c>.
/// <para>
/// For a queue owner exiting with entries still pending. The queue lives in the owner's memory,
/// so exiting used to discard every pending snapshot silently — and unlike a file snapshot, whose
/// received file stays on disk when its diff tool closes, an inline snapshot had nothing left
/// anywhere. Written out, the pending entries degrade to exactly the arrangement a run with no
/// owner leaves behind, which accept tooling already knows how to find, review and apply.
/// </para>
/// <para>
/// Verify itself stages under the project's intermediate directory, which only the test process
/// knows. An exiting owner has just the patch, so the nearest project directory to the source
/// file stands in: tooling locates staged snapshots by the <c>VerifyInline</c> directory name,
/// not by the path above it.
/// </para>
/// </summary>
public static class InlineStaging
{
    public const string DirectoryName = "VerifyInline";

    /// <summary>
    /// Persists every entry, one file trio per variant so a conflicted entry keeps each
    /// framework's content. Returns how many trios were written.
    /// <para>
    /// Best effort, per variant: the caller is exiting, so an entry whose project directory
    /// cannot be found, or whose write fails, is skipped rather than taking the rest down with
    /// it. Deterministic names, so persisting the same call site again overwrites rather than
    /// accumulates.
    /// </para>
    /// </summary>
    public static int Persist(IEnumerable<PendingInline> pending)
    {
        var written = 0;
        foreach (var entry in pending)
        {
            foreach (var variant in entry.Variants)
            {
                if (TryPersist(variant.Patch, variant.Origins))
                {
                    written++;
                }
            }
        }

        return written;
    }

    // The 20.4.0 signature, kept for binary compatibility with callers compiled against it.
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static int Clear(string sourceFile, int line, string? memberName, string? extraDirectory, string? origin) =>
        Clear(sourceFile, line, memberName, extraDirectory, origin, null);

    /// <summary>
    /// Deletes the staged files for a call site, for a run that has just settled or retired it.
    /// Returns how many trios were cleared.
    /// </summary>
    /// <remarks>
    /// Settling only ever spoke to the queue owner, which says nothing to a snapshot that is on
    /// disk rather than in a queue — so a staged snapshot outlived the run that made it stale, and
    /// accept tooling went on offering it for a test that now passes. Rare while staging was only
    /// the no-owner fallback, and the ordinary case once <see cref="Persist" /> made every owner
    /// exit write one.
    /// <para>
    /// <paramref name="memberName" /> is the same fallback the queue uses, for a call site whose
    /// line has moved, under the same rule: only where the member names exactly one call site,
    /// since dropping the wrong one discards a snapshot that is still pending. Several files can
    /// share a line — one per framework — so it is call sites that are counted, not files.
    /// </para>
    /// </remarks>
    /// <param name="sourceFile">The source file the settled call site is in.</param>
    /// <param name="line">The line the call site was recorded at.</param>
    /// <param name="memberName">The member the call site is in, used where the line has moved.</param>
    /// <param name="extraDirectory">
    /// A staging root to clear beside the source project's, for a producer that stages somewhere
    /// of its own. Verify's own fallback writes under the project's intermediate directory, which
    /// is normally inside that <c>obj</c> and found anyway, but does not have to be.
    /// </param>
    /// <param name="origin">
    /// The framework moniker of the run that settled, which scopes the clear to that run's own
    /// trio. This is what <see cref="InlineQueue.Settle(string, string?)" /> already does with a
    /// queue, and staging is the same situation with the same answer: one call site stages one
    /// trio per framework, so a multi-framework run where one framework starts passing and another
    /// does not would otherwise have the passing one delete the failing one's staged snapshot.
    /// <para>
    /// Null clears every framework's trio for the call site, which is right for a retire - the
    /// call site is not an inline snapshot in any framework any more - and is the behaviour of
    /// every caller that does not pass this.
    /// </para>
    /// <para>
    /// A trio staged without a framework label is cleared either way, since there is nothing to
    /// scope it by and leaving it would strand it forever.
    /// </para>
    /// </param>
    /// <param name="value">
    /// What the settling call's expected argument holds. Narrows the <paramref name="memberName" />
    /// fallback to the call site it settles, as <see cref="InlineQueue.Settle(string, string?, string?, string?)" />
    /// does, since a member holding one staged call site is not one call site: the passing sibling
    /// beside a failing call used to clear the failing one's trio. Null keeps the member alone.
    /// </param>
    public static int Clear(string sourceFile, int line, string? memberName, string? extraDirectory = null, string? origin = null, string? value = null)
    {
        var cleared = 0;
        foreach (var directory in StagingDirectories(sourceFile, extraDirectory))
        {
            cleared += ClearIn(directory, sourceFile, line, memberName, origin, value);
        }

        return cleared;
    }

    static int ClearIn(string directory, string sourceFile, int line, string? memberName, string? origin, string? value)
    {
        var staged = ReadStaged(directory)
            .Where(_ => SamePath(_.Patch.SourceFile, sourceFile))
            .ToList();
        if (staged.Count == 0)
        {
            return 0;
        }

        var matching = staged
            .Where(_ => _.Patch.LineHint == line)
            .ToList();

        if (matching.Count == 0 &&
            !string.IsNullOrEmpty(memberName))
        {
            var byMember = staged
                .Where(_ => _.Patch.MemberName == memberName &&
                            (value is null || _.Patch.IsSettledBy(value)))
                .ToList();
            if (IsOneCallSite(byMember))
            {
                matching = byMember;
            }
        }

        if (origin != null)
        {
            // One call site stages one trio per framework, so a settle that names the framework it
            // came from must take only that one. Without it a net9 run that started passing
            // deleted net8's still-failing trio beside its own, and that snapshot was then pending
            // nowhere: not staged, and not in a queue either, since nothing had one
            matching = matching
                .Where(_ => _.Patch.Framework == null || _.Patch.Framework == origin)
                .ToList();
        }

        var cleared = 0;
        foreach (var entry in matching)
        {
            if (DeleteTrio(entry.PatchPath))
            {
                cleared++;
            }
        }

        return cleared;
    }

    /// <summary>
    /// Whether these all name one call site, which is what makes a member unambiguous: the member
    /// holds a single inline snapshot, and the files are its frameworks. More than one line is
    /// more than one snapshot in that member, and nothing here can say which of them settled.
    /// </summary>
    static bool IsOneCallSite(List<(string PatchPath, InlinePatch Patch)> staged)
    {
        if (staged.Count == 0)
        {
            return false;
        }

        var line = staged[0].Patch.LineHint;
        return staged.All(_ => _.Patch.LineHint == line);
    }

    /// <summary>
    /// Read again only when the directory has changed since. <see cref="Clear(string, int, string?, string?, string?, string?)" /> runs once per
    /// verification and used to read and parse every staged patch each time, so a run with a few
    /// hundred staged was reading them a few hundred times over. Creating or deleting a trio is
    /// what changes a directory's write time, and overwriting one keeps the name - which is derived
    /// from the call site and framework that matching reads - so what is cached still matches.
    /// </summary>
    static List<(string PatchPath, InlinePatch Patch)> ReadStaged(string directory)
    {
        DateTime written;
        try
        {
            written = Directory.GetLastWriteTimeUtc(directory);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (stagedCache.TryGetValue(directory, out var cached) &&
            cached.Written == written)
        {
            return cached.Patches;
        }

        var patches = ReadStagedFiles(directory);
        stagedCache[directory] = (written, patches);
        return patches;
    }

    static readonly ConcurrentDictionary<string, (DateTime Written, List<(string PatchPath, InlinePatch Patch)> Patches)> stagedCache =
        new(StringComparer.OrdinalIgnoreCase);

    static List<(string PatchPath, InlinePatch Patch)> ReadStagedFiles(string directory)
    {
        var result = new List<(string, InlinePatch)>();
        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.inlinepatch");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return result;
        }

        foreach (var file in files)
        {
            if (InlinePatchFile.TryRead(file, out var patch))
            {
                result.Add((file, patch));
            }
        }

        return result;
    }

    /// <summary>
    /// The patch and the two texts beside it, which all share a name. A file that cannot be
    /// deleted leaves the snapshot pending rather than half cleared, so the whole trio is judged
    /// by the patch: with it gone nothing reads the other two.
    /// </summary>
    static bool DeleteTrio(string patchPath)
    {
        var directory = Path.GetDirectoryName(patchPath);
        var stem = Path.GetFileNameWithoutExtension(patchPath);
        if (directory is null)
        {
            return false;
        }

        if (!Delete(patchPath))
        {
            return false;
        }

        Delete(Path.Combine(directory, $"{stem}.received.txt"));
        Delete(Path.Combine(directory, $"{stem}.expected.txt"));
        return true;
    }

    static bool Delete(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return true;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Everywhere a snapshot for this source could have been staged: whatever the caller names,
    /// and every <c>VerifyInline</c> under the source project's <c>obj</c> — which covers both
    /// where <see cref="Persist" /> writes and where a test run's own fallback does.
    /// </summary>
    static IEnumerable<string> StagingDirectories(string sourceFile, string? extraDirectory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(extraDirectory))
        {
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            var named = Path.Combine(extraDirectory!, DirectoryName);
            if (Directory.Exists(named) &&
                seen.Add(named))
            {
                yield return named;
            }
        }

        var project = FindProjectDirectory(sourceFile);
        if (project is null)
        {
            yield break;
        }

        var obj = Path.Combine(project, "obj");
        if (!Directory.Exists(obj))
        {
            yield break;
        }

        foreach (var directory in FindStaging(obj, 0))
        {
            if (seen.Add(directory))
            {
                yield return directory;
            }
        }
    }

    // An intermediate directory sits a handful of levels below the project it belongs to, so the
    // walk is bounded rather than open ended, the same way ReceivedMaps bounds its own.
    const int maxDepth = 8;

    static IEnumerable<string> FindStaging(string root, int depth)
    {
        if (depth > maxDepth)
        {
            yield break;
        }

        string[] children;
        try
        {
            children = Directory.GetDirectories(root);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (string.Equals(Path.GetFileName(child), DirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                // Staged files are flat inside, so there is no need to descend further.
                yield return child;
                continue;
            }

            foreach (var nested in FindStaging(child, depth + 1))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// Through <see cref="InlineKey.For" />, so two spellings of one path are judged the same way
    /// the queue judges them, and on the platforms where that matters.
    /// </summary>
    static bool SamePath(string left, string right) =>
        InlineKey.For(left, 0) == InlineKey.For(right, 0);

    static bool TryPersist(InlinePatch patch, IReadOnlyList<string> origins)
    {
        // A Remove is applied by whoever produced it and is never reviewed, so there is nothing to
        // hand to a reviewer. Queues refuse them on arrival; checked anyway, since this writes.
        if (patch.Mode == InlinePatchMode.Remove)
        {
            return false;
        }

        try
        {
            // A patch whose source file has gone can never apply — the literal is located by
            // content search inside that file — so there is nothing worth staging for it. This is
            // also what stops the project walk below from wandering up from a path that was never
            // real on this machine.
            if (!File.Exists(patch.SourceFile))
            {
                return false;
            }

            var project = FindProjectDirectory(patch.SourceFile);
            if (project is null)
            {
                return false;
            }

            var directory = Path.Combine(project, "obj", DirectoryName);
            Directory.CreateDirectory(directory);

            // The variant's first origin over the patch's own framework: a variant two frameworks
            // merged into carries both labels while the patch keeps its birth framework, and the
            // label is what a reader shows.
            var origin = origins.Count > 0 ? origins[0] : patch.Framework;
            var baseName = BuildName(patch, origin, directory);

            var encoding = new UTF8Encoding(false);
            File.WriteAllText(
                Path.Combine(directory, $"{baseName}.inlinepatch"),
                InlinePatchFile.Build(patch, origin),
                encoding);
            File.WriteAllText(
                Path.Combine(directory, $"{baseName}.received.txt"),
                patch.NewContent,
                encoding);
            File.WriteAllText(
                Path.Combine(directory, $"{baseName}.expected.txt"),
                patch.OriginalValue ?? "",
                encoding);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                      or UnauthorizedAccessException
                      or ArgumentException
                      or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Mirrors the shape Verify stages under: test name, a call site hash so re-persisting
    /// overwrites, and the framework last, which is the segment conflict labels are read from.
    /// The framework's dots become underscores for the same reason Verify writes DotNet10_0
    /// rather than a versioned moniker: the last dot has to be the one before it.
    /// <para>
    /// The test name is cut to fit. A file name component is 255 characters on NTFS and 255 bytes
    /// on ext4 and APFS, and a long F# sentence name with the rest of this added went past it: the
    /// write threw, <see cref="TryPersist" /> took that for a file it could not write, and the
    /// snapshot was staged nowhere. Cut by UTF-8 bytes, which is the tighter of the two limits for
    /// the same name. The call site hash keeps two names that are cut to the same prefix apart.
    /// </para>
    /// <para>
    /// On .NET Framework the whole path is cut to fit as well. It holds a path to MAX_PATH unless
    /// the machine has opted in to long paths, which most have not, and a name that fits its
    /// component can still take the path past 260 characters under a deep project.
    /// </para>
    /// </summary>
    // ReSharper disable once UnusedParameter.Local
    static string BuildName(InlinePatch patch, string? origin, string directory)
    {
        var test = Sanitize(patch.TestName) ?? Path.GetFileNameWithoutExtension(patch.SourceFile);
        var runtime = Sanitize(origin)?.Replace('.', '_') ?? "unknown";
        var rest = $".{Hash($"{patch.SourceFile}:{patch.LineHint}")}.{runtime}";
        var bytes = maxComponentBytes - Encoding.UTF8.GetByteCount(rest + longestExtension);
#if NETFRAMEWORK
        // One for the separator the name is joined to the directory with
        var characters = maxFrameworkPath - directory.Length - 1 - rest.Length - longestExtension.Length;
#else
        var characters = int.MaxValue;
#endif
        return Truncate(test, bytes, characters) + rest;
    }

    const int maxComponentBytes = 255;

#if NETFRAMEWORK
    /// <summary>
    /// MAX_PATH less the terminating null.
    /// </summary>
    const int maxFrameworkPath = 259;
#endif

    /// <summary>
    /// The longest of the three extensions a staged entry is written with.
    /// </summary>
    const string longestExtension = ".received.txt";

    /// <summary>
    /// The longest prefix of <paramref name="value" /> that is at most <paramref name="bytes" /> in
    /// UTF-8 and <paramref name="characters" /> long, never splitting a surrogate pair.
    /// </summary>
    static string Truncate(string value, int bytes, int characters)
    {
        var used = 0;
        var index = 0;
        while (index < value.Length)
        {
            var length = char.IsHighSurrogate(value[index]) &&
                         index + 1 < value.Length &&
                         char.IsLowSurrogate(value[index + 1])
                ? 2
                : 1;
            var size = Encoding.UTF8.GetByteCount(value.ToCharArray(index, length));
            if (used + size > bytes ||
                index + length > characters)
            {
                break;
            }

            used += size;
            index += length;
        }

        return value.Substring(0, index);
    }

    /// <summary>
    /// Answers for source files already asked about. <see cref="Clear(string, int, string?, string?, string?, string?)" /> runs once per
    /// verification, and the walk below is a directory enumeration per level of the path — paid
    /// on every one of them, including the overwhelmingly common case where nothing is staged and
    /// the answer is thrown away.
    /// <para>
    /// Only this half is cached. Which project a source file belongs to cannot change while a run
    /// is going, whereas the staging directories under it can: a run that finds no queue owner
    /// creates one as it goes, and a cached "nothing here" would then miss what it wrote.
    /// </para>
    /// </summary>
    static readonly ConcurrentDictionary<string, string?> projectDirectories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The nearest directory above the source file holding a project file, whose <c>obj</c> is
    /// where a test run for that source would stage. Null when there is none, which is a source
    /// path from another machine or a file that has gone along with its project.
    /// </summary>
    static string? FindProjectDirectory(string sourceFile) =>
        projectDirectories.GetOrAdd(sourceFile, WalkToProjectDirectory);

    static string? WalkToProjectDirectory(string sourceFile)
    {
        var directory = Path.GetDirectoryName(sourceFile);
        while (!string.IsNullOrEmpty(directory))
        {
            try
            {
                if (Directory.Exists(directory) &&
                    Directory.EnumerateFiles(directory, "*.*proj").Any())
                {
                    return directory;
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // A directory that cannot be read cannot be staged under; keep walking.
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // ReSharper disable once RedundantSuppressNullableWarningExpression
        var builder = new StringBuilder(value!.Length);
        foreach (var character in value)
        {
            builder.Append(Array.IndexOf(invalidFileNameChars, character) >= 0 ? '_' : character);
        }

        return builder.ToString();
    }

    static readonly char[] invalidFileNameChars = Path.GetInvalidFileNameChars();

    /// <summary>
    /// FNV-1a, for a name that is the same every run without carrying a whole path in it.
    /// </summary>
    static string Hash(string value)
    {
        var hash = 2166136261u;
        foreach (var character in value)
        {
            hash = (hash ^ character) * 16777619u;
        }

        return hash.ToString("x8");
    }
}
