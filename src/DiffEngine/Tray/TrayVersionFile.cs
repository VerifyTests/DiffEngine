/// <summary>
/// A marker file written by DiffEngineTray on startup so client libraries can
/// detect the running tray's version. Old trays (pre 20.0.0) never write it.
/// </summary>
static class TrayVersionFile
{
    public static string FilePath { get; } =
        Path.Combine(Path.GetTempPath(), "DiffEngineTray", "version.txt");

    public static void Write(string informationalVersion)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, StripSuffix(informationalVersion));
    }

    public static void Delete()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch
        {
            // Best effort
        }
    }

    public static bool TryRead([NotNullWhen(true)] out Version? version)
    {
        version = null;
        try
        {
            if (!File.Exists(FilePath))
            {
                return false;
            }

            var text = StripSuffix(File.ReadAllText(FilePath).Trim());
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
