using System.Security.Cryptography;

namespace DiffEngine;

/// <summary>
/// Applies an <see cref="InlinePatch"/> to a C# source file, preserving the file's
/// encoding, BOM and line endings. Owns all locking (cross process and in process);
/// callers must not add their own.
/// </summary>
public static class InlineApplier
{
    static readonly ConcurrentDictionary<string, object> gates = new(StringComparer.OrdinalIgnoreCase);

    public static Task<InlineApplyResult> ApplyAsync(InlinePatch patch, Cancel cancel = default) =>
        Task.Run(() => Apply(patch), cancel);

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

        if (!File.Exists(fullPath))
        {
            return InlineApplyResult.Failed($"Source file does not exist: {fullPath}");
        }

        var newContent = CsStringLiteral.NormalizeNewlines(patch.NewContent);
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
        catch (Exception exception)
        {
            return InlineApplyResult.Failed($"Failed to decode: {fullPath}", exception);
        }

        var status = InlinePatcher.TryApply(
            source,
            patch.LineHint,
            patch.Mode,
            patch.OriginalExpression,
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

            File.WriteAllBytes(fullPath, output);
        }
        catch (Exception exception)
        {
            return InlineApplyResult.Failed($"Failed to write: {fullPath}", exception);
        }

        return InlineApplyResult.Applied;
    }

    static (Encoding encoding, int bomLength) DetectEncoding(byte[] bytes)
    {
        if (bytes is [0xFF, 0xFE, 0x00, 0x00, ..])
        {
            return (new UTF32Encoding(false, true), 4);
        }

        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            return (new UTF8Encoding(true), 3);
        }

        if (bytes is [0xFF, 0xFE, ..])
        {
            return (new UnicodeEncoding(false, true), 2);
        }

        if (bytes is [0xFE, 0xFF, ..])
        {
            return (new UnicodeEncoding(true, true), 2);
        }

        return (new UTF8Encoding(false), 0);
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
