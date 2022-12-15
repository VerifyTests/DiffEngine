static class OsSettingsResolver
{
    public static bool Resolve(
        OsSupport osSupport,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out LaunchArguments? launchArguments)
    {
        if (TryResolveForOs(osSupport.Windows, out path, out launchArguments, OSPlatform.Windows))
        {
            return true;
        }

        if (TryResolveForOs(osSupport.Linux, out path, out launchArguments, OSPlatform.Linux))
        {
            return true;
        }

        if (TryResolveForOs(osSupport.Osx, out path, out launchArguments, OSPlatform.OSX))
        {
            return true;
        }

        path = null;
        launchArguments = null;
        return false;
    }

    static bool TryResolveForOs(
        OsSettings? os,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out LaunchArguments? launchArguments,
        OSPlatform platform)
    {
        if (os != null &&
            RuntimeInformation.IsOSPlatform(platform))
        {
            if (ExeFinder.TryFindExe(os.ExeName, os.SearchDirectories, out path))
            {
                launchArguments = os.LaunchArguments;
                return true;
            }
        }

        launchArguments = null;
        path = null;
        return false;
    }

}