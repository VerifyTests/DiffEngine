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

    public static InlineApplyResult Apply(InlinePatch patch)
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

                return LockedApply(fullPath, patch, newContent);
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

    static InlineApplyResult LockedApply(string fullPath, InlinePatch patch, string newContent)
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
    static void WriteThroughTemporary(string fullPath, byte[] output)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        // Named after the file it replaces, so anything left by a process that died between the
        // write and the swap says what it was for. The extension keeps it out of a *.cs glob
        var temporary = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, output);
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
