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
            var baseName = BuildName(patch, origin);

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
    /// </summary>
    static string BuildName(InlinePatch patch, string? origin)
    {
        var test = Sanitize(patch.TestName) ?? Path.GetFileNameWithoutExtension(patch.SourceFile);
        var runtime = Sanitize(origin)?.Replace('.', '_') ?? "unknown";
        return $"{test}.{Hash($"{patch.SourceFile}:{patch.LineHint}")}.{runtime}";
    }

    /// <summary>
    /// The nearest directory above the source file holding a project file, whose <c>obj</c> is
    /// where a test run for that source would stage. Null when there is none, which is a source
    /// path from another machine or a file that has gone along with its project.
    /// </summary>
    static string? FindProjectDirectory(string sourceFile)
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
