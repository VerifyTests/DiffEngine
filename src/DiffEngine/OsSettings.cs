public record OsSettings(string ExeName, string PathCommandName, LaunchArguments LaunchArguments, params string[] SearchDirectories)
{
    public OsSettings(string exeName, string pathCommandName, LaunchArguments launchArguments, string searchDirectory) :
        this(
            exeName,
            pathCommandName,
            launchArguments,
            [searchDirectory])
    {
    }

    public OsSettings(string exeName, LaunchArguments launchArguments, params string[] searchDirectories) :
        this(exeName, exeName, launchArguments, searchDirectories)
    {
    }

    public OsSettings(string exeName, LaunchArguments launchArguments, string searchDirectory) :
        this(
            exeName,
            exeName,
            launchArguments,
            [searchDirectory])
    {
    }

    public OsSettings(string exeName, string pathCommandName, LaunchArguments launchArguments) :
        this(exeName, pathCommandName, launchArguments, Array.Empty<string>())
    {
    }

    public OsSettings(string exeName, LaunchArguments launchArguments) :
        this(exeName, exeName, launchArguments, Array.Empty<string>())
    {
    }
}