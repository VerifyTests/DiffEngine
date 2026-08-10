/// <summary>
/// Locates the packages a Release build drops in <c>nugets</c>, and reads their entry lists with
/// everything that moves between builds normalised away.
/// </summary>
static class Packages
{
    static string Repository { get; } = FindRepository();

    /// <summary>
    /// The version this build produced, so the assertions ignore the older packages that
    /// accumulate in <c>nugets</c>.
    /// <para>
    /// Read out of the props file rather than off this assembly, because ProjectDefaults sets
    /// <c>GenerateAssemblyInfo=false</c> for every project here, so there is no
    /// <c>AssemblyInformationalVersionAttribute</c> to read.
    /// </para>
    /// </summary>
    public static string Version { get; } = ReadVersion();

    public static string NugetsDirectory { get; } = Path.Combine(Repository, "nugets");

    /// <summary>
    /// The package file names this build produced. Empty for a Debug build, which packs nothing.
    /// </summary>
    public static IReadOnlyList<string> Produced()
    {
        if (!Directory.Exists(NugetsDirectory))
        {
            return [];
        }

        return new DirectoryInfo(NugetsDirectory)
            .GetFiles($"*.{Version}.nupkg")
            // Normalised like the entry lists are, so a version bump is not a snapshot change.
            .Select(_ => Normalize(_.Name))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public static ZipArchive Open(string id)
    {
        var path = Path.Combine(NugetsDirectory, $"{id}.{Version}.nupkg");
        if (!File.Exists(path))
        {
            throw new($"{id}.{Version}.nupkg is missing from {NugetsDirectory}.");
        }

        return ZipFile.OpenRead(path);
    }

    public static IReadOnlyList<string> Entries(ZipArchive archive) =>
        archive.Entries
            .Select(_ => _.FullName)
            .Where(_ => !_.StartsWith(manifest, StringComparison.Ordinal))
            .Select(Normalize)
            .Order(StringComparer.Ordinal)
            .ToList();

    const string coreProperties = "package/services/metadata/core-properties/";

    /// <summary>
    /// The SBOM, which only exists on CI: DiffEngine references Microsoft.Sbom.Targets under a
    /// condition on the CI variable. Left out of the snapshots so one baseline describes both a
    /// local pack and a release, and asserted separately where it is actually produced.
    /// </summary>
    public const string manifest = "_manifest/";

    public static bool HasSbom(ZipArchive archive) =>
        archive.Entries.Any(_ => _.FullName.StartsWith(manifest, StringComparison.Ordinal));

    /// <summary>
    /// Whether this build was the one that generates an SBOM, matching the condition in
    /// DiffEngine.csproj rather than guessing at a CI provider.
    /// </summary>
    public static bool OnCi() =>
        Environment.GetEnvironmentVariable("CI") == "true";

    static string Normalize(string path)
    {
        // NuGet stamps a fresh guid into the core properties part name on every pack.
        if (path.StartsWith(coreProperties, StringComparison.Ordinal))
        {
            return $"{coreProperties}{{guid}}.psmdcp";
        }

        return path.Replace(Version, "{version}", StringComparison.Ordinal);
    }

    static string ReadVersion()
    {
        var path = Path.Combine(Repository, "src", "Directory.Build.props");
        var version = XDocument.Load(path)
            .Descendants("Version")
            .FirstOrDefault();
        if (version is null)
        {
            throw new($"No Version element in {path}.");
        }

        return version.Value;
    }

    static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "DiffEngine.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new($"Could not find the repository root above {AppContext.BaseDirectory}.");
    }
}
