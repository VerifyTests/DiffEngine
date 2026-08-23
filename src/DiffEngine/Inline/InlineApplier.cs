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
    static readonly ConcurrentDictionary<string, object> gates = new(StringComparer.OrdinalIgnoreCase);

    public static InlineApplyResult Apply(InlinePatch patch) =>
        Run(patch, write: true);

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

    static InlineApplyResult Run(InlinePatch patch, bool write)
    {
        if (string.IsNullOrWhiteSpace(patch.SourceFile))
        {
            return InlineApplyResult.Failed("InlinePatch.SourceFile is empty");
        }

        if (patch.LineHint < 1)
        {
            return InlineApplyResult.Failed($"InlinePatch.LineHint must be 1 or greater. Value: {patch.LineHint}");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(patch.SourceFile);
        }
        catch (Exception exception)
        {
            return InlineApplyResult.Failed($"Invalid InlinePatch.SourceFile: {patch.SourceFile}", exception);
        }

        // Followed before anything else, so the lock, the mutex, the read and the swap all name
        // the file that actually holds the source
        fullPath = ResolveLink(fullPath);

        var newContent = SourceLanguage.NormalizeNewlines(patch.NewContent);
        var normalizedPath = fullPath.ToLowerInvariant();
        lock (gates.GetOrAdd(normalizedPath, static _ => new()))
        {
            using var mutex = new Mutex(false, MutexName(normalizedPath));
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
                    return InlineApplyResult.Failed($"Timed out waiting for the inline patch mutex for: {fullPath}");
                }

                return LockedApply(fullPath, patch, newContent, write);
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

    static InlineApplyResult LockedApply(string fullPath, InlinePatch patch, string newContent, bool write)
    {
        // Asked here rather than before the lock, because the swap at the end of this method takes
        // the path away for the instant it takes to rename over it. Asked outside, an applier
        // waiting its turn on a file another one was finishing with saw the file as missing and
        // reported it, which is neither true nor the sort of thing a retry was going to fix
        if (!File.Exists(fullPath))
        {
            return InlineApplyResult.Failed($"Source file does not exist: {fullPath}");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception exception)
        {
            return InlineApplyResult.Failed($"Failed to read: {fullPath}", exception);
        }

        var (encoding, bomLength) = DetectEncoding(bytes);
        string source;
        try
        {
            source = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        }
        catch (DecoderFallbackException exception)
        {
            return InlineApplyResult.Failed(
                $"Could not decode as {encoding.WebName}: {fullPath}. Every byte that failed to decode would be replaced on write, so the file is left alone. Convert it to UTF-8 and re-run the test.",
                exception);
        }
        catch (Exception exception)
        {
            return InlineApplyResult.Failed($"Failed to decode: {fullPath}", exception);
        }

        var status = InlinePatcher.TryApply(
            SourceLanguage.ForFile(fullPath),
            source,
            patch.LineHint,
            patch.Mode,
            patch.OriginalExpression,
            patch.OriginalValue,
            patch.MemberName,
            newContent,
            out var newSource,
            out var failReason);

        switch (status)
        {
            case PatchStatus.AlreadyApplied:
                return InlineApplyResult.AlreadyApplied;
            case PatchStatus.NotFound:
                return InlineApplyResult.NotFound(failReason);
        }

        // Every reason a patch can be refused for has been asked by this point and none of them
        // held. All that remains is the write, which is the one step a dry run may not take
        if (!write)
        {
            return InlineApplyResult.Applied;
        }

        try
        {
            var content = encoding.GetBytes(newSource);
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

            WriteThroughTemporary(fullPath, output);
        }
        catch (Exception exception)
        {
            return InlineApplyResult.Failed($"Failed to write: {fullPath}", exception);
        }

        return InlineApplyResult.Applied;
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
        return path;
#endif
    }

    /// <summary>
    /// The destination's Unix permissions onto the temporary, because the swap is a rename and the
    /// file that survives it is the temporary - created with this process's umask. A source file
    /// that was executable, or group writable, or anything else out of the ordinary, came back as
    /// whatever the umask happened to say. Windows keeps the destination's ACLs across a Replace,
    /// so there is nothing to carry there.
    /// </summary>
    static void CopyMode(string destination, string temporary)
    {
#if NET7_0_OR_GREATER
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
#endif
    }

    static void WriteThroughTemporary(string fullPath, byte[] output)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        // Named after the file it replaces, so anything left by a process that died between the
        // write and the swap says what it was for. The extension keeps it out of a *.cs glob
        var temporary = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, output);
            CopyMode(fullPath, temporary);
            File.Replace(temporary, fullPath, null);
        }
        finally
        {
            // Replace consumed it. Anything still there is this method's litter, and failing an
            // applied patch over a temporary file that could not be deleted helps nobody
            try
            {
                if (File.Exists(temporary))
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

    static string MutexName(string normalizedPath)
    {
#if NET6_0_OR_GREATER
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
#else
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
#endif
        var builder = new StringBuilder("DiffEngineInline_");
        foreach (var b in hash)
        {
            builder.Append(b.ToString("X2"));
        }

        return builder.ToString();
    }
}
