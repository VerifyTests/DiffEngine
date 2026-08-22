using Microsoft.Win32;

public class Startup
{
    /// <summary>
    /// Registers this executable, wherever it actually is.
    /// <para>
    /// The path used to be guessed as <c>%USERPROFILE%\.dotnet\tools\DiffEngineTray.exe</c>, which
    /// is only right for a default global install. A <c>--tool-path</c> install, a
    /// <c>DOTNET_CLI_HOME</c> that moves the tools directory, or simply running a local build all
    /// registered a path with nothing at it - so the tray never started at login, while the
    /// Options checkbox, which reads the settings file rather than the registry, went on reporting
    /// that it would.
    /// </para>
    /// <para>
    /// Quoted, because a Run value is a command line: an unquoted path containing a space is read
    /// as a program name followed by arguments.
    /// </para>
    /// </summary>
    public static void Add()
    {
        var exePath = Environment.ProcessPath;
        if (exePath == null)
        {
            // No host path to register, which a normal launch does not produce
            return;
        }

        using var key = GetRunKey();
        key.SetValue("DiffEngineTray", $"\"{exePath}\"");
    }

    public static void Remove()
    {
        using var key = GetRunKey();
        key.DeleteValue("DiffEngineTray", false);
    }

    public static bool Exists()
    {
        using var key = GetRunKey();
        return key.GetValue("DiffEngineTray") != null;
    }

    static RegistryKey GetRunKey() =>
        Registry.CurrentUser
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)!;
}
