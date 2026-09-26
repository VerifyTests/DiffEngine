namespace DiffEngine;

/// <summary>
/// Copies of the viewer that are usually on the machine without anyone installing one: beside an
/// installed DiffEngineTray, and inside DiffEngine's own package in the NuGet cache.
/// <para>
/// The bundled lookup depends on buildTransitive/DiffEngine.targets having stamped the package
/// path into the consuming project, which does not happen for every project shape, so the cache is
/// searched as well. The cache's copy of this library's own version is tried before any other
/// version, which is as close to version matched as a fallback gets.
/// </para>
/// <para>
/// Written with environment variables rather than resolved, like every other search directory, so
/// the generated docs describe any machine rather than the one they were generated on.
/// </para>
/// </summary>
static class FallbackViewerDirectories
{
    /// <summary>
    /// The tray is a dotnet tool, and ships a viewer for itself under its package in the store.
    /// Windows only, as the tray is.
    /// </summary>
    public static IEnumerable<string> Tray()
    {
        var rid = Rid("win");
        if (rid == null)
        {
            yield break;
        }

        yield return $@"%USERPROFILE%\.dotnet\tools\.store\diffenginetray\*\diffenginetray\*\tools\*\any\viewer\{rid}\";
    }

    public static IEnumerable<string> Windows()
    {
        var rid = Rid("win");
        if (rid == null)
        {
            yield break;
        }

        foreach (var root in NuGetRoots(@"%USERPROFILE%\.nuget\packages"))
        {
            foreach (var directory in NuGet(root, rid, '\\'))
            {
                yield return directory;
            }
        }
    }

    public static IEnumerable<string> Osx() =>
        Unix("osx");

    /// <summary>
    /// Nothing on musl: the cache only holds the glibc build, whose apphost cannot start there.
    /// </summary>
    public static IEnumerable<string> Linux()
    {
        if (BundledViewerDirectory.IsMusl())
        {
            return [];
        }

        return Unix("linux");
    }

    static IEnumerable<string> Unix(string os)
    {
        var rid = Rid(os);
        if (rid == null)
        {
            yield break;
        }

        foreach (var root in NuGetRoots("%HOME%/.nuget/packages"))
        {
            foreach (var directory in NuGet(root, rid, '/'))
            {
                yield return directory;
            }
        }
    }

    /// <summary>
    /// <c>NUGET_PACKAGES</c> first, since when it is set that is where restore put the package. When
    /// it is not, it stays unexpanded, names nothing, and is passed over.
    /// </summary>
    static IEnumerable<string> NuGetRoots(string defaultRoot) =>
    [
        "%NUGET_PACKAGES%",
        defaultRoot
    ];

    static IEnumerable<string> NuGet(string root, string rid, char separator)
    {
        var version = LibraryVersion();
        if (version != null)
        {
            yield return $"{root}{separator}diffengine{separator}{version}{separator}tools{separator}viewer{separator}{rid}{separator}";
        }

        yield return $"{root}{separator}diffengine{separator}*{separator}tools{separator}viewer{separator}{rid}{separator}";
    }

    static string? Rid(string os)
    {
        var architecture = BundledViewerDirectory.Architecture();
        if (architecture == null)
        {
            return null;
        }

        return $"{os}-{architecture}";
    }

    /// <summary>
    /// This library's package version, as the NuGet cache names its folder: lower case, with the
    /// source revision the SDK appends after a '+' dropped.
    /// </summary>
    internal static string? LibraryVersion()
    {
        var attribute = typeof(FallbackViewerDirectories).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = attribute?.InformationalVersion;
        if (version is not {Length: > 0})
        {
            return null;
        }

        var plus = version.IndexOf('+');
        if (plus >= 0)
        {
            version = version.Substring(0, plus);
        }

        return version.ToLowerInvariant();
    }
}
