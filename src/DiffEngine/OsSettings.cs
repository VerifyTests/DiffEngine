namespace DiffEngine;

public record OsSettings(string ExeName, LaunchArguments LaunchArguments, params string[] SearchDirectories)
{
    public OsSettings(string exeName, LaunchArguments launchArguments, string searchDirectory) :
        this(exeName, launchArguments, new[] {searchDirectory})
    {
    }

    public OsSettings(string exeName, LaunchArguments launchArguments) :
        this(exeName, launchArguments, Array.Empty<string>())
    {
    }
}