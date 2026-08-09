namespace DiffEngine;

/// <summary>
/// Locates the copy of DiffEngineViewer bundled inside DiffEngine.nupkg, so inline snapshots work
/// with no extra install.
/// <para>
/// buildTransitive/DiffEngine.targets writes the package's tools/viewer path into the consuming
/// project's runtimeconfig as DiffEngine.ViewerDirectory. Only projects that produce a
/// runtimeconfig can carry it, which rules out net462 to net48; those fall back to the globally
/// installed dotnet tool.
/// </para>
/// </summary>
static class BundledViewerDirectory
{
    public const string Key = "DiffEngine.ViewerDirectory";

#if NET6_0_OR_GREATER
    public static string? Find()
    {
        if (AppContext.GetData(Key) is not string root ||
            root.Length == 0)
        {
            return null;
        }

        foreach (var rid in Rids())
        {
            var directory = Path.Combine(root, rid);
            if (Directory.Exists(directory))
            {
                return directory;
            }
        }

        return null;
    }

    static IEnumerable<string> Rids()
    {
        // The framework's own value first. On Alpine that is linux-musl-x64, a RID we do not
        // ship, so the probe misses and the caller falls through to the dotnet tool rather than
        // resolving a glibc build against musl.
        yield return RuntimeInformation.RuntimeIdentifier;

        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => null
        };

        if (architecture == null)
        {
            yield break;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return $"win-{architecture}";
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return $"osx-{architecture}";
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return $"linux-{architecture}";
        }
    }
#else
    /// <summary>
    /// .NET Framework consumers have no runtimeconfig to carry the path, so there is nothing to
    /// find and resolution falls through to the dotnet tool location.
    /// </summary>
    public static string? Find() =>
        null;
#endif
}
