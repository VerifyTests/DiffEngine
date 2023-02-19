namespace DiffEngine;

public record OsSettings(string? EnvironmentVariable, string ExeName, LaunchArguments LaunchArguments, params string[] SearchDirectories)
{
    public OsSettings(string ExeName, LaunchArguments LaunchArguments, params string[] SearchDirectories) :
        this(null, ExeName, LaunchArguments, SearchDirectories)
    {
    }

    public OsSettings(string? environmentVariable, string exeName, LaunchArguments launchArguments, string searchDirectory) :
        this(environmentVariable, exeName, launchArguments, new[] { searchDirectory })
    {
    }

    public OsSettings(string exeName, LaunchArguments launchArguments, string searchDirectory) :
        this(null, exeName, launchArguments, new[] { searchDirectory })
    {
    }

    public OsSettings(string exeName, LaunchArguments launchArguments) :
        this(null, exeName, launchArguments, Array.Empty<string>())
    {
    }
}