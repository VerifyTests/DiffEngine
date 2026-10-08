namespace DiffEngine;

/// <summary>
/// A marker file written by DiffEngineTray on startup so client libraries can
/// detect the running tray's version. Old trays (pre 20.0.0) never write it.
/// </summary>
static class TrayVersionFile
{
    public static string FilePath { get; } =
        Path.Combine(Path.GetTempPath(), "DiffEngineTray", "version.txt");

    public static void Write(string informationalVersion) =>
        Write(FilePath, informationalVersion);

    public static void Delete() =>
        Delete(FilePath);

    public static bool TryRead([NotNullWhen(true)] out Version? version) =>
        TryRead(FilePath, out version);

    // The three below take the file, for the tests: the marker is one file for the whole machine,
    // and a test that wrote and deleted it removed the marker of the tray running there.

    internal static void Write(string path, string informationalVersion)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, StripSuffix(informationalVersion));
    }

    internal static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort
        }
    }

    internal static bool TryRead(string path, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var text = StripSuffix(File.ReadAllText(path).Trim());
            return Version.TryParse(text, out version);
        }
        catch
        {
            return false;
        }
    }

    static string StripSuffix(string version)
    {
        var index = version.IndexOfAny(['-', '+']);
        return index < 0 ? version : version.Substring(0, index);
    }
}
