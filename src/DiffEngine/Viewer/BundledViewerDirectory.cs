namespace DiffEngine;

/// <summary>
/// Locates the copy of DiffEngineViewer bundled inside DiffEngine.nupkg, so inline snapshots work
/// with no extra install.
/// <para>
/// buildTransitive/DiffEngine.targets publishes the package's tools/viewer path to the consuming
/// project. Projects that produce a runtimeconfig carry it there, and .NET Framework projects,
/// which do not, carry it as an assembly level <see cref="AssemblyMetadataAttribute" /> instead.
/// </para>
/// </summary>
static class BundledViewerDirectory
{
    public const string Key = "DiffEngine.ViewerDirectory";

    public static string? Find()
    {
        var root = FindRoot();
        if (root == null ||
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

    /// <summary>
    /// The first candidate naming a directory that is on this machine.
    /// <para>
    /// A stamped path is only as good as the machine it was stamped on, so one that is not here
    /// is passed over rather than taken and then found wanting.
    /// </para>
    /// </summary>
    internal static string? FirstUsable(IEnumerable<string?> roots)
    {
        foreach (var root in roots)
        {
            if (root is {Length: > 0} &&
                Directory.Exists(root))
            {
                return root;
            }
        }

        return null;
    }

#if NET6_0_OR_GREATER
    static string? FindRoot() =>
        FirstUsable([AppContext.GetData(Key) as string]);
#else
    /// <summary>
    /// .NET Framework has no runtimeconfig to carry the path, so it is read from the metadata
    /// attribute the targets add to the consuming assembly.
    /// <para>
    /// Every stamped assembly carries one, prebuilt dependencies included: a Verify.dll from
    /// NuGet holds the package path of the machine that built it, which on this one is not there.
    /// So the entry assembly is asked first, and the rest are still asked after it.
    /// </para>
    /// </summary>
    static string? FindRoot() =>
        FirstUsable(Roots());

    static IEnumerable<string?> Roots()
    {
        var entry = Assembly.GetEntryAssembly();
        if (entry != null)
        {
            yield return ReadRoot(entry);
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic ||
                assembly == entry)
            {
                continue;
            }

            yield return ReadRoot(assembly);
        }
    }

    static string? ReadRoot(Assembly assembly)
    {
        try
        {
            foreach (var attribute in assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
            {
                var metadata = (AssemblyMetadataAttribute) attribute;
                if (metadata.Key == Key)
                {
                    return metadata.Value;
                }
            }
        }
        catch
        {
            // A reflection only or otherwise unreadable assembly cannot carry the path
        }

        return null;
    }
#endif

    static IEnumerable<string> Rids() =>
#if NET6_0_OR_GREATER
        Rids(RuntimeInformation.RuntimeIdentifier);

    internal static IEnumerable<string> Rids(string runtimeIdentifier)
    {
        // The framework's own value first. On Alpine that is linux-musl-x64, a RID we do not
        // ship, so the probe misses and the caller falls through to the dotnet tool rather than
        // resolving a glibc build against musl.
        yield return runtimeIdentifier;

        // And nothing else, or the synthesised RID below undoes that: linux-{arch} is the glibc
        // build, so on musl the probe would hit after all and hand back an apphost that cannot
        // start. Falling through to the dotnet tool is the outcome the comment above describes and
        // was not what happened
        if (runtimeIdentifier.Contains("-musl-", StringComparison.Ordinal))
        {
            yield break;
        }
#else
        InnerRids();

    static IEnumerable<string> InnerRids()
    {
#endif

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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return $"win-{architecture}";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return $"osx-{architecture}";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            yield return $"linux-{architecture}";
        }
    }
}
